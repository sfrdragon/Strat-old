using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Utils;

namespace FlagshipStrategy
{

    /// <summary>
    /// Advanced futures trading strategy for ES and NQ with volume delta analysis,
    /// session-based take profits, and dynamic stop loss management
    /// </summary>
    public class FlagshipFuturesStrategy : Strategy, ICurrentSymbol, ICurrentAccount
    {
        #region === CORE CONFIGURATION ===

        [InputParameter("Symbol", 0)]
        public Symbol CurrentSymbol { get; set; }

        [InputParameter("Account", 1)]
        public Account CurrentAccount { get; set; }

        [InputParameter("Time Frame", 2)]
        public Period TimeFrame { get; set; } = Period.MIN1;

        [InputParameter("Contract Size", 3, 0.01, 100, 0.01, 2)]
        public double ContractSize { get; set; } = 1.0;

        [InputParameter("Enable Order Stacking", 4)]
        public bool EnableStacking { get; set; } = false;

        [InputParameter("Max Stack Count", 5, 1, 10, 1, 0)]
        public int MaxStackCount { get; set; } = 3;

        [InputParameter("Allow Reversal", 6)]
        public bool AllowReversal { get; set; } = true;

        #endregion

        #region === RVOL PARAMETERS ===

        [InputParameter("RVOL Short Window", 10, 5, 100, 1, 0)]
        public int RvolShortWindow { get; set; } = 10;

        [InputParameter("RVOL Long Window", 11, 10, 200, 1, 0)]
        public int RvolLongWindow { get; set; } = 20;

        [InputParameter("HMA Period", 12, 5, 50, 1, 0)]
        public int HmaPeriod { get; set; } = 14;

        [InputParameter("HMA on Price (false=Volume)", 13)]
        public bool HmaOnPrice { get; set; } = true;

        [InputParameter("RVOL Threshold", 14, 0.1, 5.0, 0.1, 2)]
        public double RvolThreshold { get; set; } = 1.0;

        #endregion

        #region === VOLUME DELTA PARAMETERS ===

        [InputParameter("VD Lookback Window", 20, 5, 100, 1, 0)]
        public int VdLookbackWindow { get; set; } = 20;

        [InputParameter("VD Strength Threshold", 21, 0.5, 3.0, 0.1, 2)]
        public double VdStrengthThreshold { get; set; } = 1.2;

        [InputParameter("VD to Price Ratio Threshold", 22, 0.5, 3.0, 0.1, 2)]
        public double VdPriceRatioThreshold { get; set; } = 1.5;

        [InputParameter("VD to Volume Ratio Threshold", 23, 0.5, 3.0, 0.1, 2)]
        public double VdVolumeRatioThreshold { get; set; } = 1.3;

        [InputParameter("Use Median (false=Mean)", 24)]
        public bool UseMedianCalculation { get; set; } = false;

        #endregion

        #region === CUSTOM HMA PARAMETERS ===

        [InputParameter("Custom HMA Base Period", 30, 5, 100, 1, 0)]
        public int CustomHmaBasePeriod { get; set; } = 20;

        [InputParameter("ATR Period", 31, 10, 50, 1, 0)]
        public int AtrPeriod { get; set; } = 14;

        #endregion

        #region === STOP LOSS PARAMETERS ===

        [InputParameter("ATR Multiplier for SL", 40, 0.0, 2.0, 0.1, 2)]
        public double AtrMultiplierSL { get; set; } = 1.0;

        [InputParameter("Min Stop Distance (ticks)", 41, 1, 50, 1, 0)]
        public int MinStopDistance { get; set; } = 4;

        [InputParameter("Max Stop Distance (ticks)", 42, 10, 100, 1, 0)]
        public int MaxStopDistance { get; set; } = 20;

        #endregion

        #region === TAKE PROFIT PARAMETERS ===

        [InputParameter("Min TP Distance (ticks)", 50, 1, 50, 1, 0)]
        public int MinTpDistance { get; set; } = 8;

        [InputParameter("Alt Take Profit (ticks)", 51, 5, 100, 1, 0)]
        public int AltTakeProfit { get; set; } = 12;

        #endregion

        #region === TIME FILTER PARAMETERS ===

        [InputParameter("Enable Time Period 1", 60)]
        public bool EnableTimePeriod1 { get; set; } = false;

        [InputParameter("Start Time 1 (HHMM)", 61, 0, 2359, 1, 0)]
        public int StartTime1 { get; set; } = 930;

        [InputParameter("End Time 1 (HHMM)", 62, 0, 2359, 1, 0)]
        public int EndTime1 { get; set; } = 1130;

        [InputParameter("Enable Time Period 2", 63)]
        public bool EnableTimePeriod2 { get; set; } = false;

        [InputParameter("Start Time 2 (HHMM)", 64, 0, 2359, 1, 0)]
        public int StartTime2 { get; set; } = 1300;

        [InputParameter("End Time 2 (HHMM)", 65, 0, 2359, 1, 0)]
        public int EndTime2 { get; set; } = 1500;

        [InputParameter("Enable Time Period 3", 66)]
        public bool EnableTimePeriod3 { get; set; } = false;

        [InputParameter("Start Time 3 (HHMM)", 67, 0, 2359, 1, 0)]
        public int StartTime3 { get; set; } = 400;

        [InputParameter("End Time 3 (HHMM)", 68, 0, 2359, 1, 0)]
        public int EndTime3 { get; set; } = 929;

        #endregion

        #region === SIGNAL SELECTION PARAMETERS ===

        // Entry Signals
        [InputParameter("Entry: Use RVOL", 70)]
        public bool EntryUseRvol { get; set; } = true;

        [InputParameter("Entry: Use VD Strength", 71)]
        public bool EntryUseVdStrength { get; set; } = true;

        [InputParameter("Entry: Use VD Price Ratio", 72)]
        public bool EntryUseVdPriceRatio { get; set; } = true;

        [InputParameter("Entry: Use Custom HMA", 73)]
        public bool EntryUseCustomHma { get; set; } = true;

        [InputParameter("Entry: Use VD Volume Ratio", 74)]
        public bool EntryUseVdVolumeRatio { get; set; } = false;

        [InputParameter("Entry: Use VD Divergence", 75)]
        public bool EntryUseVdDivergence { get; set; } = false;

        [InputParameter("Entry Signals Required", 76, 1, 6, 1, 0)]
        public int EntrySignalsRequired { get; set; } = 2;

        // Exit Signals
        [InputParameter("Exit: Use RVOL", 80)]
        public bool ExitUseRvol { get; set; } = true;

        [InputParameter("Exit: Use VD Strength", 81)]
        public bool ExitUseVdStrength { get; set; } = true;

        [InputParameter("Exit: Use VD Price Ratio", 82)]
        public bool ExitUseVdPriceRatio { get; set; } = false;

        [InputParameter("Exit: Use Custom HMA", 83)]
        public bool ExitUseCustomHma { get; set; } = true;

        [InputParameter("Exit: Use VD Volume Ratio", 84)]
        public bool ExitUseVdVolumeRatio { get; set; } = false;

        [InputParameter("Exit: Use VD Divergence", 85)]
        public bool ExitUseVdDivergence { get; set; } = false;

        [InputParameter("Exit Signals Required", 86, 1, 6, 1, 0)]
        public int ExitSignalsRequired { get; set; } = 2;

        #endregion

        #region === RISK MANAGEMENT PARAMETERS ===

        [InputParameter("Enable Daily Loss Limit", 90)]
        public bool EnableDailyLossLimit { get; set; } = false;

        [InputParameter("Max Daily Loss ($)", 91, 100, 10000, 100, 0)]
        public double MaxDailyLoss { get; set; } = 1000;

        [InputParameter("Slippage ATR Multiplier", 92, 0.0, 1.0, 0.01, 2)]
        public double SlippageAtrMultiplier { get; set; } = 0.1;

        #endregion

        #region === PRIVATE FIELDS ===

        // Historical Data Management
        private HistoricalData historicalData;
        private DateTime warmupStartTime;
        private bool isWarmupComplete = false;
        private const int WARMUP_HOURS = 72;

        // Backtesting support
        public DateTime StartPoint { get; set; } = default;

        // Volume Analysis
        private IVolumeAnalysisCalculationProgress volumeAnalysisProgress;
        private bool volumeAnalysisReady = false;

        // Indicators
        private Indicator hmaIndicator;
        private Indicator atrIndicator;
        private Indicator customHmaIndicator;
        private Indicator volumeIndicator;

        // Position Management
        private int currentPositionStatus = 0; // -1 = short, 0 = flat, 1 = long
        private int currentStackCount = 0;
        private List<Position> activePositions = new List<Position>();
        private List<Order> pendingOrders = new List<Order>();
        private bool isHistoricalDataLoading = false; // Flag to prevent duplicate loading

        // Session Tracking
        private SessionManager sessionManager;
        private DateTime lastSessionUpdate = DateTime.MinValue;

        // Stop Loss & Take Profit Management
        private StopLossManager stopLossManager;
        private StopLossOrderManager stopLossOrderManager;

        // Time Filter Management
        private TimeFilterManager timeFilterManager;

        // Order Management
        private OrderManager orderManager;

        // Risk Management
        private RiskManager riskManager;

        // Logging and Monitoring
        private LoggingManager loggingManager;

        // Signal State
        private SignalManager signalManager;
        private SignalSnapshot lastSignalSnapshot;
        private DateTime lastCandleTime = DateTime.MinValue;
        private HistoryItemBar previousCandle;

        // Logging
        private int candleCounter = 0;
        private DateTime strategyStartTime;

        // Order Type
        private string marketOrderTypeId;

        #endregion

        #region === CONSTRUCTOR ===

        public FlagshipFuturesStrategy() : base()
        {
            this.Name = "Flagship Futures Strategy";
            this.Description = "Advanced multi-timeframe futures trading strategy with volume delta analysis";
        }

        #endregion

        #region === LIFECYCLE METHODS ===

        protected override void OnRun()
        {
            try
            {
                this.strategyStartTime = Core.Instance.TimeUtils.DateTimeUtcNow;
                this.Log($"Strategy starting at {strategyStartTime:yyyy-MM-dd HH:mm:ss}", StrategyLoggingLevel.Info);

                // Validate and restore connection objects
                if (!ValidateAndRestoreConnections())
                {
                    this.Log("Failed to validate connections. Strategy cannot start.", StrategyLoggingLevel.Error);
                    return;
                }

                // Get market order type
                this.marketOrderTypeId = GetMarketOrderType();
                if (string.IsNullOrEmpty(this.marketOrderTypeId))
                {
                    this.Log("Market order type not available. Strategy cannot start.", StrategyLoggingLevel.Error);
                    return;
                }

                // Subscribe to symbol events
                this.CurrentSymbol.NewLast += OnNewLast;
                this.CurrentSymbol.NewQuote += OnNewQuote;

                // Load historical data immediately (following HRVD pattern)
                LoadHistoricalData();

                this.Log($"Strategy initialization complete", StrategyLoggingLevel.Info);
            }
            catch (Exception ex)
            {
                this.Log($"Error in OnRun: {ex.Message}", StrategyLoggingLevel.Error);
                this.Stop();
            }
        }

        protected override void OnStop()
        {
            try
            {
                this.Log("Strategy stopping...", StrategyLoggingLevel.Info);

                // Unsubscribe from events
                UnsubscribeFromEvents();

                // Close all positions
                CloseAllPositions("Strategy Stop");

                // Dispose resources
                DisposeResources();

                this.Log($"Strategy stopped. Total runtime: {(Core.Instance.TimeUtils.DateTimeUtcNow - strategyStartTime).TotalMinutes:F2} minutes",
                    StrategyLoggingLevel.Info);
            }
            catch (Exception ex)
            {
                this.Log($"Error in OnStop: {ex.Message}", StrategyLoggingLevel.Error);
            }
        }

        protected override void OnRemove()
        {
            this.Log("Strategy being removed", StrategyLoggingLevel.Info);
            base.OnRemove();
        }

        #endregion

        #region === INITIALIZATION METHODS ===

        private bool ValidateAndRestoreConnections()
        {
            // Restore Symbol from active connection
            if (this.CurrentSymbol != null && this.CurrentSymbol.State == BusinessObjectState.Fake)
            {
                this.CurrentSymbol = Core.Instance.GetSymbol(this.CurrentSymbol.CreateInfo());
            }

            if (this.CurrentSymbol == null)
            {
                this.Log("Symbol not specified or invalid", StrategyLoggingLevel.Error);
                return false;
            }

            // Restore Account from active connection
            if (this.CurrentAccount != null && this.CurrentAccount.State == BusinessObjectState.Fake)
            {
                this.CurrentAccount = Core.Instance.GetAccount(this.CurrentAccount.CreateInfo());
            }

            if (this.CurrentAccount == null)
            {
                this.Log("Account not specified or invalid", StrategyLoggingLevel.Error);
                return false;
            }

            // Validate same connection
            if (this.CurrentSymbol.ConnectionId != this.CurrentAccount.ConnectionId)
            {
                this.Log("Symbol and Account must be from the same connection", StrategyLoggingLevel.Error);
                return false;
            }

            this.Log($"Connections validated - Symbol: {CurrentSymbol.Name}, Account: {CurrentAccount.Name}",
                StrategyLoggingLevel.Info);
            return true;
        }

        private string GetMarketOrderType()
        {
            // Use Core.Instance.OrderTypes like SimpleMACross and HRVD strategies
            var orderType = Core.Instance.OrderTypes.FirstOrDefault(
                x => x.ConnectionId == this.CurrentSymbol.ConnectionId &&
                     x.Behavior == OrderTypeBehavior.Market);

            if (orderType == null || string.IsNullOrEmpty(orderType.Id))
            {
                this.Log("ERROR: Connection does not support market orders", StrategyLoggingLevel.Error);
                return null;
            }

            this.Log($"Market order type found: {orderType.Name} (ID: {orderType.Id})", StrategyLoggingLevel.Info);
            return orderType.Id;
        }

        private void LoadHistoricalData()
        {
            try
            {
                // Calculate start time for historical data (following HRVD pattern)
                var fromTime = this.StartPoint == default ?
                    Core.Instance.TimeUtils.DateTimeUtcNow.AddHours(-WARMUP_HOURS) :
                    this.StartPoint;

                this.Log($"History request: Period={this.TimeFrame} From={fromTime:O}", StrategyLoggingLevel.Info);

                // Load historical data
                this.historicalData = this.CurrentSymbol.GetHistory(this.TimeFrame, this.CurrentSymbol.HistoryType, fromTime);

                if (this.historicalData == null)
                {
                    this.Log("GetHistory returned null", StrategyLoggingLevel.Error);
                    return;
                }

                this.Log($"Historical data loaded: {historicalData.Count} bars", StrategyLoggingLevel.Info);

                // Subscribe to history events - use NewHistoryItem for bar close processing ONLY
                // This ensures orders are placed only when a bar closes, not on every tick
                this.historicalData.NewHistoryItem += OnNewHistoryItem;

                // Initialize volume analysis if we have data
                if (this.historicalData.Count > 0)
                {
                    InitializeVolumeAnalysis();
                    InitializeIndicators();
                }

                // Subscribe to remaining events
                SubscribeToRemainingEvents();

                // Check if we're already warmed up
                CheckWarmupCompletion();
            }
            catch (Exception ex)
            {
                this.Log($"Error loading historical data: {ex.Message}", StrategyLoggingLevel.Error);
            }
        }

        private void InitializeWarmupPeriod(DateTime referenceTime)
        {
            // Calculate warmup start time based on reference time (first tick/quote time in backtesting)
            this.warmupStartTime = referenceTime.AddHours(-WARMUP_HOURS);

            // Try to load historical data with different time ranges if initial request returns no data
            int[] hoursToTry = { WARMUP_HOURS, 48, 24, 12, 6, 1 };

            foreach (int hours in hoursToTry)
            {
                var requestStartTime = referenceTime.AddHours(-hours);

                // Request historical data for warmup period
                var historyRequest = new HistoryRequestParameters
                {
                    Symbol = this.CurrentSymbol,
                    FromTime = requestStartTime,
                    ToTime = referenceTime, // Use reference time instead of default for backtesting
                    Aggregation = new HistoryAggregationTime(this.TimeFrame, HistoryType.Last)
                };

                this.historicalData = this.CurrentSymbol.GetHistory(historyRequest);

                if (this.historicalData != null && this.historicalData.Count > 0)
                {
                    this.Log($"Historical data loaded: {hours} hours requested, {historicalData.Count} bars received",
                        StrategyLoggingLevel.Info);
                    break;
                }
                else if (hours == 1)
                {
                    // Last attempt failed, create empty historical data to continue
                    this.Log("No historical data available. Will process bars as they arrive.", StrategyLoggingLevel.Info);
                    if (this.historicalData == null)
                    {
                        // Request with minimal range to get the HistoricalData object
                        var minRequest = new HistoryRequestParameters
                        {
                            Symbol = this.CurrentSymbol,
                            FromTime = referenceTime.AddMinutes(-1),
                            ToTime = referenceTime,
                            Aggregation = new HistoryAggregationTime(this.TimeFrame, HistoryType.Last)
                        };
                        this.historicalData = this.CurrentSymbol.GetHistory(minRequest);
                    }
                }
            }

            if (this.historicalData == null)
            {
                this.Log("Failed to create historical data object", StrategyLoggingLevel.Error);
                return;
            }

            this.Log($"Loading {WARMUP_HOURS} hours of historical data. Bars loaded: {historicalData.Count}",
                StrategyLoggingLevel.Info);

            // Event subscription moved to LoadHistoricalData method

            // If we have initial bars, process them
            if (this.historicalData.Count > 0)
            {
                this.Log($"Processing {historicalData.Count} initial historical bars", StrategyLoggingLevel.Info);

                // Initialize volume analysis
                InitializeVolumeAnalysis();

                // Initialize indicators
                InitializeIndicators();
            }
            else
            {
                this.Log("No initial historical bars available. Waiting for data stream...", StrategyLoggingLevel.Info);
                // We'll initialize volume analysis and indicators when first bar arrives
            }

            // Subscribe to remaining events
            SubscribeToRemainingEvents();
        }

        private void InitializeVolumeAnalysis()
        {
            try
            {
                var volumeAnalysisParams = new VolumeAnalysisCalculationParameters
                {
                    CalculatePriceLevels = false,
                    DeltaCalculationType = this.CurrentSymbol.DeltaCalculationType
                };

                this.volumeAnalysisProgress = this.historicalData.CalculateVolumeProfile(volumeAnalysisParams);

                if (this.volumeAnalysisProgress != null)
                {
                    this.volumeAnalysisProgress.ProgressChanged += OnVolumeAnalysisProgressChanged;
                    this.Log("Volume analysis calculation started", StrategyLoggingLevel.Info);
                }
            }
            catch (Exception ex)
            {
                this.Log($"Error initializing volume analysis: {ex.Message}", StrategyLoggingLevel.Error);
            }
        }

        private void InitializeIndicators()
        {
            try
            {
                // Only initialize indicators if historical data is available
                if (this.historicalData == null)
                {
                    this.Log("Cannot initialize indicators - historical data not available", StrategyLoggingLevel.Error);
                    return;
                }

                // Initialize ATR using the proper method
                try
                {
                    var atrInfo = Core.Instance.Indicators.All.FirstOrDefault(x => x.Name.Contains("ATR") || x.Name.Contains("Average True Range"));
                    if (atrInfo != null)
                    {
                        this.atrIndicator = Core.Instance.Indicators.CreateIndicator(atrInfo);
                        var atrSettings = new List<SettingItem>
                        {
                            new SettingItemInteger("Period", this.AtrPeriod)
                        };
                        this.atrIndicator.Settings = atrSettings;
                        this.historicalData.AddIndicator(this.atrIndicator);
                        this.Log($"ATR indicator initialized with period {this.AtrPeriod}", StrategyLoggingLevel.Info);
                    }
                    else
                    {
                        this.Log("ATR indicator not found in available indicators", StrategyLoggingLevel.Error);
                    }
                }
                catch (Exception ex)
                {
                    this.Log($"Error creating ATR indicator: {ex.Message}", StrategyLoggingLevel.Error);
                }

                // Initialize SMA for RVOL smoothing (as HMA approximation)
                try
                {
                    var smaInfo = Core.Instance.Indicators.All.FirstOrDefault(x => x.Name == "Simple Moving Average" || x.Name.Contains("SMA"));
                    if (smaInfo != null)
                    {
                        this.hmaIndicator = Core.Instance.Indicators.CreateIndicator(smaInfo);
                        var smaSettings = new List<SettingItem>
                        {
                            new SettingItemInteger("Period", this.HmaPeriod),
                            new SettingItemObject("Sources prices for MA", this.HmaOnPrice ? PriceType.Close : PriceType.Volume)
                        };
                        this.hmaIndicator.Settings = smaSettings;
                        this.historicalData.AddIndicator(this.hmaIndicator);
                        this.Log($"SMA (as HMA) indicator initialized with period {this.HmaPeriod}", StrategyLoggingLevel.Info);
                    }
                    else
                    {
                        this.Log("SMA indicator not found in available indicators", StrategyLoggingLevel.Error);
                    }
                }
                catch (Exception ex)
                {
                    this.Log($"Error creating SMA indicator: {ex.Message}", StrategyLoggingLevel.Error);
                }

                // Initialize Volume indicator
                try
                {
                    var volumeInfo = Core.Instance.Indicators.All.FirstOrDefault(x => x.Name == "Volume");
                    if (volumeInfo != null)
                    {
                        this.volumeIndicator = Core.Instance.Indicators.CreateIndicator(volumeInfo);
                        this.historicalData.AddIndicator(this.volumeIndicator);
                        this.Log("Volume indicator initialized", StrategyLoggingLevel.Info);
                    }
                    else
                    {
                        this.Log("Volume indicator not found in available indicators", StrategyLoggingLevel.Error);
                    }
                }
                catch (Exception ex)
                {
                    this.Log($"Error creating Volume indicator: {ex.Message}", StrategyLoggingLevel.Error);
                }

                // Initialize Signal Calculators
                InitializeSignalCalculators();

                this.Log("Indicators and signal calculators initialized successfully", StrategyLoggingLevel.Info);
            }
            catch (Exception ex)
            {
                this.Log($"Error initializing indicators: {ex.Message}", StrategyLoggingLevel.Error);
            }
        }

        private void InitializeSignalCalculators()
        {
            // Create entry signals dictionary
            var entrySignals = new Dictionary<string, bool>
            {
                { "RVOL", this.EntryUseRvol },
                { "VDStrength", this.EntryUseVdStrength },
                { "VDPriceRatio", this.EntryUseVdPriceRatio },
                { "CustomHMA", this.EntryUseCustomHma },
                { "VDVolumeRatio", this.EntryUseVdVolumeRatio },
                { "VDDivergence", this.EntryUseVdDivergence }
            };

            // Create exit signals dictionary
            var exitSignals = new Dictionary<string, bool>
            {
                { "RVOL", this.ExitUseRvol },
                { "VDStrength", this.ExitUseVdStrength },
                { "VDPriceRatio", this.ExitUseVdPriceRatio },
                { "CustomHMA", this.ExitUseCustomHma },
                { "VDVolumeRatio", this.ExitUseVdVolumeRatio },
                { "VDDivergence", this.ExitUseVdDivergence }
            };

            // Initialize SignalManager
            this.signalManager = new SignalManager(
                entrySignals,
                exitSignals,
                this.EntrySignalsRequired,
                this.ExitSignalsRequired
            );

            // Add RVOL Calculator
            var rvolCalculator = new RvolCalculator(
                this.RvolShortWindow,
                this.RvolLongWindow,
                this.HmaPeriod,
                this.HmaOnPrice,
                this.RvolThreshold,
                this.UseMedianCalculation,
                this.hmaIndicator,
                this.atrIndicator
            );
            this.signalManager.AddCalculator("RVOL", rvolCalculator);

            // Add VD Strength Calculator
            var vdStrengthCalculator = new VolumeDeltaStrengthCalculator(
                this.VdLookbackWindow,
                this.VdStrengthThreshold,
                this.UseMedianCalculation
            );
            this.signalManager.AddCalculator("VDStrength", vdStrengthCalculator);

            // Add VD Price Ratio Calculator
            var vdPriceRatioCalculator = new VDPriceRatioCalculator(
                this.VdLookbackWindow,
                this.VdPriceRatioThreshold,
                this.UseMedianCalculation
            );
            this.signalManager.AddCalculator("VDPriceRatio", vdPriceRatioCalculator);

            // Add Custom HMA Calculator
            var customHmaCalculator = new CustomHMACalculator(
                this.CustomHmaBasePeriod,
                this.atrIndicator,
                this.historicalData
            );
            this.signalManager.AddCalculator("CustomHMA", customHmaCalculator);

            // Add VD Volume Ratio Calculator
            var vdVolumeRatioCalculator = new VDVolumeRatioCalculator(
                this.VdLookbackWindow,
                this.VdVolumeRatioThreshold,
                this.UseMedianCalculation
            );
            this.signalManager.AddCalculator("VDVolumeRatio", vdVolumeRatioCalculator);

            // Add VD Divergence Calculator
            var vdDivergenceCalculator = new VDDivergenceCalculator();
            this.signalManager.AddCalculator("VDDivergence", vdDivergenceCalculator);

            this.Log($"Signal calculators initialized - Entry signals required: {this.EntrySignalsRequired}, Exit signals required: {this.ExitSignalsRequired}",
                StrategyLoggingLevel.Info);
        }

        #endregion

        #region === EVENT SUBSCRIPTION ===

        private void SubscribeToRemainingEvents()
        {
            // Core events for position and order management
            Core.Instance.PositionAdded += OnPositionAdded;
            Core.Instance.PositionRemoved += OnPositionRemoved;
            Core.Instance.OrderAdded += OnOrderAdded;
            Core.Instance.OrderRemoved += OnOrderRemoved;
            Core.Instance.TradeAdded += OnTradeAdded;

            this.Log("Event subscriptions completed", StrategyLoggingLevel.Trading);
        }

        private void UnsubscribeFromEvents()
        {
            // Symbol events
            if (this.CurrentSymbol != null)
            {
                this.CurrentSymbol.NewLast -= OnNewLast;
                this.CurrentSymbol.NewQuote -= OnNewQuote;
            }

            // Historical data events
            if (this.historicalData != null)
            {
                this.historicalData.NewHistoryItem -= OnNewHistoryItem;
            }

            // Volume analysis events
            if (this.volumeAnalysisProgress != null)
            {
                this.volumeAnalysisProgress.ProgressChanged -= OnVolumeAnalysisProgressChanged;
            }

            // Core events
            Core.Instance.PositionAdded -= OnPositionAdded;
            Core.Instance.PositionRemoved -= OnPositionRemoved;
            Core.Instance.OrderAdded -= OnOrderAdded;
            Core.Instance.OrderRemoved -= OnOrderRemoved;
            Core.Instance.TradeAdded -= OnTradeAdded;
        }

        #endregion

        #region === EVENT HANDLERS (PLACEHOLDERS) ===

        private void OnNewLast(Symbol symbol, Last last)
        {
            // Monitor for SL/TP hits on every tick (trade)
            if (isWarmupComplete && stopLossManager != null)
            {
                CheckStopLossAndTakeProfitHits(last.Price, "Last");
            }

            // Check time filters for position exit on tick
            if (isWarmupComplete && timeFilterManager != null && activePositions.Any())
            {
                // Check if we've left allowed trading time
                bool canTrade = timeFilterManager.IsTradingAllowed(last.Time);

                if (timeFilterManager.ShouldClosePositions)
                {
                    string exitReason = $"Time filter exit (tick): {timeFilterManager.ExitReason}";
                    this.Log(exitReason, StrategyLoggingLevel.Trading);
                    CloseAllPositions(exitReason);
                    timeFilterManager.ResetClosePositionsFlag();
                }
            }
        }

        private void OnNewQuote(Symbol symbol, Quote quote)
        {
            // Monitor for SL/TP hits on every tick (quote)
            if (isWarmupComplete && stopLossManager != null)
            {
                // Check using bid for sells, ask for buys
                foreach (var position in activePositions.ToList())
                {
                    double checkPrice = position.Side == Side.Buy ? quote.Bid : quote.Ask;
                    CheckStopLossAndTakeProfitHit(position, checkPrice, "Quote");
                }
            }
        }

        private void CheckStopLossAndTakeProfitHits(double currentPrice, string source)
        {
            foreach (var position in activePositions.ToList())
            {
                CheckStopLossAndTakeProfitHit(position, currentPrice, source);
            }
        }

        private void CheckStopLossAndTakeProfitHit(Position position, double currentPrice, string source)
        {
            // Skip if position doesn't have any quantity
            if (position.Quantity == 0)
                return;

            string positionId = position.Id;

            // Get SL and TP levels from manager
            double? slLevel = stopLossManager.GetCurrentStopLoss(positionId);
            double? tpLevel = stopLossManager.GetTakeProfit(positionId);

            // Skip if levels not set
            if (!slLevel.HasValue || !tpLevel.HasValue)
                return;

            // Check Stop Loss
            if (stopLossManager.IsStopLossHit(positionId, currentPrice, position.Side))
            {
                this.Log($"Stop Loss hit for {position.Side} position at {currentPrice:F2} (SL: {slLevel.Value:F2}) (Source: {source})",
                    StrategyLoggingLevel.Trading);
                ClosePosition(position, "Stop Loss Hit");
                return;
            }

            // Check Take Profit
            if (stopLossManager.IsTakeProfitHit(positionId, currentPrice, position.Side))
            {
                this.Log($"Take Profit hit for {position.Side} position at {currentPrice:F2} (TP: {tpLevel.Value:F2}) (Source: {source})",
                    StrategyLoggingLevel.Trading);
                ClosePosition(position, "Take Profit Hit");
            }
        }

        private void ClosePosition(Position position, string reason)
        {
            try
            {
                // Remove from active positions immediately to prevent duplicate closes
                if (activePositions.Contains(position))
                {
                    activePositions.Remove(position);
                }

                // Clean up SL/TP tracking first
                if (stopLossOrderManager != null)
                {
                    stopLossOrderManager.CancelOrdersForPosition(position.Id);
                }

                // Clean up stop loss manager tracking
                if (stopLossManager != null)
                {
                    stopLossManager.RemovePosition(position.Id);
                }

                // Close the position
                var result = position.Close();
                if (result.Status == TradingOperationResultStatus.Success)
                {
                    this.Log($"Position closed - Reason: {reason}, PnL: {position.GrossPnL.Value:F2}",
                        StrategyLoggingLevel.Trading);
                }
                else
                {
                    this.Log($"Failed to close position: {result.Message}", StrategyLoggingLevel.Error);
                    // Re-add to active positions if close failed
                    if (!activePositions.Contains(position))
                    {
                        activePositions.Add(position);
                    }
                }
            }
            catch (Exception ex)
            {
                this.Log($"Error closing position: {ex.Message}", StrategyLoggingLevel.Error);
                // Re-add to active positions if error occurred
                if (!activePositions.Contains(position))
                {
                    activePositions.Add(position);
                }
            }
        }

        private void OnNewHistoryItem(object sender, HistoryEventArgs e)
        {
            // Process ONLY on bar close (NewHistoryItem event)
            // This ensures all volume delta calculations and order placements
            // happen only when a bar has fully closed with complete volume data
            if (e?.HistoryItem is HistoryItemBar bar)
            {
                ProcessNewBar(bar);
            }
        }

        private void ProcessNewBar(HistoryItemBar bar)
        {
            // Initialize volume analysis and indicators if not done yet (for backtesting with 0 initial bars)
            if (!volumeAnalysisReady && historicalData != null && historicalData.Count > 0)
            {
                if (volumeAnalysisProgress == null)
                {
                    this.Log("Late initialization of volume analysis and indicators", StrategyLoggingLevel.Info);
                    InitializeVolumeAnalysis();
                    InitializeIndicators();
                }
            }

            // Update session manager
            if (this.sessionManager != null)
            {
                this.sessionManager.ProcessBar(bar);
            }

            // Update signal calculators
            if (this.signalManager != null)
            {
                this.signalManager.UpdateAllCalculators(bar, this.historicalData);
            }

            // Store previous candle for stop loss calculations
            if (this.previousCandle != null && bar.TimeLeft > this.previousCandle.TimeLeft)
            {
                this.previousCandle = bar;

                // Update stop loss manager with new candle and ATR
                if (this.stopLossManager != null)
                {
                    if (this.atrIndicator != null)
                    {
                        double atr = this.atrIndicator.GetValue();
                        this.stopLossManager.UpdateAtr(atr);
                    }
                    this.stopLossManager.UpdatePreviousCandle(bar);

                    // Update stop loss orders for all positions
                    foreach (var position in activePositions)
                    {
                        if (this.stopLossOrderManager != null)
                        {
                            this.stopLossOrderManager.UpdateStopLossOrder(position.Id);
                        }
                    }
                }
            }
            else if (this.previousCandle == null)
            {
                this.previousCandle = bar;
            }

            // Check if this is a new candle close
            if (bar.TimeLeft > this.lastCandleTime)
            {
                this.lastCandleTime = bar.TimeLeft;

                // Only process candle close for trading after warmup is complete
                if (isWarmupComplete)
                {
                    OnCandleClose(bar);
                }
            }

            // Increment candle counter for logging
            this.candleCounter++;
            if (this.candleCounter % 3 == 0 && isWarmupComplete)
            {
                LogParameterValues(bar);
            }

            // Check warmup completion after processing bar
            if (!isWarmupComplete)
            {
                CheckWarmupCompletion();
            }
        }

        private void OnCandleClose(HistoryItemBar bar)
        {
            // ═══════════════════════════════════════════════════════════════
            // CRITICAL: This method is ONLY called when a bar closes
            // (triggered by NewHistoryItem event, not HistoryItemUpdated)
            //
            // This ensures:
            // 1. All volume delta calculations use complete, finalized data
            // 2. All orders are placed ONLY on bar close, never mid-bar
            // 3. Signals are evaluated consistently at bar boundaries
            // ═══════════════════════════════════════════════════════════════

            // Cache signals at candle close for consistent evaluation
            if (this.signalManager != null)
            {
                this.signalManager.CacheSignalsAtCandleClose(bar.TimeLeft);
            }

            // Check time filters first
            if (this.timeFilterManager != null)
            {
                bool canTrade = this.timeFilterManager.IsTradingAllowed(bar.TimeLeft);

                // Check if we need to exit positions due to time filter
                if (this.timeFilterManager.ShouldClosePositions)
                {
                    string exitReason = $"Time filter exit: {this.timeFilterManager.ExitReason}";
                    this.Log(exitReason, StrategyLoggingLevel.Trading);
                    CloseAllPositions(exitReason);
                    this.timeFilterManager.ResetClosePositionsFlag();
                    return; // Don't evaluate new trades when exiting due to time
                }

                // If outside trading hours, don't evaluate signals
                if (!canTrade)
                {
                    // Log status periodically
                    if (this.candleCounter % 10 == 0)
                    {
                        this.Log($"Outside trading hours: {this.timeFilterManager.GetCurrentStatus()}",
                            StrategyLoggingLevel.Trading);
                    }
                    return;
                }

                // Check if we're too close to period end (optional: avoid new entries near close)
                if (this.timeFilterManager.IsApproachingPeriodEnd(5)) // Within 5 minutes of period end
                {
                    this.Log("Approaching end of trading period - no new entries",
                        StrategyLoggingLevel.Trading);
                    // Could still allow exits but not new entries
                    // This is optional based on strategy requirements
                }
            }

            // === MAIN TRADING LOGIC ===
            // All orders are placed at candle close as per strategy requirements

            // Check if entry is allowed
            if (!IsNewEntryAllowed(bar.TimeLeft))
            {
                return;
            }

            // Get current ATR for slippage and other calculations
            double currentAtr = this.atrIndicator?.GetValue() ?? 0;

            // Get cached signals (evaluated at candle close)
            bool entryLongSignal = this.signalManager != null ? this.signalManager.IsEntryLongSignal() : false;
            bool entryShortSignal = this.signalManager != null ? this.signalManager.IsEntryShortSignal() : false;
            bool exitLongSignal = this.signalManager != null ? this.signalManager.IsExitLongSignal() : false;
            bool exitShortSignal = this.signalManager != null ? this.signalManager.IsExitShortSignal() : false;

            // Log signal status periodically (every 10 candles)
            if (this.candleCounter % 10 == 0)
            {
                this.Log($"Signal check at bar #{this.candleCounter} - Entry Long: {entryLongSignal}, " +
                        $"Entry Short: {entryShortSignal}, Exit Long: {exitLongSignal}, " +
                        $"Exit Short: {exitShortSignal}", StrategyLoggingLevel.Trading);

                // Also log why signals might not be generated
                if (!entryLongSignal && !entryShortSignal)
                {
                    var snapshot = this.signalManager?.GetCurrentSnapshot(bar.TimeLeft);
                    if (snapshot != null)
                    {
                        this.Log($"Signal details - RvolLong: {snapshot.RvolLongOkay}, RvolShort: {snapshot.RvolShortOkay}, " +
                                $"VDLong: {snapshot.VdStrengthLongOkay}, VDShort: {snapshot.VdStrengthShortOkay}, " +
                                $"HMALong: {snapshot.CustomHmaLongOkay}, HMAShort: {snapshot.CustomHmaShortOkay}",
                                StrategyLoggingLevel.Trading);
                    }
                }
            }

            // Log if signals are generated
            if (entryLongSignal || entryShortSignal || exitLongSignal || exitShortSignal)
            {
                this.Log($"SIGNALS GENERATED at candle close - Entry Long: {entryLongSignal}, " +
                        $"Entry Short: {entryShortSignal}, Exit Long: {exitLongSignal}, " +
                        $"Exit Short: {exitShortSignal}", StrategyLoggingLevel.Trading);
            }

            // CRITICAL: Prevent contradictory signals
            // You cannot have both entry long and entry short at the same time
            if (entryLongSignal && entryShortSignal)
            {
                this.Log("WARNING: Both long and short entry signals detected - ignoring both",
                    StrategyLoggingLevel.Error);
                entryLongSignal = false;
                entryShortSignal = false;
            }

            // Process signals through order manager
            if (this.orderManager != null)
            {
                // Store current snapshot for logging
                this.lastSignalSnapshot = this.signalManager?.GetCurrentSnapshot(bar.TimeLeft);

                // Update position list in order manager
                this.orderManager.UpdateActivePositions(this.activePositions);

                // Process trading signals
                this.orderManager.ProcessSignalAtCandleClose(
                    entryLongSignal,
                    entryShortSignal,
                    exitLongSignal,
                    exitShortSignal,
                    bar.Close,
                    currentAtr,
                    bar.TimeLeft
                );

                // Log position status after processing
                string positionStatus = this.orderManager.GetPositionStatus();
                this.Log($"Position status after candle: {positionStatus}",
                    StrategyLoggingLevel.Trading);
            }
        }

        private void LogParameterValues(HistoryItemBar bar)
        {
            // Log every 3 candles as per requirements (lines 79-80 of strategy outline)

            // Get all parameter values
            var snapshot = this.signalManager?.GetCurrentSnapshot(bar.TimeLeft);
            double atr = this.atrIndicator?.GetValue() ?? 0;
            double customHma = this.customHmaIndicator?.GetValue() ?? 0;
            var (closestHigh, closestLow) = this.sessionManager?.GetClosestLevels(bar.Close) ?? (0, 0);

            double stopUp = 0, stopDown = 0;
            if (this.previousCandle != null && atr > 0)
            {
                stopUp = this.previousCandle.High + (atr * this.AtrMultiplierSL);
                stopDown = this.previousCandle.Low - (atr * this.AtrMultiplierSL);
            }

            string timeStatus = this.timeFilterManager?.GetCurrentStatus() ?? "Unknown";
            string riskStatus = this.riskManager?.GetDailySummary() ?? "Unknown";

            // Update signal snapshot with current price
            if (snapshot != null)
            {
                snapshot.CurrentPrice = bar.Close;
            }

            // Use comprehensive logging manager for parameter snapshot
            if (this.loggingManager != null)
            {
                this.loggingManager.LogParameterSnapshot(
                    bar.TimeLeft,
                    this.candleCounter,
                    snapshot,
                    atr,
                    customHma,
                    closestHigh,
                    closestLow,
                    stopUp,
                    stopDown,
                    timeStatus,
                    riskStatus
                );
            }

            // Update unrealized PnL in risk manager
            if (this.riskManager != null)
            {
                this.riskManager.UpdateUnrealizedPnL(this.activePositions);
            }
        }

        private void OnVolumeAnalysisProgressChanged(object sender, VolumeAnalysisTaskEventArgs e)
        {
            if (e.CalculationState == VolumeAnalysisCalculationState.Finished)
            {
                this.volumeAnalysisReady = true;
                this.Log($"Volume analysis completed. Progress: {e.ProgressPercent}%", StrategyLoggingLevel.Info);
                CheckWarmupCompletion();
            }
            else if (e.ProgressPercent % 20 == 0)
            {
                this.Log($"Volume analysis progress: {e.ProgressPercent}%", StrategyLoggingLevel.Trading);
            }
        }

        private void OnPositionAdded(Position position)
        {
            // Track new position
            if (position.Symbol == this.CurrentSymbol && position.Account == this.CurrentAccount)
            {
                activePositions.Add(position);

                // Set up stop loss and take profit for the new position
                if (stopLossOrderManager != null && isWarmupComplete)
                {
                    stopLossOrderManager.PlaceStopLossAndTakeProfit(position);
                }

                // Synchronize position tracking with OrderManager
                // DO NOT update currentStackCount here - let OrderManager handle it
                if (orderManager != null)
                {
                    orderManager.UpdateActivePositions(activePositions);
                    // Get the actual stack count from OrderManager to stay synchronized
                    currentStackCount = orderManager.GetCurrentStackCount();
                    currentPositionStatus = orderManager.GetCurrentPositionStatus();
                }
                else
                {
                    // Only update if OrderManager is not available (fallback)
                    if (position.Side == Side.Buy)
                    {
                        currentPositionStatus = 1;
                        currentStackCount = activePositions.Count(p => p.Side == Side.Buy);
                    }
                    else
                    {
                        currentPositionStatus = -1;
                        currentStackCount = activePositions.Count(p => p.Side == Side.Sell);
                    }
                }

                // Log trade entry with comprehensive details
                if (this.loggingManager != null)
                {
                    string timeFrame = this.timeFilterManager?.GetCurrentStatus() ?? "Unknown";
                    string reason = currentStackCount > 1 ? $"Stacked entry #{currentStackCount}" : "New entry";

                    this.loggingManager.LogTradeEntry(
                        DateTime.UtcNow,
                        position.Side,
                        position.OpenPrice,
                        position.Quantity,
                        reason,
                        this.lastSignalSnapshot,
                        currentStackCount,
                        timeFrame
                    );
                }
                else
                {
                    this.Log($"Position opened: {position.Side} {position.Quantity} @ {position.OpenPrice:F2} " +
                            $"(Stack: {currentStackCount})",
                        StrategyLoggingLevel.Trading);
                }
            }
        }

        private void OnPositionRemoved(Position position)
        {
            // Remove from tracking
            if (activePositions.Contains(position))
            {
                activePositions.Remove(position);

                // Clean up SL/TP tracking
                if (stopLossOrderManager != null)
                {
                    stopLossOrderManager.CancelOrdersForPosition(position.Id);
                }

                // Update order manager's position tracking
                if (orderManager != null)
                {
                    orderManager.UpdateActivePositions(activePositions);
                }

                // Update position status if no more positions
                if (!activePositions.Any())
                {
                    currentPositionStatus = 0;
                    currentStackCount = 0;
                }
                else
                {
                    // Update stack count for remaining positions
                    var firstPosition = activePositions.First();
                    currentStackCount = activePositions.Count(p => p.Side == firstPosition.Side);
                }

                // Update risk manager with realized PnL
                if (riskManager != null)
                {
                    riskManager.UpdateRealizedPnL(position);
                }

                // Log trade exit with comprehensive details
                if (this.loggingManager != null)
                {
                    string timeFrame = this.timeFilterManager?.GetCurrentStatus() ?? "Unknown";
                    double dailyPnL = this.riskManager != null ? this.riskManager.GetDailyPnL() : 0;

                    // Determine exit reason
                    string reason = "Manual close";
                    // Note: Position doesn't have ClosedOrder property in this API version

                    this.loggingManager.LogTradeExit(
                        DateTime.UtcNow,
                        position.Side == Side.Buy ? Side.Sell : Side.Buy,
                        position.CurrentPrice,
                        position.Quantity,
                        reason,
                        position.NetPnL.Value,
                        dailyPnL,
                        timeFrame
                    );
                }
                else
                {
                    this.Log($"Position closed: {position.Side} PnL: {position.NetPnL.Value:F2}",
                        StrategyLoggingLevel.Trading);
                }
            }
        }

        private void OnOrderAdded(Order order)
        {
            // Track order placement
            if (order.Symbol == this.CurrentSymbol && order.Account == this.CurrentAccount)
            {
                // Log order details
                this.Log($"Order placed: {order.Side} {order.TotalQuantity} @ " +
                        $"{(order.Price > 0 ? order.Price.ToString("F2") : "Market")} - " +
                        $"Type: {order.OrderType?.Name ?? "Unknown"}",
                        StrategyLoggingLevel.Trading);

                // Track pending orders
                if (order.Status != OrderStatus.Cancelled && order.Status != OrderStatus.Refused)
                {
                    pendingOrders.Add(order);
                }
            }
        }

        private void OnOrderRemoved(Order order)
        {
            // Remove from pending orders tracking
            if (pendingOrders.Contains(order))
            {
                pendingOrders.Remove(order);

                // Log order completion
                if (order.FilledQuantity > 0)
                {
                    this.Log($"Order filled: {order.Side} {order.FilledQuantity}/{order.TotalQuantity} - " +
                            $"Status: {order.Status}", StrategyLoggingLevel.Trading);
                }
                else if (order.Status == OrderStatus.Cancelled)
                {
                    this.Log($"Order cancelled: {order.Side} {order.TotalQuantity}",
                            StrategyLoggingLevel.Trading);
                }
            }
        }

        private void OnTradeAdded(Trade trade)
        {
            // Track trade for risk management
            if (trade.Symbol == this.CurrentSymbol && trade.Account == this.CurrentAccount)
            {
                // Log trade details
                this.Log($"Trade executed: {trade.Side} {trade.Quantity} @ {trade.Price:F2}",
                        StrategyLoggingLevel.Trading);

                // Update unrealized PnL in risk manager
                if (riskManager != null && activePositions.Any())
                {
                    riskManager.UpdateUnrealizedPnL(activePositions);

                    // Check if we should halt after this trade
                    if (riskManager.IsTradingHalted())
                    {
                        this.Log($"Risk limit reached - {riskManager.GetHaltReason()}",
                            StrategyLoggingLevel.Error);
                    }
                }
            }
        }

        #endregion

        #region === WARMUP COMPLETION ===

        private void CheckWarmupCompletion()
        {
            if (!isWarmupComplete && historicalData != null && historicalData.Count > 0)
            {
                // Check if we have enough historical data
                var oldestBar = historicalData[0];
                var newestBar = historicalData[historicalData.Count - 1];
                var dataSpan = newestBar.TimeLeft - oldestBar.TimeLeft;

                // Be more flexible with warmup requirements
                // Accept if we have either:
                // 1. Close to the requested warmup hours (90% tolerance)
                // 2. At least 24 hours of data and volume analysis is ready
                // 3. At least 100 bars (minimum for indicators)
                bool hasEnoughTime = dataSpan.TotalHours >= WARMUP_HOURS * 0.9;
                bool hasMinimumData = dataSpan.TotalHours >= 24 && volumeAnalysisReady;
                bool hasEnoughBars = historicalData.Count >= 100;

                if (hasEnoughTime || hasMinimumData || hasEnoughBars)
                {
                    isWarmupComplete = true;
                    this.Log($"Warmup period complete. Ready to trade. Data span: {dataSpan.TotalHours:F2} hours, Bars: {historicalData.Count}",
                        StrategyLoggingLevel.Info);

                    // Initialize session tracking with warmup data
                    InitializeSessionTracking();
                }
                else
                {
                    // Log progress
                    if (this.candleCounter % 10 == 0)
                    {
                        this.Log($"Warmup progress: {dataSpan.TotalHours:F2}/{WARMUP_HOURS} hours, Bars: {historicalData.Count}",
                            StrategyLoggingLevel.Trading);
                    }
                }
            }
        }

        private void InitializeSessionTracking()
        {
            // Initialize Session Manager for take profit level tracking
            this.sessionManager = new SessionManager(
                this.MinTpDistance,
                this.AltTakeProfit,
                this.CurrentSymbol.TickSize,
                (msg, level) => this.Log(msg, level)
            );

            // Process historical data to establish session highs and lows
            if (this.historicalData != null && this.historicalData.Count > 0)
            {
                this.sessionManager.ProcessHistoricalData(this.historicalData);
                this.Log($"Session tracking initialized with {historicalData.Count} historical bars",
                    StrategyLoggingLevel.Info);

                // Log initial session levels - use last bar's close price instead of Last which is 0 in backtesting
                double referencePrice = historicalData.Count > 0 ?
                    ((HistoryItemBar)historicalData[historicalData.Count - 1]).Close :
                    this.CurrentSymbol.Last;

                // Log session states for debugging
                this.sessionManager.LogSessionStates();

                var (closestHigh, closestLow) = this.sessionManager.GetClosestLevels(referencePrice);

                if (closestHigh > 0 && closestLow > 0)
                {
                    this.Log($"Initial session levels - Closest High: {closestHigh:F2}, Closest Low: {closestLow:F2}, Reference Price: {referencePrice:F2}",
                        StrategyLoggingLevel.Trading);
                }
                else
                {
                    this.Log($"WARNING: No valid session levels found! High: {closestHigh:F2}, Low: {closestLow:F2}",
                        StrategyLoggingLevel.Error);
                }
            }
            else
            {
                this.Log("No historical data available for session tracking", StrategyLoggingLevel.Error);
            }

            // Initialize Stop Loss Manager
            InitializeStopLossManager();

            // Initialize Time Filter Manager
            InitializeTimeFilterManager();

            // Initialize Order Manager
            InitializeOrderManager();

            // Initialize Risk Manager
            InitializeRiskManager();

            // Initialize Logging Manager
            InitializeLoggingManager();
        }

        private void InitializeStopLossManager()
        {
            // Initialize Stop Loss Manager
            this.stopLossManager = new StopLossManager(
                this.AtrMultiplierSL,
                this.MinStopDistance,
                this.MaxStopDistance,
                this.CurrentSymbol.TickSize,
                this.sessionManager,
                (msg, level) => this.Log(msg, level)
            );

            // Initialize Stop Loss Order Manager
            this.stopLossOrderManager = new StopLossOrderManager(
                this.CurrentSymbol,
                this.CurrentAccount,
                Core.Instance,
                this.stopLossManager,
                this.sessionManager,
                this.CurrentSymbol.TickSize,
                this.MinTpDistance,
                this.AltTakeProfit,
                (msg, level) => this.Log(msg, level)
            );

            this.Log("Stop Loss and Take Profit management initialized", StrategyLoggingLevel.Info);
        }

        private void InitializeTimeFilterManager()
        {
            // Initialize Time Filter Manager with three configurable periods
            this.timeFilterManager = new TimeFilterManager(
                this.EnableTimePeriod1, this.StartTime1, this.EndTime1,
                this.EnableTimePeriod2, this.StartTime2, this.EndTime2,
                this.EnableTimePeriod3, this.StartTime3, this.EndTime3,
                (msg, level) => this.Log(msg, level)
            );

            // Check if any periods are enabled
            if (!this.timeFilterManager.HasEnabledPeriods())
            {
                this.Log("WARNING: No time periods enabled - strategy will not trade",
                    StrategyLoggingLevel.Error);
            }
            else
            {
                this.Log($"Time filters initialized - Status: {this.timeFilterManager.GetCurrentStatus()}",
                    StrategyLoggingLevel.Info);
            }
        }

        private void InitializeOrderManager()
        {
            // Initialize Order Manager for position control and stacking
            this.orderManager = new OrderManager(
                this.CurrentSymbol,
                this.CurrentAccount,
                this.ContractSize,
                this.MaxStackCount,
                this.SlippageAtrMultiplier,
                this.AllowReversal,
                this.stopLossOrderManager,
                this.marketOrderTypeId,
                (msg, level) => this.Log(msg, level)
            );

            this.Log($"Order manager initialized - Max stack: {this.MaxStackCount}, " +
                     $"Reversals: {(this.AllowReversal ? "Enabled" : "Disabled")}, " +
                     $"Slippage: {this.SlippageAtrMultiplier}x ATR",
                     StrategyLoggingLevel.Info);
        }

        private void InitializeRiskManager()
        {
            // Initialize Risk Manager for daily loss limits and position sizing
            this.riskManager = new RiskManager(
                this.MaxDailyLoss,
                this.ContractSize * this.MaxStackCount, // Max position size
                this.ContractSize,
                this.CurrentSymbol,
                this.CurrentAccount,
                this.timeFilterManager,
                (msg, level) => this.Log(msg, level)
            );

            this.Log($"Risk manager initialized - Max Daily Loss: ${this.MaxDailyLoss:F2}",
                     StrategyLoggingLevel.Info);
        }

        private void InitializeLoggingManager()
        {
            // Initialize comprehensive logging manager
            // Enable file logging if in production
            bool enableFileLogging = false; // Can be made configurable
            string logDirectory = null; // Can be set to specific directory

            this.loggingManager = new LoggingManager(
                (msg, level) => this.Log(msg, level),
                enableFileLogging,
                logDirectory
            );

            this.Log("Logging manager initialized - Comprehensive tracking enabled",
                     StrategyLoggingLevel.Info);
        }

        #endregion

        #region === UTILITY METHODS ===

        private void CloseAllPositions(string reason)
        {
            // Use order manager to close positions with proper slippage simulation
            if (orderManager != null && activePositions.Any())
            {
                double currentAtr = this.atrIndicator?.GetValue() ?? 0;
                orderManager.CloseAllPositions(reason, currentAtr);
            }
            else
            {
                // Fallback to direct closure if order manager not available
                // First cancel all SL/TP orders
                if (stopLossOrderManager != null)
                {
                    stopLossOrderManager.ClearAll();
                }

                foreach (var position in activePositions.ToList())
                {
                    try
                    {
                        var result = position.Close();
                        if (result.Status == TradingOperationResultStatus.Success)
                        {
                            this.Log($"Position closed - Reason: {reason}", StrategyLoggingLevel.Trading);
                        }
                    }
                    catch (Exception ex)
                    {
                        this.Log($"Error closing position: {ex.Message}", StrategyLoggingLevel.Error);
                    }
                }
            }
        }

        /// <summary>
        /// Check if new entries are allowed based on time filters and other conditions
        /// </summary>
        private bool IsNewEntryAllowed(DateTime currentTime)
        {
            // CRITICAL: Check if warmup period is complete
            if (!isWarmupComplete)
            {
                // Log warmup status periodically
                if (candleCounter % 20 == 0)
                {
                    this.Log($"Warmup not complete. Waiting for sufficient historical data. Current bars: {historicalData?.Count ?? 0}",
                        StrategyLoggingLevel.Info);
                }
                return false;
            }

            // Check risk manager for daily loss limits
            if (riskManager != null)
            {
                if (riskManager.IsTradingHalted())
                {
                    // Log halt reason periodically
                    if (candleCounter % 10 == 0)
                    {
                        this.Log($"Trading halted: {riskManager.GetHaltReason()}",
                            StrategyLoggingLevel.Error);
                    }
                    return false;
                }

                // Check if we should halt based on current PnL
                if (riskManager.ShouldHaltTrading(currentTime))
                {
                    return false;
                }

                // Validate position size
                if (!riskManager.ValidatePositionSize(this.ContractSize, this.activePositions))
                {
                    return false;
                }
            }

            // Check time filters
            if (timeFilterManager != null)
            {
                if (!timeFilterManager.IsTradingAllowed(currentTime))
                {
                    return false;
                }

                // Optional: Don't enter new positions close to period end
                if (timeFilterManager.IsApproachingPeriodEnd(5)) // 5 minutes before close
                {
                    return false;
                }

                // Optional: Don't enter immediately after period start (let market settle)
                if (timeFilterManager.HasRecentlyEnteredPeriod(2)) // 2 minutes after open
                {
                    // Could add this as a configurable option
                    // return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Check if exits are allowed (typically always true unless special conditions)
        /// </summary>
        private bool IsExitAllowed(DateTime currentTime)
        {
            // Exits are generally always allowed to manage risk
            // But could add special conditions here if needed
            return true;
        }

        private void DisposeResources()
        {
            // Generate comprehensive final report
            if (this.loggingManager != null)
            {
                string summaryReport = this.loggingManager.GenerateSummaryReport();
                this.Log(summaryReport, StrategyLoggingLevel.Info);

                // Optionally export trade log to CSV
                // string csvPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), 
                //                              $"FlagshipTrades_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                // this.loggingManager.ExportTradesToCsv(csvPath);
            }

            // Log final statistics
            if (this.orderManager != null)
            {
                var stats = this.orderManager.GetStatistics();
                this.Log("=== Final Order Statistics ===", StrategyLoggingLevel.Info);
                foreach (var stat in stats)
                {
                    this.Log($"  {stat.Key}: {stat.Value}", StrategyLoggingLevel.Info);
                }
            }

            if (this.riskManager != null)
            {
                var riskStats = this.riskManager.GetStatistics();
                this.Log("=== Final Risk Statistics ===", StrategyLoggingLevel.Info);
                foreach (var stat in riskStats)
                {
                    this.Log($"  {stat.Key}: {stat.Value}", StrategyLoggingLevel.Info);
                }
            }

            // Clean up risk management
            if (this.riskManager != null)
            {
                this.riskManager.Reset();
                this.riskManager = null;
            }

            // Clean up order management
            if (this.orderManager != null)
            {
                this.orderManager.Reset();
                this.orderManager = null;
            }

            // Clean up stop loss management
            if (this.stopLossOrderManager != null)
            {
                this.stopLossOrderManager.ClearAll();
                this.stopLossOrderManager = null;
            }

            if (this.stopLossManager != null)
            {
                this.stopLossManager.ClearAll();
                this.stopLossManager = null;
            }

            if (this.historicalData != null)
            {
                this.historicalData.Dispose();
                this.historicalData = null;
            }

            if (this.hmaIndicator != null)
            {
                this.hmaIndicator.Dispose();
                this.hmaIndicator = null;
            }

            if (this.atrIndicator != null)
            {
                this.atrIndicator.Dispose();
                this.atrIndicator = null;
            }

            if (this.customHmaIndicator != null)
            {
                this.customHmaIndicator.Dispose();
                this.customHmaIndicator = null;
            }

            if (this.volumeIndicator != null)
            {
                this.volumeIndicator.Dispose();
                this.volumeIndicator = null;
            }
        }

        #endregion

        #region === MONITORING CONNECTION ===

        public override string[] MonitoringConnectionsIds =>
            new string[] { this.CurrentSymbol?.ConnectionId, this.CurrentAccount?.ConnectionId };

        #endregion
    }


    #region === SIGNAL CALCULATORS ===

    /// <summary>
    /// Base interface for all signal calculators
    /// </summary>
    public interface ISignalCalculator
    {
        void UpdateCalculations(HistoryItemBar currentBar, HistoricalData history);
        bool IsLongSignal();
        bool IsShortSignal();
        double GetCurrentValue();
        string GetSignalName();
    }

    /// <summary>
    /// RVOL (Relative Volume) Calculator with HMA smoothing
    /// Following strategy outline lines 22-34
    /// </summary>
    public class RvolCalculator : ISignalCalculator
    {
        private readonly int shortWindow;
        private readonly int longWindow;
        private readonly int hmaPeriod;
        private readonly bool hmaOnPrice;
        private readonly double threshold;
        private readonly bool useMedian;

        private double rvolSmoothed;
        private double rvolNormalized;
        private double rvolDifference;
        private double previousRvolNormalized;
        private Indicator hmaIndicator;
        private Indicator atrIndicator;

        public RvolCalculator(int shortWindow, int longWindow, int hmaPeriod,
            bool hmaOnPrice, double threshold, bool useMedian,
            Indicator hma, Indicator atr)
        {
            this.shortWindow = shortWindow;
            this.longWindow = longWindow;
            this.hmaPeriod = hmaPeriod;
            this.hmaOnPrice = hmaOnPrice;
            this.threshold = threshold;
            this.useMedian = useMedian;
            this.hmaIndicator = hma;
            this.atrIndicator = atr;
        }

        public void UpdateCalculations(HistoryItemBar currentBar, HistoricalData history)
        {
            if (history.Count < Math.Max(shortWindow, longWindow))
                return;

            // Calculate RVOL Short (volume / average volume over short window)
            double avgVolumeShort = CalculateAverageVolume(history, shortWindow);
            double rvolShort = avgVolumeShort > 0 ? currentBar.Volume / avgVolumeShort : 0;

            // Calculate RVOL Long (volume / average volume over long window)
            double avgVolumeLong = CalculateAverageVolume(history, longWindow);
            double rvolLong = avgVolumeLong > 0 ? currentBar.Volume / avgVolumeLong : 0;

            // Get HMA value (either price-based or volume-based)
            double hmaValue = hmaIndicator.GetValue();

            // RvolSmoothed = (RvolShort + RvolLong + HMA) / 3
            rvolSmoothed = (rvolShort + rvolLong + hmaValue) / 3.0;

            // Get ATR for normalization
            double atr = atrIndicator.GetValue();

            // RvolNormalized = RvolSmoothed / ATR
            previousRvolNormalized = rvolNormalized;
            rvolNormalized = atr > 0 ? rvolSmoothed / atr : rvolSmoothed;

            // RvolDifference = absolute value(RvolNormalized - RvolNormalized[t-1])
            rvolDifference = Math.Abs(rvolNormalized - previousRvolNormalized);
        }

        private double CalculateAverageVolume(HistoricalData history, int window)
        {
            if (useMedian)
            {
                var volumes = new List<double>();
                int startIdx = Math.Max(0, history.Count - window);
                for (int i = startIdx; i < history.Count; i++)
                {
                    if (history[i] is HistoryItemBar bar)
                        volumes.Add(bar.Volume);
                }
                volumes.Sort();
                return volumes.Count > 0 ? volumes[volumes.Count / 2] : 0;
            }
            else
            {
                double sum = 0;
                int count = 0;
                int startIdx = Math.Max(0, history.Count - window);
                for (int i = startIdx; i < history.Count; i++)
                {
                    if (history[i] is HistoryItemBar bar)
                    {
                        sum += bar.Volume;
                        count++;
                    }
                }
                return count > 0 ? sum / count : 0;
            }
        }

        public bool IsLongSignal()
        {
            // Safety check for uninitialized values
            if (rvolNormalized == 0 && previousRvolNormalized == 0)
                return false;

            // RvolLongOkay = RvolOkay + isRising
            bool rvolOkay = rvolDifference > threshold;
            bool isRising = rvolNormalized > previousRvolNormalized;  // Fixed: compare normalized values
            return rvolOkay && isRising;
        }

        public bool IsShortSignal()
        {
            // Safety check for uninitialized values
            if (rvolNormalized == 0 && previousRvolNormalized == 0)
                return false;

            // RvolShortOkay = RvolOkay + isFalling
            bool rvolOkay = rvolDifference > threshold;
            bool isFalling = rvolNormalized < previousRvolNormalized;  // Fixed: compare normalized values
            return rvolOkay && isFalling;
        }

        public double GetCurrentValue() => rvolNormalized;
        public string GetSignalName() => "RVOL";
    }

    /// <summary>
    /// Volume Delta Strength Calculator
    /// Following strategy outline lines 51-60
    /// </summary>
    public class VolumeDeltaStrengthCalculator : ISignalCalculator
    {
        private readonly int lookbackWindow;
        private readonly double threshold;
        private readonly bool useMedian;

        private double currentVD;
        private double averageVD;
        private bool vdPositive;
        private bool vdStrong;

        public VolumeDeltaStrengthCalculator(int lookbackWindow, double threshold, bool useMedian)
        {
            this.lookbackWindow = lookbackWindow;
            this.threshold = threshold;
            this.useMedian = useMedian;
        }

        public void UpdateCalculations(HistoryItemBar currentBar, HistoricalData history)
        {
            if (history.Count < lookbackWindow || currentBar.VolumeAnalysisData == null)
                return;

            // IMPORTANT: currentBar is the bar that just CLOSED (triggered by NewHistoryItem event)
            // This ensures we're using complete, finalized volume delta data
            // Get current Volume Delta from VolumeAnalysisData (using Quantower's built-in)
            var volumeData = currentBar.VolumeAnalysisData;
            currentVD = volumeData.Total.Delta; // BuyVolume - SellVolume for CLOSED bar

            // Calculate average VD over lookback window
            averageVD = CalculateAverageVD(history, lookbackWindow);

            // VD positive = Volume delta > 0
            vdPositive = currentVD > 0;

            // VDstrong = VD > average VD * threshold multiple
            vdStrong = Math.Abs(currentVD) > (averageVD * threshold);
        }

        private double CalculateAverageVD(HistoricalData history, int window)
        {
            var vdValues = new List<double>();
            int startIdx = Math.Max(0, history.Count - window);

            for (int i = startIdx; i < history.Count; i++)
            {
                if (history[i] is HistoryItemBar bar && bar.VolumeAnalysisData != null)
                {
                    vdValues.Add(Math.Abs(bar.VolumeAnalysisData.Total.Delta));
                }
            }

            if (vdValues.Count == 0)
                return 0;

            if (useMedian)
            {
                vdValues.Sort();
                return vdValues[vdValues.Count / 2];
            }
            else
            {
                return vdValues.Average();
            }
        }

        public bool IsLongSignal() => vdStrong && vdPositive;
        public bool IsShortSignal() => vdStrong && !vdPositive;
        public double GetCurrentValue() => currentVD;
        public string GetSignalName() => "VD Strength";
    }

    /// <summary>
    /// Volume Delta to Price Ratio Analyzer
    /// Following strategy outline lines 35-48
    /// </summary>
    public class VDPriceRatioCalculator : ISignalCalculator
    {
        private readonly int lookbackWindow;
        private readonly double threshold;
        private readonly bool useMedian;

        private double currentRatio;
        private double averageRatio;
        private bool vdPositive;
        private bool ratioStrong;
        private double currentPriceMove;
        private double currentVD;

        public VDPriceRatioCalculator(int lookbackWindow, double threshold, bool useMedian)
        {
            this.lookbackWindow = lookbackWindow;
            this.threshold = threshold;
            this.useMedian = useMedian;
        }

        public void UpdateCalculations(HistoryItemBar currentBar, HistoricalData history)
        {
            if (history.Count < lookbackWindow || currentBar.VolumeAnalysisData == null)
                return;

            // IMPORTANT: currentBar is the CLOSED bar (triggered by NewHistoryItem event)
            // Price move = absolute value(open - close) for CLOSED bar
            currentPriceMove = Math.Abs(currentBar.Open - currentBar.Close);

            // VD = absolute value(volume delta) for CLOSED bar
            currentVD = Math.Abs(currentBar.VolumeAnalysisData.Total.Delta);

            // Current price move to current VD ratio (CPVD) = price move / VD
            currentRatio = currentVD > 0 ? currentPriceMove / currentVD : 0;

            // Calculate average price move to average VD ratio
            averageRatio = CalculateAverageRatio(history, lookbackWindow);

            // VD positive check
            vdPositive = currentBar.VolumeAnalysisData.Total.Delta > 0;

            // PVD strong = CPVD > APAVD * threshold multiple
            ratioStrong = currentRatio > (averageRatio * threshold);
        }

        private double CalculateAverageRatio(HistoricalData history, int window)
        {
            double sumPriceMove = 0;
            double sumVD = 0;
            var ratios = new List<double>();
            int startIdx = Math.Max(0, history.Count - window);

            for (int i = startIdx; i < history.Count; i++)
            {
                if (history[i] is HistoryItemBar bar && bar.VolumeAnalysisData != null)
                {
                    double move = Math.Abs(bar.Open - bar.Close);
                    double vd = Math.Abs(bar.VolumeAnalysisData.Total.Delta);

                    if (useMedian && vd > 0)
                    {
                        ratios.Add(move / vd);
                    }
                    else
                    {
                        sumPriceMove += move;
                        sumVD += vd;
                    }
                }
            }

            if (useMedian && ratios.Count > 0)
            {
                ratios.Sort();
                return ratios[ratios.Count / 2];
            }
            else
            {
                // Average price move to average VD ratio (APAVD) = average price move / average volume delta
                return sumVD > 0 ? sumPriceMove / sumVD : 0;
            }
        }

        public bool IsLongSignal() => ratioStrong && vdPositive;
        public bool IsShortSignal() => ratioStrong && !vdPositive;
        public double GetCurrentValue() => currentRatio;
        public string GetSignalName() => "VD Price Ratio";
    }

    /// <summary>
    /// Custom HMA Calculator with ATR-adjusted period
    /// Following strategy outline lines 61-64
    /// </summary>
    public class CustomHMACalculator : ISignalCalculator
    {
        private readonly int basePeriod;
        private Indicator atrIndicator;
        private double currentPrice;
        private double hmaValue;
        private HistoricalData historicalData;

        public CustomHMACalculator(int basePeriod, Indicator atr, HistoricalData history)
        {
            this.basePeriod = basePeriod;
            this.atrIndicator = atr;
            this.historicalData = history;
        }

        public void UpdateCalculations(HistoryItemBar currentBar, HistoricalData history)
        {
            currentPrice = currentBar.Close;

            // Get ATR value
            double atr = atrIndicator.GetValue();

            // Custom HMA: HMA Length divided by ATR
            int adjustedPeriod = atr > 0 ? (int)(basePeriod / atr) : basePeriod;
            adjustedPeriod = Math.Max(2, Math.Min(100, adjustedPeriod)); // Clamp between 2 and 100

            // Calculate HMA with adjusted period
            hmaValue = CalculateHMA(history, adjustedPeriod);
        }

        private double CalculateHMA(HistoricalData history, int period)
        {
            // HMA = WMA(2*WMA(n/2) - WMA(n), sqrt(n))
            if (history.Count < period)
                return currentPrice;

            int halfPeriod = period / 2;
            int sqrtPeriod = (int)Math.Sqrt(period);

            // Calculate WMA with half period
            double wmaHalf = CalculateWMA(history, halfPeriod);

            // Calculate WMA with full period
            double wmaFull = CalculateWMA(history, period);

            // Calculate raw HMA series: 2*WMA(n/2) - WMA(n)
            double[] rawHma = new double[sqrtPeriod];
            for (int i = 0; i < sqrtPeriod; i++)
            {
                int idx = history.Count - sqrtPeriod + i;
                if (idx >= 0 && idx < history.Count)
                {
                    // Simplified: using the same calculation for each point
                    rawHma[i] = 2 * wmaHalf - wmaFull;
                }
            }

            // Calculate final WMA of raw HMA
            return CalculateWMAFromArray(rawHma);
        }

        private double CalculateWMA(HistoricalData history, int period)
        {
            double sum = 0;
            double weightSum = 0;
            int startIdx = Math.Max(0, history.Count - period);

            for (int i = startIdx; i < history.Count; i++)
            {
                if (history[i] is HistoryItemBar bar)
                {
                    int weight = i - startIdx + 1;
                    sum += bar.Close * weight;
                    weightSum += weight;
                }
            }

            return weightSum > 0 ? sum / weightSum : currentPrice;
        }

        private double CalculateWMAFromArray(double[] values)
        {
            double sum = 0;
            double weightSum = 0;

            for (int i = 0; i < values.Length; i++)
            {
                int weight = i + 1;
                sum += values[i] * weight;
                weightSum += weight;
            }

            return weightSum > 0 ? sum / weightSum : currentPrice;
        }

        public bool IsLongSignal() => currentPrice > hmaValue;
        public bool IsShortSignal() => currentPrice < hmaValue;
        public double GetCurrentValue() => hmaValue;
        public string GetSignalName() => "Custom HMA";
    }

    /// <summary>
    /// Volume Delta to Volume Ratio Calculator
    /// Following strategy outline lines 65-78
    /// </summary>
    public class VDVolumeRatioCalculator : ISignalCalculator
    {
        private readonly int lookbackWindow;
        private readonly double threshold;
        private readonly bool useMedian;

        private double currentRatio;
        private double averageRatio;
        private bool vdPositive;
        private bool ratioStrong;

        public VDVolumeRatioCalculator(int lookbackWindow, double threshold, bool useMedian)
        {
            this.lookbackWindow = lookbackWindow;
            this.threshold = threshold;
            this.useMedian = useMedian;
        }

        public void UpdateCalculations(HistoryItemBar currentBar, HistoricalData history)
        {
            if (history.Count < lookbackWindow || currentBar.VolumeAnalysisData == null)
                return;

            // IMPORTANT: currentBar is the CLOSED bar (triggered by NewHistoryItem event)
            // VD = absolute value of volume delta for CLOSED bar
            double currentVD = Math.Abs(currentBar.VolumeAnalysisData.Total.Delta);
            double currentVolume = currentBar.Volume;

            // Current VD to Volume ratio
            currentRatio = currentVolume > 0 ? currentVD / currentVolume : 0;

            // Calculate average VD/Volume ratio
            double avgVD = 0;
            double avgVolume = 0;
            var ratios = new List<double>();
            int startIdx = Math.Max(0, history.Count - lookbackWindow);

            for (int i = startIdx; i < history.Count; i++)
            {
                if (history[i] is HistoryItemBar bar && bar.VolumeAnalysisData != null)
                {
                    double vd = Math.Abs(bar.VolumeAnalysisData.Total.Delta);
                    double volume = bar.Volume;

                    if (useMedian && volume > 0)
                    {
                        ratios.Add(vd / volume);
                    }
                    else
                    {
                        avgVD += vd;
                        avgVolume += volume;
                    }
                }
            }

            if (useMedian && ratios.Count > 0)
            {
                ratios.Sort();
                averageRatio = ratios[ratios.Count / 2];
            }
            else
            {
                averageRatio = avgVolume > 0 ? avgVD / avgVolume : 0;
            }

            // VD positive = Volume delta > 0
            vdPositive = currentBar.VolumeAnalysisData.Total.Delta > 0;

            // VDtV strong = (VD/volume) > (Average volume delta / Average volume) * threshold
            ratioStrong = currentRatio > (averageRatio * threshold);
        }

        public bool IsLongSignal() => ratioStrong && vdPositive;
        public bool IsShortSignal() => ratioStrong && !vdPositive;
        public double GetCurrentValue() => currentRatio;
        public string GetSignalName() => "VD Volume Ratio";
    }

    /// <summary>
    /// Volume Delta Divergence Detector
    /// Following strategy outline lines 79-85
    /// </summary>
    public class VDDivergenceCalculator : ISignalCalculator
    {
        private bool vdPositive;
        private bool priceRising;
        private double priceMove;
        private double volumeDelta;

        public void UpdateCalculations(HistoryItemBar currentBar, HistoricalData history)
        {
            if (currentBar.VolumeAnalysisData == null)
                return;

            // Get volume delta
            volumeDelta = currentBar.VolumeAnalysisData.Total.Delta;

            // VD positive = Volume delta > 0
            vdPositive = volumeDelta > 0;

            // Price movement
            priceMove = currentBar.Close - currentBar.Open;

            // Price rise = (close - open) > 0
            priceRising = priceMove > 0;
        }

        public bool IsLongSignal()
        {
            // VD long okay = price drop and VD positive
            // Long signal when price drops but VD is positive (buying pressure divergence)
            return !priceRising && vdPositive;
        }

        public bool IsShortSignal()
        {
            // VD short okay = price rise and VD negative
            // Short signal when price rises but VD is negative (selling pressure divergence)
            return priceRising && !vdPositive;
        }

        public double GetCurrentValue() => volumeDelta;
        public string GetSignalName() => "VD Divergence";
    }

    /// <summary>
    /// Signal Manager that aggregates all signal calculators
    /// </summary>
    public class SignalManager
    {
        private readonly Dictionary<string, ISignalCalculator> signalCalculators;
        private readonly Dictionary<string, bool> entrySignalsEnabled;
        private readonly Dictionary<string, bool> exitSignalsEnabled;
        private readonly int entrySignalsRequired;
        private readonly int exitSignalsRequired;

        // Signal caching for candle close evaluation
        private SignalSnapshot cachedSnapshot;
        private DateTime lastCacheTime;
        private bool entryLongCached;
        private bool entryShortCached;
        private bool exitLongCached;
        private bool exitShortCached;

        public SignalManager(
            Dictionary<string, bool> entrySignals,
            Dictionary<string, bool> exitSignals,
            int entryRequired,
            int exitRequired)
        {
            this.signalCalculators = new Dictionary<string, ISignalCalculator>();
            this.entrySignalsEnabled = entrySignals;
            this.exitSignalsEnabled = exitSignals;
            this.entrySignalsRequired = entryRequired;
            this.exitSignalsRequired = exitRequired;
            this.lastCacheTime = DateTime.MinValue;
        }

        public void AddCalculator(string name, ISignalCalculator calculator)
        {
            signalCalculators[name] = calculator;
        }

        public void UpdateAllCalculators(HistoryItemBar currentBar, HistoricalData history)
        {
            foreach (var calculator in signalCalculators.Values)
            {
                calculator.UpdateCalculations(currentBar, history);
            }

            // Invalidate cache when new calculations are done
            InvalidateCache();
        }

        /// <summary>
        /// Invalidate cached signals when new bar data arrives
        /// </summary>
        public void InvalidateCache()
        {
            cachedSnapshot = null;
        }

        /// <summary>
        /// Cache signals at candle close for consistent evaluation
        /// </summary>
        public void CacheSignalsAtCandleClose(DateTime candleTime)
        {
            if (candleTime > lastCacheTime)
            {
                lastCacheTime = candleTime;
                entryLongCached = CalculateEntryLongSignal();
                entryShortCached = CalculateEntryShortSignal();
                exitLongCached = CalculateExitLongSignal();
                exitShortCached = CalculateExitShortSignal();
                cachedSnapshot = GetCurrentSnapshot(candleTime);
            }
        }

        /// <summary>
        /// Get cached entry long signal (evaluated at candle close)
        /// </summary>
        public bool IsEntryLongSignal()
        {
            return entryLongCached;
        }

        /// <summary>
        /// Get cached entry short signal (evaluated at candle close)
        /// </summary>
        public bool IsEntryShortSignal()
        {
            return entryShortCached;
        }

        /// <summary>
        /// Get cached exit long signal (evaluated at candle close)
        /// </summary>
        public bool IsExitLongSignal()
        {
            return exitLongCached;
        }

        /// <summary>
        /// Get cached exit short signal (evaluated at candle close)
        /// </summary>
        public bool IsExitShortSignal()
        {
            return exitShortCached;
        }

        /// <summary>
        /// Calculate entry long signal (internal method)
        /// </summary>
        private bool CalculateEntryLongSignal()
        {
            int validSignals = 0;
            var signalDetails = new List<string>();

            foreach (var kvp in entrySignalsEnabled)
            {
                if (kvp.Value && signalCalculators.ContainsKey(kvp.Key))
                {
                    bool isLong = signalCalculators[kvp.Key].IsLongSignal();
                    if (isLong)
                    {
                        validSignals++;
                        signalDetails.Add(kvp.Key);
                    }
                }
            }

            // Log detailed signal info periodically
            if (validSignals > 0 && entrySignalsRequired > 0)
            {
                Console.WriteLine($"Entry Long: {validSignals}/{entrySignalsRequired} signals [{string.Join(",", signalDetails)}]");
            }

            return validSignals >= entrySignalsRequired && entrySignalsRequired > 0;
        }

        /// <summary>
        /// Calculate entry short signal (internal method)
        /// </summary>
        private bool CalculateEntryShortSignal()
        {
            int validSignals = 0;
            var signalDetails = new List<string>();

            foreach (var kvp in entrySignalsEnabled)
            {
                if (kvp.Value && signalCalculators.ContainsKey(kvp.Key))
                {
                    bool isShort = signalCalculators[kvp.Key].IsShortSignal();
                    if (isShort)
                    {
                        validSignals++;
                        signalDetails.Add(kvp.Key);
                    }
                }
            }

            // Log detailed signal info periodically
            if (validSignals > 0 && entrySignalsRequired > 0)
            {
                Console.WriteLine($"Entry Short: {validSignals}/{entrySignalsRequired} signals [{string.Join(",", signalDetails)}]");
            }

            return validSignals >= entrySignalsRequired && entrySignalsRequired > 0;
        }

        /// <summary>
        /// Calculate exit long signal (internal method)
        /// </summary>
        private bool CalculateExitLongSignal()
        {
            int validSignals = 0;

            foreach (var kvp in exitSignalsEnabled)
            {
                if (kvp.Value && signalCalculators.ContainsKey(kvp.Key))
                {
                    if (signalCalculators[kvp.Key].IsShortSignal()) // Exit long on short signals
                        validSignals++;
                }
            }

            return validSignals >= exitSignalsRequired;
        }

        /// <summary>
        /// Calculate exit short signal (internal method)
        /// </summary>
        private bool CalculateExitShortSignal()
        {
            int validSignals = 0;

            foreach (var kvp in exitSignalsEnabled)
            {
                if (kvp.Value && signalCalculators.ContainsKey(kvp.Key))
                {
                    if (signalCalculators[kvp.Key].IsLongSignal()) // Exit short on long signals
                        validSignals++;
                }
            }

            return validSignals >= exitSignalsRequired;
        }

        public SignalSnapshot GetCurrentSnapshot(DateTime timestamp)
        {
            var snapshot = new SignalSnapshot { Timestamp = timestamp };

            if (signalCalculators.ContainsKey("RVOL"))
            {
                var rvol = signalCalculators["RVOL"];
                snapshot.RvolLongOkay = rvol.IsLongSignal();
                snapshot.RvolShortOkay = rvol.IsShortSignal();
                snapshot.RvolNormalized = rvol.GetCurrentValue();
            }

            if (signalCalculators.ContainsKey("VDStrength"))
            {
                var vdStrength = signalCalculators["VDStrength"];
                snapshot.VdStrengthLongOkay = vdStrength.IsLongSignal();
                snapshot.VdStrengthShortOkay = vdStrength.IsShortSignal();
                snapshot.CurrentVd = vdStrength.GetCurrentValue();
            }

            if (signalCalculators.ContainsKey("VDPriceRatio"))
            {
                var vdPrice = signalCalculators["VDPriceRatio"];
                snapshot.VdPriceRatioLongOkay = vdPrice.IsLongSignal();
                snapshot.VdPriceRatioShortOkay = vdPrice.IsShortSignal();
            }

            if (signalCalculators.ContainsKey("CustomHMA"))
            {
                var hma = signalCalculators["CustomHMA"];
                snapshot.CustomHmaLongOkay = hma.IsLongSignal();
                snapshot.CustomHmaShortOkay = hma.IsShortSignal();
            }

            if (signalCalculators.ContainsKey("VDVolumeRatio"))
            {
                var vdVolume = signalCalculators["VDVolumeRatio"];
                snapshot.VdVolumeRatioLongOkay = vdVolume.IsLongSignal();
                snapshot.VdVolumeRatioShortOkay = vdVolume.IsShortSignal();
            }

            if (signalCalculators.ContainsKey("VDDivergence"))
            {
                var vdDiv = signalCalculators["VDDivergence"];
                snapshot.VdDivergenceLongOkay = vdDiv.IsLongSignal();
                snapshot.VdDivergenceShortOkay = vdDiv.IsShortSignal();
            }

            return snapshot;
        }
    }

    #endregion


    #region === SESSION MANAGEMENT ===

    /// <summary>
    /// Enum for session types as defined in strategy outline
    /// </summary>
    public enum SessionType
    {
        PreviousDay,    // 9:30am - 5:00pm EST (previous day)
        Overnight,      // 6:00pm - 4:00am EST (previous day to current day)
        Morning         // 4:00am - 9:29am EST (current day)
    }

    /// <summary>
    /// Represents a trading session with high/low tracking
    /// Following strategy outline lines 138-147
    /// </summary>
    public class TradingSession
    {
        public SessionType Type { get; set; }
        public DateTime SessionStartUTC { get; set; }
        public DateTime SessionEndUTC { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public bool IsValid { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public int BarCount { get; set; }

        public TradingSession(SessionType type)
        {
            Type = type;
            High = double.MinValue;
            Low = double.MaxValue;
            IsValid = false;
            BarCount = 0;
        }

        public void UpdateHighLow(double high, double low, DateTime time)
        {
            if (high > High || High == double.MinValue)
                High = high;

            if (low < Low || Low == double.MaxValue)
                Low = low;

            LastUpdateTime = time;
            BarCount++;
            IsValid = true;
        }

        public void Reset()
        {
            High = double.MinValue;
            Low = double.MaxValue;
            IsValid = false;
            BarCount = 0;
        }

        public override string ToString()
        {
            return $"{Type}: H={High:F2} L={Low:F2} Valid={IsValid} Bars={BarCount}";
        }
    }

    /// <summary>
    /// Manages session tracking and take profit level selection
    /// Implements requirements from strategy outline lines 138-158
    /// </summary>
    public class SessionManager
    {
        // Session definitions in EST (will be converted to UTC)
        private const int PREVIOUS_DAY_START_HOUR = 9;
        private const int PREVIOUS_DAY_START_MINUTE = 30;
        private const int PREVIOUS_DAY_END_HOUR = 17;
        private const int PREVIOUS_DAY_END_MINUTE = 0;

        private const int OVERNIGHT_START_HOUR = 18;
        private const int OVERNIGHT_START_MINUTE = 0;
        private const int OVERNIGHT_END_HOUR = 4;
        private const int OVERNIGHT_END_MINUTE = 0;

        private const int MORNING_START_HOUR = 4;
        private const int MORNING_START_MINUTE = 0;
        private const int MORNING_END_HOUR = 9;
        private const int MORNING_END_MINUTE = 29;

        // Session tracking
        private readonly Dictionary<SessionType, TradingSession> sessions;
        private readonly List<SessionHistoryEntry> sessionHistory;
        private DateTime currentTradingDate;
        private DateTime lastProcessedBar;

        // Configuration
        private readonly int minTpDistanceTicks;
        private readonly int altTpTicks;
        private readonly double tickSize;

        // Logging
        private readonly Action<string, StrategyLoggingLevel> logAction;

        public SessionManager(int minTpDistanceTicks, int altTpTicks, double tickSize,
            Action<string, StrategyLoggingLevel> logger)
        {
            this.minTpDistanceTicks = minTpDistanceTicks;
            this.altTpTicks = altTpTicks;
            this.tickSize = tickSize;
            this.logAction = logger;

            sessions = new Dictionary<SessionType, TradingSession>
            {
                { SessionType.PreviousDay, new TradingSession(SessionType.PreviousDay) },
                { SessionType.Overnight, new TradingSession(SessionType.Overnight) },
                { SessionType.Morning, new TradingSession(SessionType.Morning) }
            };

            sessionHistory = new List<SessionHistoryEntry>();
            currentTradingDate = DateTime.MinValue;
        }

        /// <summary>
        /// Process historical data to establish session highs and lows
        /// Called during 72-hour warmup period
        /// </summary>
        public void ProcessHistoricalData(HistoricalData historicalData)
        {
            if (historicalData == null || historicalData.Count == 0)
            {
                logAction?.Invoke("No historical data to process for session tracking",
                    StrategyLoggingLevel.Error);
                return;
            }

            logAction?.Invoke($"Processing {historicalData.Count} historical bars for session tracking",
                StrategyLoggingLevel.Info);

            // Clear existing data
            foreach (var session in sessions.Values)
            {
                session.Reset();
            }
            sessionHistory.Clear();

            // Process each bar
            int barsProcessed = 0;
            for (int i = 0; i < historicalData.Count; i++)
            {
                if (historicalData[i] is HistoryItemBar bar)
                {
                    ProcessBar(bar);
                    barsProcessed++;
                }
            }

            logAction?.Invoke($"Processed {barsProcessed} bars for session tracking",
                StrategyLoggingLevel.Info);

            // Log final session states
            LogSessionStates();
        }

        /// <summary>
        /// Process a single bar for session tracking
        /// </summary>
        public void ProcessBar(HistoryItemBar bar)
        {
            // Validate bar data
            if (bar.High <= 0 || bar.Low <= 0)
            {
                logAction?.Invoke($"Invalid bar data: High={bar.High:F2}, Low={bar.Low:F2}",
                    StrategyLoggingLevel.Error);
                return;
            }

            // Get bar time in EST
            DateTime barTimeEST = TimeZoneUtilities.ConvertBrokerTimeToEST(bar.TimeLeft);

            // Check if we've moved to a new trading day
            if (ShouldResetSessions(barTimeEST))
            {
                SaveSessionHistory();
                ResetSessionsForNewDay(barTimeEST);
            }

            // Determine which session this bar belongs to
            SessionType? sessionType = DetermineSession(barTimeEST);

            if (sessionType.HasValue)
            {
                var session = sessions[sessionType.Value];
                double oldHigh = session.High;
                double oldLow = session.Low;

                session.UpdateHighLow(bar.High, bar.Low, bar.TimeLeft);

                // Log significant updates
                if (session.High != oldHigh || session.Low != oldLow)
                {
                    logAction?.Invoke($"Session {sessionType.Value} updated: High={session.High:F2}, Low={session.Low:F2}",
                        StrategyLoggingLevel.Info);
                }
            }

            lastProcessedBar = bar.TimeLeft;
        }

        /// <summary>
        /// Determine which session a given time belongs to
        /// </summary>
        private SessionType? DetermineSession(DateTime estTime)
        {
            int totalMinutes = estTime.Hour * 60 + estTime.Minute;

            // Previous Day Session: 9:30am - 5:00pm EST (same day)
            int prevDayStart = PREVIOUS_DAY_START_HOUR * 60 + PREVIOUS_DAY_START_MINUTE;
            int prevDayEnd = PREVIOUS_DAY_END_HOUR * 60 + PREVIOUS_DAY_END_MINUTE;

            if (totalMinutes >= prevDayStart && totalMinutes < prevDayEnd)
            {
                return SessionType.PreviousDay;
            }

            // Overnight Session: 6:00pm - 4:00am EST (crosses midnight)
            int overnightStart = OVERNIGHT_START_HOUR * 60 + OVERNIGHT_START_MINUTE;
            int overnightEnd = OVERNIGHT_END_HOUR * 60 + OVERNIGHT_END_MINUTE;

            if (totalMinutes >= overnightStart || totalMinutes < overnightEnd)
            {
                return SessionType.Overnight;
            }

            // Morning Session: 4:00am - 9:29am EST
            int morningStart = MORNING_START_HOUR * 60 + MORNING_START_MINUTE;
            int morningEnd = MORNING_END_HOUR * 60 + MORNING_END_MINUTE;

            if (totalMinutes >= morningStart && totalMinutes <= morningEnd)
            {
                return SessionType.Morning;
            }

            return null; // Outside trading sessions
        }

        /// <summary>
        /// Check if we should reset sessions for a new trading day
        /// </summary>
        private bool ShouldResetSessions(DateTime estTime)
        {
            // Reset at 9:30am EST (start of regular trading)
            if (estTime.Hour == 9 && estTime.Minute >= 30)
            {
                if (currentTradingDate.Date != estTime.Date)
                {
                    return true;
                }
            }

            // Also reset if we haven't set a trading date yet
            if (currentTradingDate == DateTime.MinValue)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Save current sessions to history before resetting
        /// </summary>
        private void SaveSessionHistory()
        {
            foreach (var session in sessions.Values)
            {
                if (session.IsValid)
                {
                    sessionHistory.Add(new SessionHistoryEntry
                    {
                        Type = session.Type,
                        Date = currentTradingDate,
                        High = session.High,
                        Low = session.Low,
                        StartTime = session.SessionStartUTC,
                        EndTime = session.SessionEndUTC
                    });
                }
            }

            // Keep only last 10 days of history (30 sessions)
            while (sessionHistory.Count > 30)
            {
                sessionHistory.RemoveAt(0);
            }
        }

        /// <summary>
        /// Reset sessions for a new trading day
        /// </summary>
        private void ResetSessionsForNewDay(DateTime newDate)
        {
            currentTradingDate = newDate.Date;

            // Set session time boundaries
            UpdateSessionBoundaries(newDate);

            // Reset high/low values
            foreach (var session in sessions.Values)
            {
                session.Reset();
            }

            logAction?.Invoke($"Sessions reset for new trading day: {currentTradingDate:yyyy-MM-dd}",
                StrategyLoggingLevel.Trading);
        }

        /// <summary>
        /// Update session time boundaries for the current trading day
        /// </summary>
        private void UpdateSessionBoundaries(DateTime currentDateEST)
        {
            // Previous Day Session (actually from yesterday)
            DateTime prevDayDate = GetPreviousTradingDay(currentDateEST);
            sessions[SessionType.PreviousDay].SessionStartUTC = TimeZoneUtilities.ConvertESTToBrokerTime(
                new DateTime(prevDayDate.Year, prevDayDate.Month, prevDayDate.Day,
                    PREVIOUS_DAY_START_HOUR, PREVIOUS_DAY_START_MINUTE, 0));
            sessions[SessionType.PreviousDay].SessionEndUTC = TimeZoneUtilities.ConvertESTToBrokerTime(
                new DateTime(prevDayDate.Year, prevDayDate.Month, prevDayDate.Day,
                    PREVIOUS_DAY_END_HOUR, PREVIOUS_DAY_END_MINUTE, 0));

            // Overnight Session (from yesterday evening to today morning)
            sessions[SessionType.Overnight].SessionStartUTC = TimeZoneUtilities.ConvertESTToBrokerTime(
                new DateTime(prevDayDate.Year, prevDayDate.Month, prevDayDate.Day,
                    OVERNIGHT_START_HOUR, OVERNIGHT_START_MINUTE, 0));
            sessions[SessionType.Overnight].SessionEndUTC = TimeZoneUtilities.ConvertESTToBrokerTime(
                new DateTime(currentDateEST.Year, currentDateEST.Month, currentDateEST.Day,
                    OVERNIGHT_END_HOUR, OVERNIGHT_END_MINUTE, 0));

            // Morning Session (today)
            sessions[SessionType.Morning].SessionStartUTC = TimeZoneUtilities.ConvertESTToBrokerTime(
                new DateTime(currentDateEST.Year, currentDateEST.Month, currentDateEST.Day,
                    MORNING_START_HOUR, MORNING_START_MINUTE, 0));
            sessions[SessionType.Morning].SessionEndUTC = TimeZoneUtilities.ConvertESTToBrokerTime(
                new DateTime(currentDateEST.Year, currentDateEST.Month, currentDateEST.Day,
                    MORNING_END_HOUR, MORNING_END_MINUTE, 0));
        }

        /// <summary>
        /// Get the previous trading day, handling weekends and holidays
        /// </summary>
        private DateTime GetPreviousTradingDay(DateTime currentDate)
        {
            DateTime previousDay = currentDate.AddDays(-1);

            // Skip weekends
            while (previousDay.DayOfWeek == DayOfWeek.Saturday ||
                   previousDay.DayOfWeek == DayOfWeek.Sunday)
            {
                previousDay = previousDay.AddDays(-1);
            }

            // TODO: Add holiday checking
            // For now, we're just skipping weekends

            return previousDay;
        }

        /// <summary>
        /// Select take profit level based on current price and position side
        /// Following strategy outline lines 148-157
        /// </summary>
        public double SelectTakeProfit(double currentPrice, Side side)
        {
            // Collect all valid session highs and lows
            List<double> highs = new List<double>();
            List<double> lows = new List<double>();

            // Add current sessions
            foreach (var session in sessions.Values)
            {
                if (session.IsValid)
                {
                    if (session.High != double.MinValue && session.High > 0)
                        highs.Add(session.High);
                    if (session.Low != double.MaxValue && session.Low > 0)
                        lows.Add(session.Low);
                }
            }

            // Add historical sessions if current sessions not fully populated
            if (highs.Count < 3 || lows.Count < 3)
            {
                AddHistoricalLevels(highs, lows);
            }

            // Ensure we have at least some levels
            if (highs.Count == 0 || lows.Count == 0)
            {
                logAction?.Invoke("No valid session levels available, using alt TP",
                    StrategyLoggingLevel.Error);
                return CalculateAltTp(currentPrice, side);
            }

            double selectedTp = 0;

            logAction?.Invoke($"Selecting TP for {side} at price {currentPrice:F2} | " +
                $"Available Highs: [{string.Join(", ", highs.Select(h => h.ToString("F2")))}] | " +
                $"Available Lows: [{string.Join(", ", lows.Select(l => l.ToString("F2")))}]",
                StrategyLoggingLevel.Info);

            if (side == Side.Buy)
            {
                // For buys, select the closest high that is > price
                var validHighs = highs.Where(h => h > currentPrice).OrderBy(h => h - currentPrice);

                if (validHighs.Any())
                {
                    selectedTp = validHighs.First();
                    logAction?.Invoke($"Buy TP: Initial selection = {selectedTp:F2} (closest high above {currentPrice:F2})",
                        StrategyLoggingLevel.Info);

                    // Check minimum distance
                    double distanceTicks = (selectedTp - currentPrice) / tickSize;
                    if (distanceTicks < minTpDistanceTicks)
                    {
                        // Try next level or use alt TP
                        if (validHighs.Count() > 1)
                        {
                            selectedTp = validHighs.ElementAt(1);
                            logAction?.Invoke($"Buy TP: Adjusted to next level = {selectedTp:F2} (min distance not met)",
                                StrategyLoggingLevel.Info);
                        }
                        else
                        {
                            selectedTp = CalculateAltTp(currentPrice, side);
                            logAction?.Invoke($"Buy TP: Using alt TP = {selectedTp:F2} (no valid session levels)",
                                StrategyLoggingLevel.Info);
                        }
                    }
                }
                else
                {
                    // Price is above all highs, use alt TP
                    selectedTp = CalculateAltTp(currentPrice, side);
                    logAction?.Invoke($"Buy TP: Using alt TP = {selectedTp:F2} (price above all highs)",
                        StrategyLoggingLevel.Info);
                }
            }
            else // Side.Sell
            {
                // For sells, select the closest low that is < price
                var validLows = lows.Where(l => l < currentPrice).OrderByDescending(l => l);

                if (validLows.Any())
                {
                    selectedTp = validLows.First();
                    logAction?.Invoke($"Sell TP: Initial selection = {selectedTp:F2} (closest low below {currentPrice:F2})",
                        StrategyLoggingLevel.Info);

                    // Check minimum distance
                    double distanceTicks = (currentPrice - selectedTp) / tickSize;
                    if (distanceTicks < minTpDistanceTicks)
                    {
                        // Try next level or use alt TP
                        if (validLows.Count() > 1)
                        {
                            selectedTp = validLows.ElementAt(1);
                            logAction?.Invoke($"Sell TP: Adjusted to next level = {selectedTp:F2} (min distance not met)",
                                StrategyLoggingLevel.Info);
                        }
                        else
                        {
                            selectedTp = CalculateAltTp(currentPrice, side);
                            logAction?.Invoke($"Sell TP: Using alt TP = {selectedTp:F2} (no valid session levels)",
                                StrategyLoggingLevel.Info);
                        }
                    }
                }
                else
                {
                    // Price is below all lows, use alt TP
                    selectedTp = CalculateAltTp(currentPrice, side);
                    logAction?.Invoke($"Sell TP: Using alt TP = {selectedTp:F2} (price below all lows)",
                        StrategyLoggingLevel.Info);
                }
            }

            logAction?.Invoke($"TP selected for {side}: {selectedTp:F2} (Current: {currentPrice:F2})",
                StrategyLoggingLevel.Trading);

            return selectedTp;
        }

        /// <summary>
        /// Add historical session levels to the lists
        /// </summary>
        private void AddHistoricalLevels(List<double> highs, List<double> lows)
        {
            // Get the most recent sessions from history
            var recentSessions = sessionHistory
                .OrderByDescending(s => s.Date)
                .Take(9); // Up to 3 days worth of sessions

            foreach (var session in recentSessions)
            {
                if (session.High > 0 && !highs.Contains(session.High))
                    highs.Add(session.High);
                if (session.Low > 0 && !lows.Contains(session.Low))
                    lows.Add(session.Low);

                // Stop once we have enough levels
                if (highs.Count >= 3 && lows.Count >= 3)
                    break;
            }
        }

        /// <summary>
        /// Calculate alternative take profit when session levels not suitable
        /// </summary>
        private double CalculateAltTp(double currentPrice, Side side)
        {
            double altTpPrice = side == Side.Buy ?
                currentPrice + (altTpTicks * tickSize) :
                currentPrice - (altTpTicks * tickSize);

            return altTpPrice;
        }

        /// <summary>
        /// Get current session information for logging
        /// </summary>
        public Dictionary<SessionType, TradingSession> GetCurrentSessions()
        {
            return new Dictionary<SessionType, TradingSession>(sessions);
        }

        /// <summary>
        /// Get the closest high and low levels for logging
        /// </summary>
        public (double closestHigh, double closestLow) GetClosestLevels(double currentPrice)
        {
            List<double> highs = new List<double>();
            List<double> lows = new List<double>();

            foreach (var session in sessions.Values)
            {
                if (session.IsValid)
                {
                    if (session.High != double.MinValue && session.High > 0)
                        highs.Add(session.High);
                    if (session.Low != double.MaxValue && session.Low > 0)
                        lows.Add(session.Low);
                }
            }

            double closestHigh = highs.Where(h => h > currentPrice)
                .OrderBy(h => h - currentPrice)
                .FirstOrDefault();

            double closestLow = lows.Where(l => l < currentPrice)
                .OrderByDescending(l => l)
                .FirstOrDefault();

            return (closestHigh, closestLow);
        }

        /// <summary>
        /// Log current session states
        /// </summary>
        public void LogSessionStates()
        {
            logAction?.Invoke("Current Session States:", StrategyLoggingLevel.Trading);
            foreach (var session in sessions.Values)
            {
                logAction?.Invoke($"  {session}", StrategyLoggingLevel.Trading);
            }
        }

        /// <summary>
        /// Check if we have valid session data
        /// </summary>
        public bool HasValidSessionData()
        {
            return sessions.Values.Any(s => s.IsValid);
        }
    }

    /// <summary>
    /// Historical session entry for tracking past sessions
    /// </summary>
    public class SessionHistoryEntry
    {
        public SessionType Type { get; set; }
        public DateTime Date { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
    }

    #endregion


    #region === STOP LOSS MANAGEMENT ===

    /// <summary>
    /// Information about a position's stop loss configuration
    /// </summary>
    public class StopLossInfo
    {
        public string PositionId { get; set; }
        public Side Side { get; set; }
        public double EntryPrice { get; set; }
        public double InitialStopLoss { get; set; }
        public double CurrentStopLoss { get; set; }
        public double TakeProfitLevel { get; set; }
        public DateTime EntryTime { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public int UpdateCount { get; set; }
        public bool IsTrailing { get; set; }
    }

    /// <summary>
    /// Manages stop loss calculations and updates for the strategy
    /// </summary>
    public class StopLossManager
    {
        private readonly double atrMultiplier;
        private readonly int minStopDistanceTicks;
        private readonly int maxStopDistanceTicks;
        private readonly double tickSize;
        private readonly Action<string, StrategyLoggingLevel> logAction;

        private Dictionary<string, StopLossInfo> stopLosses;
        private double currentAtr;
        private HistoryItemBar previousCandle;

        public StopLossManager(
            double atrMultiplier,
            int minStopDistanceTicks,
            int maxStopDistanceTicks,
            double tickSize,
            SessionManager sessionManager,
            Action<string, StrategyLoggingLevel> logger)
        {
            this.atrMultiplier = atrMultiplier;
            this.minStopDistanceTicks = minStopDistanceTicks;
            this.maxStopDistanceTicks = maxStopDistanceTicks;
            this.tickSize = tickSize;
            this.logAction = logger;

            this.stopLosses = new Dictionary<string, StopLossInfo>();
        }

        public void UpdateAtr(double atrValue)
        {
            currentAtr = atrValue;
        }

        public void UpdatePreviousCandle(HistoryItemBar candle)
        {
            previousCandle = candle;
        }

        public double CalculateStopLoss(Side side, double currentPrice, double entryPrice)
        {
            if (previousCandle == null || currentAtr <= 0)
            {
                // Use minimum distance as fallback
                logAction?.Invoke($"No previous candle or ATR data, using min stop distance",
                    StrategyLoggingLevel.Info);
                return side == Side.Buy ?
                    entryPrice - (minStopDistanceTicks * tickSize) :
                    entryPrice + (minStopDistanceTicks * tickSize);
            }

            double atrDistance = currentAtr * atrMultiplier;
            double stopLoss;

            if (side == Side.Buy)
            {
                // For long positions: Stop Loss = Previous Candle Low - (ATR * Multiplier)
                stopLoss = previousCandle.Low - atrDistance;
                double distance = entryPrice - stopLoss;
                double distanceTicks = distance / tickSize;

                logAction?.Invoke($"Buy SL Calc: Prev Low={previousCandle.Low:F2}, ATR={currentAtr:F2}, " +
                    $"ATR Dist={atrDistance:F2}, Raw SL={stopLoss:F2}, Distance={distanceTicks:F0} ticks",
                    StrategyLoggingLevel.Info);

                // Apply min/max constraints
                if (distanceTicks < minStopDistanceTicks)
                {
                    stopLoss = entryPrice - (minStopDistanceTicks * tickSize);
                    logAction?.Invoke($"SL adjusted to min distance: {stopLoss:F2}", StrategyLoggingLevel.Info);
                }
                else if (distanceTicks > maxStopDistanceTicks)
                {
                    stopLoss = entryPrice - (maxStopDistanceTicks * tickSize);
                    logAction?.Invoke($"SL adjusted to max distance: {stopLoss:F2}", StrategyLoggingLevel.Info);
                }
            }
            else
            {
                // For short positions: Stop Loss = Previous Candle High + (ATR * Multiplier)
                stopLoss = previousCandle.High + atrDistance;
                double distance = stopLoss - entryPrice;
                double distanceTicks = distance / tickSize;

                logAction?.Invoke($"Sell SL Calc: Prev High={previousCandle.High:F2}, ATR={currentAtr:F2}, " +
                    $"ATR Dist={atrDistance:F2}, Raw SL={stopLoss:F2}, Distance={distanceTicks:F0} ticks",
                    StrategyLoggingLevel.Info);

                // Apply min/max constraints
                if (distanceTicks < minStopDistanceTicks)
                {
                    stopLoss = entryPrice + (minStopDistanceTicks * tickSize);
                    logAction?.Invoke($"SL adjusted to min distance: {stopLoss:F2}", StrategyLoggingLevel.Info);
                }
                else if (distanceTicks > maxStopDistanceTicks)
                {
                    stopLoss = entryPrice + (maxStopDistanceTicks * tickSize);
                    logAction?.Invoke($"SL adjusted to max distance: {stopLoss:F2}", StrategyLoggingLevel.Info);
                }
            }

            return stopLoss;
        }

        public void RegisterPosition(string positionId, Side side, double entryPrice, double takeProfitLevel)
        {
            double initialStopLoss = CalculateStopLoss(side, entryPrice, entryPrice);

            var stopLossInfo = new StopLossInfo
            {
                PositionId = positionId,
                Side = side,
                EntryPrice = entryPrice,
                InitialStopLoss = initialStopLoss,
                CurrentStopLoss = initialStopLoss,
                TakeProfitLevel = takeProfitLevel,
                EntryTime = DateTime.UtcNow,
                LastUpdateTime = DateTime.UtcNow,
                UpdateCount = 0,
                IsTrailing = false
            };

            stopLosses[positionId] = stopLossInfo;

            logAction?.Invoke(
                $"[SL REGISTERED] {side} position | Entry: {entryPrice:F2} | " +
                $"Initial SL: {initialStopLoss:F2} | TP: {takeProfitLevel:F2}",
                StrategyLoggingLevel.Info);
        }

        public void UpdateStopLoss(string positionId, double currentPrice, Side side, double entryPrice)
        {
            if (stopLosses.TryGetValue(positionId, out var stopLossInfo))
            {
                double newStopLoss = CalculateStopLoss(side, currentPrice, entryPrice);
                bool shouldUpdate = false;

                // Only move stop in favorable direction (trailing stop logic)
                if (side == Side.Buy && newStopLoss > stopLossInfo.CurrentStopLoss)
                {
                    shouldUpdate = true;
                }
                else if (side == Side.Sell && newStopLoss < stopLossInfo.CurrentStopLoss)
                {
                    shouldUpdate = true;
                }

                if (shouldUpdate)
                {
                    double oldStopLoss = stopLossInfo.CurrentStopLoss;
                    stopLossInfo.CurrentStopLoss = newStopLoss;
                    stopLossInfo.LastUpdateTime = DateTime.UtcNow;
                    stopLossInfo.UpdateCount++;
                    stopLossInfo.IsTrailing = true;

                    // Log with detailed information
                    logAction?.Invoke(
                        $"[SL UPDATE] {stopLossInfo.Side} position | " +
                        $"Old: {oldStopLoss:F2} -> New: {newStopLoss:F2} | " +
                        $"Entry: {stopLossInfo.EntryPrice:F2} | " +
                        $"Distance: {Math.Abs(stopLossInfo.EntryPrice - newStopLoss):F2}",
                        StrategyLoggingLevel.Trading);
                }
            }
        }

        public double? GetCurrentStopLoss(string positionId)
        {
            if (stopLosses.TryGetValue(positionId, out var info))
                return info.CurrentStopLoss;
            return null;
        }

        public double? GetTakeProfit(string positionId)
        {
            if (stopLosses.TryGetValue(positionId, out var info))
                return info.TakeProfitLevel;
            return null;
        }

        public void RemovePosition(string positionId)
        {
            if (stopLosses.Remove(positionId))
            {
                logAction?.Invoke($"[SL REMOVED] Position {positionId} removed from tracking",
                    StrategyLoggingLevel.Info);
            }
        }

        public Dictionary<string, double> GetStatistics()
        {
            var stats = new Dictionary<string, double>
            {
                ["Active Positions"] = stopLosses.Count,
                ["Trailing Stops"] = stopLosses.Count(kvp => kvp.Value.IsTrailing),
                ["Total Updates"] = stopLosses.Sum(kvp => kvp.Value.UpdateCount)
            };

            if (stopLosses.Any())
            {
                stats["Avg Distance (ticks)"] = stopLosses.Average(kvp =>
                    Math.Abs(kvp.Value.EntryPrice - kvp.Value.CurrentStopLoss) / tickSize);
            }

            return stats;
        }

        public bool IsStopLossHit(string positionId, double currentPrice, Side side)
        {
            if (!stopLosses.TryGetValue(positionId, out var info))
                return false;

            if (side == Side.Buy)
                return currentPrice <= info.CurrentStopLoss;
            else
                return currentPrice >= info.CurrentStopLoss;
        }

        public bool IsTakeProfitHit(string positionId, double currentPrice, Side side)
        {
            if (!stopLosses.TryGetValue(positionId, out var info))
                return false;

            if (side == Side.Buy)
                return currentPrice >= info.TakeProfitLevel;
            else
                return currentPrice <= info.TakeProfitLevel;
        }

        public void ClearAll()
        {
            stopLosses.Clear();
        }
    }

    /// <summary>
    /// Manages stop loss and take profit orders for positions
    /// </summary>
    public class StopLossOrderManager
    {
        private readonly Symbol symbol;
        private readonly Account account;
        private readonly Core core;
        private readonly StopLossManager stopLossManager;
        private readonly SessionManager sessionManager;
        private readonly double tickSize;
        private readonly int minTpDistanceTicks;
        private readonly int altTpTicks;
        private readonly Action<string, StrategyLoggingLevel> logAction;

        private Dictionary<string, List<Order>> positionOrders;
        private Dictionary<string, OrderType> orderTypeCache;

        public StopLossOrderManager(
            Symbol symbol,
            Account account,
            Core core,
            StopLossManager stopLossManager,
            SessionManager sessionManager,
            double tickSize,
            int minTpDistanceTicks,
            int altTpTicks,
            Action<string, StrategyLoggingLevel> logger)
        {
            this.symbol = symbol;
            this.account = account;
            this.core = core;
            this.stopLossManager = stopLossManager;
            this.sessionManager = sessionManager;
            this.tickSize = tickSize;
            this.minTpDistanceTicks = minTpDistanceTicks;
            this.altTpTicks = altTpTicks;
            this.logAction = logger;

            this.positionOrders = new Dictionary<string, List<Order>>();
            this.orderTypeCache = new Dictionary<string, OrderType>();
        }

        public void PlaceStopLossAndTakeProfit(Position position)
        {
            try
            {
                // Get TP level from session manager
                double tpLevel = sessionManager.SelectTakeProfit(
                    position.OpenPrice,
                    position.Side);

                // Register position with stop loss manager
                stopLossManager.RegisterPosition(
                    position.Id,
                    position.Side,
                    position.OpenPrice,
                    tpLevel);

                // Get current stop loss
                double? slLevel = stopLossManager.GetCurrentStopLoss(position.Id);

                if (!slLevel.HasValue)
                {
                    logAction?.Invoke("Failed to get stop loss level for position",
                        StrategyLoggingLevel.Error);
                    return;
                }

                var orders = new List<Order>();

                // Place stop loss order
                var slOrder = PlaceStopLossOrder(position, slLevel.Value);
                if (slOrder != null)
                    orders.Add(slOrder);

                // Place take profit order
                var tpOrder = PlaceTakeProfitOrder(position, tpLevel);
                if (tpOrder != null)
                    orders.Add(tpOrder);

                if (orders.Any())
                {
                    positionOrders[position.Id] = orders;
                    logAction?.Invoke(
                        $"[SL/TP PLACED] Position {position.Id} | " +
                        $"SL: {slLevel.Value:F2} | TP: {tpLevel:F2}",
                        StrategyLoggingLevel.Trading);
                }
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Error placing SL/TP orders: {ex.Message}",
                    StrategyLoggingLevel.Error);
            }
        }

        private Order PlaceStopLossOrder(Position position, double stopLossPrice)
        {
            try
            {
                var orderType = GetOrderType(OrderTypeBehavior.Stop);
                if (orderType == null)
                {
                    logAction?.Invoke("Stop order type not available",
                        StrategyLoggingLevel.Error);
                    return null;
                }

                var parameters = new PlaceOrderRequestParameters
                {
                    Symbol = symbol,
                    Account = account,
                    Side = position.Side == Side.Buy ? Side.Sell : Side.Buy,
                    OrderTypeId = orderType.Id,
                    Quantity = position.Quantity,
                    TriggerPrice = stopLossPrice,
                    TimeInForce = TimeInForce.GTC,
                    PositionId = position.Id,
                    AdditionalParameters = new List<SettingItem>
                    {
                        new SettingItemBoolean(OrderType.REDUCE_ONLY, true)
                    }
                };

                var result = core.PlaceOrder(parameters);

                if (result.Status == TradingOperationResultStatus.Success)
                {
                    // TradingOperationResult doesn't have Order property, but we can get last order
                    var order = Core.Instance.Orders.LastOrDefault(o =>
                        o.Symbol == symbol && o.Account == account && o.PositionId == position.Id);
                    return order;
                }
                else
                {
                    logAction?.Invoke($"Failed to place stop loss: {result.Message}",
                        StrategyLoggingLevel.Error);
                    return null;
                }
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Error placing stop loss order: {ex.Message}",
                    StrategyLoggingLevel.Error);
                return null;
            }
        }

        private Order PlaceTakeProfitOrder(Position position, double takeProfitPrice)
        {
            try
            {
                var orderType = GetOrderType(OrderTypeBehavior.Limit);
                if (orderType == null)
                {
                    logAction?.Invoke("Limit order type not available",
                        StrategyLoggingLevel.Error);
                    return null;
                }

                var parameters = new PlaceOrderRequestParameters
                {
                    Symbol = symbol,
                    Account = account,
                    Side = position.Side == Side.Buy ? Side.Sell : Side.Buy,
                    OrderTypeId = orderType.Id,
                    Quantity = position.Quantity,
                    Price = takeProfitPrice,
                    TimeInForce = TimeInForce.GTC,
                    PositionId = position.Id,
                    AdditionalParameters = new List<SettingItem>
                    {
                        new SettingItemBoolean(OrderType.REDUCE_ONLY, true)
                    }
                };

                var result = core.PlaceOrder(parameters);

                if (result.Status == TradingOperationResultStatus.Success)
                {
                    // TradingOperationResult doesn't have Order property, but we can get last order
                    var order = Core.Instance.Orders.LastOrDefault(o =>
                        o.Symbol == symbol && o.Account == account && o.PositionId == position.Id);
                    return order;
                }
                else
                {
                    logAction?.Invoke($"Failed to place take profit: {result.Message}",
                        StrategyLoggingLevel.Error);
                    return null;
                }
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Error placing take profit order: {ex.Message}",
                    StrategyLoggingLevel.Error);
                return null;
            }
        }

        public void UpdateStopLossOrder(string positionId)
        {
            if (!positionOrders.TryGetValue(positionId, out var orders))
                return;

            var slOrder = orders.FirstOrDefault(o =>
                o.OrderType?.Behavior == OrderTypeBehavior.Stop);

            if (slOrder == null || slOrder.Status == OrderStatus.Cancelled || slOrder.Status == OrderStatus.Refused)
                return;

            double? newStopLoss = stopLossManager.GetCurrentStopLoss(positionId);
            if (!newStopLoss.HasValue)
                return;

            // Only update if price changed
            if (Math.Abs(slOrder.TriggerPrice - newStopLoss.Value) < tickSize)
                return;

            try
            {
                var parameters = new ModifyOrderRequestParameters
                {
                    OrderId = slOrder.Id,
                    TriggerPrice = newStopLoss.Value
                };

                var result = core.ModifyOrder(parameters);

                if (result.Status == TradingOperationResultStatus.Success)
                {
                    logAction?.Invoke(
                        $"[SL ORDER UPDATED] Position {positionId} | " +
                        $"Old: {slOrder.TriggerPrice:F2} -> New: {newStopLoss.Value:F2}",
                        StrategyLoggingLevel.Trading);
                }
                else
                {
                    logAction?.Invoke($"Failed to update stop loss order: {result.Message}",
                        StrategyLoggingLevel.Error);
                }
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Error updating stop loss order: {ex.Message}",
                    StrategyLoggingLevel.Error);
            }
        }

        public void CancelOrdersForPosition(string positionId)
        {
            if (!positionOrders.TryGetValue(positionId, out var orders))
                return;

            foreach (var order in orders.Where(o => o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Refused))
            {
                try
                {
                    var result = core.CancelOrder(order as IOrder);
                    if (result.Status != TradingOperationResultStatus.Success)
                    {
                        logAction?.Invoke($"Failed to cancel order: {result.Message}",
                            StrategyLoggingLevel.Error);
                    }
                }
                catch (Exception ex)
                {
                    logAction?.Invoke($"Error cancelling order: {ex.Message}",
                        StrategyLoggingLevel.Error);
                }
            }

            positionOrders.Remove(positionId);
            stopLossManager.RemovePosition(positionId);
        }

        private OrderType GetOrderType(OrderTypeBehavior behavior)
        {
            string key = $"{symbol.Id}_{behavior}";

            if (orderTypeCache.TryGetValue(key, out var cached))
                return cached;

            var orderTypes = symbol.GetAlowedOrderTypes(OrderTypeUsage.CloseOrder);
            var orderType = orderTypes?.FirstOrDefault(ot => ot.Behavior == behavior);

            if (orderType != null)
                orderTypeCache[key] = orderType;

            return orderType;
        }

        public void CheckStopLossTakeProfit(double currentPrice, List<Position> positions)
        {
            foreach (var position in positions)
            {
                double? slLevel = stopLossManager.GetCurrentStopLoss(position.Id);
                double? tpLevel = stopLossManager.GetTakeProfit(position.Id);

                if (!slLevel.HasValue || !tpLevel.HasValue)
                    continue;

                bool slHit = false;
                bool tpHit = false;

                if (position.Side == Side.Buy)
                {
                    slHit = currentPrice <= slLevel.Value;
                    tpHit = currentPrice >= tpLevel.Value;
                }
                else
                {
                    slHit = currentPrice >= slLevel.Value;
                    tpHit = currentPrice <= tpLevel.Value;
                }

                if (slHit)
                {
                    logAction?.Invoke(
                        $"[SL HIT] {position.Side} position | " +
                        $"Current: {currentPrice:F2} | SL: {slLevel.Value:F2}",
                        StrategyLoggingLevel.Trading);
                }
                else if (tpHit)
                {
                    logAction?.Invoke(
                        $"[TP HIT] {position.Side} position | " +
                        $"Current: {currentPrice:F2} | TP: {tpLevel.Value:F2}",
                        StrategyLoggingLevel.Trading);
                }
            }
        }

        public void ClearAll()
        {
            foreach (var positionId in positionOrders.Keys.ToList())
            {
                CancelOrdersForPosition(positionId);
            }
            positionOrders.Clear();
        }
    }

    #endregion


    #region === TIME FILTER MANAGEMENT ===

    /// <summary>
    /// Represents a trading time period with start and end times
    /// </summary>
    public class TradingPeriod
    {
        public string Name { get; set; }
        public bool IsEnabled { get; set; }
        public int StartTime { get; set; } // HHMM format (e.g., 930 for 9:30am)
        public int EndTime { get; set; } // HHMM format (e.g., 1600 for 4:00pm)
        public bool IsActive { get; private set; }
        public DateTime LastCheckTime { get; private set; }

        public TradingPeriod(string name, bool enabled, int startTime, int endTime)
        {
            Name = name;
            IsEnabled = enabled;
            StartTime = startTime;
            EndTime = endTime;
            IsActive = false;
            LastCheckTime = DateTime.MinValue;
        }

        /// <summary>
        /// Check if current time is within this trading period
        /// </summary>
        public bool IsTimeInPeriod(DateTime estTime)
        {
            if (!IsEnabled)
            {
                IsActive = false;
                return false;
            }

            int currentTimeInt = estTime.Hour * 100 + estTime.Minute;

            // Handle periods that cross midnight
            if (StartTime > EndTime)
            {
                // Period crosses midnight (e.g., 1800 to 400)
                IsActive = currentTimeInt >= StartTime || currentTimeInt < EndTime;
            }
            else
            {
                // Normal period (e.g., 930 to 1600)
                IsActive = currentTimeInt >= StartTime && currentTimeInt < EndTime;
            }

            LastCheckTime = estTime;
            return IsActive;
        }

        /// <summary>
        /// Get minutes until this period starts (if not active)
        /// </summary>
        public int GetMinutesUntilStart(DateTime estTime)
        {
            if (IsActive || !IsEnabled)
                return -1;

            int currentMinutes = estTime.Hour * 60 + estTime.Minute;
            int startMinutes = (StartTime / 100) * 60 + (StartTime % 100);

            int minutesUntilStart;
            if (startMinutes > currentMinutes)
            {
                // Period starts later today
                minutesUntilStart = startMinutes - currentMinutes;
            }
            else
            {
                // Period starts tomorrow
                minutesUntilStart = (24 * 60) - currentMinutes + startMinutes;
            }

            return minutesUntilStart;
        }

        /// <summary>
        /// Get minutes until this period ends (if active)
        /// </summary>
        public int GetMinutesUntilEnd(DateTime estTime)
        {
            if (!IsActive || !IsEnabled)
                return -1;

            int currentMinutes = estTime.Hour * 60 + estTime.Minute;
            int endMinutes = (EndTime / 100) * 60 + (EndTime % 100);

            int minutesUntilEnd;
            if (endMinutes > currentMinutes)
            {
                // Period ends later today
                minutesUntilEnd = endMinutes - currentMinutes;
            }
            else
            {
                // Period ends tomorrow (for overnight sessions)
                minutesUntilEnd = (24 * 60) - currentMinutes + endMinutes;
            }

            return minutesUntilEnd;
        }

        public override string ToString()
        {
            string startStr = $"{StartTime / 100:D2}:{StartTime % 100:D2}";
            string endStr = $"{EndTime / 100:D2}:{EndTime % 100:D2}";
            return $"{Name}: {startStr}-{endStr} EST (Enabled={IsEnabled}, Active={IsActive})";
        }
    }

    /// <summary>
    /// Manages time-based trading filters with three configurable periods
    /// Handles EST/DST conversions and position exit on period transitions
    /// </summary>
    public class TimeFilterManager
    {
        // Trading periods
        private readonly TradingPeriod period1;
        private readonly TradingPeriod period2;
        private readonly TradingPeriod period3;
        private readonly List<TradingPeriod> allPeriods;

        // State tracking
        private bool wasInTradingPeriod;
        private DateTime lastCheckTime;
        private string currentActivePeriod;
        private DateTime lastPeriodTransition;

        // Logging
        private readonly Action<string, StrategyLoggingLevel> logAction;

        public TimeFilterManager(
            bool enablePeriod1, int startTime1, int endTime1,
            bool enablePeriod2, int startTime2, int endTime2,
            bool enablePeriod3, int startTime3, int endTime3,
            Action<string, StrategyLoggingLevel> logger)
        {
            this.logAction = logger;

            // Initialize trading periods
            period1 = new TradingPeriod("Period 1", enablePeriod1, startTime1, endTime1);
            period2 = new TradingPeriod("Period 2", enablePeriod2, startTime2, endTime2);
            period3 = new TradingPeriod("Period 3", enablePeriod3, startTime3, endTime3);

            allPeriods = new List<TradingPeriod> { period1, period2, period3 };

            wasInTradingPeriod = false;
            lastCheckTime = DateTime.MinValue;
            currentActivePeriod = string.Empty;
            lastPeriodTransition = DateTime.MinValue;

            LogInitialization();
        }

        /// <summary>
        /// Log initialization details
        /// </summary>
        private void LogInitialization()
        {
            logAction?.Invoke("Time Filter Manager initialized with periods:", StrategyLoggingLevel.Info);
            foreach (var period in allPeriods)
            {
                if (period.IsEnabled)
                {
                    logAction?.Invoke($"  {period}", StrategyLoggingLevel.Info);
                }
            }

            if (!allPeriods.Any(p => p.IsEnabled))
            {
                logAction?.Invoke("WARNING: No trading periods enabled - strategy will not trade",
                    StrategyLoggingLevel.Error);
            }
        }

        /// <summary>
        /// Check if trading is allowed at current time
        /// </summary>
        public bool IsTradingAllowed(DateTime brokerTime)
        {
            // Convert to EST for time checking
            DateTime estTime = TimeZoneUtilities.ConvertBrokerTimeToEST(brokerTime);

            // Check if it's a trading day (skip weekends)
            if (!TimeZoneUtilities.IsCMETradingDay(estTime))
            {
                if (wasInTradingPeriod)
                {
                    OnExitTradingPeriod("Weekend/Holiday");
                }
                return false;
            }

            // Check each enabled period
            bool inAnyPeriod = false;
            string activePeriodName = string.Empty;

            foreach (var period in allPeriods)
            {
                if (period.IsTimeInPeriod(estTime))
                {
                    inAnyPeriod = true;
                    activePeriodName = period.Name;
                    break;
                }
            }

            // Handle period transitions
            HandlePeriodTransition(inAnyPeriod, activePeriodName, estTime);

            lastCheckTime = brokerTime;
            return inAnyPeriod;
        }

        /// <summary>
        /// Handle transitions between trading periods
        /// </summary>
        private void HandlePeriodTransition(bool inPeriod, string periodName, DateTime estTime)
        {
            // Entering a trading period
            if (inPeriod && !wasInTradingPeriod)
            {
                OnEnterTradingPeriod(periodName, estTime);
            }
            // Exiting a trading period
            else if (!inPeriod && wasInTradingPeriod)
            {
                OnExitTradingPeriod("Period ended");
            }
            // Switching between periods
            else if (inPeriod && wasInTradingPeriod && currentActivePeriod != periodName)
            {
                OnSwitchTradingPeriod(currentActivePeriod, periodName, estTime);
            }

            wasInTradingPeriod = inPeriod;
            currentActivePeriod = periodName;
        }

        /// <summary>
        /// Called when entering a trading period
        /// </summary>
        private void OnEnterTradingPeriod(string periodName, DateTime estTime)
        {
            lastPeriodTransition = estTime;
            logAction?.Invoke($"Entering trading period: {periodName} at {estTime:HH:mm:ss} EST",
                StrategyLoggingLevel.Trading);
        }

        /// <summary>
        /// Called when exiting a trading period
        /// </summary>
        private void OnExitTradingPeriod(string reason)
        {
            logAction?.Invoke($"Exiting trading period: {currentActivePeriod} - Reason: {reason}",
                StrategyLoggingLevel.Trading);

            // Signal that positions should be closed
            ShouldClosePositions = true;
            ExitReason = reason;
        }

        /// <summary>
        /// Called when switching between trading periods
        /// </summary>
        private void OnSwitchTradingPeriod(string fromPeriod, string toPeriod, DateTime estTime)
        {
            lastPeriodTransition = estTime;
            logAction?.Invoke($"Switching from {fromPeriod} to {toPeriod} at {estTime:HH:mm:ss} EST",
                StrategyLoggingLevel.Trading);

            // Depending on strategy settings, might want to close positions when switching periods
            // This can be configured based on requirements
        }

        /// <summary>
        /// Check if we should close positions due to time filters
        /// </summary>
        public bool ShouldClosePositions { get; private set; }

        /// <summary>
        /// Reason for position closure
        /// </summary>
        public string ExitReason { get; private set; }

        /// <summary>
        /// Reset the close positions flag after handling
        /// </summary>
        public void ResetClosePositionsFlag()
        {
            ShouldClosePositions = false;
            ExitReason = string.Empty;
        }

        /// <summary>
        /// Get current trading period status
        /// </summary>
        public string GetCurrentStatus()
        {
            if (!wasInTradingPeriod)
            {
                // Find next period that will be active
                var nextPeriod = GetNextActivePeriod();
                if (nextPeriod != null)
                {
                    DateTime estNow = TimeZoneUtilities.ConvertBrokerTimeToEST(DateTime.UtcNow);
                    int minutesUntil = nextPeriod.GetMinutesUntilStart(estNow);
                    return $"Outside trading hours. Next period: {nextPeriod.Name} in {minutesUntil} minutes";
                }
                return "Outside trading hours. No upcoming periods today.";
            }

            var activePeriod = allPeriods.FirstOrDefault(p => p.IsActive);
            if (activePeriod != null)
            {
                DateTime estNow = TimeZoneUtilities.ConvertBrokerTimeToEST(DateTime.UtcNow);
                int minutesRemaining = activePeriod.GetMinutesUntilEnd(estNow);
                return $"In {activePeriod.Name}, {minutesRemaining} minutes remaining";
            }

            return "Status unknown";
        }

        /// <summary>
        /// Get the next trading period that will become active
        /// </summary>
        private TradingPeriod GetNextActivePeriod()
        {
            DateTime estNow = TimeZoneUtilities.ConvertBrokerTimeToEST(DateTime.UtcNow);

            return allPeriods
                .Where(p => p.IsEnabled && !p.IsActive)
                .OrderBy(p => p.GetMinutesUntilStart(estNow))
                .FirstOrDefault();
        }

        /// <summary>
        /// Check if any trading period is enabled
        /// </summary>
        public bool HasEnabledPeriods()
        {
            return allPeriods.Any(p => p.IsEnabled);
        }

        /// <summary>
        /// Get all configured periods for logging
        /// </summary>
        public List<TradingPeriod> GetAllPeriods()
        {
            return new List<TradingPeriod>(allPeriods);
        }

        /// <summary>
        /// Check if we're approaching end of current period (within X minutes)
        /// </summary>
        public bool IsApproachingPeriodEnd(int minutesThreshold)
        {
            if (!wasInTradingPeriod)
                return false;

            var activePeriod = allPeriods.FirstOrDefault(p => p.IsActive);
            if (activePeriod == null)
                return false;

            DateTime estNow = TimeZoneUtilities.ConvertBrokerTimeToEST(DateTime.UtcNow);
            int minutesRemaining = activePeriod.GetMinutesUntilEnd(estNow);

            return minutesRemaining >= 0 && minutesRemaining <= minutesThreshold;
        }

        /// <summary>
        /// Check if we recently entered a new period (within X minutes)
        /// </summary>
        public bool HasRecentlyEnteredPeriod(int minutesThreshold)
        {
            if (!wasInTradingPeriod || lastPeriodTransition == DateTime.MinValue)
                return false;

            TimeSpan timeSinceEntry = DateTime.UtcNow - lastPeriodTransition;
            return timeSinceEntry.TotalMinutes <= minutesThreshold;
        }
    }

    #endregion


    #region === ORDER MANAGEMENT ===

    /// <summary>
    /// Trade signal with all necessary information for order placement
    /// </summary>
    public class TradeSignal
    {
        public Side Side { get; set; }
        public SignalType Type { get; set; }
        public double Price { get; set; }
        public DateTime Time { get; set; }
        public string Reason { get; set; }
        public Dictionary<string, double> SignalValues { get; set; }

        public TradeSignal()
        {
            SignalValues = new Dictionary<string, double>();
        }
    }

    public enum SignalType
    {
        Entry,
        Exit,
        Reversal,
        StopLoss,
        TakeProfit,
        TimeExit
    }

    /// <summary>
    /// Manages order placement, position stacking, and reversals
    /// Following strategy outline lines 44-71
    /// </summary>
    public class OrderManager
    {
        // Configuration
        private readonly Symbol symbol;
        private readonly Account account;
        private readonly double contractSize;
        private readonly int maxStackCount;
        private readonly double slippageAtrMultiplier;
        private readonly bool allowReversal;

        // State tracking
        private int currentPositionStatus; // -1 = short, 0 = flat, 1 = long
        private int currentStackCount;
        private List<Position> activePositions;
        private List<Order> pendingOrders;
        private OrderType marketOrderType;
        private DateTime lastOrderTime;

        // Dependencies
        private readonly StopLossOrderManager stopLossOrderManager;
        private readonly Action<string, StrategyLoggingLevel> logAction;

        // Statistics
        private int totalTradesPlaced;
        private int reversalCount;
        private int stackedPositionCount;
        private double totalSlippage;

        public OrderManager(
            Symbol symbol,
            Account account,
            double contractSize,
            int maxStackCount,
            double slippageAtrMultiplier,
            bool allowReversal,
            StopLossOrderManager stopLossOrderManager,
            string marketOrderTypeId,
            Action<string, StrategyLoggingLevel> logger)
        {
            this.symbol = symbol;
            this.account = account;
            this.contractSize = contractSize;
            this.maxStackCount = maxStackCount;
            this.slippageAtrMultiplier = slippageAtrMultiplier;
            this.allowReversal = allowReversal;
            this.stopLossOrderManager = stopLossOrderManager;
            this.logAction = logger;

            this.activePositions = new List<Position>();
            this.pendingOrders = new List<Order>();
            this.currentPositionStatus = 0;
            this.currentStackCount = 0;
            this.lastOrderTime = DateTime.MinValue;

            InitializeOrderTypes(marketOrderTypeId);
        }

        /// <summary>
        /// Initialize market order type for the symbol
        /// </summary>
        private void InitializeOrderTypes(string marketOrderTypeId)
        {
            if (!string.IsNullOrEmpty(marketOrderTypeId))
            {
                // Use Core.Instance.OrderTypes like working strategies
                marketOrderType = Core.Instance.OrderTypes.FirstOrDefault(
                    ot => ot.Id == marketOrderTypeId &&
                          ot.ConnectionId == symbol.ConnectionId);

                if (marketOrderType != null)
                {
                    logAction?.Invoke($"Market order type initialized: {marketOrderType.Name} (ID: {marketOrderType.Id})",
                        StrategyLoggingLevel.Info);
                }
                else
                {
                    logAction?.Invoke($"ERROR: Market order type ID '{marketOrderTypeId}' not found",
                        StrategyLoggingLevel.Error);
                }
            }
            else
            {
                logAction?.Invoke("ERROR: No market order type ID provided to OrderManager",
                    StrategyLoggingLevel.Error);
            }
        }

        /// <summary>
        /// Process trading signal at candle close
        /// Following strategy outline lines 56-59: entries/exits only at candle close
        /// </summary>
        public void ProcessSignalAtCandleClose(
            bool entryLongSignal,
            bool entryShortSignal,
            bool exitLongSignal,
            bool exitShortSignal,
            double currentPrice,
            double atrValue,
            DateTime candleTime)
        {
            // Check for exit signals first (priority over entries)
            if (currentPositionStatus == 1 && exitLongSignal)
            {
                // Exit long positions
                var signal = new TradeSignal
                {
                    Side = Side.Sell,
                    Type = SignalType.Exit,
                    Price = currentPrice,
                    Time = candleTime,
                    Reason = "Exit signal for long positions"
                };
                ProcessExitSignal(signal, atrValue);
            }
            else if (currentPositionStatus == -1 && exitShortSignal)
            {
                // Exit short positions
                var signal = new TradeSignal
                {
                    Side = Side.Buy,
                    Type = SignalType.Exit,
                    Price = currentPrice,
                    Time = candleTime,
                    Reason = "Exit signal for short positions"
                };
                ProcessExitSignal(signal, atrValue);
            }

            // Check for reversal signals (same candle flip)
            // Following strategy outline lines 50-52
            if (allowReversal && currentPositionStatus != 0)
            {
                if (currentPositionStatus == 1 && entryShortSignal)
                {
                    // Reverse from long to short
                    var signal = new TradeSignal
                    {
                        Side = Side.Sell,
                        Type = SignalType.Reversal,
                        Price = currentPrice,
                        Time = candleTime,
                        Reason = "Reversal from long to short"
                    };
                    ProcessReversalSignal(signal, atrValue);
                    return; // Reversal includes new entry
                }
                else if (currentPositionStatus == -1 && entryLongSignal)
                {
                    // Reverse from short to long
                    var signal = new TradeSignal
                    {
                        Side = Side.Buy,
                        Type = SignalType.Reversal,
                        Price = currentPrice,
                        Time = candleTime,
                        Reason = "Reversal from short to long"
                    };
                    ProcessReversalSignal(signal, atrValue);
                    return; // Reversal includes new entry
                }
            }

            // Check for new entry signals (including stacking)
            if (entryLongSignal && (currentPositionStatus >= 0))
            {
                // Check stacking limit
                // Following strategy outline lines 44-49
                if (currentStackCount < maxStackCount)
                {
                    var signal = new TradeSignal
                    {
                        Side = Side.Buy,
                        Type = SignalType.Entry,
                        Price = currentPrice,
                        Time = candleTime,
                        Reason = currentPositionStatus == 0 ? "New long entry" :
                                $"Stacked long entry #{currentStackCount + 1}"
                    };
                    ProcessEntrySignal(signal, atrValue);
                }
                else
                {
                    logAction?.Invoke($"Max stack count ({maxStackCount}) reached for long positions",
                        StrategyLoggingLevel.Trading);
                }
            }
            else if (entryShortSignal && (currentPositionStatus <= 0))
            {
                // Check stacking limit
                if (currentStackCount < maxStackCount)
                {
                    var signal = new TradeSignal
                    {
                        Side = Side.Sell,
                        Type = SignalType.Entry,
                        Price = currentPrice,
                        Time = candleTime,
                        Reason = currentPositionStatus == 0 ? "New short entry" :
                                $"Stacked short entry #{currentStackCount + 1}"
                    };
                    ProcessEntrySignal(signal, atrValue);
                }
                else
                {
                    logAction?.Invoke($"Max stack count ({maxStackCount}) reached for short positions",
                        StrategyLoggingLevel.Trading);
                }
            }
        }

        /// <summary>
        /// Process entry signal with stacking support
        /// </summary>
        private void ProcessEntrySignal(TradeSignal signal, double atrValue)
        {
            try
            {
                // Calculate slippage for backtesting
                // Following strategy outline lines 69-71
                double slippage = CalculateSlippage(signal.Side, atrValue);
                double entryPrice = ApplySlippage(signal.Price, signal.Side, slippage);

                // Check if we have a valid order type
                if (marketOrderType == null || string.IsNullOrEmpty(marketOrderType.Id))
                {
                    logAction?.Invoke("ERROR: Cannot place order - no valid market order type",
                        StrategyLoggingLevel.Error);
                    return;
                }

                // Place market order
                var request = new PlaceOrderRequestParameters
                {
                    Symbol = symbol,
                    Account = account,
                    Side = signal.Side,
                    Quantity = contractSize,
                    OrderTypeId = marketOrderType.Id, // Use the validated order type ID
                    TimeInForce = TimeInForce.IOC, // Immediate or cancel for market orders
                    Comment = $"{signal.Type}_{signal.Reason}_{DateTime.UtcNow:HHmmss}"
                };

                logAction?.Invoke($"Placing order: Side={request.Side}, Qty={request.Quantity}, OrderType={request.OrderTypeId}",
                    StrategyLoggingLevel.Trading);

                var result = Core.Instance.PlaceOrder(request);

                if (result.Status == TradingOperationResultStatus.Success)
                {
                    totalTradesPlaced++;

                    // Position tracking will be updated in OnPositionAdded event
                    // Just track stacking statistics here
                    if (currentStackCount > 0)
                    {
                        stackedPositionCount++;
                    }

                    logAction?.Invoke(
                        $"Entry order placed: {signal.Side} {contractSize} @ {entryPrice:F2} " +
                        $"(Slippage: {slippage:F4}) - {signal.Reason}",
                        StrategyLoggingLevel.Trading);

                    totalSlippage += Math.Abs(slippage);
                    lastOrderTime = signal.Time;
                }
                else
                {
                    logAction?.Invoke($"Failed to place entry order: {result.Message}",
                        StrategyLoggingLevel.Error);
                }
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Error processing entry signal: {ex.Message}",
                    StrategyLoggingLevel.Error);
            }
        }

        /// <summary>
        /// Process exit signal for all positions
        /// </summary>
        private void ProcessExitSignal(TradeSignal signal, double atrValue)
        {
            try
            {
                // Close all positions in the direction
                foreach (var position in activePositions.ToList())
                {
                    if ((currentPositionStatus == 1 && signal.Side == Side.Sell) ||
                        (currentPositionStatus == -1 && signal.Side == Side.Buy))
                    {
                        ClosePositionWithSlippage(position, signal, atrValue);
                    }
                }

                // Reset position tracking
                currentPositionStatus = 0;
                currentStackCount = 0;

                logAction?.Invoke($"Exit signal processed: {signal.Reason}",
                    StrategyLoggingLevel.Trading);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Error processing exit signal: {ex.Message}",
                    StrategyLoggingLevel.Error);
            }
        }

        /// <summary>
        /// Process reversal signal (close all and open opposite)
        /// Following strategy outline lines 50-52
        /// </summary>
        private void ProcessReversalSignal(TradeSignal signal, double atrValue)
        {
            try
            {
                logAction?.Invoke($"Processing reversal: {signal.Reason}",
                    StrategyLoggingLevel.Trading);

                // First close all existing positions
                CloseAllPositions("Reversal", atrValue);

                // Then open new position in opposite direction
                var newSide = currentPositionStatus == 1 ? Side.Sell : Side.Buy;
                var entrySignal = new TradeSignal
                {
                    Side = newSide,
                    Type = SignalType.Entry,
                    Price = signal.Price,
                    Time = signal.Time,
                    Reason = $"Reversal entry to {newSide}"
                };

                // Reset tracking before new entry
                currentPositionStatus = 0;
                currentStackCount = 0;

                ProcessEntrySignal(entrySignal, atrValue);
                reversalCount++;

                logAction?.Invoke($"[REVERSAL] Completed: {(signal.Side == Side.Sell ? Side.Buy : Side.Sell)} -> {newSide} | " +
                    $"Price: {signal.Price:F2} | Stack: {currentStackCount}",
                    StrategyLoggingLevel.Trading);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Error processing reversal signal: {ex.Message}",
                    StrategyLoggingLevel.Error);
            }
        }

        /// <summary>
        /// Close position with slippage simulation
        /// </summary>
        private void ClosePositionWithSlippage(Position position, TradeSignal signal, double atrValue)
        {
            try
            {
                // Calculate slippage
                double slippage = CalculateSlippage(signal.Side, atrValue);
                double exitPrice = ApplySlippage(signal.Price, signal.Side, slippage);

                // Close position
                var result = position.Close();

                if (result.Status == TradingOperationResultStatus.Success)
                {
                    logAction?.Invoke(
                        $"Position closed: {signal.Side} @ {exitPrice:F2} " +
                        $"(Slippage: {slippage:F4}) - PnL: {position.GrossPnL.Value:F2}",
                        StrategyLoggingLevel.Trading);

                    totalSlippage += Math.Abs(slippage);
                }
                else
                {
                    logAction?.Invoke($"Failed to close position: {result.Message}",
                        StrategyLoggingLevel.Error);
                }
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Error closing position: {ex.Message}",
                    StrategyLoggingLevel.Error);
            }
        }

        /// <summary>
        /// Calculate slippage based on ATR
        /// Following strategy outline lines 69-71
        /// </summary>
        private double CalculateSlippage(Side side, double atrValue)
        {
            if (slippageAtrMultiplier <= 0 || atrValue <= 0)
                return 0;

            // Slippage = ATR * multiplier
            double baseSlippage = atrValue * slippageAtrMultiplier;

            // Add some randomness for realistic simulation (±20% variation)
            Random rand = new Random();
            double variation = (rand.NextDouble() - 0.5) * 0.4; // -0.2 to +0.2
            double slippage = baseSlippage * (1 + variation);

            // Round to tick size
            slippage = Math.Round(slippage / symbol.TickSize) * symbol.TickSize;

            return slippage;
        }

        /// <summary>
        /// Apply slippage to price based on side
        /// </summary>
        private double ApplySlippage(double price, Side side, double slippage)
        {
            // Buys execute at higher price (negative for trader)
            // Sells execute at lower price (negative for trader)
            if (side == Side.Buy)
            {
                return price + Math.Abs(slippage);
            }
            else
            {
                return price - Math.Abs(slippage);
            }
        }

        /// <summary>
        /// Update active positions list
        /// </summary>
        public void UpdateActivePositions(IList<Position> positions)
        {
            activePositions = positions.Where(p =>
                p.Symbol == symbol && p.Account == account).ToList();

            // Recalculate position status
            if (!activePositions.Any())
            {
                currentPositionStatus = 0;
                currentStackCount = 0;
            }
            else
            {
                currentStackCount = activePositions.Count;
                // Determine direction from first position
                var firstPosition = activePositions.First();
                currentPositionStatus = firstPosition.Side == Side.Buy ? 1 : -1;
            }
        }

        /// <summary>
        /// Get current stack count for synchronization
        /// </summary>
        public int GetCurrentStackCount()
        {
            return currentStackCount;
        }

        /// <summary>
        /// Get current position status for synchronization
        /// </summary>
        public int GetCurrentPositionStatus()
        {
            return currentPositionStatus;
        }

        /// <summary>
        /// Close all positions
        /// </summary>
        public void CloseAllPositions(string reason, double atrValue)
        {
            foreach (var position in activePositions.ToList())
            {
                var signal = new TradeSignal
                {
                    Side = position.Side == Side.Buy ? Side.Sell : Side.Buy,
                    Type = SignalType.Exit,
                    Price = symbol != null ? symbol.Last : position.CurrentPrice,
                    Time = DateTime.UtcNow,
                    Reason = reason
                };

                ClosePositionWithSlippage(position, signal, atrValue);
            }

            currentPositionStatus = 0;
            currentStackCount = 0;
        }

        /// <summary>
        /// Get current position status
        /// </summary>
        public string GetPositionStatus()
        {
            if (currentPositionStatus == 0)
                return $"Flat (no positions)";
            else if (currentPositionStatus == 1)
                return $"Long ({currentStackCount} positions)";
            else
                return $"Short ({currentStackCount} positions)";
        }

        /// <summary>
        /// Get order statistics
        /// </summary>
        public Dictionary<string, object> GetStatistics()
        {
            return new Dictionary<string, object>
            {
                { "TotalTrades", totalTradesPlaced },
                { "Reversals", reversalCount },
                { "StackedPositions", stackedPositionCount },
                { "TotalSlippage", totalSlippage },
                { "CurrentStatus", currentPositionStatus },
                { "CurrentStack", currentStackCount }
            };
        }

        /// <summary>
        /// Check if can enter new position
        /// </summary>
        public bool CanEnterPosition(Side side)
        {
            // Check if we're flat or in same direction
            if (currentPositionStatus == 0)
                return true;

            // Check if same direction and under stack limit
            if ((side == Side.Buy && currentPositionStatus == 1) ||
                (side == Side.Sell && currentPositionStatus == -1))
            {
                return currentStackCount < maxStackCount;
            }

            // Check if reversal is allowed
            if (allowReversal)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reset order manager state
        /// </summary>
        public void Reset()
        {
            activePositions.Clear();
            pendingOrders.Clear();
            currentPositionStatus = 0;
            currentStackCount = 0;
            lastOrderTime = DateTime.MinValue;

            logAction?.Invoke("Order manager reset", StrategyLoggingLevel.Trading);
        }
    }

    #endregion


    #region === RISK MANAGEMENT ===

    /// <summary>
    /// Daily PnL tracking information
    /// </summary>
    public class DailyPnLTracker
    {
        public DateTime Date { get; set; }
        public double RealizedPnL { get; set; }
        public double UnrealizedPnL { get; set; }
        public double TotalPnL => RealizedPnL + UnrealizedPnL;
        public int TradeCount { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public double MaxDrawdown { get; set; }
        public double MaxProfit { get; set; }
        public DateTime LastUpdateTime { get; set; }
        public bool IsHalted { get; set; }
        public string HaltReason { get; set; }

        public DailyPnLTracker()
        {
            Date = DateTime.UtcNow.Date;
            Reset();
        }

        public void Reset()
        {
            RealizedPnL = 0;
            UnrealizedPnL = 0;
            TradeCount = 0;
            WinningTrades = 0;
            LosingTrades = 0;
            MaxDrawdown = 0;
            MaxProfit = 0;
            LastUpdateTime = DateTime.UtcNow;
            IsHalted = false;
            HaltReason = string.Empty;
        }

        public double GetWinRate()
        {
            if (TradeCount == 0) return 0;
            return (double)WinningTrades / TradeCount * 100;
        }
    }

    /// <summary>
    /// Manages risk limits and daily loss tracking
    /// Following strategy outline lines 72-75
    /// </summary>
    public class RiskManager
    {
        // Configuration
        private readonly double maxDailyLoss;
        private readonly double maxPositionSize;
        private readonly double contractSize;
        private readonly Symbol symbol;
        private readonly Account account;

        // Dependencies
        private readonly TimeFilterManager timeFilterManager;
        private readonly Action<string, StrategyLoggingLevel> logAction;

        // State tracking
        private DailyPnLTracker currentDayTracker;
        private Dictionary<DateTime, DailyPnLTracker> historicalTrackers;
        private DateTime lastResetTime;
        private DateTime currentTradingPeriodStart;
        private bool isTradingHalted;
        private string haltReason;

        // Statistics
        private double totalRealizedPnL;
        private double peakEquity;
        private double maxDrawdownAllTime;
        private int totalTrades;
        private int consecutiveLosses;
        private int maxConsecutiveLosses;

        public RiskManager(
            double maxDailyLoss,
            double maxPositionSize,
            double contractSize,
            Symbol symbol,
            Account account,
            TimeFilterManager timeFilterManager,
            Action<string, StrategyLoggingLevel> logger)
        {
            this.maxDailyLoss = maxDailyLoss;
            this.maxPositionSize = maxPositionSize;
            this.contractSize = contractSize;
            this.symbol = symbol;
            this.account = account;
            this.timeFilterManager = timeFilterManager;
            this.logAction = logger;

            this.currentDayTracker = new DailyPnLTracker();
            this.historicalTrackers = new Dictionary<DateTime, DailyPnLTracker>();
            this.lastResetTime = DateTime.MinValue;
            this.isTradingHalted = false;

            InitializeTracking();
        }

        /// <summary>
        /// Initialize risk tracking
        /// </summary>
        private void InitializeTracking()
        {
            // Get initial account equity
            if (account != null)
            {
                peakEquity = account.Balance;
            }

            logAction?.Invoke($"Risk Manager initialized - Max Daily Loss: ${maxDailyLoss:F2}, " +
                            $"Max Position Size: {maxPositionSize}",
                            StrategyLoggingLevel.Info);
        }

        /// <summary>
        /// Check if trading should be halted due to risk limits
        /// </summary>
        public bool ShouldHaltTrading(DateTime currentTime)
        {
            // Check if we need to reset based on time frame change
            CheckForTimeFrameReset(currentTime);

            // If already halted, check if we should resume
            if (isTradingHalted)
            {
                // Trading resumes at the start of next time frame
                if (HasEnteredNewTimeFrame(currentTime))
                {
                    ResumeTrading("New time frame started");
                }
                else
                {
                    return true; // Still halted
                }
            }

            // Check max daily loss
            if (maxDailyLoss > 0 && Math.Abs(currentDayTracker.TotalPnL) >= maxDailyLoss)
            {
                HaltTrading($"Max daily loss reached: ${currentDayTracker.TotalPnL:F2}");
                return true;
            }

            // Additional risk checks can be added here
            // For example: consecutive losses, drawdown percentage, etc.

            return false;
        }

        /// <summary>
        /// Check if we've entered a new time frame and should reset
        /// Following strategy outline lines 73-74: reset at start of time frame
        /// </summary>
        private void CheckForTimeFrameReset(DateTime currentTime)
        {
            if (timeFilterManager == null)
                return;

            // Check if we've entered a new trading period
            if (HasEnteredNewTimeFrame(currentTime))
            {
                ResetDailyTracking(currentTime);
            }
        }

        /// <summary>
        /// Check if we've entered a new time frame
        /// </summary>
        private bool HasEnteredNewTimeFrame(DateTime currentTime)
        {
            // Convert to EST for time frame checking
            DateTime estTime = TimeZoneUtilities.ConvertBrokerTimeToEST(currentTime);

            // Check if it's a new trading day
            if (estTime.Date != currentDayTracker.Date)
            {
                return true;
            }

            // Check if we've entered a new trading period within the day
            // This depends on how time frames are defined in your strategy
            // For now, we'll use the time filter manager's periods
            if (timeFilterManager != null)
            {
                var periods = timeFilterManager.GetAllPeriods();
                foreach (var period in periods.Where(p => p.IsEnabled))
                {
                    if (period.IsTimeInPeriod(estTime) && !period.IsTimeInPeriod(
                        TimeZoneUtilities.ConvertBrokerTimeToEST(lastResetTime)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Reset daily tracking for new time frame
        /// </summary>
        private void ResetDailyTracking(DateTime currentTime)
        {
            // Save current tracker to history
            if (currentDayTracker.TradeCount > 0)
            {
                historicalTrackers[currentDayTracker.Date] = currentDayTracker;

                logAction?.Invoke($"[DAILY RESET] Summary for {currentDayTracker.Date:yyyy-MM-dd} | " +
                                $"PnL: ${currentDayTracker.TotalPnL:F2} | " +
                                $"Trades: {currentDayTracker.TradeCount} | " +
                                $"Wins: {currentDayTracker.WinningTrades} | " +
                                $"Losses: {currentDayTracker.LosingTrades} | " +
                                $"Win Rate: {currentDayTracker.GetWinRate():F1}% | " +
                                $"Max DD: ${currentDayTracker.MaxDrawdown:F2}",
                                StrategyLoggingLevel.Info);
            }

            // Create new tracker
            currentDayTracker = new DailyPnLTracker
            {
                Date = TimeZoneUtilities.ConvertBrokerTimeToEST(currentTime).Date
            };

            lastResetTime = currentTime;
            currentTradingPeriodStart = currentTime;

            // Reset halt status for new period
            if (isTradingHalted && haltReason.Contains("daily loss"))
            {
                ResumeTrading("Daily reset - new time frame");
            }

            logAction?.Invoke($"Risk tracking reset for new time frame: {currentDayTracker.Date:yyyy-MM-dd}",
                            StrategyLoggingLevel.Trading);
        }

        /// <summary>
        /// Update PnL when a position is closed
        /// </summary>
        public void UpdateRealizedPnL(Position position)
        {
            double pnl = position.NetPnL.Value;

            // Update daily tracker
            currentDayTracker.RealizedPnL += pnl;
            currentDayTracker.TradeCount++;
            currentDayTracker.LastUpdateTime = DateTime.UtcNow;

            if (pnl > 0)
            {
                currentDayTracker.WinningTrades++;
                consecutiveLosses = 0;
            }
            else if (pnl < 0)
            {
                currentDayTracker.LosingTrades++;
                consecutiveLosses++;
                maxConsecutiveLosses = Math.Max(maxConsecutiveLosses, consecutiveLosses);
            }

            // Update max drawdown and profit
            if (currentDayTracker.TotalPnL < currentDayTracker.MaxDrawdown)
            {
                currentDayTracker.MaxDrawdown = currentDayTracker.TotalPnL;
            }
            if (currentDayTracker.TotalPnL > currentDayTracker.MaxProfit)
            {
                currentDayTracker.MaxProfit = currentDayTracker.TotalPnL;
            }

            // Update all-time statistics
            totalRealizedPnL += pnl;
            totalTrades++;

            // Check if we should halt trading
            if (ShouldHaltTrading(DateTime.UtcNow))
            {
                logAction?.Invoke($"Trading halted after position close - Daily PnL: ${currentDayTracker.TotalPnL:F2}",
                                StrategyLoggingLevel.Error);
            }
            else
            {
                logAction?.Invoke($"Position PnL: ${pnl:F2}, Daily Total: ${currentDayTracker.TotalPnL:F2} " +
                                $"(Limit: ${maxDailyLoss:F2})",
                                StrategyLoggingLevel.Trading);
            }
        }

        /// <summary>
        /// Update unrealized PnL for open positions
        /// </summary>
        public void UpdateUnrealizedPnL(List<Position> openPositions)
        {
            double unrealizedPnL = 0;

            foreach (var position in openPositions)
            {
                unrealizedPnL += position.GrossPnL.Value;
            }

            currentDayTracker.UnrealizedPnL = unrealizedPnL;
            currentDayTracker.LastUpdateTime = DateTime.UtcNow;

            // Update equity tracking
            if (account != null)
            {
                double currentEquity = account.Balance;
                if (currentEquity > peakEquity)
                {
                    peakEquity = currentEquity;
                }

                double drawdown = peakEquity - currentEquity;
                if (drawdown > maxDrawdownAllTime)
                {
                    maxDrawdownAllTime = drawdown;
                }
            }
        }

        /// <summary>
        /// Validate position size before entry
        /// </summary>
        public bool ValidatePositionSize(double requestedSize, List<Position> currentPositions)
        {
            // Check max position size
            if (maxPositionSize > 0 && requestedSize > maxPositionSize)
            {
                logAction?.Invoke($"Position size {requestedSize} exceeds max allowed {maxPositionSize}",
                                StrategyLoggingLevel.Error);
                return false;
            }

            // Calculate total exposure
            double totalExposure = requestedSize;
            foreach (var position in currentPositions)
            {
                totalExposure += position.Quantity;
            }

            // Check if total exposure exceeds limits
            double maxTotalExposure = maxPositionSize * 3; // Assuming max 3 stacked positions
            if (totalExposure > maxTotalExposure)
            {
                logAction?.Invoke($"Total exposure {totalExposure} would exceed max allowed {maxTotalExposure}",
                                StrategyLoggingLevel.Error);
                return false;
            }

            // Check account margin requirements
            if (account != null)
            {
                double requiredMargin = CalculateRequiredMargin(requestedSize);
                double availableBalance = account.Balance;
                if (requiredMargin > availableBalance)
                {
                    logAction?.Invoke($"Insufficient balance. Required: ${requiredMargin:F2}, " +
                                    $"Available: ${availableBalance:F2}",
                                    StrategyLoggingLevel.Error);
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Calculate required margin for position
        /// </summary>
        private double CalculateRequiredMargin(double positionSize)
        {
            // This is a simplified calculation
            // Actual margin requirements depend on broker and symbol
            double notionalValue = positionSize * (symbol != null ? symbol.Last : 0);
            double marginRate = 0.05; // 5% margin requirement (example)
            return notionalValue * marginRate;
        }

        /// <summary>
        /// Halt trading due to risk limit
        /// </summary>
        private void HaltTrading(string reason)
        {
            isTradingHalted = true;
            haltReason = reason;
            currentDayTracker.IsHalted = true;
            currentDayTracker.HaltReason = reason;

            logAction?.Invoke($"[RISK HALT] TRADING HALTED: {reason} | " +
                $"Daily PnL: ${currentDayTracker.TotalPnL:F2} | " +
                $"Trades Today: {currentDayTracker.TradeCount}",
                StrategyLoggingLevel.Error);
        }

        /// <summary>
        /// Resume trading after halt
        /// </summary>
        private void ResumeTrading(string reason)
        {
            isTradingHalted = false;
            haltReason = string.Empty;
            currentDayTracker.IsHalted = false;
            currentDayTracker.HaltReason = string.Empty;

            logAction?.Invoke($"Trading resumed: {reason}", StrategyLoggingLevel.Info);
        }

        /// <summary>
        /// Check if trading is currently halted
        /// </summary>
        public bool IsTradingHalted()
        {
            return isTradingHalted;
        }

        /// <summary>
        /// Get halt reason if halted
        /// </summary>
        public string GetHaltReason()
        {
            return haltReason;
        }

        /// <summary>
        /// Get current day PnL
        /// </summary>
        public double GetDailyPnL()
        {
            return currentDayTracker.TotalPnL;
        }

        /// <summary>
        /// Get remaining loss allowed today
        /// </summary>
        public double GetRemainingDailyLossAllowed()
        {
            if (maxDailyLoss <= 0) return double.MaxValue;
            return maxDailyLoss - Math.Abs(currentDayTracker.TotalPnL);
        }

        /// <summary>
        /// Get risk statistics
        /// </summary>
        public Dictionary<string, object> GetStatistics()
        {
            return new Dictionary<string, object>
            {
                { "DailyPnL", currentDayTracker.TotalPnL },
                { "DailyRealizedPnL", currentDayTracker.RealizedPnL },
                { "DailyUnrealizedPnL", currentDayTracker.UnrealizedPnL },
                { "DailyTradeCount", currentDayTracker.TradeCount },
                { "DailyWinRate", currentDayTracker.GetWinRate() },
                { "TotalRealizedPnL", totalRealizedPnL },
                { "TotalTrades", totalTrades },
                { "MaxDrawdown", maxDrawdownAllTime },
                { "ConsecutiveLosses", consecutiveLosses },
                { "MaxConsecutiveLosses", maxConsecutiveLosses },
                { "IsHalted", isTradingHalted },
                { "HaltReason", haltReason }
            };
        }

        /// <summary>
        /// Get daily summary for logging
        /// </summary>
        public string GetDailySummary()
        {
            double pnlPercentage = maxDailyLoss > 0 ? (currentDayTracker.TotalPnL / maxDailyLoss * 100) : 0;
            return $"Daily PnL: ${currentDayTracker.TotalPnL:F2}/{maxDailyLoss:F2} " +
                   $"({pnlPercentage:F1}%), " +
                   $"Trades: {currentDayTracker.TradeCount}, " +
                   $"Win Rate: {currentDayTracker.GetWinRate():F1}%, " +
                   $"Status: {(isTradingHalted ? "HALTED" : "Active")}";
        }

        /// <summary>
        /// Reset all tracking
        /// </summary>
        public void Reset()
        {
            currentDayTracker.Reset();
            historicalTrackers.Clear();
            isTradingHalted = false;
            haltReason = string.Empty;
            totalRealizedPnL = 0;
            totalTrades = 0;
            consecutiveLosses = 0;
            maxConsecutiveLosses = 0;

            logAction?.Invoke("Risk manager reset", StrategyLoggingLevel.Info);
        }
    }

    #endregion


    #region === LOGGING MANAGEMENT ===

    /// <summary>
    /// Trade log entry with all required details
    /// </summary>
    public class TradeLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Action { get; set; } // Entry, Exit, Reversal, SL Hit, TP Hit
        public Side Side { get; set; }
        public double Price { get; set; }
        public double Quantity { get; set; }
        public string Reason { get; set; }
        public Dictionary<string, double> SignalValues { get; set; }
        public double PnL { get; set; }
        public double DailyPnL { get; set; }
        public int StackCount { get; set; }
        public string TimeFrame { get; set; }

        public TradeLogEntry()
        {
            SignalValues = new Dictionary<string, double>();
        }

        public string ToLogString()
        {
            var sb = new StringBuilder();
            sb.Append($"[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] ");
            sb.Append($"{Action} - {Side} {Quantity} @ {Price:F2} ");
            sb.Append($"| Reason: {Reason} ");

            if (SignalValues.Any())
            {
                sb.Append("| Signals: ");
                foreach (var signal in SignalValues)
                {
                    sb.Append($"{signal.Key}={signal.Value:F4} ");
                }
            }

            if (Action.Contains("Exit") || Action.Contains("Hit"))
            {
                sb.Append($"| PnL: ${PnL:F2} ");
                sb.Append($"| Daily Total: ${DailyPnL:F2} ");
            }

            if (StackCount > 0)
            {
                sb.Append($"| Stack: {StackCount} ");
            }

            if (!string.IsNullOrEmpty(TimeFrame))
            {
                sb.Append($"| Period: {TimeFrame}");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Parameter log entry for 3-candle intervals
    /// </summary>
    public class ParameterLogEntry
    {
        public DateTime Timestamp { get; set; }
        public int CandleNumber { get; set; }
        public double RVOL { get; set; }
        public double VolumeDelta { get; set; }
        public double PriceMove { get; set; }
        public double ATR { get; set; }
        public double CustomHMA { get; set; }
        public double VDStrength { get; set; }
        public double VDPriceRatio { get; set; }
        public double VDVolumeRatio { get; set; }
        public bool VDDivergence { get; set; }
        public double CurrentPrice { get; set; }
        public double ClosestHigh { get; set; }
        public double ClosestLow { get; set; }
        public double StopLossUp { get; set; }
        public double StopLossDown { get; set; }
        public string TimeStatus { get; set; }
        public string RiskStatus { get; set; }

        public string ToLogString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== 3-CANDLE PARAMETER LOG #{CandleNumber} [{Timestamp:yyyy-MM-dd HH:mm:ss}] ===");
            sb.AppendLine($"Price: {CurrentPrice:F2} | ATR: {ATR:F4} | Price Move: {PriceMove:F4}");
            sb.AppendLine($"RVOL: {RVOL:F4} | Volume Delta: {VolumeDelta:F2}");
            sb.AppendLine($"VD Strength: {VDStrength:F4} | VD/Price Ratio: {VDPriceRatio:F4} | VD/Volume Ratio: {VDVolumeRatio:F4}");
            sb.AppendLine($"Custom HMA: {CustomHMA:F4} | VD Divergence: {VDDivergence}");
            sb.AppendLine($"Session Levels - High: {ClosestHigh:F2} | Low: {ClosestLow:F2}");
            sb.AppendLine($"Stop Loss Levels - Up: {StopLossUp:F2} | Down: {StopLossDown:F2}");
            sb.AppendLine($"Time Status: {TimeStatus}");
            sb.AppendLine($"Risk Status: {RiskStatus}");
            sb.AppendLine("=====================================");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Comprehensive logging manager for all strategy events
    /// Following strategy outline lines 76-89
    /// </summary>
    public class LoggingManager
    {
        private readonly Action<string, StrategyLoggingLevel> logAction;
        private readonly string logFilePath;
        private readonly bool enableFileLogging;
        private readonly object logLock = new object();

        // Log storage
        private List<TradeLogEntry> tradeLog;
        private List<ParameterLogEntry> parameterLog;
        private List<string> stopLossUpdateLog;
        private List<string> takeProfitUpdateLog;
        private List<string> reversalLog;
        private List<string> timeFilterLog;
        private List<string> riskEventLog;

        // Statistics
        private int totalLogsWritten;
        private DateTime startTime;

        public LoggingManager(
            Action<string, StrategyLoggingLevel> logger,
            bool enableFileLogging = false,
            string logDirectory = null)
        {
            this.logAction = logger;
            this.enableFileLogging = enableFileLogging;

            if (enableFileLogging && !string.IsNullOrEmpty(logDirectory))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                this.logFilePath = Path.Combine(logDirectory, $"FlagshipStrategy_{timestamp}.log");
            }

            InitializeLogs();
            startTime = DateTime.UtcNow;
        }

        private void InitializeLogs()
        {
            tradeLog = new List<TradeLogEntry>();
            parameterLog = new List<ParameterLogEntry>();
            stopLossUpdateLog = new List<string>();
            takeProfitUpdateLog = new List<string>();
            reversalLog = new List<string>();
            timeFilterLog = new List<string>();
            riskEventLog = new List<string>();
        }

        /// <summary>
        /// Log trade entry with signal values
        /// Following strategy outline lines 76-78
        /// </summary>
        public void LogTradeEntry(
            DateTime timestamp,
            Side side,
            double price,
            double quantity,
            string reason,
            SignalSnapshot signals,
            int stackCount,
            string timeFrame)
        {
            var entry = new TradeLogEntry
            {
                Timestamp = timestamp,
                Action = stackCount > 1 ? $"Stacked Entry #{stackCount}" : "Entry",
                Side = side,
                Price = price,
                Quantity = quantity,
                Reason = reason,
                StackCount = stackCount,
                TimeFrame = timeFrame
            };

            // Add signal values that triggered the trade
            if (signals != null)
            {
                entry.SignalValues["RVOL"] = signals.RvolNormalized;
                entry.SignalValues["VD"] = signals.CurrentVd;
                entry.SignalValues["VDStrength"] = signals.VdStrength;
                entry.SignalValues["VDPriceRatio"] = signals.VdPriceRatio;
                entry.SignalValues["VDVolumeRatio"] = signals.VdVolumeRatio;
                entry.SignalValues["PriceMove"] = signals.CurrentPriceMove;
            }

            tradeLog.Add(entry);

            string logMessage = entry.ToLogString();
            LogMessage(logMessage, StrategyLoggingLevel.Trading);
            WriteToFile(logMessage);
        }

        /// <summary>
        /// Log trade exit with PnL
        /// Following strategy outline lines 79-80
        /// </summary>
        public void LogTradeExit(
            DateTime timestamp,
            Side side,
            double price,
            double quantity,
            string reason,
            double pnl,
            double dailyPnL,
            string timeFrame)
        {
            var entry = new TradeLogEntry
            {
                Timestamp = timestamp,
                Action = reason.Contains("SL") ? "SL Hit" :
                        reason.Contains("TP") ? "TP Hit" : "Exit",
                Side = side,
                Price = price,
                Quantity = quantity,
                Reason = reason,
                PnL = pnl,
                DailyPnL = dailyPnL,
                TimeFrame = timeFrame
            };

            tradeLog.Add(entry);

            string logMessage = entry.ToLogString();
            LogMessage(logMessage, StrategyLoggingLevel.Trading);
            WriteToFile(logMessage);
        }

        /// <summary>
        /// Log position reversal
        /// Following strategy outline line 84
        /// </summary>
        public void LogReversal(
            DateTime timestamp,
            Side fromSide,
            Side toSide,
            double price,
            SignalSnapshot signals,
            string reason)
        {
            string message = $"[{timestamp:yyyy-MM-dd HH:mm:ss}] REVERSAL: {fromSide} -> {toSide} @ {price:F2} | {reason}";

            if (signals != null)
            {
                message += $" | Signals: RVOL={signals.RvolNormalized:F4}, VD={signals.CurrentVd:F2}";
            }

            reversalLog.Add(message);
            LogMessage(message, StrategyLoggingLevel.Trading);
            WriteToFile(message);
        }

        /// <summary>
        /// Log stop loss update
        /// Following strategy outline lines 81-82
        /// </summary>
        public void LogStopLossUpdate(
            DateTime timestamp,
            string positionId,
            Side side,
            double oldStop,
            double newStop,
            double currentPrice)
        {
            string message = $"[{timestamp:yyyy-MM-dd HH:mm:ss}] SL UPDATE: {side} position " +
                           $"| Old: {oldStop:F2} -> New: {newStop:F2} " +
                           $"| Price: {currentPrice:F2} " +
                           $"| Distance: {Math.Abs(currentPrice - newStop):F2}";

            stopLossUpdateLog.Add(message);
            LogMessage(message, StrategyLoggingLevel.Trading);
            WriteToFile(message);
        }

        /// <summary>
        /// Log take profit update
        /// </summary>
        public void LogTakeProfitUpdate(
            DateTime timestamp,
            string positionId,
            Side side,
            double takeProfitLevel,
            string sessionType,
            double currentPrice)
        {
            string message = $"[{timestamp:yyyy-MM-dd HH:mm:ss}] TP SET: {side} position " +
                           $"| Level: {takeProfitLevel:F2} from {sessionType} " +
                           $"| Price: {currentPrice:F2} " +
                           $"| Distance: {Math.Abs(takeProfitLevel - currentPrice):F2}";

            takeProfitUpdateLog.Add(message);
            LogMessage(message, StrategyLoggingLevel.Trading);
            WriteToFile(message);
        }

        /// <summary>
        /// Log parameter values every 3 candles
        /// Following strategy outline lines 79-80
        /// </summary>
        public void LogParameterSnapshot(
            DateTime timestamp,
            int candleNumber,
            SignalSnapshot signals,
            double atr,
            double customHma,
            double closestHigh,
            double closestLow,
            double stopUp,
            double stopDown,
            string timeStatus,
            string riskStatus)
        {
            var entry = new ParameterLogEntry
            {
                Timestamp = timestamp,
                CandleNumber = candleNumber / 3,
                RVOL = signals?.RvolNormalized ?? 0,
                VolumeDelta = signals?.CurrentVd ?? 0,
                PriceMove = signals?.CurrentPriceMove ?? 0,
                ATR = atr,
                CustomHMA = customHma,
                VDStrength = signals?.VdStrength ?? 0,
                VDPriceRatio = signals?.VdPriceRatio ?? 0,
                VDVolumeRatio = signals?.VdVolumeRatio ?? 0,
                VDDivergence = signals?.VdDivergenceLongOkay ?? false,
                CurrentPrice = signals?.CurrentPrice ?? 0,
                ClosestHigh = closestHigh,
                ClosestLow = closestLow,
                StopLossUp = stopUp,
                StopLossDown = stopDown,
                TimeStatus = timeStatus,
                RiskStatus = riskStatus
            };

            parameterLog.Add(entry);

            string logMessage = entry.ToLogString();
            LogMessage(logMessage, StrategyLoggingLevel.Info);
            WriteToFile(logMessage);
        }

        /// <summary>
        /// Log time filter events
        /// Following strategy outline line 86
        /// </summary>
        public void LogTimeFilterEvent(
            DateTime timestamp,
            string eventType,
            string details)
        {
            string message = $"[{timestamp:yyyy-MM-dd HH:mm:ss}] TIME FILTER: {eventType} | {details}";

            timeFilterLog.Add(message);
            LogMessage(message, StrategyLoggingLevel.Trading);
            WriteToFile(message);
        }

        /// <summary>
        /// Log risk management events
        /// Following strategy outline lines 85, 87
        /// </summary>
        public void LogRiskEvent(
            DateTime timestamp,
            string eventType,
            double currentPnL,
            double maxLoss,
            string action)
        {
            string message = $"[{timestamp:yyyy-MM-dd HH:mm:ss}] RISK EVENT: {eventType} " +
                           $"| PnL: ${currentPnL:F2} / Max: ${maxLoss:F2} " +
                           $"| Action: {action}";

            riskEventLog.Add(message);
            LogMessage(message, StrategyLoggingLevel.Error);
            WriteToFile(message);
        }

        /// <summary>
        /// Log daily PnL reset
        /// Following strategy outline line 85
        /// </summary>
        public void LogDailyReset(
            DateTime timestamp,
            double finalPnL,
            int tradeCount,
            double winRate,
            string newPeriod)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[{timestamp:yyyy-MM-dd HH:mm:ss}] === DAILY RESET ===");
            sb.AppendLine($"Final PnL: ${finalPnL:F2}");
            sb.AppendLine($"Trade Count: {tradeCount}");
            sb.AppendLine($"Win Rate: {winRate:F1}%");
            sb.AppendLine($"New Period: {newPeriod}");
            sb.AppendLine("================================");

            string message = sb.ToString();
            riskEventLog.Add(message);
            LogMessage(message, StrategyLoggingLevel.Info);
            WriteToFile(message);
        }

        /// <summary>
        /// Generate summary report
        /// </summary>
        public string GenerateSummaryReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n========== STRATEGY EXECUTION SUMMARY ==========");
            sb.AppendLine($"Start Time: {startTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"End Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Duration: {(DateTime.UtcNow - startTime).TotalHours:F1} hours");
            sb.AppendLine();

            sb.AppendLine("=== TRADE STATISTICS ===");
            sb.AppendLine($"Total Trades: {tradeLog.Count(t => t.Action.Contains("Entry"))}");
            sb.AppendLine($"Reversals: {reversalLog.Count}");
            sb.AppendLine($"SL Hits: {tradeLog.Count(t => t.Action == "SL Hit")}");
            sb.AppendLine($"TP Hits: {tradeLog.Count(t => t.Action == "TP Hit")}");
            sb.AppendLine();

            sb.AppendLine("=== LOG STATISTICS ===");
            sb.AppendLine($"Trade Logs: {tradeLog.Count}");
            sb.AppendLine($"Parameter Logs: {parameterLog.Count}");
            sb.AppendLine($"SL Updates: {stopLossUpdateLog.Count}");
            sb.AppendLine($"TP Updates: {takeProfitUpdateLog.Count}");
            sb.AppendLine($"Time Filter Events: {timeFilterLog.Count}");
            sb.AppendLine($"Risk Events: {riskEventLog.Count}");
            sb.AppendLine($"Total Logs Written: {totalLogsWritten}");
            sb.AppendLine("==============================================\n");

            return sb.ToString();
        }

        /// <summary>
        /// Internal logging method
        /// </summary>
        private void LogMessage(string message, StrategyLoggingLevel level)
        {
            logAction?.Invoke(message, level);
            totalLogsWritten++;
        }

        /// <summary>
        /// Write to file if enabled
        /// </summary>
        private void WriteToFile(string message)
        {
            if (!enableFileLogging || string.IsNullOrEmpty(logFilePath))
                return;

            try
            {
                lock (logLock)
                {
                    File.AppendAllText(logFilePath, message + Environment.NewLine);
                }
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Failed to write to log file: {ex.Message}",
                    StrategyLoggingLevel.Error);
            }
        }

        /// <summary>
        /// Export logs to CSV for analysis
        /// </summary>
        public void ExportTradesToCsv(string filePath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Timestamp,Action,Side,Price,Quantity,Reason,PnL,DailyPnL,RVOL,VD,StackCount");

                foreach (var trade in tradeLog)
                {
                    sb.AppendLine($"{trade.Timestamp:yyyy-MM-dd HH:mm:ss}," +
                                $"{trade.Action}," +
                                $"{trade.Side}," +
                                $"{trade.Price}," +
                                $"{trade.Quantity}," +
                                $"\"{trade.Reason}\"," +
                                $"{trade.PnL}," +
                                $"{trade.DailyPnL}," +
                                $"{(trade.SignalValues.ContainsKey("RVOL") ? trade.SignalValues["RVOL"] : 0)}," +
                                $"{(trade.SignalValues.ContainsKey("VD") ? trade.SignalValues["VD"] : 0)}," +
                                $"{trade.StackCount}");
                }

                File.WriteAllText(filePath, sb.ToString());
                logAction?.Invoke($"Trade log exported to: {filePath}", StrategyLoggingLevel.Info);
            }
            catch (Exception ex)
            {
                logAction?.Invoke($"Failed to export trades to CSV: {ex.Message}",
                    StrategyLoggingLevel.Error);
            }
        }
    }

    #endregion



    #region === HELPER CLASSES ===

    /// <summary>
    /// Snapshot of all signal states at a specific point in time
    /// </summary>
    public class SignalSnapshot
    {
        public DateTime Timestamp { get; set; }

        // RVOL Signals
        public bool RvolLongOkay { get; set; }
        public bool RvolShortOkay { get; set; }

        // VD Strength Signals
        public bool VdStrengthLongOkay { get; set; }
        public bool VdStrengthShortOkay { get; set; }

        // VD Price Ratio Signals
        public bool VdPriceRatioLongOkay { get; set; }
        public bool VdPriceRatioShortOkay { get; set; }

        // Custom HMA Signals
        public bool CustomHmaLongOkay { get; set; }
        public bool CustomHmaShortOkay { get; set; }

        // VD Volume Ratio Signals
        public bool VdVolumeRatioLongOkay { get; set; }
        public bool VdVolumeRatioShortOkay { get; set; }

        // VD Divergence Signals
        public bool VdDivergenceLongOkay { get; set; }
        public bool VdDivergenceShortOkay { get; set; }

        // Calculated values for logging
        public double RvolNormalized { get; set; }
        public double CurrentVd { get; set; }
        public double CurrentPriceMove { get; set; }
        public double CurrentPrice { get; set; }
        public double VdStrength { get; set; }
        public double VdPriceRatio { get; set; }
        public double VdVolumeRatio { get; set; }
    }

    /// <summary>
    /// Utility class for time zone conversions and DST handling
    /// </summary>
    public static class TimeZoneUtilities
    {
        private static readonly TimeZoneInfo EasternTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

        /// <summary>
        /// Convert broker time to EST/EDT with automatic DST adjustment
        /// </summary>
        public static DateTime ConvertBrokerTimeToEST(DateTime brokerTime)
        {
            // Broker time is assumed to be in UTC for backtesting
            var utcTime = brokerTime.Kind == DateTimeKind.Unspecified ?
                DateTime.SpecifyKind(brokerTime, DateTimeKind.Utc) : brokerTime;

            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, EasternTimeZone);
        }

        /// <summary>
        /// Convert EST/EDT to broker time (UTC)
        /// </summary>
        public static DateTime ConvertESTToBrokerTime(DateTime estTime)
        {
            var estTimeSpecified = DateTime.SpecifyKind(estTime, DateTimeKind.Unspecified);
            return TimeZoneInfo.ConvertTimeToUtc(estTimeSpecified, EasternTimeZone);
        }

        /// <summary>
        /// Check if given time is within a trading period (handles cross-midnight periods)
        /// </summary>
        public static bool IsInTradingPeriod(DateTime currentTime, int startHHMM, int endHHMM)
        {
            var estTime = ConvertBrokerTimeToEST(currentTime);
            var currentMinutes = estTime.Hour * 60 + estTime.Minute;

            var startMinutes = (startHHMM / 100) * 60 + (startHHMM % 100);
            var endMinutes = (endHHMM / 100) * 60 + (endHHMM % 100);

            // Handle periods that cross midnight
            if (startMinutes > endMinutes)
            {
                return currentMinutes >= startMinutes || currentMinutes <= endMinutes;
            }

            return currentMinutes >= startMinutes && currentMinutes <= endMinutes;
        }

        /// <summary>
        /// Get the EST time for a specific hour and minute on a given date
        /// </summary>
        public static DateTime GetESTTimeForDate(DateTime date, int hour, int minute)
        {
            var estDate = ConvertBrokerTimeToEST(date);
            var targetTime = new DateTime(estDate.Year, estDate.Month, estDate.Day, hour, minute, 0);
            return ConvertESTToBrokerTime(targetTime);
        }

        /// <summary>
        /// Check if it's a CME trading day (excludes weekends and major holidays)
        /// </summary>
        public static bool IsCMETradingDay(DateTime date)
        {
            var estDate = ConvertBrokerTimeToEST(date);

            // Skip weekends
            if (estDate.DayOfWeek == DayOfWeek.Saturday || estDate.DayOfWeek == DayOfWeek.Sunday)
                return false;

            // TODO: Add CME holiday calendar check
            // For now, just excluding weekends
            return true;
        }
    }


    #endregion

}
