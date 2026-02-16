#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// Namespace personalizado para mantener consistencia con tus indicadores
namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader
{
    public class EduS_Keltner_StopTarget : Indicator
    {
        #region Variables internas
        private Series<double> diff;
        private EMA emaDiff;
        private EMA emaTypical;

        private DateTime lastUpdateTime;
        #endregion

        #region OnStateChange
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Calcula Stop y Target propuestos en base al ancho del Keltner Channel.";
                Name        = "EduS_Keltner_StopTarget";

                Calculate   = Calculate.OnEachTick;
                IsOverlay   = true;
                DisplayInDataBox      = true;
                DrawOnPricePanel      = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines   = true;
                PaintPriceMarkers       = true;
                IsSuspendedWhileInactive = true;

                // --- Parámetros Keltner (mismos que tu Master Indicator por defecto) ---
                Period          = 52;
                OffsetMultiplier = 3.5;

                // --- Parámetros de gestión de riesgo ---
                StopPercentOfBand = 0.30;    // 30% del ancho de la banda
                RiskRewardRatio   = 2.0;     // Target 1:2 sobre el stop

                // --- Frecuencia de refresco de la caja (en minutos) ---
                RefreshMinutes = 3;          // por defecto 3 minutos

                // --- Parámetros monetarios ---
                Contracts      = 1;          // Nº de contratos para cálculo en USD

                // --- Visuales de la caja ---
                InfoPosition   = TextPosition.TopRight;
                InfoBrush      = Brushes.LimeGreen;
                BackgroundBrush = Brushes.Black;
                AreaOpacity    = 60;
            }
            else if (State == State.Configure)
            {
                diff       = new Series<double>(this);
                emaDiff    = EMA(diff, Period);
                emaTypical = EMA(Typical, Period);

                lastUpdateTime = DateTime.MinValue;
            }
        }
        #endregion

        #region OnBarUpdate
        protected override void OnBarUpdate()
        {
            // Solo calculamos en la serie primaria
            if (BarsInProgress != 0)
                return;

            // Seguridad: asegurarse de tener suficientes barras para el EMA
            if (CurrentBar < Period)
                return;

            // --- Cálculo Keltner (idéntico a tu indicador Master) ---
            diff[0] = High[0] - Low[0];
            double middle = emaTypical[0];
            double offset = emaDiff[0] * OffsetMultiplier;
            double upper  = middle + offset;
            double lower  = middle - offset;

            double bandWidth = upper - lower;          // Ancho de la banda en puntos de precio

            // --- Control de frecuencia de refresco (en minutos) ---
            if (lastUpdateTime != DateTime.MinValue)
            {
                double minutesFromLast = (Time[0] - lastUpdateTime).TotalMinutes;
                if (minutesFromLast < RefreshMinutes)
                    return;
            }
            lastUpdateTime = Time[0];

            // --- Cálculo de Stop y Target en puntos ---
            double stopPoints   = bandWidth * StopPercentOfBand;
            double targetPoints = stopPoints * RiskRewardRatio;

            // --- Conversión a ticks ---
            double tickSize   = Instrument.MasterInstrument.TickSize;
            double stopTicks  = stopPoints   / tickSize;
            double targetTicks = targetPoints / tickSize;

            // --- Conversión a USD (por nº de contratos configurado) ---
            double pointValue = Instrument.MasterInstrument.PointValue; // USD por punto por contrato
            double stopUsd    = stopPoints   * pointValue * Contracts;
            double targetUsd  = targetPoints * pointValue * Contracts;

            // --- Texto a mostrar en la caja ---
            string infoText =
                "Keltner Width: " + bandWidth.ToString("0.00") + " pts" + Environment.NewLine +
                "Stop (" + (StopPercentOfBand * 100.0).ToString("0") + "% ancho): " +
                stopPoints.ToString("0.00") + " pts  |  " +
                stopTicks.ToString("0") + " ticks  |  $" +
                stopUsd.ToString("0.00") + Environment.NewLine +
                "Target (" + RiskRewardRatio.ToString("0.0") + ":1): " +
                targetPoints.ToString("0.00") + " pts  |  " +
                targetTicks.ToString("0") + " ticks  |  $" +
                targetUsd.ToString("0.00");

            try
            {
                Draw.TextFixed(
                    this,
                    "EduS_Keltner_StopTarget_Box",   // Tag único
                    infoText,
                    InfoPosition,
                    InfoBrush,
                    ChartControl.Properties.LabelFont,
                    BackgroundBrush,
                    BackgroundBrush,
                    AreaOpacity
                );
            }
            catch { }
        }
        #endregion

        #region Propiedades

        // --- 1. Parámetros Keltner ---
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Period", Order = 1, GroupName = "1. Keltner")]
        public int Period
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "OffsetMultiplier", Order = 2, GroupName = "1. Keltner")]
        public double OffsetMultiplier
        { get; set; }

        // --- 2. Gestión de riesgo (Stop & Target) ---
        [NinjaScriptProperty]
        [Range(0.01, 1.0)]
        [Display(Name = "StopPercentOfBand", Order = 1, GroupName = "2. Stop/Target")]
        public double StopPercentOfBand
        { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, double.MaxValue)]
        [Display(Name = "RiskRewardRatio", Order = 2, GroupName = "2. Stop/Target")]
        public double RiskRewardRatio
        { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contracts", Order = 3, GroupName = "2. Stop/Target")]
        public int Contracts
        { get; set; }

        // --- 3. Refresco de información ---
        [NinjaScriptProperty]
        [Range(1, 60)]
        [Display(Name = "RefreshMinutes", Description = "Minutos entre actualizaciones (1, 3, 5, 10, etc.)", Order = 1, GroupName = "3. Refresh")]
        public int RefreshMinutes
        { get; set; }

        // --- 4. Visual ---
        [NinjaScriptProperty]
        [Display(Name = "InfoPosition", Order = 1, GroupName = "4. Visual")]
        public TextPosition InfoPosition
        { get; set; }

        [XmlIgnore]
        [Display(Name = "InfoBrush", Order = 2, GroupName = "4. Visual")]
        public Brush InfoBrush
        { get; set; }

        [Browsable(false)]
        public string InfoBrushSerialize
        {
            get { return Serialize.BrushToString(InfoBrush); }
            set { InfoBrush = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "BackgroundBrush", Order = 3, GroupName = "4. Visual")]
        public Brush BackgroundBrush
        { get; set; }

        [Browsable(false)]
        public string BackgroundBrushSerialize
        {
            get { return Serialize.BrushToString(BackgroundBrush); }
            set { BackgroundBrush = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "AreaOpacity", Order = 4, GroupName = "4. Visual")]
        public int AreaOpacity
        { get; set; }

        #endregion
    }
}


#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduS_Keltner_StopTarget[] cacheEduS_Keltner_StopTarget;
		public EduS_Trader.EduS_Keltner_StopTarget EduS_Keltner_StopTarget(int period, double offsetMultiplier, double stopPercentOfBand, double riskRewardRatio, int contracts, int refreshMinutes, TextPosition infoPosition, int areaOpacity)
		{
			return EduS_Keltner_StopTarget(Input, period, offsetMultiplier, stopPercentOfBand, riskRewardRatio, contracts, refreshMinutes, infoPosition, areaOpacity);
		}

		public EduS_Trader.EduS_Keltner_StopTarget EduS_Keltner_StopTarget(ISeries<double> input, int period, double offsetMultiplier, double stopPercentOfBand, double riskRewardRatio, int contracts, int refreshMinutes, TextPosition infoPosition, int areaOpacity)
		{
			if (cacheEduS_Keltner_StopTarget != null)
				for (int idx = 0; idx < cacheEduS_Keltner_StopTarget.Length; idx++)
					if (cacheEduS_Keltner_StopTarget[idx] != null && cacheEduS_Keltner_StopTarget[idx].Period == period && cacheEduS_Keltner_StopTarget[idx].OffsetMultiplier == offsetMultiplier && cacheEduS_Keltner_StopTarget[idx].StopPercentOfBand == stopPercentOfBand && cacheEduS_Keltner_StopTarget[idx].RiskRewardRatio == riskRewardRatio && cacheEduS_Keltner_StopTarget[idx].Contracts == contracts && cacheEduS_Keltner_StopTarget[idx].RefreshMinutes == refreshMinutes && cacheEduS_Keltner_StopTarget[idx].InfoPosition == infoPosition && cacheEduS_Keltner_StopTarget[idx].AreaOpacity == areaOpacity && cacheEduS_Keltner_StopTarget[idx].EqualsInput(input))
						return cacheEduS_Keltner_StopTarget[idx];
			return CacheIndicator<EduS_Trader.EduS_Keltner_StopTarget>(new EduS_Trader.EduS_Keltner_StopTarget(){ Period = period, OffsetMultiplier = offsetMultiplier, StopPercentOfBand = stopPercentOfBand, RiskRewardRatio = riskRewardRatio, Contracts = contracts, RefreshMinutes = refreshMinutes, InfoPosition = infoPosition, AreaOpacity = areaOpacity }, input, ref cacheEduS_Keltner_StopTarget);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduS_Keltner_StopTarget EduS_Keltner_StopTarget(int period, double offsetMultiplier, double stopPercentOfBand, double riskRewardRatio, int contracts, int refreshMinutes, TextPosition infoPosition, int areaOpacity)
		{
			return indicator.EduS_Keltner_StopTarget(Input, period, offsetMultiplier, stopPercentOfBand, riskRewardRatio, contracts, refreshMinutes, infoPosition, areaOpacity);
		}

		public Indicators.EduS_Trader.EduS_Keltner_StopTarget EduS_Keltner_StopTarget(ISeries<double> input , int period, double offsetMultiplier, double stopPercentOfBand, double riskRewardRatio, int contracts, int refreshMinutes, TextPosition infoPosition, int areaOpacity)
		{
			return indicator.EduS_Keltner_StopTarget(input, period, offsetMultiplier, stopPercentOfBand, riskRewardRatio, contracts, refreshMinutes, infoPosition, areaOpacity);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduS_Keltner_StopTarget EduS_Keltner_StopTarget(int period, double offsetMultiplier, double stopPercentOfBand, double riskRewardRatio, int contracts, int refreshMinutes, TextPosition infoPosition, int areaOpacity)
		{
			return indicator.EduS_Keltner_StopTarget(Input, period, offsetMultiplier, stopPercentOfBand, riskRewardRatio, contracts, refreshMinutes, infoPosition, areaOpacity);
		}

		public Indicators.EduS_Trader.EduS_Keltner_StopTarget EduS_Keltner_StopTarget(ISeries<double> input , int period, double offsetMultiplier, double stopPercentOfBand, double riskRewardRatio, int contracts, int refreshMinutes, TextPosition infoPosition, int areaOpacity)
		{
			return indicator.EduS_Keltner_StopTarget(input, period, offsetMultiplier, stopPercentOfBand, riskRewardRatio, contracts, refreshMinutes, infoPosition, areaOpacity);
		}
	}
}

#endregion
