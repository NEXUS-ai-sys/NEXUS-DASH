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
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Net.Http;
#endregion

//This namespace holds Strategies in this folder and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Strategies
{
    public class NEXUSV5NinjaTrader : Strategy
    {
        #region Data Structures
        
        public class TradeSignal
        {
            public int Direction { get; set; }          // 1 = Long, -1 = Short, 0 = No Signal
            public double Confidence { get; set; }       // 0.0 to 1.0
            public string Strategy { get; set; }         // Strategy name
            public double EntryPrice { get; set; }
            public double StopLoss { get; set; }
            public double Target { get; set; }
            public string Reason { get; set; }
        }

        public class LiquidationEvent
        {
            public double Price { get; set; }
            public double Volume { get; set; }
            public double Intensity { get; set; }
            public int Direction { get; set; } // 1 = long liquidation, -1 = short liquidation
            public double Confidence { get; set; }
            public DateTime Timestamp { get; set; }
            public string Reason { get; set; }
        }

        public class MQDimension
        {
            public double Score { get; set; }        // 0-100 score for this dimension
            public double Weight { get; set; }       // Weight in overall calculation
            public double RawValue { get; set; }     // Raw calculated value
            public string Status { get; set; }       // "Excellent", "Good", "Fair", "Poor"
        }

        public class MQScore6D
        {
            public MQDimension Liquidity { get; set; }
            public MQDimension Efficiency { get; set; }
            public MQDimension Volatility { get; set; }
            public MQDimension Momentum { get; set; }
            public MQDimension Microstructure { get; set; }
            public MQDimension Stability { get; set; }
            
            public double OverallScore { get; set; }
            public double Confidence { get; set; }
            public string Regime { get; set; }
            public DateTime LastUpdate { get; set; }
        }

        public class SignalAggregator
        {
            public List<TradeSignal> Signals { get; set; }
            public int LongSignals { get; set; }
            public int ShortSignals { get; set; }
            public double TotalConfidence { get; set; }
            
            public SignalAggregator()
            {
                Signals = new List<TradeSignal>();
                LongSignals = 0;
                ShortSignals = 0;
                TotalConfidence = 0.0;
            }
            
            public void AddSignal(TradeSignal signal)
            {
                if (signal.Direction != 0)
                {
                    Signals.Add(signal);
                    if (signal.Direction == 1) LongSignals++;
                    if (signal.Direction == -1) ShortSignals++;
                    TotalConfidence += signal.Confidence;
                }
            }
            
            public void Clear()
            {
                Signals.Clear();
                LongSignals = 0;
                ShortSignals = 0;
                TotalConfidence = 0.0;
            }
        }

        #endregion

        #region Variables

        // Strategy enable/disable flags
        private bool enableLiquidityAbsorption = true;
        private bool enableIcebergDetection = true;
        private bool enableDeltaDivergence = true;
        private bool enableVolumeImbalance = true;
        private bool enableStopRunAnticipation = true;
        private bool enableHVNRejection = true;
        private bool enableLVNBreakout = true;
        private bool enableMomentumBreakout = true;
        private bool enableCumulativeDelta = true;
        private bool enableLiquidityTraps = true;
        private bool enableLiquidationDetection = true;

        // Risk management
        private double maxDailyLoss = 1000.0;
        private double dailyProfitTarget = 2000.0;
        private double maxPortfolioHeat = 2.0;
        private double positionSizeRisk = 1.0;
        private double maxDrawdownLimit = 5.0;

        // Order flow data
        private double cumulativeDelta = 0.0;
        private List<double> hvnLevels = new List<double>();
        private List<double> lvnLevels = new List<double>();
        private List<LiquidationEvent> recentLiquidations = new List<LiquidationEvent>();
        
        // Performance tracking
        private double dailyPnL = 0.0;
        private int totalTrades = 0;
        private int winningTrades = 0;
        private int losingTrades = 0;
        private double portfolioHeat = 0.0;

        // ML Integration - File paths for Python models
        private string pythonPath = @"C:\Users\Nexus\AppData\Local\Microsoft\WindowsApps\PythonSoftwareFoundation.Python.3.11_qbz5n2kfra8p0\python.exe";
        private string modelsPath = @"C:\Users\Nexus\Documents\script for train model\";

        #endregion

        #region NinjaScript Parameters

        [NinjaScriptProperty]
        [Display(Name = "Enable Auto Trading", Order = 1, GroupName = "System Controls")]
        public bool EnableAutoTrading { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Trade Quantity", Order = 2, GroupName = "System Controls")]
        public int TradeQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Max Daily Trades", Order = 3, GroupName = "System Controls")]
        public int MaxDailyTrades { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable Detailed Logging", Order = 4, GroupName = "System Controls")]
        public bool EnableDetailedLogging { get; set; }

        [NinjaScriptProperty]
        [Range(100, 10000)]
        [Display(Name = "Max Daily Loss", Order = 5, GroupName = "Risk Management")]
        public double MaxDailyLoss
        {
            get { return maxDailyLoss; }
            set { maxDailyLoss = value; }
        }

        [NinjaScriptProperty]
        [Range(100, 20000)]
        [Display(Name = "Daily Profit Target", Order = 6, GroupName = "Risk Management")]
        public double DailyProfitTarget
        {
            get { return dailyProfitTarget; }
            set { dailyProfitTarget = value; }
        }

        [NinjaScriptProperty]
        [Range(0.5, 10.0)]
        [Display(Name = "Max Portfolio Heat (%)", Order = 7, GroupName = "Risk Management")]
        public double MaxPortfolioHeat
        {
            get { return maxPortfolioHeat; }
            set { maxPortfolioHeat = value; }
        }

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Position Size Risk (%)", Order = 8, GroupName = "Risk Management")]
        public double PositionSizeRisk
        {
            get { return positionSizeRisk; }
            set { positionSizeRisk = value; }
        }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"NEXUS V5.0 for NinjaTrader - Professional multi-strategy order flow trading system with ML integration";
                Name = "NEXUSV5NinjaTrader";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 50;
                IsInstantiatedOnEachOptimizationIteration = true;
                
                // Initialize parameter defaults
                EnableAutoTrading = true;
                TradeQuantity = 1;
                MaxDailyTrades = 20;
                EnableDetailedLogging = true;
            }
            else if (State == State.Configure)
            {
                // Add Order Flow data types if available
                try
                {
                    AddDataSeries(Data.BarsPeriodType.Tick, 1);
                }
                catch (Exception ex)
                {
                    Print("Error configuring data series: " + ex.Message);
                }
            }
            else if (State == State.DataLoaded)
            {
                // Initialize collections to prevent null reference exceptions
                if (hvnLevels == null) hvnLevels = new List<double>();
                if (lvnLevels == null) lvnLevels = new List<double>();
                if (recentLiquidations == null) recentLiquidations = new List<LiquidationEvent>();
            }
        }

        protected override void OnBarUpdate()
        {
            try
            {
                // Skip if not enough bars for analysis or data not ready
                if (CurrentBar < BarsRequiredToTrade || 
                    BarsInProgress != 0 || 
                    Close == null || 
                    Volumes == null || 
                    Volumes[0] == null)
                    return;

                // Update order flow data
                UpdateOrderFlowData();

                // Check trading hours and risk limits
                if (!IsWithinTradingHours() || !CheckRiskLimits()) return;

                // Initialize signal aggregator
                SignalAggregator aggregator = new SignalAggregator();

                // Run all enabled strategies
                RunTradingStrategies(aggregator);

                // Apply ML-enhanced decision making
                ProcessMLEnhancedSignals(aggregator);

                // Export dashboard data (only every 10 bars to reduce load)
                if (CurrentBar % 10 == 0)
                    ExportDashboardData(aggregator);
            }
            catch (Exception ex)
            {
                if (EnableDetailedLogging)
                    Print("Error in OnBarUpdate: " + ex.Message);
            }
        }

        #region Strategy Implementations

        private void RunTradingStrategies(SignalAggregator aggregator)
        {
            if (enableLiquidityAbsorption)
            {
                var signal = CheckLiquidityAbsorption();
                aggregator.AddSignal(signal);
            }

            if (enableIcebergDetection)
            {
                var signal = CheckIcebergDetection();
                aggregator.AddSignal(signal);
            }

            if (enableDeltaDivergence)
            {
                var signal = CheckDeltaDivergence();
                aggregator.AddSignal(signal);
            }

            if (enableVolumeImbalance)
            {
                var signal = CheckVolumeImbalance();
                aggregator.AddSignal(signal);
            }

            if (enableStopRunAnticipation)
            {
                var signal = CheckStopRunAnticipation();
                aggregator.AddSignal(signal);
            }

            if (enableHVNRejection)
            {
                var signal = CheckHVNRejection();
                aggregator.AddSignal(signal);
            }

            if (enableLVNBreakout)
            {
                var signal = CheckLVNBreakout();
                aggregator.AddSignal(signal);
            }

            if (enableMomentumBreakout)
            {
                var signal = CheckMomentumBreakout();
                aggregator.AddSignal(signal);
            }

            if (enableCumulativeDelta)
            {
                var signal = CheckCumulativeDeltaTrend();
                aggregator.AddSignal(signal);
            }

            if (enableLiquidityTraps)
            {
                var signal = CheckLiquidityTraps();
                aggregator.AddSignal(signal);
            }

            if (enableLiquidationDetection)
            {
                DetectLiquidations();
                var signal = CheckLiquidationSignals();
                aggregator.AddSignal(signal);
            }
        }

        private TradeSignal CheckLiquidityAbsorption()
        {
            var signal = new TradeSignal { Strategy = "Liquidity Absorption" };
            
            try
            {
                if (CurrentBar < 2 || Volumes == null || Volumes[0] == null || Close == null || Low == null) 
                    return signal;

                // Get volume data - using Volumes array for NinjaTrader
                if (Volumes[0].Count <= 1) return signal;
                
                double currentVolume = Volumes[0][0];
                double prevVolume = Volumes[0][1];
                
                if (currentVolume <= 0 || prevVolume <= 0) return signal;

                // Simplified logic for demonstration
                if (currentVolume > prevVolume * 2.0)
                {
                    signal.Direction = 1;
                    signal.Confidence = 0.7;
                    signal.EntryPrice = Close[0];
                    signal.StopLoss = Low[0] - 2 * TickSize;
                    signal.Target = Close[0] + (Close[0] - signal.StopLoss) * 1.5;
                    signal.Reason = "High volume absorption detected";
                }
            }
            catch (Exception ex)
            {
                if (EnableDetailedLogging)
                    Print("Error in CheckLiquidityAbsorption: " + ex.Message);
            }

            return signal;
        }

        private TradeSignal CheckIcebergDetection()
        {
            var signal = new TradeSignal { Strategy = "Iceberg Detection" };
            
            if (CurrentBar < 5) return signal;

            double range = High[0] - Low[0];
            double avgRange = 0;
            for (int i = 1; i <= 5; i++)
                avgRange += High[i] - Low[i];
            avgRange /= 5.0;

            if (Volumes[0][0] > 100 && range < avgRange * 0.6)
            {
                signal.Direction = 1;
                signal.Confidence = 0.6;
                signal.EntryPrice = Close[0];
                signal.StopLoss = Low[0] - 2 * TickSize;
                signal.Target = Close[0] + (Close[0] - signal.StopLoss) * 1.5;
                signal.Reason = "Potential iceberg order detected";
            }

            return signal;
        }

        private TradeSignal CheckDeltaDivergence()
        {
            var signal = new TradeSignal { Strategy = "Delta Divergence" };
            
            if (CurrentBar < 20) return signal;

            // Simplified delta divergence logic
            double currentHigh = High[0];
            double prevHigh = High[1];
            
            // Would need actual delta calculations here
            if (currentHigh > prevHigh)
            {
                signal.Direction = -1;
                signal.Confidence = 0.7;
                signal.EntryPrice = Close[0];
                signal.StopLoss = High[0] + 2 * TickSize;
                signal.Target = Close[0] - (signal.StopLoss - Close[0]) * 1.5;
                signal.Reason = "Bearish delta divergence";
            }

            return signal;
        }

        private TradeSignal CheckVolumeImbalance()
        {
            var signal = new TradeSignal { Strategy = "Volume Imbalance" };
            
            // Simplified volume imbalance logic
            if (Volumes[0][0] > Volumes[0][1] * 1.5)
            {
                signal.Direction = Close[0] > Open[0] ? 1 : -1;
                signal.Confidence = 0.65;
                signal.EntryPrice = Close[0];
                
                if (signal.Direction == 1)
                {
                    signal.StopLoss = Low[0] - 2 * TickSize;
                    signal.Target = Close[0] + (Close[0] - signal.StopLoss) * 1.5;
                    signal.Reason = "Strong buying volume imbalance";
                }
                else
                {
                    signal.StopLoss = High[0] + 2 * TickSize;
                    signal.Target = Close[0] - (signal.StopLoss - Close[0]) * 1.5;
                    signal.Reason = "Strong selling volume imbalance";
                }
            }

            return signal;
        }

        private TradeSignal CheckStopRunAnticipation()
        {
            var signal = new TradeSignal { Strategy = "Stop Run Anticipation" };
            
            if (CurrentBar < 5) return signal;

            if (High[0] > High[1] + 2 * TickSize && Volumes[0][0] > Volumes[0][1] * 1.5)
            {
                signal.Direction = -1;
                signal.Confidence = 0.6;
                signal.EntryPrice = Close[0];
                signal.StopLoss = High[0] + 2 * TickSize;
                signal.Target = Close[0] - (signal.StopLoss - Close[0]) * 1.5;
                signal.Reason = "Stop run above high anticipated";
            }

            return signal;
        }

        private TradeSignal CheckHVNRejection()
        {
            var signal = new TradeSignal { Strategy = "HVN Rejection" };
            
            foreach (double hvn in hvnLevels)
            {
                if (Math.Abs(Close[0] - hvn) < 2 * TickSize)
                {
                    signal.Direction = Close[0] < hvn ? 1 : -1;
                    signal.Confidence = 0.6;
                    signal.EntryPrice = Close[0];
                    
                    if (signal.Direction == 1)
                    {
                        signal.StopLoss = Low[0] - 2 * TickSize;
                        signal.Target = Close[0] + (Close[0] - signal.StopLoss) * 1.5;
                    }
                    else
                    {
                        signal.StopLoss = High[0] + 2 * TickSize;
                        signal.Target = Close[0] - (signal.StopLoss - Close[0]) * 1.5;
                    }
                    signal.Reason = "HVN rejection at level";
                    break;
                }
            }

            return signal;
        }

        private TradeSignal CheckLVNBreakout()
        {
            var signal = new TradeSignal { Strategy = "LVN Breakout" };
            
            foreach (double lvn in lvnLevels)
            {
                if (Math.Abs(Close[0] - lvn) < 2 * TickSize)
                {
                    signal.Direction = Close[0] > lvn ? 1 : -1;
                    signal.Confidence = 0.6;
                    signal.EntryPrice = Close[0];
                    
                    if (signal.Direction == 1)
                    {
                        signal.StopLoss = Low[0] - 2 * TickSize;
                        signal.Target = Close[0] + (Close[0] - signal.StopLoss) * 1.5;
                    }
                    else
                    {
                        signal.StopLoss = High[0] + 2 * TickSize;
                        signal.Target = Close[0] - (signal.StopLoss - Close[0]) * 1.5;
                    }
                    signal.Reason = "LVN breakout at level";
                    break;
                }
            }

            return signal;
        }

        private TradeSignal CheckMomentumBreakout()
        {
            var signal = new TradeSignal { Strategy = "Momentum Breakout" };
            
            if (CurrentBar < 5) return signal;

            double recentHigh = High[1];
            for (int i = 2; i <= 5; i++)
                recentHigh = Math.Max(recentHigh, High[i]);

            if (High[0] > recentHigh && Volumes[0][0] > Volumes[0][1] * 1.2)
            {
                signal.Direction = 1;
                signal.Confidence = 0.7;
                signal.EntryPrice = Close[0];
                signal.StopLoss = Low[0] - 2 * TickSize;
                signal.Target = Close[0] + (Close[0] - signal.StopLoss) * 1.5;
                signal.Reason = "Momentum breakout above range";
            }

            return signal;
        }

        private TradeSignal CheckCumulativeDeltaTrend()
        {
            var signal = new TradeSignal { Strategy = "Cumulative Delta Trend" };
            
            // Simplified cumulative delta logic
            if (cumulativeDelta > 0)
            {
                signal.Direction = 1;
                signal.Confidence = 0.6;
                signal.EntryPrice = Close[0];
                signal.StopLoss = Low[0] - 2 * TickSize;
                signal.Target = Close[0] + (Close[0] - signal.StopLoss) * 1.5;
                signal.Reason = "Positive cumulative delta trend";
            }
            else if (cumulativeDelta < 0)
            {
                signal.Direction = -1;
                signal.Confidence = 0.6;
                signal.EntryPrice = Close[0];
                signal.StopLoss = High[0] + 2 * TickSize;
                signal.Target = Close[0] - (signal.StopLoss - Close[0]) * 1.5;
                signal.Reason = "Negative cumulative delta trend";
            }

            return signal;
        }

        private TradeSignal CheckLiquidityTraps()
        {
            var signal = new TradeSignal { Strategy = "Liquidity Traps" };
            
            if (CurrentBar < 5) return signal;

            double avgVolume = 0;
            for (int i = 1; i <= 5; i++)
                avgVolume += Volumes[0][i];
            avgVolume /= 5.0;

            double range = High[0] - Low[0];
            if (Volumes[0][0] > avgVolume * 2 && range < 3 * TickSize)
            {
                signal.Direction = Close[0] > Open[0] ? -1 : 1;
                signal.Confidence = 0.6;
                signal.EntryPrice = Close[0];
                
                if (signal.Direction == 1)
                {
                    signal.StopLoss = Low[0] - 2 * TickSize;
                    signal.Target = Close[0] + (Close[0] - signal.StopLoss) * 1.5;
                }
                else
                {
                    signal.StopLoss = High[0] + 2 * TickSize;
                    signal.Target = Close[0] - (signal.StopLoss - Close[0]) * 1.5;
                }
                signal.Reason = "Possible liquidity trap reversal";
            }

            return signal;
        }

        #endregion

        #region Liquidation Detection

        private void DetectLiquidations()
        {
            if (CurrentBar < 5) return;

            double avgVolume = 0;
            for (int i = 1; i <= 5; i++)
                avgVolume += Volumes[0][i];
            avgVolume /= 5.0;

            double volumeRatio = Volumes[0][0] / (avgVolume + 1e-6);
            if (volumeRatio < 2.0) return;

            double priceRange = High[0] - Low[0];
            double avgRange = 0;
            for (int i = 1; i <= 5; i++)
                avgRange += High[i] - Low[i];
            avgRange /= 5.0;

            double priceMomentum = Close[0] - Close[3];
            double prevMomentum = Close[3] - Close[6];

            bool momentumReversal = (priceMomentum * prevMomentum < 0) && 
                                   (Math.Abs(priceMomentum) < Math.Abs(prevMomentum) * 0.5);
            bool priceStall = priceRange < avgRange * 0.6;

            if (momentumReversal || priceStall)
            {
                var liquidation = new LiquidationEvent
                {
                    Price = Close[0],
                    Volume = Volumes[0][0],
                    Intensity = volumeRatio * (1.0 + Math.Abs(priceMomentum / TickSize)),
                    Direction = priceMomentum > 0 ? -1 : 1,
                    Confidence = Math.Min(0.9, volumeRatio / 5.0),
                    Timestamp = Time[0],
                    Reason = volumeRatio > 3.0 ? "High volume spike with price stall" : "Volume surge liquidation"
                };

                recentLiquidations.Add(liquidation);
                
                // Keep only last 100 events
                if (recentLiquidations.Count > 100)
                    recentLiquidations.RemoveAt(0);

                if (EnableDetailedLogging)
                {
                    Print("LIQUIDATION DETECTED - Price: " + liquidation.Price.ToString("F2") + ", Volume: " + liquidation.Volume.ToString("F0") + ", " +
                          "Intensity: " + liquidation.Intensity.ToString("F2") + ", Direction: " + (liquidation.Direction == 1 ? "Long Squeeze" : "Short Squeeze") + ", " +
                          "Reason: " + liquidation.Reason);
                }
            }
        }

        private TradeSignal CheckLiquidationSignals()
        {
            var signal = new TradeSignal { Strategy = "Liquidation Detection" };

            if (!recentLiquidations.Any()) return signal;

            var currentTime = Time[0];
            var currentPrice = Close[0];

            var recentEvent = recentLiquidations
                .Where(e => (currentTime - e.Timestamp).TotalMinutes < 5)
                .Where(e => Math.Abs(currentPrice - e.Price) < 10 * TickSize && e.Intensity > 2.0)
                .FirstOrDefault();

            if (recentEvent != null)
            {
                signal.Direction = -recentEvent.Direction;
                signal.Confidence = Math.Min(0.9, recentEvent.Intensity / 5.0);
                signal.EntryPrice = currentPrice;

                if (signal.Direction == 1)
                {
                    signal.StopLoss = currentPrice - (8 * TickSize);
                    signal.Target = currentPrice + (15 * TickSize);
                    signal.Reason = "Long liquidation bounce opportunity";
                }
                else
                {
                    signal.StopLoss = currentPrice + (8 * TickSize);
                    signal.Target = currentPrice - (15 * TickSize);
                    signal.Reason = "Short liquidation drop opportunity";
                }
            }

            return signal;
        }

        #endregion

        #region ML Integration

        private void ProcessMLEnhancedSignals(SignalAggregator aggregator)
        {
            if (!aggregator.Signals.Any()) return;

            // Extract market features for ML models
            var features = ExtractMarketFeatures();
            
            // Get ML predictions (simplified - would call Python scripts)
            var mlPredictions = GetMLPredictions(features, aggregator);

            // Execute trades based on ML-weighted confidence
            if (mlPredictions.OverallConfidence > 0.6)
            {
                double weightedLongConfidence = 0.0;
                double weightedShortConfidence = 0.0;

                for (int i = 0; i < aggregator.Signals.Count; i++)
                {
                    var signal = aggregator.Signals[i];
                    double weight = i < mlPredictions.StrategyWeights.Count ? mlPredictions.StrategyWeights[i] : 1.0;
                    
                    if (signal.Direction == 1)
                        weightedLongConfidence += signal.Confidence * weight;
                    else if (signal.Direction == -1)
                        weightedShortConfidence += signal.Confidence * weight;
                }

                if (weightedLongConfidence > weightedShortConfidence && weightedLongConfidence > 0.6)
                {
                    ExecuteLongTrade(aggregator);
                }
                else if (weightedShortConfidence > weightedLongConfidence && weightedShortConfidence > 0.6)
                {
                    ExecuteShortTrade(aggregator);
                }
            }
        }

        private MarketFeatures ExtractMarketFeatures()
        {
            return new MarketFeatures
            {
                CurrentPrice = Close[0],
                Volume = Volumes[0][0],
                Volatility = CalculateVolatility(20),
                Momentum = (Close[0] - Close[10]) / Close[10],
                TimeOfDay = Time[0].Hour + Time[0].Minute / 60.0,
                DayOfWeek = (int)Time[0].DayOfWeek,
                CumulativeDelta = cumulativeDelta
            };
        }

        private MLPredictions GetMLPredictions(MarketFeatures features, SignalAggregator aggregator)
        {
            try
            {
                // Call Python ML models
                return CallPythonMLModels(features, aggregator);
            }
            catch (Exception ex)
            {
                if (EnableDetailedLogging)
                    Print("Error calling Python ML models: " + ex.Message + ". Using fallback predictions.");
                
                // Fallback to simple predictions if Python call fails
                return new MLPredictions
                {
                    MarketRegime = features.CumulativeDelta > 0 ? "Bullish" : "Bearish",
                    VolatilityForecast = features.Volatility * 1.1,
                    PriceDirection = features.Momentum > 0 ? 0.7 : -0.7,
                    OverallConfidence = Math.Min(1.0, aggregator.TotalConfidence / aggregator.Signals.Count),
                    StrategyWeights = Enumerable.Repeat(1.0, aggregator.Signals.Count).ToList()
                };
            }
        }
        
        private MLPredictions CallPythonMLModels(MarketFeatures features, SignalAggregator aggregator)
        {
            // Create temporary JSON file with market features
            string tempInputFile = Path.Combine(Path.GetTempPath(), "nexus_input_" + DateTime.Now.Ticks + ".json");
            string tempOutputFile = Path.Combine(Path.GetTempPath(), "nexus_output_" + DateTime.Now.Ticks + ".json");
            
            try
            {
                // Write features to JSON file
                WriteFeaturesToFile(features, aggregator, tempInputFile);
                
                // Call Python script (try enhanced version first, fallback to basic)
                string enhancedScript = Path.Combine(modelsPath, "nexus_ml_predictor_enhanced.py");
                string basicScript = Path.Combine(modelsPath, "nexus_ml_predictor.py");
                string pythonScript = File.Exists(enhancedScript) ? enhancedScript : basicScript;
                string arguments = "\"" + tempInputFile + "\" \"" + tempOutputFile + "\"";
                
                var processInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pythonPath,
                    Arguments = "\"" + pythonScript + "\" " + arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                
                using (var process = System.Diagnostics.Process.Start(processInfo))
                {
                    process.WaitForExit(5000); // 5 second timeout
                    
                    if (process.ExitCode == 0 && File.Exists(tempOutputFile))
                    {
                        // Read Python predictions
                        return ReadPredictionsFromFile(tempOutputFile);
                    }
                    else
                    {
                        if (EnableDetailedLogging)
                        {
                            string error = process.StandardError.ReadToEnd();
                            Print("Python script error: " + error);
                        }
                        throw new Exception("Python script execution failed");
                    }
                }
            }
            finally
            {
                // Clean up temporary files
                try
                {
                    if (File.Exists(tempInputFile)) File.Delete(tempInputFile);
                    if (File.Exists(tempOutputFile)) File.Delete(tempOutputFile);
                }
                catch { /* Ignore cleanup errors */ }
            }
        }
        
        private void WriteFeaturesToFile(MarketFeatures features, SignalAggregator aggregator, string filePath)
        {
            StringBuilder json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"features\": {");
            json.AppendLine("    \"current_price\": " + features.CurrentPrice + ",");
            json.AppendLine("    \"volume\": " + features.Volume + ",");
            json.AppendLine("    \"volatility\": " + features.Volatility + ",");
            json.AppendLine("    \"momentum\": " + features.Momentum + ",");
            json.AppendLine("    \"time_of_day\": " + features.TimeOfDay + ",");
            json.AppendLine("    \"day_of_week\": " + features.DayOfWeek + ",");
            json.AppendLine("    \"cumulative_delta\": " + features.CumulativeDelta);
            json.AppendLine("  },");
            json.AppendLine("  \"signals\": {");
            json.AppendLine("    \"total_signals\": " + aggregator.Signals.Count + ",");
            json.AppendLine("    \"long_signals\": " + aggregator.LongSignals + ",");
            json.AppendLine("    \"short_signals\": " + aggregator.ShortSignals + ",");
            json.AppendLine("    \"total_confidence\": " + aggregator.TotalConfidence);
            json.AppendLine("  },");
            json.AppendLine("  \"market_data\": {");
            
            // Add recent price history
            json.AppendLine("    \"price_history\": [");
            for (int i = 0; i < Math.Min(20, CurrentBar + 1); i++)
            {
                if (i < Close.Count)
                {
                    json.Append("      " + Close[i]);
                    if (i < Math.Min(19, CurrentBar) && i < Close.Count - 1) json.Append(",");
                    json.AppendLine();
                }
            }
            json.AppendLine("    ],");
            
            // Add recent volume history
            json.AppendLine("    \"volume_history\": [");
            for (int i = 0; i < Math.Min(20, CurrentBar + 1); i++)
            {
                if (i < Volumes[0].Count)
                {
                    json.Append("      " + Volumes[0][i]);
                    if (i < Math.Min(19, CurrentBar) && i < Volumes[0].Count - 1) json.Append(",");
                    json.AppendLine();
                }
            }
            json.AppendLine("    ]");
            json.AppendLine("  }");
            json.AppendLine("}");
            
            File.WriteAllText(filePath, json.ToString());
        }
        
        private MLPredictions ReadPredictionsFromFile(string filePath)
        {
            string jsonContent = File.ReadAllText(filePath);
            
            // Parse JSON manually (simple parser for specific format)
            var predictions = new MLPredictions();
            
            // Extract values using simple string parsing
            predictions.MarketRegime = ExtractJsonValue(jsonContent, "market_regime");
            predictions.VolatilityForecast = double.Parse(ExtractJsonValue(jsonContent, "volatility_forecast") ?? "0");
            predictions.PriceDirection = double.Parse(ExtractJsonValue(jsonContent, "price_direction") ?? "0");
            predictions.OverallConfidence = double.Parse(ExtractJsonValue(jsonContent, "overall_confidence") ?? "0");
            
            // Extract strategy weights array
            string weightsStr = ExtractJsonArray(jsonContent, "strategy_weights");
            if (!string.IsNullOrEmpty(weightsStr))
            {
                string[] weights = weightsStr.Split(',');
                foreach (string weight in weights)
                {
                    double w;
                    if (double.TryParse(weight.Trim(), out w))
                        predictions.StrategyWeights.Add(w);
                }
            }
            
            return predictions;
        }
        
        private string ExtractJsonValue(string json, string key)
        {
            string pattern = "\"" + key + "\":\\s*\"?([^,}\"]+)\"?";
            var match = Regex.Match(json, pattern);
            return match.Success ? match.Groups[1].Value.Trim('"') : null;
        }
        
        private string ExtractJsonArray(string json, string key)
        {
            string pattern = "\"" + key + "\":\\s*\\[([^\\]]+)\\]";
            var match = Regex.Match(json, pattern);
            return match.Success ? match.Groups[1].Value : null;
        }

        #endregion

        #region Execution and Risk Management

        private void ExecuteLongTrade(SignalAggregator aggregator)
        {
            if (!EnableAutoTrading || Position.MarketPosition == MarketPosition.Long) return;

            var bestSignal = aggregator.Signals.Where(s => s.Direction == 1).OrderByDescending(s => s.Confidence).FirstOrDefault();
            if (bestSignal == null) return;

            if (!CheckRiskLimits()) return;

            EnterLong(TradeQuantity, "NEXUS_LONG");
            
            if (EnableDetailedLogging)
            {
                Print("LONG ENTRY EXECUTED - Strategy: " + bestSignal.Strategy + ", Confidence: " + bestSignal.Confidence.ToString("F2") + ", Reason: " + bestSignal.Reason);
            }

            SetStopLoss("NEXUS_LONG", CalculationMode.Price, bestSignal.StopLoss, false);
            SetProfitTarget("NEXUS_LONG", CalculationMode.Price, bestSignal.Target);
        }

        private void ExecuteShortTrade(SignalAggregator aggregator)
        {
            if (!EnableAutoTrading || Position.MarketPosition == MarketPosition.Short) return;

            var bestSignal = aggregator.Signals.Where(s => s.Direction == -1).OrderByDescending(s => s.Confidence).FirstOrDefault();
            if (bestSignal == null) return;

            if (!CheckRiskLimits()) return;

            EnterShort(TradeQuantity, "NEXUS_SHORT");
            
            if (EnableDetailedLogging)
            {
                Print("SHORT ENTRY EXECUTED - Strategy: " + bestSignal.Strategy + ", Confidence: " + bestSignal.Confidence.ToString("F2") + ", Reason: " + bestSignal.Reason);
            }

            SetStopLoss("NEXUS_SHORT", CalculationMode.Price, bestSignal.StopLoss, false);
            SetProfitTarget("NEXUS_SHORT", CalculationMode.Price, bestSignal.Target);
        }

        private bool CheckRiskLimits()
        {
            // Check daily loss limit
            if (dailyPnL < -MaxDailyLoss)
            {
                if (EnableDetailedLogging)
                    Print("Daily loss limit reached - trading disabled");
                return false;
            }

            // Check daily profit target
            if (dailyPnL > DailyProfitTarget)
            {
                if (EnableDetailedLogging)
                    Print("Daily profit target reached - trading disabled");
                return false;
            }

            // Check max trades
            if (totalTrades >= MaxDailyTrades)
            {
                if (EnableDetailedLogging)
                    Print("Maximum daily trades reached");
                return false;
            }

            return true;
        }

        #endregion

        #region Utility Functions

        private void UpdateOrderFlowData()
        {
            try
            {
                // Update cumulative delta (simplified)
                if (CurrentBar > 0 && Close != null && Volumes != null && Volumes[0] != null)
                {
                    double delta = (Close[0] > Close[1]) ? Volumes[0][0] : -Volumes[0][0];
                    cumulativeDelta += delta;
                }

                // Update volume profile levels (simplified)
                UpdateVolumeProfile();
            }
            catch (Exception ex)
            {
                if (EnableDetailedLogging)
                    Print("Error in UpdateOrderFlowData: " + ex.Message);
            }
        }

        private void UpdateVolumeProfile()
        {
            try
            {
                // Simplified volume profile calculation
                if (hvnLevels == null) hvnLevels = new List<double>();
                if (lvnLevels == null) lvnLevels = new List<double>();
                
                hvnLevels.Clear();
                lvnLevels.Clear();

                if (CurrentBar < 50 || Close == null || Volumes == null || Volumes[0] == null) return;

                Dictionary<double, double> volumeAtPrice = new Dictionary<double, double>();

                for (int i = 0; i < Math.Min(50, CurrentBar + 1); i++)
                {
                    if (i >= Close.Count || i >= Volumes[0].Count) break;
                    
                    double price = Close[i];
                    double volume = Volumes[0][i];
                    
                    if (price <= 0 || volume <= 0) continue;
                    
                    double roundedPrice = Math.Round(price / TickSize) * TickSize;
                    
                    if (volumeAtPrice.ContainsKey(roundedPrice))
                        volumeAtPrice[roundedPrice] += volume;
                    else
                        volumeAtPrice[roundedPrice] = volume;
                }

                if (!volumeAtPrice.Any()) return;

                double avgVolume = volumeAtPrice.Values.Average();
                double hvnThreshold = avgVolume * 1.5;
                double lvnThreshold = avgVolume * 0.5;

                foreach (var kvp in volumeAtPrice)
                {
                    if (kvp.Value >= hvnThreshold)
                        hvnLevels.Add(kvp.Key);
                    else if (kvp.Value <= lvnThreshold)
                        lvnLevels.Add(kvp.Key);
                }
            }
            catch (Exception ex)
            {
                if (EnableDetailedLogging)
                    Print("Error in UpdateVolumeProfile: " + ex.Message);
            }
        }

        private bool IsWithinTradingHours()
        {
            var currentTime = Time[0].TimeOfDay;
            var marketOpen = new TimeSpan(9, 30, 0);
            var marketClose = new TimeSpan(16, 0, 0);

            return currentTime >= marketOpen && currentTime <= marketClose;
        }

        private double CalculateVolatility(int lookback)
        {
            if (CurrentBar < lookback) return 0.0;

            List<double> returns = new List<double>();
            for (int i = 1; i <= lookback; i++)
            {
                if (Close[i] > 0)
                    returns.Add((Close[i-1] - Close[i]) / Close[i]);
            }

            if (!returns.Any()) return 0.0;

            double mean = returns.Average();
            double variance = returns.Select(r => Math.Pow(r - mean, 2)).Average();
            
            return Math.Sqrt(variance);
        }

        private MQScore6D CalculateMQScore()
        {
            var score = new MQScore6D
            {
                LastUpdate = Time[0],
                Liquidity = new MQDimension { Score = 75, Weight = 0.20, Status = "Good" },
                Efficiency = new MQDimension { Score = 80, Weight = 0.15, Status = "Good" },
                Volatility = new MQDimension { Score = 65, Weight = 0.15, Status = "Fair" },
                Momentum = new MQDimension { Score = 70, Weight = 0.20, Status = "Good" },
                Microstructure = new MQDimension { Score = 78, Weight = 0.15, Status = "Good" },
                Stability = new MQDimension { Score = 72, Weight = 0.15, Status = "Good" }
            };

            score.OverallScore = (score.Liquidity.Score * score.Liquidity.Weight) +
                               (score.Efficiency.Score * score.Efficiency.Weight) +
                               (score.Volatility.Score * score.Volatility.Weight) +
                               (score.Momentum.Score * score.Momentum.Weight) +
                               (score.Microstructure.Score * score.Microstructure.Weight) +
                               (score.Stability.Score * score.Stability.Weight);

            if (score.Momentum.Score > 70 && score.Volatility.Score > 60)
                score.Regime = "Strong Trending";
            else if (score.Momentum.Score > 60)
                score.Regime = "Trending";
            else if (score.Volatility.Score > 70)
                score.Regime = "High Volatility";
            else if (score.Stability.Score > 70)
                score.Regime = "Stable Ranging";
            else
                score.Regime = "Mixed Conditions";

            score.Confidence = 0.85;

            return score;
        }

        private void SendDashboardData(string json)
        {
            try
            {
                using (var client = new HttpClient())
                {
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    client.PostAsync("http://localhost:4000/api/dashboard-data", content).Wait();
                }
            }
            catch (Exception ex)
            {
                if (EnableDetailedLogging)
                    Print("Error sending dashboard data: " + ex.Message);
            }
        }

        #endregion

        #region Dashboard Export

        private void ExportDashboardData(SignalAggregator aggregator)
        {
            try
            {
                var dashboardData = new
                {
                    timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    systemVersion = "5.0",
                    strategies = new
                    {
                        liquidityAbsorption = enableLiquidityAbsorption,
                        icebergDetection = enableIcebergDetection,
                        deltaDivergence = enableDeltaDivergence,
                        volumeImbalance = enableVolumeImbalance,
                        stopRun = enableStopRunAnticipation,
                        hvnRejection = enableHVNRejection,
                        lvnBreakout = enableLVNBreakout,
                        momentumBreakout = enableMomentumBreakout,
                        cumulativeDelta = enableCumulativeDelta,
                        liquidityTraps = enableLiquidityTraps,
                        liquidationDetection = enableLiquidationDetection
                    },
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
                        activeCount = aggregator.Signals.Count,
                        longSignals = aggregator.LongSignals,
                        shortSignals = aggregator.ShortSignals,
                        totalConfidence = aggregator.TotalConfidence
                    },
                    market = new
                    {
                        currentPrice = Close[0],
                        cumulativeDelta = cumulativeDelta,
                        volume = Volumes[0][0],
                        volatility = CalculateVolatility(20)
                    },
                    position = new
                    {
                        quantity = Position.Quantity,
                        averagePrice = Position.AveragePrice,
                        unrealizedPnL = Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0])
                    },
                    mqscore = CalculateMQScore()
                };

                // Create simple JSON-like output without external dependencies
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("{");
                sb.AppendLine("  \"timestamp\": \"" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "\",");
                sb.AppendLine("  \"systemVersion\": \"5.0\",");
                sb.AppendLine("  \"dailyPnL\": " + dailyPnL + ",");
                sb.AppendLine("  \"totalTrades\": " + totalTrades + ",");
                sb.AppendLine("  \"winningTrades\": " + winningTrades + ",");
                sb.AppendLine("  \"losingTrades\": " + losingTrades + ",");
                sb.AppendLine("  \"winRate\": " + (totalTrades > 0 ? (double)winningTrades / totalTrades * 100 : 0) + ",");
                sb.AppendLine("  \"currentPrice\": " + Close[0] + ",");
                sb.AppendLine("  \"cumulativeDelta\": " + cumulativeDelta + ",");
                sb.AppendLine("  \"volume\": " + Volumes[0][0] + ",");
                sb.AppendLine("  \"activeSignals\": " + aggregator.Signals.Count + ",");
                sb.AppendLine("  \"longSignals\": " + aggregator.LongSignals + ",");
                sb.AppendLine("  \"shortSignals\": " + aggregator.ShortSignals + ",");
                sb.AppendLine("  \"totalConfidence\": " + aggregator.TotalConfidence + ",");
                sb.AppendLine("  \"positionQuantity\": " + Position.Quantity + ",");
                sb.AppendLine("  \"positionAvgPrice\": " + Position.AveragePrice + ",");
                sb.AppendLine("  \"unrealizedPnL\": " + Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]));
                sb.AppendLine("}");
                
                string filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), 
                                             "NinjaTrader 8", "nexus_dashboard_data.json");
                
                File.WriteAllText(filePath, sb.ToString());
                // Send to API
                SendDashboardData(sb.ToString());
            }
            catch (Exception ex)
            {
                if (EnableDetailedLogging)
                    Print("Error exporting dashboard data: " + ex.Message);
            }
        }

        #endregion

        #region Supporting Classes

        public class MarketFeatures
        {
            public double CurrentPrice { get; set; }
            public double Volume { get; set; }
            public double Volatility { get; set; }
            public double Momentum { get; set; }
            public double TimeOfDay { get; set; }
            public int DayOfWeek { get; set; }
            public double CumulativeDelta { get; set; }
        }

        public class MLPredictions
        {
            public string MarketRegime { get; set; }
            public double VolatilityForecast { get; set; }
            public double PriceDirection { get; set; }
            public double OverallConfidence { get; set; }
            public List<double> StrategyWeights { get; set; }
            
            public MLPredictions()
            {
                StrategyWeights = new List<double>();
            }
        }

        #endregion

        protected override void OnExecutionUpdate(Execution execution, string executionId, double price, int quantity, MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution.Order != null && execution.Order.OrderState == OrderState.Filled)
            {
                totalTrades++;
                
                if (Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency, Close[0]) > 0)
                    winningTrades++;
                else
                    losingTrades++;

                dailyPnL += execution.Order.Filled * (execution.Price - Position.AveragePrice) * Instrument.MasterInstrument.PointValue;

                if (EnableDetailedLogging)
                {
                    Print("Trade executed: " + execution.Order.OrderAction + " " + quantity + " at " + price.ToString("F2"));
                }
            }
        }
    }
}
