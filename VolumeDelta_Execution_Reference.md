# Volume Delta, Historical Data, and Execution Reference

This document extracts key components related to Volume Delta processing, Historical Data loading (including Volume Analysis), and Order Execution logic from the strategy codebase.

## 1. Volume Delta & Indicators
**File:** `Strategy update/DeltaBasedIndicators/DeltaBasedIndicators.cs`

### Core Logic
The `DeltaBasedIndicators` class calculates volume delta signals. It supports both Tick-based delta (accumulated real-time) and Volume Analysis delta (historical/bar-based).

#### Initialization (`OnInit`)
Configures update type based on mode (Tick vs Bar Close).
```csharp
protected override void OnInit()
{
    // Enable tick updates if tick-based delta mode is active
    if (this._UseTickBasedDelta != 0)
    {
        this.UpdateType = IndicatorUpdateType.OnTick;
        this._tickBasedModeActive = true;
        Core.Instance.Loggers.Log("[DeltaIndicators][Init] TICK MODE ACTIVATED...", LoggingLevel.Trading);
    }
    else
    {
        this.UpdateType = IndicatorUpdateType.OnBarClose;
        this._tickBasedModeActive = false;
    }
    // ... buffer initialization ...
}
```

#### Update Logic (`OnUpdate`)
Handles both historical loading and real-time updates. Uses `GetHistoricalTickDelta(0)` for current bar delta in tick mode, or `VolumeAnalysisData.Total.Delta` in standard mode.

```csharp
protected override void OnUpdate(UpdateArgs args)
{
    // ... Tick mode handling (saving bars, resetting counters) ...

    // Run calculations only on bar close/new bar
    if (args.Reason != UpdateReason.NewBar && args.Reason != UpdateReason.HistoricalBar)
        return;

    // Get delta from appropriate source
    double delta;
    if (this._tickBasedModeActive)
    {
        // Get delta for the current bar that just closed (now at index [0])
        delta = GetHistoricalTickDelta(0);
    }
    else
    {
        // Wait until volume analysis is fully available
        if (!this.volumeReady && _forceVolume == 0)
            return;
        
        var currentBar = this.HistoricalData[0];
        var volumeAnalysis = currentBar?.VolumeAnalysisData;
        var totalVolumeAnalysis = volumeAnalysis?.Total;
        // ...
        delta = totalVolumeAnalysis.Delta;
    }

    // ... Buffer maintenance and Signal Calculation (APAVD, Strength, Divergence, VDtV) ...
    
    // VDtV flag (|VD|/Vol vs averages)
    if (this._VolumeBuffer.IsFull && this._DeltaBuffer_VDtV.IsFull)
    {
        var avgVD_v = this._DeltaBuffer_VDtV.ToArray().Average();
        var avgVol = this._VolumeBuffer.ToArray().Average();
        var vdNow = Math.Abs(delta);
        double volNow = this.HistoricalData[0]?[PriceType.Volume] ?? double.NaN;
        
        // Ratio calculation...
        bool strongVDtV = ...;
        int vdtvFlag = strongVDtV ? (delta > 0 ? 1 : -1) : 0;
        // ...
    }
}
```

#### Tick Processing (`ProcessTick`, `GetTickBasedDelta`)
Accumulates Up/Down ticks to approximate delta when Volume Analysis is unavailable or for higher precision.
```csharp
private void ProcessTick(double currentPrice)
{
    // Compare current tick to previous tick
    if (currentPrice > _lastTickPrice)
        _currentBarUpTicks++;
    else if (currentPrice < _lastTickPrice)
        _currentBarDownTicks++;
    
    _lastTickPrice = currentPrice;
}

private double GetTickBasedDelta()
{
    return (double)(_currentBarUpTicks - _currentBarDownTicks);
}
```

## 2. Historical Data & Volume Analysis Loading
**Files:** `VolumeHistoryProvider.cs`, `HystoryDataProvider.cs`

### Volume History Provider
**File:** `Strategy update/Quantower-Orders-Manager/OperationSystemAdv/DDDCore/VolumeHistoryProvider.cs`
Responsible for loading historical data and triggering Volume Analysis calculations asynchronously.

```csharp
public async Task<HistoricalData> LoadAsync(DateTime from, Period period, CancellationToken token)
{
    _history = _symbol.GetHistory(period, from);
    _history.NewHistoryItem += (s, e) => OnNewData?.Invoke();

    var parameters = new VolumeAnalysisCalculationParameters
    {
        DeltaCalculationType = DeltaCalculationType.AggressorFlag
    };

    // Trigger Volume Analysis calculation
    _progress = Core.Instance.VolumeAnalysis.CalculateProfile(_history, parameters);
    _progress.ProgressChanged += (s, e) =>
    {
        if (e.ProgressPercent == 100)
            _profileReadySignal.Signal(); // Signal completion
    };

    return _history;
}
```

### History Data Provider Factory
**File:** `Strategy update/Quantower-Orders-Manager/OperationSystemAdv/DDDCore/HystoryDataProvider.cs`
Manages the lifecycle of historical data requests, including async/sync loading options.

```csharp
public HystoryDataProvider(..., VolumeAnalysisCalculationParameters volReq = null, bool loadAsAsdync = false, ...)
{
    this.HistoricalData = symbol.GetHistory(request);
    // ...
    if (this.LoadVolume)
    {
        if (loadAsAsdync)
        {
            this.CancToken = new CancellationTokenSource();
            this.ExecuteAsync(volReq, this.CancToken.Token);
            this.WaitForReady(_timeoutMs);
        }
        else
        {
            this.LoadVolumeSync(volReq);
        }
    }
    // ...
}

public Task ExecuteAsync(VolumeAnalysisCalculationParameters req, CancellationToken token)
{
    // Spawns background thread for Volume Analysis
    this.worker = new Thread(() =>
    {
        this.VolumeAnalysisCalProgress = Core.Instance.VolumeAnalysis.CalculateProfile(this.HistoricalData, req);
        this.VolumeAnalysisCalProgress.ProgressChanged += this.VolumeAnalysisCalProgress_ProgressChanged;
    });
    this.worker.IsBackground = true;
    this.worker.Start();
    return Task.CompletedTask;
}
```

## 3. Strategy Execution & Order Management
**Files:** `RowanStrategy.cs`, `ConditionableBase.cs`

### Bar Close Logic (`ProcessHistoryUpdate`)
**File:** `Strategy update/Quantower-Orders-Manager/Strategies/RowanStrategy.cs`
This method runs every time the history updates (Tick or Bar Close). It orchestrates exposure tracking, signal calculation, and trade execution.

```csharp
private void ProcessHistoryUpdate(HistoryEventArgs e, HistoryUpdAteType updateType)
{
    // PHASE 1: CRITICAL - Refresh exposure from broker truth EVERY TICK
    RefreshExposureTracking();
    
    // ... Session updates ...

    // Only process logic on NewItem (Bar Close)
    bool isBarClose = updateType == HistoryUpdAteType.NewItem;
    if (!isBarClose) return; 

    // ... Data preparation (ATR, Previous High/Low) ...

    TradeSignal entry_signal = this.CalculateTradeSignal(true);
    TradeSignal exit_signal = this.CalculateTradeSignal(false);
    TradeAction action = DetermineTradeAction(entry_signal, exit_signal);

    // ... Execution Phases ...
    
    // Revert signal: execute single-order reversal
    if (action == TradeAction.Revert)
    {
        if (ExecuteSingleOrderReversal(marketData, entry_signal, exit_signal))
            return;
        else
        {
            // CRITICAL: Reversal failed - do NOT fall through to normal entry
            return;
        }
    }

    // PHASE 2: ENTRY HANDLING
    if (action == TradeAction.Buy || action == TradeAction.Sell)
    {
        // ... Max Exposure Check ...
        
        // PHASE 2: Risk gate before entry
        if (!PreTradeRiskCheck("ImmediateEntry")) return;

        // Execute Entry
        this.ComputeTradeAction(marketData, entrySide);
    }
}
```

### Order Placement

#### 1. Normal Entry (`ComputeTradeAction` -> `Trade`)
**File:** `ConditionableBase.cs` (inherited by RowanStrategy)
Uses the `PlaceOrderRequestParameters` to send orders via the `TpSlPositionManager`.

```csharp
public virtual void Trade(Side side, double price, T slMarketData, T tpMarketData)
{
    // ... Validation & Session Checks ...

    double contractQuantity = this.CalculateContractQuantity();
    var comment = GenerateComment();

    var ord_Request = new PlaceOrderRequestParameters
    {
        // ... Account, Symbol, Side ...
        Quantity = contractQuantity,
        OrderTypeId = ... // Market or Limit
        Comment = comment
    };

    // Calculate SL/TP
    var sl = Strategy.CalculateSl(slMarketData, side, price);
    var tp = Strategy.CalculateTp(tpMarketData, side, price);

    // Delegate to Manager
    _manager.PlaceEntryOrder(ord_Request, comment, slReqests, tpReqests, this);
}
```

#### 2. Reversal Entry (`ExecuteSingleOrderReversal`)
**File:** `RowanStrategy.cs`
Executes a single market order to flip the position (Net Quantity + 1). Bypasses the standard manager entry flow to avoid overhead but re-integrates for SL/TP.

```csharp
private bool ExecuteSingleOrderReversal(SlTpData marketData, TradeSignal entrySignal, TradeSignal exitSignal)
{
    // ... Net Position Calculation ...
    
    // Always 1 contract for new position + quantity to flatten
    double flattenQty = netQty;
    double newQty = 1.0; 
    double totalReversalQty = flattenQty + newQty;

    // ... Risk Checks (Session, Max Loss) ...

    // Build Market Order
    var reversalRequest = new PlaceOrderRequestParameters
    {
        Side = reversalSide,
        Quantity = totalReversalQty,
        OrderTypeId = marketOrderType.Id,
        Comment = $"{comment}.{OrderTypeSubcomment.Entry}", // Suffix for Manager tracking
        // ...
    };

    // Place Order Directly
    Core.Instance.PlaceOrder(reversalRequest);

    // ... Reversal Tracking Setup (MonitorReversalFills) ...
}
```

