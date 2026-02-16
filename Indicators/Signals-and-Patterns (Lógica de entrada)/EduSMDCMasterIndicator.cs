#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// Namespace personalizado solicitado: EduS_Trader
namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader
{
    public class EduS_Master_Indicator : Indicator
    {
        #region Variables Keltner & InfoPanel
        private Series<double> diff;
        private EMA emaDiff;
        private EMA emaTypical;
        private int lastBar;
        private long vlmDaily;
        private bool flagTextVolPB;
        private int cicloTime;
        private int cicloBar;
        private int speedBar;
        private ATR atrAlgo; 
        #endregion

        #region Variables MACD BB (Integrado)
        // Series internas para cálculo matemático (no visibles en gráfico de precio)
        private Series<double> macdSeries;
        private Series<double> macdAvg;
        private Series<double> macdDiff;
        // Variables para Bollinger sobre MACD
        private Series<double> bbUpper;
        private Series<double> bbLower;
        private Series<double> bbMiddle;
        #endregion

        #region Variables PreMarket & Visuals
        private bool altoBajoFlecha;
        private bool altoBajoDiamante;
        private bool drawAPre;
        private bool flagTextPre;
        private bool flagTextPreUpdateDate;
        private bool flagTextPreVol;

        private int indexStartDay;
        private int indexFinalDay;
        private int arrowsEveryDlastD;
        private int diamondsEveryDlastD;
        private int contAPre;
        private int dayCurrentBar;
        private int tomorrowCurrentBar;

        private double textPreVol;
        
        private DateTime myFechaFinalDia;
        private DateTime myToDay;
        private DateTime myTomorrow;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Indicador Maestro EduS_Trader: Keltner + PreMarket + MACD Logic + InfoPanel. Optimizado sin logos y 100% nativo.";
                Name = "EduS_Master_Indicator";
                Calculate = Calculate.OnEachTick; 
                IsOverlay = true; // Se dibuja sobre el precio
                DisplayInDataBox = true;
                DrawOnPricePanel = true;
                DrawHorizontalGridLines = true;
                DrawVerticalGridLines = true;
                PaintPriceMarkers = true;
                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                // --- Defaults Keltner ---
                Show_KeltChannel = true;
                Show_LR = true;
                Show_SMA_1 = false;
                Show_SMA_2 = false;
                Show_EMADot = false;
                Show_ChannelFill = false;
                Color_ChannelFill = Brushes.White;
                Opacity_ChannelFill = 10;
                Period = 52;
                OffsetMultiplier = 3.5;
                Period_LR = 89;
                Period_SMA_1 = 20;
                Period_SMA_2 = 80;
                Period_EMADot = 1;

                // --- Defaults MACD BB Logic (Interno) ---
                // Estos valores controlan la lógica de las flechas
                MACD_Fast = 12;
                MACD_Slow = 26;
                MACD_Smooth = 5; // Signal smoothing
                MACD_BB_Period = 10;
                MACD_BB_StdDev = 1.0;

                // --- Defaults InfoPanel ---
                Show_InfoMarket = true;
                Color_InfoMarket = Brushes.Silver;
                Show_Position = TextPosition.BottomRight;
                CountDown = true;
                ShowPercent = false;
                ATR_Period = 14;

                // --- Defaults PreMarket ---
                Show_Arrows = false;
                Show_Diamonds = false;
                Show_ArrowsEvery_Last = false;
                Show_DiamondsEvery_Last = false;
                Show_AreaPre = true;
                Show_MaxMinPastDay = false;
                Show_InfoPreM = false;
                
                Long_Arrows = 4;
                Width_Arrows = 1;
                Width_MaxMinPastDay = 2;
                Style_Arrows = DashStyleHelper.Solid;
                Style_MaxMinPastDay = DashStyleHelper.Dash;
                Color_ArrowUp = Brushes.CornflowerBlue;
                Color_MaxPastDay = Brushes.CornflowerBlue;
                Color_ArrowDown = Brushes.Red;
                Color_MinPastDay = Brushes.Red;
                Color_DiamondUp = Brushes.Blue;
                Color_DiamondDown = Brushes.Red;
                
                // Horarios
                FinalDay = DateTime.Parse("16:00", System.Globalization.CultureInfo.InvariantCulture);
                StartDay = DateTime.Parse("09:30", System.Globalization.CultureInfo.InvariantCulture);
                Hora_InfoPreM = DateTime.Parse("09:25");
                
                // Visuals PreMarket
                myAreaOpacity = 25;
                ColorFondo = Brushes.DimGray;
                ColorBorde = Brushes.Transparent;

                // Variables internas inicialización
                lastBar = 0;
                vlmDaily = 0;
                cicloBar = 0;
                cicloTime = 0;
                flagTextVolPB = true;
                flagTextPre = true;
                flagTextPreUpdateDate = true;
                flagTextPreVol = false;
                textPreVol = 0;
                altoBajoFlecha = true;
                altoBajoDiamante = true;
                drawAPre = false;
                contAPre = 0;

                // Definición de Plots (Visuales)
                AddPlot(Brushes.Cornsilk, "UpperBand");
                AddPlot(Brushes.Snow, "Midline");
                AddPlot(Brushes.Cornsilk, "LowerBand");
                AddPlot(new Stroke(Brushes.Gold, 2), PlotStyle.Line, "LR");
                AddPlot(Brushes.OrangeRed, "SMA_1");
                AddPlot(new Stroke(Brushes.Purple, 2), PlotStyle.Line, "SMA_2");
                AddPlot(new Stroke(Brushes.Snow, DashStyleHelper.Dot, 2), PlotStyle.Dot, "EMA_Dot");
            }
            else if (State == State.Configure)
            {
                // Inicialización Series Keltner
                diff = new Series<double>(this);
                emaDiff = EMA(diff, Period);
                emaTypical = EMA(Typical, Period);

                // Inicialización Series MACD (Motor interno)
                macdSeries = new Series<double>(this);
                macdAvg = new Series<double>(this);
                macdDiff = new Series<double>(this);
                bbUpper = new Series<double>(this);
                bbLower = new Series<double>(this);
                bbMiddle = new Series<double>(this);

                // Inicialización PreMarket
                myTomorrow = Bars.LastBarTime;
                SetZOrder(-100); // Enviar al fondo para no tapar velas
            }
            else if (State == State.DataLoaded)
            {
                cicloBar = 0;
                cicloTime = 0;
                
                atrAlgo = ATR(ATR_Period);
                
                if (Bars.Count > 0)
                {
                    try {
                        DateTime targetDate = Bars.LastBarTime.AddDays(-1);
                        arrowsEveryDlastD = ChartBars.GetBarIdxByTime(ChartControl, new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, FinalDay.Hour, FinalDay.Minute, FinalDay.Second));
                        diamondsEveryDlastD = arrowsEveryDlastD;
                    } catch { 
                        arrowsEveryDlastD = 0; 
                        diamondsEveryDlastD = 0;
                    }
                }
            }
        }

        protected override void OnBarUpdate()
        {
            // Seguridad para cálculo de indicadores
            if (CurrentBar < Period_SMA_2 || CurrentBar < Period_LR || CurrentBar < MACD_Slow) return;

            // ==========================================
            // 1. MOTOR MACD (Cálculo Matemático Interno)
            // ==========================================
            // Replicamos la lógica exacta del indicador MDCMACDBBlines
            double fastEma = EMA(Input, MACD_Fast)[0];
            double slowEma = EMA(Input, MACD_Slow)[0];
            
            macdSeries[0] = fastEma - slowEma; // Valor MACD
            macdAvg[0]    = EMA(macdSeries, MACD_Smooth)[0]; // Signal Line
            macdDiff[0]   = macdSeries[0] - macdAvg[0]; // Histograma

            // Bollinger Bands sobre el MACD
            double bbStdDevVal = StdDev(macdSeries, MACD_BB_Period)[0];
            double bbSmaVal    = SMA(macdSeries, MACD_BB_Period)[0];

            bbUpper[0]  = bbSmaVal + (bbStdDevVal * MACD_BB_StdDev);
            bbLower[0]  = bbSmaVal - (bbStdDevVal * MACD_BB_StdDev);
            bbMiddle[0] = bbSmaVal;

            // ==========================================
            // 2. CÁLCULOS KELTNER CHANNEL
            // ==========================================
            diff[0] = High[0] - Low[0];
            double middle = emaTypical[0];
            double offset = emaDiff[0] * OffsetMultiplier;
            double upper = middle + offset;
            double lower = middle - offset;

            if (Show_KeltChannel)
            {
                Midline[0] = middle;
                UpperBand[0] = upper;
                LowerBand[0] = lower;
            }
            if (Show_LR) LR[0] = LinReg(Input, Period_LR)[0];
            if (Show_SMA_1) SMA_1[0] = SMA(Input, Period_SMA_1)[0];
            if (Show_SMA_2) SMA_2[0] = SMA(Input, Period_SMA_2)[0];
            if (Show_EMADot) EMA_Dot[0] = EMA(Input, Period_EMADot)[0];

            if (Show_ChannelFill)
                Draw.Region(this, @"MDC_Channel_Fill", CurrentBar, 0, UpperBand, LowerBand, null, Color_ChannelFill, Opacity_ChannelFill);


            // ==========================================
            // 3. LOGICA PRE-MARKET (Zonas)
            // ==========================================
            DateTime barTime = ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar);
            
            if (barTime.Hour >= FinalDay.Hour && !drawAPre)
            {
                myFechaFinalDia = barTime;
                contAPre++;
                drawAPre = true;
            }
            if ((barTime.Hour == StartDay.Hour) && (barTime.Minute >= StartDay.Minute) && (barTime.Second >= 00))
            {
                drawAPre = false;
            }
            if (drawAPre && Show_AreaPre)
            {
                Draw.RegionHighlightX(this, @"MDCPreMarket AreaPre" + contAPre, myFechaFinalDia, barTime, ColorBorde, ColorFondo, myAreaOpacity);
            }

            // ==========================================
            // 4. LOGICA SEÑALES (Flechas y Diamantes)
            // ==========================================
            
            // --- FLECHAS ---
            if (Show_Arrows)
            {
                // Lógica de Compra: Precio > Midline, Precio > Regresión, y MACD > 0
                // Usamos macdSeries[0] que acabamos de calcular internamente
                if ((Low[0] > Midline[0]) 
                    && (Low[0] > LR[0]) 
                    && (macdSeries[0] > 0) 
                    && altoBajoFlecha == true)
                {
                    if ((CurrentBar >= arrowsEveryDlastD) && !Show_ArrowsEvery_Last)
                        Draw.ArrowLine(this, "MDC_ArrLast_1" + CurrentBar, false, 0, High[0] + 0.1, 0, High[0] + Long_Arrows, Color_ArrowUp, Style_Arrows, Width_Arrows, true);
                    else if (Show_ArrowsEvery_Last)
                        Draw.ArrowLine(this, "MDC_ArrEvery_1" + CurrentBar, false, 0, High[0] + 0.1, 0, High[0] + Long_Arrows, Color_ArrowUp, Style_Arrows, Width_Arrows, true);
                    
                    altoBajoFlecha = false;
                }
                
                // Lógica de Venta
                if ((High[0] < Midline[0]) 
                    && (High[0] < LR[0]) 
                    && (macdSeries[0] < 0) 
                    && altoBajoFlecha == false)
                {
                    if ((CurrentBar >= arrowsEveryDlastD) && !Show_ArrowsEvery_Last)
                        Draw.ArrowLine(this, "MDC_ArrLast_2" + CurrentBar, false, 0, Low[0] - 0.1, 0, Low[0] - Long_Arrows, Color_ArrowDown, Style_Arrows, Width_Arrows, true);
                    else if (Show_ArrowsEvery_Last)
                        Draw.ArrowLine(this, "MDC_ArrEvery_2" + CurrentBar, false, 0, Low[0] - 0.1, 0, Low[0] - Long_Arrows, Color_ArrowDown, Style_Arrows, Width_Arrows, true);
                    
                    altoBajoFlecha = true;
                }
            }

            // --- DIAMANTES (Cruce de SMAs) ---
            if (Show_Diamonds)
            {
                if (CrossAbove(SMA_1, SMA_2, 1) && altoBajoDiamante == true)
                {
                    if ((CurrentBar >= diamondsEveryDlastD) && !Show_DiamondsEvery_Last)
                        Draw.Diamond(this, @"MDC_DiaLast_1" + CurrentBar, false, 0, High[0] + 1.0, Color_DiamondUp, true);
                    else if (Show_DiamondsEvery_Last)
                        Draw.Diamond(this, @"MDC_DiaEvery_1" + CurrentBar, false, 0, High[0] + 1.0, Color_DiamondUp, true);
                    
                    altoBajoDiamante = false;
                }
                if (CrossBelow(SMA_1, SMA_2, 1) && altoBajoDiamante == false)
                {
                    if ((CurrentBar >= diamondsEveryDlastD) && !Show_DiamondsEvery_Last)
                        Draw.Diamond(this, @"MDC_DiaLast_2" + CurrentBar, false, 0, Low[0] - 1.0, Color_DiamondDown, true);
                    else if (Show_DiamondsEvery_Last)
                        Draw.Diamond(this, @"MDC_DiaEvery_2" + CurrentBar, false, 0, Low[0] - 1.0, Color_DiamondDown, true);
                    
                    altoBajoDiamante = true;
                }
            }

            // ==========================================
            // 5. MAXIMOS/MINIMOS DIA ANTERIOR & INFOPANEL TEXT
            // ==========================================
            if (BarsInProgress == 0) 
            {
                myToDay = Bars.LastBarTime;
                textPreVol += Volume[0];
                dayCurrentBar = barTime.Day;

                if (flagTextPreUpdateDate)
                {
                    tomorrowCurrentBar = barTime.AddDays(barTime.DayOfWeek.Equals(DayOfWeek.Friday) ? 3 : 1).Day;
                    flagTextPreUpdateDate = false;
                }
                
                if (dayCurrentBar == tomorrowCurrentBar)
                {
                    flagTextPre = true;
                    flagTextPreUpdateDate = true;
                    flagTextPreVol = true;
                }
                if (barTime.Hour == 18 && barTime.Minute >= 00 && flagTextPreVol)
                {
                    textPreVol = Volume[0];
                    flagTextPreVol = false;
                }

                // Info Pre-Market Texto Flotante
                if (barTime.Hour == Hora_InfoPreM.Hour && 
                    barTime.Minute >= Hora_InfoPreM.Minute && 
                    flagTextPre && Show_InfoPreM)
                {
                    flagTextPre = false;
                    try
                    {
                        Draw.Text(this, @"MDCPreMarket TextPre" + CurrentBar, true,
                                        "Hora: " + Hora_InfoPreM.Hour + ":" + Hora_InfoPreM.Minute +
                                        "\nVolumen: " + String.Format("{0:#,###,000}", textPreVol) +
                                        "\nBandasSp: " + String.Format("{0:#.##}", UpperBand[0] - LowerBand[0]),
                                        0, UpperBand[0] + 3.0, 0, null, null, TextAlignment.Left, null, null, 100);
                    }
                    catch { }
                }

                // Cálculo Máximos y Mínimos Pasados
                if (myToDay.Day == myTomorrow.Day && Show_MaxMinPastDay)
                {
                    DateTime auxDateTime = barTime.AddDays(-1);
                    int safeLookBack = 1;
                    while (auxDateTime.DayOfWeek == DayOfWeek.Saturday || auxDateTime.DayOfWeek == DayOfWeek.Sunday)
                    {
                        safeLookBack++;
                        auxDateTime = barTime.AddDays(-safeLookBack);
                    }

                    indexStartDay = ChartBars.GetBarIdxByTime(ChartControl, new DateTime(auxDateTime.Year, auxDateTime.Month, auxDateTime.Day, StartDay.Hour, StartDay.Minute, 0));
                    indexFinalDay = ChartBars.GetBarIdxByTime(ChartControl, new DateTime(auxDateTime.Year, auxDateTime.Month, auxDateTime.Day, FinalDay.Hour, FinalDay.Minute, 0));
                    
                    if(indexStartDay > 0 && indexFinalDay > indexStartDay)
                        maxMinLast();
                        
                    myTomorrow = Bars.LastBarTime.AddDays(1);
                    flagTextPre = true;
                }
            }

            // ==========================================
            // 6. INFOPANEL (Esquina Inferior)
            // ==========================================
            if (Show_InfoMarket && IsVisible)
            {
                double atrValue = atrAlgo[0];
                string atrString = Instrument.MasterInstrument.FormatPrice(atrValue);

                double periodValue = (BarsPeriod.BarsPeriodType == BarsPeriodType.Tick) ? BarsPeriod.Value : BarsPeriod.BaseBarsPeriodValue;
                double tickCount = ShowPercent ? CountDown ? (1 - Bars.PercentComplete) * 100 : Bars.PercentComplete * 100 : CountDown ? periodValue - Bars.TickCount : Bars.TickCount;
                string tick1 = (BarsPeriod.BarsPeriodType == BarsPeriodType.Tick 
                            || ((BarsPeriod.BarsPeriodType == BarsPeriodType.HeikenAshi || BarsPeriod.BarsPeriodType == BarsPeriodType.Volumetric) && BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Tick) ? ((CountDown
                                            ? NinjaTrader.Custom.Resource.TickCounterTicksRemaining + tickCount : NinjaTrader.Custom.Resource.TickCounterTickCount + tickCount) + (ShowPercent ? "%" : ""))
                                            : NinjaTrader.Custom.Resource.TickCounterBarError);

                try {
                    vlmDaily = Instrument.MarketData.DailyVolume.Volume;
                } catch {
                    if (lastBar != CurrentBar) {
                        vlmDaily += Bars.GetVolume(lastBar);
                        lastBar = CurrentBar;
                    }
                }

                int currentMinute = Bars.GetTime(CurrentBar).Minute;
                if (currentMinute != cicloTime)
                {
                    speedBar = CurrentBar - cicloBar;
                    cicloTime = currentMinute;
                    cicloBar = CurrentBar;
                }

                try {
                    Draw.TextFixed(this, @"MDC_InfoPanel", 
                        "BandSp = " + ((int)((upper - lower) * 100)) / 100.0 +
                        "\nVolumen = " + String.Format("{0:#,###,000}", vlmDaily) +
                        "\n" + tick1 +
                        "\nBars/min = " + speedBar +
                        "\nATR (" + ATR_Period + "): " + atrString +
                        "\nHora = " + String.Format("{0:00}", barTime.Hour) + ":" + String.Format("{0:00}", barTime.Minute) + ":" + String.Format("{0:00}", barTime.Second),
                        Show_Position, Color_InfoMarket, ChartControl.Properties.LabelFont, Brushes.Transparent, Brushes.Transparent, 0);
                } catch { }
            }
        }

        protected void maxMinLast()
        {
            if (indexStartDay < 0 || indexFinalDay > Bars.Count - 1) return;
            
            double priceMax = double.MinValue;
            double priceMin = double.MaxValue;

            for (int i = indexStartDay; i <= indexFinalDay; i++)
            {
                priceMax = Math.Max(priceMax, Bars.GetHigh(i));
                priceMin = Math.Min(priceMin, Bars.GetLow(i));
            }
            Draw.HorizontalLine(this, @"MDCPreMarket MaxLast", priceMax, Color_MaxPastDay, Style_MaxMinPastDay, Width_MaxMinPastDay);
            Draw.HorizontalLine(this, @"MDCPreMarket MinLast", priceMin, Color_MinPastDay, Style_MaxMinPastDay, Width_MaxMinPastDay);
        }

        #region Properties
        // --- 1. Keltner Logic ---
        [NinjaScriptProperty]
        [Display(Name = "Show_KeltChannel", Order = 1, GroupName = "1. Keltner Logic")]
        public bool Show_KeltChannel { get; set; }
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Period", Order = 2, GroupName = "1. Keltner Logic")]
        public int Period { get; set; }
        [NinjaScriptProperty]
        [Range(1, double.MaxValue)]
        [Display(Name = "OffsetMultiplier", Order = 3, GroupName = "1. Keltner Logic")]
        public double OffsetMultiplier { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Show_LR", Order = 4, GroupName = "1. Keltner Logic")]
        public bool Show_LR { get; set; }
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Period_LR", Order = 5, GroupName = "1. Keltner Logic")]
        public int Period_LR { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Show_SMA_1", Order = 6, GroupName = "1. Keltner Logic")]
        public bool Show_SMA_1 { get; set; }
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Period_SMA_1", Order = 7, GroupName = "1. Keltner Logic")]
        public int Period_SMA_1 { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Show_SMA_2", Order = 8, GroupName = "1. Keltner Logic")]
        public bool Show_SMA_2 { get; set; }
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Period_SMA_2", Order = 9, GroupName = "1. Keltner Logic")]
        public int Period_SMA_2 { get; set; }
        [NinjaScriptProperty]
        [Display(Name = "Show_EMADot", Order = 10, GroupName = "1. Keltner Logic")]
        public bool Show_EMADot { get; set; }
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Period_EMADot", Order = 11, GroupName = "1. Keltner Logic")]
        public int Period_EMADot { get; set; }
        [NinjaScriptProperty]
        [Display(Name="Show_ChannelFill", Order=12, GroupName="1. Keltner Logic")]
        public bool Show_ChannelFill { get; set; }
        [XmlIgnore]
        [Display(Name="Color_ChannelFill", Order=13, GroupName="1. Keltner Logic")]        
        public Brush Color_ChannelFill { get; set; }
        [Browsable(false)]
        public string Color_ChannelFillSerialize { get { return Serialize.BrushToString(this.Color_ChannelFill); } set { this.Color_ChannelFill = Serialize.StringToBrush(value); } }
        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name="Opacity_ChannelFill", Order=14, GroupName="1. Keltner Logic")]
        public int Opacity_ChannelFill { get; set; }

        // --- 2. MACD Logic (Nuevo Grupo) ---
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="MACD Fast", Order=1, GroupName="2. MACD Logic (Internal)")]
        public int MACD_Fast { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="MACD Slow", Order=2, GroupName="2. MACD Logic (Internal)")]
        public int MACD_Slow { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="MACD Smooth", Order=3, GroupName="2. MACD Logic (Internal)")]
        public int MACD_Smooth { get; set; }
        
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="MACD BB Period", Order=4, GroupName="2. MACD Logic (Internal)")]
        public int MACD_BB_Period { get; set; }
        
        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name="MACD BB StdDev", Order=5, GroupName="2. MACD Logic (Internal)")]
        public double MACD_BB_StdDev { get; set; }

        // --- 3. Info Market ---
        [NinjaScriptProperty]
        [Display(Name = "Show_InfoMarket", Order = 1, GroupName = "3. Info Market")]
        public bool Show_InfoMarket { get; set; }
        [NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "CountDown" + "Ticks", Order = 2, GroupName = "3. Info Market")]
        public bool CountDown { get; set; }
        [NinjaScriptProperty]
        [Display(ResourceType = typeof(Custom.Resource), Name = "ShowPercent" + "Ticks", Order = 3, GroupName = "3. Info Market")]
        public bool ShowPercent { get; set; }
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Periodo ATR", Description = "Periodo para ATR en el Info Market", Order = 4, GroupName = "3. Info Market")]
        public int ATR_Period { get; set; }
        [XmlIgnore]
        [Display(Name = "Color_InfoMarket", Order = 5, GroupName = "3. Info Market")]
        public Brush Color_InfoMarket { get; set; }
        [Browsable(false)]
        public string Color_InfoMarketSerialize { get { return Serialize.BrushToString(this.Color_InfoMarket); } set { this.Color_InfoMarket = Serialize.StringToBrush(value); } }
        [NinjaScriptProperty]
        [Display(Name = "Show_Position", Order = 6, GroupName = "3. Info Market")]
        public TextPosition Show_Position { get; set; }

        // --- 4. Pre-Market ---
        [NinjaScriptProperty]
        [Display(Name = "Show_AreaPre", Order = 1, GroupName = "4. Pre-Market")]
        public bool Show_AreaPre { get; set; }
        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "StartDay", Order = 2, GroupName = "4. Pre-Market")]
        public DateTime StartDay { get; set; }
        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name = "FinalDay", Order = 3, GroupName = "4. Pre-Market")]
        public DateTime FinalDay { get; set; }
        [XmlIgnore]
        [Display(Name="ColorFondo", Order=4, GroupName="4. Pre-Market")]        
        public Brush ColorFondo { get; set;}
        [Browsable(false)]
        public string ColorFondoSerialize { get { return Serialize.BrushToString(this.ColorFondo); } set { this.ColorFondo = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name="ColorBorde", Order=5, GroupName="4. Pre-Market")]        
        public Brush ColorBorde { get; set; }
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name="myAreaOpacity", Order=6, GroupName="4. Pre-Market")]
        public int myAreaOpacity { get; set; }
        
        // --- 5. Signals ---
        [NinjaScriptProperty]
        [Display(Name="Show_Arrows", Order=1, GroupName="5. Signals")]
        public bool Show_Arrows { get; set; }
        [NinjaScriptProperty]
        [Display(Name="Show_Diamonds", Order=2, GroupName="5. Signals")]
        public bool Show_Diamonds { get; set; }
        [NinjaScriptProperty]
        [Display(Name="Show_ArrowsEvery_Last", Order=3, GroupName="5. Signals")]
        public bool Show_ArrowsEvery_Last { get; set; }
        [NinjaScriptProperty]
        [Display(Name="Show_DiamondsEvery_Last", Order=4, GroupName="5. Signals")]
        public bool Show_DiamondsEvery_Last { get; set; }
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Long_Arrows", Order=5, GroupName="5. Signals")]
        public int Long_Arrows { get; set; }
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Width_Arrows", Order=6, GroupName="5. Signals")]
        public int Width_Arrows { get; set; }
        [NinjaScriptProperty]
        [Display(Name="Style_Arrows", Order=7, GroupName="5. Signals")]        
        public DashStyleHelper Style_Arrows { get; set; }
        [XmlIgnore]
        [Display(Name="Color_ArrowUp", Order=8, GroupName="5. Signals")]        
        public Brush Color_ArrowUp { get; set; }
        [Browsable(false)]
        public string Color_ArrowUpSerialize { get { return Serialize.BrushToString(this.Color_ArrowUp); } set { this.Color_ArrowUp = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name="Color_ArrowDown", Order=9, GroupName="5. Signals")]        
        public Brush Color_ArrowDown { get; set; }
        [Browsable(false)]
        public string Color_ArrowDownSerialize { get { return Serialize.BrushToString(this.Color_ArrowDown); } set { this.Color_ArrowDown = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name="Color_DiamondUp", Order=10, GroupName="5. Signals")]        
        public Brush Color_DiamondUp { get; set; }
        [Browsable(false)]
        public string Color_DiamondUpSerialize { get { return Serialize.BrushToString(this.Color_DiamondUp); } set { this.Color_DiamondUp = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name="Color_DiamondDown", Order=11, GroupName="5. Signals")]        
        public Brush Color_DiamondDown { get; set; }
        [Browsable(false)]
        public string Color_DiamondDownSerialize { get { return Serialize.BrushToString(this.Color_DiamondDown); } set { this.Color_DiamondDown = Serialize.StringToBrush(value); } }

        // --- 6. Max/Min History ---
        [NinjaScriptProperty]
        [Display(Name="Show_MaxMinPastDay", Order=1, GroupName="6. Max/Min History")]
        public bool Show_MaxMinPastDay { get; set; }
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name="Width_MaxMinPastDay", Order=2, GroupName="6. Max/Min History")]
        public int Width_MaxMinPastDay { get; set; }
        [NinjaScriptProperty]
        [Display(Name="Style_MaxMinPastDay", Order=3, GroupName="6. Max/Min History")]        
        public DashStyleHelper Style_MaxMinPastDay { get; set; }
        [XmlIgnore]
        [Display(Name="Color_MaxPastDay", Order=4, GroupName="6. Max/Min History")]        
        public Brush Color_MaxPastDay { get; set; }
        [Browsable(false)]
        public string Color_MaxPastDaySerialize { get { return Serialize.BrushToString(this.Color_MaxPastDay); } set { this.Color_MaxPastDay = Serialize.StringToBrush(value); } }
        [XmlIgnore]
        [Display(Name="Color_MinPastDay", Order=5, GroupName="6. Max/Min History")]        
        public Brush Color_MinPastDay { get; set; }
        [Browsable(false)]
        public string Color_MinPastDaySerialize { get { return Serialize.BrushToString(this.Color_MinPastDay); } set { this.Color_MinPastDay = Serialize.StringToBrush(value); } }
        
        // --- 7. Extra Info ---
        [NinjaScriptProperty]
        [Display(Name="Show_InfoPreM", Order=1, GroupName="7. Extra Visuals")]
        public bool Show_InfoPreM { get; set; }
        [NinjaScriptProperty]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
        [Display(Name="Hora_InfoPreM", Order=2, GroupName="7. Extra Visuals")]
        public DateTime Hora_InfoPreM { get; set; }

        // --- Outputs (Plots) ---
        [Browsable(false)] [XmlIgnore] public Series<double> UpperBand { get { return Values[0]; } }
        [Browsable(false)] [XmlIgnore] public Series<double> Midline { get { return Values[1]; } }
        [Browsable(false)] [XmlIgnore] public Series<double> LowerBand { get { return Values[2]; } }
        [Browsable(false)] [XmlIgnore] public Series<double> LR { get { return Values[3]; } }
        [Browsable(false)] [XmlIgnore] public Series<double> SMA_1 { get { return Values[4]; } }
        [Browsable(false)] [XmlIgnore] public Series<double> SMA_2 { get { return Values[5]; } }
        [Browsable(false)] [XmlIgnore] public Series<double> EMA_Dot { get { return Values[6]; } }
        
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduS_Master_Indicator[] cacheEduS_Master_Indicator;
		public EduS_Trader.EduS_Master_Indicator EduS_Master_Indicator(bool show_KeltChannel, int period, double offsetMultiplier, bool show_LR, int period_LR, bool show_SMA_1, int period_SMA_1, bool show_SMA_2, int period_SMA_2, bool show_EMADot, int period_EMADot, bool show_ChannelFill, int opacity_ChannelFill, int mACD_Fast, int mACD_Slow, int mACD_Smooth, int mACD_BB_Period, double mACD_BB_StdDev, bool show_InfoMarket, bool countDown, bool showPercent, int aTR_Period, TextPosition show_Position, bool show_AreaPre, DateTime startDay, DateTime finalDay, int myAreaOpacity, bool show_Arrows, bool show_Diamonds, bool show_ArrowsEvery_Last, bool show_DiamondsEvery_Last, int long_Arrows, int width_Arrows, DashStyleHelper style_Arrows, bool show_MaxMinPastDay, int width_MaxMinPastDay, DashStyleHelper style_MaxMinPastDay, bool show_InfoPreM, DateTime hora_InfoPreM)
		{
			return EduS_Master_Indicator(Input, show_KeltChannel, period, offsetMultiplier, show_LR, period_LR, show_SMA_1, period_SMA_1, show_SMA_2, period_SMA_2, show_EMADot, period_EMADot, show_ChannelFill, opacity_ChannelFill, mACD_Fast, mACD_Slow, mACD_Smooth, mACD_BB_Period, mACD_BB_StdDev, show_InfoMarket, countDown, showPercent, aTR_Period, show_Position, show_AreaPre, startDay, finalDay, myAreaOpacity, show_Arrows, show_Diamonds, show_ArrowsEvery_Last, show_DiamondsEvery_Last, long_Arrows, width_Arrows, style_Arrows, show_MaxMinPastDay, width_MaxMinPastDay, style_MaxMinPastDay, show_InfoPreM, hora_InfoPreM);
		}

		public EduS_Trader.EduS_Master_Indicator EduS_Master_Indicator(ISeries<double> input, bool show_KeltChannel, int period, double offsetMultiplier, bool show_LR, int period_LR, bool show_SMA_1, int period_SMA_1, bool show_SMA_2, int period_SMA_2, bool show_EMADot, int period_EMADot, bool show_ChannelFill, int opacity_ChannelFill, int mACD_Fast, int mACD_Slow, int mACD_Smooth, int mACD_BB_Period, double mACD_BB_StdDev, bool show_InfoMarket, bool countDown, bool showPercent, int aTR_Period, TextPosition show_Position, bool show_AreaPre, DateTime startDay, DateTime finalDay, int myAreaOpacity, bool show_Arrows, bool show_Diamonds, bool show_ArrowsEvery_Last, bool show_DiamondsEvery_Last, int long_Arrows, int width_Arrows, DashStyleHelper style_Arrows, bool show_MaxMinPastDay, int width_MaxMinPastDay, DashStyleHelper style_MaxMinPastDay, bool show_InfoPreM, DateTime hora_InfoPreM)
		{
			if (cacheEduS_Master_Indicator != null)
				for (int idx = 0; idx < cacheEduS_Master_Indicator.Length; idx++)
					if (cacheEduS_Master_Indicator[idx] != null && cacheEduS_Master_Indicator[idx].Show_KeltChannel == show_KeltChannel && cacheEduS_Master_Indicator[idx].Period == period && cacheEduS_Master_Indicator[idx].OffsetMultiplier == offsetMultiplier && cacheEduS_Master_Indicator[idx].Show_LR == show_LR && cacheEduS_Master_Indicator[idx].Period_LR == period_LR && cacheEduS_Master_Indicator[idx].Show_SMA_1 == show_SMA_1 && cacheEduS_Master_Indicator[idx].Period_SMA_1 == period_SMA_1 && cacheEduS_Master_Indicator[idx].Show_SMA_2 == show_SMA_2 && cacheEduS_Master_Indicator[idx].Period_SMA_2 == period_SMA_2 && cacheEduS_Master_Indicator[idx].Show_EMADot == show_EMADot && cacheEduS_Master_Indicator[idx].Period_EMADot == period_EMADot && cacheEduS_Master_Indicator[idx].Show_ChannelFill == show_ChannelFill && cacheEduS_Master_Indicator[idx].Opacity_ChannelFill == opacity_ChannelFill && cacheEduS_Master_Indicator[idx].MACD_Fast == mACD_Fast && cacheEduS_Master_Indicator[idx].MACD_Slow == mACD_Slow && cacheEduS_Master_Indicator[idx].MACD_Smooth == mACD_Smooth && cacheEduS_Master_Indicator[idx].MACD_BB_Period == mACD_BB_Period && cacheEduS_Master_Indicator[idx].MACD_BB_StdDev == mACD_BB_StdDev && cacheEduS_Master_Indicator[idx].Show_InfoMarket == show_InfoMarket && cacheEduS_Master_Indicator[idx].CountDown == countDown && cacheEduS_Master_Indicator[idx].ShowPercent == showPercent && cacheEduS_Master_Indicator[idx].ATR_Period == aTR_Period && cacheEduS_Master_Indicator[idx].Show_Position == show_Position && cacheEduS_Master_Indicator[idx].Show_AreaPre == show_AreaPre && cacheEduS_Master_Indicator[idx].StartDay == startDay && cacheEduS_Master_Indicator[idx].FinalDay == finalDay && cacheEduS_Master_Indicator[idx].myAreaOpacity == myAreaOpacity && cacheEduS_Master_Indicator[idx].Show_Arrows == show_Arrows && cacheEduS_Master_Indicator[idx].Show_Diamonds == show_Diamonds && cacheEduS_Master_Indicator[idx].Show_ArrowsEvery_Last == show_ArrowsEvery_Last && cacheEduS_Master_Indicator[idx].Show_DiamondsEvery_Last == show_DiamondsEvery_Last && cacheEduS_Master_Indicator[idx].Long_Arrows == long_Arrows && cacheEduS_Master_Indicator[idx].Width_Arrows == width_Arrows && cacheEduS_Master_Indicator[idx].Style_Arrows == style_Arrows && cacheEduS_Master_Indicator[idx].Show_MaxMinPastDay == show_MaxMinPastDay && cacheEduS_Master_Indicator[idx].Width_MaxMinPastDay == width_MaxMinPastDay && cacheEduS_Master_Indicator[idx].Style_MaxMinPastDay == style_MaxMinPastDay && cacheEduS_Master_Indicator[idx].Show_InfoPreM == show_InfoPreM && cacheEduS_Master_Indicator[idx].Hora_InfoPreM == hora_InfoPreM && cacheEduS_Master_Indicator[idx].EqualsInput(input))
						return cacheEduS_Master_Indicator[idx];
			return CacheIndicator<EduS_Trader.EduS_Master_Indicator>(new EduS_Trader.EduS_Master_Indicator(){ Show_KeltChannel = show_KeltChannel, Period = period, OffsetMultiplier = offsetMultiplier, Show_LR = show_LR, Period_LR = period_LR, Show_SMA_1 = show_SMA_1, Period_SMA_1 = period_SMA_1, Show_SMA_2 = show_SMA_2, Period_SMA_2 = period_SMA_2, Show_EMADot = show_EMADot, Period_EMADot = period_EMADot, Show_ChannelFill = show_ChannelFill, Opacity_ChannelFill = opacity_ChannelFill, MACD_Fast = mACD_Fast, MACD_Slow = mACD_Slow, MACD_Smooth = mACD_Smooth, MACD_BB_Period = mACD_BB_Period, MACD_BB_StdDev = mACD_BB_StdDev, Show_InfoMarket = show_InfoMarket, CountDown = countDown, ShowPercent = showPercent, ATR_Period = aTR_Period, Show_Position = show_Position, Show_AreaPre = show_AreaPre, StartDay = startDay, FinalDay = finalDay, myAreaOpacity = myAreaOpacity, Show_Arrows = show_Arrows, Show_Diamonds = show_Diamonds, Show_ArrowsEvery_Last = show_ArrowsEvery_Last, Show_DiamondsEvery_Last = show_DiamondsEvery_Last, Long_Arrows = long_Arrows, Width_Arrows = width_Arrows, Style_Arrows = style_Arrows, Show_MaxMinPastDay = show_MaxMinPastDay, Width_MaxMinPastDay = width_MaxMinPastDay, Style_MaxMinPastDay = style_MaxMinPastDay, Show_InfoPreM = show_InfoPreM, Hora_InfoPreM = hora_InfoPreM }, input, ref cacheEduS_Master_Indicator);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduS_Master_Indicator EduS_Master_Indicator(bool show_KeltChannel, int period, double offsetMultiplier, bool show_LR, int period_LR, bool show_SMA_1, int period_SMA_1, bool show_SMA_2, int period_SMA_2, bool show_EMADot, int period_EMADot, bool show_ChannelFill, int opacity_ChannelFill, int mACD_Fast, int mACD_Slow, int mACD_Smooth, int mACD_BB_Period, double mACD_BB_StdDev, bool show_InfoMarket, bool countDown, bool showPercent, int aTR_Period, TextPosition show_Position, bool show_AreaPre, DateTime startDay, DateTime finalDay, int myAreaOpacity, bool show_Arrows, bool show_Diamonds, bool show_ArrowsEvery_Last, bool show_DiamondsEvery_Last, int long_Arrows, int width_Arrows, DashStyleHelper style_Arrows, bool show_MaxMinPastDay, int width_MaxMinPastDay, DashStyleHelper style_MaxMinPastDay, bool show_InfoPreM, DateTime hora_InfoPreM)
		{
			return indicator.EduS_Master_Indicator(Input, show_KeltChannel, period, offsetMultiplier, show_LR, period_LR, show_SMA_1, period_SMA_1, show_SMA_2, period_SMA_2, show_EMADot, period_EMADot, show_ChannelFill, opacity_ChannelFill, mACD_Fast, mACD_Slow, mACD_Smooth, mACD_BB_Period, mACD_BB_StdDev, show_InfoMarket, countDown, showPercent, aTR_Period, show_Position, show_AreaPre, startDay, finalDay, myAreaOpacity, show_Arrows, show_Diamonds, show_ArrowsEvery_Last, show_DiamondsEvery_Last, long_Arrows, width_Arrows, style_Arrows, show_MaxMinPastDay, width_MaxMinPastDay, style_MaxMinPastDay, show_InfoPreM, hora_InfoPreM);
		}

		public Indicators.EduS_Trader.EduS_Master_Indicator EduS_Master_Indicator(ISeries<double> input , bool show_KeltChannel, int period, double offsetMultiplier, bool show_LR, int period_LR, bool show_SMA_1, int period_SMA_1, bool show_SMA_2, int period_SMA_2, bool show_EMADot, int period_EMADot, bool show_ChannelFill, int opacity_ChannelFill, int mACD_Fast, int mACD_Slow, int mACD_Smooth, int mACD_BB_Period, double mACD_BB_StdDev, bool show_InfoMarket, bool countDown, bool showPercent, int aTR_Period, TextPosition show_Position, bool show_AreaPre, DateTime startDay, DateTime finalDay, int myAreaOpacity, bool show_Arrows, bool show_Diamonds, bool show_ArrowsEvery_Last, bool show_DiamondsEvery_Last, int long_Arrows, int width_Arrows, DashStyleHelper style_Arrows, bool show_MaxMinPastDay, int width_MaxMinPastDay, DashStyleHelper style_MaxMinPastDay, bool show_InfoPreM, DateTime hora_InfoPreM)
		{
			return indicator.EduS_Master_Indicator(input, show_KeltChannel, period, offsetMultiplier, show_LR, period_LR, show_SMA_1, period_SMA_1, show_SMA_2, period_SMA_2, show_EMADot, period_EMADot, show_ChannelFill, opacity_ChannelFill, mACD_Fast, mACD_Slow, mACD_Smooth, mACD_BB_Period, mACD_BB_StdDev, show_InfoMarket, countDown, showPercent, aTR_Period, show_Position, show_AreaPre, startDay, finalDay, myAreaOpacity, show_Arrows, show_Diamonds, show_ArrowsEvery_Last, show_DiamondsEvery_Last, long_Arrows, width_Arrows, style_Arrows, show_MaxMinPastDay, width_MaxMinPastDay, style_MaxMinPastDay, show_InfoPreM, hora_InfoPreM);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduS_Master_Indicator EduS_Master_Indicator(bool show_KeltChannel, int period, double offsetMultiplier, bool show_LR, int period_LR, bool show_SMA_1, int period_SMA_1, bool show_SMA_2, int period_SMA_2, bool show_EMADot, int period_EMADot, bool show_ChannelFill, int opacity_ChannelFill, int mACD_Fast, int mACD_Slow, int mACD_Smooth, int mACD_BB_Period, double mACD_BB_StdDev, bool show_InfoMarket, bool countDown, bool showPercent, int aTR_Period, TextPosition show_Position, bool show_AreaPre, DateTime startDay, DateTime finalDay, int myAreaOpacity, bool show_Arrows, bool show_Diamonds, bool show_ArrowsEvery_Last, bool show_DiamondsEvery_Last, int long_Arrows, int width_Arrows, DashStyleHelper style_Arrows, bool show_MaxMinPastDay, int width_MaxMinPastDay, DashStyleHelper style_MaxMinPastDay, bool show_InfoPreM, DateTime hora_InfoPreM)
		{
			return indicator.EduS_Master_Indicator(Input, show_KeltChannel, period, offsetMultiplier, show_LR, period_LR, show_SMA_1, period_SMA_1, show_SMA_2, period_SMA_2, show_EMADot, period_EMADot, show_ChannelFill, opacity_ChannelFill, mACD_Fast, mACD_Slow, mACD_Smooth, mACD_BB_Period, mACD_BB_StdDev, show_InfoMarket, countDown, showPercent, aTR_Period, show_Position, show_AreaPre, startDay, finalDay, myAreaOpacity, show_Arrows, show_Diamonds, show_ArrowsEvery_Last, show_DiamondsEvery_Last, long_Arrows, width_Arrows, style_Arrows, show_MaxMinPastDay, width_MaxMinPastDay, style_MaxMinPastDay, show_InfoPreM, hora_InfoPreM);
		}

		public Indicators.EduS_Trader.EduS_Master_Indicator EduS_Master_Indicator(ISeries<double> input , bool show_KeltChannel, int period, double offsetMultiplier, bool show_LR, int period_LR, bool show_SMA_1, int period_SMA_1, bool show_SMA_2, int period_SMA_2, bool show_EMADot, int period_EMADot, bool show_ChannelFill, int opacity_ChannelFill, int mACD_Fast, int mACD_Slow, int mACD_Smooth, int mACD_BB_Period, double mACD_BB_StdDev, bool show_InfoMarket, bool countDown, bool showPercent, int aTR_Period, TextPosition show_Position, bool show_AreaPre, DateTime startDay, DateTime finalDay, int myAreaOpacity, bool show_Arrows, bool show_Diamonds, bool show_ArrowsEvery_Last, bool show_DiamondsEvery_Last, int long_Arrows, int width_Arrows, DashStyleHelper style_Arrows, bool show_MaxMinPastDay, int width_MaxMinPastDay, DashStyleHelper style_MaxMinPastDay, bool show_InfoPreM, DateTime hora_InfoPreM)
		{
			return indicator.EduS_Master_Indicator(input, show_KeltChannel, period, offsetMultiplier, show_LR, period_LR, show_SMA_1, period_SMA_1, show_SMA_2, period_SMA_2, show_EMADot, period_EMADot, show_ChannelFill, opacity_ChannelFill, mACD_Fast, mACD_Slow, mACD_Smooth, mACD_BB_Period, mACD_BB_StdDev, show_InfoMarket, countDown, showPercent, aTR_Period, show_Position, show_AreaPre, startDay, finalDay, myAreaOpacity, show_Arrows, show_Diamonds, show_ArrowsEvery_Last, show_DiamondsEvery_Last, long_Arrows, width_Arrows, style_Arrows, show_MaxMinPastDay, width_MaxMinPastDay, style_MaxMinPastDay, show_InfoPreM, hora_InfoPreM);
		}
	}
}

#endregion
