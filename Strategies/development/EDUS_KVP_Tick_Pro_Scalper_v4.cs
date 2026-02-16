#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.Data;
using NinjaTrader.Cbi;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class EDUS_KVP_Tick_Pro_Scalper_v4 : Strategy
    {
        // ======= Parámetros =======
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name="Contratos", Order=0, GroupName="1) Tamaño")]
        public int Contratos { get; set; } = 2;

        [NinjaScriptProperty, Range(0.5, 10)]
        [Display(Name="TP1 (R múltiplos)", Order=1, GroupName="2) Targets")]
        public double R_Target1 { get; set; } = 1.3;     // más agresivo para scalping

        [NinjaScriptProperty, Range(0.5, 5)]
        [Display(Name="ATR Stop Mult", Order=2, GroupName="3) Stops")]
        public double ATR_StopMult { get; set; } = 1.15;

        [NinjaScriptProperty, Range(1, 1000)]
        [Display(Name="Min Stop (ticks)", Order=3, GroupName="3) Stops")]
        public int MinStopTicks { get; set; } = 6;

        [NinjaScriptProperty, Range(1, 5000)]
        [Display(Name="Max Stop (ticks)", Order=4, GroupName="3) Stops")]
        public int MaxStopTicks { get; set; } = 24;

        [NinjaScriptProperty, Range(0.1, 2.0)]
        [Display(Name="BE en R (ej: 0.8)", Order=5, GroupName="3) Stops")]
        public double BreakEvenAtR { get; set; } = 0.8;

        [NinjaScriptProperty, Range(0.5, 5)]
        [Display(Name="ATR Trail Mult (runner)", Order=6, GroupName="3) Stops")]
        public double ATR_TrailMult { get; set; } = 2.0;

        [NinjaScriptProperty]
        [Display(Name="Usar TP fijo en ticks (A)", Order=7, GroupName="2) Targets")]
        public bool UseFixedTPA { get; set; } = true;

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name="TP fijo A (ticks)", Order=8, GroupName="2) Targets")]
        public int FixedTPA_Ticks { get; set; } = 14;     // ES 610T: 12–16; NQ 377T: 24–32

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name="ATR Period", Order=9, GroupName="4) Indicadores")]
        public int ATRPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(5, 100)]
        [Display(Name="EMA Tendencia (ticks)", Order=10, GroupName="4) Indicadores")]
        public int EMA_Tick_Period { get; set; } = 34;

        [NinjaScriptProperty, Range(5, 60)]
        [Display(Name="EMA Pullback", Order=11, GroupName="4) Indicadores")]
        public int EMA_Pull_Period { get; set; } = 20;

        [NinjaScriptProperty, Range(10, 100)]
        [Display(Name="Keltner EMA Period", Order=12, GroupName="4) Indicadores")]
        public int KeltnerEMAPeriod { get; set; } = 20;

        [NinjaScriptProperty, Range(0.5, 5)]
        [Display(Name="Keltner Offset (ATR mult)", Order=13, GroupName="4) Indicadores")]
        public double KeltnerOffset { get; set; } = 1.2;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name="RSI Periodo", Order=14, GroupName="4) Indicadores")]
        public int RSIPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name="RSI Suavizado", Order=15, GroupName="4) Indicadores")]
        public int RSISmooth { get; set; } = 3;

        [NinjaScriptProperty, Range(0.0, 1.0)]
        [Display(Name="Wick ratio (mecha/cuerpo)", Order=16, GroupName="5) Filtros vela")]
        public double WickRatio { get; set; } = 0.4;     // menos estricto (antes 0.6)

        [NinjaScriptProperty, Range(0.5, 2.0)]
        [Display(Name="Vol x SMA50", Order=17, GroupName="5) Filtros volumen")]
        public double VolMult { get; set; } = 1.0;       // 1.0–1.05

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name="RSI Long mínimo", Order=18, GroupName="5) Filtros RSI")]
        public int RSILongMin { get; set; } = 47;

        [NinjaScriptProperty, Range(0, 100)]
        [Display(Name="RSI Short máximo", Order=19, GroupName="5) Filtros RSI")]
        public int RSIShortMax { get; set; } = 53;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name="ADX Umbral (5m)", Order=20, GroupName="5) Filtros 5m")]
        public int ADX_Threshold { get; set; } = 14;

        [NinjaScriptProperty]
        [Display(Name="Usar Opening Range (5m)", Order=21, GroupName="6) OR/VWAP")]
        public bool UseOpeningRange { get; set; } = true;

        [NinjaScriptProperty, Range(1, 30)]
        [Display(Name="OR minutos", Order=22, GroupName="6) OR/VWAP")]
        public int OR_Minutes { get; set; } = 5;

        [NinjaScriptProperty]
        [Display(Name="Usar VWAP de sesión (interno)", Order=23, GroupName="6) OR/VWAP")]
        public bool UseVWAP { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Activar Breakouts", Order=24, GroupName="7) Breakout")]
        public bool UseBreakouts { get; set; } = true;

        [NinjaScriptProperty, Range(5, 200)]
        [Display(Name="Breakout lookback (barras)", Order=25, GroupName="7) Breakout")]
        public int BreakoutLookback { get; set; } = 20;

        [NinjaScriptProperty, Range(0, 20)]
        [Display(Name="Breakout buffer (ticks)", Order=26, GroupName="7) Breakout")]
        public int BreakoutBufferTicks { get; set; } = 1;

        [NinjaScriptProperty]
        [Display(Name="Usar ventanas horarias", Order=27, GroupName="8) Horarios")]
        public bool UseTimeWindows { get; set; } = true;

        [NinjaScriptProperty, Range(0, 235959)]
        [Display(Name="Inicio 1 (HHmmss)", Order=28, GroupName="8) Horarios")]
        public int StartTime1 { get; set; } = 94000;

        [NinjaScriptProperty, Range(0, 235959)]
        [Display(Name="Fin 1 (HHmmss)", Order=29, GroupName="8) Horarios")]
        public int EndTime1 { get; set; } = 113000;

        [NinjaScriptProperty, Range(0, 235959)]
        [Display(Name="Inicio 2 (HHmmss)", Order=30, GroupName="8) Horarios")]
        public int StartTime2 { get; set; } = 140000;

        [NinjaScriptProperty, Range(0, 235959)]
        [Display(Name="Fin 2 (HHmmss)", Order=31, GroupName="8) Horarios")]
        public int EndTime2 { get; set; } = 154500;

        [NinjaScriptProperty, Range(0, 200)]
        [Display(Name="Cooldown tras stop (barras)", Order=32, GroupName="9) Control")]
        public int CooldownBars { get; set; } = 8;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name="Máx. trades por día", Order=33, GroupName="9) Control")]
        public int MaxTradesPerDay { get; set; } = 10;

        [NinjaScriptProperty, Range(0, 100000)]
        [Display(Name="Meta diaria USD", Order=34, GroupName="9) Control")]
        public double DailyProfitTargetUSD { get; set; } = 600;

        [NinjaScriptProperty, Range(0, 100000)]
        [Display(Name="Pérdida diaria USD", Order=35, GroupName="9) Control")]
        public double DailyLossLimitUSD { get; set; } = 450;

        [NinjaScriptProperty, Range(5, 400)]
        [Display(Name="Time Stop (barras)", Order=36, GroupName="9) Control")]
        public int TimeStopBars { get; set; } = 30;

        // ======= Indicadores =======
        private EMA emaTrend, emaPull, emaKC;
        private ATR atr;
        private RSI rsi;
        private SMA volSma50;
        private EMA ema5Fast, ema5Slow;
        private ADX adx5;
        private Swing swing;
        private MAX maxHigh;
        private MIN minLow;

        // ======= Estado =======
        private int atrTicks, target1Ticks;
        private double entryPriceB, lastStopPriceB = double.NaN;

        private const string SignalA = "KVP_A";
        private const string SignalB = "KVP_B";

        // OR/VWAP
        private double orHigh, orLow;
        private DateTime sessionOpen, orEnd;
        private bool orReady;

        private double vwapPVSum, vwapVSum, vwapPrice;

        // Control diario
        private int cooldownLeft = 0;
        private int lastTradesCount = 0;
        private int tradesStartOfSession = 0;
        private double pnlStartOfSession = 0;

        // Captura runner sin overrides
        private double pendingEntryB = 0;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "EDUS_KVP_Tick_Pro_Scalper_v4";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 2;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 5;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 5); // filtro de tendencia
            }
            else if (State == State.DataLoaded)
            {
                emaTrend = EMA(EMA_Tick_Period);
                emaPull  = EMA(EMA_Pull_Period);
                emaKC    = EMA(KeltnerEMAPeriod);
                atr      = ATR(ATRPeriod);
                rsi      = RSI(RSIPeriod, RSISmooth);
                volSma50 = SMA(Volumes[0], 50);
                swing    = Swing(5);
                maxHigh  = MAX(High, BreakoutLookback);
                minLow   = MIN(Low,  BreakoutLookback);

                ema5Fast = EMA(BarsArray[1], 50);
                ema5Slow = EMA(BarsArray[1], 200);
                adx5     = ADX(BarsArray[1], 14);

                string inst = Instrument.MasterInstrument.Name.ToUpperInvariant();
                if (inst.Contains("NQ"))
                {
                    ATR_StopMult  = Math.Max(ATR_StopMult, 1.6);
                    ATR_TrailMult = Math.Max(ATR_TrailMult, 2.4);
                    MinStopTicks  = Math.Max(MinStopTicks, 12);
                    MaxStopTicks  = Math.Max(MaxStopTicks, 40);
                    ADX_Threshold = Math.Max(ADX_Threshold, 16);
                    FixedTPA_Ticks= Math.Max(FixedTPA_Ticks, 24);
                }
            }
        }

        public override string DisplayName => $"{Name} [{Instrument?.MasterInstrument?.Name}]";

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 50 || CurrentBars[1] < 200)
                return;
            if (BarsInProgress != 0) return;

            // ===== Reset de sesión y métricas diarias =====
            if (Bars.IsFirstBarOfSession)
            {
                sessionOpen = Time[0];
                orEnd = sessionOpen.AddMinutes(OR_Minutes);
                orHigh = Low[0]; orLow = High[0];
                orReady = false;

                vwapPVSum = 0; vwapVSum = 0; vwapPrice = Close[0];

                tradesStartOfSession = SystemPerformance.AllTrades.Count;
                pnlStartOfSession    = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
            }

            // PnL/trades del día
            double pnlToday = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit - pnlStartOfSession;
            int tradesToday = SystemPerformance.AllTrades.Count - tradesStartOfSession;

            bool hitDailyStop  = pnlToday <= -Math.Abs(DailyLossLimitUSD);
            bool hitDailyGoal  = pnlToday >= Math.Abs(DailyProfitTargetUSD);
            bool hitMaxTrades  = tradesToday >= MaxTradesPerDay;

          // Cooldown por última pérdida (compatible con todas las builds)
			if (SystemPerformance.AllTrades.Count > lastTradesCount)
			{
    			var t = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
    			if (t.ProfitCurrency < 0)
        			cooldownLeft = CooldownBars;
    		lastTradesCount = SystemPerformance.AllTrades.Count;
}
if (cooldownLeft > 0) cooldownLeft--;


            // ----- Bloqueo de nuevas entradas si se alcanzan límites -----
            bool allowNewEntries = !(hitDailyStop || hitDailyGoal || hitMaxTrades) && cooldownLeft <= 0;

            // ===== Opening Range y VWAP =====
            if (!orReady)
            {
                orHigh = Math.Max(orHigh, High[0]);
                orLow  = Math.Min(orLow, Low[0]);
                if (Time[0] >= orEnd) orReady = true;
            }

            if (UseVWAP)
            {
                double volNow = Convert.ToDouble(Volumes[0][0]);
                double tp = (High[0] + Low[0] + Close[0]) / 3.0;
                vwapPVSum += tp * volNow;
                vwapVSum  += volNow;
                if (vwapVSum > 0) vwapPrice = vwapPVSum / vwapVSum;
            }

            // ===== Filtro horario =====
            bool timeOK = true;
            if (UseTimeWindows)
            {
                int now = ToTime(Time[0]);
                bool win1 = now >= StartTime1 && now <= EndTime1;
                bool win2 = now >= StartTime2 && now <= EndTime2;
                timeOK = win1 || win2;
            }

            // ===== Filtros de tendencia =====
            bool trendUp5m   = ema5Fast[0] > ema5Slow[0];
            bool trendDown5m = ema5Fast[0] < ema5Slow[0];
            bool adxOK       = adx5[0] > ADX_Threshold;

            bool longBias  = adxOK && trendUp5m  && Close[0] > emaTrend[0];
            bool shortBias = adxOK && trendDown5m && Close[0] < emaTrend[0];

            // ===== Keltner manual =====
            double atrNow  = atr[0];
            double kcMid   = emaKC[0];
            double kcUp    = kcMid + KeltnerOffset * atrNow;
            double kcDn    = kcMid - KeltnerOffset * atrNow;

            // ===== Señal de vela + volumen =====
            double body      = Math.Abs(Close[0] - Open[0]);
            double lowerWick = Math.Min(Open[0], Close[0]) - Low[0];
            double upperWick = High[0] - Math.Max(Open[0], Close[0]);

            double volNowBar = Convert.ToDouble(Volumes[0][0]);
            bool volOk       = volNowBar >= volSma50[0] * VolMult;

            bool longCandle  = Close[0] > Open[0] && lowerWick >= body * WickRatio;
            bool shortCandle = Close[0] < Open[0] && upperWick >= body * WickRatio;

            bool rsiLong  = rsi[0] >= RSILongMin;
            bool rsiShort = rsi[0] <= RSIShortMax;

            // ===== Pullbacks + Breakouts =====
            bool pullLong  = Low[0] <= kcMid || Low[0] <= kcDn || Low[0] <= emaPull[0] ||
                             (UseOpeningRange && orReady && Low[0] <= orHigh + 2*TickSize);
            bool pullShort = High[0] >= kcMid || High[0] >= kcUp || High[0] >= emaPull[0] ||
                             (UseOpeningRange && orReady && High[0] >= orLow - 2*TickSize);

            double prevHH = MAX(High, BreakoutLookback)[1];
            double prevLL = MIN(Low,  BreakoutLookback)[1];

            bool breakoutLong  = UseBreakouts && High[0] >= prevHH + BreakoutBufferTicks * TickSize;
            bool breakoutShort = UseBreakouts && Low[0]  <= prevLL - BreakoutBufferTicks * TickSize;

            // ===== Tamaños y stops =====
            int qtyA = Math.Max(1, Contratos / 2);
            int qtyB = Math.Max(Contratos - qtyA, 0);

            int atrTicksRaw = (int)Math.Round((atrNow * ATR_StopMult) / TickSize, MidpointRounding.AwayFromZero);
            int swingTicksLong = int.MaxValue;
            int swingTicksShort = int.MaxValue;
            double swL = swing.SwingLow[0];
            double swH = swing.SwingHigh[0];
            if (!double.IsNaN(swL)) swingTicksLong  = (int)Math.Max(1, Math.Round((Close[0] - swL) / TickSize));
            if (!double.IsNaN(swH)) swingTicksShort = (int)Math.Max(1, Math.Round((swH - Close[0]) / TickSize));

            // ===== Entradas =====
            if (Position.MarketPosition == MarketPosition.Flat && allowNewEntries && timeOK)
            {
                // LONG
                if (longBias && volOk && rsiLong && ( (pullLong && longCandle) || breakoutLong ) && (!UseVWAP || Close[0] > vwapPrice))
                {
                    int stopTicks = atrTicksRaw;
                    if (swingTicksLong != int.MaxValue) stopTicks = Math.Min(stopTicks, swingTicksLong);
                    stopTicks = Math.Max(MinStopTicks, Math.Min(MaxStopTicks, stopTicks));

                    atrTicks = stopTicks;
                    target1Ticks = UseFixedTPA ? FixedTPA_Ticks
                                               : (int)Math.Round(atrTicks * R_Target1, MidpointRounding.AwayFromZero);

                    double entryStop = Math.Max(High[0] + TickSize, prevHH + BreakoutBufferTicks*TickSize);
                    if (qtyA > 0)
                    {
                        SetStopLoss(SignalA, CalculationMode.Ticks, atrTicks, false);
                        SetProfitTarget(SignalA, CalculationMode.Ticks, target1Ticks);
                        EnterLongStopMarket(qtyA, entryStop, SignalA);
                    }
                    if (qtyB > 0)
                    {
                        SetStopLoss(SignalB, CalculationMode.Ticks, atrTicks, false);
                        EnterLongStopMarket(qtyB, entryStop, SignalB);
                        pendingEntryB = entryStop;
                    }
                }

                // SHORT
                if (shortBias && volOk && rsiShort && ( (pullShort && shortCandle) || breakoutShort ) && (!UseVWAP || Close[0] < vwapPrice))
                {
                    int stopTicks = atrTicksRaw;
                    if (swingTicksShort != int.MaxValue) stopTicks = Math.Min(stopTicks, swingTicksShort);
                    stopTicks = Math.Max(MinStopTicks, Math.Min(MaxStopTicks, stopTicks));

                    atrTicks = stopTicks;
                    target1Ticks = UseFixedTPA ? FixedTPA_Ticks
                                               : (int)Math.Round(atrTicks * R_Target1, MidpointRounding.AwayFromZero);

                    double entryStop = Math.Min(Low[0] - TickSize, prevLL - BreakoutBufferTicks*TickSize);
                    if (qtyA > 0)
                    {
                        SetStopLoss(SignalA, CalculationMode.Ticks, atrTicks, false);
                        SetProfitTarget(SignalA, CalculationMode.Ticks, target1Ticks);
                        EnterShortStopMarket(qtyA, entryStop, SignalA);
                    }
                    if (qtyB > 0)
                    {
                        SetStopLoss(SignalB, CalculationMode.Ticks, atrTicks, false);
                        EnterShortStopMarket(qtyB, entryStop, SignalB);
                        pendingEntryB = entryStop;
                    }
                }
            }

            // ===== Si el runner B ya existe, fijamos entryPriceB
            if (entryPriceB <= 0)
            {
                int barsB = BarsSinceEntryExecution(0, SignalB, 0);
                if (barsB >= 0) { entryPriceB = pendingEntryB; lastStopPriceB = double.NaN; }
            }

            // ===== Gestión =====
            if (Position.MarketPosition == MarketPosition.Long)
            {
                int barsA = BarsSinceEntryExecution(0, SignalA, 0);
                int barsB = BarsSinceEntryExecution(0, SignalB, 0);
                if (barsB >= 0 && entryPriceB > 0)
                {
                    double trail = Close[0] - ATR_TrailMult * atrNow;
                    double beDist = BreakEvenAtR * atrTicks * TickSize;
                    if (Close[0] - entryPriceB >= beDist)
                        trail = Math.Max(trail, entryPriceB + TickSize);

                    if (double.IsNaN(lastStopPriceB) || trail > lastStopPriceB)
                    {
                        SetStopLoss(SignalB, CalculationMode.Price, trail, false);
                        lastStopPriceB = trail;
                    }
                }
                if (TimeStopBars > 0)
                {
                    if (barsA >= TimeStopBars && barsA != int.MaxValue) ExitLong(SignalA);
                    if (barsB >= TimeStopBars && barsB != int.MaxValue) ExitLong(SignalB);
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                int barsA = BarsSinceEntryExecution(0, SignalA, 0);
                int barsB = BarsSinceEntryExecution(0, SignalB, 0);
                if (barsB >= 0 && entryPriceB > 0)
                {
                    double trail = Close[0] + ATR_TrailMult * atrNow;
                    double beDist = BreakEvenAtR * atrTicks * TickSize;
                    if (entryPriceB - Close[0] >= beDist)
                        trail = Math.Min(trail, entryPriceB - TickSize);

                    if (double.IsNaN(lastStopPriceB) || trail < lastStopPriceB)
                    {
                        SetStopLoss(SignalB, CalculationMode.Price, trail, false);
                        lastStopPriceB = trail;
                    }
                }
                if (TimeStopBars > 0)
                {
                    if (barsA >= TimeStopBars && barsA != int.MaxValue) ExitShort(SignalA);
                    if (barsB >= TimeStopBars && barsB != int.MaxValue) ExitShort(SignalB);
                }
            }

            if (Position.MarketPosition == MarketPosition.Flat)
            {
                entryPriceB = 0;
                pendingEntryB = 0;
                lastStopPriceB = double.NaN;
            }
        }
    }
}
