#!/usr/bin/env python3
"""
WebSocket Client for NEXUS AI Trading Dashboard
==============================================

This module handles real-time communication between the local trading system
and the online dashboard through WebSocket connections.

Features:
- Automatic reconnection with exponential backoff
- Data compression and batching
- Connection health monitoring
- Secure authentication
- Error handling and logging

Author: NEXUS AI Trading System
Date: 2025
"""

import asyncio
import json
import time
import ssl
from datetime import datetime
from typing import Dict, Any, Optional, Callable
from dataclasses import dataclass, asdict
import websockets
from loguru import logger
import gzip
import base64


@dataclass
class DashboardMessage:
    """Structure for dashboard messages"""
    message_type: str
    timestamp: str
    data: Dict[str, Any]
    system_id: str
    sequence_id: int = 0


class WebSocketDashboardClient:
    """
    WebSocket client for sending trading data to online dashboard
    """
    
    def __init__(self, config: Dict[str, Any]):
        self.config = config
        self.websocket = None
        self.is_connected = False
        self.is_running = False
        self.reconnect_attempts = 0
        self.max_reconnect_attempts = config.get('max_reconnect_attempts', 10)
        self.reconnect_delay = config.get('reconnect_delay', 5)
        self.max_reconnect_delay = config.get('max_reconnect_delay', 300)
        self.sequence_id = 0
        
        # Connection settings
        self.dashboard_url = config['dashboard_url']
        self.api_key = config.get('api_key', '')
        self.system_id = config.get('system_id', 'nexus_ai_local')
        self.compress_data = config.get('compress_data', True)
        self.batch_size = config.get('batch_size', 10)
        self.send_interval = config.get('send_interval', 2.0)  # seconds
        
        # Data queue for batching
        self.message_queue = asyncio.Queue()
        self.send_task = None
        self.heartbeat_task = None
        
        # Statistics
        self.stats = {
            'messages_sent': 0,
            'messages_failed': 0,
            'bytes_sent': 0,
            'connection_uptime': 0,
            'last_heartbeat': None,
            'reconnections': 0
        }
        
        logger.info(f"WebSocket Dashboard Client initialized for {self.dashboard_url}")
    
    async def start(self) -> bool:
        """Start the WebSocket client"""
        if self.is_running:
            logger.warning("WebSocket client is already running")
            return True
        
        logger.info("Starting WebSocket Dashboard Client...")
        self.is_running = True
        
        try:
            # Start connection and background tasks
            connection_task = asyncio.create_task(self._connection_manager())
            self.send_task = asyncio.create_task(self._send_worker())
            self.heartbeat_task = asyncio.create_task(self._heartbeat_worker())
            
            # Wait for initial connection
            await asyncio.sleep(2)
            
            if self.is_connected:
                logger.info("WebSocket Dashboard Client started successfully")
                return True
            else:
                logger.warning("WebSocket client started but not yet connected")
                return False
                
        except Exception as e:
            logger.error(f"Failed to start WebSocket client: {e}")
            return False
    
    async def stop(self):
        """Stop the WebSocket client"""
        logger.info("Stopping WebSocket Dashboard Client...")
        self.is_running = False
        
        # Cancel background tasks
        if self.send_task:
            self.send_task.cancel()
        if self.heartbeat_task:
            self.heartbeat_task.cancel()
        
        # Close WebSocket connection
        if self.websocket:
            await self.websocket.close()
            self.websocket = None
            self.is_connected = False
        
        logger.info("WebSocket Dashboard Client stopped")
    
    async def _connection_manager(self):
        """Manage WebSocket connection with auto-reconnect"""
        while self.is_running:
            try:
                await self._connect()
                if self.is_connected:
                    self.reconnect_attempts = 0
                    await self._listen_for_messages()
            except Exception as e:
                logger.error(f"Connection error: {e}")
                self.is_connected = False
                
                if self.is_running:
                    await self._handle_reconnect()
            
            await asyncio.sleep(1)
    
    async def _connect(self):
        """Establish WebSocket connection"""
        try:
            # Prepare connection headers
            headers = {
                'Authorization': f'Bearer {self.api_key}',
                'X-System-ID': self.system_id,
                'X-Client-Type': 'nexus_ai_trading'
            }
            
            # SSL context for secure connections
            ssl_context = None
            if self.dashboard_url.startswith('wss://'):
                ssl_context = ssl.create_default_context()
                ssl_context.check_hostname = False
                ssl_context.verify_mode = ssl.CERT_NONE
            
            logger.info(f"Connecting to dashboard: {self.dashboard_url}")
            
            # Establish connection (compatible with different websockets versions)
            try:
                # Try with extra_headers first (newer versions)
                self.websocket = await websockets.connect(
                    self.dashboard_url,
                    extra_headers=headers,
                    ssl=ssl_context,
                    ping_interval=30,
                    ping_timeout=10,
                    close_timeout=10
                )
            except TypeError:
                # Fallback for older versions without extra_headers support
                logger.info("Using websockets without extra_headers (older version)")
                self.websocket = await websockets.connect(
                    self.dashboard_url,
                    ssl=ssl_context,
                    ping_interval=30,
                    ping_timeout=10,
                    close_timeout=10
                )
            
            self.is_connected = True
            self.stats['reconnections'] += 1
            logger.info("Successfully connected to dashboard")
            
            # Send initial handshake
            await self._send_handshake()
            
        except Exception as e:
            logger.error(f"Failed to connect to dashboard: {e}")
            self.is_connected = False
            raise
    
    async def _send_handshake(self):
        """Send initial handshake message"""
        handshake_data = {
            'system_info': {
                'system_id': self.system_id,
                'version': '1.0.0',
                'timestamp': datetime.now().isoformat(),
                'capabilities': ['real_time_data', 'compressed_data', 'batch_updates']
            },
            'config': {
                'send_interval': self.send_interval,
                'batch_size': self.batch_size,
                'compression': self.compress_data
            }
        }
        
        message = DashboardMessage(
            message_type='handshake',
            timestamp=datetime.now().isoformat(),
            data=handshake_data,
            system_id=self.system_id
        )
        
        await self._send_raw_message(message)
        logger.info("Handshake sent to dashboard")
    
    async def _listen_for_messages(self):
        """Listen for incoming messages from dashboard"""
        try:
            async for message in self.websocket:
                try:
                    data = json.loads(message)
                    await self._handle_incoming_message(data)
                except json.JSONDecodeError:
                    logger.warning(f"Received invalid JSON: {message}")
                except Exception as e:
                    logger.error(f"Error handling incoming message: {e}")
        except websockets.exceptions.ConnectionClosed:
            logger.info("Dashboard connection closed")
            self.is_connected = False
        except Exception as e:
            logger.error(f"Error listening for messages: {e}")
            self.is_connected = False
    
    async def _handle_incoming_message(self, data: Dict[str, Any]):
        """Handle incoming messages from dashboard"""
        message_type = data.get('type', 'unknown')
        
        if message_type == 'ping':
            await self._send_pong()
        elif message_type == 'config_update':
            await self._handle_config_update(data.get('config', {}))
        elif message_type == 'command':
            await self._handle_command(data.get('command', {}))
        else:
            logger.debug(f"Received message type: {message_type}")
    
    async def _send_pong(self):
        """Send pong response"""
        pong_message = DashboardMessage(
            message_type='pong',
            timestamp=datetime.now().isoformat(),
            data={'status': 'ok'},
            system_id=self.system_id
        )
        await self._send_raw_message(pong_message)
    
    async def _handle_config_update(self, config: Dict[str, Any]):
        """Handle configuration updates from dashboard"""
        logger.info(f"Received config update: {config}")
        # Update local configuration as needed
        for key, value in config.items():
            if key in self.config:
                self.config[key] = value
                logger.info(f"Updated config: {key} = {value}")
    
    async def _handle_command(self, command: Dict[str, Any]):
        """Handle commands from dashboard"""
        cmd_type = command.get('type')
        logger.info(f"Received command: {cmd_type}")
        
        if cmd_type == 'get_status':
            await self.send_system_status()
        elif cmd_type == 'restart':
            logger.info("Restart command received from dashboard")
            # Could trigger system restart if needed
    
    async def _handle_reconnect(self):
        """Handle reconnection logic with exponential backoff"""
        if self.reconnect_attempts < self.max_reconnect_attempts:
            self.reconnect_attempts += 1
            delay = min(
                self.reconnect_delay * (2 ** (self.reconnect_attempts - 1)),
                self.max_reconnect_delay
            )
            
            logger.info(f"Reconnecting in {delay}s (attempt {self.reconnect_attempts}/{self.max_reconnect_attempts})")
            await asyncio.sleep(delay)
        else:
            logger.error(f"Max reconnection attempts reached ({self.max_reconnect_attempts})")
            await asyncio.sleep(self.max_reconnect_delay)
            self.reconnect_attempts = 0  # Reset for next cycle
    
    async def _send_worker(self):
        """Background worker to send queued messages in batches"""
        while self.is_running:
            try:
                if self.is_connected and not self.message_queue.empty():
                    # Collect messages for batch
                    messages = []
                    for _ in range(self.batch_size):
                        try:
                            message = self.message_queue.get_nowait()
                            messages.append(message)
                        except asyncio.QueueEmpty:
                            break
                    
                    if messages:
                        if len(messages) == 1:
                            # Send single message
                            await self._send_raw_message(messages[0])
                        else:
                            # Send batch
                            await self._send_batch(messages)
                
                await asyncio.sleep(self.send_interval)
                
            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Error in send worker: {e}")
                await asyncio.sleep(1)
    
    async def _send_batch(self, messages: list):
        """Send multiple messages in a batch"""
        batch_message = DashboardMessage(
            message_type='batch',
            timestamp=datetime.now().isoformat(),
            data={'messages': [asdict(msg) for msg in messages]},
            system_id=self.system_id,
            sequence_id=self._get_next_sequence_id()
        )
        
        await self._send_raw_message(batch_message)
        logger.debug(f"Sent batch of {len(messages)} messages")
    
    async def _send_raw_message(self, message: DashboardMessage):
        """Send raw message through WebSocket"""
        if not self.is_connected or not self.websocket:
            return False
        
        try:
            # Convert to JSON
            message_json = json.dumps(asdict(message), default=str)
            
            # Compress if enabled
            if self.compress_data:
                compressed = gzip.compress(message_json.encode('utf-8'))
                encoded = base64.b64encode(compressed).decode('utf-8')
                final_message = json.dumps({
                    'compressed': True,
                    'data': encoded
                })
            else:
                final_message = message_json
            
            # Send message
            await self.websocket.send(final_message)
            
            # Update statistics
            self.stats['messages_sent'] += 1
            self.stats['bytes_sent'] += len(final_message)
            
            return True
            
        except Exception as e:
            logger.error(f"Failed to send message: {e}")
            self.stats['messages_failed'] += 1
            self.is_connected = False
            return False
    
    async def _heartbeat_worker(self):
        """Send periodic heartbeats"""
        while self.is_running:
            try:
                if self.is_connected:
                    await self.send_heartbeat()
                    self.stats['last_heartbeat'] = datetime.now()
                
                await asyncio.sleep(30)  # Send heartbeat every 30 seconds
                
            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Error in heartbeat worker: {e}")
                await asyncio.sleep(30)
    
    def _get_next_sequence_id(self) -> int:
        """Get next sequence ID for message ordering"""
        self.sequence_id += 1
        return self.sequence_id
    
    # Public methods for sending different types of data
    
    async def send_portfolio_data(self, portfolio_data: Dict[str, Any]):
        """Send portfolio data to dashboard"""
        message = DashboardMessage(
            message_type='portfolio_update',
            timestamp=datetime.now().isoformat(),
            data=portfolio_data,
            system_id=self.system_id,
            sequence_id=self._get_next_sequence_id()
        )
        await self.message_queue.put(message)
    
    async def send_trade_signal(self, signal_data: Dict[str, Any]):
        """Send trading signal to dashboard"""
        message = DashboardMessage(
            message_type='trade_signal',
            timestamp=datetime.now().isoformat(),
            data=signal_data,
            system_id=self.system_id,
            sequence_id=self._get_next_sequence_id()
        )
        await self.message_queue.put(message)
    
    async def send_risk_alert(self, risk_data: Dict[str, Any]):
        """Send risk alert to dashboard"""
        message = DashboardMessage(
            message_type='risk_alert',
            timestamp=datetime.now().isoformat(),
            data=risk_data,
            system_id=self.system_id,
            sequence_id=self._get_next_sequence_id()
        )
        await self.message_queue.put(message)
    
    async def send_system_status(self):
        """Send system status to dashboard"""
        status_data = {
            'status': 'running' if self.is_running else 'stopped',
            'connection': 'connected' if self.is_connected else 'disconnected',
            'stats': self.stats,
            'config': self.config
        }
        
        message = DashboardMessage(
            message_type='system_status',
            timestamp=datetime.now().isoformat(),
            data=status_data,
            system_id=self.system_id,
            sequence_id=self._get_next_sequence_id()
        )
        await self.message_queue.put(message)
    
    async def send_heartbeat(self):
        """Send heartbeat to dashboard"""
        heartbeat_data = {
            'timestamp': datetime.now().isoformat(),
            'uptime': time.time() - self.stats.get('start_time', time.time()),
            'queue_size': self.message_queue.qsize()
        }
        
        message = DashboardMessage(
            message_type='heartbeat',
            timestamp=datetime.now().isoformat(),
            data=heartbeat_data,
            system_id=self.system_id,
            sequence_id=self._get_next_sequence_id()
        )
        
        # Send heartbeat immediately, not queued
        await self._send_raw_message(message)
    
    def get_connection_status(self) -> Dict[str, Any]:
        """Get current connection status"""
        return {
            'connected': self.is_connected,
            'running': self.is_running,
            'reconnect_attempts': self.reconnect_attempts,
            'queue_size': self.message_queue.qsize(),
            'stats': self.stats.copy()
        }
