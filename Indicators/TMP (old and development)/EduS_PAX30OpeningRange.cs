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

// -----------------------------------------------------------------------------
// CORRECCIÓN 1: Enum Global
// -----------------------------------------------------------------------------
public enum TargetCalculationMode
{
    FixedPoints,
    ATRBased
}

namespace NinjaTrader.NinjaScript.Indicators.Edus_Trader
{
    public class EduS_PAX30OpeningRange : Indicator
    {
        // Constants
        private const int DAYS_TO_DISPLAY = 2;
        private DateTime cutoffStartDate = DateTime.MinValue;
        private const int EXTRA_DAYS = 2; 
        
        // ORB tracking
        private double orbHigh;
        private double orbLow;
        private double orbMid;
        private int ORBSeconds;
        
        // CORRECCIÓN 2: Variable inOrbPeriod
        private bool inOrbPeriod = false;
        
        private DateTime currentOrbDate = DateTime.MinValue;
        private DateTime lastProcessTime = DateTime.MinValue; 
        
        // ATR Indicator holder
        private NinjaTrader.NinjaScript.Indicators.ATR atrAlgo;

        private Dictionary<DateTime, Dictionary<int, DateTime>> upperLevelStartTimes = new Dictionary<DateTime, Dictionary<int, DateTime>>();
        private Dictionary<DateTime, Dictionary<int, DateTime>> lowerLevelStartTimes = new Dictionary<DateTime, Dictionary<int, DateTime>>();
        
        // Real-time tracking
        private DateTime realtimeOrbDate = DateTime.MinValue;
        private Dictionary<string, DateTime> activeLabels = new Dictionary<string, DateTime>();
        
        private struct OrbData
        {
            public double High;
            public double Low;
            public double Mid;
            public bool IsToday;
            
            public OrbData(double high, double low, double mid, bool isToday)
            {
                High = high;
                Low = low;
                Mid = mid;
                IsToday = isToday;
            }
        }
        
        private Dictionary<DateTime, OrbData> orbValues = new Dictionary<DateTime, OrbData>();
        private Dictionary<DateTime, List<double>> upperLevels = new Dictionary<DateTime, List<double>>();
        private Dictionary<DateTime, List<double>> lowerLevels = new Dictionary<DateTime, List<double>>();
        
        private HashSet<string> drawnLevels = new HashSet<string>();
        
        // Cache
        [XmlIgnore]
        private SimpleFont cachedFont;
        
        private DateTime lastCleanupTime = DateTime.MinValue;
        
        #region Properties
        
        [NinjaScriptProperty]
        [Display(Name = "Show Targets", Description = "Mostrar líneas de objetivos dinámicos", Order = 0, GroupName = "Target Parameters")]
        public bool ShowTargets { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Calculation Mode", Description = "Método de cálculo para la distancia de los targets", Order = 1, GroupName = "Target Parameters")]
        public TargetCalculationMode CalcMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ATR Period", Description = "Periodo del ATR (si se usa modo ATR)", Order = 2, GroupName = "Target Parameters")]
        [Range(1, 100)]
        public int ATRPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ATR Factor", Description = "Multiplicador del ATR para definir distancia", Order = 3, GroupName = "Target Parameters")]
        [Range(0.1, 50.0)]
        public double ATRFactor { get; set; }
        
        [XmlIgnore]
        [Browsable(false)]
        public TimeSpan ORBStart { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "ORB Local Start Time", Order = 2, GroupName = "ORB Parameters")]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditor")]
        [XmlElement("ORBStart")]
        public string ORBStartSerialize
        {
            get { return ORBStart.ToString(); }
            set { ORBStart = TimeSpan.Parse(value); }
        }

        [XmlIgnore]
        [Browsable(false)]
        public TimeSpan ORBEndPlot { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "ORB Local Line End Time", Order = 5, GroupName = "ORB Parameters")]
        [PropertyEditor("NinjaTrader.Gui.Tools.TimeEditor")]
        [XmlElement("ORBEndPlot")]
        public string ORBEndPlotSerialize
        {
            get { return ORBEndPlot.ToString(); }
            set { ORBEndPlot = TimeSpan.Parse(value); }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Text Vert Offset ", Order = 7, GroupName = "xyDisplay Settings")]
        [Range(-50, 50)]
        public int TextvertPixels  { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Text Horz Offset", Order = 8, GroupName = "xyDisplay Settings")]
        [Range(-100, 100)]
        public int TextHorzOffset { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Font Size", Order = 9, GroupName = "xyDisplay Settings")]
        [Range(6, 36)]
        public int FontSize { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Bold Font", Order = 10, GroupName = "xyDisplay Settings")]
        public bool BoldFont { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Price Label Prefix", Order = 11, GroupName = "xyDisplay Parameters")]
        public string LabelPrefix { get; set; }
        
        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "High Line Color", Order = 12, GroupName = "xyORB Colors")]
        public Brush HighLineColor { get; set; }
        
        [Browsable(false)]
        [XmlElement("HighLineColorSerializable")]
        public string HighLineColorSerializable
        {
            get { return Serialize.BrushToString(HighLineColor); }
            set { HighLineColor = Serialize.StringToBrush(value); }
        }
        
        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Low Line Color", Order = 13, GroupName = "xyORB Colors")]
        public Brush LowLineColor { get; set; }
        
        [Browsable(false)]
        [XmlElement("LowLineColorSerializable")]
        public string LowLineColorSerializable
        {
            get { return Serialize.BrushToString(LowLineColor); }
            set { LowLineColor = Serialize.StringToBrush(value); }
        }
        
        [XmlIgnore]
        [NinjaScriptProperty]
        [Display(Name = "Mid Line Color", Order = 14, GroupName = "xyORB Colors")]
        public Brush MidLineColor { get; set; }
        
        [Browsable(false)]
        [XmlElement("MidLineColorSerializable")]
        public string MidLineColorSerializable
        {
            get { return Serialize.BrushToString(MidLineColor); }
            set { MidLineColor = Serialize.StringToBrush(value); }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "High/Low Line Width", Order = 15, GroupName = "xyDisplay Parameters")]
        [Range(1, 10)]
        public int MainLineWidth { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Mid Line Width", Order = 16, GroupName = "xyDisplay Parameters")]
        [Range(1, 10)]
        public int MidLineWidth { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Levels Line Width", Order = 17, GroupName = "xyDisplay Parameters")]
        [Range(1, 10)]
        public int LevelsLineWidth { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Show Mid Line", Order = 18, GroupName = "xyDisplay Parameters")]
        public bool ShowMid { get; set; }
        #endregion

        private double GetMarketLevelFactor()
        {
            if (!ShowTargets) return 0;

            if (CalcMode == TargetCalculationMode.ATRBased)
            {
                if (atrAlgo != null && atrAlgo.CurrentBar > 0)
                {
                    double atrValue = atrAlgo.Value[0]; 
                    double factor = atrValue * ATRFactor;
                    return RoundToNearestTick(factor);
                }
                return 0;
            }
            else 
            {
                if (Instrument == null || Instrument.MasterInstrument == null)
                    return 0;
                    
                string sym = Instrument.MasterInstrument.Name.ToUpper();
                
                if (sym.Contains("ES") || sym.Contains("MES"))
                    return 15;
                else if (sym.Contains("NQ") || sym.Contains("MNQ"))
                    return 65;
                else
                    return 0; 
            }
        }

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Multi-timeframe 30 second ORB con ATR Targets.";
                Name = "EduS_OpeningRange_v2";
                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                
                IsAutoScale = false; 

                ShowTargets = true;
                CalcMode = TargetCalculationMode.ATRBased; 
                ATRPeriod = 14;
                ATRFactor = 3.0;
                
                ORBStart = new TimeSpan(9, 30, 0); 
                ORBStartSerialize = ORBStart.ToString();
                ORBSeconds = 30;
                ORBEndPlot = new TimeSpan(17, 0, 0);
                ORBEndPlotSerialize = ORBEndPlot.ToString();
                TextvertPixels = 1;
                TextHorzOffset = 5;
                FontSize = 10;
                BoldFont = false;
                ORBSeconds = 30;
                LabelPrefix = "OR";
                HighLineColor = Brushes.DeepSkyBlue;
                LowLineColor = Brushes.OrangeRed;
                MidLineColor = Brushes.Gold;
                MainLineWidth = 2;
                MidLineWidth = 1;
                LevelsLineWidth = 2;
                ShowMid = true;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Second, 30);
            }
            else if (State == State.DataLoaded)
            {
                // CORRECCIÓN ATR
                atrAlgo = ATR(Closes[0], ATRPeriod);

                if (activeLabels == null) activeLabels = new Dictionary<string, DateTime>();
                if (orbValues == null) orbValues = new Dictionary<DateTime, OrbData>();
                if (upperLevels == null) upperLevels = new Dictionary<DateTime, List<double>>();
                if (lowerLevels == null) lowerLevels = new Dictionary<DateTime, List<double>>();
                if (drawnLevels == null) drawnLevels = new HashSet<string>();
                
                cachedFont = new SimpleFont("Arial", FontSize) { Bold = BoldFont };
                
                if (BarsArray.Length > 0 && BarsArray[0].Count > 0)
                {
                    DateTime anchor = BarsArray[0].GetTime(BarsArray[0].Count - 1).Date;
                    int keepDays = DAYS_TO_DISPLAY + EXTRA_DAYS;
                    cutoffStartDate = anchor.AddDays(-(keepDays - 1));
                }
            }
            else if (State == State.Terminated)
            {
                if (activeLabels != null) activeLabels.Clear();
                if (orbValues != null) orbValues.Clear();
                if (upperLevels != null)
                {
                    foreach (var list in upperLevels.Values) list.Clear();
                    upperLevels.Clear();
                }
                if (lowerLevels != null)
                {
                    foreach (var list in lowerLevels.Values) list.Clear();
                    lowerLevels.Clear();
                }
                if (drawnLevels != null) drawnLevels.Clear();
            }
        }
        
        private double RoundToNearestTick(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return double.NaN;
                
            double tickSize = TickSize;
            return Math.Round(value / tickSize) * tickSize;
        }
        
        private DateTime GetLabelTimeWithOffset(DateTime baseTime, bool isRealtime)
        {
            if (TextHorzOffset == 0)
                return baseTime;
                
            try
            {
                TimeSpan barInterval = TimeSpan.Zero;
                var chartPeriod = BarsArray != null && BarsArray.Length > 0 && BarsArray[0] != null 
                    ? BarsArray[0].BarsPeriod 
                    : BarsPeriod;
                
                if (chartPeriod.BarsPeriodType == BarsPeriodType.Tick || 
                    chartPeriod.BarsPeriodType == BarsPeriodType.Volume || 
                    chartPeriod.BarsPeriodType == BarsPeriodType.Range ||
                    chartPeriod.BarsPeriodType == BarsPeriodType.Renko)
                {
                    barInterval = TimeSpan.FromSeconds(30 * TextHorzOffset);
                }
                else 
                {
                    switch (chartPeriod.BarsPeriodType)
                    {
                        case BarsPeriodType.Minute:
                            barInterval = TimeSpan.FromMinutes(chartPeriod.Value * TextHorzOffset);
                            break;
                        case BarsPeriodType.Second:
                            barInterval = TimeSpan.FromSeconds(chartPeriod.Value * TextHorzOffset);
                            break;
                        default:
                            barInterval = TimeSpan.FromMinutes(TextHorzOffset); 
                            break;
                    }
                }
                
                return baseTime.Add(barInterval);
            }
            catch
            {
                return baseTime;
            }
        }
    
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1) return;
            
            if (BarsInProgress == 0)
            {
                if (IsFirstTickOfBar && activeLabels.Count > 0)
                {
                    MoveActiveLabels();
                    if (orbValues.ContainsKey(realtimeOrbDate))
                    {
                        DrawOrbForDay(realtimeOrbDate, true);
                    }
                }
                return; 
            }
            
            if (BarsInProgress != 1)
                return;

            DateTime currentTime = Time[0]; 
            DateTime currentDate = currentTime.Date;
            
            if (currentDate < cutoffStartDate)
                return; 
            
            if (currentTime == lastProcessTime)
                return;
            lastProcessTime = currentTime;
            
            TimeSpan currentTimeOfDay = currentTime.TimeOfDay;
            TimeSpan orbEndTimeOfDay = ORBStart.Add(TimeSpan.FromSeconds(30));
            
            bool isOrbBar = (currentTimeOfDay == orbEndTimeOfDay);
            
            if (isOrbBar && !orbValues.ContainsKey(currentDate))
            {
                currentOrbDate = currentDate;
                orbHigh = High[0];  
                orbLow = Low[0];    
                orbMid = RoundToNearestTick(orbLow + ((orbHigh - orbLow) * 0.5));
                
                bool isToday = IsCurrentTradingDay(currentDate);
                
                orbValues[currentDate] = new OrbData(orbHigh, orbLow, orbMid, isToday);
                upperLevels[currentDate] = new List<double>();
                lowerLevels[currentDate] = new List<double>();
                
                double levelFactor = GetMarketLevelFactor();
                if (levelFactor > 0)
                {
                    upperLevels[currentDate].Add(RoundToNearestTick(orbHigh + levelFactor));
                    lowerLevels[currentDate].Add(RoundToNearestTick(orbLow - levelFactor));
                }
                
                DrawOrbForDay(currentDate, isToday);
                
                if (isToday)
                    realtimeOrbDate = currentDate;
                
                inOrbPeriod = false;
            }
            else if (currentDate != currentOrbDate && !orbValues.ContainsKey(currentDate))
            {
                bool isToday = IsCurrentTradingDay(currentDate);
                
                if (currentTimeOfDay > orbEndTimeOfDay && currentTimeOfDay <= ORBEndPlot)
                {
                    currentOrbDate = currentDate;
                    orbHigh = High[0];
                    orbLow = Low[0];
                    orbMid = RoundToNearestTick(orbLow + ((orbHigh - orbLow) * 0.5));
                    
                    orbValues[currentDate] = new OrbData(orbHigh, orbLow, orbMid, isToday);
                    upperLevels[currentDate] = new List<double>();
                    lowerLevels[currentDate] = new List<double>();
                    
                    double levelFactor = GetMarketLevelFactor();
                    if (levelFactor > 0)
                    {
                        upperLevels[currentDate].Add(RoundToNearestTick(orbHigh + levelFactor));
                        lowerLevels[currentDate].Add(RoundToNearestTick(orbLow - levelFactor));
                    }
                    
                    DrawOrbForDay(currentDate, isToday);
                    
                    if (isToday)
                        realtimeOrbDate = currentDate;
                    
                    inOrbPeriod = false;
                }
            }
            
            if (orbValues.ContainsKey(currentDate) && currentTimeOfDay > orbEndTimeOfDay && currentTimeOfDay <= ORBEndPlot)
            {
                CheckAndAddDynamicLevels(currentDate, currentTime);
            }
            
            if (currentTime.Subtract(lastCleanupTime).TotalHours >= 1)
            {
                CleanupOldData(currentDate);
                lastCleanupTime = currentTime;
            }
        }

        private bool IsCurrentTradingDay(DateTime date)
        {
            DateTime now = Core.Globals.Now;
            return date.Date == now.Date;
        }
        
        private void DrawOrbForDay(DateTime orbDate, bool isRealtime)
        {
            if (!orbValues.ContainsKey(orbDate)) return;
                
            var orbData = orbValues[orbDate];
            double dayHigh = orbData.High;
            double dayLow = orbData.Low;
            double dayMid = orbData.Mid;
            
            string dateStr = orbDate.ToString("yyyyMMdd");
            
            DateTime lineStart = orbDate.Add(ORBStart.Add(TimeSpan.FromSeconds(ORBSeconds)));
            DateTime maxEndTime = orbDate.Add(ORBEndPlot);
            DateTime lineEnd;
            DateTime labelTime;
            
            if (isRealtime)
            {
                DateTime currentTime = (Times[0].Count > 0) ? Times[0][0] : lineStart;
                lineEnd = currentTime < maxEndTime ? currentTime : maxEndTime;
                labelTime = lineEnd;
            }
            else
            {
                lineEnd = maxEndTime;
                labelTime = maxEndTime;
            }
            
            labelTime = GetLabelTimeWithOffset(labelTime, isRealtime);
            
            // --------------------------------------------------------------------------------------
            // CORRECCIÓN CRÍTICA: Cambiado el tercer parámetro de 'true' a 'false' en todos los Draws.
            // Esto evita que las líneas forcen la auto-escala del gráfico.
            // --------------------------------------------------------------------------------------
            try
            {
                string highLineKey = "PAX_HighLine_" + dateStr;
                Draw.Line(this, highLineKey, false, lineStart, dayHigh, lineEnd, dayHigh, 
                    HighLineColor, DashStyleHelper.Solid, MainLineWidth);
                
                string highLabelKey = "PAX_HighLabel_" + dateStr;
                Draw.Text(this, highLabelKey, false, LabelPrefix + " " + dayHigh.ToString("F2"), 
                    labelTime, dayHigh, TextvertPixels, HighLineColor, cachedFont,
                    TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                
                if (isRealtime) activeLabels[highLabelKey] = labelTime;
            } catch {}
            
            try
            {
                string lowLineKey = "PAX_LowLine_" + dateStr;
                Draw.Line(this, lowLineKey, false, lineStart, dayLow, lineEnd, dayLow, 
                    LowLineColor, DashStyleHelper.Solid, MainLineWidth);
                
                string lowLabelKey = "PAX_LowLabel_" + dateStr;
                Draw.Text(this, lowLabelKey, false, LabelPrefix + " " + dayLow.ToString("F2"), 
                    labelTime, dayLow, TextvertPixels, LowLineColor, cachedFont,
                    TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                
                if (isRealtime) activeLabels[lowLabelKey] = labelTime;
            } catch {}
            
            if (ShowMid)
            {
                string midLineKey = "PAX_MidLine_" + dateStr;
                Draw.Line(this, midLineKey, false, lineStart, dayMid, lineEnd, dayMid, 
                    MidLineColor, DashStyleHelper.Solid, MidLineWidth);
                
                string midLabelKey = "PAX_MidLabel_" + dateStr;
                Draw.Text(this, midLabelKey, false, LabelPrefix + " MID " + dayMid.ToString("F2"), 
                    labelTime, dayMid, TextvertPixels, MidLineColor, cachedFont,
                    TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                
                if (isRealtime) activeLabels[midLabelKey] = labelTime;
            }
            
            double levelFactor = GetMarketLevelFactor();
            if (levelFactor > 0 && upperLevels.ContainsKey(orbDate) && lowerLevels.ContainsKey(orbDate))
            {
                if (upperLevels[orbDate].Count > 0)
                {
                    double upperLevel = upperLevels[orbDate][0];
                    string upperLineKey = "PAX_UpperLevel_" + dateStr + "_0";
                    Draw.Line(this, upperLineKey, false, lineStart, upperLevel, lineEnd, upperLevel, 
                        HighLineColor, DashStyleHelper.Dash, LevelsLineWidth);
                    
                    string upperLabelKey = "PAX_UpperLabel_" + dateStr + "_0";
                    Draw.Text(this, upperLabelKey, false, LabelPrefix + " " + upperLevel.ToString("F2"), 
                        labelTime, upperLevel, TextvertPixels, HighLineColor, cachedFont,
                        TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                    
                    if (isRealtime) activeLabels[upperLabelKey] = labelTime;
                }
                
                if (lowerLevels[orbDate].Count > 0)
                {
                    double lowerLevel = lowerLevels[orbDate][0];
                    string lowerLineKey = "PAX_LowerLevel_" + dateStr + "_0";
                    Draw.Line(this, lowerLineKey, false, lineStart, lowerLevel, lineEnd, lowerLevel, 
                        LowLineColor, DashStyleHelper.Dash, LevelsLineWidth);
                    
                    string lowerLabelKey = "PAX_LowerLabel_" + dateStr + "_0";
                    Draw.Text(this, lowerLabelKey, false, LabelPrefix + " " + lowerLevel.ToString("F2"), 
                        labelTime, lowerLevel, TextvertPixels, LowLineColor, cachedFont,
                        TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                    
                    if (isRealtime) activeLabels[lowerLabelKey] = labelTime;
                }
            }
        }
        
        private void CheckAndAddDynamicLevels(DateTime currentDate, DateTime currentTime)
        {
            if (!ShowTargets) return;

            double levelFactor = GetMarketLevelFactor();
            if (!orbValues.ContainsKey(currentDate) || levelFactor <= 0) return;
            if (!upperLevels.ContainsKey(currentDate) || !lowerLevels.ContainsKey(currentDate)) return;
            
            var currentUpperLevels = upperLevels[currentDate];
            var currentLowerLevels = lowerLevels[currentDate];
            
            if (currentUpperLevels.Count > 0 && currentLowerLevels.Count > 0)
            {
                double highestUpperLevel = currentUpperLevels[currentUpperLevels.Count - 1];
                double lowestLowerLevel = currentLowerLevels[currentLowerLevels.Count - 1];
                
                string dateStr = currentDate.ToString("yyyyMMdd");
                DateTime maxEndTime = currentDate.Add(ORBEndPlot);
                bool isRealtime = orbValues[currentDate].IsToday;
                
                DateTime tickTime = (Times[0].Count > 0) ? Times[0][0] : currentTime;
                DateTime lineEnd = isRealtime ? tickTime : maxEndTime;
                
                if (High[0] > highestUpperLevel)
                {
                    double newUpperLevel = RoundToNearestTick(highestUpperLevel + levelFactor);
                    int levelIndex = currentUpperLevels.Count;
                    
                    string levelKey = "PAX_UpperLevel_" + dateStr + "_" + levelIndex;
                    if (!drawnLevels.Contains(levelKey))
                    {
                        currentUpperLevels.Add(newUpperLevel);
                        drawnLevels.Add(levelKey);
                        
                        if (!upperLevelStartTimes.ContainsKey(currentDate))
                            upperLevelStartTimes[currentDate] = new Dictionary<int, DateTime>();
                        upperLevelStartTimes[currentDate][levelIndex] = currentTime;
                        
                        try
                        {
                            Draw.Line(this, levelKey, false, currentTime, newUpperLevel, lineEnd, newUpperLevel, 
                                HighLineColor, DashStyleHelper.Dash, LevelsLineWidth);
                            
                            DateTime labelTime = isRealtime ? lineEnd : maxEndTime;
                            labelTime = GetLabelTimeWithOffset(labelTime, isRealtime);
                            
                            string labelKey = "PAX_UpperLabel_" + dateStr + "_" + levelIndex;
                            Draw.Text(this, labelKey, false, 
                                LabelPrefix + " " + newUpperLevel.ToString("F2"), 
                                labelTime, newUpperLevel, TextvertPixels, HighLineColor, cachedFont,
                                TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                            
                            if (isRealtime) activeLabels[labelKey] = labelTime;
                        } catch {}
                    }
                }
                
                if (Low[0] < lowestLowerLevel)
                {
                    double newLowerLevel = RoundToNearestTick(lowestLowerLevel - levelFactor);
                    int levelIndex = currentLowerLevels.Count;
                    
                    string levelKey = "PAX_LowerLevel_" + dateStr + "_" + levelIndex;
                    if (!drawnLevels.Contains(levelKey))
                    {
                        currentLowerLevels.Add(newLowerLevel);
                        drawnLevels.Add(levelKey);
                        
                        if (!lowerLevelStartTimes.ContainsKey(currentDate))
                            lowerLevelStartTimes[currentDate] = new Dictionary<int, DateTime>();
                        lowerLevelStartTimes[currentDate][levelIndex] = currentTime;
                        
                        try
                        {
                            Draw.Line(this, levelKey, false, currentTime, newLowerLevel, lineEnd, newLowerLevel, 
                                LowLineColor, DashStyleHelper.Dash, LevelsLineWidth);
                            
                            DateTime labelTime = isRealtime ? lineEnd : maxEndTime;
                            labelTime = GetLabelTimeWithOffset(labelTime, isRealtime);
                            
                            string labelKey = "PAX_LowerLabel_" + dateStr + "_" + levelIndex;
                            Draw.Text(this, labelKey, false, 
                                LabelPrefix + " " + newLowerLevel.ToString("F2"), 
                                labelTime, newLowerLevel, TextvertPixels, LowLineColor, cachedFont,
                                TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                            
                            if (isRealtime) activeLabels[labelKey] = labelTime;
                        } catch {}
                    }
                }
            }
        }
        
        private void MoveActiveLabels()
        {
            if (Times[0].Count < 1 || !orbValues.ContainsKey(realtimeOrbDate))
                return;
            
            DateTime currentTime = Times[0][0];
            DateTime orbEndTime = realtimeOrbDate.Add(ORBEndPlot);
            
            if (currentTime >= orbEndTime) return;
            
            DateTime lineEnd = currentTime < orbEndTime ? currentTime : orbEndTime;
            string dateStr = realtimeOrbDate.ToString("yyyyMMdd");
            var orbData = orbValues[realtimeOrbDate];
            DateTime lineStart = realtimeOrbDate.Add(ORBStart.Add(TimeSpan.FromSeconds(ORBSeconds)));
            
            // CORRECCIÓN en MoveActiveLabels también
            Draw.Line(this, "PAX_HighLine_" + dateStr, false, lineStart, orbData.High, lineEnd, orbData.High, 
                HighLineColor, DashStyleHelper.Solid, MainLineWidth);
            Draw.Line(this, "PAX_LowLine_" + dateStr, false, lineStart, orbData.Low, lineEnd, orbData.Low, 
                LowLineColor, DashStyleHelper.Solid, MainLineWidth);
            
            if (ShowMid)
                Draw.Line(this, "PAX_MidLine_" + dateStr, false, lineStart, orbData.Mid, lineEnd, orbData.Mid, 
                    MidLineColor, DashStyleHelper.Solid, MidLineWidth);
            
            if (ShowTargets)
            {
                if (upperLevels.ContainsKey(realtimeOrbDate))
                {
                    for (int i = 0; i < upperLevels[realtimeOrbDate].Count; i++)
                    {
                        DateTime levelStartTime = lineStart;
                        if (i > 0 && upperLevelStartTimes.ContainsKey(realtimeOrbDate) && upperLevelStartTimes[realtimeOrbDate].ContainsKey(i))
                            levelStartTime = upperLevelStartTimes[realtimeOrbDate][i];
                        
                        Draw.Line(this, "PAX_UpperLevel_" + dateStr + "_" + i, false, 
                            levelStartTime, upperLevels[realtimeOrbDate][i], lineEnd, upperLevels[realtimeOrbDate][i], 
                            HighLineColor, DashStyleHelper.Dash, LevelsLineWidth);
                    }
                }
                
                if (lowerLevels.ContainsKey(realtimeOrbDate))
                {
                    for (int i = 0; i < lowerLevels[realtimeOrbDate].Count; i++)
                    {
                        DateTime levelStartTime = lineStart;
                        if (i > 0 && lowerLevelStartTimes.ContainsKey(realtimeOrbDate) && lowerLevelStartTimes[realtimeOrbDate].ContainsKey(i))
                            levelStartTime = lowerLevelStartTimes[realtimeOrbDate][i];
                        
                        Draw.Line(this, "PAX_LowerLevel_" + dateStr + "_" + i, false, 
                            levelStartTime, lowerLevels[realtimeOrbDate][i], lineEnd, lowerLevels[realtimeOrbDate][i], 
                            LowLineColor, DashStyleHelper.Dash, LevelsLineWidth);
                    }
                }
            }
            
            DateTime newLabelTime = GetLabelTimeWithOffset(currentTime, true);
            var labelKeys = activeLabels.Keys.ToList();
            
            foreach (string labelKey in labelKeys)
            {
                double price = 0;
                string labelText = "";
                Brush color = Brushes.White;
                
                if (labelKey.Contains("HighLabel")) { price = orbData.High; labelText = LabelPrefix + " " + price.ToString("F2"); color = HighLineColor; }
                else if (labelKey.Contains("LowLabel")) { price = orbData.Low; labelText = LabelPrefix + " " + price.ToString("F2"); color = LowLineColor; }
                else if (labelKey.Contains("MidLabel")) { price = orbData.Mid; labelText = LabelPrefix + " MID " + price.ToString("F2"); color = MidLineColor; }
                else if (ShowTargets && labelKey.Contains("UpperLabel")) 
                {
                   string[] parts = labelKey.Split('_');
                   if (parts.Length > 1 && int.TryParse(parts[parts.Length - 1], out int idx)) 
                   {
                       if (upperLevels.ContainsKey(realtimeOrbDate) && idx < upperLevels[realtimeOrbDate].Count) {
                           price = upperLevels[realtimeOrbDate][idx]; labelText = LabelPrefix + " " + price.ToString("F2"); color = HighLineColor;
                       }
                   }
                }
                else if (ShowTargets && labelKey.Contains("LowerLabel")) 
                {
                   string[] parts = labelKey.Split('_');
                   if (parts.Length > 1 && int.TryParse(parts[parts.Length - 1], out int idx)) 
                   {
                       if (lowerLevels.ContainsKey(realtimeOrbDate) && idx < lowerLevels[realtimeOrbDate].Count) {
                           price = lowerLevels[realtimeOrbDate][idx]; labelText = LabelPrefix + " " + price.ToString("F2"); color = LowLineColor;
                       }
                   }
                }
                
                if (price > 0)
                {
                    Draw.Text(this, labelKey, false, labelText, 
                        newLabelTime, price, TextvertPixels, color, cachedFont,
                        TextAlignment.Left, Brushes.Transparent, Brushes.Transparent, 0);
                    activeLabels[labelKey] = newLabelTime;
                }
            }
        }
        
        private void CleanupOldData(DateTime currentDate)
        {
            var keysToRemove = orbValues.Keys
                .Where(date => (currentDate - date).Days >= DAYS_TO_DISPLAY)
                .ToList();
            
            foreach (var key in keysToRemove)
            {
                string dateStr = key.ToString("yyyyMMdd");
                if (key == realtimeOrbDate) { realtimeOrbDate = DateTime.MinValue; activeLabels.Clear(); }
                drawnLevels.RemoveWhere(x => x.Contains(dateStr));
                orbValues.Remove(key);
                if (upperLevels.ContainsKey(key)) { upperLevels[key].Clear(); upperLevels.Remove(key); }
                if (lowerLevels.ContainsKey(key)) { lowerLevels[key].Clear(); lowerLevels.Remove(key); }
                if (upperLevelStartTimes.ContainsKey(key)) { upperLevelStartTimes[key].Clear(); upperLevelStartTimes.Remove(key); }
                if (lowerLevelStartTimes.ContainsKey(key)) { lowerLevelStartTimes[key].Clear(); lowerLevelStartTimes.Remove(key); }
            }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Edus_Trader.EduS_PAX30OpeningRange[] cacheEduS_PAX30OpeningRange;
		public Edus_Trader.EduS_PAX30OpeningRange EduS_PAX30OpeningRange(bool showTargets, TargetCalculationMode calcMode, int aTRPeriod, double aTRFactor, string oRBStartSerialize, string oRBEndPlotSerialize, int textvertPixels, int textHorzOffset, int fontSize, bool boldFont, string labelPrefix, Brush highLineColor, Brush lowLineColor, Brush midLineColor, int mainLineWidth, int midLineWidth, int levelsLineWidth, bool showMid)
		{
			return EduS_PAX30OpeningRange(Input, showTargets, calcMode, aTRPeriod, aTRFactor, oRBStartSerialize, oRBEndPlotSerialize, textvertPixels, textHorzOffset, fontSize, boldFont, labelPrefix, highLineColor, lowLineColor, midLineColor, mainLineWidth, midLineWidth, levelsLineWidth, showMid);
		}

		public Edus_Trader.EduS_PAX30OpeningRange EduS_PAX30OpeningRange(ISeries<double> input, bool showTargets, TargetCalculationMode calcMode, int aTRPeriod, double aTRFactor, string oRBStartSerialize, string oRBEndPlotSerialize, int textvertPixels, int textHorzOffset, int fontSize, bool boldFont, string labelPrefix, Brush highLineColor, Brush lowLineColor, Brush midLineColor, int mainLineWidth, int midLineWidth, int levelsLineWidth, bool showMid)
		{
			if (cacheEduS_PAX30OpeningRange != null)
				for (int idx = 0; idx < cacheEduS_PAX30OpeningRange.Length; idx++)
					if (cacheEduS_PAX30OpeningRange[idx] != null && cacheEduS_PAX30OpeningRange[idx].ShowTargets == showTargets && cacheEduS_PAX30OpeningRange[idx].CalcMode == calcMode && cacheEduS_PAX30OpeningRange[idx].ATRPeriod == aTRPeriod && cacheEduS_PAX30OpeningRange[idx].ATRFactor == aTRFactor && cacheEduS_PAX30OpeningRange[idx].ORBStartSerialize == oRBStartSerialize && cacheEduS_PAX30OpeningRange[idx].ORBEndPlotSerialize == oRBEndPlotSerialize && cacheEduS_PAX30OpeningRange[idx].TextvertPixels == textvertPixels && cacheEduS_PAX30OpeningRange[idx].TextHorzOffset == textHorzOffset && cacheEduS_PAX30OpeningRange[idx].FontSize == fontSize && cacheEduS_PAX30OpeningRange[idx].BoldFont == boldFont && cacheEduS_PAX30OpeningRange[idx].LabelPrefix == labelPrefix && cacheEduS_PAX30OpeningRange[idx].HighLineColor == highLineColor && cacheEduS_PAX30OpeningRange[idx].LowLineColor == lowLineColor && cacheEduS_PAX30OpeningRange[idx].MidLineColor == midLineColor && cacheEduS_PAX30OpeningRange[idx].MainLineWidth == mainLineWidth && cacheEduS_PAX30OpeningRange[idx].MidLineWidth == midLineWidth && cacheEduS_PAX30OpeningRange[idx].LevelsLineWidth == levelsLineWidth && cacheEduS_PAX30OpeningRange[idx].ShowMid == showMid && cacheEduS_PAX30OpeningRange[idx].EqualsInput(input))
						return cacheEduS_PAX30OpeningRange[idx];
			return CacheIndicator<Edus_Trader.EduS_PAX30OpeningRange>(new Edus_Trader.EduS_PAX30OpeningRange(){ ShowTargets = showTargets, CalcMode = calcMode, ATRPeriod = aTRPeriod, ATRFactor = aTRFactor, ORBStartSerialize = oRBStartSerialize, ORBEndPlotSerialize = oRBEndPlotSerialize, TextvertPixels = textvertPixels, TextHorzOffset = textHorzOffset, FontSize = fontSize, BoldFont = boldFont, LabelPrefix = labelPrefix, HighLineColor = highLineColor, LowLineColor = lowLineColor, MidLineColor = midLineColor, MainLineWidth = mainLineWidth, MidLineWidth = midLineWidth, LevelsLineWidth = levelsLineWidth, ShowMid = showMid }, input, ref cacheEduS_PAX30OpeningRange);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Edus_Trader.EduS_PAX30OpeningRange EduS_PAX30OpeningRange(bool showTargets, TargetCalculationMode calcMode, int aTRPeriod, double aTRFactor, string oRBStartSerialize, string oRBEndPlotSerialize, int textvertPixels, int textHorzOffset, int fontSize, bool boldFont, string labelPrefix, Brush highLineColor, Brush lowLineColor, Brush midLineColor, int mainLineWidth, int midLineWidth, int levelsLineWidth, bool showMid)
		{
			return indicator.EduS_PAX30OpeningRange(Input, showTargets, calcMode, aTRPeriod, aTRFactor, oRBStartSerialize, oRBEndPlotSerialize, textvertPixels, textHorzOffset, fontSize, boldFont, labelPrefix, highLineColor, lowLineColor, midLineColor, mainLineWidth, midLineWidth, levelsLineWidth, showMid);
		}

		public Indicators.Edus_Trader.EduS_PAX30OpeningRange EduS_PAX30OpeningRange(ISeries<double> input , bool showTargets, TargetCalculationMode calcMode, int aTRPeriod, double aTRFactor, string oRBStartSerialize, string oRBEndPlotSerialize, int textvertPixels, int textHorzOffset, int fontSize, bool boldFont, string labelPrefix, Brush highLineColor, Brush lowLineColor, Brush midLineColor, int mainLineWidth, int midLineWidth, int levelsLineWidth, bool showMid)
		{
			return indicator.EduS_PAX30OpeningRange(input, showTargets, calcMode, aTRPeriod, aTRFactor, oRBStartSerialize, oRBEndPlotSerialize, textvertPixels, textHorzOffset, fontSize, boldFont, labelPrefix, highLineColor, lowLineColor, midLineColor, mainLineWidth, midLineWidth, levelsLineWidth, showMid);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Edus_Trader.EduS_PAX30OpeningRange EduS_PAX30OpeningRange(bool showTargets, TargetCalculationMode calcMode, int aTRPeriod, double aTRFactor, string oRBStartSerialize, string oRBEndPlotSerialize, int textvertPixels, int textHorzOffset, int fontSize, bool boldFont, string labelPrefix, Brush highLineColor, Brush lowLineColor, Brush midLineColor, int mainLineWidth, int midLineWidth, int levelsLineWidth, bool showMid)
		{
			return indicator.EduS_PAX30OpeningRange(Input, showTargets, calcMode, aTRPeriod, aTRFactor, oRBStartSerialize, oRBEndPlotSerialize, textvertPixels, textHorzOffset, fontSize, boldFont, labelPrefix, highLineColor, lowLineColor, midLineColor, mainLineWidth, midLineWidth, levelsLineWidth, showMid);
		}

		public Indicators.Edus_Trader.EduS_PAX30OpeningRange EduS_PAX30OpeningRange(ISeries<double> input , bool showTargets, TargetCalculationMode calcMode, int aTRPeriod, double aTRFactor, string oRBStartSerialize, string oRBEndPlotSerialize, int textvertPixels, int textHorzOffset, int fontSize, bool boldFont, string labelPrefix, Brush highLineColor, Brush lowLineColor, Brush midLineColor, int mainLineWidth, int midLineWidth, int levelsLineWidth, bool showMid)
		{
			return indicator.EduS_PAX30OpeningRange(input, showTargets, calcMode, aTRPeriod, aTRFactor, oRBStartSerialize, oRBEndPlotSerialize, textvertPixels, textHorzOffset, fontSize, boldFont, labelPrefix, highLineColor, lowLineColor, midLineColor, mainLineWidth, midLineWidth, levelsLineWidth, showMid);
		}
	}
}

#endregion
