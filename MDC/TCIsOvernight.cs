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
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

// This namespace holds all indicators and is required. Do not change it.
namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Paints the display when outside of day session hours. Copyright TradingCoders.com 2009.
	/// DO NOT REDISTRIBUTE. DO NOT REMOVE TradingCoders.com COPYRIGHT NOTICE. 
	/// NOT FOR RESALE.
    /// </summary>
    [Description("Paints the display when outside of day session hours. Courtesy of TradingCoders.com")]
	//[Gui.Design.DisplayName("TC_IsOvernight: TradingCoders.com")]
    public class TCIsOvernight : Indicator
    {
        #region Variables
		// Internal Variables;
		private Series<bool> IsOvernightDS;
		bool IsOvernight = false;
		
        #endregion

		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Initialize();	// call the original NT7 Initialize()
			}
		}
		
        /// <summary>
        /// This method is used to configure the indicator and is called once before any bar data is loaded.
        /// </summary>
        protected void Initialize()
        {
			Name = "TC_IsOvernight: TradingCoders.com";
            IsOverlay				= true;
			IsOvernightDS = new Series<bool>(this);
			
			TradingSessionOpen = 830;
			TradingSessionClose = 1500;
			OvernightColor = Brushes.Plum;
        }
		
		public override string DisplayName
		{
			get { return "TC_IsOvernight"; }
		}

        /// <summary>
        /// Called on each bar update event (incoming tick)
        /// </summary>
        protected override void OnBarUpdate()
        {
			CheckTime();
			IsOvernightDS[0] = (IsOvernight);	
		}
		
		// this method checks the open and closing times (USER INPUT) for the session.
		// It colors the background for overnight, and sets the boolean flag IsOvernight
		private void CheckTime()
		{

			int lOpen =  TradingSessionOpen * 100;
			int lClose = TradingSessionClose * 100;
			
			IsOvernight = false;		// initialise to false
			
			if (lOpen == lClose)
				return;	// nothing to do if both times the same
			
			Brush opaqueBrush = OvernightColor.Clone();
			opaqueBrush.Opacity = 0.33;
			opaqueBrush.Freeze();
			
			if (lOpen<lClose)
			{	// a "normal" day, that does not span midnight

				if ( (ToTime(Time[0]) > lOpen) && (ToTime(Time[0]) <= lClose)
					//&& (Time[0].DayOfWeek !=DayOfWeek.Saturday) && (Time[0].DayOfWeek != DayOfWeek.Sunday) )
					)
				{
					// It is a valid session time, do stuff here if required
				}
				else
				{
					if (OvernightColor != Brushes.Transparent)
						BackBrush = opaqueBrush;
					IsOvernight = true;
				}
			}
			else
			{	// a global session which DOES span midnight.

				if ( ((ToTime(Time[0]) > lOpen) || (ToTime(Time[0]) <= lClose))
					//&& (Time[0].DayOfWeek !=DayOfWeek.Saturday) && (Time[0].DayOfWeek != DayOfWeek.Sunday) )
					)
				{
					// it is a valid session time, do stuff here if required
				}
				else
				{
					if (OvernightColor != Brushes.Transparent)
						BackBrush = opaqueBrush;
					IsOvernight = true;
				}
				
			}

		}
		// **************************************************************************************************************88
		
        

        #region Properties

		[Browsable(false)]	// this line prevents the data series from being displayed in the indicator properties dialog, do not remove
        [XmlIgnore()]		// this line ensures that the indicator can be saved/recovered as part of a chart template, do not remove
        public Series<bool> Night
        {
            get { return IsOvernightDS; }
        }
		
		[Range(0, 2359), NinjaScriptProperty]
		[Display(Description = "Opening time for our trading session HHMM eg 0830",
		 ResourceType = typeof(Custom.Resource), Name = "TradingSessionOpen", GroupName = "NinjaScriptParameters", Order = 0)]
		public int TradingSessionOpen
		{ get; set; }
		
		
		[Range(0, 2359), NinjaScriptProperty]
		[Display(Description = "Closing time for our trading session HHMM eg 1500",
		 ResourceType = typeof(Custom.Resource), Name = "TradingSessionClose", GroupName = "NinjaScriptParameters", Order = 1)]
		public int TradingSessionClose
		{ get; set; }
		
				
		// ******************************************************************************	
	
		// Create our user definable color input
		[XmlIgnore()]        
		[NinjaScriptProperty]
		[Display(Description = "Color for Overnight. Set to 'Transparent' to disable coloring",
		 ResourceType = typeof(Custom.Resource), Name = "OvernightColor", GroupName = "NinjaScriptParameters", Order = 2)]
		public Brush OvernightColor
		{ get; set; }
		
		// Serialize our Color object
		[Browsable(false)]
		public string OvernightColorSerialize
		{
			get { return Serialize.BrushToString(OvernightColor); }
			set { OvernightColor = Serialize.StringToBrush(value); }
		}
		
		
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private TCIsOvernight[] cacheTCIsOvernight;
		public TCIsOvernight TCIsOvernight(int tradingSessionOpen, int tradingSessionClose, Brush overnightColor)
		{
			return TCIsOvernight(Input, tradingSessionOpen, tradingSessionClose, overnightColor);
		}

		public TCIsOvernight TCIsOvernight(ISeries<double> input, int tradingSessionOpen, int tradingSessionClose, Brush overnightColor)
		{
			if (cacheTCIsOvernight != null)
				for (int idx = 0; idx < cacheTCIsOvernight.Length; idx++)
					if (cacheTCIsOvernight[idx] != null && cacheTCIsOvernight[idx].TradingSessionOpen == tradingSessionOpen && cacheTCIsOvernight[idx].TradingSessionClose == tradingSessionClose && cacheTCIsOvernight[idx].OvernightColor == overnightColor && cacheTCIsOvernight[idx].EqualsInput(input))
						return cacheTCIsOvernight[idx];
			return CacheIndicator<TCIsOvernight>(new TCIsOvernight(){ TradingSessionOpen = tradingSessionOpen, TradingSessionClose = tradingSessionClose, OvernightColor = overnightColor }, input, ref cacheTCIsOvernight);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.TCIsOvernight TCIsOvernight(int tradingSessionOpen, int tradingSessionClose, Brush overnightColor)
		{
			return indicator.TCIsOvernight(Input, tradingSessionOpen, tradingSessionClose, overnightColor);
		}

		public Indicators.TCIsOvernight TCIsOvernight(ISeries<double> input , int tradingSessionOpen, int tradingSessionClose, Brush overnightColor)
		{
			return indicator.TCIsOvernight(input, tradingSessionOpen, tradingSessionClose, overnightColor);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.TCIsOvernight TCIsOvernight(int tradingSessionOpen, int tradingSessionClose, Brush overnightColor)
		{
			return indicator.TCIsOvernight(Input, tradingSessionOpen, tradingSessionClose, overnightColor);
		}

		public Indicators.TCIsOvernight TCIsOvernight(ISeries<double> input , int tradingSessionOpen, int tradingSessionClose, Brush overnightColor)
		{
			return indicator.TCIsOvernight(input, tradingSessionOpen, tradingSessionClose, overnightColor);
		}
	}
}

#endregion
