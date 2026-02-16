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
	public class MDCKeltnerChannel : Indicator
	{
		private Series<double>		diff;
		private	EMA					emaDiff;
		private	EMA					emaTypical;
		private int					lastBar;
		private long				vlmDaily;
		private bool				flagTextVolPB;
		private int					cicloTime;
		private int 				cicloBar;
		private int					speedBar;
		//Agregado EduSTrader
		private ATR atrAlgo; // Variable para el indicador
		//Fin Agregado EduSTrader
		//************************************************
		protected override void OnStateChange()		
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Escriba la descripción de su nuevo cliente Indicador aquí.";
				Name										= "MDCKeltnerChannel";
				Calculate									= Calculate.OnEachTick;
				IsOverlay									= true;
				DisplayInDataBox							= true;
				DrawOnPricePanel							= true;
				DrawHorizontalGridLines						= true;
				DrawVerticalGridLines						= true;
				PaintPriceMarkers							= true;
				ScaleJustification							= NinjaTrader.Gui.Chart.ScaleJustification.Right;
				//Disable this property if your indicator requires custom values that cumulate with each new market data event. 
				//See Help Guide for additional information.
				IsSuspendedWhileInactive					= true;
				Show_KeltChannel							= true;
				Show_LR										= true;
				Show_SMA_1									= false;
				Show_SMA_2									= false;
				Show_EMADot									= false;
				Show_InfoMarket								= false;
				Color_InfoMarket							= Brushes.Silver;
				Show_Position								= TextPosition.BottomRight;
				CountDown									= true;
				ShowPercent									= false;
				Show_ChannelFill							= false;
				flagTextVolPB								= true;
				Color_ChannelFill							= Brushes.White;
				Opacity_ChannelFill							= 10;
				Period										= 52;
				OffsetMultiplier							= 3.5;
				Period_LR									= 89;
				Period_SMA_1								= 20;
				Period_SMA_2								= 80;
				Period_EMADot								= 1;
				lastBar										= 0;
				vlmDaily									= 0;
				cicloBar = 0;
				cicloTime = 0;
				
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
				diff				= new Series<double>(this);
				emaDiff				= EMA(diff, Period);
				emaTypical			= EMA(Typical, Period);
			}else if (State == State.DataLoaded)
			{
				cicloBar = 0;
				cicloTime = 0;
				//Agregado EdusTrader
				atrAlgo = ATR(ATR_Period); // Inicializamos el ATR con el periodo elegido
				//Fin Agregado EdusTrader
			}
		}

		protected override void OnBarUpdate()
		{			
			//3
			diff[0]			= High[0] - Low[0];

			double middle	= emaTypical[0];
			double offset	= emaDiff[0] * OffsetMultiplier;

			double upper	= middle + offset;
			double lower	= middle - offset;
			if(Show_KeltChannel)
			{
				Midline[0]		= middle;
				UpperBand[0]		= upper;
				LowerBand[0]		= lower;
			}
			if(Show_LR)
				LR[0] = LinReg(Input, Period_LR)[0];
			if(Show_SMA_1)
				SMA_1[0] = SMA(Input, Period_SMA_1)[0];
			if(Show_SMA_2)
				SMA_2[0] = SMA(Input, Period_SMA_2)[0];
			if(Show_EMADot)
				EMA_Dot[0] = EMA(Input, Period_EMADot)[0];	
			
			//2
			if(Show_ChannelFill)
				Draw.Region(this, @"MDCKeltner3.Channel FillChannel_1", CurrentBar, 0, UpperBand, LowerBand, null, Color_ChannelFill, Opacity_ChannelFill);
			//1
			if(Show_InfoMarket)
			{
				double periodValue = (BarsPeriod.BarsPeriodType == BarsPeriodType.Tick) ? BarsPeriod.Value : BarsPeriod.BaseBarsPeriodValue;
				double tickCount = ShowPercent ? CountDown ? (1 - Bars.PercentComplete) * 100 : Bars.PercentComplete * 100 : CountDown ? periodValue - Bars.TickCount : Bars.TickCount;
				
				//Agregado EdusTrader
				double atrValue = atrAlgo[0];
    			string atrString = Instrument.MasterInstrument.FormatPrice(atrValue); // Formato de precio correcto (ticks/puntos)
				//string atrString = (atrAlgo[0] / TickSize).ToString("0"); // Si se quiere el valor en Ticks y no en Puntos
				//Fin Agregado EdusTrader
				
				string tick1 = (BarsPeriod.BarsPeriodType == BarsPeriodType.Tick 
							|| ((BarsPeriod.BarsPeriodType == BarsPeriodType.HeikenAshi || BarsPeriod.BarsPeriodType == BarsPeriodType.Volumetric) && BarsPeriod.BaseBarsPeriodType == BarsPeriodType.Tick) ? ((CountDown
											? NinjaTrader.Custom.Resource.TickCounterTicksRemaining + tickCount : NinjaTrader.Custom.Resource.TickCounterTickCount + tickCount) + (ShowPercent ? "%" : ""))
											: NinjaTrader.Custom.Resource.TickCounterBarError);
				
				try{
					vlmDaily = Instrument.MarketData.DailyVolume.Volume;
				}catch(Exception ex1){
					if(ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Hour == 18 && ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Minute >= 00 && ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Second >= 00 && flagTextVolPB)
					{
						if(lastBar != CurrentBar)
						{
							flagTextVolPB = false;
							vlmDaily = Bars.GetVolume(lastBar);
							lastBar = CurrentBar;
						}
					}else if(ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Hour > 18 && !flagTextVolPB)
					{
						flagTextVolPB = true;
					}
						
					if(lastBar != CurrentBar)
					{
						vlmDaily += Bars.GetVolume(lastBar);
						lastBar = CurrentBar;
					}
					
				}
				if((cicloTime + 1) >= 60)
				{
					if(Bars.GetTime(CurrentBar).Minute == 0)
					{
						speedBar = CurrentBar - cicloBar;
						cicloTime = Bars.GetTime(CurrentBar).Minute;
						cicloBar = CurrentBar;
					}else{
						cicloTime = Bars.GetTime(CurrentBar).Minute;
						cicloBar = CurrentBar;
					}
						
				}else if(Bars.GetTime(CurrentBar).Minute == (cicloTime + 1))
				{
					speedBar = CurrentBar - cicloBar;
					cicloTime = Bars.GetTime(CurrentBar).Minute;
					cicloBar = CurrentBar;
				}else if(Bars.GetTime(CurrentBar).Minute > (cicloTime + 1))
				{
					cicloTime = Bars.GetTime(CurrentBar).Minute;
					cicloBar = CurrentBar;
				}else if(Bars.GetTime(CurrentBar).Minute < (cicloTime))
				{
					cicloTime = Bars.GetTime(CurrentBar).Minute;
					cicloBar = CurrentBar;
				}
//				Draw.TextFixed(this, "PruebaTextoSpeed", "cicloTime: " + cicloTime + " CurrentBatMinute: " + Bars.GetTime(CurrentBar).Minute + "\n" + 
//														" CurrentBar: " + CurrentBar + " cicloBar: " + cicloBar , TextPosition.BottomLeft);
				try{
					Draw.TextFixed(this, @"MDCKeltnerChannel TickConuter1", "BandSp = " + ((int)((upper - lower)*100))/100.0 + 
																			"\nVolumen = " + String.Format("{0:#,###,000}", vlmDaily) +
																			"\n" + tick1 +
																			"\nBars/min = " + speedBar +
																			//Agregado EdusTrader
																			"\nATR (" + ATR_Period + "): " + atrString + // <--- NUEVA LÍNEA AGREGADA	
																			//Fin Agregado EdusTrader
																			"\nHora = " + String.Format("{0:00}", Bars.GetTime(CurrentBar).Hour) + ":" + 
																			String.Format("{0:00}", Bars.GetTime(CurrentBar).Minute) + ":" + 
																			String.Format("{0:00}", Bars.GetTime(CurrentBar).Second), 
																			Show_Position, //TextPosition.BottomRight, 
																			Color_InfoMarket, //ChartControl.Properties.ChartText, 
																			ChartControl.Properties.LabelFont, 
																			Brushes.Transparent, 
																			Brushes.Transparent, 
																			0);
				}catch(Exception ex1){}
			}
		}

		#region Properties
		[NinjaScriptProperty]
		[Display(Name="Show_KeltChannel", Order=1, GroupName="1.Parameters")]
		public bool Show_KeltChannel
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Period", Order=2, GroupName="1.Parameters")]
		public int Period
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, double.MaxValue)]
		[Display(Name="OffsetMultiplier", Order=3, GroupName="1.Parameters")]
		public double OffsetMultiplier
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Show_LR", Order=4, GroupName="1.Parameters")]
		public bool Show_LR
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Period_LR", Order=5, GroupName="1.Parameters")]
		public int Period_LR
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Show_SMA_1", Order=6, GroupName="1.Parameters")]
		public bool Show_SMA_1
		{ get; set; }
	
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Period_SMA_1", Order=7, GroupName="1.Parameters")]
		public int Period_SMA_1
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Show_SMA_2", Order=8, GroupName="1.Parameters")]
		public bool Show_SMA_2
		{ get; set; }

		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Period_SMA_2", Order=9, GroupName="1.Parameters")]
		public int Period_SMA_2
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Show_EMADot", Order=10, GroupName="1.Parameters")]
		public bool Show_EMADot
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Period_EMADot", Order=11, GroupName="1.Parameters")]
		public int Period_EMADot
		{ get; set; }

		[NinjaScriptProperty]
		[Display(Name="Show_InfoMarket", Order=12, GroupName="2.Show_Info.Market")]
		public bool Show_InfoMarket
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(ResourceType = typeof (Custom.Resource), Name = "CountDown" + "Ticks", Order = 13, GroupName = "2.Show_Info.Market")]
		public bool CountDown
		{ get; set; }

		[NinjaScriptProperty]
		[Display(ResourceType = typeof (Custom.Resource), Name = "ShowPercent" + "Ticks", Order = 14, GroupName = "2.Show_Info.Market")]
		public bool ShowPercent
		{ get; set; }
		
		//Agregado EduSTrader
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name = "Periodo ATR", Description = "Periodo para el cálculo del ATR en el Info Market", Order = 10, GroupName = "Info Market")]
		public int ATR_Period { get; set; } = 14;
		//Fin Agregado EduSTrader
		
		[XmlIgnore]
		[Display(Name="Color_InfoMarket", Order=15, GroupName="2.Show_Info.Market")]		
		public Brush Color_InfoMarket 
		{ get; set;}
		[Browsable(false)]
		public string Color_InfoMarketSerialize
		{
		  get { return Serialize.BrushToString(this.Color_InfoMarket); }
		  set { this.Color_InfoMarket = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[Display(Name="Show_InfoMarket", Order=16, GroupName="2.Show_Info.Market")]
		public TextPosition Show_Position
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Show_ChannelFill", Order=17, GroupName="3.Channel Fill")]
		public bool Show_ChannelFill
		{ get; set; }
		
		[XmlIgnore]
		[Display(Name="Color_ChannelFill", Description="Color 3.Channel Fill", Order=18, GroupName="3.Channel Fill")]		
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
		[Display(Name="Opacity_ChannelFill", Order=19, GroupName="3.Channel Fill")]
		public int Opacity_ChannelFill
		{ get; set; }
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> UpperBand
		{
			get { return Values[0]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> Midline
		{
			get { return Values[1]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> LowerBand
		{
			get { return Values[2]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> LR
		{
			get { return Values[3]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SMA_1
		{
			get { return Values[4]; }
		}

		[Browsable(false)]
		[XmlIgnore]
		public Series<double> SMA_2
		{
			get { return Values[5]; }
		}
		
		[Browsable(false)]
		[XmlIgnore]
		public Series<double> EMA_Dot
		{
			get { return Values[6]; }
		}
		
		#endregion
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MDCKeltnerChannel[] cacheMDCKeltnerChannel;
		public MDCKeltnerChannel MDCKeltnerChannel(bool show_KeltChannel, int period, double offsetMultiplier, bool show_LR, int period_LR, bool show_SMA_1, int period_SMA_1, bool show_SMA_2, int period_SMA_2, bool show_EMADot, int period_EMADot, bool show_InfoMarket, bool countDown, bool showPercent, int aTR_Period, TextPosition show_Position, bool show_ChannelFill, int opacity_ChannelFill)
		{
			return MDCKeltnerChannel(Input, show_KeltChannel, period, offsetMultiplier, show_LR, period_LR, show_SMA_1, period_SMA_1, show_SMA_2, period_SMA_2, show_EMADot, period_EMADot, show_InfoMarket, countDown, showPercent, aTR_Period, show_Position, show_ChannelFill, opacity_ChannelFill);
		}

		public MDCKeltnerChannel MDCKeltnerChannel(ISeries<double> input, bool show_KeltChannel, int period, double offsetMultiplier, bool show_LR, int period_LR, bool show_SMA_1, int period_SMA_1, bool show_SMA_2, int period_SMA_2, bool show_EMADot, int period_EMADot, bool show_InfoMarket, bool countDown, bool showPercent, int aTR_Period, TextPosition show_Position, bool show_ChannelFill, int opacity_ChannelFill)
		{
			if (cacheMDCKeltnerChannel != null)
				for (int idx = 0; idx < cacheMDCKeltnerChannel.Length; idx++)
					if (cacheMDCKeltnerChannel[idx] != null && cacheMDCKeltnerChannel[idx].Show_KeltChannel == show_KeltChannel && cacheMDCKeltnerChannel[idx].Period == period && cacheMDCKeltnerChannel[idx].OffsetMultiplier == offsetMultiplier && cacheMDCKeltnerChannel[idx].Show_LR == show_LR && cacheMDCKeltnerChannel[idx].Period_LR == period_LR && cacheMDCKeltnerChannel[idx].Show_SMA_1 == show_SMA_1 && cacheMDCKeltnerChannel[idx].Period_SMA_1 == period_SMA_1 && cacheMDCKeltnerChannel[idx].Show_SMA_2 == show_SMA_2 && cacheMDCKeltnerChannel[idx].Period_SMA_2 == period_SMA_2 && cacheMDCKeltnerChannel[idx].Show_EMADot == show_EMADot && cacheMDCKeltnerChannel[idx].Period_EMADot == period_EMADot && cacheMDCKeltnerChannel[idx].Show_InfoMarket == show_InfoMarket && cacheMDCKeltnerChannel[idx].CountDown == countDown && cacheMDCKeltnerChannel[idx].ShowPercent == showPercent && cacheMDCKeltnerChannel[idx].ATR_Period == aTR_Period && cacheMDCKeltnerChannel[idx].Show_Position == show_Position && cacheMDCKeltnerChannel[idx].Show_ChannelFill == show_ChannelFill && cacheMDCKeltnerChannel[idx].Opacity_ChannelFill == opacity_ChannelFill && cacheMDCKeltnerChannel[idx].EqualsInput(input))
						return cacheMDCKeltnerChannel[idx];
			return CacheIndicator<MDCKeltnerChannel>(new MDCKeltnerChannel(){ Show_KeltChannel = show_KeltChannel, Period = period, OffsetMultiplier = offsetMultiplier, Show_LR = show_LR, Period_LR = period_LR, Show_SMA_1 = show_SMA_1, Period_SMA_1 = period_SMA_1, Show_SMA_2 = show_SMA_2, Period_SMA_2 = period_SMA_2, Show_EMADot = show_EMADot, Period_EMADot = period_EMADot, Show_InfoMarket = show_InfoMarket, CountDown = countDown, ShowPercent = showPercent, ATR_Period = aTR_Period, Show_Position = show_Position, Show_ChannelFill = show_ChannelFill, Opacity_ChannelFill = opacity_ChannelFill }, input, ref cacheMDCKeltnerChannel);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MDCKeltnerChannel MDCKeltnerChannel(bool show_KeltChannel, int period, double offsetMultiplier, bool show_LR, int period_LR, bool show_SMA_1, int period_SMA_1, bool show_SMA_2, int period_SMA_2, bool show_EMADot, int period_EMADot, bool show_InfoMarket, bool countDown, bool showPercent, int aTR_Period, TextPosition show_Position, bool show_ChannelFill, int opacity_ChannelFill)
		{
			return indicator.MDCKeltnerChannel(Input, show_KeltChannel, period, offsetMultiplier, show_LR, period_LR, show_SMA_1, period_SMA_1, show_SMA_2, period_SMA_2, show_EMADot, period_EMADot, show_InfoMarket, countDown, showPercent, aTR_Period, show_Position, show_ChannelFill, opacity_ChannelFill);
		}

		public Indicators.MDCKeltnerChannel MDCKeltnerChannel(ISeries<double> input , bool show_KeltChannel, int period, double offsetMultiplier, bool show_LR, int period_LR, bool show_SMA_1, int period_SMA_1, bool show_SMA_2, int period_SMA_2, bool show_EMADot, int period_EMADot, bool show_InfoMarket, bool countDown, bool showPercent, int aTR_Period, TextPosition show_Position, bool show_ChannelFill, int opacity_ChannelFill)
		{
			return indicator.MDCKeltnerChannel(input, show_KeltChannel, period, offsetMultiplier, show_LR, period_LR, show_SMA_1, period_SMA_1, show_SMA_2, period_SMA_2, show_EMADot, period_EMADot, show_InfoMarket, countDown, showPercent, aTR_Period, show_Position, show_ChannelFill, opacity_ChannelFill);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MDCKeltnerChannel MDCKeltnerChannel(bool show_KeltChannel, int period, double offsetMultiplier, bool show_LR, int period_LR, bool show_SMA_1, int period_SMA_1, bool show_SMA_2, int period_SMA_2, bool show_EMADot, int period_EMADot, bool show_InfoMarket, bool countDown, bool showPercent, int aTR_Period, TextPosition show_Position, bool show_ChannelFill, int opacity_ChannelFill)
		{
			return indicator.MDCKeltnerChannel(Input, show_KeltChannel, period, offsetMultiplier, show_LR, period_LR, show_SMA_1, period_SMA_1, show_SMA_2, period_SMA_2, show_EMADot, period_EMADot, show_InfoMarket, countDown, showPercent, aTR_Period, show_Position, show_ChannelFill, opacity_ChannelFill);
		}

		public Indicators.MDCKeltnerChannel MDCKeltnerChannel(ISeries<double> input , bool show_KeltChannel, int period, double offsetMultiplier, bool show_LR, int period_LR, bool show_SMA_1, int period_SMA_1, bool show_SMA_2, int period_SMA_2, bool show_EMADot, int period_EMADot, bool show_InfoMarket, bool countDown, bool showPercent, int aTR_Period, TextPosition show_Position, bool show_ChannelFill, int opacity_ChannelFill)
		{
			return indicator.MDCKeltnerChannel(input, show_KeltChannel, period, offsetMultiplier, show_LR, period_LR, show_SMA_1, period_SMA_1, show_SMA_2, period_SMA_2, show_EMADot, period_EMADot, show_InfoMarket, countDown, showPercent, aTR_Period, show_Position, show_ChannelFill, opacity_ChannelFill);
		}
	}
}

#endregion
