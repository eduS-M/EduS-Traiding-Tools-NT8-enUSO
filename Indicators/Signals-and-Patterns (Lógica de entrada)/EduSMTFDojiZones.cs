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
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader
{
    public class EduS_MTF_Doji_Zones : Indicator
    {
        public enum DojiType { Clasica, Libelula, Lapida, CuatroPrecios }
        public enum TimeFrameId { M5, M15, H1, H4 }

        private class DojiBox
        {
            public TimeFrameId TF;
            public DateTime StartTime;
            public DateTime EndTime;
            public double Low;
            public double High;
            public DojiType Type;
            public string Tag;
        }

        private List<DojiBox> boxes;
        private int bip5m = -1, bip15m = -1, bip60m = -1, bip240m = -1;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Detecta Dojis HTF. Zonas por color de TF, Punto y Etiqueta globales.";
                Name = "EduS_MTF_Doji_Zones";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;

                // Parámetros lógicos originales
                BodyToWickRatioMax = 0.15;
                MinWickRatio = 0.30;
                DragonflyThreshold = 0.60;
                GravestoneThreshold = 0.60;
                SymmetryTolerance = 0.20;
                MinRangeTicks = 2;
                IncludeFourPriceDoji = true;

                Usar5m = true; Usar15m = true; Usar60m = true; Usar240m = false;
                DiasHaciaAtras = 2;
                PermitirSolapamiento = true;
                MostrarPuntoCuerpo = true;
                MostrarEtiqueta = true;

                // Colores predeterminados por TF
                DojiColor5m = Brushes.DodgerBlue;
                DojiColor15m = Brushes.MediumSeaGreen;
                DojiColor60m = Brushes.Goldenrod;
                DojiColor240m = Brushes.IndianRed;
                
                // Colores Globales para Punto y Etiqueta
                PuntoCuerpoColor = Brushes.White;
                EtiquetaColor = Brushes.White;

                OpacidadRectangulo = 40;
                TamanoPuntoCuerpo = 10;
            }
            else if (State == State.Configure)
            {
                if (Usar5m) { AddDataSeries(BarsPeriodType.Minute, 5); bip5m = BarsArray.Length - 1; }
                if (Usar15m) { AddDataSeries(BarsPeriodType.Minute, 15); bip15m = BarsArray.Length - 1; }
                if (Usar60m) { AddDataSeries(BarsPeriodType.Minute, 60); bip60m = BarsArray.Length - 1; }
                if (Usar240m) { AddDataSeries(BarsPeriodType.Minute, 240); bip240m = BarsArray.Length - 1; }
            }
            else if (State == State.DataLoaded)
            {
                boxes = new List<DojiBox>();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == 0 || CurrentBars[BarsInProgress] < 1 || CurrentBars[0] < 1)
                return;

            TimeFrameId tf;
            Brush tfColor; 
            bool tfActiva;

            if (!TryGetTimeFrameForBIP(BarsInProgress, out tf, out tfColor, out tfActiva) || !tfActiva)
                return;

            DateTime barTime = Times[BarsInProgress][0];
            if (barTime < DateTime.Now.AddDays(-DiasHaciaAtras)) return;

            double o = Opens[BarsInProgress][0];
            double h = Highs[BarsInProgress][0];
            double l = Lows[BarsInProgress][0];
            double c = Closes[BarsInProgress][0];
            double rango = h - l;

            if (rango <= TickSize * MinRangeTicks) return;

            double cuerpo = Math.Abs(c - o);
            double upperWick = h - Math.Max(o, c);
            double lowerWick = Math.Min(o, c) - l;

            if (cuerpo / rango > BodyToWickRatioMax) return;

            DojiType tipo;
            double upperRatio = upperWick / rango;
            double lowerRatio = lowerWick / rango;

            if (IncludeFourPriceDoji && cuerpo == 0 && upperWick == 0 && lowerWick == 0) tipo = DojiType.CuatroPrecios;
            else if (lowerRatio >= DragonflyThreshold && upperRatio < MinWickRatio) tipo = DojiType.Libelula;
            else if (upperRatio >= GravestoneThreshold && lowerRatio < MinWickRatio) tipo = DojiType.Lapida;
            else if (Math.Abs(upperRatio - lowerRatio) <= SymmetryTolerance) tipo = DojiType.Clasica;
            else return;

            int tfMinutes = GetMinutesForTimeFrame(tf);
            DateTime endTime = barTime;
            DateTime startTime = barTime.AddMinutes(-tfMinutes);

            int startIdx = BarsArray[0].GetBar(startTime);
            int endIdx = BarsArray[0].GetBar(endTime);

            if (startIdx < 0 || endIdx < 0) return;

            int startBarsAgo = CurrentBars[0] - startIdx;
            int endBarsAgo = CurrentBars[0] - endIdx;

            if (!PermitirSolapamiento)
            {
                if (boxes.Any(b => (startTime <= b.EndTime && endTime >= b.StartTime) && GetTFRank(b.TF) > GetTFRank(tf)))
                    return;
            }

            string tagBase = string.Format("MTFDoji_{0}_{1:yyyyMMdd_HHmm}", GetTFLabel(tf), barTime);

            // --- DIBUJO DE RECTÁNGULO (Usa el color de la temporalidad para el sombreado) ---
            // El primer 'tfColor' es el borde, el segundo es el relleno.
            Draw.Rectangle(this, tagBase, false, startBarsAgo, h, endBarsAgo, l, tfColor, tfColor, OpacidadRectangulo);

            // --- DIBUJO DE PUNTO (Mantiene Color Global definido por el usuario) ---
            if (MostrarPuntoCuerpo)
                Draw.Dot(this, tagBase + "_body", false, endBarsAgo, (o + c) / 2.0, PuntoCuerpoColor);

            // --- DIBUJO DE ETIQUETA (Mantiene Color Global definido por el usuario) ---
            if (MostrarEtiqueta)
            {
                string texto = string.Format("D.{0}.{1}", GetTFLabel(tf), GetDojiTypeLabel(tipo));
                Draw.Text(this, tagBase + "_label", texto, endBarsAgo, h + (2 * TickSize), EtiquetaColor);
            }

            boxes.Add(new DojiBox { TF = tf, StartTime = startTime, EndTime = endTime, Low = l, High = h, Type = tipo, Tag = tagBase });
        }

        #region Helpers
        private bool TryGetTimeFrameForBIP(int bip, out TimeFrameId tf, out Brush brush, out bool enabled)
        {
            tf = TimeFrameId.M5; brush = Brushes.Gray; enabled = false;
            if (bip == bip5m) { tf = TimeFrameId.M5; brush = DojiColor5m; enabled = Usar5m; return true; }
            if (bip == bip15m) { tf = TimeFrameId.M15; brush = DojiColor15m; enabled = Usar15m; return true; }
            if (bip == bip60m) { tf = TimeFrameId.H1; brush = DojiColor60m; enabled = Usar60m; return true; }
            if (bip == bip240m) { tf = TimeFrameId.H4; brush = DojiColor240m; enabled = Usar240m; return true; }
            return false;
        }

        private int GetMinutesForTimeFrame(TimeFrameId tf) {
            if (tf == TimeFrameId.M5) return 5; if (tf == TimeFrameId.M15) return 15;
            if (tf == TimeFrameId.H1) return 60; return 240;
        }

        private int GetTFRank(TimeFrameId tf) { return (int)tf; }

        private string GetTFLabel(TimeFrameId tf) {
            if (tf == TimeFrameId.M5) return "5m"; if (tf == TimeFrameId.M15) return "15m";
            if (tf == TimeFrameId.H1) return "1H"; return "4H";
        }

        private string GetDojiTypeLabel(DojiType tipo) {
            if (tipo == DojiType.CuatroPrecios) return "4P";
            return tipo.ToString();
        }
        #endregion

        #region Propiedades
        [NinjaScriptProperty] [Display(Name = "Ratio Cuerpo Max", GroupName = "Doji")] public double BodyToWickRatioMax { get; set; }
        [NinjaScriptProperty] [Display(Name = "Min Wick Ratio", GroupName = "Doji")] public double MinWickRatio { get; set; }
        [NinjaScriptProperty] [Display(Name = "Dragonfly Threshold", GroupName = "Doji")] public double DragonflyThreshold { get; set; }
        [NinjaScriptProperty] [Display(Name = "Gravestone Threshold", GroupName = "Doji")] public double GravestoneThreshold { get; set; }
        [NinjaScriptProperty] [Display(Name = "Symmetry Tolerance", GroupName = "Doji")] public double SymmetryTolerance { get; set; }
        [NinjaScriptProperty] [Display(Name = "Min Range Ticks", GroupName = "Doji")] public int MinRangeTicks { get; set; }
        [NinjaScriptProperty] [Display(Name = "Include 4P Doji", GroupName = "Doji")] public bool IncludeFourPriceDoji { get; set; }

        [NinjaScriptProperty] [Display(Name = "Usar 5m", GroupName = "Temporalidades")] public bool Usar5m { get; set; }
        [NinjaScriptProperty] [Display(Name = "Usar 15m", GroupName = "Temporalidades")] public bool Usar15m { get; set; }
        [NinjaScriptProperty] [Display(Name = "Usar 1H", GroupName = "Temporalidades")] public bool Usar60m { get; set; }
        [NinjaScriptProperty] [Display(Name = "Usar 4H", GroupName = "Temporalidades")] public bool Usar240m { get; set; }

        [NinjaScriptProperty] [Display(Name = "Dias Atrás", GroupName = "General")] public int DiasHaciaAtras { get; set; }
        [NinjaScriptProperty] [Display(Name = "Permitir Solapamiento", GroupName = "General")] public bool PermitirSolapamiento { get; set; }
        [NinjaScriptProperty] [Display(Name = "Mostrar Punto", GroupName = "Visual")] public bool MostrarPuntoCuerpo { get; set; }
        [NinjaScriptProperty] [Display(Name = "Mostrar Etiqueta", GroupName = "Visual")] public bool MostrarEtiqueta { get; set; }
        [NinjaScriptProperty] [Display(Name = "Tamaño Punto", GroupName = "Visual")] public int TamanoPuntoCuerpo { get; set; }
        [NinjaScriptProperty] [Display(Name = "Opacidad %", GroupName = "Visual")] public int OpacidadRectangulo { get; set; }

        [XmlIgnore] [Display(Name = "Color 5m", GroupName = "Colores por TF")] public Brush DojiColor5m { get; set; }
        [Browsable(false)] public string DojiColor5mS { get { return Serialize.BrushToString(DojiColor5m); } set { DojiColor5m = Serialize.StringToBrush(value); } }
        [XmlIgnore] [Display(Name = "Color 15m", GroupName = "Colores por TF")] public Brush DojiColor15m { get; set; }
        [Browsable(false)] public string DojiColor15mS { get { return Serialize.BrushToString(DojiColor15m); } set { DojiColor15m = Serialize.StringToBrush(value); } }
        [XmlIgnore] [Display(Name = "Color 1H", GroupName = "Colores por TF")] public Brush DojiColor60m { get; set; }
        [Browsable(false)] public string DojiColor60mS { get { return Serialize.BrushToString(DojiColor60m); } set { DojiColor60m = Serialize.StringToBrush(value); } }
        [XmlIgnore] [Display(Name = "Color 4H", GroupName = "Colores por TF")] public Brush DojiColor240m { get; set; }
        [Browsable(false)] public string DojiColor240mS { get { return Serialize.BrushToString(DojiColor240m); } set { DojiColor240m = Serialize.StringToBrush(value); } }
        
        [XmlIgnore] [Display(Name = "Color Punto (Global)", GroupName = "Colores Globales")] public Brush PuntoCuerpoColor { get; set; }
        [Browsable(false)] public string PuntoCuerpoColorS { get { return Serialize.BrushToString(PuntoCuerpoColor); } set { PuntoCuerpoColor = Serialize.StringToBrush(value); } }
        [XmlIgnore] [Display(Name = "Color Etiqueta (Global)", GroupName = "Colores Globales")] public Brush EtiquetaColor { get; set; }
        [Browsable(false)] public string EtiquetaColorS { get { return Serialize.BrushToString(EtiquetaColor); } set { EtiquetaColor = Serialize.StringToBrush(value); } }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduS_MTF_Doji_Zones[] cacheEduS_MTF_Doji_Zones;
		public EduS_Trader.EduS_MTF_Doji_Zones EduS_MTF_Doji_Zones(double bodyToWickRatioMax, double minWickRatio, double dragonflyThreshold, double gravestoneThreshold, double symmetryTolerance, int minRangeTicks, bool includeFourPriceDoji, bool usar5m, bool usar15m, bool usar60m, bool usar240m, int diasHaciaAtras, bool permitirSolapamiento, bool mostrarPuntoCuerpo, bool mostrarEtiqueta, int tamanoPuntoCuerpo, int opacidadRectangulo)
		{
			return EduS_MTF_Doji_Zones(Input, bodyToWickRatioMax, minWickRatio, dragonflyThreshold, gravestoneThreshold, symmetryTolerance, minRangeTicks, includeFourPriceDoji, usar5m, usar15m, usar60m, usar240m, diasHaciaAtras, permitirSolapamiento, mostrarPuntoCuerpo, mostrarEtiqueta, tamanoPuntoCuerpo, opacidadRectangulo);
		}

		public EduS_Trader.EduS_MTF_Doji_Zones EduS_MTF_Doji_Zones(ISeries<double> input, double bodyToWickRatioMax, double minWickRatio, double dragonflyThreshold, double gravestoneThreshold, double symmetryTolerance, int minRangeTicks, bool includeFourPriceDoji, bool usar5m, bool usar15m, bool usar60m, bool usar240m, int diasHaciaAtras, bool permitirSolapamiento, bool mostrarPuntoCuerpo, bool mostrarEtiqueta, int tamanoPuntoCuerpo, int opacidadRectangulo)
		{
			if (cacheEduS_MTF_Doji_Zones != null)
				for (int idx = 0; idx < cacheEduS_MTF_Doji_Zones.Length; idx++)
					if (cacheEduS_MTF_Doji_Zones[idx] != null && cacheEduS_MTF_Doji_Zones[idx].BodyToWickRatioMax == bodyToWickRatioMax && cacheEduS_MTF_Doji_Zones[idx].MinWickRatio == minWickRatio && cacheEduS_MTF_Doji_Zones[idx].DragonflyThreshold == dragonflyThreshold && cacheEduS_MTF_Doji_Zones[idx].GravestoneThreshold == gravestoneThreshold && cacheEduS_MTF_Doji_Zones[idx].SymmetryTolerance == symmetryTolerance && cacheEduS_MTF_Doji_Zones[idx].MinRangeTicks == minRangeTicks && cacheEduS_MTF_Doji_Zones[idx].IncludeFourPriceDoji == includeFourPriceDoji && cacheEduS_MTF_Doji_Zones[idx].Usar5m == usar5m && cacheEduS_MTF_Doji_Zones[idx].Usar15m == usar15m && cacheEduS_MTF_Doji_Zones[idx].Usar60m == usar60m && cacheEduS_MTF_Doji_Zones[idx].Usar240m == usar240m && cacheEduS_MTF_Doji_Zones[idx].DiasHaciaAtras == diasHaciaAtras && cacheEduS_MTF_Doji_Zones[idx].PermitirSolapamiento == permitirSolapamiento && cacheEduS_MTF_Doji_Zones[idx].MostrarPuntoCuerpo == mostrarPuntoCuerpo && cacheEduS_MTF_Doji_Zones[idx].MostrarEtiqueta == mostrarEtiqueta && cacheEduS_MTF_Doji_Zones[idx].TamanoPuntoCuerpo == tamanoPuntoCuerpo && cacheEduS_MTF_Doji_Zones[idx].OpacidadRectangulo == opacidadRectangulo && cacheEduS_MTF_Doji_Zones[idx].EqualsInput(input))
						return cacheEduS_MTF_Doji_Zones[idx];
			return CacheIndicator<EduS_Trader.EduS_MTF_Doji_Zones>(new EduS_Trader.EduS_MTF_Doji_Zones(){ BodyToWickRatioMax = bodyToWickRatioMax, MinWickRatio = minWickRatio, DragonflyThreshold = dragonflyThreshold, GravestoneThreshold = gravestoneThreshold, SymmetryTolerance = symmetryTolerance, MinRangeTicks = minRangeTicks, IncludeFourPriceDoji = includeFourPriceDoji, Usar5m = usar5m, Usar15m = usar15m, Usar60m = usar60m, Usar240m = usar240m, DiasHaciaAtras = diasHaciaAtras, PermitirSolapamiento = permitirSolapamiento, MostrarPuntoCuerpo = mostrarPuntoCuerpo, MostrarEtiqueta = mostrarEtiqueta, TamanoPuntoCuerpo = tamanoPuntoCuerpo, OpacidadRectangulo = opacidadRectangulo }, input, ref cacheEduS_MTF_Doji_Zones);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduS_MTF_Doji_Zones EduS_MTF_Doji_Zones(double bodyToWickRatioMax, double minWickRatio, double dragonflyThreshold, double gravestoneThreshold, double symmetryTolerance, int minRangeTicks, bool includeFourPriceDoji, bool usar5m, bool usar15m, bool usar60m, bool usar240m, int diasHaciaAtras, bool permitirSolapamiento, bool mostrarPuntoCuerpo, bool mostrarEtiqueta, int tamanoPuntoCuerpo, int opacidadRectangulo)
		{
			return indicator.EduS_MTF_Doji_Zones(Input, bodyToWickRatioMax, minWickRatio, dragonflyThreshold, gravestoneThreshold, symmetryTolerance, minRangeTicks, includeFourPriceDoji, usar5m, usar15m, usar60m, usar240m, diasHaciaAtras, permitirSolapamiento, mostrarPuntoCuerpo, mostrarEtiqueta, tamanoPuntoCuerpo, opacidadRectangulo);
		}

		public Indicators.EduS_Trader.EduS_MTF_Doji_Zones EduS_MTF_Doji_Zones(ISeries<double> input , double bodyToWickRatioMax, double minWickRatio, double dragonflyThreshold, double gravestoneThreshold, double symmetryTolerance, int minRangeTicks, bool includeFourPriceDoji, bool usar5m, bool usar15m, bool usar60m, bool usar240m, int diasHaciaAtras, bool permitirSolapamiento, bool mostrarPuntoCuerpo, bool mostrarEtiqueta, int tamanoPuntoCuerpo, int opacidadRectangulo)
		{
			return indicator.EduS_MTF_Doji_Zones(input, bodyToWickRatioMax, minWickRatio, dragonflyThreshold, gravestoneThreshold, symmetryTolerance, minRangeTicks, includeFourPriceDoji, usar5m, usar15m, usar60m, usar240m, diasHaciaAtras, permitirSolapamiento, mostrarPuntoCuerpo, mostrarEtiqueta, tamanoPuntoCuerpo, opacidadRectangulo);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduS_MTF_Doji_Zones EduS_MTF_Doji_Zones(double bodyToWickRatioMax, double minWickRatio, double dragonflyThreshold, double gravestoneThreshold, double symmetryTolerance, int minRangeTicks, bool includeFourPriceDoji, bool usar5m, bool usar15m, bool usar60m, bool usar240m, int diasHaciaAtras, bool permitirSolapamiento, bool mostrarPuntoCuerpo, bool mostrarEtiqueta, int tamanoPuntoCuerpo, int opacidadRectangulo)
		{
			return indicator.EduS_MTF_Doji_Zones(Input, bodyToWickRatioMax, minWickRatio, dragonflyThreshold, gravestoneThreshold, symmetryTolerance, minRangeTicks, includeFourPriceDoji, usar5m, usar15m, usar60m, usar240m, diasHaciaAtras, permitirSolapamiento, mostrarPuntoCuerpo, mostrarEtiqueta, tamanoPuntoCuerpo, opacidadRectangulo);
		}

		public Indicators.EduS_Trader.EduS_MTF_Doji_Zones EduS_MTF_Doji_Zones(ISeries<double> input , double bodyToWickRatioMax, double minWickRatio, double dragonflyThreshold, double gravestoneThreshold, double symmetryTolerance, int minRangeTicks, bool includeFourPriceDoji, bool usar5m, bool usar15m, bool usar60m, bool usar240m, int diasHaciaAtras, bool permitirSolapamiento, bool mostrarPuntoCuerpo, bool mostrarEtiqueta, int tamanoPuntoCuerpo, int opacidadRectangulo)
		{
			return indicator.EduS_MTF_Doji_Zones(input, bodyToWickRatioMax, minWickRatio, dragonflyThreshold, gravestoneThreshold, symmetryTolerance, minRangeTicks, includeFourPriceDoji, usar5m, usar15m, usar60m, usar240m, diasHaciaAtras, permitirSolapamiento, mostrarPuntoCuerpo, mostrarEtiqueta, tamanoPuntoCuerpo, opacidadRectangulo);
		}
	}
}

#endregion
