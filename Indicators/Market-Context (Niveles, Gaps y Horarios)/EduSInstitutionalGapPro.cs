#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

public enum EduSTextPos { Centered, AboveLine }

namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader
{
//    public enum EduSTextPos { Centered, AboveLine }

    public class EduSMultiGapFinal : Indicator
    {
        private class GapData
        {
            public double CloseLevel;
            public double OpenLevel;
            public string Tag;
            public int StartBar;
            public DateTime StartTime;
            public bool IsGapAndGo;
            public bool IsActive = true;
            public Brush VisualBrush;
        }

        private List<GapData> activeGaps = new List<GapData>();
        private double currentHigh = double.MinValue;
        private double currentLow = double.MaxValue;
        private double priorHigh = 0;
        private double priorLow = 0;
        private bool firstSessionProcessed = false;

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Mínimo Gap (Ticks)", Order=1, GroupName="1. Filtros")]
        public int MinGapTicks { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 100)]
        [Display(Name="VIX Actual (Manual)", Order=2, GroupName="1. Filtros")]
        public double VixValue { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name="Opacidad (%)", Order=1, GroupName="2. Visualización")]
        public int AreaOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Margen Derecho (Pixeles)", Order=2, GroupName="2. Visualización")]
        public int RightMargin { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Posición del Texto", Order=3, GroupName="2. Visualización")]
        public EduSTextPos TextPosition { get; set; }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Herramienta para la detección, clasificación y visualización de gaps en la apertura de sesiones." + 
								"\n• Rojo (Crimson) → Gap bajista estándar (apertura por debajo del cierre anterior)." +				
								"\n		Interpretación: Gap bajista estándar (bearish gap)." + 
								"\n• Verde (SeaGreen) → Gap alcista estándar (apertura por encima del cierre anterior)" + 
								"\n		Interpretación: Gap alcista estándar (bullish gap)." + 				
								"\n• Dorado (Goldenrod) → Gap institucional (Gap and Go)." + 
								"\n		Este escenario indica que el gap está en zona de valor favorable para continuidad (institucional).";
				Name = "EduS MultiGap Final";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                MinGapTicks = 12;
                VixValue = 18.5;
                AreaOpacity = 40;
                RightMargin = 60;
                TextPosition = EduSTextPos.AboveLine;
            }
            else if (State == State.DataLoaded)
            {
                activeGaps.Clear();
                firstSessionProcessed = false;
                currentHigh = double.MinValue;
                currentLow = double.MaxValue;
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;

            // 1. Detección de Sesión y Captura de Datos previos
            if (Bars.IsFirstBarOfSession)
            {
                if (currentHigh != double.MinValue)
                {
                    priorHigh = currentHigh;
                    priorLow = currentLow;
                    firstSessionProcessed = true;

                    double priorClose = Bars.GetClose(CurrentBar - 1);
                    double currentOpen = Open[0];

                    if (Math.Abs(currentOpen - priorClose) >= (MinGapTicks * TickSize))
                    {
                        // Lógica Value Area (Gap and Go)
                        double range = priorHigh - priorLow;
                        double vah = priorHigh - (range * 0.25);
                        double val = priorLow + (range * 0.25);
                        bool isGo = (currentOpen > vah || currentOpen < val);
                        
                        // FIX: Clonar y descongelar el pincel inmediatamente
                        Brush colorBase = isGo ? Brushes.Goldenrod : (currentOpen > priorClose ? Brushes.SeaGreen : Brushes.Crimson);
                        Brush writableBrush = colorBase.Clone();
                        writableBrush.Opacity = (double)AreaOpacity / 100.0;

                        activeGaps.Add(new GapData {
                            CloseLevel = priorClose,
                            OpenLevel = currentOpen,
                            Tag = "GAP_" + CurrentBar,
                            StartBar = CurrentBar,
                            StartTime = Time[0],
                            IsGapAndGo = isGo,
                            VisualBrush = writableBrush
                        });
                    }
                }
                currentHigh = High[0];
                currentLow = Low[0];
            }
            else
            {
                currentHigh = Math.Max(currentHigh, High[0]);
                currentLow = Math.Min(currentLow, Low[0]);
            }

            // 2. Renderizado y Limpieza de Gaps
            for (int i = activeGaps.Count - 1; i >= 0; i--)
            {
                var gap = activeGaps[i];
                if (!gap.IsActive) continue;

                bool isBullsGap = (gap.OpenLevel > gap.CloseLevel);
                if ((isBullsGap && Low[0] <= gap.CloseLevel) || (!isBullsGap && High[0] >= gap.CloseLevel))
                {
                    gap.IsActive = false;
                    RemoveDrawObject(gap.Tag);
                    Draw.Text(this, gap.Tag + "_txt", "GAP CLOSED " + gap.StartTime.ToString("dd/MM"), 0, gap.CloseLevel, Brushes.Gray);
                    continue;
                }

                // Asegurar que la opacidad se mantenga según el input del usuario
                if (gap.VisualBrush.Opacity != (double)AreaOpacity / 100.0)
                    gap.VisualBrush.Opacity = (double)AreaOpacity / 100.0;

                // Dibujar Rectángulo usando BarsAgo para máxima estabilidad
                Draw.Rectangle(this, gap.Tag, false, CurrentBar - gap.StartBar, gap.OpenLevel, 0, gap.CloseLevel, 
                               gap.VisualBrush, gap.VisualBrush, AreaOpacity);

                // Dibujar Texto según posición solicitada
                double yText = (TextPosition == EduSTextPos.Centered) ? (gap.OpenLevel + gap.CloseLevel) / 2 : gap.CloseLevel + (2 * TickSize);
                string label = string.Format("{0} GAP | {1}", gap.IsGapAndGo ? "INST" : "STD", gap.StartTime.ToString("dd/MM/yyyy"));
                Draw.Text(this, gap.Tag + "_txt", label, 0, yText, Brushes.White);
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            // Panel Superior Pro
            double prob = Math.Max(5, Math.Min(95, 100 - (VixValue * 2.2)));
            int activeCount = activeGaps.Count(g => g.IsActive);
            string info = $"EDUS PANEL | Gaps Activos: {activeCount} | Prob. Relleno (VIX): {prob:F0}%";

            SharpDX.DirectWrite.TextFormat textFormat = new SharpDX.DirectWrite.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Verdana", 10);
            float xPos = (float)chartControl.CanvasRight - 380 - RightMargin;
            
            // Fondo del panel
            RenderTarget.FillRectangle(new SharpDX.RectangleF(xPos - 5, 30, 305, 22), new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.Black) { Opacity = 0.4f });
            RenderTarget.DrawText(info, textFormat, new SharpDX.RectangleF(xPos, 34, 305, 22), new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, SharpDX.Color.White));
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduSMultiGapFinal[] cacheEduSMultiGapFinal;
		public EduS_Trader.EduSMultiGapFinal EduSMultiGapFinal(int minGapTicks, double vixValue, int areaOpacity, int rightMargin, EduSTextPos textPosition)
		{
			return EduSMultiGapFinal(Input, minGapTicks, vixValue, areaOpacity, rightMargin, textPosition);
		}

		public EduS_Trader.EduSMultiGapFinal EduSMultiGapFinal(ISeries<double> input, int minGapTicks, double vixValue, int areaOpacity, int rightMargin, EduSTextPos textPosition)
		{
			if (cacheEduSMultiGapFinal != null)
				for (int idx = 0; idx < cacheEduSMultiGapFinal.Length; idx++)
					if (cacheEduSMultiGapFinal[idx] != null && cacheEduSMultiGapFinal[idx].MinGapTicks == minGapTicks && cacheEduSMultiGapFinal[idx].VixValue == vixValue && cacheEduSMultiGapFinal[idx].AreaOpacity == areaOpacity && cacheEduSMultiGapFinal[idx].RightMargin == rightMargin && cacheEduSMultiGapFinal[idx].TextPosition == textPosition && cacheEduSMultiGapFinal[idx].EqualsInput(input))
						return cacheEduSMultiGapFinal[idx];
			return CacheIndicator<EduS_Trader.EduSMultiGapFinal>(new EduS_Trader.EduSMultiGapFinal(){ MinGapTicks = minGapTicks, VixValue = vixValue, AreaOpacity = areaOpacity, RightMargin = rightMargin, TextPosition = textPosition }, input, ref cacheEduSMultiGapFinal);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduSMultiGapFinal EduSMultiGapFinal(int minGapTicks, double vixValue, int areaOpacity, int rightMargin, EduSTextPos textPosition)
		{
			return indicator.EduSMultiGapFinal(Input, minGapTicks, vixValue, areaOpacity, rightMargin, textPosition);
		}

		public Indicators.EduS_Trader.EduSMultiGapFinal EduSMultiGapFinal(ISeries<double> input , int minGapTicks, double vixValue, int areaOpacity, int rightMargin, EduSTextPos textPosition)
		{
			return indicator.EduSMultiGapFinal(input, minGapTicks, vixValue, areaOpacity, rightMargin, textPosition);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduSMultiGapFinal EduSMultiGapFinal(int minGapTicks, double vixValue, int areaOpacity, int rightMargin, EduSTextPos textPosition)
		{
			return indicator.EduSMultiGapFinal(Input, minGapTicks, vixValue, areaOpacity, rightMargin, textPosition);
		}

		public Indicators.EduS_Trader.EduSMultiGapFinal EduSMultiGapFinal(ISeries<double> input , int minGapTicks, double vixValue, int areaOpacity, int rightMargin, EduSTextPos textPosition)
		{
			return indicator.EduSMultiGapFinal(input, minGapTicks, vixValue, areaOpacity, rightMargin, textPosition);
		}
	}
}

#endregion
