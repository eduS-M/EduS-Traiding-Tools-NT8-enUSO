#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader
{
    public class EduS_Doji_Naked_Targets_V2 : Indicator
    {
        private SMA volSMA;
        private ATR atr;
        private int secondaryBarsIdx = 1;
        private List<DojiLevel> activeLevels = new List<DojiLevel>();

        // Estructura interna mejorada
        private class DojiLevel {
            public string Tag;
            public double Price;
            public Brush Color;
            public bool IsMitigated;
            public DateTime StartTime;
            public int Direction; // 1 para Largo (Continuidad/Giro), -1 para Corto
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "EduS Doji Naked Targets V2";
                Calculate = Calculate.OnBarClose; // Cambiamos a OnBarClose para estabilidad de señales
                IsOverlay = true;
                
                BaseMinutes = 15;
                DojiThresholdPct = 10.0;
                VolumeAvgPeriod = 20;
                HighVolumeFactor = 1.5;
                SwingLookback = 5;      // Filtro de ubicación
                MinATRMult = 0.5;      // Filtro de tamaño mínimo
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, BaseMinutes);
            }
            else if (State == State.DataLoaded)
            {
                volSMA = SMA(Volumes[secondaryBarsIdx], VolumeAvgPeriod);
                atr = ATR(BarsArray[secondaryBarsIdx], 14);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBars[0] < 20 || CurrentBars[secondaryBarsIdx] < 20) return;

            // 1. Lógica de DETECCIÓN CON FILTROS (En serie secundaria)
            if (BarsInProgress == secondaryBarsIdx)
            {
                CheckForQualifiedDoji();
            }

            // 2. Lógica de MITIGACIÓN (En serie principal)
            if (BarsInProgress == 0)
            {
                UpdateMitigation();
            }
        }

        private void CheckForQualifiedDoji()
        {
            // Analizamos la vela [1] porque la [0] aún no cierra en lógica secundaria estable
            double h = Highs[secondaryBarsIdx][1];
            double l = Lows[secondaryBarsIdx][1];
            double o = Opens[secondaryBarsIdx][1];
            double c = Closes[secondaryBarsIdx][1];
            double v = Volumes[secondaryBarsIdx][1];
            
            double body = Math.Abs(c - o);
            double range = h - l;

            // --- FILTRO 1: ¿Es un Doji geométrico? ---
            if (range == 0 || body > (range * (DojiThresholdPct / 100.0))) return;

            // --- FILTRO 2: ¿Tiene tamaño relevante (ATR)? ---
            if (range < atr[1] * MinATRMult) return;

            // --- FILTRO 3: ¿Es un extremo (Swing)? ---
            bool isSwingHigh = h >= MAX(Highs[secondaryBarsIdx], SwingLookback)[1];
            bool isSwingLow = l <= MIN(Lows[secondaryBarsIdx], SwingLookback)[1];
            
            if (!isSwingHigh && !isSwingLow) return;

            // --- FILTRO 4: Confirmación de Volumen Institucional ---
            if (v < volSMA[1] * HighVolumeFactor) return;

            // Si pasa los filtros, lo guardamos como nivel relevante
            double targetPrice = (o + c) / 2;
            string tag = "DojiV2_" + BarsArray[secondaryBarsIdx].GetTime(1).Ticks;

            activeLevels.Add(new DojiLevel {
                Tag = tag,
                Price = targetPrice,
                Color = isSwingHigh ? Brushes.Red : Brushes.LimeGreen, // Rojo para techos, Verde para suelos
                IsMitigated = false,
                StartTime = BarsArray[secondaryBarsIdx].GetTime(1)
            });
        }

        private void UpdateMitigation()
        {
            double high0 = High[0];
            double low0 = Low[0];

            for (int i = activeLevels.Count - 1; i >= 0; i--)
            {
                var level = activeLevels[i];
                if (level.IsMitigated) continue;

                // Mitigación: el precio toca el nivel
                if (low0 <= level.Price && high0 >= level.Price)
                {
                    level.IsMitigated = true;
                    RemoveDrawObject(level.Tag);
                    continue;
                }

                // Dibujar línea con extensión
                Draw.Line(this, level.Tag, false, level.StartTime, level.Price, 
                          Time[0].AddMinutes(BaseMinutes), level.Price, level.Color, DashStyleHelper.Dash, 2);
            }
        }

        #region Properties
        [NinjaScriptProperty] [Display(Name="Minutos Doji", GroupName="1. Estructura")] public int BaseMinutes { get; set; }
        [NinjaScriptProperty] [Display(Name="Lookback Swing", GroupName="1. Estructura")] public int SwingLookback { get; set; }
        [NinjaScriptProperty] [Display(Name="Umbral Doji %", GroupName="2. Filtros")] public double DojiThresholdPct { get; set; }
        [NinjaScriptProperty] [Display(Name="Factor Vol (Min)", GroupName="2. Filtros")] public double HighVolumeFactor { get; set; }
        [NinjaScriptProperty] [Display(Name="ATR Multiplier", GroupName="2. Filtros")] public double MinATRMult { get; set; }
        [NinjaScriptProperty] [Display(Name="Media Vol Periodo", GroupName="2. Filtros")] public int VolumeAvgPeriod { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduS_Doji_Naked_Targets_V2[] cacheEduS_Doji_Naked_Targets_V2;
		public EduS_Trader.EduS_Doji_Naked_Targets_V2 EduS_Doji_Naked_Targets_V2(int baseMinutes, int swingLookback, double dojiThresholdPct, double highVolumeFactor, double minATRMult, int volumeAvgPeriod)
		{
			return EduS_Doji_Naked_Targets_V2(Input, baseMinutes, swingLookback, dojiThresholdPct, highVolumeFactor, minATRMult, volumeAvgPeriod);
		}

		public EduS_Trader.EduS_Doji_Naked_Targets_V2 EduS_Doji_Naked_Targets_V2(ISeries<double> input, int baseMinutes, int swingLookback, double dojiThresholdPct, double highVolumeFactor, double minATRMult, int volumeAvgPeriod)
		{
			if (cacheEduS_Doji_Naked_Targets_V2 != null)
				for (int idx = 0; idx < cacheEduS_Doji_Naked_Targets_V2.Length; idx++)
					if (cacheEduS_Doji_Naked_Targets_V2[idx] != null && cacheEduS_Doji_Naked_Targets_V2[idx].BaseMinutes == baseMinutes && cacheEduS_Doji_Naked_Targets_V2[idx].SwingLookback == swingLookback && cacheEduS_Doji_Naked_Targets_V2[idx].DojiThresholdPct == dojiThresholdPct && cacheEduS_Doji_Naked_Targets_V2[idx].HighVolumeFactor == highVolumeFactor && cacheEduS_Doji_Naked_Targets_V2[idx].MinATRMult == minATRMult && cacheEduS_Doji_Naked_Targets_V2[idx].VolumeAvgPeriod == volumeAvgPeriod && cacheEduS_Doji_Naked_Targets_V2[idx].EqualsInput(input))
						return cacheEduS_Doji_Naked_Targets_V2[idx];
			return CacheIndicator<EduS_Trader.EduS_Doji_Naked_Targets_V2>(new EduS_Trader.EduS_Doji_Naked_Targets_V2(){ BaseMinutes = baseMinutes, SwingLookback = swingLookback, DojiThresholdPct = dojiThresholdPct, HighVolumeFactor = highVolumeFactor, MinATRMult = minATRMult, VolumeAvgPeriod = volumeAvgPeriod }, input, ref cacheEduS_Doji_Naked_Targets_V2);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduS_Doji_Naked_Targets_V2 EduS_Doji_Naked_Targets_V2(int baseMinutes, int swingLookback, double dojiThresholdPct, double highVolumeFactor, double minATRMult, int volumeAvgPeriod)
		{
			return indicator.EduS_Doji_Naked_Targets_V2(Input, baseMinutes, swingLookback, dojiThresholdPct, highVolumeFactor, minATRMult, volumeAvgPeriod);
		}

		public Indicators.EduS_Trader.EduS_Doji_Naked_Targets_V2 EduS_Doji_Naked_Targets_V2(ISeries<double> input , int baseMinutes, int swingLookback, double dojiThresholdPct, double highVolumeFactor, double minATRMult, int volumeAvgPeriod)
		{
			return indicator.EduS_Doji_Naked_Targets_V2(input, baseMinutes, swingLookback, dojiThresholdPct, highVolumeFactor, minATRMult, volumeAvgPeriod);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduS_Doji_Naked_Targets_V2 EduS_Doji_Naked_Targets_V2(int baseMinutes, int swingLookback, double dojiThresholdPct, double highVolumeFactor, double minATRMult, int volumeAvgPeriod)
		{
			return indicator.EduS_Doji_Naked_Targets_V2(Input, baseMinutes, swingLookback, dojiThresholdPct, highVolumeFactor, minATRMult, volumeAvgPeriod);
		}

		public Indicators.EduS_Trader.EduS_Doji_Naked_Targets_V2 EduS_Doji_Naked_Targets_V2(ISeries<double> input , int baseMinutes, int swingLookback, double dojiThresholdPct, double highVolumeFactor, double minATRMult, int volumeAvgPeriod)
		{
			return indicator.EduS_Doji_Naked_Targets_V2(input, baseMinutes, swingLookback, dojiThresholdPct, highVolumeFactor, minATRMult, volumeAvgPeriod);
		}
	}
}

#endregion
