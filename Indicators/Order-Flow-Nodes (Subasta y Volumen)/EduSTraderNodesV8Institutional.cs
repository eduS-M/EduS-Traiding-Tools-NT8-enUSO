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
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Gui.Chart;

using SDX = SharpDX;
using D2D1 = SharpDX.Direct2D1;
using DW   = SharpDX.DirectWrite;
#endregion

// =========================================================
// 1. ENUMS GLOBALES
// =========================================================
public enum EduS_DistMode_Institutional { Full, Close, OHLC }
public enum EduS_LegendPos_Institutional { TopLeft, TopRight, BottomLeft, BottomRight }
// NUEVO: Enum para las Formas Visuales
public enum EduS_NodeType { POC, AntiPOC, Structural }

namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader    
{
    // =========================================================
    // 2. CLASES DE DATOS
    // =========================================================
    public class EduS_Node_Institutional
    {
        public int BarIndex;
        public double Price;
        public string LabelText;
        public int ColorType; 
        public double DeltaValue; 
        // NUEVO: Tipo de forma a dibujar
        public EduS_NodeType Type;
    }

    public class EduS_Line_Institutional
    {
        public int StartBar;
        public double StartPrice;
        public int EndBar;
        public double EndPrice;
        public int ColorType;
    }

    public class EduS_NakedNode_Institutional
    {
        public double Price;
        public int StartBarIndex;
        public double Volume;
        public bool IsPOC; 
    }

    // =========================================================
    // 3. CLASE PRINCIPAL (LOGICA ORIGINAL + MEJORAS VISUALES)
    // =========================================================
    public class EduS_Trader_Nodes_V8_Institutional : Indicator
    {
        #region Propiedades (Inputs)

        [NinjaScriptProperty, Range(1, 365)]
        [Display(Name = "Días a Mostrar", Order = 1, GroupName = "1. Config General")]
        public int DaysToDisplay { get; set; } = 2;

			//Inicio: Cambio 09-01-2025
		
//        [NinjaScriptProperty, Range(1, 2000)]
//        [Display(Name = "Barras Ventana", Order = 2, GroupName = "2. Cálculo Base")]
//        public int WindowBars { get; set; } = 200;
		
		
		// NUEVO: Definimos minutos para la ventana
		[NinjaScriptProperty, Range(1, 240)]
		[Display(Name = "Minutos Ventana", Order = 2, GroupName = "2. Cálculo")]
		public int WindowMinutes { get; set; } = 30;

			//Fin: Cambio 09-01-2025

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "Intervalo Barras (Calc)", Order = 3, GroupName = "2. Cálculo Base")]
        public int BarsInterval { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name = "Modo Distribución (RT)", Description="Modo para Tiempo Real. En histórico se fuerza OHLC para velocidad.", Order = 4, GroupName = "2. Cálculo Base")]
        public EduS_DistMode_Institutional DistributionMode { get; set; } = EduS_DistMode_Institutional.Full;
        
        [NinjaScriptProperty, Range(0, 1500)]
        [Display(Name = "Ticks Unión (Merge)", Order = 5, GroupName = "2. Cálculo Base")]
        public int ZoneMergeTicks { get; set; } = 1000;

        // --- DINÁMICA ATR ---
        [NinjaScriptProperty]
        [Display(Name = "Usar Fuerza Dinámica (ATR)", Order = 6, GroupName = "3. Dinámica (ATR)")]
        public bool UseDynamicStrength { get; set; } = true;

        [NinjaScriptProperty, Range(1, 50)]
        [Display(Name = "Periodo ATR", Order = 7, GroupName = "3. Dinámica (ATR)")]
        public int ATR_Period { get; set; } = 14;

        [NinjaScriptProperty, Range(0.1, 10.0)]
        [Display(Name = "Factor Multiplicador ATR", Order = 8, GroupName = "3. Dinámica (ATR)")]
        public double ATR_Factor { get; set; } = 1.5;

        [NinjaScriptProperty, Range(1, 150)]
        [Display(Name = "Fuerza Fija (Si ATR=Falso)", Order = 9, GroupName = "3. Dinámica (ATR)")]
        public int NodeStrength { get; set; } = 40;
        
        // --- DELTA ---
        [NinjaScriptProperty]
        [Display(Name = "Usar Color Delta en Nodos", Order = 10, GroupName = "4. Order Flow (Delta)")]
        public bool UseDeltaColoring { get; set; } = true;

        // --- NAKED POCS ---
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Naked POCs", Order = 11, GroupName = "5. Naked Levels")]
        public bool ShowNakedPOCs { get; set; } = false;
        
        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Grosor Línea Naked", Order = 12, GroupName = "5. Naked Levels")]
        public float NakedWidth { get; set; } = 1.5f;

        [NinjaScriptProperty]
        [Display(Name = "Ignorar Toques Overnight", Description="Si es TRUE, el nivel NO se borra si es tocado fuera del horario RTH", Order = 13, GroupName = "5. Naked Levels")]
        public bool IgnoreOvernightTouches { get; set; } = false;

        [NinjaScriptProperty]
        [Display(Name = "Fin RTH (HHmm)", Description="Hora fin para considerar el toque válido como RTH (ej: 1600)", Order = 14, GroupName = "5. Naked Levels")]
        public int RthEndTime { get; set; } = 1600;

        // Lógica Sesión
        [NinjaScriptProperty] [Display(Name = "Habilitar Ventana", Order = 15, GroupName = "6. Lógica")] public bool EnableWindow { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Habilitar Sesión", Order = 16, GroupName = "6. Lógica")] public bool EnableSession { get; set; } = false;
        [NinjaScriptProperty] [Display(Name = "Calc POC/AntiPOC", Order = 17, GroupName = "6. Lógica")] public bool EnableAbsoluteNodes { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Calc Estructurales", Order = 18, GroupName = "6. Lógica")] public bool EnableStructuralLVN { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Hora Inicio (HHmm)", Order = 19, GroupName = "6. Lógica")] public int SessionStartTime { get; set; } = 930;
        [NinjaScriptProperty] [Display(Name = "Auto-Reset Sesión", Order = 20, GroupName = "6. Lógica")] public bool UseSessionAutoReset { get; set; } = true;

        // Visual
//        [NinjaScriptProperty] [Display(Name = "Ver Anti-POC Ventana", Order = 21, GroupName = "7. Visual")] public bool ShowAntiPOCWindow { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Ver POC Ventana", Order = 22, GroupName = "7. Visual")] public bool ShowPOCWindow { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Ver LVN Estruc Win", Order = 23, GroupName = "7. Visual")] public bool ShowLVNStructWindow { get; set; } = true;
//        [NinjaScriptProperty] [Display(Name = "Ver Anti-POC Sesión", Order = 24, GroupName = "7. Visual")] public bool ShowAntiPOCSession { get; set; } = false;
        [NinjaScriptProperty] [Display(Name = "Ver POC Sesión", Order = 25, GroupName = "7. Visual")] public bool ShowPOCSession { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Ver LVN Estruc Ses", Order = 26, GroupName = "7. Visual")] public bool ShowLVNStructSession { get; set; } = false;
		
		// Inicio: 11/01/2026
				
		// Para Ventana
		[NinjaScriptProperty]
		[Display(Name = "Ver Anti-POC Superior (Ventana)", Order = 40, GroupName = "7. Visual")]
		public bool ShowAntiPOCUpperWindow { get; set; } = true;
		
		[NinjaScriptProperty]
		[Display(Name = "Ver Anti-POC Inferior (Ventana)", Order = 41, GroupName = "7. Visual")]
		public bool ShowAntiPOCLowerWindow { get; set; } = true;
		
		// Para Sesión
		[NinjaScriptProperty]
		[Display(Name = "Ver Anti-POC Superior (Sesión)", Order = 42, GroupName = "7. Visual")]
		public bool ShowAntiPOCUpperSession { get; set; } = false;
		
		[NinjaScriptProperty]
		[Display(Name = "Ver Anti-POC Inferior (Sesión)", Order = 43, GroupName = "7. Visual")]
		public bool ShowAntiPOCLowerSession { get; set; } = false;

		// FIN: 11/01/2026
		
		
        [NinjaScriptProperty] [Display(Name = "Conectar líneas", Order = 27, GroupName = "7. Visual")] public bool ConnectWithLine { get; set; } = true;
        
        // --- VISUAL: ETIQUETAS ---
        [NinjaScriptProperty] [Display(Name = "Mostrar Etiquetas (Global)", Order = 28, GroupName = "7. Visual")] public bool ShowLabels { get; set; } = false;
        [NinjaScriptProperty] [Display(Name = "Mostrar SOLO Última Etiqueta", Description="Si Global está OFF, fuerza mostrar la última etiqueta.", Order = 28, GroupName = "7. Visual")] public bool ShowOnlyLastLabel { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Offset X Etiqueta", Order = 29, GroupName = "7. Visual")] public int LabelOffsetX { get; set; } = 15;
        [NinjaScriptProperty] [Display(Name = "Offset Y Etiqueta", Order = 29, GroupName = "7. Visual")] public int LabelOffsetY { get; set; } = -5;
        [NinjaScriptProperty] [Display(Name = "Tamaño Texto", Order = 30, GroupName = "7. Visual")] public int LabelSize { get; set; } = 8;
        [NinjaScriptProperty] [Display(Name = "Fuente Texto", Order = 31, GroupName = "7. Visual")] public string LabelFontFamily { get; set; } = "Arial";
        [NinjaScriptProperty] [Display(Name = "Radio / Tamaño (px)", Order = 32, GroupName = "7. Visual")] public float DotRadius { get; set; } = 2f;

		
        // Leyenda
        [NinjaScriptProperty] [Display(Name = "Ver Leyenda", Order = 33, GroupName = "9. Leyenda")] public bool ShowLegend { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Mostrar Fondo Leyenda", Order = 34, GroupName = "9. Leyenda")] public bool ShowLegendBackground { get; set; } = true; 
        [NinjaScriptProperty] [Display(Name = "Posición", Order = 35, GroupName = "9. Leyenda")] public EduS_LegendPos_Institutional LegendPosition { get; set; } = EduS_LegendPos_Institutional.TopLeft;
        
        [XmlIgnore] [Display(Name = "Fondo Leyenda", Order = 36, GroupName = "9. Leyenda")] 
        public Brush LegendBackground { get; set; } = Brushes.White; 
        
        [Range(0, 100)] [Display(Name = "Opacidad Fondo %", Order = 37, GroupName = "9. Leyenda")] 
        public int LegendOpacity { get; set; } = 40; 
		
		//Inicio: 09-01-2026
		
		[NinjaScriptProperty]
		[Range(0, 500)]
		[Display(Name = "Margen Horizontal Leyenda", Order = 38, GroupName = "9. Leyenda")]
		public int LegendMarginX { get; set; } = 150;
		
		[NinjaScriptProperty]
		[Range(0, 500)]
		[Display(Name = "Margen Vertical Leyenda", Order = 39, GroupName = "9. Leyenda")]
		public int LegendMarginY { get; set; } = 30;

		//Fin:09-01-2026
        
        [Browsable(false)] public string LegendBackgroundSerializable { get => SerializeBrush(LegendBackground); set => LegendBackground = DeserializeBrush(value); }

        // Colores
        [XmlIgnore] [Display(Name = "Color Anti-POC (W)", Order = 38, GroupName = "8. Colores")] public Brush ColorLVN_Window { get; set; } = Brushes.LimeGreen;
        [XmlIgnore] [Display(Name = "Color POC (W)", Order = 39, GroupName = "8. Colores")]      public Brush ColorHVN_Window { get; set; } = Brushes.OrangeRed;
        [XmlIgnore] [Display(Name = "Color Estruc (W)", Order = 40, GroupName = "8. Colores")] public Brush ColorLVN_Struct_W { get; set; } = Brushes.CornflowerBlue;
        [XmlIgnore] [Display(Name = "Color Anti-POC (S)", Order = 41, GroupName = "8. Colores")] public Brush ColorLVN_Session { get; set; } = Brushes.DarkGreen;
        [XmlIgnore] [Display(Name = "Color POC (S)", Order = 42, GroupName = "8. Colores")]      public Brush ColorHVN_Session { get; set; } = Brushes.DarkRed;
        [XmlIgnore] [Display(Name = "Color Estruc (S)", Order = 43, GroupName = "8. Colores")] public Brush ColorLVN_Struct_S { get; set; } = Brushes.DarkBlue;
        [XmlIgnore] [Display(Name = "Color Delta Positivo", Order = 44, GroupName = "8. Colores")] public Brush ColorDeltaPos { get; set; } = Brushes.Cyan;
        [XmlIgnore] [Display(Name = "Color Delta Negativo", Order = 45, GroupName = "8. Colores")] public Brush ColorDeltaNeg { get; set; } = Brushes.Magenta;
        [XmlIgnore] [Display(Name = "Color Naked POC", Order = 46, GroupName = "8. Colores")] public Brush ColorNakedPOC { get; set; } = Brushes.Gold;

        [Browsable(false)] public string ColorLVN_WindowSerializable { get => SerializeBrush(ColorLVN_Window); set => ColorLVN_Window = DeserializeBrush(value); }
        [Browsable(false)] public string ColorHVN_WindowSerializable { get => SerializeBrush(ColorHVN_Window); set => ColorHVN_Window = DeserializeBrush(value); }
        [Browsable(false)] public string ColorLVN_Struct_WSerializable { get => SerializeBrush(ColorLVN_Struct_W); set => ColorLVN_Struct_W = DeserializeBrush(value); }
        [Browsable(false)] public string ColorLVN_SessionSerializable { get => SerializeBrush(ColorLVN_Session); set => ColorLVN_Session = DeserializeBrush(value); }
        [Browsable(false)] public string ColorHVN_SessionSerializable { get => SerializeBrush(ColorHVN_Session); set => ColorHVN_Session = DeserializeBrush(value); }
        [Browsable(false)] public string ColorLVN_Struct_SSerializable { get => SerializeBrush(ColorLVN_Struct_S); set => ColorLVN_Struct_S = DeserializeBrush(value); }
        [Browsable(false)] public string ColorDeltaPosSerializable { get => SerializeBrush(ColorDeltaPos); set => ColorDeltaPos = DeserializeBrush(value); }
        [Browsable(false)] public string ColorDeltaNegSerializable { get => SerializeBrush(ColorDeltaNeg); set => ColorDeltaNeg = DeserializeBrush(value); }
        [Browsable(false)] public string ColorNakedPOCSerializable { get => SerializeBrush(ColorNakedPOC); set => ColorNakedPOC = DeserializeBrush(value); }

        #endregion

        #region Variables Internas
		
		//
		
		private int WindowBars; // Se recalcula dinámicamente
		

		//
		
        private List<EduS_Node_Institutional> nodesToDraw;
        private List<EduS_Line_Institutional> linesToDraw;
        private List<EduS_NakedNode_Institutional> nakedNodes;
        private object drawLock = new object();

        private Dictionary<double, double> vpWindow;
        private Dictionary<double, double> vpWindowDelta;
        private Dictionary<double, double> vpSession;
        private Dictionary<double, double> vpSessionDelta;
        
        private Queue<Dictionary<double, double>> fifoWindowData;
        private Queue<Dictionary<double, double>> fifoWindowDelta;
        
        // Optimización memoria etiquetas
        private Dictionary<string, int> lastLabelIndices = new Dictionary<string, int>();

        private bool sessionStartedToday = false;
        private DateTime cutoffDate;
        private double tickSize;

        private int lastBar_LVN_W = -1, lastBar_HVN_W = -1;
        private double lastPrice_LVN_W = double.NaN, lastPrice_HVN_W = double.NaN;
        private int lastBar_LVN_S = -1, lastBar_HVN_S = -1;
        private double lastPrice_LVN_S = double.NaN, lastPrice_HVN_S = double.NaN;

        private double leg_LVN_W, leg_HVN_W, leg_Str_W, leg_LVN_S, leg_HVN_S, leg_Str_S;
        private ATR atrAlgo;
        private int currentDynamicStrength; 

		//inicio:11/01/2026
				
		// Nuevos niveles Anti-POC para Ventana y Sesión
		private double lvnLowerW = double.NaN;
		private double lvnUpperW = double.NaN;
		private double lvnLowerS = double.NaN;
		private double lvnUpperS = double.NaN;
		//Fin: 11/01/2026
		
        // DX Resources
        private D2D1.SolidColorBrush b_LVN_W, b_HVN_W, b_Str_W, b_LVN_S, b_HVN_S, b_Str_S;
        private D2D1.SolidColorBrush b_DeltaPos, b_DeltaNeg, b_Naked;
        private D2D1.SolidColorBrush b_Text, b_LegendBg, b_LegendBorder;
        private D2D1.StrokeStyle strokeNaked;
        // NOTA: Eliminamos strokeSolid para evitar crash si no se inicializa. Usaremos default.
        private DW.TextFormat txtFormatNodes, txtFormatLegend;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "EduS_Trader_Nodes_V8_Institutional";
                Description = "Autor: EduS Trader — POC/Anti-POC + LVN Estructural + Estructura Dinámica.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                IsSuspendedWhileInactive = true;
                
                leg_LVN_W = leg_HVN_W = leg_Str_W = leg_LVN_S = leg_HVN_S = leg_Str_S = double.NaN;
            }
            else if (State == State.Configure)
            {
                nodesToDraw = new List<EduS_Node_Institutional>();
                linesToDraw = new List<EduS_Line_Institutional>();
                nakedNodes  = new List<EduS_NakedNode_Institutional>();
            }
            else if (State == State.DataLoaded)
            {
                tickSize = (Instrument != null) ? Instrument.MasterInstrument.TickSize : 0.25;

                vpWindow = new Dictionary<double, double>();
                vpWindowDelta = new Dictionary<double, double>();
                
                vpSession = new Dictionary<double, double>();
                vpSessionDelta = new Dictionary<double, double>();
                
                fifoWindowData = new Queue<Dictionary<double, double>>();
                fifoWindowDelta = new Queue<Dictionary<double, double>>();
				
			                
                if (UseDynamicStrength) atrAlgo = ATR(ATR_Period);

                lock (drawLock)
                {
                    nodesToDraw.Clear();
                    linesToDraw.Clear();
                    nakedNodes.Clear();
                }

                cutoffDate = DateTime.Now.Date.AddDays(-DaysToDisplay);
				
				// NUEVO: Ajustamos WindowBars según minutos definidos
    			AjustarWindowBarsPorTiempo();
				
            }
            else if (State == State.Terminated)
            {
                DisposeDXResources();
            }
        }

        public override string DisplayName => Name;

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Math.Max(ATR_Period, 2) || tickSize <= 0) return;
            if (Time[0].Date < cutoffDate) return;

            if (UseDynamicStrength && atrAlgo != null) atrAlgo.Update();

			
		// AQUÍ INSERTAS LA LLAMADA OPCIONAL
		    if (CurrentBar % 50 == 0) // cada 50 barras
		        AjustarWindowBarsPorTiempo();

			
            bool isNewDay = false;
            int currentHHMM = Time[0].Hour * 100 + Time[0].Minute; 

            if (CurrentBar > 0) isNewDay = (Time[0].Date > Time[1].Date);

            if (EnableSession)
            {
                if (isNewDay)
                {
                    StoreNakedPOC();       
                    vpSession.Clear();     
                    vpSessionDelta.Clear();
                    ResetSessionTrackers();
                    sessionStartedToday = true;
                }
                else if (UseSessionAutoReset)
                {
                    int prevHHMM = Time[1].Hour * 100 + Time[1].Minute;
                    if (!sessionStartedToday && prevHHMM < SessionStartTime && currentHHMM >= SessionStartTime)
                    {
                        vpSession.Clear(); 
                        vpSessionDelta.Clear();
                        ResetSessionTrackers();
                        sessionStartedToday = true;
                    }
                }
            }

            double vol = Math.Max(Volume[0], 1);
            double delta = (Close[0] > Open[0]) ? vol : (Close[0] < Open[0] ? -vol : 0);

            bool isHistorical = (State == State.Historical);
            ProcessVolumeOptimized(vol, delta, High[0], Low[0], Close[0], Open[0], isHistorical);

            if (ShowNakedPOCs && nakedNodes.Count > 0)
            {
                double h = High[0];
                double l = Low[0];
                bool isRTH = (currentHHMM >= SessionStartTime && currentHHMM < RthEndTime);

                lock(drawLock)
                {
                    for (int i = nakedNodes.Count - 1; i >= 0; i--)
                    {
                        var np = nakedNodes[i];
                        if (h >= np.Price && l <= np.Price)
                        {
                            if (IgnoreOvernightTouches && !isRTH) continue;
                            nakedNodes.RemoveAt(i);
                        }
                    }
                }
            }

            if ((CurrentBar % BarsInterval) != 0) return;

            int calcStrength = NodeStrength;
            if (UseDynamicStrength && atrAlgo != null)
            {
                double atrValue = atrAlgo[0];
                if (atrValue > 0)
                {
                    calcStrength = (int)Math.Round((atrValue * ATR_Factor) / tickSize);
                    calcStrength = Math.Max(2, Math.Min(calcStrength, 100));
                }
            }
            currentDynamicStrength = calcStrength;

            lock (drawLock)
            {
                // VENTANA
                if (EnableWindow && vpWindow.Count > 0)
                {
                    if (EnableAbsoluteNodes)
                    {
                        //Inicio: 11/01/2026
//						FindAbsoluteNodes(vpWindow, out double lvn, out double hvn);
//                        leg_LVN_W = ShowAntiPOCWindow ? lvn : double.NaN;
//                        leg_HVN_W = ShowPOCWindow ? hvn : double.NaN;
                        
//                        double dL = GetDeltaForPrice(vpWindowDelta, lvn);
//                        double dH = GetDeltaForPrice(vpWindowDelta, hvn);
                        
//                        // Pasamos el TIPO
//                        if (ShowAntiPOCWindow) AddTrackedNode(CurrentBar, lvn, "AP", 0, dL, EduS_NodeType.AntiPOC, ref lastBar_LVN_W, ref lastPrice_LVN_W);
//                        if (ShowPOCWindow)     AddTrackedNode(CurrentBar, hvn, "POC", 1, dH, EduS_NodeType.POC, ref lastBar_HVN_W, ref lastPrice_HVN_W);
                    
						
						
						FindExtendedNodes(vpWindow, out double hvnW, out double tempLowerW, out double tempUpperW);
						leg_HVN_W = ShowPOCWindow ? hvnW : double.NaN;
						lvnLowerW = tempLowerW;
						lvnUpperW = tempUpperW;
						
						if (ShowAntiPOCLowerWindow) AddTrackedNode(CurrentBar, lvnLowerW, "AP↓", 0, GetDeltaForPrice(vpWindowDelta, lvnLowerW), EduS_NodeType.AntiPOC, ref lastBar_LVN_W, ref lastPrice_LVN_W);
						if (ShowAntiPOCUpperWindow) AddTrackedNode(CurrentBar, lvnUpperW, "AP↑", 0, GetDeltaForPrice(vpWindowDelta, lvnUpperW), EduS_NodeType.AntiPOC, ref lastBar_LVN_W, ref lastPrice_LVN_W);
						if (ShowPOCWindow) AddTrackedNode(CurrentBar, hvnW, "POC", 1, GetDeltaForPrice(vpWindowDelta, hvnW), EduS_NodeType.POC, ref lastBar_HVN_W, ref lastPrice_HVN_W);
	
					//Fin: 11/01/2026		
					
					}
                    if (EnableStructuralLVN && ShowLVNStructWindow)
                    {
                        FindStructuralLVNs(vpWindow, calcStrength, out List<double> lvns);
                        leg_Str_W = (lvns.Count > 0) ? lvns[0] : double.NaN;
                        foreach (var p in lvns) AddSimpleNode(CurrentBar, p, "L", 2, GetDeltaForPrice(vpWindowDelta, p), EduS_NodeType.Structural); 
                    }
                }

                // SESIÓN
                if (EnableSession && vpSession.Count > 0)
                {
                    if (EnableAbsoluteNodes)
                    {
                        //Inicio: 11/01/2026
						
//						FindAbsoluteNodes(vpSession, out double lvn, out double hvn);
//                        leg_LVN_S = ShowAntiPOCSession ? lvn : double.NaN;
//                        leg_HVN_S = ShowPOCSession ? hvn : double.NaN;
//                        double dL = GetDeltaForPrice(vpSessionDelta, lvn);
//                        double dH = GetDeltaForPrice(vpSessionDelta, hvn);
//                        if (ShowAntiPOCSession) AddTrackedNode(CurrentBar, lvn, "AP(S)", 3, dL, EduS_NodeType.AntiPOC, ref lastBar_LVN_S, ref lastPrice_LVN_S);
//                        if (ShowPOCSession)     AddTrackedNode(CurrentBar, hvn, "POC(S)", 4, dH, EduS_NodeType.POC, ref lastBar_HVN_S, ref lastPrice_HVN_S);
                    
						
					FindExtendedNodes(vpSession, out double hvnS, out double tempLowerS, out double tempUpperS);
					leg_HVN_S = ShowPOCSession ? hvnS : double.NaN;
					lvnLowerS = tempLowerS;
					lvnUpperS = tempUpperS;
					
					if (ShowAntiPOCLowerSession) AddTrackedNode(CurrentBar, lvnLowerS, "AP↓(S)", 3, GetDeltaForPrice(vpSessionDelta, lvnLowerS), EduS_NodeType.AntiPOC, ref lastBar_LVN_S, ref lastPrice_LVN_S);
					if (ShowAntiPOCUpperSession) AddTrackedNode(CurrentBar, lvnUpperS, "AP↑(S)", 3, GetDeltaForPrice(vpSessionDelta, lvnUpperS), EduS_NodeType.AntiPOC, ref lastBar_LVN_S, ref lastPrice_LVN_S);
					if (ShowPOCSession) AddTrackedNode(CurrentBar, hvnS, "POC(S)", 4, GetDeltaForPrice(vpSessionDelta, hvnS), EduS_NodeType.POC, ref lastBar_HVN_S, ref lastPrice_HVN_S);

					
					//Fin: 11/01/2026
					}
                    if (EnableStructuralLVN && ShowLVNStructSession)
                    {
                        FindStructuralLVNs(vpSession, calcStrength, out List<double> lvns);
                        leg_Str_S = (lvns.Count > 0) ? lvns[0] : double.NaN;
                        foreach (var p in lvns) AddSimpleNode(CurrentBar, p, "L(S)", 5, GetDeltaForPrice(vpSessionDelta, p), EduS_NodeType.Structural);
                    }
                }
            }
        }

		//NUEVO MÉTODO: Ajusta WindowBars según minutos definidos
		// <summary>
		// Ajusta WindowBars según los minutos definidos y el timeframe del gráfico.
		// </summary>
		private void AjustarWindowBarsPorTiempo()
		{
		    int barrasPorMinuto = 1; // Valor por defecto
		
		    if (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute)
		    {
		        // Si el gráfico es de minutos, calculamos barras por minuto
		        barrasPorMinuto = 1 / BarsPeriod.Value;
		        if (barrasPorMinuto < 1) barrasPorMinuto = 1;
		    }
		    else
		    {
		        // Si el gráfico es Tick, estimamos barras por minuto usando los últimos 10 minutos
		        int lookbackMinutes = 10;
		        int barsBack = Bars.GetBar(Time[0].AddMinutes(-lookbackMinutes));
		        if (barsBack > 0)
		        {
		            barrasPorMinuto = (CurrentBar - barsBack) / lookbackMinutes;
		            if (barrasPorMinuto < 1) barrasPorMinuto = 1;
		        }
		    }
		
		    WindowBars = WindowMinutes * barrasPorMinuto;
		    Print($"[INFO] Ventana ajustada: {WindowMinutes} min → {WindowBars} barras (Barras/min: {barrasPorMinuto})");
		}

        // ===========================
        // MÉTODOS DE APOYO
        // ===========================

        private void StoreNakedPOC()
        {
            if (!ShowNakedPOCs || vpSession.Count == 0) return;
            double hvnPrice = double.NaN;
            double maxVol = -1;
            foreach(var kvp in vpSession) {
                if (kvp.Value > maxVol) { maxVol = kvp.Value; hvnPrice = kvp.Key; }
            }
            if (!double.IsNaN(hvnPrice)) {
                lock(drawLock) {
                    nakedNodes.Add(new EduS_NakedNode_Institutional { 
                        Price = hvnPrice, StartBarIndex = CurrentBar, Volume = maxVol, IsPOC = true 
                    });
                }
            }
        }

        private void ProcessVolumeOptimized(double vol, double delta, double high, double low, double close, double open, bool forceLiteMode)
        {
            EduS_DistMode_Institutional mode = DistributionMode;
            if (forceLiteMode && mode == EduS_DistMode_Institutional.Full) 
                mode = EduS_DistMode_Institutional.OHLC;

            var barProfile = CalculateBarProfile(vol, high, low, close, open, mode);
            var barProfileDelta = CalculateBarProfile(delta, high, low, close, open, mode);

            if (EnableWindow)
            {
                foreach(var kvp in barProfile) {
                    if (!vpWindow.ContainsKey(kvp.Key)) vpWindow[kvp.Key] = 0; vpWindow[kvp.Key] += kvp.Value;
                }
                foreach(var kvp in barProfileDelta) {
                    if (!vpWindowDelta.ContainsKey(kvp.Key)) vpWindowDelta[kvp.Key] = 0; vpWindowDelta[kvp.Key] += kvp.Value;
                }
                
                fifoWindowData.Enqueue(barProfile);
                fifoWindowDelta.Enqueue(barProfileDelta);

                while (fifoWindowData.Count > WindowBars) {
                    var old = fifoWindowData.Dequeue();
                    foreach (var kvp in old) {
                        if (vpWindow.ContainsKey(kvp.Key)) {
                            vpWindow[kvp.Key] -= kvp.Value;
                            if (vpWindow[kvp.Key] <= 0.001) vpWindow.Remove(kvp.Key);
                        }
                    }
                    var oldDelta = fifoWindowDelta.Dequeue();
                    foreach (var kvp in oldDelta) {
                         if (vpWindowDelta.ContainsKey(kvp.Key)) {
                            vpWindowDelta[kvp.Key] -= kvp.Value;
                            if (Math.Abs(vpWindowDelta[kvp.Key]) <= 0.001 && !vpWindow.ContainsKey(kvp.Key)) 
                                vpWindowDelta.Remove(kvp.Key);
                        }
                    }
                }
            }

            if (EnableSession)
            {
                foreach(var kvp in barProfile) {
                    if (!vpSession.ContainsKey(kvp.Key)) vpSession[kvp.Key] = 0; vpSession[kvp.Key] += kvp.Value;
                }
                foreach(var kvp in barProfileDelta) {
                    if (!vpSessionDelta.ContainsKey(kvp.Key)) vpSessionDelta[kvp.Key] = 0; vpSessionDelta[kvp.Key] += kvp.Value;
                }
            }
        }

        private Dictionary<double, double> CalculateBarProfile(double val, double high, double low, double close, double open, EduS_DistMode_Institutional mode)
        {
            var r = new Dictionary<double, double>();
            if (Math.Abs(val) <= 0.000001) return r;

            switch (mode)
            {
                case EduS_DistMode_Institutional.Close:
                    r[Instrument.MasterInstrument.RoundToTickSize(close)] = val; 
                    break;
                case EduS_DistMode_Institutional.OHLC:
                    double v4 = val * 0.25; 
                    void Add(double p) { 
                        double pr = Instrument.MasterInstrument.RoundToTickSize(p); 
                        if(!r.ContainsKey(pr)) r[pr]=0; r[pr]+=v4; 
                    }
                    Add(open); Add(high); Add(low); Add(close);
                    break;
                default: 
                    int ticks = (int)Math.Round((high - low) / tickSize) + 1;
                    if (ticks < 1) ticks = 1;
                    double vt = val / (double)ticks;
                    for (int i=0; i<ticks; i++) {
                        double pr = Instrument.MasterInstrument.RoundToTickSize(low + i*tickSize);
                        if (!r.ContainsKey(pr)) r[pr]=0; r[pr]+=vt;
                    }
                    break;
            }
            return r;
        }

		//Inicio: 11/01/2026
		
//        private void FindAbsoluteNodes(Dictionary<double, double> p, out double lvn, out double hvn)
//        {
//            lvn = double.NaN; hvn = double.NaN;
//            if (p == null || p.Count == 0) return;
//            double maxV = -1.0, minV = double.MaxValue;
//            foreach(var kvp in p) {
//                if (kvp.Value < 0.1) continue; 
//                if (kvp.Value > maxV) { maxV = kvp.Value; hvn = kvp.Key; }
//                if (kvp.Value < minV) { minV = kvp.Value; lvn = kvp.Key; }
//            }
//        }
		
				
		private void FindExtendedNodes(Dictionary<double, double> p, out double hvn, out double lvnLower, out double lvnUpper)
		{
		    hvn = double.NaN; lvnLower = double.NaN; lvnUpper = double.NaN;
		    if (p == null || p.Count == 0) return;
		
		    var sorted = p.OrderBy(kv => kv.Key).ToList();
		    hvn = sorted.OrderByDescending(kv => kv.Value).First().Key;
		
		    double minLower = double.MaxValue;
		    foreach (var kv in sorted.Take(sorted.Count / 3))
		        if (kv.Value < minLower) { minLower = kv.Value; lvnLower = kv.Key; }
		
		    double minUpper = double.MaxValue;
		    foreach (var kv in sorted.Skip(sorted.Count * 2 / 3))
		        if (kv.Value < minUpper) { minUpper = kv.Value; lvnUpper = kv.Key; }
		}
		
		//Fin: 11/01/2026

        private void FindStructuralLVNs(Dictionary<double, double> p, int strength, out List<double> res)
        {
            res = new List<double>();
            if (p == null || p.Count < (strength * 2 + 1)) return;
            var s = p.OrderBy(kv => kv.Key).ToList();
            for (int i = strength; i < s.Count - strength; i++) {
                double cv = s[i].Value;
                bool valley = true;
                for (int j = 1; j <= strength; j++) {
                    if (s[i-j].Value <= cv || s[i+j].Value <= cv) { valley = false; break; }
                }
                if (valley) res.Add(s[i].Key);
            }
        }

        private double GetDeltaForPrice(Dictionary<double, double> deltaDict, double price)
        {
            if (deltaDict != null && deltaDict.ContainsKey(price)) return deltaDict[price];
            return 0;
        }

        private void ResetSessionTrackers()
        {
            lastBar_LVN_S = -1; lastBar_HVN_S = -1;
            lastPrice_LVN_S = double.NaN; lastPrice_HVN_S = double.NaN;
            sessionStartedToday = false;
        }

        private void AddTrackedNode(int barIdx, double price, string lbl, int colorType, double delta, EduS_NodeType nType, ref int lastBar, ref double lastPrice)
        {
            if (double.IsNaN(price)) return;
            int finalColor = colorType;
            if (UseDeltaColoring) {
                if (delta > 0) finalColor = 6; 
                else if (delta < 0) finalColor = 7; 
            }
            nodesToDraw.Add(new EduS_Node_Institutional { BarIndex = barIdx, Price = price, LabelText = lbl, ColorType = finalColor, DeltaValue = delta, Type = nType });
            if (ConnectWithLine && lastBar >= 0 && !double.IsNaN(lastPrice))
            {
                double tickDiff = Math.Abs((price - lastPrice) / tickSize);
                if (tickDiff <= ZoneMergeTicks)
                {
                    linesToDraw.Add(new EduS_Line_Institutional { StartBar = lastBar, StartPrice = lastPrice, EndBar = barIdx, EndPrice = price, ColorType = finalColor });
                }
            }
            lastBar = barIdx; lastPrice = price;
        }

        private void AddSimpleNode(int barIdx, double price, string lbl, int colorType, double delta, EduS_NodeType nType)
        {
            if (double.IsNaN(price)) return;
            int finalColor = colorType;
            if (UseDeltaColoring) {
                if (delta > 0) finalColor = 6;
                else if (delta < 0) finalColor = 7;
            }
            nodesToDraw.Add(new EduS_Node_Institutional { BarIndex = barIdx, Price = price, LabelText = lbl, ColorType = finalColor, DeltaValue = delta, Type = nType });
        }

        // ==========================================
        // RENDER (DIBUJO DEFINITIVO Y SEGURO)
        // ==========================================
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (nodesToDraw == null) return;
            try { CreateDXResources(RenderTarget); } catch { return; }
            if (b_LVN_W == null) return;

            RenderTarget.AntialiasMode = D2D1.AntialiasMode.PerPrimitive;

            int startIdx = ChartBars.FromIndex;
            int endIdx = ChartBars.ToIndex;

            lock (drawLock)
            {
                // 1. NAKED POCS
                if (ShowNakedPOCs)
                {
                    foreach(var np in nakedNodes)
                    {
                        float y = chartScale.GetYByValue(np.Price);
                        if (y < -10 || y > chartControl.ActualHeight + 10) continue;

                        float x1 = chartControl.GetXByBarIndex(ChartBars, Math.Max(startIdx, np.StartBarIndex));
                        float x2 = chartControl.GetXByBarIndex(ChartBars, endIdx); 
                        
                        RenderTarget.DrawLine(new SDX.Vector2(x1, y), new SDX.Vector2(x2, y), b_Naked, NakedWidth, strokeNaked);
                    }
                }

                // 2. LÍNEAS DE CONEXIÓN
                foreach (var line in linesToDraw)
                {
                    if (line.EndBar < startIdx || line.StartBar > endIdx) continue;
                    float x1 = chartControl.GetXByBarIndex(ChartBars, line.StartBar);
                    float y1 = chartScale.GetYByValue(line.StartPrice);
                    float x2 = chartControl.GetXByBarIndex(ChartBars, line.EndBar);
                    float y2 = chartScale.GetYByValue(line.EndPrice);
                    RenderTarget.DrawLine(new SDX.Vector2(x1, y1), new SDX.Vector2(x2, y2), GetBrush(line.ColorType), 1.5f);
                }

                // 3. NODOS CON FORMAS + ETIQUETAS INTELIGENTES
                // A) Escaneo previo para encontrar el último de cada tipo (para la lógica "Solo última etiqueta")
                lastLabelIndices.Clear();
                int nodeCount = nodesToDraw.Count;

                if (ShowOnlyLastLabel && !ShowLabels)
                {
                    for (int i = 0; i < nodeCount; i++)
                    {
                        var node = nodesToDraw[i];
                        if (!string.IsNullOrEmpty(node.LabelText))
                            lastLabelIndices[node.LabelText] = i; 
                    }
                }

                // B) Bucle de Dibujo
                for (int i = 0; i < nodeCount; i++)
                {
                    var node = nodesToDraw[i];
                    if (node.BarIndex < startIdx || node.BarIndex > endIdx) continue;
                    
                    float x = chartControl.GetXByBarIndex(ChartBars, node.BarIndex);
                    float y = chartScale.GetYByValue(node.Price);
                    var b = GetBrush(node.ColorType);

                    // --- DIBUJO FORMAS (X, Punto, Guion) ---
                    switch (node.Type)
                    {
                        case EduS_NodeType.POC: // Punto
                            RenderTarget.FillEllipse(new D2D1.Ellipse(new SDX.Vector2(x, y), DotRadius, DotRadius), b);
                            break;
                        
						//Inicio: 11/01/2026	
//                        case EduS_NodeType.AntiPOC: // X (Cruz)
//                            float r = DotRadius;
//                            // Nota: Usamos stroke 1.5f sólido por defecto.
//                            RenderTarget.DrawLine(new SDX.Vector2(x - r, y - r), new SDX.Vector2(x + r, y + r), b, 1.5f);
//                            RenderTarget.DrawLine(new SDX.Vector2(x + r, y - r), new SDX.Vector2(x - r, y + r), b, 1.5f);
//                            break;
						
							case EduS_NodeType.AntiPOC: //AP↓ → triángulo invertido (punta hacia abajo). AP↑ → triángulo normal (punta hacia arriba). Otros Anti-POCs (si existieran) → X por defecto.
							float r = DotRadius;
							
							// Distinción por etiqueta
							if (node.LabelText.Contains("AP↓")) // Anti-POC inferior
							{
							    // Triángulo apuntando hacia abajo
							    var p1 = new SDX.Vector2(x - r, y - r);
							    var p2 = new SDX.Vector2(x + r, y - r);
							    var p3 = new SDX.Vector2(x, y + r);
							    RenderTarget.DrawLine(p1, p2, b, 1.5f);
							    RenderTarget.DrawLine(p2, p3, b, 1.5f);
							    RenderTarget.DrawLine(p3, p1, b, 1.5f);
							}
							else if (node.LabelText.Contains("AP↑")) // Anti-POC superior
							{
							    // Triángulo apuntando hacia arriba
							    var p1 = new SDX.Vector2(x - r, y + r);
							    var p2 = new SDX.Vector2(x + r, y + r);
							    var p3 = new SDX.Vector2(x, y - r);
							    RenderTarget.DrawLine(p1, p2, b, 1.5f);
							    RenderTarget.DrawLine(p2, p3, b, 1.5f);
							    RenderTarget.DrawLine(p3, p1, b, 1.5f);
							}
							else
							{
							    // Si no hay etiqueta específica, dibuja X por defecto
							    RenderTarget.DrawLine(new SDX.Vector2(x - r, y - r), new SDX.Vector2(x + r, y + r), b, 1.5f);
							    RenderTarget.DrawLine(new SDX.Vector2(x + r, y - r), new SDX.Vector2(x - r, y + r), b, 1.5f);
							}
							break;
							
							//Fin: 11/01/2026
	
							
                            
                        case EduS_NodeType.Structural: // Guion
                            float r2 = DotRadius * 1.5f;
                            RenderTarget.DrawLine(new SDX.Vector2(x - r2, y), new SDX.Vector2(x + r2, y), b, 2.0f);
                            break;
                    }

                    // --- DIBUJO ETIQUETAS ---
                    bool shouldDraw = false;

                    if (ShowLabels) 
                    {
                        shouldDraw = true; 
                    }
                    else if (ShowOnlyLastLabel)
                    {
                        // Solo si este índice coincide con el último encontrado para este texto específico
                        if (lastLabelIndices.ContainsKey(node.LabelText) && lastLabelIndices[node.LabelText] == i)
                            shouldDraw = true;
                    }

                    if (shouldDraw)
                    {
                        float txtX = x + LabelOffsetX;
                        float txtY = y + LabelOffsetY;
                        
                        using (var l = new DW.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, node.LabelText, txtFormatNodes, 150f, 20f)) 
                        {
                            RenderTarget.DrawTextLayout(new SDX.Vector2(txtX, txtY), l, b);
                        }
                    }
                }
            }
            if (ShowLegend) DrawOptimizedLegend(chartControl);
        }

        private void CreateDXResources(D2D1.RenderTarget rt)
        {
            if (b_LVN_W != null && !b_LVN_W.IsDisposed) return;
            b_LVN_W = ToDx(rt, ColorLVN_Window); b_HVN_W = ToDx(rt, ColorHVN_Window); b_Str_W = ToDx(rt, ColorLVN_Struct_W);
            b_LVN_S = ToDx(rt, ColorLVN_Session); b_HVN_S = ToDx(rt, ColorHVN_Session); b_Str_S = ToDx(rt, ColorLVN_Struct_S);
            
            b_DeltaPos = ToDx(rt, ColorDeltaPos);
            b_DeltaNeg = ToDx(rt, ColorDeltaNeg);
            b_Naked = ToDx(rt, ColorNakedPOC);

            b_Text = new D2D1.SolidColorBrush(rt, SDX.Color.White);

            var wpfColor = (LegendBackground as SolidColorBrush ?? Brushes.White).Color;
            float alpha = LegendOpacity / 100f; 
            b_LegendBg = new D2D1.SolidColorBrush(rt, new SDX.Color4(wpfColor.R/255f, wpfColor.G/255f, wpfColor.B/255f, alpha));
            
            b_LegendBorder = new D2D1.SolidColorBrush(rt, SDX.Color.Gray);
            
            using (var factory = new D2D1.Factory())
            {
                strokeNaked = new D2D1.StrokeStyle(factory, new D2D1.StrokeStyleProperties {
                    DashStyle = D2D1.DashStyle.Dash
                });
            }

            txtFormatNodes = new DW.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, LabelFontFamily??"Arial", DW.FontWeight.Bold, DW.FontStyle.Normal, (float)LabelSize);
            txtFormatLegend = new DW.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Consolas", DW.FontWeight.Normal, DW.FontStyle.Normal, 11f);
        }

        private void DisposeDXResources()
        {
            b_LVN_W?.Dispose(); b_HVN_W?.Dispose(); b_Str_W?.Dispose(); b_LVN_S?.Dispose(); b_HVN_S?.Dispose(); b_Str_S?.Dispose();
            b_DeltaPos?.Dispose(); b_DeltaNeg?.Dispose(); b_Naked?.Dispose();
            b_Text?.Dispose(); b_LegendBg?.Dispose(); b_LegendBorder?.Dispose();
            strokeNaked?.Dispose();
            txtFormatNodes?.Dispose(); txtFormatLegend?.Dispose(); b_LVN_W = null;
        }

        private D2D1.SolidColorBrush ToDx(D2D1.RenderTarget rt, Brush b) {
            var c = (b as SolidColorBrush ?? Brushes.White).Color;
            return new D2D1.SolidColorBrush(rt, new SDX.Color4(c.R/255f, c.G/255f, c.B/255f, c.A/255f));
        }

        private D2D1.SolidColorBrush GetBrush(int t) {
            switch(t) { 
                case 0:return b_LVN_W; case 1:return b_HVN_W; case 2:return b_Str_W; 
                case 3:return b_LVN_S; case 4:return b_HVN_S; case 5:return b_Str_S; 
                case 6:return b_DeltaPos; case 7:return b_DeltaNeg;
                default:return b_Text;
            }
        }

        private void DrawOptimizedLegend(ChartControl cc)
        {
            var L = new List<(string t, D2D1.Brush b)>();
            string F(double d) => double.IsNaN(d) ? "-" : Instrument.MasterInstrument.FormatPrice(d);
            
            string strInfo = UseDynamicStrength ? $" (Dyn {currentDynamicStrength})" : "";

            if(EnableWindow) {
                //if(ShowAntiPOCWindow) L.Add(($"Win AP: {F(leg_LVN_W)}", b_LVN_W)); 
                if(ShowPOCWindow)     L.Add(($"Win POC: {F(leg_HVN_W)}", b_HVN_W)); 
                if(ShowLVNStructWindow) L.Add(($"Win Str{strInfo}: {F(leg_Str_W)}", b_Str_W));
            }

            if(EnableSession) {
                //if(ShowAntiPOCSession) L.Add(($"Ses AP: {F(leg_LVN_S)}", b_LVN_S)); 
                if(ShowPOCSession)     L.Add(($"Ses POC: {F(leg_HVN_S)}", b_HVN_S)); 
                if(ShowLVNStructSession) L.Add(($"Ses Str: {F(leg_Str_S)}", b_Str_S));
            }

            if(ShowNakedPOCs) L.Add(($"Naked Levels: {nakedNodes.Count}", b_Naked));
			
			//Inicio:11/01/2026
			if (ShowAntiPOCLowerWindow) L.Add(("Win AP↓: " + F(lvnLowerW), b_LVN_W));
			if (ShowAntiPOCUpperWindow) L.Add(("Win AP↑: " + F(lvnUpperW), b_LVN_W));
			if (ShowAntiPOCLowerSession) L.Add(("Ses AP↓: " + F(lvnLowerS), b_LVN_S));
			if (ShowAntiPOCUpperSession) L.Add(("Ses AP↑: " + F(lvnUpperS), b_LVN_S));
			
			//Fin:11/01/2026

            if (L.Count == 0) return;

            float lh = 16f;
            float w = 180f;
            float h = L.Count * lh + 10; 
//            float pad = 10f;
//            float rightAxisMargin = 60f; 

//            float x = (LegendPosition == EduS_LegendPos_Institutional.TopRight || LegendPosition == EduS_LegendPos_Institutional.BottomRight) 
//                ? (float)cc.ActualWidth - w - pad - rightAxisMargin 
//                : pad;

//            float bottomMargin = 25f; 
//            float y = (LegendPosition == EduS_LegendPos_Institutional.BottomLeft || LegendPosition == EduS_LegendPos_Institutional.BottomRight) 
//                ? (float)cc.ActualHeight - h - pad - bottomMargin 
//                : pad;
            
			//Inicio: 09-01-2026
			
			
			float padX = (float)LegendMarginX;
			float padY = (float)LegendMarginY;
			float rightAxisMargin = 60f;
			
			float x = (LegendPosition == EduS_LegendPos_Institutional.TopRight
			           || LegendPosition == EduS_LegendPos_Institutional.BottomRight)
			           ? (float)cc.ActualWidth - w - padX - rightAxisMargin
			           : padX;
			
			float y = (LegendPosition == EduS_LegendPos_Institutional.TopLeft
			           || LegendPosition == EduS_LegendPos_Institutional.TopRight)
			           ? padY
			           : (float)cc.ActualHeight - h - padY;

			
			//fin: 09-01-2026
			
			
			
            if (ShowLegendBackground)
            {
                var r = new SDX.RectangleF(x, y, w, h);
                RenderTarget.FillRectangle(r, b_LegendBg); RenderTarget.DrawRectangle(r, b_LegendBorder);
            }

            float cy = y + 5;
            foreach(var i in L) {
                using (var tl = new DW.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, i.t, txtFormatLegend, w, lh)) {
                    RenderTarget.DrawTextLayout(new SDX.Vector2(x+5, cy), tl, i.b);
                }
                cy += lh;
            }
        }

        private BrushConverter bc = new BrushConverter();
        private string SerializeBrush(Brush b) => bc.ConvertToString(b);
        private Brush DeserializeBrush(string s) => (Brush)bc.ConvertFromString(s);
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduS_Trader_Nodes_V8_Institutional[] cacheEduS_Trader_Nodes_V8_Institutional;
		public EduS_Trader.EduS_Trader_Nodes_V8_Institutional EduS_Trader_Nodes_V8_Institutional(int daysToDisplay, int windowMinutes, int barsInterval, EduS_DistMode_Institutional distributionMode, int zoneMergeTicks, bool useDynamicStrength, int aTR_Period, double aTR_Factor, int nodeStrength, bool useDeltaColoring, bool showNakedPOCs, float nakedWidth, bool ignoreOvernightTouches, int rthEndTime, bool enableWindow, bool enableSession, bool enableAbsoluteNodes, bool enableStructuralLVN, int sessionStartTime, bool useSessionAutoReset, bool showPOCWindow, bool showLVNStructWindow, bool showPOCSession, bool showLVNStructSession, bool showAntiPOCUpperWindow, bool showAntiPOCLowerWindow, bool showAntiPOCUpperSession, bool showAntiPOCLowerSession, bool connectWithLine, bool showLabels, bool showOnlyLastLabel, int labelOffsetX, int labelOffsetY, int labelSize, string labelFontFamily, float dotRadius, bool showLegend, bool showLegendBackground, EduS_LegendPos_Institutional legendPosition, int legendMarginX, int legendMarginY)
		{
			return EduS_Trader_Nodes_V8_Institutional(Input, daysToDisplay, windowMinutes, barsInterval, distributionMode, zoneMergeTicks, useDynamicStrength, aTR_Period, aTR_Factor, nodeStrength, useDeltaColoring, showNakedPOCs, nakedWidth, ignoreOvernightTouches, rthEndTime, enableWindow, enableSession, enableAbsoluteNodes, enableStructuralLVN, sessionStartTime, useSessionAutoReset, showPOCWindow, showLVNStructWindow, showPOCSession, showLVNStructSession, showAntiPOCUpperWindow, showAntiPOCLowerWindow, showAntiPOCUpperSession, showAntiPOCLowerSession, connectWithLine, showLabels, showOnlyLastLabel, labelOffsetX, labelOffsetY, labelSize, labelFontFamily, dotRadius, showLegend, showLegendBackground, legendPosition, legendMarginX, legendMarginY);
		}

		public EduS_Trader.EduS_Trader_Nodes_V8_Institutional EduS_Trader_Nodes_V8_Institutional(ISeries<double> input, int daysToDisplay, int windowMinutes, int barsInterval, EduS_DistMode_Institutional distributionMode, int zoneMergeTicks, bool useDynamicStrength, int aTR_Period, double aTR_Factor, int nodeStrength, bool useDeltaColoring, bool showNakedPOCs, float nakedWidth, bool ignoreOvernightTouches, int rthEndTime, bool enableWindow, bool enableSession, bool enableAbsoluteNodes, bool enableStructuralLVN, int sessionStartTime, bool useSessionAutoReset, bool showPOCWindow, bool showLVNStructWindow, bool showPOCSession, bool showLVNStructSession, bool showAntiPOCUpperWindow, bool showAntiPOCLowerWindow, bool showAntiPOCUpperSession, bool showAntiPOCLowerSession, bool connectWithLine, bool showLabels, bool showOnlyLastLabel, int labelOffsetX, int labelOffsetY, int labelSize, string labelFontFamily, float dotRadius, bool showLegend, bool showLegendBackground, EduS_LegendPos_Institutional legendPosition, int legendMarginX, int legendMarginY)
		{
			if (cacheEduS_Trader_Nodes_V8_Institutional != null)
				for (int idx = 0; idx < cacheEduS_Trader_Nodes_V8_Institutional.Length; idx++)
					if (cacheEduS_Trader_Nodes_V8_Institutional[idx] != null && cacheEduS_Trader_Nodes_V8_Institutional[idx].DaysToDisplay == daysToDisplay && cacheEduS_Trader_Nodes_V8_Institutional[idx].WindowMinutes == windowMinutes && cacheEduS_Trader_Nodes_V8_Institutional[idx].BarsInterval == barsInterval && cacheEduS_Trader_Nodes_V8_Institutional[idx].DistributionMode == distributionMode && cacheEduS_Trader_Nodes_V8_Institutional[idx].ZoneMergeTicks == zoneMergeTicks && cacheEduS_Trader_Nodes_V8_Institutional[idx].UseDynamicStrength == useDynamicStrength && cacheEduS_Trader_Nodes_V8_Institutional[idx].ATR_Period == aTR_Period && cacheEduS_Trader_Nodes_V8_Institutional[idx].ATR_Factor == aTR_Factor && cacheEduS_Trader_Nodes_V8_Institutional[idx].NodeStrength == nodeStrength && cacheEduS_Trader_Nodes_V8_Institutional[idx].UseDeltaColoring == useDeltaColoring && cacheEduS_Trader_Nodes_V8_Institutional[idx].ShowNakedPOCs == showNakedPOCs && cacheEduS_Trader_Nodes_V8_Institutional[idx].NakedWidth == nakedWidth && cacheEduS_Trader_Nodes_V8_Institutional[idx].IgnoreOvernightTouches == ignoreOvernightTouches && cacheEduS_Trader_Nodes_V8_Institutional[idx].RthEndTime == rthEndTime && cacheEduS_Trader_Nodes_V8_Institutional[idx].EnableWindow == enableWindow && cacheEduS_Trader_Nodes_V8_Institutional[idx].EnableSession == enableSession && cacheEduS_Trader_Nodes_V8_Institutional[idx].EnableAbsoluteNodes == enableAbsoluteNodes && cacheEduS_Trader_Nodes_V8_Institutional[idx].EnableStructuralLVN == enableStructuralLVN && cacheEduS_Trader_Nodes_V8_Institutional[idx].SessionStartTime == sessionStartTime && cacheEduS_Trader_Nodes_V8_Institutional[idx].UseSessionAutoReset == useSessionAutoReset && cacheEduS_Trader_Nodes_V8_Institutional[idx].ShowPOCWindow == showPOCWindow && cacheEduS_Trader_Nodes_V8_Institutional[idx].ShowLVNStructWindow == showLVNStructWindow && cacheEduS_Trader_Nodes_V8_Institutional[idx].ShowPOCSession == showPOCSession && cacheEduS_Trader_Nodes_V8_Institutional[idx].ShowLVNStructSession == showLVNStructSession && cacheEduS_Trader_Nodes_V8_Institutional[idx].ShowAntiPOCUpperWindow == showAntiPOCUpperWindow && cacheEduS_Trader_Nodes_V8_Institutional[idx].ShowAntiPOCLowerWindow == showAntiPOCLowerWindow && cacheEduS_Trader_Nodes_V8_Institutional[idx].ShowAntiPOCUpperSession == showAntiPOCUpperSession && cacheEduS_Trader_Nodes_V8_Institutional[idx].ShowAntiPOCLowerSession == showAntiPOCLowerSession && cacheEduS_Trader_Nodes_V8_Institutional[idx].ConnectWithLine == connectWithLine && cacheEduS_Trader_Nodes_V8_Institutional[idx].ShowLabels == showLabels && cacheEduS_Trader_Nodes_V8_Institutional[idx].ShowOnlyLastLabel == showOnlyLastLabel && cacheEduS_Trader_Nodes_V8_Institutional[idx].LabelOffsetX == labelOffsetX && cacheEduS_Trader_Nodes_V8_Institutional[idx].LabelOffsetY == labelOffsetY && cacheEduS_Trader_Nodes_V8_Institutional[idx].LabelSize == labelSize && cacheEduS_Trader_Nodes_V8_Institutional[idx].LabelFontFamily == labelFontFamily && cacheEduS_Trader_Nodes_V8_Institutional[idx].DotRadius == dotRadius && cacheEduS_Trader_Nodes_V8_Institutional[idx].ShowLegend == showLegend && cacheEduS_Trader_Nodes_V8_Institutional[idx].ShowLegendBackground == showLegendBackground && cacheEduS_Trader_Nodes_V8_Institutional[idx].LegendPosition == legendPosition && cacheEduS_Trader_Nodes_V8_Institutional[idx].LegendMarginX == legendMarginX && cacheEduS_Trader_Nodes_V8_Institutional[idx].LegendMarginY == legendMarginY && cacheEduS_Trader_Nodes_V8_Institutional[idx].EqualsInput(input))
						return cacheEduS_Trader_Nodes_V8_Institutional[idx];
			return CacheIndicator<EduS_Trader.EduS_Trader_Nodes_V8_Institutional>(new EduS_Trader.EduS_Trader_Nodes_V8_Institutional(){ DaysToDisplay = daysToDisplay, WindowMinutes = windowMinutes, BarsInterval = barsInterval, DistributionMode = distributionMode, ZoneMergeTicks = zoneMergeTicks, UseDynamicStrength = useDynamicStrength, ATR_Period = aTR_Period, ATR_Factor = aTR_Factor, NodeStrength = nodeStrength, UseDeltaColoring = useDeltaColoring, ShowNakedPOCs = showNakedPOCs, NakedWidth = nakedWidth, IgnoreOvernightTouches = ignoreOvernightTouches, RthEndTime = rthEndTime, EnableWindow = enableWindow, EnableSession = enableSession, EnableAbsoluteNodes = enableAbsoluteNodes, EnableStructuralLVN = enableStructuralLVN, SessionStartTime = sessionStartTime, UseSessionAutoReset = useSessionAutoReset, ShowPOCWindow = showPOCWindow, ShowLVNStructWindow = showLVNStructWindow, ShowPOCSession = showPOCSession, ShowLVNStructSession = showLVNStructSession, ShowAntiPOCUpperWindow = showAntiPOCUpperWindow, ShowAntiPOCLowerWindow = showAntiPOCLowerWindow, ShowAntiPOCUpperSession = showAntiPOCUpperSession, ShowAntiPOCLowerSession = showAntiPOCLowerSession, ConnectWithLine = connectWithLine, ShowLabels = showLabels, ShowOnlyLastLabel = showOnlyLastLabel, LabelOffsetX = labelOffsetX, LabelOffsetY = labelOffsetY, LabelSize = labelSize, LabelFontFamily = labelFontFamily, DotRadius = dotRadius, ShowLegend = showLegend, ShowLegendBackground = showLegendBackground, LegendPosition = legendPosition, LegendMarginX = legendMarginX, LegendMarginY = legendMarginY }, input, ref cacheEduS_Trader_Nodes_V8_Institutional);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduS_Trader_Nodes_V8_Institutional EduS_Trader_Nodes_V8_Institutional(int daysToDisplay, int windowMinutes, int barsInterval, EduS_DistMode_Institutional distributionMode, int zoneMergeTicks, bool useDynamicStrength, int aTR_Period, double aTR_Factor, int nodeStrength, bool useDeltaColoring, bool showNakedPOCs, float nakedWidth, bool ignoreOvernightTouches, int rthEndTime, bool enableWindow, bool enableSession, bool enableAbsoluteNodes, bool enableStructuralLVN, int sessionStartTime, bool useSessionAutoReset, bool showPOCWindow, bool showLVNStructWindow, bool showPOCSession, bool showLVNStructSession, bool showAntiPOCUpperWindow, bool showAntiPOCLowerWindow, bool showAntiPOCUpperSession, bool showAntiPOCLowerSession, bool connectWithLine, bool showLabels, bool showOnlyLastLabel, int labelOffsetX, int labelOffsetY, int labelSize, string labelFontFamily, float dotRadius, bool showLegend, bool showLegendBackground, EduS_LegendPos_Institutional legendPosition, int legendMarginX, int legendMarginY)
		{
			return indicator.EduS_Trader_Nodes_V8_Institutional(Input, daysToDisplay, windowMinutes, barsInterval, distributionMode, zoneMergeTicks, useDynamicStrength, aTR_Period, aTR_Factor, nodeStrength, useDeltaColoring, showNakedPOCs, nakedWidth, ignoreOvernightTouches, rthEndTime, enableWindow, enableSession, enableAbsoluteNodes, enableStructuralLVN, sessionStartTime, useSessionAutoReset, showPOCWindow, showLVNStructWindow, showPOCSession, showLVNStructSession, showAntiPOCUpperWindow, showAntiPOCLowerWindow, showAntiPOCUpperSession, showAntiPOCLowerSession, connectWithLine, showLabels, showOnlyLastLabel, labelOffsetX, labelOffsetY, labelSize, labelFontFamily, dotRadius, showLegend, showLegendBackground, legendPosition, legendMarginX, legendMarginY);
		}

		public Indicators.EduS_Trader.EduS_Trader_Nodes_V8_Institutional EduS_Trader_Nodes_V8_Institutional(ISeries<double> input , int daysToDisplay, int windowMinutes, int barsInterval, EduS_DistMode_Institutional distributionMode, int zoneMergeTicks, bool useDynamicStrength, int aTR_Period, double aTR_Factor, int nodeStrength, bool useDeltaColoring, bool showNakedPOCs, float nakedWidth, bool ignoreOvernightTouches, int rthEndTime, bool enableWindow, bool enableSession, bool enableAbsoluteNodes, bool enableStructuralLVN, int sessionStartTime, bool useSessionAutoReset, bool showPOCWindow, bool showLVNStructWindow, bool showPOCSession, bool showLVNStructSession, bool showAntiPOCUpperWindow, bool showAntiPOCLowerWindow, bool showAntiPOCUpperSession, bool showAntiPOCLowerSession, bool connectWithLine, bool showLabels, bool showOnlyLastLabel, int labelOffsetX, int labelOffsetY, int labelSize, string labelFontFamily, float dotRadius, bool showLegend, bool showLegendBackground, EduS_LegendPos_Institutional legendPosition, int legendMarginX, int legendMarginY)
		{
			return indicator.EduS_Trader_Nodes_V8_Institutional(input, daysToDisplay, windowMinutes, barsInterval, distributionMode, zoneMergeTicks, useDynamicStrength, aTR_Period, aTR_Factor, nodeStrength, useDeltaColoring, showNakedPOCs, nakedWidth, ignoreOvernightTouches, rthEndTime, enableWindow, enableSession, enableAbsoluteNodes, enableStructuralLVN, sessionStartTime, useSessionAutoReset, showPOCWindow, showLVNStructWindow, showPOCSession, showLVNStructSession, showAntiPOCUpperWindow, showAntiPOCLowerWindow, showAntiPOCUpperSession, showAntiPOCLowerSession, connectWithLine, showLabels, showOnlyLastLabel, labelOffsetX, labelOffsetY, labelSize, labelFontFamily, dotRadius, showLegend, showLegendBackground, legendPosition, legendMarginX, legendMarginY);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduS_Trader_Nodes_V8_Institutional EduS_Trader_Nodes_V8_Institutional(int daysToDisplay, int windowMinutes, int barsInterval, EduS_DistMode_Institutional distributionMode, int zoneMergeTicks, bool useDynamicStrength, int aTR_Period, double aTR_Factor, int nodeStrength, bool useDeltaColoring, bool showNakedPOCs, float nakedWidth, bool ignoreOvernightTouches, int rthEndTime, bool enableWindow, bool enableSession, bool enableAbsoluteNodes, bool enableStructuralLVN, int sessionStartTime, bool useSessionAutoReset, bool showPOCWindow, bool showLVNStructWindow, bool showPOCSession, bool showLVNStructSession, bool showAntiPOCUpperWindow, bool showAntiPOCLowerWindow, bool showAntiPOCUpperSession, bool showAntiPOCLowerSession, bool connectWithLine, bool showLabels, bool showOnlyLastLabel, int labelOffsetX, int labelOffsetY, int labelSize, string labelFontFamily, float dotRadius, bool showLegend, bool showLegendBackground, EduS_LegendPos_Institutional legendPosition, int legendMarginX, int legendMarginY)
		{
			return indicator.EduS_Trader_Nodes_V8_Institutional(Input, daysToDisplay, windowMinutes, barsInterval, distributionMode, zoneMergeTicks, useDynamicStrength, aTR_Period, aTR_Factor, nodeStrength, useDeltaColoring, showNakedPOCs, nakedWidth, ignoreOvernightTouches, rthEndTime, enableWindow, enableSession, enableAbsoluteNodes, enableStructuralLVN, sessionStartTime, useSessionAutoReset, showPOCWindow, showLVNStructWindow, showPOCSession, showLVNStructSession, showAntiPOCUpperWindow, showAntiPOCLowerWindow, showAntiPOCUpperSession, showAntiPOCLowerSession, connectWithLine, showLabels, showOnlyLastLabel, labelOffsetX, labelOffsetY, labelSize, labelFontFamily, dotRadius, showLegend, showLegendBackground, legendPosition, legendMarginX, legendMarginY);
		}

		public Indicators.EduS_Trader.EduS_Trader_Nodes_V8_Institutional EduS_Trader_Nodes_V8_Institutional(ISeries<double> input , int daysToDisplay, int windowMinutes, int barsInterval, EduS_DistMode_Institutional distributionMode, int zoneMergeTicks, bool useDynamicStrength, int aTR_Period, double aTR_Factor, int nodeStrength, bool useDeltaColoring, bool showNakedPOCs, float nakedWidth, bool ignoreOvernightTouches, int rthEndTime, bool enableWindow, bool enableSession, bool enableAbsoluteNodes, bool enableStructuralLVN, int sessionStartTime, bool useSessionAutoReset, bool showPOCWindow, bool showLVNStructWindow, bool showPOCSession, bool showLVNStructSession, bool showAntiPOCUpperWindow, bool showAntiPOCLowerWindow, bool showAntiPOCUpperSession, bool showAntiPOCLowerSession, bool connectWithLine, bool showLabels, bool showOnlyLastLabel, int labelOffsetX, int labelOffsetY, int labelSize, string labelFontFamily, float dotRadius, bool showLegend, bool showLegendBackground, EduS_LegendPos_Institutional legendPosition, int legendMarginX, int legendMarginY)
		{
			return indicator.EduS_Trader_Nodes_V8_Institutional(input, daysToDisplay, windowMinutes, barsInterval, distributionMode, zoneMergeTicks, useDynamicStrength, aTR_Period, aTR_Factor, nodeStrength, useDeltaColoring, showNakedPOCs, nakedWidth, ignoreOvernightTouches, rthEndTime, enableWindow, enableSession, enableAbsoluteNodes, enableStructuralLVN, sessionStartTime, useSessionAutoReset, showPOCWindow, showLVNStructWindow, showPOCSession, showLVNStructSession, showAntiPOCUpperWindow, showAntiPOCLowerWindow, showAntiPOCUpperSession, showAntiPOCLowerSession, connectWithLine, showLabels, showOnlyLastLabel, labelOffsetX, labelOffsetY, labelSize, labelFontFamily, dotRadius, showLegend, showLegendBackground, legendPosition, legendMarginX, legendMarginY);
		}
	}
}

#endregion
