# NEXUS V5.0 NinjaTrader 8 Integration Guide

## Overview
The NEXUS V5.0 NinjaTrader 8 addon provides real-time market data streaming, order flow analysis, and advanced trading metrics directly to your NEXUS dashboard.

## Features
- **Real-time Market Data**: Live price, volume, and order flow data
- **MQ Score Analytics**: 6-dimensional market quality scoring
- **Strategy Signals**: 8 built-in trading strategies with confidence levels
- **Order Flow Analysis**: Cumulative delta, volume imbalance, HVN/LVN levels
- **Performance Tracking**: Real-time P&L, win rate, and portfolio heat
- **Position Management**: Live position data and unrealized P&L

## Installation Instructions

### Step 1: Install Required Dependencies
1. Ensure you have NinjaTrader 8.1 or later installed
2. Install Newtonsoft.Json NuGet package:
   - Open NinjaTrader 8
   - Go to Tools → Options → NinjaScript → References
   - Add reference to `Newtonsoft.Json.dll`

### Step 2: Install the NEXUS Addon
1. Copy `NEXUSV5Connector.cs` to your NinjaTrader custom indicators folder:
   ```
   Documents\NinjaTrader 8\bin\Custom\Indicators\
   ```

2. Compile the indicator:
   - Open NinjaTrader 8
   - Press F5 or go to Tools → Compile NinjaScript
   - Verify compilation is successful

### Step 3: Configure the Addon
1. Add the indicator to a chart:
   - Right-click on any chart
   - Select Indicators → NEXUS V5.0 Connector

2. Configure connection settings:
   - **NEXUS API URL**: `http://localhost:4000/api/dashboard-data`
   - **Send Interval**: 1 second (recommended)

### Step 4: Enable Required Permissions
1. Enable NinjaTrader Interface (NTI):
   - Go to Tools → Options → NinjaScript
   - Check "Enable NTI"
   - Set port to 8080

2. Enable real-time data:
   - Ensure your data feed is connected
   - Verify market data permissions

## Configuration Options

### Strategy Settings
The addon includes 8 built-in strategies that can be enabled/disabled:

- **Liquidity Absorption**: Detects large volume absorption at key levels
- **Iceberg Detection**: Identifies hidden large orders
- **Delta Divergence**: Spots order flow divergences
- **Volume Imbalance**: Tracks bid/ask volume imbalances
- **Stop Run Anticipation**: Predicts stop loss hunting
- **HVN Rejection**: High Volume Node rejection trades
- **LVN Breakout**: Low Volume Node breakout trades
- **Momentum Breakout**: Volume-confirmed momentum trades

### MQ Score Dimensions
The Market Quality Score analyzes 6 dimensions:

1. **Liquidity** (20% weight): Market depth and spread efficiency
2. **Efficiency** (18% weight): Price discovery effectiveness
3. **Volatility** (15% weight): Price stability metrics
4. **Momentum** (17% weight): Trend strength indicators
5. **Microstructure** (16% weight): Order flow health
6. **Stability** (14% weight): Market resilience factors

## Data Streaming Format

The addon sends comprehensive market data in JSON format:

```json
{
  "timestamp": "2024-03-15T14:30:45.123Z",
  "systemVersion": "5.0.1",
  "strategies": {
    "LiquidityAbsorption": true,
    "MomentumBreakout": true,
    // ... other strategies
  },
  "performance": {
    "dailyPnL": 2450.75,
    "totalTrades": 12,
    "winningTrades": 8,
    "winRate": 66.67,
    "portfolioHeat": 15.2
  },
  "signals": {
    "activeCount": 3,
    "longSignals": 2,
    "shortSignals": 1,
    "totalConfidence": 78.5
  },
  "market": {
    "currentPrice": 4325.50,
    "cumulativeDelta": 15800,
    "volume": 125000,
    "volatility": 1.25,
    "bid": 4325.25,
    "ask": 4325.50
  },
  "orderFlow": {
    "cumulativeDelta": 15800,
    "bidVolume": 1200000,
    "askVolume": 1135000,
    "absorption": 60.2,
    "imbalance": 4.8,
    "hvnLevels": [4321.25, 4315.50, 4308.75],
    "lvnLevels": [4298.25, 4286.75, 4279.50]
  },
  "mqscore": {
    "Liquidity": {
      "Score": 85.2,
      "Status": "EXCELLENT"
    },
    "OverallScore": 78.5,
    "Grade": "B+",
    "Regime": "TRENDING"
  }
}
```

## Troubleshooting

### Common Issues

**1. Compilation Errors**
- Ensure Newtonsoft.Json reference is added
- Check NinjaTrader version compatibility
- Verify all using statements are correct

**2. Connection Issues**
- Verify NEXUS dashboard is running on localhost:4000
- Check Windows Firewall settings
- Ensure NTI is enabled in NinjaTrader

**3. No Data Streaming**
- Confirm market data feed is connected
- Check indicator is added to an active chart
- Verify real-time data permissions

**4. Performance Issues**
- Reduce send interval if experiencing lag
- Monitor CPU usage during market hours
- Consider disabling unused strategies

### Log Files
Check NinjaTrader logs for detailed error information:
```
Documents\NinjaTrader 8\trace\
```

## Advanced Configuration

### Custom Strategy Development
To add custom strategies, modify the `UpdateStrategySignals()` method:

```csharp
// Example: Custom RSI Strategy
if (activeStrategies["CustomRSI"])
{
    double rsi = RSI(14, 0)[0];
    bool signal = rsi > 70 || rsi < 30;
    if (signal)
    {
        activeSignalCount++;
        if (rsi < 30) longSignals++;
        else shortSignals++;
        totalConfidence += 65;
    }
}
```

### Performance Optimization
For high-frequency trading:
- Increase send interval to reduce network load
- Disable unnecessary strategies
- Use tick-based calculations instead of bar-based

## Support
For technical support and updates:
- GitHub: [NEXUS V5.0 Repository]
- Documentation: [NEXUS V5.0 Docs]
- Community: [NEXUS Trading Discord]

## Version History
- **v5.0.1**: Initial release with full dashboard integration
- **v5.0.2**: Added MQ Score analytics
- **v5.0.3**: Enhanced order flow calculations
- **v5.1.0**: Added custom strategy framework

---

**Note**: This addon requires an active NinjaTrader 8 license and real-time market data subscription. Ensure compliance with your broker's API usage policies.