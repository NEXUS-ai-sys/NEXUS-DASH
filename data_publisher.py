#!/usr/bin/env python3
"""
Dashboard Data Publisher
========================

This module extracts and formats data from the trading system components
and publishes it to the dashboard through WebSocket connections.

Features:
- Real-time portfolio data publishing
- Trading signal broadcasting
- Risk metrics and alerts
- Model performance tracking
- System health monitoring

Author: NEXUS AI Trading System
Date: 2025
"""

import asyncio
import json
from datetime import datetime, timedelta
from typing import Dict, Any, Optional, List
from dataclasses import dataclass
from loguru import logger

from .websocket_client import WebSocketDashboardClient


@dataclass
class PublisherConfig:
    """Configuration for dashboard publisher"""
    update_interval: float = 2.0  # seconds
    portfolio_update_interval: float = 5.0  # seconds
    batch_signals: bool = True
    max_history_length: int = 1000
    enable_compression: bool = True


class DashboardDataPublisher:
    """
    Publishes trading system data to the dashboard in real-time
    """
    
    def __init__(self, websocket_client: WebSocketDashboardClient, config: PublisherConfig = None):
        self.websocket_client = websocket_client
        self.config = config or PublisherConfig()
        
        # Data tracking
        self.last_portfolio_update = None
        self.signal_cache = []
        self.risk_alerts = []
        self.performance_history = {}
        
        # Publishing tasks
        self.portfolio_task = None
        self.signal_task = None
        self.system_task = None
        
        self.is_running = False
        
        logger.info("Dashboard Data Publisher initialized")
    
    async def start(self):
        """Start the data publisher"""
        if self.is_running:
            logger.warning("Data publisher is already running")
            return
        
        logger.info("Starting Dashboard Data Publisher...")
        self.is_running = True
        
        # Start publishing tasks
        self.portfolio_task = asyncio.create_task(self._portfolio_publisher())
        self.system_task = asyncio.create_task(self._system_status_publisher())
        
        logger.info("Dashboard Data Publisher started successfully")
    
    async def stop(self):
        """Stop the data publisher"""
        logger.info("Stopping Dashboard Data Publisher...")
        self.is_running = False
        
        # Cancel tasks
        if self.portfolio_task:
            self.portfolio_task.cancel()
        if self.system_task:
            self.system_task.cancel()
        
        logger.info("Dashboard Data Publisher stopped")
    
    async def _portfolio_publisher(self):
        """Periodically publish portfolio data"""
        while self.is_running:
            try:
                await asyncio.sleep(self.config.portfolio_update_interval)
                # Portfolio data will be published when updated externally
                
            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Error in portfolio publisher: {e}")
                await asyncio.sleep(5)
    
    async def _system_status_publisher(self):
        """Periodically publish system status"""
        while self.is_running:
            try:
                await self.websocket_client.send_system_status()
                await asyncio.sleep(30)  # Send status every 30 seconds
                
            except asyncio.CancelledError:
                break
            except Exception as e:
                logger.error(f"Error in system status publisher: {e}")
                await asyncio.sleep(30)
    
    # Public methods for publishing different types of data
    
    async def publish_portfolio_data(self, trading_engine, risk_adjuster=None):
        """Publish portfolio data from trading engine"""
        try:
            # Get portfolio summary from trading engine
            portfolio_summary = trading_engine.get_portfolio_summary()
            
            # Get performance metrics
            performance_metrics = trading_engine.performance_tracker.get_performance_summary()
            
            # Format portfolio data for dashboard
            portfolio_data = {
                'timestamp': datetime.now().isoformat(),
                'portfolio': {
                    'total_value': portfolio_summary.get('total_portfolio_value', 0),
                    'cash': portfolio_summary.get('cash', 0),
                    'positions_value': portfolio_summary.get('positions_value', 0),
                    'unrealized_pnl': portfolio_summary.get('unrealized_pnl', 0),
                    'realized_pnl': portfolio_summary.get('realized_pnl', 0),
                    'total_pnl': portfolio_summary.get('total_pnl', 0),
                    'position_count': len(portfolio_summary.get('positions', {}))
                },
                'performance': {
                    'total_return': performance_metrics.get('total_return', 0),
                    'daily_return': performance_metrics.get('daily_return', 0),
                    'sharpe_ratio': performance_metrics.get('sharpe_ratio', 0),
                    'max_drawdown': performance_metrics.get('max_drawdown', 0),
                    'win_rate': performance_metrics.get('win_rate', 0),
                    'profit_factor': performance_metrics.get('profit_factor', 0)
                },
                'positions': self._format_positions(portfolio_summary.get('positions', {})),
                'recent_trades': self._format_recent_trades(trading_engine.get_recent_trades(10)),
                'equity_curve': self._get_equity_curve_data(trading_engine.performance_tracker)
            }
            
            # Add risk metrics if available
            if risk_adjuster:
                risk_metrics = await self._get_risk_metrics(risk_adjuster)
                portfolio_data['risk'] = risk_metrics
            
            await self.websocket_client.send_portfolio_data(portfolio_data)
            self.last_portfolio_update = datetime.now()
            
            logger.debug("Portfolio data published to dashboard")
            
        except Exception as e:
            logger.error(f"Error publishing portfolio data: {e}")
    
    async def publish_trade_signal(self, signal_data: Dict[str, Any], strategy_name: str = None):
        """Publish trading signal to dashboard"""
        try:
            formatted_signal = {
                'timestamp': datetime.now().isoformat(),
                'strategy': strategy_name or 'unknown',
                'signal': signal_data,
                'confidence': signal_data.get('confidence', 0),
                'action': signal_data.get('action', 'HOLD'),
                'symbol': signal_data.get('symbol', 'unknown'),
                'price': signal_data.get('price', 0),
                'quantity': signal_data.get('quantity', 0),
                'metadata': signal_data.get('metadata', {})
            }
            
            await self.websocket_client.send_trade_signal(formatted_signal)
            
            # Cache signal for batching if enabled
            if self.config.batch_signals:
                self.signal_cache.append(formatted_signal)
                self._cleanup_signal_cache()
            
            logger.debug(f"Trade signal published: {strategy_name} - {signal_data.get('action', 'HOLD')}")
            
        except Exception as e:
            logger.error(f"Error publishing trade signal: {e}")
    
    async def publish_risk_alert(self, alert_type: str, message: str, severity: str = 'medium', data: Dict[str, Any] = None):
        """Publish risk alert to dashboard"""
        try:
            risk_alert = {
                'timestamp': datetime.now().isoformat(),
                'type': alert_type,
                'message': message,
                'severity': severity.lower(),
                'data': data or {},
                'alert_id': f"{alert_type}_{int(datetime.now().timestamp())}"
            }
            
            await self.websocket_client.send_risk_alert(risk_alert)
            
            # Cache alert
            self.risk_alerts.append(risk_alert)
            self._cleanup_risk_alerts()
            
            logger.info(f"Risk alert published: {alert_type} - {severity}")
            
        except Exception as e:
            logger.error(f"Error publishing risk alert: {e}")
    
    async def publish_model_performance(self, model_name: str, performance_data: Dict[str, Any]):
        """Publish model performance data"""
        try:
            model_perf = {
                'timestamp': datetime.now().isoformat(),
                'model_name': model_name,
                'performance': performance_data,
                'metrics': {
                    'accuracy': performance_data.get('accuracy', 0),
                    'precision': performance_data.get('precision', 0),
                    'recall': performance_data.get('recall', 0),
                    'f1_score': performance_data.get('f1_score', 0),
                    'returns': performance_data.get('returns', 0),
                    'sharpe': performance_data.get('sharpe', 0)
                }
            }
            
            # Update performance history
            if model_name not in self.performance_history:
                self.performance_history[model_name] = []
            
            self.performance_history[model_name].append(model_perf)
            
            # Keep only recent history
            if len(self.performance_history[model_name]) > self.config.max_history_length:
                self.performance_history[model_name] = self.performance_history[model_name][-self.config.max_history_length:]
            
            # Send as part of system status or create dedicated message
            await self.websocket_client.send_portfolio_data({
                'model_performance': model_perf
            })
            
            logger.debug(f"Model performance published: {model_name}")
            
        except Exception as e:
            logger.error(f"Error publishing model performance: {e}")
    
    async def publish_system_metrics(self, system_metrics: Dict[str, Any]):
        """Publish general system metrics"""
        try:
            metrics_data = {
                'timestamp': datetime.now().isoformat(),
                'system': system_metrics,
                'connection_status': self.websocket_client.get_connection_status(),
                'publisher_stats': {
                    'last_portfolio_update': self.last_portfolio_update.isoformat() if self.last_portfolio_update else None,
                    'signals_cached': len(self.signal_cache),
                    'alerts_cached': len(self.risk_alerts),
                    'is_running': self.is_running
                }
            }
            
            await self.websocket_client.send_system_status()
            
        except Exception as e:
            logger.error(f"Error publishing system metrics: {e}")
    
    # Helper methods
    
    def _format_positions(self, positions: Dict[str, Any]) -> List[Dict[str, Any]]:
        """Format positions data for dashboard"""
        formatted_positions = []
        
        for symbol, position_data in positions.items():
            formatted_position = {
                'symbol': symbol,
                'quantity': position_data.get('quantity', 0),
                'avg_price': position_data.get('avg_price', 0),
                'current_price': position_data.get('current_price', 0),
                'market_value': position_data.get('market_value', 0),
                'unrealized_pnl': position_data.get('unrealized_pnl', 0),
                'unrealized_pnl_pct': position_data.get('unrealized_pnl_pct', 0),
                'side': position_data.get('side', 'long'),
                'entry_time': position_data.get('entry_time', ''),
                'last_update': datetime.now().isoformat()
            }
            formatted_positions.append(formatted_position)
        
        return formatted_positions
    
    def _format_recent_trades(self, recent_trades: List[Dict[str, Any]]) -> List[Dict[str, Any]]:
        """Format recent trades for dashboard"""
        formatted_trades = []
        
        for trade in recent_trades:
            formatted_trade = {
                'trade_id': trade.get('trade_id', ''),
                'symbol': trade.get('symbol', ''),
                'side': trade.get('side', ''),
                'quantity': trade.get('quantity', 0),
                'price': trade.get('price', 0),
                'value': trade.get('value', 0),
                'pnl': trade.get('pnl', 0),
                'commission': trade.get('commission', 0),
                'timestamp': trade.get('timestamp', ''),
                'strategy': trade.get('strategy', 'unknown')
            }
            formatted_trades.append(formatted_trade)
        
        return formatted_trades
    
    def _get_equity_curve_data(self, performance_tracker) -> List[Dict[str, Any]]:
        """Get equity curve data for charting"""
        try:
            equity_data = performance_tracker.get_equity_curve()
            
            formatted_data = []
            for point in equity_data[-100:]:  # Last 100 points
                formatted_data.append({
                    'timestamp': point.get('timestamp', ''),
                    'equity': point.get('equity', 0),
                    'drawdown': point.get('drawdown', 0)
                })
            
            return formatted_data
            
        except Exception as e:
            logger.error(f"Error getting equity curve data: {e}")
            return []
    
    async def _get_risk_metrics(self, risk_adjuster) -> Dict[str, Any]:
        """Get risk metrics from risk adjuster"""
        try:
            risk_metrics = {
                'var_95': 0,  # Value at Risk
                'expected_shortfall': 0,
                'position_concentration': 0,
                'leverage': 0,
                'correlation_risk': 0,
                'liquidity_risk': 0
            }
            
            # Get actual risk metrics if methods exist
            if hasattr(risk_adjuster, 'calculate_var'):
                risk_metrics['var_95'] = await risk_adjuster.calculate_var()
            
            if hasattr(risk_adjuster, 'get_portfolio_concentration'):
                risk_metrics['position_concentration'] = await risk_adjuster.get_portfolio_concentration()
            
            return risk_metrics
            
        except Exception as e:
            logger.error(f"Error getting risk metrics: {e}")
            return {}
    
    def _cleanup_signal_cache(self):
        """Clean up old signals from cache"""
        if len(self.signal_cache) > self.config.max_history_length:
            self.signal_cache = self.signal_cache[-self.config.max_history_length//2:]
    
    def _cleanup_risk_alerts(self):
        """Clean up old risk alerts"""
        # Keep only alerts from last 24 hours
        cutoff_time = datetime.now() - timedelta(hours=24)
        
        self.risk_alerts = [
            alert for alert in self.risk_alerts
            if datetime.fromisoformat(alert['timestamp'].replace('Z', '+00:00')) > cutoff_time
        ]
    
    def get_publisher_status(self) -> Dict[str, Any]:
        """Get current publisher status"""
        return {
            'is_running': self.is_running,
            'last_portfolio_update': self.last_portfolio_update.isoformat() if self.last_portfolio_update else None,
            'signals_cached': len(self.signal_cache),
            'alerts_cached': len(self.risk_alerts),
            'websocket_status': self.websocket_client.get_connection_status(),
            'config': {
                'update_interval': self.config.update_interval,
                'portfolio_update_interval': self.config.portfolio_update_interval,
                'batch_signals': self.config.batch_signals,
                'max_history_length': self.config.max_history_length
            }
        }
