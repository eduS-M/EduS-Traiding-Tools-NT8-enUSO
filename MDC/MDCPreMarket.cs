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
	public class MDCPreMarket : Indicator
	{
		private MDCKeltnerChannel MDCKeltnerChannel1;
		private MDCMACDBBlines MDCMACDBBlines1;
		
		private bool	altoBajoFlecha;
		private bool	altoBajoDiamante;
		private bool	drawAPre;
		private bool	flagTextPre;
		private bool	flagTextPreUpdateDate;
		private bool	flagTextPreVol;
		
		//private int diasLoading;
		private int indexStartDay;
		private int indexFinalDay;
		private int isMonday;
		private int arrowsEveryDlastD;
		private int diamondsEveryDlastD;
		private int contAPre;
		private int dayCurrentBar;
		private int tomorrowCurrentBar;
		
		private double textPreVol;
		
		private DateTime myFechaFinalDia;
		private DateTime myFechaInicioDia;
		private DateTime myToDay;
		private DateTime myTomorrow;
		
		//***************************************
		private SharpDX.Direct2D1.Bitmap 		myBitmap;
		private SharpDX.IO.NativeFileStream 	fileStream;
		private SharpDX.WIC.BitmapDecoder 		bitmapDecoder;
		private SharpDX.WIC.FormatConverter 	converter;
		private SharpDX.WIC.BitmapFrameDecode 	frame;
		//*******************************************
		private double textPreVolAUX;
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				Description									= @"Escriba la descripción de su nuevo cliente Indicador aquí.";
				Name										= "MDCPreMarket";
				Calculate									= Calculate.OnBarClose;
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
				Show_Arrows									= false;
				Show_Diamonds								= false;
				Show_ArrowsEvery_Last						= false;
				Show_DiamondsEvery_Last						= false;
				Show_AreaPre								= true;
				Show_MaxMinPastDay							= false;
				Show_InfoPreM								= false;
				flagTextPre									= true;
				flagTextPreUpdateDate						= true;
				flagTextPreVol								= false;
				
				Long_Arrows									= 4;
				Width_Arrows								= 1;
				Width_MaxMinPastDay							= 2;
				Style_Arrows								= DashStyleHelper.Solid;
				Style_MaxMinPastDay							= DashStyleHelper.Dash;
				Color_ArrowUp								= Brushes.CornflowerBlue;
				Color_MaxPastDay							= Brushes.CornflowerBlue;
				Color_ArrowDown								= Brushes.Red;
				Color_MinPastDay							= Brushes.Red;
				Color_DiamondUp								= Brushes.Blue;
				Color_DiamondDown							= Brushes.Red;
				FinalDay									= DateTime.Parse("16:00", System.Globalization.CultureInfo.InvariantCulture);
				StartDay									= DateTime.Parse("09:30", System.Globalization.CultureInfo.InvariantCulture);
				Hora_InfoPreM								= DateTime.Parse("09:25");
				myAreaOpacity 								= 25;
				Opacity_MDC									= 0.7f;
				ColorFondo									= Brushes.DimGray;
				ColorBorde									= Brushes.Transparent;
				textPreVol									= 0;
				textPreVolAUX								= 0;
				Size_IconMDC								= 200;
				
			}else if (State == State.Configure)
			{
				altoBajoFlecha 								= true;
				altoBajoDiamante 							= true;
				myTomorrow 									= Bars.LastBarTime;
				//diasLoading 								= ChartBars.Properties.DaysBack;
				
				contAPre									= 0;
				drawAPre									= false;	
				SetZOrder(-100);				
			}else if (State == State.DataLoaded)
			{				
				//MDCKeltnerChannel1							= MDCKeltnerChannel(Close, true, 52, 3.5, true, 89, true, 20, true, 80, false, 1, false, false, false, TextPosition.BottomRight, false, 0);
				//Agregado EdusTRader
				// CORRECCIÓN AQUI: Agregamos el "14" (ATR Period) después de Close
				MDCKeltnerChannel1 							= MDCKeltnerChannel(Close, true, 52, 3.5, true, 89, true, 20, true, 80, false, 1, false, false, false,14, TextPosition.BottomRight, false, 0);
				//Fin Agregado EdusTRader
				MDCMACDBBlines1								= MDCMACDBBlines(Close, 12, 26, 5, 10, 1, 0.3, -0.3, false, 1, false, 0, false, 0.3, 0);
				arrowsEveryDlastD							= ChartBars.GetBarIdxByTime(ChartControl, new DateTime(Bars.LastBarTime.AddDays(-1).Year, Bars.LastBarTime.AddDays(-1).Month, Bars.LastBarTime.AddDays(-1).Day, FinalDay.Hour,FinalDay.Minute,FinalDay.Second));
				diamondsEveryDlastD							= arrowsEveryDlastD;
			}
		}		
		protected override void OnBarUpdate()
		{
			if (BarsInProgress != 0) 
				return;

			if (CurrentBars[0] < 1)
			return;
			
			//Establecer 0
			if(ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Hour >= FinalDay.Hour && !drawAPre)
			{
				myFechaFinalDia = ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar);
//				myFechaInicioDia = myFechaFinalDia.AddDays(myFechaFinalDia.DayOfWeek.Equals(DayOfWeek.Friday)? 3: 1);
//				myFechaInicioDia = new DateTime(myFechaInicioDia.Year, myFechaInicioDia.Month, myFechaInicioDia.Day, StartDay.Hour, StartDay.Minute, 0);
				contAPre++;
				drawAPre = true;
			}
			if((ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Hour == StartDay.Hour) && (ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Minute >= StartDay.Minute) && (ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Second >= 00))
			{
				drawAPre = false;
			}
			if(drawAPre && Show_AreaPre)
			{
				Draw.RegionHighlightX(this, @"MDCPreMarket AreaPre" + contAPre, myFechaFinalDia, ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar), ColorBorde, ColorFondo, myAreaOpacity);
			}
			
			//Pre
			dayCurrentBar = ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Day;
			//textPreVol = Instrument.MarketData.DailyVolume.Volume;  //No funciono, ya que la variable se reinicia en cada F5
			textPreVol += Volume.GetValueAt(CurrentBar);
			
			
			if(flagTextPreUpdateDate)
			{
				tomorrowCurrentBar = ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).AddDays(
					ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).DayOfWeek.Equals(DayOfWeek.Friday)? 3 : 1
				).Day;
				flagTextPreUpdateDate = false;
			}
//			if(dayCurrentBar > tomorrowCurrentBar || ((tomorrowCurrentBar - dayCurrentBar) > 2))
//			{
//				flagTextPreUpdateDate = true;
//			}
			if(ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Hour == Hora_InfoPreM.Hour && 
				ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Minute >= Hora_InfoPreM.Minute && 
				ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Second >= 0 &&
				flagTextPre && Show_InfoPreM)
			{
				flagTextPre = false;
				try{
					Draw.Text(this, @"MDCPreMarket TextPre" + CurrentBar, true, 
									"Hora: " + Hora_InfoPreM.Hour + ":" + Hora_InfoPreM.Minute +
									"\nVolumen: " + String.Format("{0:#,###,000}", textPreVol) +
									"\nBandasSp: " + String.Format("{0:#.##}", MDCKeltnerChannel1.UpperBand.GetValueAt(CurrentBar)-MDCKeltnerChannel1.LowerBand.GetValueAt(CurrentBar)),
									0, MDCKeltnerChannel1.UpperBand.GetValueAt(CurrentBar) + 3.0, 0, null, null, TextAlignment.Left, null, null, 100);
					}catch(Exception ex){}
			}else if(dayCurrentBar == tomorrowCurrentBar)
			{
				flagTextPre = true;
				flagTextPreUpdateDate = true;
				flagTextPreVol = true;
			}
			
			if(ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Hour == 18 && ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Minute >= 00 && ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar).Second >= 00 && flagTextPreVol)
			{
				textPreVol = Volume.GetValueAt(CurrentBar);
				flagTextPreVol = false;
			}
//			Draw.TextFixed(this,@"MDCPreMarket textoAux2", "\nDateCurrentBar:		" + ChartBars.GetTimeByBarIdx(ChartControl, CurrentBar) + 
//															"\ndayCurrentBar:		" + dayCurrentBar + 
//															"\ntomorrowCurrentBar:	" + tomorrowCurrentBar +
//															"\nHora_InfoPreM:		" + Hora_InfoPreM +
//															"\nflagTextPre:		" + flagTextPre + 
//															"\nShow_InfoPreM:		" + Show_InfoPreM + 
//															"\nVolumen:			" + textPreVol, TextPosition.TopRight);
			// Establecer 1
			if(Show_Arrows)
			{
				if (((Low[0])  > MDCKeltnerChannel1.Midline[0])
					 && ((Low[0])  > MDCKeltnerChannel1.LR[0])
					 && (MDCMACDBBlines1.BBsLineIn[0] > MDCMACDBBlines1.ZeroLine[0])
					 && altoBajoFlecha == true)
				{
					if((CurrentBar >= arrowsEveryDlastD) && !Show_ArrowsEvery_Last)
					{
						Draw.ArrowLine(this, "MDCPreMarket FlechaLast_1" + CurrentBar, false, 0, High[0] + 0.1, 0, High[0] + Long_Arrows, Color_ArrowUp, Style_Arrows, Width_Arrows, true);
					}else if(Show_ArrowsEvery_Last)
						Draw.ArrowLine(this, "MDCPreMarket FlechaEvery_1" + CurrentBar, false, 0, High[0] + 0.1, 0, High[0] + Long_Arrows, Color_ArrowUp, Style_Arrows, Width_Arrows, true);
					altoBajoFlecha = false;
				}
				if (((High[0])  < MDCKeltnerChannel1.Midline[0])
					 && ((High[0])  < MDCKeltnerChannel1.LR[0])
					 && (MDCMACDBBlines1.BBsLineIn[0] < MDCMACDBBlines1.ZeroLine[0])
					 && altoBajoFlecha == false)
				{
					if((CurrentBar >= arrowsEveryDlastD) && !Show_ArrowsEvery_Last)
					{
						Draw.ArrowLine(this, "MDCPreMarket FlechaLast_2" + CurrentBar, false, 0, Low[0] -0.1, 0, Low[0] - Long_Arrows, Color_ArrowDown, Style_Arrows, Width_Arrows, true);
					}else if(Show_ArrowsEvery_Last)
						Draw.ArrowLine(this, "MDCPreMarket FlechaEvery_2" + CurrentBar, false, 0, Low[0] -0.1, 0, Low[0] - Long_Arrows, Color_ArrowDown, Style_Arrows, Width_Arrows, true);
					altoBajoFlecha = true;
				}
			}
			//Draw.TextFixed(this,@"MDCPreMarket textoAux", arrowsEveryDlastD + " - " +  diamondsEveryDlastD + " - " + Bars.Count + " - " + CurrentBar, TextPosition.BottomLeft);
			// Establecer 2
			if(Show_Diamonds)
			{
				if (CrossAbove(MDCKeltnerChannel1.SMA_1, MDCKeltnerChannel1.SMA_2, 1)
					 && altoBajoDiamante == true)
				{
					if((CurrentBar >= diamondsEveryDlastD) && !Show_DiamondsEvery_Last)
					{
						Draw.Diamond(this, @"MDCPreMarket DiamondLast_1" + CurrentBar, false, 0, High[0] + 1.0, Color_DiamondUp, true);
					}else if(Show_DiamondsEvery_Last) 
						Draw.Diamond(this, @"MDCPreMarket DiamondEvery_1" + CurrentBar, false, 0, High[0] + 1.0, Color_DiamondUp, true);
					altoBajoDiamante = false;
				}
				if (CrossBelow(MDCKeltnerChannel1.SMA_1, MDCKeltnerChannel1.SMA_2, 1)
					 && altoBajoDiamante == false)
				{
					if((CurrentBar >= diamondsEveryDlastD) && !Show_DiamondsEvery_Last)
					{
						Draw.Diamond(this, @"MDCPreMarket DiamondLast_2" + CurrentBar, false, 0, Low[0] - 1.0, Color_DiamondDown, true);
					}else if(Show_DiamondsEvery_Last) 
						Draw.Diamond(this, @"MDCPreMarket DiamondEvery_2" + CurrentBar, false, 0, Low[0] - 1.0, Color_DiamondDown, true);
					altoBajoDiamante = true;
				}
			}
			
			// establecer x
			myToDay = Bars.LastBarTime;
			
			if(myToDay.Day == myTomorrow.Day && Show_MaxMinPastDay)
			{
				int aux										= ChartBars.GetBarIdxByTime(ChartControl, new DateTime(Bars.LastBarTime.AddDays(-1).Year, Bars.LastBarTime.AddDays(-1).Month, Bars.LastBarTime.AddDays(-1).Day, 0,0,0));
				DateTime auxDateTime						= ChartBars.GetTimeByBarIdx(ChartControl, aux);
				isMonday 									= 0;
				
				int barStartAux								= ChartBars.GetBarIdxByTime(ChartControl, new DateTime(auxDateTime.Year, auxDateTime.Month, auxDateTime.Day, StartDay.Hour, StartDay.Minute, 0));
				int barFinalAux								= ChartBars.GetBarIdxByTime(ChartControl, new DateTime(auxDateTime.Year, auxDateTime.Month, auxDateTime.Day, FinalDay.Hour, FinalDay.Minute, 0));
				
				int i = 0;
				while((auxDateTime.Hour > 9) || ((barFinalAux - barStartAux) < 150))//Detecta fesfivos, ya que el mercado abre a las 6pm
				{
					isMonday								= (auxDateTime.DayOfWeek.Equals(DayOfWeek.Sunday))? -2 : -1;
					aux										= ChartBars.GetBarIdxByTime(ChartControl, new DateTime(auxDateTime.AddDays(isMonday).Year, auxDateTime.AddDays(isMonday).Month, auxDateTime.AddDays(isMonday).Day, 0,0,0));
					auxDateTime								= ChartBars.GetTimeByBarIdx(ChartControl, aux);
					if(auxDateTime.DayOfWeek.Equals(DayOfWeek.Saturday))//Cuando el dia es festivo y dicho dia ni barras tiene, nunca caera ese dia, sino el anterior.
					{
						isMonday							= -2;
						aux									= ChartBars.GetBarIdxByTime(ChartControl, new DateTime(auxDateTime.AddDays(isMonday).Year, auxDateTime.AddDays(isMonday).Month, auxDateTime.AddDays(isMonday).Day, 0,0,0));
						auxDateTime							= ChartBars.GetTimeByBarIdx(ChartControl, aux);
					}
					barStartAux								= ChartBars.GetBarIdxByTime(ChartControl, new DateTime(auxDateTime.Year, auxDateTime.Month, auxDateTime.Day, StartDay.Hour, StartDay.Minute, 0));
					barFinalAux								= ChartBars.GetBarIdxByTime(ChartControl, new DateTime(auxDateTime.Year, auxDateTime.Month, auxDateTime.Day, FinalDay.Hour, FinalDay.Minute, 0));
					
					i++;
					if(i >= 5) //Si se diera un bug infinito, lo rompe al cumplir 5 ciclos.
						break;
				}
				
				//isMonday									= Bars.LastBarTime.DayOfWeek.Equals(DayOfWeek.Monday)? -3 : (Bars.LastBarTime.DayOfWeek.Equals(DayOfWeek.Sunday)? -2 : -1);
				indexStartDay									= ChartBars.GetBarIdxByTime(ChartControl, new DateTime(auxDateTime.Year, auxDateTime.Month, auxDateTime.Day, StartDay.Hour,StartDay.Minute,0));
				indexFinalDay									= ChartBars.GetBarIdxByTime(ChartControl, new DateTime(auxDateTime.Year, auxDateTime.Month, auxDateTime.Day, FinalDay.Hour, FinalDay.Second,0));
				maxMinLast();
				myTomorrow = Bars.LastBarTime.AddDays(1);
				flagTextPre = true;
			}			
		}
		
		protected void maxMinLast()
		{
			double auxMaxMin = 0;
			double priceMax = Bars.GetHigh(indexStartDay);
			double priceMin = Bars.GetLow(indexStartDay);
			
			for(int i = indexStartDay; i <= indexFinalDay; i++)
			{
				if(priceMax < Bars.GetHigh(i))
				{
					priceMax = Bars.GetHigh(i);
				}
				if(priceMin > Bars.GetLow(i))
				{
					priceMin = Bars.GetLow(i);
				}
			}
			Draw.HorizontalLine(this, @"MDCPreMarket MaxLast", priceMax, Color_MaxPastDay, Style_MaxMinPastDay, Width_MaxMinPastDay);
			Draw.HorizontalLine(this, @"MDCPreMarket MinLast", priceMin, Color_MinPastDay, Style_MaxMinPastDay, Width_MaxMinPastDay);
		}
		//******************************************************************************
		public override void OnRenderTargetChanged()
		{
			base.OnRenderTargetChanged();
			if (RenderTarget == null || RenderTarget.IsDisposed) return;
			
			// Dispose all Render dependant resources on RenderTarget change.
			if (myBitmap != null) 		myBitmap.Dispose();
			if (fileStream != null) 	fileStream.Dispose();
			if (bitmapDecoder != null) 	bitmapDecoder.Dispose();
			if (converter != null) 		converter.Dispose();
			if (frame != null) 			frame.Dispose();
			
			// Neccessary for creating WIC objects.
			fileStream = new SharpDX.IO.NativeFileStream(NinjaTrader.Core.Globals.UserDataDir + "\\bin\\Custom\\Indicators\\MDC_.png", SharpDX.IO.NativeFileMode.Open, SharpDX.IO.NativeFileAccess.Read);
			// Used to read the image source file.
			bitmapDecoder = new SharpDX.WIC.BitmapDecoder(Core.Globals.WicImagingFactory, fileStream, SharpDX.WIC.DecodeOptions.CacheOnDemand);
			// Get the first frame of the image.
			frame = bitmapDecoder.GetFrame(0);
			// Convert it to a compatible pixel format.			
			converter = new SharpDX.WIC.FormatConverter(Core.Globals.WicImagingFactory);
			converter.Initialize(frame, SharpDX.WIC.PixelFormat.Format32bppPRGBA);
			// Create the new Bitmap1 directly from the FormatConverter.
			myBitmap = SharpDX.Direct2D1.Bitmap.FromWicBitmap(RenderTarget, converter);		
		}

		protected override void OnRender(NinjaTrader.Gui.Chart.ChartControl chartControl, NinjaTrader.Gui.Chart.ChartScale chartScale)
		{
			base.OnRender(chartControl, chartScale);
			
			if (RenderTarget == null || Bars == null || Bars.Instrument == null || myBitmap == null)
				return;
		
					
		SharpDX.RectangleF clipRect1 = new SharpDX.RectangleF(20, 20, Size_IconMDC, Size_IconMDC);
		
		RenderTarget.PopAxisAlignedClip();
		RenderTarget.PushAxisAlignedClip(clipRect1, RenderTarget.AntialiasMode);
		
		RenderTarget.DrawBitmap(myBitmap, clipRect1, Opacity_MDC, SharpDX.Direct2D1.BitmapInterpolationMode.Linear);
		
		RenderTarget.PopAxisAlignedClip();
		RenderTarget.PushAxisAlignedClip(new SharpDX.RectangleF((float)ChartPanel.X, (float)ChartPanel.Y, (float)ChartPanel.W, (float)ChartPanel.H), RenderTarget.AntialiasMode);				
		}		
		//******************************************************************************
		#region Properties
		[NinjaScriptProperty]
		[Display(Name="Show_AreaPre", Order=12, GroupName="1.Pre-Market")]
		public bool Show_AreaPre
		{ get; set; }
		
		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name="FinalDay", Order=13, GroupName="1.Pre-Market")]
		public DateTime FinalDay
		{ get; set; }

		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name="StartDay", Order=14, GroupName="1.Pre-Market")]
		public DateTime StartDay
		{ get; set; }
		
		[XmlIgnore]
		[Display(Name="Color", Description="Color to on rang", Order=15, GroupName="1.Pre-Market")]		
		public Brush ColorFondo 
		{ get; set;}
		[Browsable(false)]
		public string ColorFondoSerialize
		{
		  get { return Serialize.BrushToString(this.ColorFondo); }
		  set { this.ColorFondo = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(Name="ColorBorde", Description="Color to on edge", Order=16, GroupName="1.Pre-Market")]		
		public Brush ColorBorde
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, 100)]
		[Display(Name="Opacidad", Order=17, GroupName="1.Pre-Market")]
		public int myAreaOpacity
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Show_MaxMinPastDay", Order=18, GroupName="1.Pre-Market")]
		public bool Show_MaxMinPastDay
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Width_MaxMinPastDay", Order=19, GroupName="1.Pre-Market")]
		public int Width_MaxMinPastDay
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Style_MaxMinPastDay", Description="Style of the past day line", Order=20, GroupName="1.Pre-Market")]		
		public DashStyleHelper Style_MaxMinPastDay 
		{ get; set; }
		
		[XmlIgnore]
		[Display(Name="Color_MaxPastDay", Description="Color of the past day line's max price", Order=21, GroupName="1.Pre-Market")]		
		public Brush Color_MaxPastDay 
		{ get; set; }
		[Browsable(false)]
		public string Color_MaxPastDaySerialize
		{
		  get { return Serialize.BrushToString(this.Color_MaxPastDay); }
		  set { this.Color_MaxPastDay = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(Name="Color_MinPastDay", Description="Color of the past day line's min price", Order=22, GroupName="1.Pre-Market")]		
		public Brush Color_MinPastDay 
		{ get; set; }
		[Browsable(false)]
		public string Color_MinPastDaySerialize
		{
		  get { return Serialize.BrushToString(this.Color_MinPastDay); }
		  set { this.Color_MinPastDay = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[Display(Name="Show_InfoPreM", Order=23, GroupName="1.Pre-Market")]
		public bool Show_InfoPreM
		{ get; set; }
		
		[NinjaScriptProperty]
		[PropertyEditor("NinjaTrader.Gui.Tools.TimeEditorKey")]
		[Display(Name="Hora_InfoPreM", Order=24, GroupName="1.Pre-Market")]
		public DateTime Hora_InfoPreM
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Show_Arrows", Order=1, GroupName="2.Arrows")]
		public bool Show_Arrows
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Show_ArrowsEvery_Last", Order=2, GroupName="2.Arrows")]
		public bool Show_ArrowsEvery_Last
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Long_Arrows", Order=3, GroupName="2.Arrows")]
		public int Long_Arrows
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Width_Arrows", Order=4, GroupName="2.Arrows")]
		public int Width_Arrows
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Style_Arrows", Description="Style arrows", Order=5, GroupName="2.Arrows")]		
		public DashStyleHelper Style_Arrows 
		{ get; set; }
		
		[XmlIgnore]
		[Display(Name="Color_ArrowUp", Description="Color arrow up", Order=6, GroupName="2.Arrows")]		
		public Brush Color_ArrowUp 
		{ get; set; }
		[Browsable(false)]
		public string Color_ArrowUpSerialize
		{
		  get { return Serialize.BrushToString(this.Color_ArrowUp); }
		  set { this.Color_ArrowUp = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(Name="Color_ArrowDown", Description="Color arrow down", Order=7, GroupName="2.Arrows")]		
		public Brush Color_ArrowDown 
		{ get; set; }
		[Browsable(false)]
		public string Color_ArrowDownSerialize
		{
		  get { return Serialize.BrushToString(this.Color_ArrowDown); }
		  set { this.Color_ArrowDown = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[Display(Name="Show_Diamonds", Order=8, GroupName="3.Diamonds")]
		public bool Show_Diamonds
		{ get; set; }
		
		[NinjaScriptProperty]
		[Display(Name="Show_DiamondsEvery_Last", Order=9, GroupName="3.Diamonds")]
		public bool Show_DiamondsEvery_Last
		{ get; set; }
		
		[XmlIgnore]
		[Display(Name="Color_DiamondUp", Description="Color diamond Up", Order=10, GroupName="3.Diamonds")]		
		public Brush Color_DiamondUp 
		{ get; set; }
		[Browsable(false)]
		public string Color_DiamondUpSerialize
		{
		  get { return Serialize.BrushToString(this.Color_DiamondUp); }
		  set { this.Color_DiamondUp = Serialize.StringToBrush(value); }
		}
		
		[XmlIgnore]
		[Display(Name="Color_DiamondDown", Description="Color diamond down", Order=11, GroupName="3.Diamonds")]		
		public Brush Color_DiamondDown 
		{ get; set; }
		[Browsable(false)]
		public string Color_DiamondDownSerialize
		{
		  get { return Serialize.BrushToString(this.Color_DiamondDown); }
		  set { this.Color_DiamondDown = Serialize.StringToBrush(value); }
		}
		
		[NinjaScriptProperty]
		[Range(0.2, 1.0)]
		[Display(Name="Opacity_IconMDC[0,2-1,0]", Order=25, GroupName="4.Icon MDC")]
		public float Opacity_MDC
		{ get; set; }
		
		[NinjaScriptProperty]
		[Range(1, int.MaxValue)]
		[Display(Name="Size_IconMDC", Order=26, GroupName="4.Icon MDC")]
		public int Size_IconMDC
		{ get; set; }
		
		#endregion
	}
		
}
public class varsPriceTarget
{
	public double price{set; get;}
	public double bandH{set; get;}
	public double bandL{set; get;}
	public double impulseForce{set; get;}
	public int barra{set; get;}
	public int counter{set; get;}
	
	public varsPriceTarget(double price, double bandH, double bandL , double impulseForce, int barra, int counter)
	{
		this.price = price;
		this.bandH = bandH;
		this.bandL = bandL;
		this.impulseForce = impulseForce;
		this.barra = barra;
		this.counter = counter;
	}
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private MDCPreMarket[] cacheMDCPreMarket;
		public MDCPreMarket MDCPreMarket(bool show_AreaPre, DateTime finalDay, DateTime startDay, int myAreaOpacity, bool show_MaxMinPastDay, int width_MaxMinPastDay, DashStyleHelper style_MaxMinPastDay, bool show_InfoPreM, DateTime hora_InfoPreM, bool show_Arrows, bool show_ArrowsEvery_Last, int long_Arrows, int width_Arrows, DashStyleHelper style_Arrows, bool show_Diamonds, bool show_DiamondsEvery_Last, float opacity_MDC, int size_IconMDC)
		{
			return MDCPreMarket(Input, show_AreaPre, finalDay, startDay, myAreaOpacity, show_MaxMinPastDay, width_MaxMinPastDay, style_MaxMinPastDay, show_InfoPreM, hora_InfoPreM, show_Arrows, show_ArrowsEvery_Last, long_Arrows, width_Arrows, style_Arrows, show_Diamonds, show_DiamondsEvery_Last, opacity_MDC, size_IconMDC);
		}

		public MDCPreMarket MDCPreMarket(ISeries<double> input, bool show_AreaPre, DateTime finalDay, DateTime startDay, int myAreaOpacity, bool show_MaxMinPastDay, int width_MaxMinPastDay, DashStyleHelper style_MaxMinPastDay, bool show_InfoPreM, DateTime hora_InfoPreM, bool show_Arrows, bool show_ArrowsEvery_Last, int long_Arrows, int width_Arrows, DashStyleHelper style_Arrows, bool show_Diamonds, bool show_DiamondsEvery_Last, float opacity_MDC, int size_IconMDC)
		{
			if (cacheMDCPreMarket != null)
				for (int idx = 0; idx < cacheMDCPreMarket.Length; idx++)
					if (cacheMDCPreMarket[idx] != null && cacheMDCPreMarket[idx].Show_AreaPre == show_AreaPre && cacheMDCPreMarket[idx].FinalDay == finalDay && cacheMDCPreMarket[idx].StartDay == startDay && cacheMDCPreMarket[idx].myAreaOpacity == myAreaOpacity && cacheMDCPreMarket[idx].Show_MaxMinPastDay == show_MaxMinPastDay && cacheMDCPreMarket[idx].Width_MaxMinPastDay == width_MaxMinPastDay && cacheMDCPreMarket[idx].Style_MaxMinPastDay == style_MaxMinPastDay && cacheMDCPreMarket[idx].Show_InfoPreM == show_InfoPreM && cacheMDCPreMarket[idx].Hora_InfoPreM == hora_InfoPreM && cacheMDCPreMarket[idx].Show_Arrows == show_Arrows && cacheMDCPreMarket[idx].Show_ArrowsEvery_Last == show_ArrowsEvery_Last && cacheMDCPreMarket[idx].Long_Arrows == long_Arrows && cacheMDCPreMarket[idx].Width_Arrows == width_Arrows && cacheMDCPreMarket[idx].Style_Arrows == style_Arrows && cacheMDCPreMarket[idx].Show_Diamonds == show_Diamonds && cacheMDCPreMarket[idx].Show_DiamondsEvery_Last == show_DiamondsEvery_Last && cacheMDCPreMarket[idx].Opacity_MDC == opacity_MDC && cacheMDCPreMarket[idx].Size_IconMDC == size_IconMDC && cacheMDCPreMarket[idx].EqualsInput(input))
						return cacheMDCPreMarket[idx];
			return CacheIndicator<MDCPreMarket>(new MDCPreMarket(){ Show_AreaPre = show_AreaPre, FinalDay = finalDay, StartDay = startDay, myAreaOpacity = myAreaOpacity, Show_MaxMinPastDay = show_MaxMinPastDay, Width_MaxMinPastDay = width_MaxMinPastDay, Style_MaxMinPastDay = style_MaxMinPastDay, Show_InfoPreM = show_InfoPreM, Hora_InfoPreM = hora_InfoPreM, Show_Arrows = show_Arrows, Show_ArrowsEvery_Last = show_ArrowsEvery_Last, Long_Arrows = long_Arrows, Width_Arrows = width_Arrows, Style_Arrows = style_Arrows, Show_Diamonds = show_Diamonds, Show_DiamondsEvery_Last = show_DiamondsEvery_Last, Opacity_MDC = opacity_MDC, Size_IconMDC = size_IconMDC }, input, ref cacheMDCPreMarket);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.MDCPreMarket MDCPreMarket(bool show_AreaPre, DateTime finalDay, DateTime startDay, int myAreaOpacity, bool show_MaxMinPastDay, int width_MaxMinPastDay, DashStyleHelper style_MaxMinPastDay, bool show_InfoPreM, DateTime hora_InfoPreM, bool show_Arrows, bool show_ArrowsEvery_Last, int long_Arrows, int width_Arrows, DashStyleHelper style_Arrows, bool show_Diamonds, bool show_DiamondsEvery_Last, float opacity_MDC, int size_IconMDC)
		{
			return indicator.MDCPreMarket(Input, show_AreaPre, finalDay, startDay, myAreaOpacity, show_MaxMinPastDay, width_MaxMinPastDay, style_MaxMinPastDay, show_InfoPreM, hora_InfoPreM, show_Arrows, show_ArrowsEvery_Last, long_Arrows, width_Arrows, style_Arrows, show_Diamonds, show_DiamondsEvery_Last, opacity_MDC, size_IconMDC);
		}

		public Indicators.MDCPreMarket MDCPreMarket(ISeries<double> input , bool show_AreaPre, DateTime finalDay, DateTime startDay, int myAreaOpacity, bool show_MaxMinPastDay, int width_MaxMinPastDay, DashStyleHelper style_MaxMinPastDay, bool show_InfoPreM, DateTime hora_InfoPreM, bool show_Arrows, bool show_ArrowsEvery_Last, int long_Arrows, int width_Arrows, DashStyleHelper style_Arrows, bool show_Diamonds, bool show_DiamondsEvery_Last, float opacity_MDC, int size_IconMDC)
		{
			return indicator.MDCPreMarket(input, show_AreaPre, finalDay, startDay, myAreaOpacity, show_MaxMinPastDay, width_MaxMinPastDay, style_MaxMinPastDay, show_InfoPreM, hora_InfoPreM, show_Arrows, show_ArrowsEvery_Last, long_Arrows, width_Arrows, style_Arrows, show_Diamonds, show_DiamondsEvery_Last, opacity_MDC, size_IconMDC);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.MDCPreMarket MDCPreMarket(bool show_AreaPre, DateTime finalDay, DateTime startDay, int myAreaOpacity, bool show_MaxMinPastDay, int width_MaxMinPastDay, DashStyleHelper style_MaxMinPastDay, bool show_InfoPreM, DateTime hora_InfoPreM, bool show_Arrows, bool show_ArrowsEvery_Last, int long_Arrows, int width_Arrows, DashStyleHelper style_Arrows, bool show_Diamonds, bool show_DiamondsEvery_Last, float opacity_MDC, int size_IconMDC)
		{
			return indicator.MDCPreMarket(Input, show_AreaPre, finalDay, startDay, myAreaOpacity, show_MaxMinPastDay, width_MaxMinPastDay, style_MaxMinPastDay, show_InfoPreM, hora_InfoPreM, show_Arrows, show_ArrowsEvery_Last, long_Arrows, width_Arrows, style_Arrows, show_Diamonds, show_DiamondsEvery_Last, opacity_MDC, size_IconMDC);
		}

		public Indicators.MDCPreMarket MDCPreMarket(ISeries<double> input , bool show_AreaPre, DateTime finalDay, DateTime startDay, int myAreaOpacity, bool show_MaxMinPastDay, int width_MaxMinPastDay, DashStyleHelper style_MaxMinPastDay, bool show_InfoPreM, DateTime hora_InfoPreM, bool show_Arrows, bool show_ArrowsEvery_Last, int long_Arrows, int width_Arrows, DashStyleHelper style_Arrows, bool show_Diamonds, bool show_DiamondsEvery_Last, float opacity_MDC, int size_IconMDC)
		{
			return indicator.MDCPreMarket(input, show_AreaPre, finalDay, startDay, myAreaOpacity, show_MaxMinPastDay, width_MaxMinPastDay, style_MaxMinPastDay, show_InfoPreM, hora_InfoPreM, show_Arrows, show_ArrowsEvery_Last, long_Arrows, width_Arrows, style_Arrows, show_Diamonds, show_DiamondsEvery_Last, opacity_MDC, size_IconMDC);
		}
	}
}

#endregion
