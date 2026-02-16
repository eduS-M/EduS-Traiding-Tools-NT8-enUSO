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

//This namespace holds Indicators in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Indicators
{
	public class MDCMACDBBlines : Indicator
	{
		//************************************
		private	Series<double>		fastEma;
		private	Series<double>		slowEma;
		private double				constant1;
		private double				constant2;
		private double				constant3;
		private double				constant4;
		private double				constant5;
		private double				constant6;
		
		private EMA					EMA_aux;
		private StdDev				StdDev_aux;
		
		private Series<double> 		bbMacd;
		//*************************************		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Escriba la descripción de su nuevo cliente Indicador aquí.";
				Name										= "MDCMACDBBlines";
				Calculate									= Calculate.OnBarClose;
				IsOverlay									= false;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= false;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
				Show_ChannelFill							= false;
				show_Areas									= false;
				Color_ChannelFill							= Brushes.White;
				Opacity_ChannelFill							= 10;
				show_Crosses								= false;
				Color_CrossUp								= Brushes.CornflowerBlue;
				Color_CrossDown								= Brushes.Red;
				Style_Cross									= DashStyleHelper.Solid;
				Width_Cross									= 1;
				Fast										= 12;
				Slow										= 26;
				Smooth										= 5;
				Length										= 10;
				Stdev										= 1;
				UpperLine									= 0.3;
				LowerLine									= -0.3;
				High_Areas									= 0.4;
				Color_Areas									= Brushes.Thistle;
				Opacity_Areas								= 20;
				
				AddLine(Brushes.Tan, UpperLine, "UpperLine0.3");
				AddLine(Brushes.Tan, LowerLine, "LowerLine-0.3");
				AddPlot(Brushes.Gray, "BBsUpperBand");
				AddPlot(Brushes.DimGray, "BBsLowerBand");
				AddPlot(Brushes.DarkGray, "ZeroLine");
				AddPlot(new Stroke(Brushes.WhiteSmoke, 1),		PlotStyle.Line,	"BBsLineIn");
				AddPlot(new Stroke(Brushes.GreenYellow, 2),		PlotStyle.Dot,	"BBsLineUp");
				AddPlot(new Stroke(Brushes.Red, 2),		PlotStyle.Dot,	"BBsLineDown");
				
				//*********************
//				AddPlot(Brushes.Crimson,									NinjaTrader.Custom.Resource.NinjaScriptIndicatorAvg);
//				AddPlot(new Stroke(Brushes.DodgerBlue, 2),	PlotStyle.Bar,	NinjaTrader.Custom.Resource.NinjaScriptIndicatorDiff);
				AddLine(Brushes.DarkGray,					0,				NinjaTrader.Custom.Resource.NinjaScriptIndicatorZeroLine);
				//***********************
			}
			else if (State == State.Configure)
			{
				constant1	= 2.0 / (1 + Fast);
				constant2	= (1 - (2.0 / (1 + Fast)));
				constant3	= 2.0 / (1 + Slow);
				constant4	= (1 - (2.0 / (1 + Slow)));
				constant5	= 2.0 / (1 + Smooth);
				constant6	= (1 - (2.0 / (1 + Smooth)));
			}
			else if (State == State.DataLoaded)
			{
				fastEma = new Series<double>(this);
				slowEma = new Series<double>(this);
				bbMacd = new Series<double>(this);
			}
		}

		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) 
				return;

			if (CurrentBars[0] < 1)
				return;
			
			double input0	= Input[0];

			if (CurrentBar == 0)
			{
				fastEma[0]		= input0;
				slowEma[0]		= input0;
				Value[0]		= 0;
//				Avg[0]			= 0;
//				Diff[0]			= 0;
			}
			else
			{
				double fastEma0	= constant1 * input0 + constant2 * fastEma[1];
				double slowEma0	= constant3 * input0 + constant4 * slowEma[1];
				double macd		= fastEma0 - slowEma0;
//				double macdAvg	= constant5 * macd + constant6 * Avg[1];

				fastEma[0]		= fastEma0;
				slowEma[0]		= slowEma0;
				Value[0]		= macd;
//				Avg[0]			= macdAvg;
//				Diff[0]			= macd - macdAvg;
				
				//*********************************
				bbMacd[0] = MACD(Input, Fast, Slow, Smooth)[0];
				
				EMA_aux = EMA(bbMacd, Length);
				StdDev_aux = StdDev(bbMacd, Length);
				double AvgBand = EMA_aux[0];
				double SDevBand = StdDev_aux[0];
				double upperBand = AvgBand + (Stdev * SDevBand);
				double lowerBand = AvgBand - (Stdev * SDevBand);
				
				BBsUpperBand[0] = upperBand;
				BBsLowerBand[0] = lowerBand;
				ZeroLine[0] = 0;
				BBsLineIn[0]	= macd;				
				
				if (BBsLineIn[0] > BBsLineIn[1])
					BBsLineUp[0] = macd;
				else
					BBsLineDown[0] = macd;
				
				if(Show_ChannelFill)
					Draw.Region(this, @"MDCMACDBBlines FillChannel_1", CurrentBar, 0, BBsUpperBand, BBsLowerBand, null, Color_ChannelFill, Opacity_ChannelFill);
			}
			 // Establecer 1
			if(show_Crosses)
			{
				if (CrossAbove(BBsLineIn, ZeroLine, 1))
				{
					Draw.VerticalLine(this, @"cruceLiena Línea vertical_1" + CurrentBar, 0, Color_CrossUp, Style_Cross, Width_Cross, false);
				}
				if (CrossBelow(BBsLineIn, ZeroLine, 1))
				{
					Draw.VerticalLine(this, @"cruceLiena Línea vertical_1" + CurrentBar, 0, Color_CrossDown, Style_Cross, Width_Cross, false);					
				}
			}
			if(show_Areas)
			{
				Draw.RegionHighlightY(this, @"MDCMACDBBlines RegionY3040", false, 0.3, High_Areas, null, Color_Areas, Opacity_Areas);
				Draw.RegionHighlightY(this, @"MDCMACDBBlines RegionY-40-30", false, -High_Areas, -0.3, null, Color_Areas, Opacity_Areas);
			}			
		}

		#region Properties
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Fast", Order=1, GroupName="1.Parameters")]
		public int Fast
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Slow", Order=2, GroupName="1.Parameters")]
		public int Slow
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Smooth", Order=3, GroupName="1.Parameters")]
		public int Smooth
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Length", Order=4, GroupName="1.Parameters")]
		public int Length
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Stdev", Order=5, GroupName="1.Parameters")]
		public int Stdev
		{ get; set; }

		[NinjaScriptProperty]
		[Range(double.MinValue, double.MaxValue)]
		[Display(Name="UpperLine", Order=6, GroupName="1.Parameters")]
		public double UpperLine
		{ get; set; }

		[NinjaScriptProperty]
		[Range(double.MinValue, double.MaxValue)]
		[Display(Name="LowerLine", Order=7, GroupName="1.Parameters")]
		public double LowerLine
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="ShowCross", Order=8, GroupName="2.Cross ZL")]
		public bool show_Crosses
		{ get; set; }
		
		[XmlIgnore]
		[Display(Name="Color_CrossUp", Description="Color Cross Up", Order=9, GroupName="2.Cross ZL")]		
		public Brush Color_CrossUp
		{ get; set; }
		[Browsable(false)]
		public string Color_CrossUpSerialize
		{
		  get { return Serialize.BrushToString(this.Color_CrossUp); }
		  set { this.Color_CrossUp = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(Name="Color_CrossDown", Description="Color Cross Down", Order=10, GroupName="2.Cross ZL")]		
		public Brush Color_CrossDown
		{ get; set; }
		[Browsable(false)]
		public string Color_CrossDownSerialize
		{
		  get { return Serialize.BrushToString(this.Color_CrossDown); }
		  set { this.Color_CrossDown = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(Name="Style_Cross", Description="Style Cross", Order=11, GroupName="2.Cross ZL")]		
		public DashStyleHelper Style_Cross
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Width_Cross", Order=12, GroupName="2.Cross ZL")]
		public int Width_Cross
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Show_ChannelFill", Order=13, GroupName="3.Channel Fill")]
		public bool Show_ChannelFill
		{ get; set; }
		
		[XmlIgnore]
		[Display(Name="Color_ChannelFill", Description="Color Channel Fill", Order=14, GroupName="3.Channel Fill")]		
		public Brush Color_ChannelFill
		{ get; set; }
		[Browsable(false)]
		public string Color_ChannelFillSerialize
		{
		  get { return Serialize.BrushToString(this.Color_ChannelFill); }
		  set { this.Color_ChannelFill = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name="Opacity_ChannelFill", Order=15, GroupName="3.Channel Fill")]
		public int Opacity_ChannelFill
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="show_Areas", Order=16, GroupName="4.Areas")]
		public bool show_Areas
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(0.3, double.MaxValue)]
		[Display(Name="High_Areas", Order=17, GroupName="4.Areas")]
		public double High_Areas
		{ get; set; }
		
		[XmlIgnore]
		[Display(Name="Color_Areas", Description="Color Areas", Order=18, GroupName="4.Areas")]		
		public Brush Color_Areas
		{ get; set; }
		[Browsable(false)]
		public string Color_AreasSerialize
		{
		  get { return Serialize.BrushToString(this.Color_Areas); }
		  set { this.Color_Areas = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[Range(0, 100)]
		[Display(Name="Opacity_Areas", Order=19, GroupName="4.Areas")]
		public int Opacity_Areas
		{ get; set; }
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> BBsUpperBand
		{
			get { return Values[0]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> BBsLowerBand
		{
			get { return Values[1]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> ZeroLine
		{
			get { return Values[2]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> BBsLineIn
		{
			get { return Values[3]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> BBsLineUp
		{
			get { return Values[4]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> BBsLineDown
		{
			get { return Values[5]; }
		}
		
		//***********************
//		[Browsable(false)]
//		[XmlIgnore]
//		public Series<double> Avg
//		{
//			get { return Values[1]; }
//		}
//		[Browsable(false)]
//		[XmlIgnore]
//		public Series<double> Diff
//		{
//			get { return Values[2]; }
//		}
		#endregion

	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MDCMACDBBlines[] cacheMDCMACDBBlines;
		public MDCMACDBBlines MDCMACDBBlines(int fast, int slow, int smooth, int length, int stdev, double upperLine, double lowerLine, bool show_Crosses, int width_Cross, bool show_ChannelFill, int opacity_ChannelFill, bool show_Areas, double high_Areas, int opacity_Areas)
		{
			return MDCMACDBBlines(Input, fast, slow, smooth, length, stdev, upperLine, lowerLine, show_Crosses, width_Cross, show_ChannelFill, opacity_ChannelFill, show_Areas, high_Areas, opacity_Areas);
		}

		public MDCMACDBBlines MDCMACDBBlines(ISeries<double> input, int fast, int slow, int smooth, int length, int stdev, double upperLine, double lowerLine, bool show_Crosses, int width_Cross, bool show_ChannelFill, int opacity_ChannelFill, bool show_Areas, double high_Areas, int opacity_Areas)
		{
			if (cacheMDCMACDBBlines != null)
				for (int idx = 0; idx < cacheMDCMACDBBlines.Length; idx++)
					if (cacheMDCMACDBBlines[idx] != null && cacheMDCMACDBBlines[idx].Fast == fast && cacheMDCMACDBBlines[idx].Slow == slow && cacheMDCMACDBBlines[idx].Smooth == smooth && cacheMDCMACDBBlines[idx].Length == length && cacheMDCMACDBBlines[idx].Stdev == stdev && cacheMDCMACDBBlines[idx].UpperLine == upperLine && cacheMDCMACDBBlines[idx].LowerLine == lowerLine && cacheMDCMACDBBlines[idx].show_Crosses == show_Crosses && cacheMDCMACDBBlines[idx].Width_Cross == width_Cross && cacheMDCMACDBBlines[idx].Show_ChannelFill == show_ChannelFill && cacheMDCMACDBBlines[idx].Opacity_ChannelFill == opacity_ChannelFill && cacheMDCMACDBBlines[idx].show_Areas == show_Areas && cacheMDCMACDBBlines[idx].High_Areas == high_Areas && cacheMDCMACDBBlines[idx].Opacity_Areas == opacity_Areas && cacheMDCMACDBBlines[idx].EqualsInput(input))
						return cacheMDCMACDBBlines[idx];
			return CacheIndicator<MDCMACDBBlines>(new MDCMACDBBlines(){ Fast = fast, Slow = slow, Smooth = smooth, Length = length, Stdev = stdev, UpperLine = upperLine, LowerLine = lowerLine, show_Crosses = show_Crosses, Width_Cross = width_Cross, Show_ChannelFill = show_ChannelFill, Opacity_ChannelFill = opacity_ChannelFill, show_Areas = show_Areas, High_Areas = high_Areas, Opacity_Areas = opacity_Areas }, input, ref cacheMDCMACDBBlines);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MDCMACDBBlines MDCMACDBBlines(int fast, int slow, int smooth, int length, int stdev, double upperLine, double lowerLine, bool show_Crosses, int width_Cross, bool show_ChannelFill, int opacity_ChannelFill, bool show_Areas, double high_Areas, int opacity_Areas)
		{
			return indicator.MDCMACDBBlines(Input, fast, slow, smooth, length, stdev, upperLine, lowerLine, show_Crosses, width_Cross, show_ChannelFill, opacity_ChannelFill, show_Areas, high_Areas, opacity_Areas);
		}

		public Indicators.MDCMACDBBlines MDCMACDBBlines(ISeries<double> input , int fast, int slow, int smooth, int length, int stdev, double upperLine, double lowerLine, bool show_Crosses, int width_Cross, bool show_ChannelFill, int opacity_ChannelFill, bool show_Areas, double high_Areas, int opacity_Areas)
		{
			return indicator.MDCMACDBBlines(input, fast, slow, smooth, length, stdev, upperLine, lowerLine, show_Crosses, width_Cross, show_ChannelFill, opacity_ChannelFill, show_Areas, high_Areas, opacity_Areas);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MDCMACDBBlines MDCMACDBBlines(int fast, int slow, int smooth, int length, int stdev, double upperLine, double lowerLine, bool show_Crosses, int width_Cross, bool show_ChannelFill, int opacity_ChannelFill, bool show_Areas, double high_Areas, int opacity_Areas)
		{
			return indicator.MDCMACDBBlines(Input, fast, slow, smooth, length, stdev, upperLine, lowerLine, show_Crosses, width_Cross, show_ChannelFill, opacity_ChannelFill, show_Areas, high_Areas, opacity_Areas);
		}

		public Indicators.MDCMACDBBlines MDCMACDBBlines(ISeries<double> input , int fast, int slow, int smooth, int length, int stdev, double upperLine, double lowerLine, bool show_Crosses, int width_Cross, bool show_ChannelFill, int opacity_ChannelFill, bool show_Areas, double high_Areas, int opacity_Areas)
		{
			return indicator.MDCMACDBBlines(input, fast, slow, smooth, length, stdev, upperLine, lowerLine, show_Crosses, width_Cross, show_ChannelFill, opacity_ChannelFill, show_Areas, high_Areas, opacity_Areas);
		}
	}
}

#endregion
