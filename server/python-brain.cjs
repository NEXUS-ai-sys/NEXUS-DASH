const express = require('express');
const { spawn } = require('child_process');
const WebSocket = require('ws');
const cors = require('cors');
const fs = require('fs');
const path = require('path');

class PythonBrainService {
    constructor() {
        this.app = express();
        this.server = null;
        this.wss = null;
        this.port = 5000;
        
        // Python process management
        this.pythonProcess = null;
        this.pythonReady = false;
        this.pythonQueue = [];
        
        // Data distribution
        this.dataStreams = new Map();
        this.subscribers = new Map();
        this.analysisCache = new Map();
        
        // Connection tracking
        this.connectedClients = new Set();
        this.lastHeartbeat = null;
        
        this.setupMiddleware();
        this.setupRoutes();
        this.setupWebSocket();
        this.initializePythonBrain();
    }

    setupMiddleware() {
        this.app.use(cors({
            origin: ['http://localhost:3000', 'http://localhost:5173'],
            credentials: true
        }));
        this.app.use(express.json({ limit: '50mb' }));
        this.app.use(express.urlencoded({ extended: true }));
        
        // Request logging
        this.app.use((req, res, next) => {
            const timestamp = new Date().toISOString();
            console.log(`${timestamp} - Python Brain: ${req.method} ${req.path}`);
            next();
        });
    }

    setupRoutes() {
        // Health check
        this.app.get('/health', (req, res) => {
            res.json({
                status: 'healthy',
                pythonReady: this.pythonReady,
                connectedClients: this.connectedClients.size,
                activeStreams: this.dataStreams.size,
                lastHeartbeat: this.lastHeartbeat,
                analysisCache: this.analysisCache.size
            });
        });

        // Python brain analysis endpoints
        this.app.post('/api/analyze/market', (req, res) => {
            this.sendToPython('market_analysis', req.body, res);
        });

        this.app.post('/api/analyze/strategy', (req, res) => {
            this.sendToPython('strategy_analysis', req.body, res);
        });

        this.app.post('/api/analyze/risk', (req, res) => {
            this.sendToPython('risk_analysis', req.body, res);
        });

        this.app.post('/api/analyze/portfolio', (req, res) => {
            this.sendToPython('portfolio_optimization', req.body, res);
        });

        this.app.post('/api/predict/price', (req, res) => {
            this.sendToPython('price_prediction', req.body, res);
        });

        this.app.post('/api/detect/patterns', (req, res) => {
            this.sendToPython('pattern_detection', req.body, res);
        });

        this.app.post('/api/backtest/strategy', (req, res) => {
            this.sendToPython('strategy_backtest', req.body, res);
        });

        // Data distribution endpoints
        this.app.post('/api/data/distribute', (req, res) => {
            this.distributeData(req.body);
            res.json({ status: 'distributed', timestamp: new Date().toISOString() });
        });

        this.app.get('/api/data/streams', (req, res) => {
            res.json({
                streams: Array.from(this.dataStreams.keys()),
                subscribers: Array.from(this.subscribers.keys())
            });
        });

        // Strategy monitoring integration
        this.app.post('/api/strategy/monitor', (req, res) => {
            this.monitorStrategy(req.body, res);
        });

        // Real-time data processing
        this.app.post('/api/process/realtime', (req, res) => {
            this.processRealtimeData(req.body, res);
        });
    }

    setupWebSocket() {
        this.server = require('http').createServer(this.app);
        this.wss = new WebSocket.Server({ server: this.server });

        this.wss.on('connection', (ws, req) => {
            const clientId = `python_client_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
            this.connectedClients.add(clientId);
            
            console.log(`🐍 Python Brain client connected: ${clientId}`);

            // Send initial status
            ws.send(JSON.stringify({
                type: 'connection_status',
                clientId,
                pythonReady: this.pythonReady,
                availableAnalysis: [
                    'market_analysis',
                    'strategy_analysis', 
                    'risk_analysis',
                    'portfolio_optimization',
                    'price_prediction',
                    'pattern_detection',
                    'strategy_backtest'
                ]
            }));

            ws.on('message', (message) => {
                try {
                    const data = JSON.parse(message);
                    this.handleWebSocketMessage(ws, data, clientId);
                } catch (error) {
                    console.error('Invalid WebSocket message:', error);
                }
            });

            ws.on('close', () => {
                this.connectedClients.delete(clientId);
                console.log(`🐍 Python Brain client disconnected: ${clientId}`);
            });

            ws.on('error', (error) => {
                console.error(`🐍 WebSocket error for ${clientId}:`, error);
                this.connectedClients.delete(clientId);
            });
        });
    }

    initializePythonBrain() {
        console.log('🐍 Initializing Python Brain...');
        
        // Create Python brain script if it doesn't exist
        this.createPythonBrainScript();
        
        // Start Python process
        this.startPythonProcess();
        
        // Setup heartbeat
        this.startHeartbeat();
    }

    createPythonBrainScript() {
        const pythonScript = `
import json
import sys
import numpy as np
import pandas as pd
from datetime import datetime, timedelta
import asyncio
import websockets
import threading
import queue
import time
import warnings
warnings.filterwarnings('ignore')

class TradingBrain:
    def __init__(self):
        self.analysis_queue = queue.Queue()
        self.result_queue = queue.Queue()
        self.running = True
        
        # Initialize models and analyzers
        self.market_analyzer = MarketAnalyzer()
        self.strategy_analyzer = StrategyAnalyzer()
        self.risk_analyzer = RiskAnalyzer()
        self.portfolio_optimizer = PortfolioOptimizer()
        self.price_predictor = PricePredictor()
        self.pattern_detector = PatternDetector()
        
        print("🧠 Trading Brain initialized", flush=True)
    
    def process_analysis(self, analysis_type, data):
        try:
            if analysis_type == 'market_analysis':
                return self.market_analyzer.analyze(data)
            elif analysis_type == 'strategy_analysis':
                return self.strategy_analyzer.analyze(data)
            elif analysis_type == 'risk_analysis':
                return self.risk_analyzer.analyze(data)
            elif analysis_type == 'portfolio_optimization':
                return self.portfolio_optimizer.optimize(data)
            elif analysis_type == 'price_prediction':
                return self.price_predictor.predict(data)
            elif analysis_type == 'pattern_detection':
                return self.pattern_detector.detect(data)
            elif analysis_type == 'strategy_backtest':
                return self.backtest_strategy(data)
            else:
                return {'error': f'Unknown analysis type: {analysis_type}'}
        except Exception as e:
            return {'error': str(e), 'type': 'analysis_error'}

class MarketAnalyzer:
    def analyze(self, data):
        # Advanced market analysis
        market_data = data.get('market_data', [])
        if not market_data:
            return {'error': 'No market data provided'}
        
        df = pd.DataFrame(market_data)
        
        analysis = {
            'trend_analysis': self.analyze_trend(df),
            'volatility_analysis': self.analyze_volatility(df),
            'momentum_analysis': self.analyze_momentum(df),
            'support_resistance': self.find_support_resistance(df),
            'market_regime': self.detect_market_regime(df),
            'confidence': 0.85,
            'timestamp': datetime.now().isoformat()
        }
        
        return analysis
    
    def analyze_trend(self, df):
        if 'price' not in df.columns:
            return {'direction': 'unknown', 'strength': 0}
        
        prices = df['price'].values
        if len(prices) < 10:
            return {'direction': 'unknown', 'strength': 0}
        
        # Simple trend analysis
        short_ma = np.mean(prices[-5:])
        long_ma = np.mean(prices[-20:]) if len(prices) >= 20 else np.mean(prices)
        
        if short_ma > long_ma * 1.01:
            direction = 'bullish'
            strength = min((short_ma - long_ma) / long_ma * 100, 100)
        elif short_ma < long_ma * 0.99:
            direction = 'bearish'
            strength = min((long_ma - short_ma) / long_ma * 100, 100)
        else:
            direction = 'sideways'
            strength = 0
        
        return {
            'direction': direction,
            'strength': round(strength, 2),
            'short_ma': round(short_ma, 2),
            'long_ma': round(long_ma, 2)
        }
    
    def analyze_volatility(self, df):
        if 'price' not in df.columns:
            return {'level': 'unknown', 'value': 0}
        
        prices = df['price'].values
        if len(prices) < 2:
            return {'level': 'unknown', 'value': 0}
        
        returns = np.diff(prices) / prices[:-1]
        volatility = np.std(returns) * np.sqrt(252) * 100  # Annualized
        
        if volatility > 30:
            level = 'high'
        elif volatility > 15:
            level = 'medium'
        else:
            level = 'low'
        
        return {
            'level': level,
            'value': round(volatility, 2),
            'percentile': min(volatility / 50 * 100, 100)
        }
    
    def analyze_momentum(self, df):
        if 'price' not in df.columns:
            return {'strength': 0, 'direction': 'neutral'}
        
        prices = df['price'].values
        if len(prices) < 5:
            return {'strength': 0, 'direction': 'neutral'}
        
        # Simple momentum calculation
        momentum = (prices[-1] - prices[-5]) / prices[-5] * 100
        
        if momentum > 2:
            direction = 'strong_bullish'
        elif momentum > 0.5:
            direction = 'bullish'
        elif momentum < -2:
            direction = 'strong_bearish'
        elif momentum < -0.5:
            direction = 'bearish'
        else:
            direction = 'neutral'
        
        return {
            'strength': round(abs(momentum), 2),
            'direction': direction,
            'value': round(momentum, 2)
        }
    
    def find_support_resistance(self, df):
        if 'price' not in df.columns:
            return {'support': [], 'resistance': []}
        
        prices = df['price'].values
        if len(prices) < 10:
            return {'support': [], 'resistance': []}
        
        # Simple support/resistance detection
        highs = []
        lows = []
        
        for i in range(2, len(prices) - 2):
            if prices[i] > prices[i-1] and prices[i] > prices[i+1]:
                highs.append(prices[i])
            elif prices[i] < prices[i-1] and prices[i] < prices[i+1]:
                lows.append(prices[i])
        
        # Cluster similar levels
        resistance = list(set([round(h, 2) for h in highs[-5:]]))
        support = list(set([round(l, 2) for l in lows[-5:]]))
        
        return {
            'support': sorted(support),
            'resistance': sorted(resistance, reverse=True)
        }
    
    def detect_market_regime(self, df):
        if 'price' not in df.columns:
            return 'unknown'
        
        prices = df['price'].values
        if len(prices) < 20:
            return 'unknown'
        
        # Simple regime detection
        volatility = np.std(np.diff(prices) / prices[:-1])
        trend_strength = abs(np.corrcoef(range(len(prices)), prices)[0, 1])
        
        if volatility > 0.02:
            return 'volatile'
        elif trend_strength > 0.7:
            return 'trending'
        else:
            return 'ranging'

class StrategyAnalyzer:
    def analyze(self, data):
        strategies = data.get('strategies', [])
        performance_data = data.get('performance', {})
        
        analysis = {
            'strategy_performance': self.analyze_performance(strategies, performance_data),
            'optimization_suggestions': self.suggest_optimizations(strategies),
            'risk_assessment': self.assess_strategy_risk(strategies),
            'correlation_analysis': self.analyze_correlations(strategies),
            'confidence': 0.78,
            'timestamp': datetime.now().isoformat()
        }
        
        return analysis
    
    def analyze_performance(self, strategies, performance_data):
        if not strategies:
            return {'error': 'No strategies provided'}
        
        results = []
        for strategy in strategies:
            perf = {
                'name': strategy.get('name', 'Unknown'),
                'win_rate': strategy.get('winRate', 0),
                'profit_factor': strategy.get('profitFactor', 1),
                'sharpe_ratio': strategy.get('sharpeRatio', 0),
                'max_drawdown': strategy.get('maxDrawdown', 0),
                'score': self.calculate_strategy_score(strategy)
            }
            results.append(perf)
        
        return sorted(results, key=lambda x: x['score'], reverse=True)
    
    def calculate_strategy_score(self, strategy):
        win_rate = strategy.get('winRate', 0) / 100
        profit_factor = min(strategy.get('profitFactor', 1), 5) / 5
        sharpe = min(max(strategy.get('sharpeRatio', 0), 0), 3) / 3
        drawdown_penalty = max(0, 1 - abs(strategy.get('maxDrawdown', 0)) / 50)
        
        score = (win_rate * 0.3 + profit_factor * 0.3 + sharpe * 0.2 + drawdown_penalty * 0.2) * 100
        return round(score, 2)
    
    def suggest_optimizations(self, strategies):
        suggestions = []
        
        for strategy in strategies:
            if strategy.get('winRate', 0) < 60:
                suggestions.append({
                    'strategy': strategy.get('name'),
                    'type': 'win_rate_improvement',
                    'suggestion': 'Consider tightening entry criteria or improving signal quality'
                })
            
            if strategy.get('profitFactor', 1) < 1.5:
                suggestions.append({
                    'strategy': strategy.get('name'),
                    'type': 'profit_factor_improvement',
                    'suggestion': 'Review risk-reward ratios and exit strategies'
                })
        
        return suggestions
    
    def assess_strategy_risk(self, strategies):
        total_risk = 0
        risk_breakdown = []
        
        for strategy in strategies:
            risk_score = self.calculate_risk_score(strategy)
            total_risk += risk_score
            risk_breakdown.append({
                'strategy': strategy.get('name'),
                'risk_score': risk_score,
                'risk_level': 'high' if risk_score > 70 else 'medium' if risk_score > 40 else 'low'
            })
        
        return {
            'total_risk': round(total_risk / len(strategies) if strategies else 0, 2),
            'breakdown': risk_breakdown
        }
    
    def calculate_risk_score(self, strategy):
        drawdown = abs(strategy.get('maxDrawdown', 0))
        volatility = strategy.get('volatility', 20)
        correlation = strategy.get('correlation', 0.5)
        
        risk_score = (drawdown * 0.4 + volatility * 0.4 + correlation * 20 * 0.2)
        return min(round(risk_score, 2), 100)
    
    def analyze_correlations(self, strategies):
        # Simplified correlation analysis
        if len(strategies) < 2:
            return {'message': 'Need at least 2 strategies for correlation analysis'}
        
        correlations = []
        for i, strat1 in enumerate(strategies):
            for j, strat2 in enumerate(strategies[i+1:], i+1):
                # Simplified correlation calculation
                corr = np.random.uniform(0.2, 0.8)  # Mock correlation
                correlations.append({
                    'strategy1': strat1.get('name'),
                    'strategy2': strat2.get('name'),
                    'correlation': round(corr, 3),
                    'risk_level': 'high' if corr > 0.7 else 'medium' if corr > 0.5 else 'low'
                })
        
        return correlations

class RiskAnalyzer:
    def analyze(self, data):
        portfolio_data = data.get('portfolio', {})
        positions = data.get('positions', [])
        market_data = data.get('market_data', [])
        
        analysis = {
            'var_analysis': self.calculate_var(portfolio_data, positions),
            'stress_testing': self.stress_test(positions, market_data),
            'concentration_risk': self.analyze_concentration(positions),
            'liquidity_risk': self.analyze_liquidity(positions),
            'correlation_risk': self.analyze_correlation_risk(positions),
            'recommendations': self.generate_risk_recommendations(positions),
            'confidence': 0.82,
            'timestamp': datetime.now().isoformat()
        }
        
        return analysis
    
    def calculate_var(self, portfolio_data, positions):
        if not positions:
            return {'var_95': 0, 'var_99': 0, 'expected_shortfall': 0}
        
        # Simplified VaR calculation
        total_value = sum(pos.get('notionalValue', 0) for pos in positions)
        portfolio_volatility = 0.15  # Assumed 15% annual volatility
        
        var_95 = total_value * portfolio_volatility * 1.645 / np.sqrt(252)  # Daily VaR
        var_99 = total_value * portfolio_volatility * 2.326 / np.sqrt(252)
        expected_shortfall = var_99 * 1.2
        
        return {
            'var_95': round(var_95, 2),
            'var_99': round(var_99, 2),
            'expected_shortfall': round(expected_shortfall, 2),
            'confidence_level': '95% and 99%'
        }
    
    def stress_test(self, positions, market_data):
        scenarios = [
            {'name': 'Market Crash (-20%)', 'shock': -0.20},
            {'name': 'Volatility Spike (+50%)', 'shock': -0.10},
            {'name': 'Interest Rate Rise', 'shock': -0.05},
            {'name': 'Liquidity Crisis', 'shock': -0.15}
        ]
        
        results = []
        for scenario in scenarios:
            total_impact = 0
            for position in positions:
                position_value = position.get('notionalValue', 0)
                impact = position_value * scenario['shock']
                total_impact += impact
            
            results.append({
                'scenario': scenario['name'],
                'impact': round(total_impact, 2),
                'impact_percentage': round(scenario['shock'] * 100, 1)
            })
        
        return results
    
    def analyze_concentration(self, positions):
        if not positions:
            return {'concentration_score': 0, 'largest_position': 0, 'top_5_concentration': 0}
        
        position_values = [abs(pos.get('notionalValue', 0)) for pos in positions]
        total_value = sum(position_values)
        
        if total_value == 0:
            return {'concentration_score': 0, 'largest_position': 0, 'top_5_concentration': 0}
        
        largest_position = max(position_values) / total_value * 100
        top_5_positions = sorted(position_values, reverse=True)[:5]
        top_5_concentration = sum(top_5_positions) / total_value * 100
        
        concentration_score = min(largest_position * 2, 100)
        
        return {
            'concentration_score': round(concentration_score, 2),
            'largest_position': round(largest_position, 2),
            'top_5_concentration': round(top_5_concentration, 2),
            'risk_level': 'high' if concentration_score > 60 else 'medium' if concentration_score > 30 else 'low'
        }
    
    def analyze_liquidity(self, positions):
        liquidity_scores = []
        
        for position in positions:
            symbol = position.get('symbol', '')
            # Simplified liquidity scoring
            if symbol in ['ES', 'NQ', 'SPY', 'QQQ']:
                liquidity_score = 95
            elif symbol in ['CL', 'GC', 'EURUSD']:
                liquidity_score = 85
            else:
                liquidity_score = 70
            
            liquidity_scores.append(liquidity_score)
        
        avg_liquidity = np.mean(liquidity_scores) if liquidity_scores else 0
        
        return {
            'average_liquidity': round(avg_liquidity, 2),
            'liquidity_risk': 'low' if avg_liquidity > 80 else 'medium' if avg_liquidity > 60 else 'high',
            'position_liquidity': [
                {'symbol': pos.get('symbol'), 'liquidity_score': score}
                for pos, score in zip(positions, liquidity_scores)
            ]
        }
    
    def analyze_correlation_risk(self, positions):
        if len(positions) < 2:
            return {'correlation_risk': 'low', 'diversification_score': 100}
        
        # Simplified correlation analysis
        symbols = [pos.get('symbol', '') for pos in positions]
        
        # Mock correlation matrix
        high_corr_pairs = 0
        total_pairs = len(symbols) * (len(symbols) - 1) // 2
        
        for i, sym1 in enumerate(symbols):
            for sym2 in symbols[i+1:]:
                # Simplified correlation logic
                if (sym1 in ['ES', 'NQ'] and sym2 in ['ES', 'NQ']) or \
                   (sym1 in ['CL', 'GC'] and sym2 in ['CL', 'GC']):
                    high_corr_pairs += 1
        
        correlation_risk_score = (high_corr_pairs / total_pairs * 100) if total_pairs > 0 else 0
        diversification_score = 100 - correlation_risk_score
        
        return {
            'correlation_risk': 'high' if correlation_risk_score > 60 else 'medium' if correlation_risk_score > 30 else 'low',
            'diversification_score': round(diversification_score, 2),
            'high_correlation_pairs': high_corr_pairs
        }
    
    def generate_risk_recommendations(self, positions):
        recommendations = []
        
        # Analyze position sizes
        if positions:
            position_values = [abs(pos.get('notionalValue', 0)) for pos in positions]
            total_value = sum(position_values)
            
            if total_value > 0:
                largest_pct = max(position_values) / total_value * 100
                if largest_pct > 25:
                    recommendations.append({
                        'type': 'concentration',
                        'priority': 'high',
                        'message': f'Largest position represents {largest_pct:.1f}% of portfolio. Consider reducing concentration.'
                    })
                
                if len(positions) < 5:
                    recommendations.append({
                        'type': 'diversification',
                        'priority': 'medium',
                        'message': 'Consider adding more positions to improve diversification.'
                    })
        
        return recommendations

class PortfolioOptimizer:
    def optimize(self, data):
        positions = data.get('positions', [])
        target_return = data.get('target_return', 0.12)
        risk_tolerance = data.get('risk_tolerance', 'medium')
        
        optimization = {
            'current_allocation': self.analyze_current_allocation(positions),
            'optimal_allocation': self.calculate_optimal_allocation(positions, target_return, risk_tolerance),
            'rebalancing_suggestions': self.generate_rebalancing_suggestions(positions),
            'expected_metrics': self.calculate_expected_metrics(positions),
            'confidence': 0.75,
            'timestamp': datetime.now().isoformat()
        }
        
        return optimization
    
    def analyze_current_allocation(self, positions):
        if not positions:
            return {}
        
        total_value = sum(abs(pos.get('notionalValue', 0)) for pos in positions)
        
        allocation = {}
        for position in positions:
            symbol = position.get('symbol', 'Unknown')
            value = abs(position.get('notionalValue', 0))
            percentage = (value / total_value * 100) if total_value > 0 else 0
            allocation[symbol] = round(percentage, 2)
        
        return allocation
    
    def calculate_optimal_allocation(self, positions, target_return, risk_tolerance):
        # Simplified optimal allocation
        risk_multiplier = {'low': 0.5, 'medium': 1.0, 'high': 1.5}.get(risk_tolerance, 1.0)
        
        if not positions:
            return {}
        
        # Mock optimal allocation based on risk tolerance
        symbols = list(set(pos.get('symbol') for pos in positions))
        equal_weight = 100 / len(symbols) if symbols else 0
        
        optimal = {}
        for symbol in symbols:
            # Adjust based on risk tolerance
            if symbol in ['ES', 'SPY']:  # Lower risk
                weight = equal_weight * (1.2 if risk_tolerance == 'low' else 1.0 if risk_tolerance == 'medium' else 0.8)
            else:  # Higher risk
                weight = equal_weight * (0.8 if risk_tolerance == 'low' else 1.0 if risk_tolerance == 'medium' else 1.2)
            
            optimal[symbol] = round(weight, 2)
        
        # Normalize to 100%
        total = sum(optimal.values())
        if total > 0:
            optimal = {k: round(v / total * 100, 2) for k, v in optimal.items()}
        
        return optimal
    
    def generate_rebalancing_suggestions(self, positions):
        current = self.analyze_current_allocation(positions)
        optimal = self.calculate_optimal_allocation(positions, 0.12, 'medium')
        
        suggestions = []
        for symbol in set(list(current.keys()) + list(optimal.keys())):
            current_weight = current.get(symbol, 0)
            optimal_weight = optimal.get(symbol, 0)
            difference = optimal_weight - current_weight
            
            if abs(difference) > 5:  # Only suggest if difference > 5%
                action = 'increase' if difference > 0 else 'decrease'
                suggestions.append({
                    'symbol': symbol,
                    'action': action,
                    'current_weight': current_weight,
                    'target_weight': optimal_weight,
                    'adjustment': round(abs(difference), 2)
                })
        
        return suggestions
    
    def calculate_expected_metrics(self, positions):
        # Mock expected metrics
        return {
            'expected_return': 12.5,
            'expected_volatility': 15.2,
            'sharpe_ratio': 0.82,
            'max_drawdown': 8.5,
            'var_95': 2.1
        }

class PricePredictor:
    def predict(self, data):
        market_data = data.get('market_data', [])
        prediction_horizon = data.get('horizon', 24)  # hours
        
        if not market_data:
            return {'error': 'No market data provided for prediction'}
        
        df = pd.DataFrame(market_data)
        
        prediction = {
            'price_forecast': self.forecast_price(df, prediction_horizon),
            'direction_probability': self.predict_direction(df),
            'volatility_forecast': self.forecast_volatility(df),
            'confidence_intervals': self.calculate_confidence_intervals(df),
            'model_accuracy': 0.73,
            'confidence': 0.68,
            'timestamp': datetime.now().isoformat()
        }
        
        return prediction
    
    def forecast_price(self, df, horizon):
        if 'price' not in df.columns or len(df) < 5:
            return {'error': 'Insufficient price data'}
        
        prices = df['price'].values
        current_price = prices[-1]
        
        # Simple trend-based forecast
        if len(prices) >= 10:
            recent_trend = (prices[-1] - prices[-10]) / prices[-10]
            forecast_price = current_price * (1 + recent_trend * horizon / 24)
        else:
            forecast_price = current_price
        
        return {
            'current_price': round(current_price, 2),
            'forecast_price': round(forecast_price, 2),
            'change': round(forecast_price - current_price, 2),
            'change_percent': round((forecast_price - current_price) / current_price * 100, 2),
            'horizon_hours': horizon
        }
    
    def predict_direction(self, df):
        if 'price' not in df.columns or len(df) < 5:
            return {'up_probability': 50, 'down_probability': 50}
        
        prices = df['price'].values
        
        # Simple momentum-based direction prediction
        short_momentum = (prices[-1] - prices[-3]) / prices[-3] if len(prices) >= 3 else 0
        long_momentum = (prices[-1] - prices[-10]) / prices[-10] if len(prices) >= 10 else 0
        
        momentum_score = (short_momentum * 0.7 + long_momentum * 0.3) * 100
        
        # Convert to probability
        if momentum_score > 0:
            up_prob = min(50 + momentum_score * 10, 85)
        else:
            up_prob = max(50 + momentum_score * 10, 15)
        
        return {
            'up_probability': round(up_prob, 1),
            'down_probability': round(100 - up_prob, 1),
            'momentum_score': round(momentum_score, 3)
        }
    
    def forecast_volatility(self, df):
        if 'price' not in df.columns or len(df) < 10:
            return {'forecast': 15.0, 'current': 15.0}
        
        prices = df['price'].values
        returns = np.diff(prices) / prices[:-1]
        
        current_vol = np.std(returns) * np.sqrt(252) * 100
        
        # Simple volatility forecast (mean reversion)
        long_term_vol = 20.0  # Assumed long-term volatility
        forecast_vol = current_vol * 0.7 + long_term_vol * 0.3
        
        return {
            'current': round(current_vol, 2),
            'forecast': round(forecast_vol, 2),
            'regime': 'high' if forecast_vol > 25 else 'medium' if forecast_vol > 15 else 'low'
        }
    
    def calculate_confidence_intervals(self, df):
        if 'price' not in df.columns or len(df) < 5:
            return {'95_percent': {'lower': 0, 'upper': 0}, '68_percent': {'lower': 0, 'upper': 0}}
        
        prices = df['price'].values
        current_price = prices[-1]
        
        # Simple volatility-based confidence intervals
        if len(prices) >= 10:
            returns = np.diff(prices) / prices[:-1]
            volatility = np.std(returns)
        else:
            volatility = 0.02  # Default 2% daily volatility
        
        # Daily confidence intervals
        ci_95_lower = current_price * (1 - 1.96 * volatility)
        ci_95_upper = current_price * (1 + 1.96 * volatility)
        ci_68_lower = current_price * (1 - 1.0 * volatility)
        ci_68_upper = current_price * (1 + 1.0 * volatility)
        
        return {
            '95_percent': {
                'lower': round(ci_95_lower, 2),
                'upper': round(ci_95_upper, 2)
            },
            '68_percent': {
                'lower': round(ci_68_lower, 2),
                'upper': round(ci_68_upper, 2)
            }
        }

class PatternDetector:
    def detect(self, data):
        market_data = data.get('market_data', [])
        
        if not market_data:
            return {'error': 'No market data provided for pattern detection'}
        
        df = pd.DataFrame(market_data)
        
        detection = {
            'chart_patterns': self.detect_chart_patterns(df),
            'candlestick_patterns': self.detect_candlestick_patterns(df),
            'support_resistance': self.detect_support_resistance_patterns(df),
            'trend_patterns': self.detect_trend_patterns(df),
            'volume_patterns': self.detect_volume_patterns(df),
            'confidence': 0.71,
            'timestamp': datetime.now().isoformat()
        }
        
        return detection
    
    def detect_chart_patterns(self, df):
        patterns = []
        
        if 'price' not in df.columns or len(df) < 20:
            return patterns
        
        prices = df['price'].values
        
        # Simple pattern detection
        if len(prices) >= 20:
            # Head and shoulders pattern (simplified)
            if self.is_head_and_shoulders(prices[-20:]):
                patterns.append({
                    'pattern': 'Head and Shoulders',
                    'type': 'reversal',
                    'confidence': 0.75,
                    'direction': 'bearish'
                })
            
            # Double top/bottom (simplified)
            if self.is_double_top(prices[-15:]):
                patterns.append({
                    'pattern': 'Double Top',
                    'type': 'reversal',
                    'confidence': 0.68,
                    'direction': 'bearish'
                })
        
        return patterns
    
    def is_head_and_shoulders(self, prices):
        # Simplified head and shoulders detection
        if len(prices) < 15:
            return False
        
        # Find local maxima
        peaks = []
        for i in range(2, len(prices) - 2):
            if prices[i] > prices[i-1] and prices[i] > prices[i+1]:
                peaks.append((i, prices[i]))
        
        if len(peaks) >= 3:
            # Check if middle peak is highest
            peaks.sort(key=lambda x: x[1], reverse=True)
            if peaks[0][0] > peaks[1][0] and peaks[0][0] > peaks[2][0]:
                return True
        
        return False
    
    def is_double_top(self, prices):
        # Simplified double top detection
        if len(prices) < 10:
            return False
        
        max_price = max(prices)
        max_indices = [i for i, p in enumerate(prices) if p >= max_price * 0.98]
        
        if len(max_indices) >= 2 and max_indices[-1] - max_indices[0] > 5:
            return True
        
        return False
    
    def detect_candlestick_patterns(self, df):
        patterns = []
        
        required_cols = ['open', 'high', 'low', 'close']
        if not all(col in df.columns for col in required_cols) or len(df) < 3:
            return patterns
        
        # Simplified candlestick pattern detection
        for i in range(2, len(df)):
            current = df.iloc[i]
            prev = df.iloc[i-1]
            
            # Doji pattern
            if abs(current['close'] - current['open']) < (current['high'] - current['low']) * 0.1:
                patterns.append({
                    'pattern': 'Doji',
                    'type': 'indecision',
                    'confidence': 0.65,
                    'index': i
                })
            
            # Hammer pattern
            body_size = abs(current['close'] - current['open'])
            lower_shadow = min(current['open'], current['close']) - current['low']
            upper_shadow = current['high'] - max(current['open'], current['close'])
            
            if lower_shadow > body_size * 2 and upper_shadow < body_size * 0.5:
                patterns.append({
                    'pattern': 'Hammer',
                    'type': 'reversal',
                    'confidence': 0.72,
                    'direction': 'bullish',
                    'index': i
                })
        
        return patterns[-5:]  # Return last 5 patterns
    
    def detect_support_resistance_patterns(self, df):
        if 'price' not in df.columns or len(df) < 10:
            return {'support_levels': [], 'resistance_levels': []}
        
        prices = df['price'].values
        
        # Find support and resistance levels
        support_levels = []
        resistance_levels = []
        
        for i in range(2, len(prices) - 2):
            # Local minima (support)
            if prices[i] < prices[i-1] and prices[i] < prices[i+1]:
                support_levels.append(prices[i])
            
            # Local maxima (resistance)
            if prices[i] > prices[i-1] and prices[i] > prices[i+1]:
                resistance_levels.append(prices[i])
        
        # Cluster similar levels
        support_levels = self.cluster_levels(support_levels)
        resistance_levels = self.cluster_levels(resistance_levels)
        
        return {
            'support_levels': support_levels[-3:],  # Last 3 support levels
            'resistance_levels': resistance_levels[-3:]  # Last 3 resistance levels
        }
    
    def cluster_levels(self, levels):
        if not levels:
            return []
        
        clustered = []
        sorted_levels = sorted(levels)
        
        current_cluster = [sorted_levels[0]]
        
        for level in sorted_levels[1:]:
            if abs(level - current_cluster[-1]) < current_cluster[-1] * 0.01:  # Within 1%
                current_cluster.append(level)
            else:
                clustered.append(round(np.mean(current_cluster), 2))
                current_cluster = [level]
        
        clustered.append(round(np.mean(current_cluster), 2))
        return clustered
    
    def detect_trend_patterns(self, df):
        if 'price' not in df.columns or len(df) < 10:
            return {'trend': 'unknown', 'strength': 0}
        
        prices = df['price'].values
        
        # Linear regression for trend
        x = np.arange(len(prices))
        slope, intercept = np.polyfit(x, prices, 1)
        
        # Calculate R-squared
        y_pred = slope * x + intercept
        ss_res = np.sum((prices - y_pred) ** 2)
        ss_tot = np.sum((prices - np.mean(prices)) ** 2)
        r_squared = 1 - (ss_res / ss_tot) if ss_tot != 0 else 0
        
        # Determine trend
        if slope > 0 and r_squared > 0.5:
            trend = 'uptrend'
        elif slope < 0 and r_squared > 0.5:
            trend = 'downtrend'
        else:
            trend = 'sideways'
        
        return {
            'trend': trend,
            'strength': round(r_squared * 100, 2),
            'slope': round(slope, 4)
        }
    
    def detect_volume_patterns(self, df):
        if 'volume' not in df.columns or len(df) < 5:
            return {'pattern': 'unknown', 'strength': 0}
        
        volumes = df['volume'].values
        prices = df['price'].values if 'price' in df.columns else None
        
        # Volume trend
        recent_vol = np.mean(volumes[-5:])
        older_vol = np.mean(volumes[-10:-5]) if len(volumes) >= 10 else recent_vol
        
        vol_change = (recent_vol - older_vol) / older_vol * 100 if older_vol != 0 else 0
        
        # Price-volume relationship
        if prices is not None and len(prices) == len(volumes):
            price_change = (prices[-1] - prices[-5]) / prices[-5] * 100 if len(prices) >= 5 else 0
            
            if price_change > 0 and vol_change > 0:
                pattern = 'bullish_confirmation'
            elif price_change < 0 and vol_change > 0:
                pattern = 'bearish_confirmation'
            elif price_change > 0 and vol_change < 0:
                pattern = 'bullish_divergence'
            elif price_change < 0 and vol_change < 0:
                pattern = 'bearish_divergence'
            else:
                pattern = 'neutral'
        else:
            pattern = 'volume_only'
        
        return {
            'pattern': pattern,
            'volume_change': round(vol_change, 2),
            'strength': min(abs(vol_change), 100)
        }

def main():
    brain = TradingBrain()
    
    print("🧠 Trading Brain ready for analysis", flush=True)
    
    # Main processing loop
    while brain.running:
        try:
            # Read from stdin
            line = sys.stdin.readline()
            if not line:
                break
            
            request = json.loads(line.strip())
            analysis_type = request.get('type')
            data = request.get('data', {})
            request_id = request.get('id')
            
            # Process the analysis
            result = brain.process_analysis(analysis_type, data)
            
            # Send result back
            response = {
                'id': request_id,
                'type': analysis_type,
                'result': result,
                'timestamp': datetime.now().isoformat()
            }
            
            print(json.dumps(response), flush=True)
            
        except Exception as e:
            error_response = {
                'id': request.get('id', 'unknown'),
                'type': 'error',
                'result': {'error': str(e)},
                'timestamp': datetime.now().isoformat()
            }
            print(json.dumps(error_response), flush=True)

if __name__ == "__main__":
    main()
`;

        const pythonDir = path.join(__dirname, 'python');
        if (!fs.existsSync(pythonDir)) {
            fs.mkdirSync(pythonDir, { recursive: true });
        }

        const scriptPath = path.join(pythonDir, 'trading_brain.py');
        fs.writeFileSync(scriptPath, pythonScript);
        
        console.log('🐍 Python brain script created at:', scriptPath);
    }

    startPythonProcess() {
        const scriptPath = path.join(__dirname, 'python', 'trading_brain.py');
        
        console.log('🐍 Starting Python brain process...');
        
        this.pythonProcess = spawn('python3', [scriptPath], {
            stdio: ['pipe', 'pipe', 'pipe'],
            cwd: __dirname
        });

        this.pythonProcess.stdout.on('data', (data) => {
            const lines = data.toString().split('\n').filter(line => line.trim());
            
            lines.forEach(line => {
                try {
                    const response = JSON.parse(line);
                    this.handlePythonResponse(response);
                } catch (error) {
                    // Regular log output
                    console.log(`🐍 Python: ${line}`);
                    if (line.includes('Trading Brain ready')) {
                        this.pythonReady = true;
                        this.lastHeartbeat = Date.now();
                        this.broadcastToClients({
                            type: 'python_status',
                            status: 'ready',
                            timestamp: new Date().toISOString()
                        });
                    }
                }
            });
        });

        this.pythonProcess.stderr.on('data', (data) => {
            console.error(`🐍 Python Error: ${data}`);
        });

        this.pythonProcess.on('close', (code) => {
            console.log(`🐍 Python process exited with code ${code}`);
            this.pythonReady = false;
            
            // Restart if unexpected exit
            if (code !== 0) {
                console.log('🐍 Restarting Python process...');
                setTimeout(() => this.startPythonProcess(), 5000);
            }
        });

        this.pythonProcess.on('error', (error) => {
            console.error('🐍 Failed to start Python process:', error);
            this.pythonReady = false;
        });
    }

    sendToPython(analysisType, data, res) {
        if (!this.pythonReady || !this.pythonProcess) {
            return res.status(503).json({ 
                error: 'Python brain not ready',
                status: 'service_unavailable'
            });
        }

        const requestId = `req_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
        
        const request = {
            id: requestId,
            type: analysisType,
            data: data,
            timestamp: new Date().toISOString()
        };

        // Store the response handler
        this.pythonQueue.push({
            id: requestId,
            res: res,
            timestamp: Date.now()
        });

        // Send to Python
        try {
            this.pythonProcess.stdin.write(JSON.stringify(request) + '\n');
        } catch (error) {
            console.error('🐍 Error sending to Python:', error);
            res.status(500).json({ error: 'Failed to send request to Python brain' });
        }
    }

    handlePythonResponse(response) {
        const requestId = response.id;
        const queueIndex = this.pythonQueue.findIndex(item => item.id === requestId);
        
        if (queueIndex !== -1) {
            const queueItem = this.pythonQueue[queueIndex];
            this.pythonQueue.splice(queueIndex, 1);
            
            // Send response back to client
            queueItem.res.json({
                analysis_type: response.type,
                result: response.result,
                timestamp: response.timestamp,
                processing_time: Date.now() - queueItem.timestamp
            });

            // Cache the result
            this.analysisCache.set(`${response.type}_${Date.now()}`, response.result);
            
            // Broadcast to WebSocket clients
            this.broadcastToClients({
                type: 'analysis_complete',
                analysis_type: response.type,
                result: response.result,
                timestamp: response.timestamp
            });
        }
    }

    distributeData(data) {
        // Distribute data to appropriate analysis modules
        const dataType = this.detectDataType(data);
        
        // Store in appropriate stream
        this.dataStreams.set(dataType, {
            data: data,
            timestamp: Date.now(),
            subscribers: this.subscribers.get(dataType) || []
        });

        // Broadcast to subscribers
        this.broadcastToSubscribers(dataType, data);

        // Trigger automatic analysis if configured
        this.triggerAutoAnalysis(dataType, data);
    }

    detectDataType(data) {
        if (data.market && data.orderFlow) return 'market_data';
        if (data.strategies) return 'strategy_data';
        if (data.positions) return 'position_data';
        if (data.performance) return 'performance_data';
        return 'generic_data';
    }

    broadcastToSubscribers(dataType, data) {
        const stream = this.dataStreams.get(dataType);
        if (stream && stream.subscribers) {
            stream.subscribers.forEach(subscriberId => {
                // Send to specific subscriber
                this.broadcastToClients({
                    type: 'data_update',
                    dataType: dataType,
                    data: data,
                    timestamp: new Date().toISOString()
                }, subscriberId);
            });
        }
    }

    triggerAutoAnalysis(dataType, data) {
        // Automatically trigger relevant analysis based on data type
        if (dataType === 'market_data' && this.pythonReady) {
            // Trigger market analysis
            this.sendToPython('market_analysis', data, {
                json: (result) => {
                    this.broadcastToClients({
                        type: 'auto_analysis',
                        analysis_type: 'market_analysis',
                        result: result,
                        triggered_by: dataType
                    });
                }
            });
        }
    }

    monitorStrategy(data, res) {
        // Enhanced strategy monitoring with Python brain integration
        if (!this.pythonReady) {
            return res.status(503).json({ error: 'Python brain not available for strategy monitoring' });
        }

        // Send strategy data for analysis
        this.sendToPython('strategy_analysis', {
            strategies: data.strategies,
            performance: data.performance,
            market_conditions: data.market_conditions
        }, res);
    }

    processRealtimeData(data, res) {
        // Process real-time data through Python brain
        const analysisTypes = ['market_analysis', 'risk_analysis'];
        const results = {};
        let completed = 0;

        analysisTypes.forEach(analysisType => {
            this.sendToPython(analysisType, data, {
                json: (result) => {
                    results[analysisType] = result;
                    completed++;
                    
                    if (completed === analysisTypes.length) {
                        res.json({
                            realtime_analysis: results,
                            timestamp: new Date().toISOString()
                        });
                    }
                }
            });
        });
    }

    handleWebSocketMessage(ws, data, clientId) {
        switch (data.type) {
            case 'subscribe':
                this.handleSubscription(ws, data, clientId);
                break;
                
            case 'analyze':
                this.handleAnalysisRequest(ws, data, clientId);
                break;
                
            case 'ping':
                ws.send(JSON.stringify({ 
                    type: 'pong', 
                    timestamp: new Date().toISOString() 
                }));
                break;
                
            default:
                console.log(`🐍 Unknown message type: ${data.type}`);
        }
    }

    handleSubscription(ws, data, clientId) {
        const dataTypes = data.dataTypes || [];
        
        dataTypes.forEach(dataType => {
            if (!this.subscribers.has(dataType)) {
                this.subscribers.set(dataType, []);
            }
            this.subscribers.get(dataType).push(clientId);
        });

        ws.send(JSON.stringify({
            type: 'subscription_confirmed',
            dataTypes: dataTypes,
            timestamp: new Date().toISOString()
        }));
    }

    handleAnalysisRequest(ws, data, clientId) {
        if (!this.pythonReady) {
            ws.send(JSON.stringify({
                type: 'analysis_error',
                error: 'Python brain not ready',
                timestamp: new Date().toISOString()
            }));
            return;
        }

        this.sendToPython(data.analysisType, data.data, {
            json: (result) => {
                ws.send(JSON.stringify({
                    type: 'analysis_result',
                    analysis_type: data.analysisType,
                    result: result,
                    timestamp: new Date().toISOString()
                }));
            }
        });
    }

    broadcastToClients(message, specificClientId = null) {
        const messageStr = JSON.stringify(message);
        
        this.wss.clients.forEach((client) => {
            if (client.readyState === WebSocket.OPEN) {
                if (!specificClientId || client.clientId === specificClientId) {
                    client.send(messageStr);
                }
            }
        });
    }

    startHeartbeat() {
        setInterval(() => {
            if (this.pythonReady) {
                this.lastHeartbeat = Date.now();
                
                // Send heartbeat to Python
                if (this.pythonProcess && this.pythonProcess.stdin.writable) {
                    try {
                        this.pythonProcess.stdin.write(JSON.stringify({
                            id: 'heartbeat',
                            type: 'heartbeat',
                            timestamp: new Date().toISOString()
                        }) + '\n');
                    } catch (error) {
                        console.error('🐍 Heartbeat error:', error);
                    }
                }
            }
        }, 30000); // Every 30 seconds
    }

    start() {
        this.server.listen(this.port, () => {
            console.log(`
╔══════════════════════════════════════════════════════════════╗
║                    NEXUS V5.0 Python Brain                  ║
║                  Advanced Analytics Engine                   ║
║                                                              ║
║  🐍 Python Brain running on http://localhost:${this.port}            ║
║  🧠 Advanced AI Analytics Engine                            ║
║  📊 Real-time Data Distribution                             ║
║  🔬 Strategy Analysis & Optimization                        ║
║                                                              ║
║  Available Analysis:                                         ║
║  • Market Analysis & Pattern Detection                      ║
║  • Strategy Performance Optimization                        ║
║  • Risk Assessment & Portfolio Analysis                     ║
║  • Price Prediction & Forecasting                          ║
║  • Real-time Data Processing                                ║
║  • Automated Strategy Monitoring                            ║
╚══════════════════════════════════════════════════════════════╝
            `);
        });

        // Graceful shutdown
        process.on('SIGINT', () => {
            console.log('\n🐍 Shutting down Python Brain...');
            
            if (this.pythonProcess) {
                this.pythonProcess.kill('SIGTERM');
            }
            
            this.server.close(() => {
                console.log('🐍 Python Brain server closed');
                process.exit(0);
            });
        });
    }
}

// Start the Python Brain service
const pythonBrain = new PythonBrainService();
pythonBrain.start();

module.exports = PythonBrainService;