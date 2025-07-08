const express = require('express');
const WebSocket = require('ws');
const cors = require('cors');
const fs = require('fs');
const path = require('path');

class NexusNT8Bridge {
    constructor() {
        this.app = express();
        this.server = null;
        this.wss = null;
        this.port = 4000;
        
        // Data storage
        this.latestData = null;
        this.dataHistory = [];
        this.maxHistorySize = 1000;
        
        // Connection tracking
        this.connectedClients = new Set();
        this.nt8Connected = false;
        this.lastNT8Heartbeat = null;
        
        // Data translation and routing
        this.dataRoutes = new Map();
        this.setupDataRoutes();
        
        this.setupMiddleware();
        this.setupRoutes();
        this.setupWebSocket();
    }

    setupDataRoutes() {
        // NinjaTrader 8 data route
        this.dataRoutes.set('NINJA_TRADER', {
            endpoint: '/api/dashboard-data',
            validator: (data) => data.systemVersion && data.market && data.orderFlow,
            transformer: (data) => ({
                ...data,
                source: 'NINJA_TRADER',
                receivedAt: new Date().toISOString(),
                dataType: 'REAL_TIME'
            })
        });

        // Sierra Chart data route
        this.dataRoutes.set('SIERRA_CHART', {
            endpoint: '/api/sierra-data',
            validator: (data) => data.DateTime && data.Symbol && data.Last,
            transformer: (data) => ({
                ...data,
                source: 'SIERRA_CHART',
                receivedAt: new Date().toISOString(),
                dataType: 'REAL_TIME'
            })
        });

        // Rithmic data route
        this.dataRoutes.set('RITHMIC', {
            endpoint: '/api/rithmic-data',
            validator: (data) => data.instrument_id && data.last_trade_price,
            transformer: (data) => ({
                ...data,
                source: 'RITHMIC',
                receivedAt: new Date().toISOString(),
                dataType: 'REAL_TIME'
            })
        });
    }

    setupMiddleware() {
        this.app.use(cors({
            origin: ['http://localhost:3000', 'http://localhost:5173'],
            credentials: true
        }));
        this.app.use(express.json({ limit: '10mb' }));
        this.app.use(express.urlencoded({ extended: true }));
        
        // Request logging with data source detection
        this.app.use((req, res, next) => {
            const timestamp = new Date().toISOString();
            console.log(`${timestamp} - ${req.method} ${req.path}`);
            
            // Detect data source from request
            if (req.body && Object.keys(req.body).length > 0) {
                const detectedSource = this.detectDataSource(req.body);
                if (detectedSource) {
                    req.dataSource = detectedSource;
                    console.log(`📡 Detected data source: ${detectedSource}`);
                }
            }
            
            next();
        });
    }

    detectDataSource(data) {
        for (const [source, route] of this.dataRoutes) {
            if (route.validator(data)) {
                return source;
            }
        }
        return 'UNKNOWN';
    }

    setupRoutes() {
        // Health check endpoint
        this.app.get('/health', (req, res) => {
            res.json({
                status: 'healthy',
                timestamp: new Date().toISOString(),
                nt8Connected: this.nt8Connected,
                connectedClients: this.connectedClients.size,
                lastDataReceived: this.latestData?.timestamp || null,
                supportedSources: Array.from(this.dataRoutes.keys())
            });
        });

        // Universal data endpoint - handles all broker types
        this.app.post('/api/dashboard-data', (req, res) => {
            this.handleUniversalData(req, res);
        });

        // Specific broker endpoints
        this.app.post('/api/sierra-data', (req, res) => {
            this.handleSierraChartData(req, res);
        });

        this.app.post('/api/rithmic-data', (req, res) => {
            this.handleRithmicData(req, res);
        });

        // Get latest data endpoint
        this.app.get('/api/dashboard-data', (req, res) => {
            if (!this.latestData) {
                return res.json({
                    message: 'No data available',
                    connectedSources: this.getConnectedSources()
                });
            }

            res.json(this.latestData);
        });

        // Historical data endpoint
        this.app.get('/api/dashboard-data/history', (req, res) => {
            const limit = parseInt(req.query.limit) || 100;
            const offset = parseInt(req.query.offset) || 0;
            const source = req.query.source;
            
            let filteredHistory = this.dataHistory;
            if (source) {
                filteredHistory = this.dataHistory.filter(item => item.source === source);
            }
            
            const historySlice = filteredHistory
                .slice(-limit - offset, -offset || undefined)
                .reverse();

            res.json({
                data: historySlice,
                total: filteredHistory.length,
                limit,
                offset,
                sources: this.getConnectedSources()
            });
        });

        // Data source management
        this.app.get('/api/sources', (req, res) => {
            res.json({
                supported: Array.from(this.dataRoutes.keys()),
                connected: this.getConnectedSources(),
                lastActivity: this.getSourceActivity()
            });
        });

        // Configuration endpoint
        this.app.get('/api/config', (req, res) => {
            res.json({
                maxHistorySize: this.maxHistorySize,
                port: this.port,
                version: '5.0.1',
                dataRoutes: Array.from(this.dataRoutes.keys())
            });
        });

        this.app.post('/api/config', (req, res) => {
            const { maxHistorySize } = req.body;
            
            if (maxHistorySize && maxHistorySize > 0) {
                this.maxHistorySize = maxHistorySize;
                
                if (this.dataHistory.length > this.maxHistorySize) {
                    this.dataHistory = this.dataHistory.slice(-this.maxHistorySize);
                }
            }

            res.json({ message: 'Configuration updated' });
        });
    }

    handleUniversalData(req, res) {
        try {
            const data = req.body;
            const source = req.dataSource || 'NINJA_TRADER'; // Default to NT8
            
            // Validate data
            if (!data.timestamp && !data.DateTime) {
                return res.status(400).json({ error: 'Missing timestamp' });
            }

            // Transform data based on source
            const route = this.dataRoutes.get(source);
            const transformedData = route ? route.transformer(data) : {
                ...data,
                source: 'UNKNOWN',
                receivedAt: new Date().toISOString()
            };

            // Update connection status
            if (source === 'NINJA_TRADER') {
                this.nt8Connected = true;
                this.lastNT8Heartbeat = Date.now();
            }

            // Store and broadcast data
            this.storeAndBroadcastData(transformedData, source);

            // Log key metrics
            this.logDataMetrics(transformedData, source);

            res.json({ 
                status: 'success',
                timestamp: new Date().toISOString(),
                source: source,
                clientsBroadcast: this.connectedClients.size
            });

        } catch (error) {
            console.error('Error processing universal data:', error);
            res.status(500).json({ error: 'Internal server error' });
        }
    }

    handleSierraChartData(req, res) {
        try {
            const data = req.body;
            
            const transformedData = {
                ...data,
                source: 'SIERRA_CHART',
                receivedAt: new Date().toISOString(),
                dataType: 'REAL_TIME'
            };

            this.storeAndBroadcastData(transformedData, 'SIERRA_CHART');
            
            console.log(`Sierra Chart Data: ${data.Symbol} @ ${data.Last}, Volume: ${data.Volume}`);

            res.json({ 
                status: 'success',
                timestamp: new Date().toISOString(),
                source: 'SIERRA_CHART'
            });

        } catch (error) {
            console.error('Error processing Sierra Chart data:', error);
            res.status(500).json({ error: 'Internal server error' });
        }
    }

    handleRithmicData(req, res) {
        try {
            const data = req.body;
            
            const transformedData = {
                ...data,
                source: 'RITHMIC',
                receivedAt: new Date().toISOString(),
                dataType: 'REAL_TIME'
            };

            this.storeAndBroadcastData(transformedData, 'RITHMIC');
            
            console.log(`Rithmic Data: ${data.instrument_id} @ ${data.last_trade_price}`);

            res.json({ 
                status: 'success',
                timestamp: new Date().toISOString(),
                source: 'RITHMIC'
            });

        } catch (error) {
            console.error('Error processing Rithmic data:', error);
            res.status(500).json({ error: 'Internal server error' });
        }
    }

    storeAndBroadcastData(data, source) {
        // Store latest data
        this.latestData = data;

        // Add to history
        this.dataHistory.push(data);
        if (this.dataHistory.length > this.maxHistorySize) {
            this.dataHistory.shift();
        }

        // Broadcast to all WebSocket clients
        this.broadcastToClients(data);
    }

    logDataMetrics(data, source) {
        switch (source) {
            case 'NINJA_TRADER':
                console.log(`NT8 Data: Price=${data.market?.currentPrice}, Volume=${data.market?.volume}, Signals=${data.signals?.activeCount}`);
                break;
            case 'SIERRA_CHART':
                console.log(`Sierra Data: ${data.Symbol} @ ${data.Last}, Volume=${data.Volume}`);
                break;
            case 'RITHMIC':
                console.log(`Rithmic Data: ${data.instrument_id} @ ${data.last_trade_price}`);
                break;
            default:
                console.log(`Unknown source data received: ${source}`);
        }
    }

    getConnectedSources() {
        const sources = [];
        if (this.nt8Connected) sources.push('NINJA_TRADER');
        // Add logic for other sources based on recent activity
        return sources;
    }

    getSourceActivity() {
        return {
            NINJA_TRADER: this.lastNT8Heartbeat,
            SIERRA_CHART: null, // Would track last Sierra Chart data
            RITHMIC: null // Would track last Rithmic data
        };
    }

    setupWebSocket() {
        this.server = require('http').createServer(this.app);
        this.wss = new WebSocket.Server({ server: this.server });

        this.wss.on('connection', (ws, req) => {
            const clientId = `client_${Date.now()}_${Math.random().toString(36).substr(2, 9)}`;
            this.connectedClients.add(clientId);
            
            console.log(`WebSocket client connected: ${clientId} (Total: ${this.connectedClients.size})`);

            // Send latest data immediately
            if (this.latestData) {
                ws.send(JSON.stringify(this.latestData));
            }

            // Send connection status
            ws.send(JSON.stringify({
                type: 'connection_status',
                clientId,
                connectedSources: this.getConnectedSources(),
                timestamp: new Date().toISOString()
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
                console.log(`WebSocket client disconnected: ${clientId} (Total: ${this.connectedClients.size})`);
            });

            ws.on('error', (error) => {
                console.error(`WebSocket error for ${clientId}:`, error);
                this.connectedClients.delete(clientId);
            });
        });
    }

    handleWebSocketMessage(ws, data, clientId) {
        switch (data.type) {
            case 'ping':
                ws.send(JSON.stringify({ type: 'pong', timestamp: new Date().toISOString() }));
                break;
                
            case 'subscribe':
                console.log(`Client ${clientId} subscribed to: ${data.streams}`);
                break;
                
            case 'get_sources':
                ws.send(JSON.stringify({
                    type: 'sources_response',
                    sources: this.getConnectedSources(),
                    activity: this.getSourceActivity()
                }));
                break;
                
            default:
                console.log(`Unknown message type from ${clientId}:`, data.type);
        }
    }

    broadcastToClients(data) {
        const message = JSON.stringify(data);
        let successCount = 0;
        let errorCount = 0;

        this.wss.clients.forEach((client) => {
            if (client.readyState === WebSocket.OPEN) {
                try {
                    client.send(message);
                    successCount++;
                } catch (error) {
                    console.error('Error broadcasting to client:', error);
                    errorCount++;
                }
            }
        });

        if (errorCount > 0) {
            console.log(`Broadcast complete: ${successCount} success, ${errorCount} errors`);
        }
    }

    startConnectionMonitor() {
        setInterval(() => {
            const now = Date.now();
            
            // Check NT8 connection (consider disconnected if no data for 30 seconds)
            if (this.lastNT8Heartbeat && (now - this.lastNT8Heartbeat) > 30000) {
                if (this.nt8Connected) {
                    console.log('NinjaTrader 8 connection lost');
                    this.nt8Connected = false;
                    
                    // Notify all clients
                    this.broadcastToClients({
                        type: 'connection_status',
                        connectedSources: this.getConnectedSources(),
                        timestamp: new Date().toISOString()
                    });
                }
            }
        }, 5000); // Check every 5 seconds
    }

    start() {
        this.server.listen(this.port, () => {
            console.log(`
╔══════════════════════════════════════════════════════════════╗
║                    NEXUS V5.0 NT8 Bridge                    ║
║                  Universal Data Translator                   ║
║                                                              ║
║  🚀 Server running on http://localhost:${this.port}                ║
║  📡 WebSocket server active                                  ║
║  🔗 Multi-broker support enabled                            ║
║                                                              ║
║  Supported Sources:                                          ║
║  • NinjaTrader 8 (POST /api/dashboard-data)                ║
║  • Sierra Chart (POST /api/sierra-data)                    ║
║  • Rithmic (POST /api/rithmic-data)                        ║
║                                                              ║
║  Features:                                                   ║
║  • Real-time data translation                               ║
║  • Universal data format                                     ║
║  • Intelligent source detection                             ║
║  • WebSocket broadcasting                                    ║
╚══════════════════════════════════════════════════════════════╝
            `);
            
            this.startConnectionMonitor();
        });

        // Graceful shutdown
        process.on('SIGINT', () => {
            console.log('\nShutting down NEXUS NT8 Bridge...');
            this.server.close(() => {
                console.log('Server closed');
                process.exit(0);
            });
        });
    }
}

// Start the bridge
const bridge = new NexusNT8Bridge();
bridge.start();

module.exports = NexusNT8Bridge;