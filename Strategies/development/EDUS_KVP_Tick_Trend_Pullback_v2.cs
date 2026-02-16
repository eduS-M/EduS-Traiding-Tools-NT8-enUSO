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
    public class EDUS_KVP_Tick_Trend_Pullback_v2 : Strategy
    {
        // ----- Parámetros -----
        [NinjaScriptProperty, Range(1, int.MaxValue)]
        [Display(Name="Contratos", Order=0, GroupName="1) Tamaño")]
        public int Contratos { get; set; } = 2;

        [NinjaScriptProperty, Range(0.5, 10)]
        [Display(Name="TP1 (R múltiplos)", Order=1, GroupName="2) Targets/Stops")]
        public double R_Target1 { get; set; } = 2.0;

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

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name="ADX Umbral (5m)", Order=6, GroupName="3) Filtros")]
        public int ADX_Threshold { get; set; } = 18;

        [NinjaScriptProperty, Range(0.5, 5)]
        [Display(Name="Vol. Spike x SMA(50)", Order=7, GroupName="3) Filtros")]
        public double VolSpikeMult { get; set; } = 1.2;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name="RSI Periodo", Order=8, GroupName="4) Indicadores")]
        public int RSIPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(1, 20)]
        [Display(Name="RSI Suavizado", Order=9, GroupName="4) Indicadores")]
        public int RSISmooth { get; set; } = 3;

        [NinjaScriptProperty, Range(10, 100)]
        [Display(Name="Keltner EMA Period", Order=10, GroupName="4) Indicadores")]
        public int KeltnerEMAPeriod { get; set; } = 20;

        [NinjaScriptProperty, Range(0.5, 5)]
        [Display(Name="Keltner Offset (ATR mult)", Order=11, GroupName="4) Indicadores")]
        public double KeltnerOffset { get; set; } = 1.5;

        [NinjaScriptProperty, Range(5, 50)]
        [Display(Name="ATR Period", Order=12, GroupName="4) Indicadores")]
        public int ATRPeriod { get; set; } = 14;

        [NinjaScriptProperty, Range(10, 400)]
        [Display(Name="EMA Tick (tendencia)", Order=13, GroupName="4) Indicadores")]
        public int EMA_Tick_Period { get; set; } = 50;

        [NinjaScriptProperty, Range(20, 400)]
        [Display(Name="EMA 5m (lenta)", Order=14, GroupName="4) Indicadores")]
        public int EMA_5m_Slow { get; set; } = 200;

        [NinjaScriptProperty, Range(10, 200)]
        [Display(Name="EMA 5m (rápida)", Order=15, GroupName="4) Indicadores")]
        public int EMA_5m_Fast { get; set; } = 50;

        [NinjaScriptProperty, Range(5, 400)]
        [Display(Name="Time Stop (barras)", Order=16, GroupName="5) Gestión")]
        public int TimeStopBars { get; set; } = 30;

        // ----- Indicadores -----
        private EMA emaTick;
        private ATR atr;
        private EMA emaKC; // base del canal Keltner
        private RSI rsi;
        private SMA volSma50;

        // Serie 5m
        private EMA ema5Fast, ema5Slow;
        private ADX adx5;

        // Estado
        private int atrTicks;
        private int target1Ticks;
        private double entryPriceB;
        private double lastStopPriceB = double.NaN;

        private const string SignalA = "KVP_A"; // parcial con TP
        private const string SignalB = "KVP_B"; // runner con trailing

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                    = "EDUS_KVP_Tick_Trend_Pullback_v2";
                Calculate               = Calculate.OnBarClose;
                EntriesPerDirection     = 2;
                EntryHandling           = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds   = 5;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 5); // filtro de tendencia
            }
            else if (State == State.DataLoaded)
            {
                // Serie principal (ticks)
                emaTick = EMA(EMA_Tick_Period);
                atr     = ATR(ATRPeriod);
                emaKC   = EMA(KeltnerEMAPeriod);
                rsi     = RSI(RSIPeriod, RSISmooth);
                volSma50= SMA(Volumes[0], 50);

                // Serie 5m
                ema5Fast = EMA(BarsArray[1], EMA_5m_Fast);
                ema5Slow = EMA(BarsArray[1], EMA_5m_Slow);
                adx5     = ADX(BarsArray[1], 14);

                // Presets rápidos por instrumento
                string inst = Instrument.MasterInstrument.Name.ToUpperInvariant();
                if (inst.Contains("NQ"))
                {
                    ATR_StopMult   = Math.Max(ATR_StopMult, 1.6);
                    ATR_TrailMult  = Math.Max(ATR_TrailMult, 2.4);
                    MinStopTicks   = Math.Max(MinStopTicks, 12);
                    MaxStopTicks   = Math.Max(MaxStopTicks, 40);
                    ADX_Threshold  = Math.Max(ADX_Threshold, 20);
                }
            }
        }

        public override string DisplayName => $"{Name} [{Instrument?.MasterInstrument?.Name}]";

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 50 || CurrentBars[1] < EMA_5m_Slow)
                return;
            if (BarsInProgress != 0) 
                return;

            // ---- Filtro de tendencia 5m + ticks
            bool trendUp5m   = ema5Fast[0] > ema5Slow[0];
            bool trendDown5m = ema5Fast[0] < ema5Slow[0];
            bool adxOK       = adx5[0] > ADX_Threshold;

            bool longBias  = adxOK && trendUp5m  && Close[0] > emaTick[0];
            bool shortBias = adxOK && trendDown5m && Close[0] < emaTick[0];

            // ---- Keltner manual (base EMA ± ATR*offset)
            double atrNow   = atr[0];
            double kcMid    = emaKC[0];
            double kcUpper  = kcMid + KeltnerOffset * atrNow;
            double kcLower  = kcMid - KeltnerOffset * atrNow;

            // ---- Vela señal y volumen
            double body      = Math.Abs(Close[0] - Open[0]);
            double lowerWick = Math.Min(Open[0], Close[0]) - Low[0];
            double upperWick = High[0] - Math.Max(Open[0], Close[0]);

            double volNow    = Convert.ToDouble(Volumes[0][0]);
            bool volSpike    = volNow > volSma50[0] * VolSpikeMult;

            bool longSignalCandle  = Close[0] > Open[0] && lowerWick > body * 0.6;
            bool shortSignalCandle = Close[0] < Open[0] && upperWick > body * 0.6;

            bool pullbackLong  = Low[0] <= kcMid || Low[0] <= kcLower;
            bool pullbackShort = High[0] >= kcMid || High[0] >= kcUpper;

            bool rsiLong  = rsi[0] > 50;
            bool rsiShort = rsi[0] < 50;

            // ---- Tamaños y stops dinámicos
            int qtyA = Math.Max(1, Contratos / 2);
            int qtyB = Math.Max(Contratos - qtyA, 0);

            atrTicks = (int)Math.Round((atrNow * ATR_StopMult) / TickSize, MidpointRounding.AwayFromZero);
            atrTicks = Math.Max(MinStopTicks, Math.Min(MaxStopTicks, atrTicks));
            target1Ticks = (int)Math.Round(atrTicks * R_Target1, MidpointRounding.AwayFromZero);

            // ---- Entradas
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                lastStopPriceB = double.NaN; // reset

                // Largos
                if (longBias && pullbackLong && longSignalCandle && volSpike && rsiLong)
                {
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
                    }
                }

                // Cortos
                if (shortBias && pullbackShort && shortSignalCandle && volSpike && rsiShort)
                {
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
                    }
                }
            }

            // ---- Gestión: trailing BE + time stop
            if (Position.MarketPosition == MarketPosition.Long)
            {
                int barsA = BarsSinceEntryExecution(0, SignalA, 0);
                int barsB = BarsSinceEntryExecution(0, SignalB, 0);

                // Runner trailing (señal B)
                if (Position.Quantity > 0 && qtyB > 0 && barsB >= 0)
                {
                    double trail = Close[0] - ATR_TrailMult * atrNow;

                    // Break-even cuando alcanza 1R
                    if (entryPriceB > 0 && Close[0] - entryPriceB >= atrTicks * TickSize)
                        trail = Math.Max(trail, entryPriceB + TickSize);

                    if (double.IsNaN(lastStopPriceB) || trail > lastStopPriceB)
                    {
                        SetStopLoss(SignalB, CalculationMode.Price, trail, false);
                        lastStopPriceB = trail;
                    }
                }

                // Time stop
                if (TimeStopBars > 0)
                {
                    if (barsA >= TimeStopBars && barsA != int.MaxValue)
                        ExitLong(SignalA);
                    if (barsB >= TimeStopBars && barsB != int.MaxValue)
                        ExitLong(SignalB);
                }
            }
            else if (Position.MarketPosition == MarketPosition.Short)
            {
                int barsA = BarsSinceEntryExecution(0, SignalA, 0);
                int barsB = BarsSinceEntryExecution(0, SignalB, 0);

                if (Position.Quantity > 0 && qtyB > 0 && barsB >= 0)
                {
                    double trail = Close[0] + ATR_TrailMult * atrNow;

                    if (entryPriceB > 0 && entryPriceB - Close[0] >= atrTicks * TickSize)
                        trail = Math.Min(trail, entryPriceB - TickSize);

                    if (double.IsNaN(lastStopPriceB) || trail < lastStopPriceB)
                    {
                        SetStopLoss(SignalB, CalculationMode.Price, trail, false);
                        lastStopPriceB = trail;
                    }
                }

                if (TimeStopBars > 0)
                {
                    if (barsA >= TimeStopBars && barsA != int.MaxValue)
                        ExitShort(SignalA);
                    if (barsB >= TimeStopBars && barsB != int.MaxValue)
                        ExitShort(SignalB);
                }
            }
        }

        protected override void OnExecutionUpdate(Cbi.Execution execution, string executionId, double price, int quantity, 
    MarketPosition marketPosition, string orderId, DateTime time)
        {
            if (execution?.Order == null)
                return;

            if (execution.Order.Name == SignalB &&
                (execution.Order.OrderAction == OrderAction.Buy || execution.Order.OrderAction == OrderAction.SellShort))
            {
                entryPriceB = execution.Order.AverageFillPrice;
                lastStopPriceB = double.NaN; // reset para comenzar trailing limpio
            }
        }
    }
}
