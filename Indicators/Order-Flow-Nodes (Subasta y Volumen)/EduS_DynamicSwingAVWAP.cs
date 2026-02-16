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


// Dynamic Swing Anchored VWAP (Zeiierman) — Pine-like “freeze” continuation, no gaps
namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader
{
    public class EduS_DynamicSwingAVWAP : Indicator
    {
        // -------- Inputs
       // -------- Inputs
[NinjaScriptProperty, Range(2, int.MaxValue)]
[Display(Name="Swing Period", Description="Bars used to detect swing highs/lows; larger = bigger pivots, smaller = quicker/noisier.", GroupName="Swing Points", Order=0)]
public int SwingPeriod { get; set; } = 50;

[NinjaScriptProperty, Range(1, 10000)]
[Display(Name="Adaptive Price Tracking (APT)", Description="Base responsiveness of the VWAP engine (lower=faster/tighter, higher=smoother/slower).", GroupName="Swing Points", Order=1)]
public int BaseAPT { get; set; } = 20;

[NinjaScriptProperty]
[Display(Name="Adapt APT by ATR ratio", Description="Auto-adjust APT by ATR/ATR-RMA ratio to react to volatility.", GroupName="Swing Points", Order=2)]
public bool UseAdaptAPT { get; set; } = true;

[NinjaScriptProperty, Range(0.1, 1000)]
[Display(Name="Volatility Bias", Description="Strength of volatility adaptation; >1 amplifies response, <1 damps it.", GroupName="Swing Points", Order=3)]
public double VolatilityBias { get; set; } = 10.0;

// -------- Style
[XmlIgnore]
[Display(Name="VWAP Up Color", Description="Color of VWAP lines anchored from swing lows (up legs).", GroupName="Style", Order=20)]
public Brush VwapUpColor { get; set; } = Brushes.Red;
[Browsable(false)] public string VwapUpColorSerializable { get => Serialize.BrushToString(VwapUpColor); set => VwapUpColor = Serialize.StringToBrush(value); }

[XmlIgnore]
[Display(Name="VWAP Down Color", Description="Color of VWAP lines anchored from swing highs (down legs).", GroupName="Style", Order=21)]
public Brush VwapDownColor { get; set; } = Brushes.Lime;
[Browsable(false)] public string VwapDownColorSerializable { get => Serialize.BrushToString(VwapDownColor); set => VwapDownColor = Serialize.StringToBrush(value); }

[NinjaScriptProperty, Range(1, 10)]
[Display(Name="VWAP Line Width", Description="Stroke width of the VWAP lines.", GroupName="Style", Order=22)]
public int VwapWidth { get; set; } = 3;

[NinjaScriptProperty, Range(0, 100)]
[Display(Name="Tail Bars After Flip", Description="How long the prior leg keeps printing a flat ‘freeze’ after a flip (bars).", GroupName="Style", Order=23)]
public int TailBars { get; set; } = 1;

// -------- Labels
[XmlIgnore]
[Display(Name="Low-pivot Label Color", Description="Color for HL/LL labels drawn at swing lows.", GroupName="Style", Order=30)]
public Brush LowLabelColor { get; set; } = Brushes.Lime;
[Browsable(false)] public string LowLabelColorSerializable { get => Serialize.BrushToString(LowLabelColor); set => LowLabelColor = Serialize.StringToBrush(value); }

[XmlIgnore]
[Display(Name="High-pivot Label Color", Description="Color for HH/LH labels drawn at swing highs.", GroupName="Style", Order=31)]
public Brush HighLabelColor { get; set; } = Brushes.Red;
[Browsable(false)] public string HighLabelColorSerializable { get => Serialize.BrushToString(HighLabelColor); set => HighLabelColor = Serialize.StringToBrush(value); }

[NinjaScriptProperty, Range(6, 48)]
[Display(Name="Pivot Label Size", Description="Font size for HH/HL/LH/LL labels.", GroupName="Style", Order=32)]
public int LabelSize { get; set; } = 15;

[NinjaScriptProperty]
[Display(Name="Pivot Label Bold", Description="Bold styling for pivot labels.", GroupName="Style", Order=33)]
public bool LabelBold { get; set; } = true;

[NinjaScriptProperty, Range(0.0, double.MaxValue)]
[Display(Name="Label Offset (points)", Description="Vertical offset for labels (in instrument points) to keep them clear of candles.", GroupName="Style", Order=40)]
public double LabelOffset { get; set; } = 3.0;

 [NinjaScriptProperty]
[Display(Name = "Discord Link", Order = 1, GroupName = "Discord", Description = "The Discord information")]
        public string DiscordDiscord
        {
            get { return discordDiscord; }
            set { discordDiscord = value; }
        }


        // -------- Internals
        private const int atrLen = 50;
        private ATR atr;
        private double atrRma = double.NaN;

        private int lastSwingHighBar = -1, lastSwingLowBar = -1;
        private double lastSwingHigh = double.NaN, lastSwingLow = double.NaN;
        private int dir = 0; // +1 up, -1 down

        // Up engine (anchored at swing lows)
        private int upAnchorBar = -1;
        private double upAnchorPrice = double.NaN;
        private double upEW_P = double.NaN, upEW_V = double.NaN;

        // Down engine (anchored at swing highs)
        private int dnAnchorBar = -1;
        private double dnAnchorPrice = double.NaN;
        private double dnEW_P = double.NaN, dnEW_V = double.NaN;

        // previous same-type pivots (for HH/HL/LH/LL)
        private double prevLowPivot  = double.NaN;
        private double prevHighPivot = double.NaN;

        // Per-plot tails (independent, never cleared while active)
        private int    tailUpRemain   = 0;   // on UP plot (Values[0])
        private double tailUpValue    = double.NaN;

        private int    tailDnRemain   = 0;   // on DOWN plot (Values[1])
        private double tailDnValue    = double.NaN;

       // Discord Link   // Watermark variables
      

        private readonly string watermarkText = "HH : Máximo Más Alto" +
												"\nLH : Máximo Más Bajo" +
												"\nLL : Mínimo Más Bajo" +
												"\nHL : Mínimo Más Alto";
        private readonly System.Windows.Media.Brush watermarkBrush = System.Windows.Media.Brushes.SeaGreen;

	   private string discordDiscord = " ";  //https://discord.gg/x99B8mAHKa




        // -------- Helpers
        private static double HLC(Indicator ind, int barsAgo)
            => (ind.High[barsAgo] + ind.Low[barsAgo] + ind.Close[barsAgo]) / 3.0;

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        private static double AlphaFromAPT(double apt)
        {
            double a = Math.Max(1.0, apt);
            double decay = Math.Exp(-Math.Log(2.0) / a);
            return 1.0 - decay;
        }

        private double RMA(double prev, double x, int len)
        {
            double k = 1.0 / Math.Max(1, len);
            return double.IsNaN(prev) ? x : (1 - k) * prev + k * x;
        }

        private double APTAt(int barsAgo)
        {
            double apt = BaseAPT;
            if (UseAdaptAPT && barsAgo <= CurrentBar)
            {
                int ba = Clamp(barsAgo, 0, CurrentBar);
                double atrNow = atr[ba];
                double ratio  = (atrRma > 0.0) ? atrNow / atrRma : 1.0;
                double adj    = BaseAPT / Math.Pow(ratio, VolatilityBias);
                apt = Math.Max(5.0, Math.Min(300.0, adj));
            }
            return apt;
        }

        private void WriteUp(int barsAgo, double v) { int ba = Clamp(barsAgo,0,CurrentBar); Values[0][ba] = v; }
        private void WriteDn(int barsAgo, double v) { int ba = Clamp(barsAgo,0,CurrentBar); Values[1][ba] = v; }

        // Clear forward from pivot to now; optionally keep bar 0 (so tails can stamp gapless)
        private void ClearForwardFromPivotInclusive(int pivotBarsAgo, int plotIdx, bool keepBar0)
        {
            // if that plot has an active tail, DO NOT clear anything — tail owns the plot
            if ((plotIdx == 0 && tailUpRemain > 0) || (plotIdx == 1 && tailDnRemain > 0))
                return;

            int start = Math.Max(0, Math.Min(pivotBarsAgo, CurrentBar));
            int stop  = keepBar0 ? 1 : 0;
            for (int i = start; i >= stop; i--) Values[plotIdx][i] = double.NaN;
        }

        // -------- NT lifecycle
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Dynamic Swing AVWAP ";
				Description = "Dynamic Swing AVWAP — a pivot-anchored, adaptive VWAP that restarts at each confirmed swing (HH/HL/LH/LL). Designed to segment trend legs cleanly, label pivots, and stay responsive with optional ATR-based adaptation.\nCreated by PropTraderz. Not for sale or redistribution. Educational use only.\nFind it helpful? Support us using our prop firms code: PROPTRADERZ.                                     www.proptraderz.com";

                IsOverlay = true;
                Calculate = Calculate.OnBarClose;
                IsSuspendedWhileInactive = true;

                BarsRequiredToPlot = Math.Max(SwingPeriod, atrLen) + 5;

                // Hash = segmented look (like Pine polylines)
                AddPlot(new Stroke(Brushes.Green, 2), PlotStyle.Hash, "VWAP_Up");
                AddPlot(new Stroke(Brushes.Red,   2), PlotStyle.Hash, "VWAP_Down");
            }
            else if (State == State.Configure)
            {
                atr = ATR(atrLen);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < BarsRequiredToPlot)
            {
                Values[0][0] = Values[1][0] = double.NaN;
                atrRma = RMA(atrRma, atr[0], atrLen);
                return;
            }

            // keep adaptation state
            atrRma = RMA(atrRma, atr[0], atrLen);

            // ----- 1) PRINT ACTIVE TAILS FIRST (freeze, no gaps, immune to flips)
            if (tailUpRemain > 0 && !double.IsNaN(tailUpValue))   Values[0][0] = tailUpValue;
            if (tailDnRemain > 0 && !double.IsNaN(tailDnValue))   Values[1][0] = tailDnValue;

            // decrement at end of bar
            if (tailUpRemain  > 0) tailUpRemain--;
            if (tailDnRemain  > 0) tailDnRemain--;

            // ----- 2) Detect swings and flips
            double ts = (Instrument?.MasterInstrument?.TickSize ?? TickSize); if (ts <= 0) ts = 0.01;
            double yOff = Math.Max(LabelOffset, ts);
            var lblFont = new SimpleFont("Segoe UI", LabelSize) { Bold = LabelBold };

            double maxH = MAX(High, SwingPeriod)[0];
            double minL = MIN(Low,  SwingPeriod)[0];
            if (High[0] >= maxH - ts/10.0) { lastSwingHighBar = CurrentBar; lastSwingHigh = High[0]; }
            if (Low[0]  <= minL + ts/10.0) { lastSwingLowBar  = CurrentBar; lastSwingLow  = Low[0];  }
            int newDir = (lastSwingHighBar > lastSwingLowBar) ? +1 : -1;

            if (newDir != dir)
            {
                // last value of outgoing leg (for its OWN plot’s tail)
                double prevVal = double.NaN;
                if (dir > 0)  prevVal = (upEW_V > 0.0) ? (upEW_P / upEW_V) : upAnchorPrice;
                if (dir < 0)  prevVal = (dnEW_V > 0.0) ? (dnEW_P / dnEW_V) : dnAnchorPrice;

                dir = newDir;

                if (dir > 0) // new UP leg from last swing LOW
                {
                    // start/extend tail on DOWN plot (independent)
                    if (!double.IsNaN(prevVal))
                    {
                        tailDnValue   = prevVal;
                        tailDnRemain  = Math.Max(tailDnRemain, TailBars); // extend if already running
                        Values[1][0]  = tailDnValue; // stamp immediately (no gap)
                    }

                    upAnchorBar   = lastSwingLowBar;
                    upAnchorPrice = lastSwingLow;
                    int pivotBarsAgo = Clamp(CurrentBar - upAnchorBar, 0, CurrentBar);

                    // clear DOWN forward unless that plot has an active tail
                    ClearForwardFromPivotInclusive(pivotBarsAgo, 1, keepBar0:true);

                    // label HL/LL
                    string lbl = double.IsNaN(prevLowPivot) ? "HL" : (upAnchorPrice > prevLowPivot ? "HL" : "LL");
                    var t = Draw.Text(this, $"LBL_UP_{upAnchorBar}", lbl, pivotBarsAgo, upAnchorPrice - yOff, LowLabelColor);
                    t.Font = lblFont; t.IsAutoScale = false;
                    prevLowPivot = upAnchorPrice;

                    // seed & pivot point
                    if (pivotBarsAgo <= CurrentBar && Volume.Count > pivotBarsAgo)
                    {
                        upEW_P = upAnchorPrice * Convert.ToDouble(Volume[pivotBarsAgo]);
                        upEW_V = Convert.ToDouble(Volume[pivotBarsAgo]);
                        WriteUp(pivotBarsAgo, upEW_V > 0.0 ? upEW_P / upEW_V : double.NaN);
                    }
                    // backfill pivot+1 → now
                    for (int idx = upAnchorBar + 1; idx <= CurrentBar; idx++)
                    {
                        int ba = CurrentBar - idx;
                        double a = AlphaFromAPT(APTAt(ba));
                        double pxv = HLC(this, ba) * Convert.ToDouble(Volume[ba]);
                        double vv  = Convert.ToDouble(Volume[ba]);
                        upEW_P = (1 - a) * upEW_P + a * pxv;
                        upEW_V = (1 - a) * upEW_V + a * vv;
                        WriteUp(ba, upEW_V > 0.0 ? upEW_P / upEW_V : double.NaN);
                    }
                }
                else // new DOWN leg from last swing HIGH
                {
                    if (!double.IsNaN(prevVal))
                    {
                        tailUpValue   = prevVal;
                        tailUpRemain  = Math.Max(tailUpRemain, TailBars);
                        Values[0][0]  = tailUpValue; // stamp immediately
                    }

                    dnAnchorBar   = lastSwingHighBar;
                    dnAnchorPrice = lastSwingHigh;
                    int pivotBarsAgo = Clamp(CurrentBar - dnAnchorBar, 0, CurrentBar);

                    ClearForwardFromPivotInclusive(pivotBarsAgo, 0, keepBar0:true);

                    string lbl = double.IsNaN(prevHighPivot) ? "HH" : (dnAnchorPrice > prevHighPivot ? "HH" : "LH");
                    var t = Draw.Text(this, $"LBL_DN_{dnAnchorBar}", lbl, pivotBarsAgo, dnAnchorPrice + yOff, HighLabelColor);
                    t.Font = lblFont; t.IsAutoScale = false;
                    prevHighPivot = dnAnchorPrice;

                    if (pivotBarsAgo <= CurrentBar && Volume.Count > pivotBarsAgo)
                    {
                        dnEW_P = dnAnchorPrice * Convert.ToDouble(Volume[pivotBarsAgo]);
                        dnEW_V = Convert.ToDouble(Volume[pivotBarsAgo]);
                        WriteDn(pivotBarsAgo, dnEW_V > 0.0 ? dnEW_P / dnEW_V : double.NaN);
                    }
                    for (int idx = dnAnchorBar + 1; idx <= CurrentBar; idx++)
                    {
                        int ba = CurrentBar - idx;
                        double a = AlphaFromAPT(APTAt(ba));
                        double pxv = HLC(this, ba) * Convert.ToDouble(Volume[ba]);
                        double vv  = Convert.ToDouble(Volume[ba]);
                        dnEW_P = (1 - a) * dnEW_P + a * pxv;
                        dnEW_V = (1 - a) * dnEW_V + a * vv;
                        WriteDn(ba, dnEW_V > 0.0 ? dnEW_P / dnEW_V : double.NaN);
                    }
                }

                // we’ve handled pivot/backfill; return so live update doesn’t overwrite tails just stamped
                return;
            }

            // ----- 3) Live update for active engine
            double alphaNow = AlphaFromAPT(APTAt(0));
            double pxvNow   = HLC(this, 0) * Convert.ToDouble(Volume[0]);
            double vNow     = Convert.ToDouble(Volume[0]);

            if (dir > 0 && upAnchorBar >= 0)
            {
                if (double.IsNaN(upEW_P) || double.IsNaN(upEW_V)) { upEW_P = pxvNow; upEW_V = vNow; }
                else { upEW_P = (1 - alphaNow) * upEW_P + alphaNow * pxvNow; upEW_V = (1 - alphaNow) * upEW_V + alphaNow * vNow; }
                WriteUp(0, (upEW_V > 0.0) ? upEW_P / upEW_V : double.NaN);
            }
            else if (dir < 0 && dnAnchorBar >= 0)
            {
                if (double.IsNaN(dnEW_P) || double.IsNaN(dnEW_V)) { dnEW_P = pxvNow; dnEW_V = vNow; }
                else { dnEW_P = (1 - alphaNow) * dnEW_P + alphaNow * pxvNow; dnEW_V = (1 - alphaNow) * dnEW_V + alphaNow * vNow; }
                WriteDn(0, (dnEW_V > 0.0) ? dnEW_P / dnEW_V : double.NaN);
            }
            else
            {
                Values[0][0] = Values[1][0] = double.NaN;
            }

            // ----- 4) Keep tails printing (they’re independent of direction)
            if (tailUpRemain > 0 && !double.IsNaN(tailUpValue)) Values[0][0] = tailUpValue;
            if (tailDnRemain > 0 && !double.IsNaN(tailDnValue)) Values[1][0] = tailDnValue;

            // Clean opposite plot ONLY if it has no tail
            if (dir > 0 && tailDnRemain <= 0) Values[1][0] = double.NaN;
            if (dir < 0 && tailUpRemain <= 0) Values[0][0] = double.NaN;

            // styling
            Plots[0].Brush = VwapUpColor;   Plots[0].Width = VwapWidth;
            Plots[1].Brush = VwapDownColor; Plots[1].Width = VwapWidth;
        }
		
		 protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
{
    base.OnRender(chartControl, chartScale);

    if (IsInHitTest || Bars == null || ChartPanel == null)
        return;

    // Use NinjaTrader's built-in method for fixed text drawing with bold font
    Draw.TextFixed(
        this,
        "Watermark",
        watermarkText,
        TextPosition.BottomLeft,
        Brushes.SeaGreen,
        new SimpleFont("Arial", 10) { Bold = false }, // Set the font to bold
        Brushes.Transparent,
        Brushes.Transparent,
        0);
}

		
		

        [Browsable(false), XmlIgnore] public Series<double> VWAP_Up   => Values[0];
        [Browsable(false), XmlIgnore] public Series<double> VWAP_Down => Values[1];
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduS_DynamicSwingAVWAP[] cacheEduS_DynamicSwingAVWAP;
		public EduS_Trader.EduS_DynamicSwingAVWAP EduS_DynamicSwingAVWAP(int swingPeriod, int baseAPT, bool useAdaptAPT, double volatilityBias, int vwapWidth, int tailBars, int labelSize, bool labelBold, double labelOffset, string discordDiscord)
		{
			return EduS_DynamicSwingAVWAP(Input, swingPeriod, baseAPT, useAdaptAPT, volatilityBias, vwapWidth, tailBars, labelSize, labelBold, labelOffset, discordDiscord);
		}

		public EduS_Trader.EduS_DynamicSwingAVWAP EduS_DynamicSwingAVWAP(ISeries<double> input, int swingPeriod, int baseAPT, bool useAdaptAPT, double volatilityBias, int vwapWidth, int tailBars, int labelSize, bool labelBold, double labelOffset, string discordDiscord)
		{
			if (cacheEduS_DynamicSwingAVWAP != null)
				for (int idx = 0; idx < cacheEduS_DynamicSwingAVWAP.Length; idx++)
					if (cacheEduS_DynamicSwingAVWAP[idx] != null && cacheEduS_DynamicSwingAVWAP[idx].SwingPeriod == swingPeriod && cacheEduS_DynamicSwingAVWAP[idx].BaseAPT == baseAPT && cacheEduS_DynamicSwingAVWAP[idx].UseAdaptAPT == useAdaptAPT && cacheEduS_DynamicSwingAVWAP[idx].VolatilityBias == volatilityBias && cacheEduS_DynamicSwingAVWAP[idx].VwapWidth == vwapWidth && cacheEduS_DynamicSwingAVWAP[idx].TailBars == tailBars && cacheEduS_DynamicSwingAVWAP[idx].LabelSize == labelSize && cacheEduS_DynamicSwingAVWAP[idx].LabelBold == labelBold && cacheEduS_DynamicSwingAVWAP[idx].LabelOffset == labelOffset && cacheEduS_DynamicSwingAVWAP[idx].DiscordDiscord == discordDiscord && cacheEduS_DynamicSwingAVWAP[idx].EqualsInput(input))
						return cacheEduS_DynamicSwingAVWAP[idx];
			return CacheIndicator<EduS_Trader.EduS_DynamicSwingAVWAP>(new EduS_Trader.EduS_DynamicSwingAVWAP(){ SwingPeriod = swingPeriod, BaseAPT = baseAPT, UseAdaptAPT = useAdaptAPT, VolatilityBias = volatilityBias, VwapWidth = vwapWidth, TailBars = tailBars, LabelSize = labelSize, LabelBold = labelBold, LabelOffset = labelOffset, DiscordDiscord = discordDiscord }, input, ref cacheEduS_DynamicSwingAVWAP);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduS_DynamicSwingAVWAP EduS_DynamicSwingAVWAP(int swingPeriod, int baseAPT, bool useAdaptAPT, double volatilityBias, int vwapWidth, int tailBars, int labelSize, bool labelBold, double labelOffset, string discordDiscord)
		{
			return indicator.EduS_DynamicSwingAVWAP(Input, swingPeriod, baseAPT, useAdaptAPT, volatilityBias, vwapWidth, tailBars, labelSize, labelBold, labelOffset, discordDiscord);
		}

		public Indicators.EduS_Trader.EduS_DynamicSwingAVWAP EduS_DynamicSwingAVWAP(ISeries<double> input , int swingPeriod, int baseAPT, bool useAdaptAPT, double volatilityBias, int vwapWidth, int tailBars, int labelSize, bool labelBold, double labelOffset, string discordDiscord)
		{
			return indicator.EduS_DynamicSwingAVWAP(input, swingPeriod, baseAPT, useAdaptAPT, volatilityBias, vwapWidth, tailBars, labelSize, labelBold, labelOffset, discordDiscord);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduS_DynamicSwingAVWAP EduS_DynamicSwingAVWAP(int swingPeriod, int baseAPT, bool useAdaptAPT, double volatilityBias, int vwapWidth, int tailBars, int labelSize, bool labelBold, double labelOffset, string discordDiscord)
		{
			return indicator.EduS_DynamicSwingAVWAP(Input, swingPeriod, baseAPT, useAdaptAPT, volatilityBias, vwapWidth, tailBars, labelSize, labelBold, labelOffset, discordDiscord);
		}

		public Indicators.EduS_Trader.EduS_DynamicSwingAVWAP EduS_DynamicSwingAVWAP(ISeries<double> input , int swingPeriod, int baseAPT, bool useAdaptAPT, double volatilityBias, int vwapWidth, int tailBars, int labelSize, bool labelBold, double labelOffset, string discordDiscord)
		{
			return indicator.EduS_DynamicSwingAVWAP(input, swingPeriod, baseAPT, useAdaptAPT, volatilityBias, vwapWidth, tailBars, labelSize, labelBold, labelOffset, discordDiscord);
		}
	}
}

#endregion
