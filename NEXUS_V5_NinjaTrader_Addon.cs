#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Net.Http;
using System.Threading;
using Newtonsoft.Json;
using System.IO;
using System.Net.WebSockets;
using System.Text.Json;
#endregion

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
    public class NEXUSV5Connector : Indicator
    {
        #region Variables
        private HttpClient httpClient;
        private Timer dataTimer;
        private string nexusApiUrl = "http://localhost:4000/api/dashboard-data";
        private bool isConnected = false;
        private DateTime lastDataSend = DateTime.MinValue;
        
        // Market Data Variables
        private double currentPrice = 0;
        private double dailyHigh = 0;
        private double dailyLow = 0;
        private double dailyOpen = 0;
        private long volume = 0;
        private double bid = 0;
        private double ask = 0;
        
        // Order Flow Variables
        private double cumulativeDelta = 0;
        private long bidVolume = 0;
        private long askVolume = 0;
        private List<double> hvnLevels = new List<double>();
        private List<double> lvnLevels = new List<double>();
        
        // Position Variables
        private double unrealizedPnL = 0;
        private int positionQuantity = 0;
        private double averagePrice = 0;
        
        // Performance Variables
        private double dailyPnL = 0;
        private int totalTrades = 0;
        private int winningTrades = 0;
        private int losingTrades = 0;
        private double portfolioHeat = 0;
        
        // MQ Score Variables
        private MQScoreData mqScore = new MQScoreData();
        
        // Strategy Signals
        private Dictionary<string, bool> activeStrategies = new Dictionary<string, bool>();
        private int activeSignalCount = 0;
        private int longSignals = 0;
        private int shortSignals = 0;
        private double totalConfidence = 0;
        #endregion

        public override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"NEXUS V5.0 Professional Trading System - Real-time data connector for NinjaTrader 8";
                Name = "NEXUS V5.0 Connector";
                Calculate = Calculate.OnEachTick;
                IsOverlay = false;
                DisplayInDataBox = true;
                DrawOnPricePanel = false;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = false;
                
                // Initialize strategies
                InitializeStrategies();
            }
            else if (State == State.DataLoaded)
            {
                // Initialize HTTP client
                httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(5);
                
                // Initialize data timer (send data every 1 second)
                dataTimer = new Timer(SendDataToNexus, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
                
                Print("NEXUS V5.0 Connector initialized successfully");
                isConnected = true;
            }
            else if (State == State.Terminated)
            {
                // Cleanup
                dataTimer?.Dispose();
                httpClient?.Dispose();
                isConnected = false;
                Print("NEXUS V5.0 Connector terminated");
            }
        }

        public override void OnBarUpdate()
        {
            if (BarsInProgress != 0 || CurrentBars[0] < 20)
                return;

            // Update basic market data
            currentPrice = Close[0];
            dailyHigh = High[0];
            dailyLow = Low[0];
            dailyOpen = Open[0];
            volume = Volume[0];
            
            // Calculate order flow metrics
            CalculateOrderFlow();
            
            // Calculate MQ Score
            CalculateMQScore();
            
            // Update strategy signals
            UpdateStrategySignals();
            
            // Calculate performance metrics
            CalculatePerformanceMetrics();
        }

        public override void OnMarketData(MarketDataEventArgs marketDataUpdate)
        {
            if (marketDataUpdate.MarketDataType == MarketDataType.Last)
            {
                currentPrice = marketDataUpdate.Price;
            }
            else if (marketDataUpdate.MarketDataType == MarketDataType.Bid)
            {
                bid = marketDataUpdate.Price;
                bidVolume += marketDataUpdate.Volume;
            }
            else if (marketDataUpdate.MarketDataType == MarketDataType.Ask)
            {
                ask = marketDataUpdate.Price;
                askVolume += marketDataUpdate.Volume;
            }
            
            // Calculate cumulative delta
            if (marketDataUpdate.MarketDataType == MarketDataType.Last)
            {
                if (marketDataUpdate.Price >= ask)
                    cumulativeDelta += marketDataUpdate.Volume;
                else if (marketDataUpdate.Price <= bid)
                    cumulativeDelta -= marketDataUpdate.Volume;
            }
        }

        private void InitializeStrategies()
        {
            activeStrategies["LiquidityAbsorption"] = true;
            activeStrategies["IcebergDetection"] = true;
            activeStrategies["DeltaDivergence"] = false;
            activeStrategies["VolumeImbalance"] = true;
            activeStrategies["StopRunAnticipation"] = false;
            activeStrategies["HVNRejection"] = true;
            activeStrategies["LVNBreakout"] = false;
            activeStrategies["MomentumBreakout"] = true;
        }

        private void CalculateOrderFlow()
        {
            if (CurrentBars[0] < 20) return;
            
            // Calculate Volume Profile levels (simplified)
            var volumeProfile = new Dictionary<double, long>();
            
            for (int i = 0; i < Math.Min(20, CurrentBars[0]); i++)
            {
                double priceLevel = Math.Round(Close[i] / 0.25) * 0.25; // Round to tick size
                if (volumeProfile.ContainsKey(priceLevel))
                    volumeProfile[priceLevel] += Volume[i];
                else
                    volumeProfile[priceLevel] = Volume[i];
            }
            
            // Find HVN and LVN levels
            var sortedLevels = volumeProfile.OrderByDescending(x => x.Value).ToList();
            
            hvnLevels.Clear();
            lvnLevels.Clear();
            
            // Top 3 high volume nodes
            for (int i = 0; i < Math.Min(3, sortedLevels.Count); i++)
            {
                hvnLevels.Add(sortedLevels[i].Key);
            }
            
            // Bottom 3 low volume nodes
            for (int i = Math.Max(0, sortedLevels.Count - 3); i < sortedLevels.Count; i++)
            {
                lvnLevels.Add(sortedLevels[i].Key);
            }
        }

        private void CalculateMQScore()
        {
            if (CurrentBars[0] < 50) return;
            
            // Calculate liquidity score (based on volume and spread)
            double avgVolume = 0;
            for (int i = 0; i < 20; i++)
                avgVolume += Volume[i];
            avgVolume /= 20;
            
            double spread = ask - bid;
            mqScore.Liquidity.Score = Math.Min(100, (avgVolume / 1000) * (1 / Math.Max(0.01, spread)));
            mqScore.Liquidity.Status = mqScore.Liquidity.Score > 80 ? "EXCELLENT" : 
                                     mqScore.Liquidity.Score > 60 ? "GOOD" : 
                                     mqScore.Liquidity.Score > 40 ? "FAIR" : "POOR";
            
            // Calculate efficiency score (based on price movement vs volume)
            double priceRange = High[0] - Low[0];
            mqScore.Efficiency.Score = Math.Min(100, (Volume[0] / Math.Max(0.01, priceRange)) / 100);
            mqScore.Efficiency.Status = mqScore.Efficiency.Score > 80 ? "EXCELLENT" : 
                                       mqScore.Efficiency.Score > 60 ? "GOOD" : 
                                       mqScore.Efficiency.Score > 40 ? "FAIR" : "POOR";
            
            // Calculate volatility score
            double atr = 0;
            for (int i = 1; i < Math.Min(15, CurrentBars[0]); i++)
            {
                atr += Math.Max(High[i] - Low[i], 
                       Math.Max(Math.Abs(High[i] - Close[i + 1]), 
                               Math.Abs(Low[i] - Close[i + 1])));
            }
            atr /= 14;
            
            mqScore.Volatility.Score = Math.Min(100, 100 - (atr / currentPrice * 10000)); // Inverse relationship
            mqScore.Volatility.Status = mqScore.Volatility.Score > 80 ? "EXCELLENT" : 
                                       mqScore.Volatility.Score > 60 ? "GOOD" : 
                                       mqScore.Volatility.Score > 40 ? "FAIR" : "POOR";
            
            // Calculate momentum score
            double momentum = (Close[0] - Close[9]) / Close[9] * 100;
            mqScore.Momentum.Score = Math.Min(100, 50 + momentum * 10);
            mqScore.Momentum.Status = mqScore.Momentum.Score > 80 ? "EXCELLENT" : 
                                     mqScore.Momentum.Score > 60 ? "GOOD" : 
                                     mqScore.Momentum.Score > 40 ? "FAIR" : "POOR";
            
            // Calculate microstructure score (based on order flow)
            double imbalance = Math.Abs(bidVolume - askVolume) / Math.Max(1, bidVolume + askVolume) * 100;
            mqScore.Microstructure.Score = Math.Min(100, 100 - imbalance);
            mqScore.Microstructure.Status = mqScore.Microstructure.Score > 80 ? "EXCELLENT" : 
                                           mqScore.Microstructure.Score > 60 ? "GOOD" : 
                                           mqScore.Microstructure.Score > 40 ? "FAIR" : "POOR";
            
            // Calculate stability score
            double volatilityCoeff = atr / currentPrice;
            mqScore.Stability.Score = Math.Min(100, 100 - (volatilityCoeff * 10000));
            mqScore.Stability.Status = mqScore.Stability.Score > 80 ? "EXCELLENT" : 
                                      mqScore.Stability.Score > 60 ? "GOOD" : 
                                      mqScore.Stability.Score > 40 ? "FAIR" : "POOR";
            
            // Calculate overall score
            mqScore.OverallScore = (mqScore.Liquidity.Score * 0.2 +
                                   mqScore.Efficiency.Score * 0.18 +
                                   mqScore.Volatility.Score * 0.15 +
                                   mqScore.Momentum.Score * 0.17 +
                                   mqScore.Microstructure.Score * 0.16 +
                                   mqScore.Stability.Score * 0.14);
            
            // Determine grade
            if (mqScore.OverallScore >= 95) mqScore.Grade = "A+";
            else if (mqScore.OverallScore >= 90) mqScore.Grade = "A";
            else if (mqScore.OverallScore >= 85) mqScore.Grade = "B+";
            else if (mqScore.OverallScore >= 80) mqScore.Grade = "B";
            else if (mqScore.OverallScore >= 75) mqScore.Grade = "C+";
            else if (mqScore.OverallScore >= 70) mqScore.Grade = "C";
            else if (mqScore.OverallScore >= 60) mqScore.Grade = "D";
            else mqScore.Grade = "F";
            
            mqScore.LastUpdate = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        }

        private void UpdateStrategySignals()
        {
            activeSignalCount = 0;
            longSignals = 0;
            shortSignals = 0;
            totalConfidence = 0;
            
            // Liquidity Absorption Strategy
            if (activeStrategies["LiquidityAbsorption"])
            {
                bool signal = Volume[0] > Volume[1] * 1.5 && Math.Abs(Close[0] - Open[0]) < (High[0] - Low[0]) * 0.3;
                if (signal)
                {
                    activeSignalCount++;
                    if (cumulativeDelta > 0) longSignals++;
                    else shortSignals++;
                    totalConfidence += 75;
                }
            }
            
            // Momentum Breakout Strategy
            if (activeStrategies["MomentumBreakout"])
            {
                bool signal = Close[0] > High[1] && Volume[0] > Volume[1] * 1.2;
                if (signal)
                {
                    activeSignalCount++;
                    longSignals++;
                    totalConfidence += 80;
                }
                
                signal = Close[0] < Low[1] && Volume[0] > Volume[1] * 1.2;
                if (signal)
                {
                    activeSignalCount++;
                    shortSignals++;
                    totalConfidence += 80;
                }
            }
            
            // HVN Rejection Strategy
            if (activeStrategies["HVNRejection"])
            {
                foreach (double hvnLevel in hvnLevels)
                {
                    if (Math.Abs(Close[0] - hvnLevel) < 2.0) // Within 2 points of HVN
                    {
                        activeSignalCount++;
                        if (Close[0] > Open[0]) longSignals++;
                        else shortSignals++;
                        totalConfidence += 70;
                        break;
                    }
                }
            }
            
            // Volume Imbalance Strategy
            if (activeStrategies["VolumeImbalance"])
            {
                double imbalance = (askVolume - bidVolume) / Math.Max(1.0, askVolume + bidVolume);
                if (Math.Abs(imbalance) > 0.3) // 30% imbalance
                {
                    activeSignalCount++;
                    if (imbalance > 0) shortSignals++; // More ask volume = bearish
                    else longSignals++; // More bid volume = bullish
                    totalConfidence += 65;
                }
            }
            
            // Calculate average confidence
            if (activeSignalCount > 0)
                totalConfidence /= activeSignalCount;
        }

        private void CalculatePerformanceMetrics()
        {
            // Get account information
            if (Account != null)
            {
                unrealizedPnL = Account.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar);
                dailyPnL = Account.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar);
                
                // Calculate portfolio heat (simplified)
                double accountValue = Account.Get(AccountItem.NetLiquidation, Currency.UsDollar);
                portfolioHeat = Math.Abs(unrealizedPnL) / accountValue * 100;
            }
            
            // Get position information
            if (Position != null)
            {
                positionQuantity = Position.Quantity;
                averagePrice = Position.AveragePrice;
            }
        }

        private async void SendDataToNexus(object state)
        {
            if (!isConnected || httpClient == null)
                return;
                
            try
            {
                var dashboardData = new
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    systemVersion = "5.0.1",
                    strategies = activeStrategies,
                    performance = new
                    {
                        dailyPnL = dailyPnL,
                        totalTrades = totalTrades,
                        winningTrades = winningTrades,
                        losingTrades = losingTrades,
                        winRate = totalTrades > 0 ? (double)winningTrades / totalTrades * 100 : 0,
                        portfolioHeat = portfolioHeat
                    },
                    signals = new
                    {
                        activeCount = activeSignalCount,
                        longSignals = longSignals,
                        shortSignals = shortSignals,
                        totalConfidence = totalConfidence
                    },
                    market = new
                    {
                        currentPrice = currentPrice,
                        cumulativeDelta = cumulativeDelta,
                        volume = volume,
                        volatility = (dailyHigh - dailyLow) / currentPrice * 100,
                        bid = bid,
                        ask = ask,
                        spread = ask - bid
                    },
                    position = new
                    {
                        quantity = positionQuantity,
                        averagePrice = averagePrice,
                        unrealizedPnL = unrealizedPnL
                    },
                    orderFlow = new
                    {
                        cumulativeDelta = cumulativeDelta,
                        bidVolume = bidVolume,
                        askVolume = askVolume,
                        absorption = Math.Min(100, Math.Abs(cumulativeDelta) / Math.Max(1, volume) * 100),
                        imbalance = (askVolume - bidVolume) / Math.Max(1.0, askVolume + bidVolume) * 100,
                        hvnLevels = hvnLevels.ToArray(),
                        lvnLevels = lvnLevels.ToArray()
                    },
                    mqscore = new
                    {
                        Liquidity = mqScore.Liquidity,
                        Efficiency = mqScore.Efficiency,
                        Volatility = mqScore.Volatility,
                        Momentum = mqScore.Momentum,
                        Microstructure = mqScore.Microstructure,
                        Stability = mqScore.Stability,
                        OverallScore = mqScore.OverallScore,
                        Grade = mqScore.Grade,
                        Confidence = totalConfidence,
                        Regime = GetMarketRegime(),
                        LastUpdate = mqScore.LastUpdate
                    }
                };

                string jsonData = JsonConvert.SerializeObject(dashboardData, Formatting.Indented);
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                
                var response = await httpClient.PostAsync(nexusApiUrl, content);
                
                if (response.IsSuccessStatusCode)
                {
                    lastDataSend = DateTime.Now;
                    // Print($"Data sent to NEXUS at {lastDataSend:HH:mm:ss}");
                }
                else
                {
                    Print($"Failed to send data to NEXUS: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Print($"Error sending data to NEXUS: {ex.Message}");
            }
        }

        private string GetMarketRegime()
        {
            if (CurrentBars[0] < 20) return "UNKNOWN";
            
            // Calculate trend strength
            double trendStrength = 0;
            for (int i = 1; i < 10; i++)
            {
                if (Close[i] > Close[i + 1]) trendStrength++;
                else trendStrength--;
            }
            
            // Calculate volatility
            double avgRange = 0;
            for (int i = 0; i < 10; i++)
                avgRange += High[i] - Low[i];
            avgRange /= 10;
            
            double currentRange = High[0] - Low[0];
            
            if (Math.Abs(trendStrength) > 6)
                return "TRENDING";
            else if (currentRange > avgRange * 1.5)
                return "VOLATILE";
            else
                return "RANGING";
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "NEXUS API URL", Description = "URL for NEXUS V5.0 API endpoint", Order = 1, GroupName = "Connection")]
        public string NexusApiUrl
        {
            get { return nexusApiUrl; }
            set { nexusApiUrl = value; }
        }

        [NinjaScriptProperty]
        [Display(Name = "Send Interval (seconds)", Description = "How often to send data to NEXUS", Order = 2, GroupName = "Connection")]
        public int SendInterval { get; set; } = 1;

        [Browsable(false)]
        [XmlIgnore]
        public bool IsConnected
        {
            get { return isConnected; }
        }
        #endregion
    }

    #region Helper Classes
    public class MQDimension
    {
        public double Score { get; set; } = 0;
        public double Weight { get; set; } = 1.0;
        public string Status { get; set; } = "UNKNOWN";
    }

    public class MQScoreData
    {
        public MQDimension Liquidity { get; set; } = new MQDimension { Weight = 0.2 };
        public MQDimension Efficiency { get; set; } = new MQDimension { Weight = 0.18 };
        public MQDimension Volatility { get; set; } = new MQDimension { Weight = 0.15 };
        public MQDimension Momentum { get; set; } = new MQDimension { Weight = 0.17 };
        public MQDimension Microstructure { get; set; } = new MQDimension { Weight = 0.16 };
        public MQDimension Stability { get; set; } = new MQDimension { Weight = 0.14 };
        public double OverallScore { get; set; } = 0;
        public string Grade { get; set; } = "F";
        public string LastUpdate { get; set; } = "";
    }
    #endregion
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
    public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
    {
        private NEXUSV5Connector[] cacheNEXUSV5Connector;
        public NEXUSV5Connector NEXUSV5Connector(string nexusApiUrl)
        {
            return NEXUSV5Connector(Input, nexusApiUrl);
        }

        public NEXUSV5Connector NEXUSV5Connector(ISeries<double> input, string nexusApiUrl)
        {
            if (cacheNEXUSV5Connector != null)
                for (int idx = 0; idx < cacheNEXUSV5Connector.Length; idx++)
                    if (cacheNEXUSV5Connector[idx] != null && cacheNEXUSV5Connector[idx].NexusApiUrl == nexusApiUrl && cacheNEXUSV5Connector[idx].EqualsInput(input))
                        return cacheNEXUSV5Connector[idx];
            return CacheIndicator<NEXUSV5Connector>(new NEXUSV5Connector(){ NexusApiUrl = nexusApiUrl }, input, ref cacheNEXUSV5Connector);
        }
    }
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
    public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
    {
        public Indicators.NEXUSV5Connector NEXUSV5Connector(string nexusApiUrl)
        {
            return indicator.NEXUSV5Connector(Input, nexusApiUrl);
        }

        public Indicators.NEXUSV5Connector NEXUSV5Connector(ISeries<double> input, string nexusApiUrl)
        {
            return indicator.NEXUSV5Connector(input, nexusApiUrl);
        }
    }
}

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
    {
        public Indicators.NEXUSV5Connector NEXUSV5Connector(string nexusApiUrl)
        {
            return indicator.NEXUSV5Connector(Input, nexusApiUrl);
        }

        public Indicators.NEXUSV5Connector NEXUSV5Connector(ISeries<double> input, string nexusApiUrl)
        {
            return indicator.NEXUSV5Connector(input, nexusApiUrl);
        }
    }
}

#endregion