#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using SharpDX.DirectWrite;
#endregion

// Enums globales
public enum EduS_HistMode { Dinamico, Manual_Fijo }
public enum EduS_TextOrient { Horizontal, Vertical }
public enum EduS_TextPos { Arriba, Abajo }

namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader
{
    public class EduS_Unified_Market_Context : Indicator
    {
        // ========================================================
        // ESTRUCTURAS DE DATOS
        // ========================================================
        public class EduS_TimeEvent
        {
            public DateTime Time { get; set; }
            public string Label { get; set; }
            public double AnchorPrice { get; set; } 
        }

        private List<EduS_TimeEvent> globalEvents = new List<EduS_TimeEvent>();
        
        // HashSet para evitar duplicados en gráficas de Ticks
        private HashSet<string> processedEventsToday = new HashSet<string>();

        // ========================================================
        // VARIABLES DE ESTADO
        // ========================================================
        
        // Niveles Estructurales
        private double pApertura = double.NaN;
        private double yHigh = double.NaN;
        private double yLow = double.NaN;
        private double preHigh = double.NaN;
        private double preLow = double.NaN;
        private double pCierre = double.NaN;
        private double histMax = double.NaN;

        // Variables de Cálculo
        private double curRthHigh = double.NaN;
        private double curRthLow = double.NaN;
        private double tmpPreHigh = double.NaN;
        private double tmpPreLow = double.NaN;
        private double lastCompletedRthHigh = double.NaN;
        private double lastCompletedRthLow = double.NaN;
        private double tempPCierre = double.NaN;

        // ORB
        private double orb30High = double.NaN;
        private double orb30Low = double.NaN;
        private double orb30Mid = double.NaN;
        
        // Control
        private DateTime currentSessionDate = DateTime.MinValue;

        // Recursos Gráficos
        private CultureInfo labelCulture;
        private SharpDX.Direct2D1.Brush cachedTimeBrush;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "EduS_Unified_Market_Context";
                Description = "EdusTrader - Unificación de Niveles Estructurales y ORB (Opening Range Breakout) 30s en motor Direct2D" + 
                                "\n• PApertura: Precio de apertura RTH del día (09:30 por defecto)," +
                                "\n• yHigh: Máximo del RTH de Ayer (visible desde las 00:00)," +
                                "\n• yLow: Mínimo  del RTH de Ayer (visible desde las 00:00)," + 
                                "\n• PreHigh: Máximo del Pre‑Market (Dinámico 00:00 → 09:30)," +
                                "\n• PreLow: Mínimo del Pre‑Market (Dinámico 00:00 → 09:30)," +
                                "\n• PCierre: Cierre de Ayer a las 16:00 (visible desde las 00:00)," +
                                "\n• HistMax: Máximo histórico dentro de los datos cargados en el gráfico" +
                                "\n• ORB H: Máximo de la Ruptura del Rango de Apertura" +
                                "\n• ORB Mid: Media de la Ruptura del Rango de Apertura" +
                                "\n• ORB L: Mínimo de la Ruptura del Rango de Apertura" +
                                "\n•Marca los horarios:" +
                                "\n     3:00 (ET) Apertura Eurex" +
                                "\n     11:30 (ET) Cierre Londres" +
                                "\n     19:00 (ET) Apertura Asia" +
                                "\n     2:00 (ET) Cierre Asia" +
                                "\n     4:00 (ET) Apertura Londres";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = false; // Dibujamos TODO manualmente en OnRender
                PaintPriceMarkers = true;
                IsAutoScale = false;
                ScaleJustification = ScaleJustification.Right;

                // --- Parámetros por Defecto ---
                RthOpenTime = 93000;    
                RthCloseTime = 160000;  
                OrbSeconds = 30;        
                
                // --- Configuración HistMax ---
                HistMode = EduS_HistMode.Dinamico; 
                ManualHistMax = 0; 

                CultureName = "es-ES";
                Decimals = 2;
                LineWidth = 2;
                RightLabelPadding = 15;
                LabelAbove = 2;

                // --- Configuración Horarios Globales ---
                ShowGlobalTimes = true;
                GlobalTimeColor = Brushes.DimGray;
                GlobalTimeStyle = DashStyleHelper.Dash;
                GlobalTimeWidth = 1;
                GlobalTimeOpacity = 60;
                
                // --- Configuración Texto Horarios ---
                GlobalTimeFontSize = 10;
                GlobalTimeTextOrientation = EduS_TextOrient.Vertical;
                GlobalTimeTextPosition = EduS_TextPos.Arriba;
                GlobalTimeTextOffsetPoints = 5; // Puntos por defecto

                // --- Definición de Plots ---
                AddPlot(Brushes.DarkGreen, "PApertura");   // 0
                AddPlot(Brushes.DarkGreen, "yHigh");       // 1
                AddPlot(Brushes.DarkGreen, "PreHigh");     // 2
                AddPlot(Brushes.Red, "HistMax");           // 3
                AddPlot(Brushes.DarkGreen, "yLow");        // 4
                AddPlot(Brushes.DarkGreen, "PreLow");      // 5
                AddPlot(Brushes.DarkGreen, "PCierre");     // 6
                
                AddPlot(Brushes.Cyan, "ORB_High");         // 7
                AddPlot(Brushes.Magenta, "ORB_Low");       // 8
                AddPlot(Brushes.Gold, "ORB_Mid");          // 9

                for (int i = 0; i < Plots.Length; i++)
                {
                    if (i == 3) Plots[i].DashStyleHelper = DashStyleHelper.Dot;
                    else if (i >= 7) Plots[i].DashStyleHelper = DashStyleHelper.Solid; 
                    else Plots[i].DashStyleHelper = DashStyleHelper.DashDot;
                    
                    Plots[i].Width = LineWidth;
                }
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Second, OrbSeconds);
            }
            else if (State == State.DataLoaded)
            {
                labelCulture = new CultureInfo(CultureName);
                if (globalEvents == null) globalEvents = new List<EduS_TimeEvent>();
                else globalEvents.Clear();
                
                if (processedEventsToday == null) processedEventsToday = new HashSet<string>();
                else processedEventsToday.Clear();
            }
            else if (State == State.Historical)
            {
                if (globalEvents != null) globalEvents.Clear();
                if (processedEventsToday != null) processedEventsToday.Clear();
            }
            else if (State == State.Terminated)
            {
                if (cachedTimeBrush != null) { cachedTimeBrush.Dispose(); cachedTimeBrush = null; }
            }
        }

        protected override void OnBarUpdate()
        {
            // BIP 0: Serie Principal
            if (BarsInProgress == 0)
            {
                // Reset diario de memoria de eventos
                if (Bars.IsFirstBarOfSession)
                {
                     processedEventsToday.Clear();
                }
                
                // A) DETECCIÓN DE HORARIOS (CRONOBIOLOGÍA)
                if (ShowGlobalTimes && (IsFirstTickOfBar || Calculate == Calculate.OnBarClose))
                {
                    // Conversión a Eastern Time
                    DateTime barTime = Time[0];
                    TimeZoneInfo easternZone;
                    try { easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
                    catch { easternZone = TimeZoneInfo.Local; } 

                    DateTime easternTime = TimeZoneInfo.ConvertTime(barTime, Core.Globals.GeneralOptions.TimeZoneInfo, easternZone);
                    TimeSpan etTimeOfDay = easternTime.TimeOfDay;
                    string dateStr = easternTime.ToString("yyyyMMdd"); 

                    // Chequeo de eventos con Tolerancia de 5 mins
                    CheckAndAddEvent("EurexOpen", new TimeSpan(3, 0, 0), "3:00 (ET) Apertura Eurex", dateStr, etTimeOfDay, barTime);
                    CheckAndAddEvent("LondonClose", new TimeSpan(11, 30, 0), "11:30 (ET) Cierre Londres", dateStr, etTimeOfDay, barTime);
                    CheckAndAddEvent("AsiaOpen", new TimeSpan(19, 0, 0), "19:00 (ET) Apertura Asia", dateStr, etTimeOfDay, barTime);
                    CheckAndAddEvent("AsiaClose", new TimeSpan(2, 0, 0), "2:00 (ET) Cierre Asia", dateStr, etTimeOfDay, barTime);
                    CheckAndAddEvent("LondonOpen", new TimeSpan(4, 0, 0), "4:00 (ET) Apertura Londres", dateStr, etTimeOfDay, barTime);
                }

                // B) LÓGICA ESTRUCTURAL ORIGINAL
                if (CurrentBar == 0)
                {
                    if (HistMode == EduS_HistMode.Manual_Fijo) histMax = ManualHistMax;
                    else histMax = High[0];

                    if (ToTime(Time[0]) < RthOpenTime)
                    {
                        tmpPreHigh = High[0]; tmpPreLow = Low[0];
                        preHigh = tmpPreHigh; preLow = tmpPreLow;
                    }
                    SetAllPlots();
                    return;
                }

                int tPrev = ToTime(Time[1]);
                int tNow = ToTime(Time[0]);

                // 1. Max Histórico
                if (HistMode == EduS_HistMode.Dinamico) histMax = Math.Max(histMax, High[0]);
                else histMax = ManualHistMax;

                // 2. Cambio de Día
                if (Time[0].Date > Time[1].Date)
                {
                    if (!double.IsNaN(lastCompletedRthHigh)) yHigh = lastCompletedRthHigh;
                    if (!double.IsNaN(lastCompletedRthLow)) yLow = lastCompletedRthLow;
                    if (!double.IsNaN(tempPCierre)) pCierre = tempPCierre;

                    tmpPreHigh = High[0]; tmpPreLow = Low[0];
                    preHigh = tmpPreHigh; preLow = tmpPreLow;
                    
                    orb30High = double.NaN; orb30Low = double.NaN; orb30Mid = double.NaN;
                    currentSessionDate = Time[0].Date;
                }

                // 3. Pre-Market
                if (tNow < RthOpenTime)
                {
                    if (double.IsNaN(tmpPreHigh)) tmpPreHigh = High[0];
                    else tmpPreHigh = Math.Max(tmpPreHigh, High[0]);

                    if (double.IsNaN(tmpPreLow)) tmpPreLow = Low[0];
                    else tmpPreLow = Math.Min(tmpPreLow, Low[0]);

                    preHigh = tmpPreHigh; preLow = tmpPreLow;
                }

                // 4. Apertura RTH
                bool crossedToRthOpen = (tPrev < RthOpenTime && tNow >= RthOpenTime);
                if (crossedToRthOpen)
                {
                    pApertura = Open[0];
                    curRthHigh = High[0]; curRthLow = Low[0];
                }

                // 5. RTH
                if (tNow >= RthOpenTime && tNow < RthCloseTime)
                {
                    curRthHigh = double.IsNaN(curRthHigh) ? High[0] : Math.Max(curRthHigh, High[0]);
                    curRthLow = double.IsNaN(curRthLow) ? Low[0] : Math.Min(curRthLow, Low[0]);
                }

                // 6. Cierre RTH
                bool crossedToRthClose = (tPrev < RthCloseTime && tNow >= RthCloseTime);
                if (crossedToRthClose)
                {
                    tempPCierre = Close[Math.Min(1, CurrentBar)];
                    if (!double.IsNaN(curRthHigh)) lastCompletedRthHigh = curRthHigh;
                    if (!double.IsNaN(curRthLow)) lastCompletedRthLow = curRthLow;
                }

                SetAllPlots();
            }
            // BIP 1: Serie Secundaria (ORB)
            else if (BarsInProgress == 1)
            {
                DateTime tBar = Time[0];
                int tBarInt = ToTime(tBar);
                TimeSpan openTS = new TimeSpan(RthOpenTime / 10000, (RthOpenTime % 10000) / 100, RthOpenTime % 100);
                TimeSpan targetTS = openTS.Add(TimeSpan.FromSeconds(OrbSeconds));
                int targetTimeInt = targetTS.Hours * 10000 + targetTS.Minutes * 100 + targetTS.Seconds;

                if (tBar.Date == currentSessionDate && tBarInt == targetTimeInt)
                {
                    orb30High = High[0]; orb30Low = Low[0];
                    orb30Mid = Math.Round((orb30High + orb30Low) / 2.0 / TickSize) * TickSize;
                }
            }
        }

        private void CheckAndAddEvent(string eventId, TimeSpan targetTime, string label, string dateStr, TimeSpan currentEtTime, DateTime barTime)
        {
            string uniqueKey = eventId + "_" + dateStr;
            if (processedEventsToday.Contains(uniqueKey)) return;

            TimeSpan tolerance = TimeSpan.FromMinutes(5);

            if (currentEtTime >= targetTime && currentEtTime < targetTime.Add(tolerance))
            {
                double anchor = (GlobalTimeTextPosition == EduS_TextPos.Arriba) ? High[0] : Low[0];

                globalEvents.Add(new EduS_TimeEvent { 
                    Time = barTime, 
                    Label = label,
                    AnchorPrice = anchor 
                });

                processedEventsToday.Add(uniqueKey);
            }
        }

        private void SetAllPlots()
        {
            Values[0][0] = pApertura; Values[1][0] = yHigh; Values[2][0] = preHigh;
            Values[3][0] = histMax; Values[4][0] = yLow; Values[5][0] = preLow;
            Values[6][0] = pCierre; Values[7][0] = orb30High; Values[8][0] = orb30Low;
            Values[9][0] = orb30Mid;
        }

        // ==============================================================================
        // MOTOR GRÁFICO (DIRECT2D) - MASTER RENDER
        // ==============================================================================
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (ChartBars == null || chartControl == null || chartScale == null) return;

            // 1. RENDERIZADO DE HORARIOS GLOBALES
            if (ShowGlobalTimes && globalEvents != null && globalEvents.Count > 0)
            {
                if (cachedTimeBrush == null || cachedTimeBrush.IsDisposed)
                {
                    var dxBrush = GlobalTimeColor.ToDxBrush(RenderTarget);
                    dxBrush.Opacity = (float)(GlobalTimeOpacity / 100.0);
                    cachedTimeBrush = dxBrush;
                }
                
                SharpDX.DirectWrite.TextFormat timeTextFormat = new SharpDX.DirectWrite.TextFormat(
                    NinjaTrader.Core.Globals.DirectWriteFactory,
                    "Arial", 
                    SharpDX.DirectWrite.FontWeight.Bold, 
                    SharpDX.DirectWrite.FontStyle.Normal, 
                    (float)GlobalTimeFontSize);

                float yTop = (float)ChartPanel.Y;
                float yBottom = (float)(ChartPanel.Y + ChartPanel.H);

                foreach (var evt in globalEvents)
                {
                    // Obtener índice de barra
                    int idx = ChartBars.GetBarIdxByTime(chartControl, evt.Time);
                    
                    // [CORRECCION DEL ERROR AQUI]
                    // Usamos ChartBars.FromIndex y ToIndex en lugar de chartControl.FirstIndex
                    if (idx < ChartBars.FromIndex || idx > ChartBars.ToIndex) continue;

                    float x = (float)chartControl.GetXByBarIndex(ChartBars, idx);

                    // A) Dibujar Línea Vertical
                    RenderTarget.DrawLine(new Vector2(x, yTop), new Vector2(x, yBottom), cachedTimeBrush, GlobalTimeWidth);

                    // B) Dibujar Texto (POSICIONAMIENTO DINÁMICO)
                    string txt = evt.Label;
                    using (var layout = new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, txt, timeTextFormat, 300f, timeTextFormat.FontSize))
                    {
                        double priceBase = evt.AnchorPrice;
                        double finalPrice = 0;

                        if (GlobalTimeTextPosition == EduS_TextPos.Arriba)
                            finalPrice = priceBase + (GlobalTimeTextOffsetPoints * TickSize);
                        else
                            finalPrice = priceBase - (GlobalTimeTextOffsetPoints * TickSize);

                        float yPrice = (float)chartScale.GetYByValue(finalPrice);

                        if (GlobalTimeTextPosition == EduS_TextPos.Abajo && GlobalTimeTextOrientation == EduS_TextOrient.Horizontal)
                        {
                            yPrice = yPrice - layout.Metrics.Height;
                        }
                        
                        float txtX = x + 3; 
                        float txtY = yPrice; 

                        if (GlobalTimeTextOrientation == EduS_TextOrient.Horizontal)
                        {
                             RenderTarget.DrawTextLayout(new Vector2(txtX, txtY), layout, cachedTimeBrush);
                        }
                        else
                        {
                            var oldTransform = RenderTarget.Transform;
                            float pivotY = txtY;
                            
                            if(GlobalTimeTextPosition == EduS_TextPos.Abajo) pivotY += 2; 
                            else pivotY -= 2;

                            RenderTarget.Transform = Matrix3x2.Rotation((float)(-Math.PI / 2), new Vector2(x, pivotY)) * oldTransform;
                            
                            if (GlobalTimeTextPosition == EduS_TextPos.Arriba)
                                RenderTarget.DrawTextLayout(new Vector2(x, pivotY), layout, cachedTimeBrush);
                            else
                                RenderTarget.DrawTextLayout(new Vector2(x - layout.Metrics.Width, pivotY), layout, cachedTimeBrush);

                            RenderTarget.Transform = oldTransform;
                        }
                    }
                }
                timeTextFormat.Dispose();
            }

            // 2. RENDERIZADO DE NIVELES ESTRUCTURALES
            var textFormat = chartControl.Properties.LabelFont.ToDirectWriteTextFormat();

            (double val, string name, int plotIdx)[] levels = new (double, string, int)[]
            {
                (pApertura, "Open RTH", 0), (yHigh, "Y-High", 1), (preHigh, "Pre-High", 2),
                (histMax, "HistMax", 3), (yLow, "Y-Low", 4), (preLow, "Pre-Low", 5),
                (pCierre, "Y-Close", 6), (orb30High, "ORB H", 7), (orb30Low, "ORB L", 8),
                (orb30Mid, "ORB Mid", 9)
            };

            float xLeft = (float)chartControl.CanvasLeft + 2f;
            float xRight = (float)chartControl.CanvasRight;
            float panelRight = (float)(ChartPanel.X + ChartPanel.W);

            for (int i = 0; i < levels.Length; i++)
            {
                double v = levels[i].val;
                if (double.IsNaN(v) || Math.Abs(v) < double.Epsilon) continue;

                float y = (float)chartScale.GetYByValue(v);
                var plot = Plots[levels[i].plotIdx];
                
                var p0 = new Vector2(xLeft, y);
                var p1 = new Vector2(xRight, y);
                RenderTarget.DrawLine(p0, p1, plot.BrushDX, plot.Width, plot.StrokeStyle);

                string formatted = v.ToString("N" + Decimals, labelCulture);
                string label = $"{levels[i].name} ({formatted})";

                using (var layout = new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, label, textFormat, ChartPanel.W, textFormat.FontSize))
                {
                    float xLabel = panelRight - RightLabelPadding - layout.Metrics.Width;
                    float yText = y - layout.Metrics.Height - LabelAbove;
                    RenderTarget.DrawTextLayout(new Vector2(xLabel, yText), layout, plot.BrushDX);
                }
            }
            textFormat.Dispose();
        }
        
        public override void OnRenderTargetChanged()
        {
            if (cachedTimeBrush != null) { cachedTimeBrush.Dispose(); cachedTimeBrush = null; }
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Inicio RTH (HHmmss)", GroupName = "1. Horarios", Order = 0)]
        public int RthOpenTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cierre RTH (HHmmss)", GroupName = "1. Horarios", Order = 1)]
        public int RthCloseTime { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Segundos ORB", Description="Duración para cálculo inicial (ej: 30)", GroupName = "1. Horarios", Order = 2)]
        public int OrbSeconds { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Modo Max Histórico", Description="Dinamico: Basado en velas cargadas\nManual_Fijo: Usa el valor ingresado abajo", GroupName = "3. Niveles Históricos", Order = 0)]
        public EduS_HistMode HistMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Precio Max Manual", Description="Valor fijo si Modo = Manual_Fijo", GroupName = "3. Niveles Históricos", Order = 1)]
        public double ManualHistMax { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cultura (Formato)", GroupName = "2. Visual", Order = 3)]
        public string CultureName { get; set; }

        [NinjaScriptProperty]
        [Range(0, 8)]
        [Display(Name = "Decimales", GroupName = "2. Visual", Order = 4)]
        public int Decimals { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Grosor Línea", GroupName = "2. Visual", Order = 5)]
        public int LineWidth { get; set; }
        
        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Margen Etiqueta (Der)", GroupName = "2. Visual", Order = 6)]
        public int RightLabelPadding { get; set; }
        
        [NinjaScriptProperty]
        [Range(-50, 50)]
        [Display(Name = "Altura Texto (Offset)", GroupName = "2. Visual", Order = 7)]
        public int LabelAbove { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Horarios Globales", Description = "Activa líneas para Eurex, Londres y Asia", Order = 1, GroupName = "4. Horarios Globales")]
        public bool ShowGlobalTimes { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Color Líneas Globales", Description = "Color de las líneas y texto", Order = 2, GroupName = "4. Horarios Globales")]
        public Brush GlobalTimeColor { get; set; }

        [Browsable(false)]
        public string GlobalTimeColorSerializable
        {
            get { return Serialize.BrushToString(GlobalTimeColor); }
            set { GlobalTimeColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Display(Name = "Estilo de Línea", Description = "Estilo (Sólido, Punteado, etc.)", Order = 3, GroupName = "4. Horarios Globales")]
        public DashStyleHelper GlobalTimeStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Grosor de Línea", Description = "Ancho en píxeles", Order = 4, GroupName = "4. Horarios Globales")]
        public int GlobalTimeWidth { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Opacidad (%)", Description = "Transparencia (0-100) para Línea y Texto", Order = 5, GroupName = "4. Horarios Globales")]
        public int GlobalTimeOpacity { get; set; }

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "Tamaño Fuente", Description = "Tamaño del texto de los horarios", Order = 6, GroupName = "4. Horarios Globales")]
        public int GlobalTimeFontSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Orientación Texto", Description = "Horizontal o Vertical", Order = 7, GroupName = "4. Horarios Globales")]
        public EduS_TextOrient GlobalTimeTextOrientation { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Posición Texto", Description = "Arriba o Abajo del panel", Order = 8, GroupName = "4. Horarios Globales")]
        public EduS_TextPos GlobalTimeTextPosition { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Offset (Puntos)", Description = "Distancia en PUNTOS (Ticks) desde el precio", Order = 9, GroupName = "4. Horarios Globales")]
        public double GlobalTimeTextOffsetPoints { get; set; }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduS_Unified_Market_Context[] cacheEduS_Unified_Market_Context;
		public EduS_Trader.EduS_Unified_Market_Context EduS_Unified_Market_Context(int rthOpenTime, int rthCloseTime, int orbSeconds, EduS_HistMode histMode, double manualHistMax, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints)
		{
			return EduS_Unified_Market_Context(Input, rthOpenTime, rthCloseTime, orbSeconds, histMode, manualHistMax, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints);
		}

		public EduS_Trader.EduS_Unified_Market_Context EduS_Unified_Market_Context(ISeries<double> input, int rthOpenTime, int rthCloseTime, int orbSeconds, EduS_HistMode histMode, double manualHistMax, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints)
		{
			if (cacheEduS_Unified_Market_Context != null)
				for (int idx = 0; idx < cacheEduS_Unified_Market_Context.Length; idx++)
					if (cacheEduS_Unified_Market_Context[idx] != null && cacheEduS_Unified_Market_Context[idx].RthOpenTime == rthOpenTime && cacheEduS_Unified_Market_Context[idx].RthCloseTime == rthCloseTime && cacheEduS_Unified_Market_Context[idx].OrbSeconds == orbSeconds && cacheEduS_Unified_Market_Context[idx].HistMode == histMode && cacheEduS_Unified_Market_Context[idx].ManualHistMax == manualHistMax && cacheEduS_Unified_Market_Context[idx].CultureName == cultureName && cacheEduS_Unified_Market_Context[idx].Decimals == decimals && cacheEduS_Unified_Market_Context[idx].LineWidth == lineWidth && cacheEduS_Unified_Market_Context[idx].RightLabelPadding == rightLabelPadding && cacheEduS_Unified_Market_Context[idx].LabelAbove == labelAbove && cacheEduS_Unified_Market_Context[idx].ShowGlobalTimes == showGlobalTimes && cacheEduS_Unified_Market_Context[idx].GlobalTimeColor == globalTimeColor && cacheEduS_Unified_Market_Context[idx].GlobalTimeStyle == globalTimeStyle && cacheEduS_Unified_Market_Context[idx].GlobalTimeWidth == globalTimeWidth && cacheEduS_Unified_Market_Context[idx].GlobalTimeOpacity == globalTimeOpacity && cacheEduS_Unified_Market_Context[idx].GlobalTimeFontSize == globalTimeFontSize && cacheEduS_Unified_Market_Context[idx].GlobalTimeTextOrientation == globalTimeTextOrientation && cacheEduS_Unified_Market_Context[idx].GlobalTimeTextPosition == globalTimeTextPosition && cacheEduS_Unified_Market_Context[idx].GlobalTimeTextOffsetPoints == globalTimeTextOffsetPoints && cacheEduS_Unified_Market_Context[idx].EqualsInput(input))
						return cacheEduS_Unified_Market_Context[idx];
			return CacheIndicator<EduS_Trader.EduS_Unified_Market_Context>(new EduS_Trader.EduS_Unified_Market_Context(){ RthOpenTime = rthOpenTime, RthCloseTime = rthCloseTime, OrbSeconds = orbSeconds, HistMode = histMode, ManualHistMax = manualHistMax, CultureName = cultureName, Decimals = decimals, LineWidth = lineWidth, RightLabelPadding = rightLabelPadding, LabelAbove = labelAbove, ShowGlobalTimes = showGlobalTimes, GlobalTimeColor = globalTimeColor, GlobalTimeStyle = globalTimeStyle, GlobalTimeWidth = globalTimeWidth, GlobalTimeOpacity = globalTimeOpacity, GlobalTimeFontSize = globalTimeFontSize, GlobalTimeTextOrientation = globalTimeTextOrientation, GlobalTimeTextPosition = globalTimeTextPosition, GlobalTimeTextOffsetPoints = globalTimeTextOffsetPoints }, input, ref cacheEduS_Unified_Market_Context);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduS_Unified_Market_Context EduS_Unified_Market_Context(int rthOpenTime, int rthCloseTime, int orbSeconds, EduS_HistMode histMode, double manualHistMax, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints)
		{
			return indicator.EduS_Unified_Market_Context(Input, rthOpenTime, rthCloseTime, orbSeconds, histMode, manualHistMax, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints);
		}

		public Indicators.EduS_Trader.EduS_Unified_Market_Context EduS_Unified_Market_Context(ISeries<double> input , int rthOpenTime, int rthCloseTime, int orbSeconds, EduS_HistMode histMode, double manualHistMax, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints)
		{
			return indicator.EduS_Unified_Market_Context(input, rthOpenTime, rthCloseTime, orbSeconds, histMode, manualHistMax, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduS_Unified_Market_Context EduS_Unified_Market_Context(int rthOpenTime, int rthCloseTime, int orbSeconds, EduS_HistMode histMode, double manualHistMax, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints)
		{
			return indicator.EduS_Unified_Market_Context(Input, rthOpenTime, rthCloseTime, orbSeconds, histMode, manualHistMax, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints);
		}

		public Indicators.EduS_Trader.EduS_Unified_Market_Context EduS_Unified_Market_Context(ISeries<double> input , int rthOpenTime, int rthCloseTime, int orbSeconds, EduS_HistMode histMode, double manualHistMax, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints)
		{
			return indicator.EduS_Unified_Market_Context(input, rthOpenTime, rthCloseTime, orbSeconds, histMode, manualHistMax, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints);
		}
	}
}

#endregion
