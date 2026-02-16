#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization; // Vital para guardar colores
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.Gui.Chart;

// Alias gráficos
using SDX = SharpDX;
using D2D1 = SharpDX.Direct2D1;
using DW   = SharpDX.DirectWrite;
#endregion

	public enum EduS_DistMode_V8 { Full, Close, OHLC }
    public enum EduS_LegendPos_V8 { TopLeft, TopRight, BottomLeft, BottomRight }


//namespace NinjaTrader.NinjaScript.Indicators.Edus_Trader
namespace NinjaTrader.NinjaScript.Indicators.Edus_Trader	
{
    // =========================================================
    // 1. TIPOS DE DATOS GLOBALES _V8 (Nombres únicos)
    // =========================================================
    
    //public enum EduS_DistMode_V8 { Full, Close, OHLC }
    //public enum EduS_LegendPos_V8 { TopLeft, TopRight, BottomLeft, BottomRight }

    public class EduS_Node_V8
    {
        public int BarIndex;
        public double Price;
        public string LabelText;
        public int ColorType;
    }

    public class EduS_Line_V8
    {
        public int StartBar;
        public double StartPrice;
        public int EndBar;
        public double EndPrice;
        public int ColorType;
    }

    // =========================================================
    // 2. CLASE PRINCIPAL
    // =========================================================
    public class EduS_Trader_Nodes_V8 : Indicator
    {
        #region Propiedades (Inputs)

        [NinjaScriptProperty, Range(1, 365)]
        [Display(Name = "Días a Mostrar", Order = 1, GroupName = "1. Rendimiento")]
        public int DaysToDisplay { get; set; } = 2;

        [NinjaScriptProperty, Range(1, 500)]
        [Display(Name = "Barras Ventana", Order = 2, GroupName = "2. Cálculo")]
        public int WindowBars { get; set; } = 150;

        [NinjaScriptProperty, Range(1, 200)]
        [Display(Name = "Intervalo Barras", Order = 3, GroupName = "2. Cálculo")]
        public int BarsInterval { get; set; } = 10;

        [NinjaScriptProperty, Range(0, 500)]
        [Display(Name = "Ticks Unión", Order = 4, GroupName = "2. Cálculo")]
        public int ZoneMergeTicks { get; set; } = 250;

        [NinjaScriptProperty, Range(1, 150)]
        [Display(Name = "Fuerza Nodo (aprox. ATR x 4)", Order = 5, GroupName = "2. Cálculo")]
        public int NodeStrength { get; set; } = 18;

        [NinjaScriptProperty]
        [Display(Name = "Modo Distribución", Order = 6, GroupName = "2. Cálculo")]
        public EduS_DistMode_V8 DistributionMode { get; set; } = EduS_DistMode_V8.Full;

        // Lógica
        [NinjaScriptProperty] [Display(Name = "Habilitar Ventana", Order = 7, GroupName = "3. Lógica")] public bool EnableWindow { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Habilitar Sesión", Order = 8, GroupName = "3. Lógica")] public bool EnableSession { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Calc POC/AntiPOC", Order = 9, GroupName = "3. Lógica")] public bool EnableAbsoluteNodes { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Calc Estructurales", Order = 10, GroupName = "3. Lógica")] public bool EnableStructuralLVN { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Hora Inicio (HHmm)", Order = 11, GroupName = "3. Lógica")] public int SessionStartTime { get; set; } = 930;
        [NinjaScriptProperty] [Display(Name = "Auto-Reset Sesión", Order = 12, GroupName = "3. Lógica")] public bool UseSessionAutoReset { get; set; } = true;

        // Visual
        [NinjaScriptProperty] [Display(Name = "Ver Anti-POC Ventana", Order = 13, GroupName = "4. Visual")] public bool ShowAntiPOCWindow { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Ver POC Ventana", Order = 14, GroupName = "4. Visual")] public bool ShowPOCWindow { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Ver LVN Estruc Win", Order = 15, GroupName = "4. Visual")] public bool ShowLVNStructWindow { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Ver Anti-POC Sesión", Order = 16, GroupName = "4. Visual")] public bool ShowAntiPOCSession { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Ver POC Sesión", Order = 17, GroupName = "4. Visual")] public bool ShowPOCSession { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Ver LVN Estruc Ses", Order = 18, GroupName = "4. Visual")] public bool ShowLVNStructSession { get; set; } = true;

        [NinjaScriptProperty] [Display(Name = "Conectar líneas", Order = 19, GroupName = "4. Visual")] public bool ConnectWithLine { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Mostrar Etiquetas", Order = 20, GroupName = "4. Visual")] public bool ShowLabels { get; set; } = true;
        [NinjaScriptProperty] [Display(Name = "Tamaño Texto", Order = 21, GroupName = "4. Visual")] public int LabelSize { get; set; } = 9;
        [NinjaScriptProperty] [Display(Name = "Fuente Texto", Order = 22, GroupName = "4. Visual")] public string LabelFontFamily { get; set; } = "Arial";
        [NinjaScriptProperty] [Display(Name = "Radio Punto (px)", Order = 23, GroupName = "4. Visual")] public float DotRadius { get; set; } = 3f;
        [NinjaScriptProperty] [Display(Name = "Ver Leyenda", Order = 24, GroupName = "4. Visual")] public bool ShowLegend { get; set; } = true;
        
		
		[NinjaScriptProperty]
		[Range(0, 500)]
		[Display(Name = "Margen Superior Leyenda", Order = 28, GroupName = "4. Visual")]
		public int LegendMarginTop { get; set; } = 40;
		
		[NinjaScriptProperty]
		[Range(0, 500)]
		[Display(Name = "Margen Lateral Leyenda", Order = 29, GroupName = "4. Visual")]
		public int LegendMarginSide { get; set; } = 20;

		
        [NinjaScriptProperty] [Display(Name = "Posición Leyenda", Order = 25, GroupName = "4. Visual")] public EduS_LegendPos_V8 LegendPosition { get; set; } = EduS_LegendPos_V8.BottomRight;

        // Colores
        [XmlIgnore] [Display(Name = "Color Anti-POC (W)", Order = 26, GroupName = "5. Colores")] public Brush ColorLVN_Window { get; set; } = Brushes.Green;
        [XmlIgnore] [Display(Name = "Color POC (W)", Order = 27, GroupName = "5. Colores")]      public Brush ColorHVN_Window { get; set; } = Brushes.Red;
        [XmlIgnore] [Display(Name = "Color Estruc (W)", Order = 28, GroupName = "5. Colores")] public Brush ColorLVN_Struct_W { get; set; } = Brushes.Purple;
        
        [XmlIgnore] [Display(Name = "Color Anti-POC (S)", Order = 29, GroupName = "5. Colores")] public Brush ColorLVN_Session { get; set; } = Brushes.Green;
        [XmlIgnore] [Display(Name = "Color POC (S)", Order = 30, GroupName = "5. Colores")]      public Brush ColorHVN_Session { get; set; } = Brushes.Red;
        [XmlIgnore] [Display(Name = "Color Estruc (S)", Order = 31, GroupName = "5. Colores")] public Brush ColorLVN_Struct_S { get; set; } = Brushes.Purple;

        // Serialización
        [Browsable(false)] public string ColorLVN_WindowSerializable { get => SerializeBrush(ColorLVN_Window); set => ColorLVN_Window = DeserializeBrush(value); }
        [Browsable(false)] public string ColorHVN_WindowSerializable { get => SerializeBrush(ColorHVN_Window); set => ColorHVN_Window = DeserializeBrush(value); }
        [Browsable(false)] public string ColorLVN_Struct_WSerializable { get => SerializeBrush(ColorLVN_Struct_W); set => ColorLVN_Struct_W = DeserializeBrush(value); }
        [Browsable(false)] public string ColorLVN_SessionSerializable { get => SerializeBrush(ColorLVN_Session); set => ColorLVN_Session = DeserializeBrush(value); }
        [Browsable(false)] public string ColorHVN_SessionSerializable { get => SerializeBrush(ColorHVN_Session); set => ColorHVN_Session = DeserializeBrush(value); }
        [Browsable(false)] public string ColorLVN_Struct_SSerializable { get => SerializeBrush(ColorLVN_Struct_S); set => ColorLVN_Struct_S = DeserializeBrush(value); }

        #endregion

        #region Variables Internas
        private List<EduS_Node_V8> nodesToDraw;
        private List<EduS_Line_V8> linesToDraw;
        private object drawLock = new object();

        private Dictionary<double, double> vpWindow;
        private Dictionary<double, double> vpSession;
        private Queue<Dictionary<double, double>> fifoWindowData;
        private bool sessionStartedToday = false;
        private DateTime cutoffDate;
        private double tickSize;

        private int lastBar_LVN_W = -1, lastBar_HVN_W = -1;
        private double lastPrice_LVN_W = double.NaN, lastPrice_HVN_W = double.NaN;
        private int lastBar_LVN_S = -1, lastBar_HVN_S = -1;
        private double lastPrice_LVN_S = double.NaN, lastPrice_HVN_S = double.NaN;

        private double leg_LVN_W, leg_HVN_W, leg_Str_W, leg_LVN_S, leg_HVN_S, leg_Str_S;

        // DX Resources
        private D2D1.SolidColorBrush b_LVN_W, b_HVN_W, b_Str_W, b_LVN_S, b_HVN_S, b_Str_S;
        private D2D1.SolidColorBrush b_Text, b_LegendBg, b_LegendBorder;
        private DW.TextFormat txtFormatNodes, txtFormatLegend;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "EduS_Trader_Nodes_V8";
                Description = "Autor: EduS Trader — POC/Anti-POC + LVN Estructural";
                Calculate = Calculate.OnEachTick;
                IsOverlay = true;
                IsSuspendedWhileInactive = true;
                
                leg_LVN_W = leg_HVN_W = leg_Str_W = leg_LVN_S = leg_HVN_S = leg_Str_S = double.NaN;
            }
            else if (State == State.Configure)
            {
                nodesToDraw = new List<EduS_Node_V8>();
                linesToDraw = new List<EduS_Line_V8>();
            }
            else if (State == State.DataLoaded)
            {
                tickSize = (Instrument != null) ? Instrument.MasterInstrument.TickSize : 0.25;

                vpWindow = new Dictionary<double, double>();
                vpSession = new Dictionary<double, double>();
                fifoWindowData = new Queue<Dictionary<double, double>>();
                
                lock (drawLock)
                {
                    nodesToDraw.Clear();
                    linesToDraw.Clear();
                }

                cutoffDate = DateTime.Now.Date.AddDays(-DaysToDisplay);
            }
            else if (State == State.Terminated)
            {
                DisposeDXResources();
            }
        }

        public override string DisplayName => Name;

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2 || tickSize <= 0) return;
            if (Time[0].Date < cutoffDate) return;

            // 1. Session
            if (EnableSession)
            {
                if (UseSessionAutoReset && Bars.IsFirstBarOfSession)
                {
                    vpSession.Clear();
                    ResetSessionTrackers();
                }
                
                int hhmm = ToTime(Time[0]) / 100;
                int hhmmPrev = ToTime(Time[1]) / 100;
                if (!sessionStartedToday && hhmmPrev < SessionStartTime && hhmm >= SessionStartTime)
                {
                    vpSession.Clear();
                    ResetSessionTrackers();
                    sessionStartedToday = true;
                }
            }

            // 2. Volume
            double vol = Math.Max(Volume[0], 1);
            ProcessVolume(vol, High[0], Low[0], Close[0], Open[0]);

            // 3. Interval
            if ((CurrentBar % BarsInterval) != 0) return;

            // 4. Calculate
            lock (drawLock)
            {
                if (EnableWindow && vpWindow.Count > 0)
                {
                    if (EnableAbsoluteNodes)
                    {
                        FindAbsoluteNodes(vpWindow, out double lvn, out double hvn);
                        leg_LVN_W = ShowAntiPOCWindow ? lvn : double.NaN;
                        leg_HVN_W = ShowPOCWindow ? hvn : double.NaN;

                        // USAMOS AddTrackedNode (PORQUE PASAMOS REF)
                        if (ShowAntiPOCWindow) AddTrackedNode(CurrentBar, lvn, "AP", 0, ref lastBar_LVN_W, ref lastPrice_LVN_W);
                        if (ShowPOCWindow)     AddTrackedNode(CurrentBar, hvn, "POC", 1, ref lastBar_HVN_W, ref lastPrice_HVN_W);
                    }
                    if (EnableStructuralLVN && ShowLVNStructWindow)
                    {
                        FindStructuralLVNs(vpWindow, out List<double> lvns);
                        leg_Str_W = (lvns.Count > 0) ? lvns[0] : double.NaN;
                        
                        // USAMOS AddSimpleNode (SIN REF, SOLO VALORES)
                        foreach (var p in lvns) AddSimpleNode(CurrentBar, p, "L", 2); 
                    }
                }

                if (EnableSession && vpSession.Count > 0)
                {
                    if (EnableAbsoluteNodes)
                    {
                        FindAbsoluteNodes(vpSession, out double lvn, out double hvn);
                        leg_LVN_S = ShowAntiPOCSession ? lvn : double.NaN;
                        leg_HVN_S = ShowPOCSession ? hvn : double.NaN;

                        // USAMOS AddTrackedNode
                        if (ShowAntiPOCSession) AddTrackedNode(CurrentBar, lvn, "AP(S)", 3, ref lastBar_LVN_S, ref lastPrice_LVN_S);
                        if (ShowPOCSession)     AddTrackedNode(CurrentBar, hvn, "POC(S)", 4, ref lastBar_HVN_S, ref lastPrice_HVN_S);
                    }
                    if (EnableStructuralLVN && ShowLVNStructSession)
                    {
                        FindStructuralLVNs(vpSession, out List<double> lvns);
                        leg_Str_S = (lvns.Count > 0) ? lvns[0] : double.NaN;
                        
                        // USAMOS AddSimpleNode
                        foreach (var p in lvns) AddSimpleNode(CurrentBar, p, "L(S)", 5);
                    }
                }
            }
        }

        // --- Helpers ---
        private void ProcessVolume(double vol, double high, double low, double close, double open)
        {
            if (EnableWindow)
            {
                var barProfile = CalculateBarProfile(vol, high, low, close, open);
                foreach(var kvp in barProfile) {
                    if (!vpWindow.ContainsKey(kvp.Key)) vpWindow[kvp.Key] = 0;
                    vpWindow[kvp.Key] += kvp.Value;
                }
                fifoWindowData.Enqueue(barProfile);
                while (fifoWindowData.Count > WindowBars) {
                    var old = fifoWindowData.Dequeue();
                    foreach (var kvp in old) {
                        if (vpWindow.ContainsKey(kvp.Key)) {
                            vpWindow[kvp.Key] -= kvp.Value;
                            if (vpWindow[kvp.Key] <= 0.0001) vpWindow.Remove(kvp.Key);
                        }
                    }
                }
            }
            if (EnableSession)
            {
                var barProfile = CalculateBarProfile(vol, high, low, close, open);
                foreach(var kvp in barProfile) {
                    if (!vpSession.ContainsKey(kvp.Key)) vpSession[kvp.Key] = 0;
                    vpSession[kvp.Key] += kvp.Value;
                }
            }
        }

        private Dictionary<double, double> CalculateBarProfile(double vol, double high, double low, double close, double open)
        {
            var r = new Dictionary<double, double>();
            if (vol <= 0) return r;
            switch (DistributionMode)
            {
                case EduS_DistMode_V8.Close:
                    r[Instrument.MasterInstrument.RoundToTickSize(close)] = vol; break;
                case EduS_DistMode_V8.OHLC:
                    double[] ohlc = { open, high, low, close };
                    double v4 = vol / 4.0;
                    foreach(var p in ohlc) {
                        double pr = Instrument.MasterInstrument.RoundToTickSize(p);
                        if (!r.ContainsKey(pr)) r[pr]=0; r[pr]+=v4;
                    }
                    break;
                default:
                    int ticks = (int)Math.Round((high - low) / tickSize) + 1;
                    if (ticks < 1) ticks = 1;
                    double vt = vol / ticks;
                    for (int i=0; i<ticks; i++) {
                        double pr = Instrument.MasterInstrument.RoundToTickSize(low + i*tickSize);
                        if (!r.ContainsKey(pr)) r[pr]=0; r[pr]+=vt;
                    }
                    break;
            }
            return r;
        }

        private void FindAbsoluteNodes(Dictionary<double, double> p, out double lvn, out double hvn)
        {
            lvn = double.NaN; hvn = double.NaN;
            if (p == null || p.Count == 0) return;
            double maxV = -1.0, minV = double.MaxValue;
            foreach(var kvp in p) {
                if (kvp.Value > maxV) { maxV = kvp.Value; hvn = kvp.Key; }
                if (kvp.Value < minV) { minV = kvp.Value; lvn = kvp.Key; }
            }
        }

        private void FindStructuralLVNs(Dictionary<double, double> p, out List<double> res)
        {
            res = new List<double>();
            if (p == null || p.Count < (NodeStrength * 2 + 1)) return;
            var s = p.OrderBy(kv => kv.Key).ToList();
            for (int i = NodeStrength; i < s.Count - NodeStrength; i++) {
                double cv = s[i].Value;
                bool valley = true;
                for (int j = 1; j <= NodeStrength; j++) {
                    if (s[i-j].Value < cv || s[i+j].Value < cv) { valley = false; break; }
                }
                if (valley) res.Add(s[i].Key);
            }
        }

        private void ResetSessionTrackers()
        {
            lastBar_LVN_S = -1; lastBar_HVN_S = -1;
            lastPrice_LVN_S = double.NaN; lastPrice_HVN_S = double.NaN;
            sessionStartedToday = false;
        }

        // =========================================================
        // MÉTODO 1: PARA POCs (Usa REF para dibujar líneas)
        // =========================================================
        private void AddTrackedNode(int barIdx, double price, string lbl, int colorType, ref int lastBar, ref double lastPrice)
        {
            if (double.IsNaN(price)) return;

            // Agregar nodo
            nodesToDraw.Add(new EduS_Node_V8 
            { 
                BarIndex = barIdx, Price = price, LabelText = lbl, ColorType = colorType 
            });

            // Lógica de líneas (solo si hay tracking)
            if (ConnectWithLine && lastBar >= 0 && !double.IsNaN(lastPrice))
            {
                if (Math.Abs((price - lastPrice) / tickSize) <= ZoneMergeTicks)
                {
                    linesToDraw.Add(new EduS_Line_V8
                    {
                        StartBar = lastBar, StartPrice = lastPrice, EndBar = barIdx, EndPrice = price, ColorType = colorType
                    });
                }
            }

            // Actualizar referencias
            lastBar = barIdx;
            lastPrice = price;
        }

        // =========================================================
        // MÉTODO 2: PARA ESTRUCTURALES (Solo agrega el punto, SIN REF)
        // =========================================================
        private void AddSimpleNode(int barIdx, double price, string lbl, int colorType)
        {
            if (double.IsNaN(price)) return;

            nodesToDraw.Add(new EduS_Node_V8 
            { 
                BarIndex = barIdx, Price = price, LabelText = lbl, ColorType = colorType 
            });
        }

        // ==========================================
        // ON RENDER (DIRECT 2D)
        // ==========================================
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (nodesToDraw == null) return;
            try { CreateDXResources(RenderTarget); } catch { return; }
            RenderTarget.AntialiasMode = D2D1.AntialiasMode.PerPrimitive;

            int startIdx = ChartBars.FromIndex;
            int endIdx = ChartBars.ToIndex;

            lock (drawLock)
            {
                // Lines
                foreach (var line in linesToDraw)
                {
                    if (line.EndBar < startIdx || line.StartBar > endIdx) continue;
                    float x1 = chartControl.GetXByBarIndex(ChartBars, line.StartBar);
                    float y1 = chartScale.GetYByValue(line.StartPrice);
                    float x2 = chartControl.GetXByBarIndex(ChartBars, line.EndBar);
                    float y2 = chartScale.GetYByValue(line.EndPrice);
                    RenderTarget.DrawLine(new SDX.Vector2(x1, y1), new SDX.Vector2(x2, y2), GetBrush(line.ColorType), 1.5f);
                }
                // Nodes
                foreach (var node in nodesToDraw)
                {
                    if (node.BarIndex < startIdx || node.BarIndex > endIdx) continue;
                    float x = chartControl.GetXByBarIndex(ChartBars, node.BarIndex);
                    float y = chartScale.GetYByValue(node.Price);
                    var b = GetBrush(node.ColorType);
                    RenderTarget.FillEllipse(new D2D1.Ellipse(new SDX.Vector2(x, y), DotRadius, DotRadius), b);
                    if (ShowLabels) {
                        using (var l = new DW.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, node.LabelText, txtFormatNodes, 100f, 20f)) {
                            RenderTarget.DrawTextLayout(new SDX.Vector2(x - (l.Metrics.Width/2), y - 15f), l, b);
                        }
                    }
                }
            }
            if (ShowLegend) DrawOptimizedLegend(chartControl);
        }

        // DX Resources
        private void CreateDXResources(D2D1.RenderTarget rt)
        {
            if (b_LVN_W != null && !b_LVN_W.IsDisposed) return;
            b_LVN_W = ToDx(rt, ColorLVN_Window); b_HVN_W = ToDx(rt, ColorHVN_Window); b_Str_W = ToDx(rt, ColorLVN_Struct_W);
            b_LVN_S = ToDx(rt, ColorLVN_Session); b_HVN_S = ToDx(rt, ColorHVN_Session); b_Str_S = ToDx(rt, ColorLVN_Struct_S);
            b_Text = new D2D1.SolidColorBrush(rt, SDX.Color.White);
            b_LegendBg = new D2D1.SolidColorBrush(rt, new SDX.Color4(0,0,0,0.4f));
            b_LegendBorder = new D2D1.SolidColorBrush(rt, SDX.Color.Gray);
            txtFormatNodes = new DW.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, LabelFontFamily??"Arial", DW.FontWeight.Bold, DW.FontStyle.Normal, (float)LabelSize);
            txtFormatLegend = new DW.TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory, "Consolas", DW.FontWeight.Normal, DW.FontStyle.Normal, 11f);
        }

        private void DisposeDXResources()
        {
            b_LVN_W?.Dispose(); b_HVN_W?.Dispose(); b_Str_W?.Dispose(); b_LVN_S?.Dispose(); b_HVN_S?.Dispose(); b_Str_S?.Dispose();
            b_Text?.Dispose(); b_LegendBg?.Dispose(); b_LegendBorder?.Dispose();
            txtFormatNodes?.Dispose(); txtFormatLegend?.Dispose(); b_LVN_W = null;
        }

        private D2D1.SolidColorBrush ToDx(D2D1.RenderTarget rt, Brush b) {
            var c = (b as SolidColorBrush ?? Brushes.White).Color;
            return new D2D1.SolidColorBrush(rt, new SDX.Color4(c.R/255f, c.G/255f, c.B/255f, c.A/255f));
        }

        private D2D1.SolidColorBrush GetBrush(int t) {
            switch(t) { case 0:return b_LVN_W; case 1:return b_HVN_W; case 2:return b_Str_W; case 3:return b_LVN_S; case 4:return b_HVN_S; case 5:return b_Str_S; default:return b_Text;}
        }

        private void DrawOptimizedLegend(ChartControl cc)
        {
            var L = new List<(string t, D2D1.Brush b)>();
            string F(double d) => double.IsNaN(d) ? "-" : Instrument.MasterInstrument.FormatPrice(d);
            L.Add(($"Win AP: {F(leg_LVN_W)}", b_LVN_W)); L.Add(($"Win POC: {F(leg_HVN_W)}", b_HVN_W)); L.Add(($"Win Str: {F(leg_Str_W)}", b_Str_W));
            L.Add(($"Ses AP: {F(leg_LVN_S)}", b_LVN_S)); L.Add(($"Ses POC: {F(leg_HVN_S)}", b_HVN_S)); L.Add(($"Ses Str: {F(leg_Str_S)}", b_Str_S));
            
			
			
		
			
            float lh = 16f, w = 170f, h = L.Count * lh + 10;//, pad = 100f;
//            float x = (LegendPosition==EduS_LegendPos_V8.TopRight||LegendPosition==EduS_LegendPos_V8.BottomRight) ? (float)cc.ActualWidth - w - pad : pad;
//            float y = (LegendPosition==EduS_LegendPos_V8.BottomLeft||LegendPosition==EduS_LegendPos_V8.BottomRight) ? (float)cc.ActualHeight - h - pad : pad;
            
			//Inicio : Ingreso 09-01-2026
			
			
				float padX = 20f; // margen lateral
				float padY = 400f; // margen superior/inferior
				//				float x = (LegendPosition==EduS_LegendPos_V8.TopRight
				//				           || LegendPosition==EduS_LegendPos_V8.BottomRight)
				//				           ? (float)cc.ActualWidth - w - padX : padX;
								
				//				float y = (LegendPosition==EduS_LegendPos_V8.BottomLeft
				//				           || LegendPosition==EduS_LegendPos_V8.BottomRight)
				//				           ? (float)cc.ActualHeight - h - padY : padY;

			
			
			float x = (LegendPosition==EduS_LegendPos_V8.TopRight || LegendPosition==EduS_LegendPos_V8.BottomRight)
			          ? (float)ChartPanel.X + (float)ChartPanel.W - w - LegendMarginSide
			          : (float)ChartPanel.X + LegendMarginSide;
			
			float y = (LegendPosition==EduS_LegendPos_V8.TopLeft || LegendPosition==EduS_LegendPos_V8.TopRight)
			          ? (float)ChartPanel.Y + LegendMarginTop
			          : (float)ChartPanel.Y + (float)ChartPanel.H - h - LegendMarginTop;
			

			
			//Fin : Ingreso 09-01-2026
			
			
			
			
            var r = new SDX.RectangleF(x, y, w, h);
            RenderTarget.FillRectangle(r, b_LegendBg); RenderTarget.DrawRectangle(r, b_LegendBorder);
            float cy = y + 5;
            foreach(var i in L) {
                using (var tl = new DW.TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, i.t, txtFormatLegend, w, lh)) {
                    RenderTarget.DrawTextLayout(new SDX.Vector2(x+5, cy), tl, i.b);
                }
                cy += lh;
            }
        }

        // Serialización
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
		private Edus_Trader.EduS_Trader_Nodes_V8[] cacheEduS_Trader_Nodes_V8;
		public Edus_Trader.EduS_Trader_Nodes_V8 EduS_Trader_Nodes_V8(int daysToDisplay, int windowBars, int barsInterval, int zoneMergeTicks, int nodeStrength, EduS_DistMode_V8 distributionMode, bool enableWindow, bool enableSession, bool enableAbsoluteNodes, bool enableStructuralLVN, int sessionStartTime, bool useSessionAutoReset, bool showAntiPOCWindow, bool showPOCWindow, bool showLVNStructWindow, bool showAntiPOCSession, bool showPOCSession, bool showLVNStructSession, bool connectWithLine, bool showLabels, int labelSize, string labelFontFamily, float dotRadius, bool showLegend, int legendMarginTop, int legendMarginSide, EduS_LegendPos_V8 legendPosition)
		{
			return EduS_Trader_Nodes_V8(Input, daysToDisplay, windowBars, barsInterval, zoneMergeTicks, nodeStrength, distributionMode, enableWindow, enableSession, enableAbsoluteNodes, enableStructuralLVN, sessionStartTime, useSessionAutoReset, showAntiPOCWindow, showPOCWindow, showLVNStructWindow, showAntiPOCSession, showPOCSession, showLVNStructSession, connectWithLine, showLabels, labelSize, labelFontFamily, dotRadius, showLegend, legendMarginTop, legendMarginSide, legendPosition);
		}

		public Edus_Trader.EduS_Trader_Nodes_V8 EduS_Trader_Nodes_V8(ISeries<double> input, int daysToDisplay, int windowBars, int barsInterval, int zoneMergeTicks, int nodeStrength, EduS_DistMode_V8 distributionMode, bool enableWindow, bool enableSession, bool enableAbsoluteNodes, bool enableStructuralLVN, int sessionStartTime, bool useSessionAutoReset, bool showAntiPOCWindow, bool showPOCWindow, bool showLVNStructWindow, bool showAntiPOCSession, bool showPOCSession, bool showLVNStructSession, bool connectWithLine, bool showLabels, int labelSize, string labelFontFamily, float dotRadius, bool showLegend, int legendMarginTop, int legendMarginSide, EduS_LegendPos_V8 legendPosition)
		{
			if (cacheEduS_Trader_Nodes_V8 != null)
				for (int idx = 0; idx < cacheEduS_Trader_Nodes_V8.Length; idx++)
					if (cacheEduS_Trader_Nodes_V8[idx] != null && cacheEduS_Trader_Nodes_V8[idx].DaysToDisplay == daysToDisplay && cacheEduS_Trader_Nodes_V8[idx].WindowBars == windowBars && cacheEduS_Trader_Nodes_V8[idx].BarsInterval == barsInterval && cacheEduS_Trader_Nodes_V8[idx].ZoneMergeTicks == zoneMergeTicks && cacheEduS_Trader_Nodes_V8[idx].NodeStrength == nodeStrength && cacheEduS_Trader_Nodes_V8[idx].DistributionMode == distributionMode && cacheEduS_Trader_Nodes_V8[idx].EnableWindow == enableWindow && cacheEduS_Trader_Nodes_V8[idx].EnableSession == enableSession && cacheEduS_Trader_Nodes_V8[idx].EnableAbsoluteNodes == enableAbsoluteNodes && cacheEduS_Trader_Nodes_V8[idx].EnableStructuralLVN == enableStructuralLVN && cacheEduS_Trader_Nodes_V8[idx].SessionStartTime == sessionStartTime && cacheEduS_Trader_Nodes_V8[idx].UseSessionAutoReset == useSessionAutoReset && cacheEduS_Trader_Nodes_V8[idx].ShowAntiPOCWindow == showAntiPOCWindow && cacheEduS_Trader_Nodes_V8[idx].ShowPOCWindow == showPOCWindow && cacheEduS_Trader_Nodes_V8[idx].ShowLVNStructWindow == showLVNStructWindow && cacheEduS_Trader_Nodes_V8[idx].ShowAntiPOCSession == showAntiPOCSession && cacheEduS_Trader_Nodes_V8[idx].ShowPOCSession == showPOCSession && cacheEduS_Trader_Nodes_V8[idx].ShowLVNStructSession == showLVNStructSession && cacheEduS_Trader_Nodes_V8[idx].ConnectWithLine == connectWithLine && cacheEduS_Trader_Nodes_V8[idx].ShowLabels == showLabels && cacheEduS_Trader_Nodes_V8[idx].LabelSize == labelSize && cacheEduS_Trader_Nodes_V8[idx].LabelFontFamily == labelFontFamily && cacheEduS_Trader_Nodes_V8[idx].DotRadius == dotRadius && cacheEduS_Trader_Nodes_V8[idx].ShowLegend == showLegend && cacheEduS_Trader_Nodes_V8[idx].LegendMarginTop == legendMarginTop && cacheEduS_Trader_Nodes_V8[idx].LegendMarginSide == legendMarginSide && cacheEduS_Trader_Nodes_V8[idx].LegendPosition == legendPosition && cacheEduS_Trader_Nodes_V8[idx].EqualsInput(input))
						return cacheEduS_Trader_Nodes_V8[idx];
			return CacheIndicator<Edus_Trader.EduS_Trader_Nodes_V8>(new Edus_Trader.EduS_Trader_Nodes_V8(){ DaysToDisplay = daysToDisplay, WindowBars = windowBars, BarsInterval = barsInterval, ZoneMergeTicks = zoneMergeTicks, NodeStrength = nodeStrength, DistributionMode = distributionMode, EnableWindow = enableWindow, EnableSession = enableSession, EnableAbsoluteNodes = enableAbsoluteNodes, EnableStructuralLVN = enableStructuralLVN, SessionStartTime = sessionStartTime, UseSessionAutoReset = useSessionAutoReset, ShowAntiPOCWindow = showAntiPOCWindow, ShowPOCWindow = showPOCWindow, ShowLVNStructWindow = showLVNStructWindow, ShowAntiPOCSession = showAntiPOCSession, ShowPOCSession = showPOCSession, ShowLVNStructSession = showLVNStructSession, ConnectWithLine = connectWithLine, ShowLabels = showLabels, LabelSize = labelSize, LabelFontFamily = labelFontFamily, DotRadius = dotRadius, ShowLegend = showLegend, LegendMarginTop = legendMarginTop, LegendMarginSide = legendMarginSide, LegendPosition = legendPosition }, input, ref cacheEduS_Trader_Nodes_V8);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Edus_Trader.EduS_Trader_Nodes_V8 EduS_Trader_Nodes_V8(int daysToDisplay, int windowBars, int barsInterval, int zoneMergeTicks, int nodeStrength, EduS_DistMode_V8 distributionMode, bool enableWindow, bool enableSession, bool enableAbsoluteNodes, bool enableStructuralLVN, int sessionStartTime, bool useSessionAutoReset, bool showAntiPOCWindow, bool showPOCWindow, bool showLVNStructWindow, bool showAntiPOCSession, bool showPOCSession, bool showLVNStructSession, bool connectWithLine, bool showLabels, int labelSize, string labelFontFamily, float dotRadius, bool showLegend, int legendMarginTop, int legendMarginSide, EduS_LegendPos_V8 legendPosition)
		{
			return indicator.EduS_Trader_Nodes_V8(Input, daysToDisplay, windowBars, barsInterval, zoneMergeTicks, nodeStrength, distributionMode, enableWindow, enableSession, enableAbsoluteNodes, enableStructuralLVN, sessionStartTime, useSessionAutoReset, showAntiPOCWindow, showPOCWindow, showLVNStructWindow, showAntiPOCSession, showPOCSession, showLVNStructSession, connectWithLine, showLabels, labelSize, labelFontFamily, dotRadius, showLegend, legendMarginTop, legendMarginSide, legendPosition);
		}

		public Indicators.Edus_Trader.EduS_Trader_Nodes_V8 EduS_Trader_Nodes_V8(ISeries<double> input , int daysToDisplay, int windowBars, int barsInterval, int zoneMergeTicks, int nodeStrength, EduS_DistMode_V8 distributionMode, bool enableWindow, bool enableSession, bool enableAbsoluteNodes, bool enableStructuralLVN, int sessionStartTime, bool useSessionAutoReset, bool showAntiPOCWindow, bool showPOCWindow, bool showLVNStructWindow, bool showAntiPOCSession, bool showPOCSession, bool showLVNStructSession, bool connectWithLine, bool showLabels, int labelSize, string labelFontFamily, float dotRadius, bool showLegend, int legendMarginTop, int legendMarginSide, EduS_LegendPos_V8 legendPosition)
		{
			return indicator.EduS_Trader_Nodes_V8(input, daysToDisplay, windowBars, barsInterval, zoneMergeTicks, nodeStrength, distributionMode, enableWindow, enableSession, enableAbsoluteNodes, enableStructuralLVN, sessionStartTime, useSessionAutoReset, showAntiPOCWindow, showPOCWindow, showLVNStructWindow, showAntiPOCSession, showPOCSession, showLVNStructSession, connectWithLine, showLabels, labelSize, labelFontFamily, dotRadius, showLegend, legendMarginTop, legendMarginSide, legendPosition);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Edus_Trader.EduS_Trader_Nodes_V8 EduS_Trader_Nodes_V8(int daysToDisplay, int windowBars, int barsInterval, int zoneMergeTicks, int nodeStrength, EduS_DistMode_V8 distributionMode, bool enableWindow, bool enableSession, bool enableAbsoluteNodes, bool enableStructuralLVN, int sessionStartTime, bool useSessionAutoReset, bool showAntiPOCWindow, bool showPOCWindow, bool showLVNStructWindow, bool showAntiPOCSession, bool showPOCSession, bool showLVNStructSession, bool connectWithLine, bool showLabels, int labelSize, string labelFontFamily, float dotRadius, bool showLegend, int legendMarginTop, int legendMarginSide, EduS_LegendPos_V8 legendPosition)
		{
			return indicator.EduS_Trader_Nodes_V8(Input, daysToDisplay, windowBars, barsInterval, zoneMergeTicks, nodeStrength, distributionMode, enableWindow, enableSession, enableAbsoluteNodes, enableStructuralLVN, sessionStartTime, useSessionAutoReset, showAntiPOCWindow, showPOCWindow, showLVNStructWindow, showAntiPOCSession, showPOCSession, showLVNStructSession, connectWithLine, showLabels, labelSize, labelFontFamily, dotRadius, showLegend, legendMarginTop, legendMarginSide, legendPosition);
		}

		public Indicators.Edus_Trader.EduS_Trader_Nodes_V8 EduS_Trader_Nodes_V8(ISeries<double> input , int daysToDisplay, int windowBars, int barsInterval, int zoneMergeTicks, int nodeStrength, EduS_DistMode_V8 distributionMode, bool enableWindow, bool enableSession, bool enableAbsoluteNodes, bool enableStructuralLVN, int sessionStartTime, bool useSessionAutoReset, bool showAntiPOCWindow, bool showPOCWindow, bool showLVNStructWindow, bool showAntiPOCSession, bool showPOCSession, bool showLVNStructSession, bool connectWithLine, bool showLabels, int labelSize, string labelFontFamily, float dotRadius, bool showLegend, int legendMarginTop, int legendMarginSide, EduS_LegendPos_V8 legendPosition)
		{
			return indicator.EduS_Trader_Nodes_V8(input, daysToDisplay, windowBars, barsInterval, zoneMergeTicks, nodeStrength, distributionMode, enableWindow, enableSession, enableAbsoluteNodes, enableStructuralLVN, sessionStartTime, useSessionAutoReset, showAntiPOCWindow, showPOCWindow, showLVNStructWindow, showAntiPOCSession, showPOCSession, showLVNStructSession, connectWithLine, showLabels, labelSize, labelFontFamily, dotRadius, showLegend, legendMarginTop, legendMarginSide, legendPosition);
		}
	}
}

#endregion
