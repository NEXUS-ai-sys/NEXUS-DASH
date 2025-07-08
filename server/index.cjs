const express = require('express');
const http = require('http');
const WebSocket = require('ws');
const bodyParser = require('body-parser');
const cors = require('cors');

const app = express();
app.use(cors());
app.use(bodyParser.json());

let latestDashboardData = null;

// HTTP API endpoint to receive dashboard data from NinjaTrader
app.post('/api/dashboard-data', (req, res) => {
  latestDashboardData = req.body;
  // Broadcast to all WebSocket clients
  wss.clients.forEach(client => {
    if (client.readyState === WebSocket.OPEN) {
      client.send(JSON.stringify(latestDashboardData));
    }
  });
  res.status(200).json({ status: 'ok' });
});

// HTTP API endpoint to get the latest dashboard data
app.get('/api/dashboard-data', (req, res) => {
  res.json(latestDashboardData || {});
});

const server = http.createServer(app);
const wss = new WebSocket.Server({ server });

wss.on('connection', ws => {
  // Send the latest data immediately on connection
  if (latestDashboardData) {
    ws.send(JSON.stringify(latestDashboardData));
  }
});

const PORT = 4000;
server.listen(PORT, () => {
  console.log(`API & WebSocket server listening on http://localhost:${PORT}`);
}); 