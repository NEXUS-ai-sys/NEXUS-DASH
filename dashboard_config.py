#!/usr/bin/env python3
"""
Dashboard Configuration
=======================

Configuration settings for dashboard WebSocket connection and data publishing.

Author: NEXUS AI Trading System
Date: 2025
"""

from typing import Dict, Any, Optional
from dataclasses import dataclass


@dataclass
class DashboardConfig:
    """Configuration for dashboard connection and publishing"""
    
    # WebSocket connection settings
    dashboard_url: str = "wss://your-dashboard-domain.com/ws/trading"  # Replace with your dashboard URL
    api_key: str = ""  # Your dashboard API key
    system_id: str = "nexus_ai_local"
    
    # Connection behavior
    auto_connect: bool = True
    max_reconnect_attempts: int = 10
    reconnect_delay: float = 5.0
    max_reconnect_delay: float = 300.0
    connection_timeout: float = 30.0
    
    # Data publishing settings
    send_interval: float = 2.0  # seconds between batch sends
    portfolio_update_interval: float = 5.0  # seconds
    batch_size: int = 10  # messages per batch
    compress_data: bool = True
    max_history_length: int = 1000
    
    # Data types to publish
    publish_portfolio: bool = True
    publish_signals: bool = True
    publish_risk_alerts: bool = True
    publish_model_performance: bool = True
    publish_system_status: bool = True
    
    # Security settings
    enable_ssl: bool = True
    verify_ssl: bool = False  # Set to True in production
    
    # Logging
    log_connection_events: bool = True
    log_data_publishing: bool = False  # Set to True for debugging


def get_default_dashboard_config() -> Dict[str, Any]:
    """Get default dashboard configuration"""
    return {
        'dashboard': {
            'enabled': False,  # Set to True when you have dashboard URL
            'url': 'wss://your-dashboard-domain.com/ws/trading',
            'api_key': '',  # Add your API key here
            'system_id': 'nexus_ai_local',
            'auto_connect': True,
            'connection': {
                'max_reconnect_attempts': 10,
                'reconnect_delay': 5.0,
                'max_reconnect_delay': 300.0,
                'timeout': 30.0,
                'enable_ssl': True,
                'verify_ssl': False
            },
            'publishing': {
                'send_interval': 2.0,
                'portfolio_update_interval': 5.0,
                'batch_size': 10,
                'compress_data': True,
                'max_history_length': 1000
            },
            'data_types': {
                'portfolio': True,
                'signals': True,
                'risk_alerts': True,
                'model_performance': True,
                'system_status': True
            },
            'logging': {
                'connection_events': True,
                'data_publishing': False
            }
        }
    }


def load_dashboard_config_from_dict(config_dict: Dict[str, Any]) -> DashboardConfig:
    """Load dashboard configuration from dictionary"""
    dashboard_section = config_dict.get('dashboard', {})
    connection_section = dashboard_section.get('connection', {})
    publishing_section = dashboard_section.get('publishing', {})
    data_types_section = dashboard_section.get('data_types', {})
    logging_section = dashboard_section.get('logging', {})
    
    return DashboardConfig(
        # Basic connection
        dashboard_url=dashboard_section.get('url', 'wss://localhost:8080/ws/trading'),
        api_key=dashboard_section.get('api_key', ''),
        system_id=dashboard_section.get('system_id', 'nexus_ai_local'),
        auto_connect=dashboard_section.get('auto_connect', True),
        
        # Connection behavior
        max_reconnect_attempts=connection_section.get('max_reconnect_attempts', 10),
        reconnect_delay=connection_section.get('reconnect_delay', 5.0),
        max_reconnect_delay=connection_section.get('max_reconnect_delay', 300.0),
        connection_timeout=connection_section.get('timeout', 30.0),
        
        # Publishing settings
        send_interval=publishing_section.get('send_interval', 2.0),
        portfolio_update_interval=publishing_section.get('portfolio_update_interval', 5.0),
        batch_size=publishing_section.get('batch_size', 10),
        compress_data=publishing_section.get('compress_data', True),
        max_history_length=publishing_section.get('max_history_length', 1000),
        
        # Data types
        publish_portfolio=data_types_section.get('portfolio', True),
        publish_signals=data_types_section.get('signals', True),
        publish_risk_alerts=data_types_section.get('risk_alerts', True),
        publish_model_performance=data_types_section.get('model_performance', True),
        publish_system_status=data_types_section.get('system_status', True),
        
        # Security
        enable_ssl=connection_section.get('enable_ssl', True),
        verify_ssl=connection_section.get('verify_ssl', False),
        
        # Logging
        log_connection_events=logging_section.get('connection_events', True),
        log_data_publishing=logging_section.get('data_publishing', False)
    )


# Example configuration for different environments
DEVELOPMENT_CONFIG = {
    'dashboard': {
        'enabled': False,  # Disabled by default in development
        'url': 'ws://localhost:8080/ws/trading',  # Local development server
        'api_key': 'dev_key_123',
        'system_id': 'nexus_ai_dev',
        'connection': {
            'enable_ssl': False,
            'verify_ssl': False,
            'timeout': 10.0
        },
        'publishing': {
            'send_interval': 1.0,  # Faster updates for development
            'portfolio_update_interval': 2.0
        },
        'logging': {
            'connection_events': True,
            'data_publishing': True  # More verbose logging in dev
        }
    }
}


PRODUCTION_CONFIG = {
    'dashboard': {
        'enabled': True,
        'url': 'wss://nexus-dashboard.yourdomain.com/ws/trading',
        'api_key': 'your_production_api_key_here',
        'system_id': 'nexus_ai_prod',
        'connection': {
            'enable_ssl': True,
            'verify_ssl': True,  # Enable SSL verification in production
            'timeout': 30.0,
            'max_reconnect_attempts': 20  # More persistent in production
        },
        'publishing': {
            'send_interval': 3.0,  # Slightly slower to reduce server load
            'compress_data': True
        },
        'logging': {
            'connection_events': True,
            'data_publishing': False  # Less verbose in production
        }
    }
}


TESTING_CONFIG = {
    'dashboard': {
        'enabled': False,  # Usually disabled for unit tests
        'url': 'ws://localhost:8080/ws/testing',
        'api_key': 'test_key',
        'system_id': 'nexus_ai_test',
        'connection': {
            'max_reconnect_attempts': 3,  # Fail fast in tests
            'reconnect_delay': 1.0,
            'timeout': 5.0
        },
        'publishing': {
            'send_interval': 0.5,  # Very fast for testing
            'batch_size': 5
        }
    }
}


def get_config_for_environment(environment: str = 'development') -> Dict[str, Any]:
    """Get configuration for specific environment"""
    if environment.lower() == 'production':
        return PRODUCTION_CONFIG
    elif environment.lower() == 'testing':
        return TESTING_CONFIG
    else:
        return DEVELOPMENT_CONFIG
