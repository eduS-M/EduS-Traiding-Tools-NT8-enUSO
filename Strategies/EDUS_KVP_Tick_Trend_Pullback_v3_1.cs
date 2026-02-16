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
    public class EDUS_KVP_Tick_Trend_Pullback_v3_1 : Strategy
    {
        // ======= Parámetros =======
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name="Contratos", Order=0, GroupName="1) Tamaño")]
        public int Contratos { get; set; } = 2;

        [NinjaScriptProperty, Range(0.5, 10)]
        [Display(Name="TP1 (R múltiplos)", Order=1, GroupName="2) Targets/Stops")]
        public double R_Target1 { get; set; } = 1.5;

        [NinjaScriptProperty, Range(0.5, 5)]
        [Display(Name="ATR Stop Mult", Order=2, GroupName="2) Targets/Stops")]
        public double ATR_StopMult { get; set; } = 1.2;

        [NinjaScriptProperty, Range(0.5, 5)]
        [Display(Name="ATR Trail Mult (runner)", Order=3, GroupName="2) Targets/Stops")]
        public double ATR_TrailMult { get; set; } = 2.0;

        [NinjaScriptProperty, Range(1, 1000)]
        [Display(Name="Min Stop (ticks)", Order=4, GroupName="2) Targets/Stops")]
        public int MinStopTicks { get; set; } = 6;

        [NinjaScriptProperty, Range(1, 5000)]
        [Display(Name="Max Stop (ticks)", Order=5, GroupName="2) Targets/Stops")]
        public int MaxStopTicks { get; set; } = 24;

        [NinjaScriptProperty, Range(0.5, 2.0)]
        [Display(Name="BE en R (ej: 0.8)", Order=6, GroupName="2) Targets/Stops")]
        public double BreakEvenAtR { get; set; } = 0.8;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name="ADX Umbral (5m)", Order=7, GroupName="3) Filtros")]
        public int ADX_Threshold { get; set; } = 16;

        [NinjaScriptProperty, Range(0.5, 5)]
        [Display(Name="Vol. Spike x SMA(50)", Order=8, GroupName="3) Filtros")]
        public double VolSpikeMult { get; set; } = 1.05;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name="RSI Periodo", Order=9, GroupName="4) Indicadores")]
        public int RSIPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name="RSI Suavizado", Order=10, GroupName="4) Indicadores")]
        public int RSISmooth { get; set; } = 3;

        [NinjaScriptProperty, Range(10, 100)]
        [Display(Name="EMA Tendencia (ticks)", Order=11, GroupName="4) Indicadores")]
        public int EMA_Tick_Period { get; set; } = 34;

        [NinjaScriptProperty, Range(5, 60)]
        [Display(Name="EMA Pullback (ticks)", Order=12, GroupName="4) Indicadores")]
        public int EMA_Fast_Period { get; set; } = 20;

        [NinjaScriptProperty, Range(10, 100)]
        [Display(Name="Keltner EMA Period", Order=13, GroupName="4) Indicadores")]
        public int KeltnerEMAPeriod { get; set; } = 20;

        [NinjaScriptProperty, Range(0.5, 5)]
        [Display(Name="Keltner Offset (ATR mult)", Order=14, GroupName="4) Indicadores")]
        public double KeltnerOffset { get; set; } = 1.2;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name="ATR Period", Order=15, GroupName="4) Indicadores")]
        public int ATRPeriod { get; set; } = 14;

        [NinjaScriptProperty]
        [Display(Name="Usar VWAP de sesión (interno)", Order=16, GroupName="5) Filtros extra")]
        public bool UseVWAP { get; set; } = true;

        [NinjaScriptProperty]
        [Display(Name="Usar Opening Range", Order=17, GroupName="5) Filtros extra")]
        public bool UseOpeningRange { get; set; } = true;

        [NinjaScriptProperty, Range(1, 30)]
        [Display(Name="OR minutos", Order=18, GroupName="5) Filtros extra")]
        public int OR_Minutes { get; set; } = 5;

        [NinjaScriptProperty]
        [Display(Name="Usar ventanas horarias", Order=19, GroupName="6) Horarios")]
        public bool UseTimeWindows { get; set; } = true;

        [NinjaScriptProperty, Range(0, 235959)]
        [Display(Name="Inicio 1 (HHmmss)", Order=20, GroupName="6) Horarios")]
        public int StartTime1 { get; set; } = 94000;

        [NinjaScriptProperty, Range(0, 235959)]
        [Display(Name="Fin 1 (HHmmss)", Order=21, GroupName="6) Horarios")]
        public int EndTime1 { get; set; } = 113000;

        [NinjaScriptProperty, Range(0, 235959)]
        [Display(Name="Inicio 2 (HHmmss)", Order=22, GroupName="6) Horarios")]
        public int StartTime2 { get; set; } = 140000;

        [NinjaScriptProperty, Range(0, 235959)]
        [Display(Name="Fin 2 (HHmmss)", Order=23, GroupName="6) Horarios")]
        public int EndTime2 { get; set; } = 154500;

        [NinjaScriptProperty]
        [Display(Name="Stop por Swing (además de ATR)", Order=24, GroupName="7) Swing/Control")]
        public bool UseSwingStop { get; set; } = true;

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name="Fuerza Swing", Order=25, GroupName="7) Swing/Control")]
        public int SwingStrength { get; set; } = 5;

        [NinjaScriptProperty, Range(0, 200)]
        [Display(Name="Cooldown tras stop (barras)", Order=26, GroupName="7) Swing/Control")]
        public int CooldownBars { get; set; } = 10;

        [NinjaScriptProperty, Range(5, 400)]
        [Display(Name="Time Stop (barras)", Order=27, GroupName="7) Swing/Control")]
        public int TimeStopBars { get; set; } = 30;

        // ======= Indicadores =======
        private EMA emaTrend, emaPull, emaKC;
        private ATR atr;
        private RSI rsi;
        private SMA volSma50;
        private EMA ema5Fast, ema5Slow;
        private ADX adx5;
        private Swing swing;

        // ======= Estado =======
        private int atrTicks, target1Ticks;
        private double entryPriceB, lastStopPriceB = double.NaN;

        private const string SignalA = "KVP_A";
        private const string SignalB = "KVP_B";

        // Opening Range
        private double orHigh, orLow;
        private DateTime sessionOpen, orEnd;
        private bool orReady;

        // VWAP interno de sesión
        private double vwapPVSum, vwapVSum, vwapPrice;

        // Cooldown y control de cierres
        private int cooldownLeft = 0;
        private int lastTradesCount = 0;

        // Captura de precio de entrada del runner sin OnExecutionUpdate
        private double pendingEntryB = 0;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "EDUS_KVP_Tick_Trend_Pullback_v3_1";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 2;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 5;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 5);
            }
            else if (State == State.DataLoaded)
            {
                emaTrend = EMA(EMA_Tick_Period);
                emaPull  = EMA(EMA_Fast_Period);
                emaKC    = EMA(KeltnerEMAPeriod);
                atr      = ATR(ATRPeriod);
                rsi      = RSI(RSIPeriod, RSISmooth);
                volSma50 = SMA(Volumes[0], 50);
                swing    = Swing(SwingStrength);

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
                    ADX_Threshold = Math.Max(ADX_Threshold, 20);
                }
            }
        }

        public override string DisplayName => $"{Name} [{Instrument?.MasterInstrument?.Name}]";

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 50 || CurrentBars[1] < 200)
                return;
            if (BarsInProgress != 0) return;

            // ===== Control de fin de trade para cooldown =====
            if (SystemPerformance.AllTrades.Count > lastTradesCount)
            {
                var t = SystemPerformance.AllTrades[SystemPerformance.AllTrades.Count - 1];
                if (t.ProfitCurrency < 0) cooldownLeft = CooldownBars;
                lastTradesCount = SystemPerformance.AllTrades.Count;
            }
            if (cooldownLeft > 0) cooldownLeft--;

            // ===== Opening Range y VWAP interno =====
            if (Bars.IsFirstBarOfSession)
            {
                sessionOpen = Time[0];
                orEnd = sessionOpen.AddMinutes(OR_Minutes);
                orHigh = Low[0]; orLow = High[0];
                orReady = false;

                vwapPVSum = 0; vwapVSum = 0; vwapPrice = Close[0];
            }

            // OR tracking
            if (!orReady)
            {
                orHigh = Math.Max(orHigh, High[0]);
                orLow  = Math.Min(orLow, Low[0]);
                if (Time[0] >= orEnd) orReady = true;
            }

            // VWAP de sesión (precio típico * volumen)
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
            bool volSpike    = volNowBar > volSma50[0] * VolSpikeMult;
            bool longCandle  = Close[0] > Open[0] && lowerWick > body * 0.6;
            bool shortCandle = Close[0] < Open[0] && upperWick > body * 0.6;

            bool rsiLong  = rsi[0] > 48;
            bool rsiShort = rsi[0] < 52;

            // ===== Pullbacks + extras =====
            bool pullLong  = Low[0] <= kcMid || Low[0] <= kcDn || Low[0] <= emaPull[0] || (UseOpeningRange && orReady && Low[0] <= orHigh + 2*TickSize);
            bool pullShort = High[0] >= kcMid || High[0] >= kcUp || High[0] >= emaPull[0] || (UseOpeningRange && orReady && High[0] >= orLow - 2*TickSize);

            bool vwapLongOk  = !UseVWAP || Close[0] > vwapPrice;
            bool vwapShortOk = !UseVWAP || Close[0] < vwapPrice;

            bool orLongOk  = !UseOpeningRange || (orReady && Close[0] > orHigh);
            bool orShortOk = !UseOpeningRange || (orReady && Close[0] < orLow);

            // ===== Tamaños y stops =====
            int qtyA = Math.Max(1, Contratos / 2);
            int qtyB = Math.Max(Contratos - qtyA, 0);

            int atrTicksRaw = (int)Math.Round((atrNow * ATR_StopMult) / TickSize, MidpointRounding.AwayFromZero);
            int swingTicksLong = int.MaxValue;
            int swingTicksShort = int.MaxValue;
            if (UseSwingStop)
            {
                double swL = swing.SwingLow[0];
                double swH = swing.SwingHigh[0];
                if (!double.IsNaN(swL)) swingTicksLong = (int)Math.Max(1, Math.Round((Close[0] - swL) / TickSize));
                if (!double.IsNaN(swH)) swingTicksShort= (int)Math.Max(1, Math.Round((swH - Close[0]) / TickSize));
            }

            if (Position.MarketPosition == MarketPosition.Flat && cooldownLeft <= 0 && timeOK)
            {
                // LONG
                if (longBias && pullLong && longCandle && volSpike && rsiLong && vwapLongOk && orLongOk)
                {
                    int stopTicks = atrTicksRaw;
                    if (UseSwingStop && swingTicksLong != int.MaxValue) stopTicks = Math.Min(stopTicks, swingTicksLong);
                    stopTicks = Math.Max(MinStopTicks, Math.Min(MaxStopTicks, stopTicks));

                    atrTicks = stopTicks;
                    target1Ticks = (int)Math.Round(atrTicks * R_Target1, MidpointRounding.AwayFromZero);

                    double entryStop = High[0] + TickSize;

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
                        pendingEntryB = entryStop; // capturamos precio al enviar
                    }
                }

                // SHORT
                if (shortBias && pullShort && shortCandle && volSpike && rsiShort && vwapShortOk && orShortOk)
                {
                    int stopTicks = atrTicksRaw;
                    if (UseSwingStop && swingTicksShort != int.MaxValue) stopTicks = Math.Min(stopTicks, swingTicksShort);
                    stopTicks = Math.Max(MinStopTicks, Math.Min(MaxStopTicks, stopTicks));

                    atrTicks = stopTicks;
                    target1Ticks = (int)Math.Round(atrTicks * R_Target1, MidpointRounding.AwayFromZero);

                    double entryStop = Low[0] - TickSize;

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

            // ===== Si el runner B ya existe, fijamos entryPriceB (sin OnExecutionUpdate)
            if (entryPriceB <= 0)
            {
                int barsB = BarsSinceEntryExecution(0, SignalB, 0);
                if (barsB >= 0) { entryPriceB = pendingEntryB; lastStopPriceB = double.NaN; }
            }

            // ===== Gestión en posición =====
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

            // Reset entryPriceB cuando salimos
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                entryPriceB = 0;
                pendingEntryB = 0;
                lastStopPriceB = double.NaN;
            }
        }
    }
}
