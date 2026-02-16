#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;

using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader
{
    public class EduS_Institutional_Naked_Pocs : Indicator
    {
        public class SimpleNakedLevel
        {
            public double Price;
            public DateTime Date;
            public bool IsActive;
        }

        private List<SimpleNakedLevel> nakedLevels;
        private Dictionary<double, double> currentSessionVolume; 
        private DateTime lastSessionDate;
        private double tickSize;
        
        // --- RECURSOS DIRECT2D ---
        private SharpDX.Direct2D1.SolidColorBrush b_NakedDX;
        private SharpDX.Direct2D1.StrokeStyle s_Style;
        private SharpDX.DirectWrite.TextFormat t_Format;
        private SharpDX.Direct2D1.SolidColorBrush b_TextBrush;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "EduS_Institutional_Naked_Pocs_v4";
                Description = "v4: Texto sobre la línea + Precio visible + Margen configurable.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                IsSuspendedWhileInactive = true;
                ScaleJustification = ScaleJustification.Right;

                // Configuración Default
                DaysToLoad = 8; 
                NakedColor = System.Windows.Media.Brushes.Gold; 
                TextColor = System.Windows.Media.Brushes.White;
                LineWidth = 2;
                IgnoreOvernightTouches = true; 
                RthStartTime = 930; 
                RthEndTime = 1600;  
                ShowLabels = true;
                LabelFontSize = 10;
                
                // Margen derecho (px)
                LabelRightMargin = 120; 
            }
            else if (State == State.Configure)
            {
                nakedLevels = new List<SimpleNakedLevel>();
                currentSessionVolume = new Dictionary<double, double>();
                lastSessionDate = DateTime.MinValue;
            }
            else if (State == State.DataLoaded)
            {
                if (Instrument != null)
                    tickSize = Instrument.MasterInstrument.TickSize;
            }
            else if (State == State.Terminated)
            {
                DisposeDXResources();
            }
        }

        public override void OnRenderTargetChanged()
        {
            base.OnRenderTargetChanged();
            DisposeDXResources();
        }

        private void DisposeDXResources()
        {
            if (b_NakedDX != null) { b_NakedDX.Dispose(); b_NakedDX = null; }
            if (s_Style != null) { s_Style.Dispose(); s_Style = null; }
            if (t_Format != null) { t_Format.Dispose(); t_Format = null; }
            if (b_TextBrush != null) { b_TextBrush.Dispose(); b_TextBrush = null; }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 10) return;

            // Lógica de Sesión
            if (Bars.IsFirstBarOfSession && lastSessionDate != DateTime.MinValue)
            {
                CalculateAndStorePOC(lastSessionDate);
                currentSessionVolume.Clear();
            }
            else if (Time[0].Date > lastSessionDate && lastSessionDate != DateTime.MinValue)
            {
                 CalculateAndStorePOC(lastSessionDate);
                 currentSessionVolume.Clear();
            }
            lastSessionDate = Time[0].Date;

            // Distribución de Volumen
            DistributeVolume(High[0], Low[0], Volume[0]);

            // Verificar Toques
            if (nakedLevels.Count > 0)
            {
                CheckTouches();
            }
        }

        private void DistributeVolume(double high, double low, double vol)
        {
            if (tickSize <= 0) return;
            int steps = (int)Math.Round((high - low) / tickSize);
            
            if (steps < 1) 
            {
                AddToVolumeMap(Close[0], vol);
                return;
            }

            double volPerTick = vol / (double)(steps + 1);

            for (int i = 0; i <= steps; i++)
            {
                double priceLevel = low + (i * tickSize);
                priceLevel = Math.Round(priceLevel / tickSize) * tickSize;
                AddToVolumeMap(priceLevel, volPerTick);
            }
        }

        private void AddToVolumeMap(double price, double vol)
        {
            if (!currentSessionVolume.ContainsKey(price)) 
                currentSessionVolume[price] = 0;
            currentSessionVolume[price] += vol;
        }

        private void CalculateAndStorePOC(DateTime dateRef)
        {
            if (currentSessionVolume.Count == 0) return;

            double maxVol = -1;
            double pocPrice = 0;

            foreach (var kvp in currentSessionVolume)
            {
                if (kvp.Value > maxVol)
                {
                    maxVol = kvp.Value;
                    pocPrice = kvp.Key;
                }
            }

            if (maxVol > 0)
            {
                nakedLevels.Add(new SimpleNakedLevel
                {
                    Price = pocPrice,
                    Date = dateRef,
                    IsActive = true
                });
            }
        }

        private void CheckTouches()
        {
            double h = High[0];
            double l = Low[0];
            
            int currentHHMM = Time[0].Hour * 100 + Time[0].Minute;
            bool isRTH = (currentHHMM >= RthStartTime && currentHHMM < RthEndTime);

            for (int i = nakedLevels.Count - 1; i >= 0; i--)
            {
                var lvl = nakedLevels[i];
                if (!lvl.IsActive) continue;

                if (h >= lvl.Price && l <= lvl.Price)
                {
                    if (IgnoreOvernightTouches && !isRTH) continue;
                    lvl.IsActive = false; 
                }
            }
        }

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (nakedLevels == null || nakedLevels.Count == 0) return;
            if (chartControl == null || chartScale == null || RenderTarget == null) return;

            // Inicialización de Recursos
            if (b_NakedDX == null)
            {
                try 
                {
                    System.Windows.Media.SolidColorBrush wpfBrush = NakedColor as System.Windows.Media.SolidColorBrush;
                    Color4 dxColor = (wpfBrush != null) 
                        ? new Color4(wpfBrush.Color.R / 255f, wpfBrush.Color.G / 255f, wpfBrush.Color.B / 255f, wpfBrush.Color.A / 255f) 
                        : new Color4(1, 0.84f, 0, 1);
                    
                    b_NakedDX = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, dxColor);

                    s_Style = new SharpDX.Direct2D1.StrokeStyle(RenderTarget.Factory, new StrokeStyleProperties
                    {
                        DashStyle = SharpDX.Direct2D1.DashStyle.Dash
                    });

                    System.Windows.Media.SolidColorBrush txtBrush = TextColor as System.Windows.Media.SolidColorBrush;
                    Color4 txtColor = (txtBrush != null)
                        ? new Color4(txtBrush.Color.R / 255f, txtBrush.Color.G / 255f, txtBrush.Color.B / 255f, 1f)
                        : new Color4(1,1,1,1);

                    b_TextBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, txtColor);
                    
                    t_Format = new SharpDX.DirectWrite.TextFormat(Core.Globals.DirectWriteFactory, "Arial", 
                        SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, (float)LabelFontSize);
                    
                    // ALINEACIÓN TEXTO: A la derecha de su caja, y pegado al fondo (bottom) de su caja
                    t_Format.TextAlignment = TextAlignment.Trailing; 
                    t_Format.ParagraphAlignment = ParagraphAlignment.Far; 
                }
                catch { return; }
            }

            float xRight = chartControl.CanvasRight;
            float xLeftCanvas = (float)chartControl.CanvasLeft;
            float canvasHeight = (float)chartControl.ActualHeight;

            foreach (var lvl in nakedLevels)
            {
                if (!lvl.IsActive) continue;

                float y = (float)chartScale.GetYByValue(lvl.Price);
                if (y < -50 || y > canvasHeight + 50) continue;

                float xStart = (float)chartControl.GetXByTime(lvl.Date);
                if (xStart < xLeftCanvas) xStart = xLeftCanvas;

                // 1. Dibujar Línea
                RenderTarget.DrawLine(new Vector2(xStart, y), new Vector2(xRight, y), b_NakedDX, LineWidth, s_Style);

                // 2. Dibujar Texto (Mejorado v4)
                if (ShowLabels)
                {
                    // Formato: "nPOC 12/29 : 4550.25"
                    string labelText = string.Format("nPOC {0:MM/dd} : {1}", lvl.Date, lvl.Price.ToString("N2"));
                    
                    // Configuración Posición
                    float textWidth = 180f; // Más ancho para que quepa el precio
                    float xPosition = xRight - (float)LabelRightMargin; 
                    float textHeight = 25f;

                    // COORDENADA Y: (y - textHeight - 2)
                    // Esto coloca la base de la caja de texto 2 píxeles ARRIBA de la línea del precio.
                    float yTop = y - textHeight - 2;

                    RectangleF rect = new RectangleF(xPosition - textWidth, yTop, textWidth, textHeight);
                    
                    RenderTarget.DrawText(labelText, t_Format, rect, b_TextBrush);
                }
            }
        }

        #region Properties
        [NinjaScriptProperty]
        [Range(1, 365)]
        [Display(Name = "Días a Cargar", GroupName = "Configuración")]
        public int DaysToLoad { get; set; }

        // --- Visual ---
        [XmlIgnore] 
        [Display(Name = "Color Línea", GroupName = "Visual")]
        public System.Windows.Media.Brush NakedColor { get; set; }

        [Browsable(false)]
        public string NakedColorSerializable
        {
            get { return Serialize.BrushToString(NakedColor); }
            set { NakedColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Color Texto", GroupName = "Visual")]
        public System.Windows.Media.Brush TextColor { get; set; }

        [Browsable(false)]
        public string TextColorSerializable
        {
            get { return Serialize.BrushToString(TextColor); }
            set { TextColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Grosor Línea", GroupName = "Visual")]
        public float LineWidth { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Etiquetas", GroupName = "Visual")]
        public bool ShowLabels { get; set; }
        
        [NinjaScriptProperty]
        [Range(6, 40)]
        [Display(Name = "Tamaño Fuente", GroupName = "Visual")]
        public int LabelFontSize { get; set; }

        [NinjaScriptProperty]
        [Range(0, 1000)]
        [Display(Name = "Margen Derecho Texto (px)", Description="Distancia desde el borde derecho.", GroupName = "Visual")]
        public int LabelRightMargin { get; set; }

        // --- Lógica ---
        [NinjaScriptProperty]
        [Display(Name = "Ignorar Toques Overnight", GroupName = "Lógica")]
        public bool IgnoreOvernightTouches { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Inicio RTH (HHmm)", GroupName = "Lógica")]
        public int RthStartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Fin RTH (HHmm)", GroupName = "Lógica")]
        public int RthEndTime { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduS_Institutional_Naked_Pocs[] cacheEduS_Institutional_Naked_Pocs;
		public EduS_Trader.EduS_Institutional_Naked_Pocs EduS_Institutional_Naked_Pocs(int daysToLoad, float lineWidth, bool showLabels, int labelFontSize, int labelRightMargin, bool ignoreOvernightTouches, int rthStartTime, int rthEndTime)
		{
			return EduS_Institutional_Naked_Pocs(Input, daysToLoad, lineWidth, showLabels, labelFontSize, labelRightMargin, ignoreOvernightTouches, rthStartTime, rthEndTime);
		}

		public EduS_Trader.EduS_Institutional_Naked_Pocs EduS_Institutional_Naked_Pocs(ISeries<double> input, int daysToLoad, float lineWidth, bool showLabels, int labelFontSize, int labelRightMargin, bool ignoreOvernightTouches, int rthStartTime, int rthEndTime)
		{
			if (cacheEduS_Institutional_Naked_Pocs != null)
				for (int idx = 0; idx < cacheEduS_Institutional_Naked_Pocs.Length; idx++)
					if (cacheEduS_Institutional_Naked_Pocs[idx] != null && cacheEduS_Institutional_Naked_Pocs[idx].DaysToLoad == daysToLoad && cacheEduS_Institutional_Naked_Pocs[idx].LineWidth == lineWidth && cacheEduS_Institutional_Naked_Pocs[idx].ShowLabels == showLabels && cacheEduS_Institutional_Naked_Pocs[idx].LabelFontSize == labelFontSize && cacheEduS_Institutional_Naked_Pocs[idx].LabelRightMargin == labelRightMargin && cacheEduS_Institutional_Naked_Pocs[idx].IgnoreOvernightTouches == ignoreOvernightTouches && cacheEduS_Institutional_Naked_Pocs[idx].RthStartTime == rthStartTime && cacheEduS_Institutional_Naked_Pocs[idx].RthEndTime == rthEndTime && cacheEduS_Institutional_Naked_Pocs[idx].EqualsInput(input))
						return cacheEduS_Institutional_Naked_Pocs[idx];
			return CacheIndicator<EduS_Trader.EduS_Institutional_Naked_Pocs>(new EduS_Trader.EduS_Institutional_Naked_Pocs(){ DaysToLoad = daysToLoad, LineWidth = lineWidth, ShowLabels = showLabels, LabelFontSize = labelFontSize, LabelRightMargin = labelRightMargin, IgnoreOvernightTouches = ignoreOvernightTouches, RthStartTime = rthStartTime, RthEndTime = rthEndTime }, input, ref cacheEduS_Institutional_Naked_Pocs);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduS_Institutional_Naked_Pocs EduS_Institutional_Naked_Pocs(int daysToLoad, float lineWidth, bool showLabels, int labelFontSize, int labelRightMargin, bool ignoreOvernightTouches, int rthStartTime, int rthEndTime)
		{
			return indicator.EduS_Institutional_Naked_Pocs(Input, daysToLoad, lineWidth, showLabels, labelFontSize, labelRightMargin, ignoreOvernightTouches, rthStartTime, rthEndTime);
		}

		public Indicators.EduS_Trader.EduS_Institutional_Naked_Pocs EduS_Institutional_Naked_Pocs(ISeries<double> input , int daysToLoad, float lineWidth, bool showLabels, int labelFontSize, int labelRightMargin, bool ignoreOvernightTouches, int rthStartTime, int rthEndTime)
		{
			return indicator.EduS_Institutional_Naked_Pocs(input, daysToLoad, lineWidth, showLabels, labelFontSize, labelRightMargin, ignoreOvernightTouches, rthStartTime, rthEndTime);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduS_Institutional_Naked_Pocs EduS_Institutional_Naked_Pocs(int daysToLoad, float lineWidth, bool showLabels, int labelFontSize, int labelRightMargin, bool ignoreOvernightTouches, int rthStartTime, int rthEndTime)
		{
			return indicator.EduS_Institutional_Naked_Pocs(Input, daysToLoad, lineWidth, showLabels, labelFontSize, labelRightMargin, ignoreOvernightTouches, rthStartTime, rthEndTime);
		}

		public Indicators.EduS_Trader.EduS_Institutional_Naked_Pocs EduS_Institutional_Naked_Pocs(ISeries<double> input , int daysToLoad, float lineWidth, bool showLabels, int labelFontSize, int labelRightMargin, bool ignoreOvernightTouches, int rthStartTime, int rthEndTime)
		{
			return indicator.EduS_Institutional_Naked_Pocs(input, daysToLoad, lineWidth, showLabels, labelFontSize, labelRightMargin, ignoreOvernightTouches, rthStartTime, rthEndTime);
		}
	}
}

#endregion
