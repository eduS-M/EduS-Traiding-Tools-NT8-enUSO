/*
 * EduS_Unified_Market_Context — NT8 (V3.0)
 * Autor: EduS Trader
 * ─────────────────────────────────────────────────────────────────────────────
 * CAMBIOS V3.0 (sobre V2.3)
 *
 *  1. MOTOR ESTADÍSTICO COMPLETO — CalcProb reescrito con:
 *       · Criterio de toque RELATIVO al open (distancia normalizada) en lugar
 *         de comparar el nivel absoluto contra el rango de sesión histórica.
 *         Para cada sesión histórica se verifica si el precio recorrió la
 *         distancia equivalente al nivel analizado (en ticks relativo al open).
 *       · Banda proporcional a la distancia del nivel:
 *         banda = Max(DistanceBandTicks, |dist| * BandPctOfDist)
 *         Se adapta automáticamente a niveles cercanos y lejanos.
 *       · Normalización por volatilidad (ATR diario en ticks) guardado en el
 *         record histórico. Permite comparar sesiones de distinto régimen de
 *         volatilidad (NQ 2022 vs 2024).
 *       · Sistema de filtros por capas adaptativas: si condCount < MinSampleThreshold
 *         se relaja primero el filtro AMT y luego se amplía la banda, hasta
 *         garantizar muestra suficiente o agotar opciones.
 *       · capaUsada guardada por nivel para mostrarse en UI (★★★ / ★★☆ / ★☆☆).
 *
 *  2. INDICADOR DE CONFIANZA POR NIVEL — cada fila de la tabla muestra el n
 *     individual real (no el máximo global) con color semáforo:
 *       · Verde  ≥ MinConfianzaAlta  (default 300) → alta confianza
 *       · Amarillo ≥ MinSampleThreshold (default 120) → confianza media
 *       · Rojo   < MinSampleThreshold → baja confianza
 *
 *  3. nSampleGlobal = MÍNIMO de todos los n (antes era el máximo), representa
 *     el peor caso. La tabla muestra "n≥X" con ese mínimo real.
 *
 *  4. DETECCIÓN DE SOLAPAMIENTO DE NIVELES — niveles dentro de MinDistSolapTicks
 *     entre sí se marcan con "≈" en la tabla y se colorean en gris, porque sus
 *     probabilidades no son estadísticamente independientes (overfitting).
 *
 *  5. RECÁLCULO EN MODO AUTO — si SegmentoEstadistica = Auto, las probabilidades
 *     se recalculan al inicio de cada nuevo segmento horario durante la sesión.
 *
 *  6. VENTANA DESLIZANTE DE HISTORIAL — nueva propiedad MaxSesionesHistorial.
 *     0 = sin límite (comportamiento anterior). N > 0 = solo se usan las N
 *     sesiones más recientes (RemoveAt(0) al superar el límite).
 *
 *  7. CORRECCIÓN cierre RTH: tempPCierre = Close[0] en lugar de Close[Min(1,CB)].
 *
 *  8. AtrTicks guardado en EduS_UMC_SesionRecord para normalización de volatilidad.
 *
 *  9. PROPIEDADES NUEVAS en grupo "6. Probabilidad":
 *       · BandPctOfDist        (default 20)  — % de la distancia para la banda
 *       · MinSampleThreshold   (default 120) — umbral mínimo para filtros adaptativos
 *       · MinConfianzaAlta     (default 300) — umbral de confianza alta (verde)
 *       · UsarNormalizacionATR (default false) — activa normalización por ATR
 *       · MaxSesionesHistorial (default 0)   — ventana deslizante (0=ilimitado)
 *       · MinDistSolapTicks    (default 5)   — distancia mínima para marcar solapamiento
 *
 * MANTENIDO DE V2.3:
 *  · Todo el render, badges, tabla, horarios globales, ORB, Market Profile,
 *    Volume Profile, segmentos, lógica RTH/ON — sin cambios estructurales.
 */

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
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
using FontWeight = SharpDX.DirectWrite.FontWeight;
using FontStyle  = SharpDX.DirectWrite.FontStyle;
#endregion

// ── Enums globales ─────────────────────────────────────────────────────────
public enum EduS_HistMode        { Dinamico, Manual_Fijo }
public enum EduS_TextOrient      { Horizontal, Vertical }
public enum EduS_TextPos         { Arriba, Abajo }
public enum EduS_TablaCorner     { TopRight, TopLeft, BottomRight, BottomLeft }

public enum EduS_SegmentoEstad
{
    Auto,           // Usa el segmento del reloj en tiempo real
    UsaAm,          // Seg 1: 08:30–11:30  ← DEFAULT
    Almuerzo,       // Seg 2: 11:30–13:30
    UsaPm,          // Seg 3: 13:30–17:00
    Asia,           // Seg 4: 18:00–02:00
    Europa,         // Seg 5: 02:00–08:30
    SesionCompleta  // Toda la sesión RTH (sin filtro horario)
}

public enum EduS_ModoRango { Segmento, SesionCompleta }

namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader
{
    public enum EduS_OpenContext { AboveVA, InsideVA, BelowVA, Unknown }
    public enum EduS_LevelType  { YHigh, YLow, PCierre, PreHigh, PreLow, YPOC, YVAH, YVAL, OnPOC, OnVAH, OnVAL }
    public enum EduS_Segment    { None=0, UsaAm=1, Almuerzo=2, UsaPm=3, Asia=4, Europa=5 }

    // V3.0 — AtrTicks añadido al record para normalización por volatilidad
    internal struct EduS_UMC_SesionRecord
    {
        public double Open;
        public double YHigh, YLow, PCierre, PreHigh, PreLow;
        public double YPOC,  YVAH, YVAL;
        public double OnPOC, OnVAH, OnVAL;
        public double SessionHigh, SessionLow;
        public double AtrTicks;   // V3.0 — rango diario en ticks (High-Low)/tickSz
        // [0]=UsaAm [1]=Almuerzo [2]=UsaPm [3]=Asia [4]=Europa
        public double[] SegHigh;
        public double[] SegLow;
        public EduS_OpenContext OpenContext;
    }

    // =========================================================
    public class EduS_Unified_Market_Context_V2 : Indicator
    {
        // ── TimeEvent (horarios globales) ──────────────────────
        public class EduS_TimeEvent
        {
            public DateTime Time        { get; set; }
            public string   Label       { get; set; }
            public double   AnchorPrice { get; set; }
        }
        private List<EduS_TimeEvent> globalEvents         = new List<EduS_TimeEvent>();
        private HashSet<string>      processedEventsToday = new HashSet<string>();

        // ── Niveles clásicos ───────────────────────────────────
        private double pApertura=double.NaN, yHigh=double.NaN, yLow=double.NaN;
        private double preHigh=double.NaN,   preLow=double.NaN, pCierre=double.NaN, histMax=double.NaN;
        private double curRthHigh=double.NaN, curRthLow=double.NaN;
        private double tmpPreHigh=double.NaN, tmpPreLow=double.NaN;
        private double lastCompletedRthHigh=double.NaN, lastCompletedRthLow=double.NaN;
        private double tempPCierre=double.NaN;
        private double orb30High=double.NaN,  orb30Low=double.NaN, orb30Mid=double.NaN;
        private DateTime  currentSessionDate = DateTime.MinValue;
        private CultureInfo labelCulture;
        private SharpDX.Direct2D1.Brush cachedTimeBrush;

        // ── Market Profile ─────────────────────────────────────
        private double yPOC=double.NaN,  yVAH=double.NaN,  yVAL=double.NaN;
        private double pendYPOC=double.NaN, pendYVAH=double.NaN, pendYVAL=double.NaN;
        private double onPOC=double.NaN,  onVAH=double.NaN, onVAL=double.NaN;
        private Dictionary<double,double> vpRTH = new Dictionary<double,double>();
        private Dictionary<double,double> vpON  = new Dictionary<double,double>();
        private double tickSz=0.25, valueAreaFactor=0.70;

        // ── Probabilidades ─────────────────────────────────────
        private List<EduS_UMC_SesionRecord> sessionHistory = new List<EduS_UMC_SesionRecord>();
        private EduS_UMC_SesionRecord currentRecord;
        private bool recordOpen=false;
        private double snapOpen=double.NaN;
        private double snapYHigh=double.NaN, snapYLow=double.NaN,  snapPCierre=double.NaN;
        private double snapPreH=double.NaN,  snapPreL=double.NaN;
        private double snapYPOC=double.NaN,  snapYVAH=double.NaN,  snapYVAL=double.NaN;
        private double snapOnPOC=double.NaN, snapOnVAH=double.NaN, snapOnVAL=double.NaN;
        private EduS_OpenContext currentOpenContext = EduS_OpenContext.Unknown;

        private double probYHigh=double.NaN,  probYLow=double.NaN,  probPCierre=double.NaN;
        private double probPreH=double.NaN,   probPreL=double.NaN;
        private double probYPOC=double.NaN,   probYVAH=double.NaN,  probYVAL=double.NaN;
        private double probOnPOC=double.NaN,  probOnVAH=double.NaN, probOnVAL=double.NaN;

        private int nYHigh=0,nYLow=0,nPCierre=0,nPreH=0,nPreL=0;
        private int nYPOC=0, nYVAH=0, nYVAL=0;
        private int nOnPOC=0, nOnVAH=0, nOnVAL=0;
        private int nSampleGlobal=0; // V3.0 = MÍNIMO de todos los n

        // V3.0 — Capa usada por nivel (0=fino, 1=medio, 2=relajado)
        private int capaYHigh=0,capaYLow=0,capaPCierre=0,capaPreH=0,capaPreL=0;
        private int capaYPOC=0,capaYVAH=0,capaYVAL=0;
        private int capaOnPOC=0,capaOnVAH=0,capaOnVAL=0;

        // V3.0 — Solapamiento
        private HashSet<int> nivelesSolapados = new HashSet<int>();

        private double sessionHigh=double.NaN, sessionLow=double.NaN;

        // ── Segmentos ─────────────────────────────────────────
        private EduS_Segment currentSegment = EduS_Segment.None;
        private EduS_Segment lastStatSegment = EduS_Segment.None; // V3.0 recálculo Auto
        private double segHigh=double.NaN, segLow=double.NaN;
        private double[] curSegHigh = new double[5];
        private double[] curSegLow  = new double[5];

        private static readonly string[] SegmentNames = {
            "-- Sin Segmento --",
            "S1 USA AM   08:30-11:30",
            "S2 Almuerzo 11:30-13:30",
            "S3 USA PM   13:30-17:00",
            "S4 Asia     18:00-02:00",
            "S5 Europa   02:00-08:30"
        };

        // ─────────────────────────────────────────────────────
        // HELPERS
        // ─────────────────────────────────────────────────────
        private bool InRth(int t) => t >= RthOpenTime && t < RthCloseTime;

        private EduS_Segment GetSegmentForTime(int et)
        {
            if (et >= 83000  && et < 113000) return EduS_Segment.UsaAm;
            if (et >= 113000 && et < 133000) return EduS_Segment.Almuerzo;
            if (et >= 133000 && et < 170000) return EduS_Segment.UsaPm;
            if (et >= 180000 || et < 20000)  return EduS_Segment.Asia;
            if (et >= 20000  && et < 83000)  return EduS_Segment.Europa;
            return EduS_Segment.None;
        }

        private int SegmentToIndex(EduS_Segment seg)
        {
            switch (seg) {
                case EduS_Segment.UsaAm:    return 0;
                case EduS_Segment.Almuerzo: return 1;
                case EduS_Segment.UsaPm:    return 2;
                case EduS_Segment.Asia:     return 3;
                case EduS_Segment.Europa:   return 4;
                default:                    return -1;
            }
        }

        private int EstadistaSegIdx()
        {
            switch (SegmentoEstadistica)
            {
                case EduS_SegmentoEstad.Auto:           return SegmentToIndex(currentSegment);
                case EduS_SegmentoEstad.UsaAm:          return 0;
                case EduS_SegmentoEstad.Almuerzo:       return 1;
                case EduS_SegmentoEstad.UsaPm:          return 2;
                case EduS_SegmentoEstad.Asia:           return 3;
                case EduS_SegmentoEstad.Europa:         return 4;
                case EduS_SegmentoEstad.SesionCompleta: return -1;
                default:                                return -1;
            }
        }

        private string EstadistaSegNombre()
        {
            switch (SegmentoEstadistica)
            {
                case EduS_SegmentoEstad.Auto:
                    return (currentSegment != EduS_Segment.None)
                        ? SegmentNames[(int)currentSegment]
                        : "Auto - Fuera de seg.";
                case EduS_SegmentoEstad.SesionCompleta: return "Sesión Completa";
                default:
                    int si = EstadistaSegIdx();
                    return (si >= 0 && si < SegmentNames.Length - 1)
                        ? SegmentNames[si + 1]
                        : "Sesión Completa";
            }
        }

        private bool LevelTouched(double level)
        {
            if (double.IsNaN(level)||double.IsNaN(curRthHigh)||double.IsNaN(curRthLow)) return false;
            double tol = tickSz*0.5;
            return level <= curRthHigh+tol && level >= curRthLow-tol;
        }

        private bool LevelTouchedInSegment(double level)
        {
            if (double.IsNaN(level)||double.IsNaN(segHigh)||double.IsNaN(segLow)) return false;
            double tol = tickSz*0.5;
            return level <= segHigh+tol && level >= segLow-tol;
        }

        // =========================================================
        // OnStateChange
        // =========================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name              = "EduS_Unified_Market_Context_V2";
                Description       = "EduS Trader V3.0 — Motor estadístico mejorado, filtros adaptativos, confianza por nivel.";
                Calculate         = Calculate.OnBarClose;
                IsOverlay         = true;
                DisplayInDataBox  = true;
                DrawOnPricePanel  = false;
                PaintPriceMarkers = true;
                IsAutoScale       = false;
                ScaleJustification= ScaleJustification.Right;

                RthOpenTime     = 93000;
                RthCloseTime    = 160000;
                GlobexStartTime = 180000;
                OrbSeconds      = 30;

                HistMode        = EduS_HistMode.Dinamico;
                ManualHistMax   = 0;

                CultureName       = "es-ES";
                Decimals          = 2;
                LineWidth         = 2;
                RightLabelPadding = 45;
                LabelAbove        = 1;

                ShowGlobalTimes            = true;
                GlobalTimeColor            = Brushes.DimGray;
                GlobalTimeStyle            = DashStyleHelper.Dash;
                GlobalTimeWidth            = 1;
                GlobalTimeOpacity          = 60;
                GlobalTimeFontSize         = 10;
                GlobalTimeTextOrientation  = EduS_TextOrient.Vertical;
                GlobalTimeTextPosition     = EduS_TextPos.Arriba;
                GlobalTimeTextOffsetPoints = 5;

                ValueAreaPercent     = 70;
                MostrarPerfilRTHAyer = true;
                MostrarPerfilON      = true;

                MostrarProbabilidad   = true;
                MinSessions           = 20;
                DistanceBandTicks     = 50;
                FiltrarPorContextoAMT = false;

                // V3.0 — Nuevas propiedades estadísticas
                BandPctOfDist        = 20;
                MinSampleThreshold   = 120;
                MinConfianzaAlta     = 300;
                UsarNormalizacionATR = false;
                MaxSesionesHistorial = 0;
                MinDistSolapTicks    = 5;

                MostrarBadge   = true;
                BadgeFontSize  = 11;
                BadgeBgOpacity = 65;

                SegmentoEstadistica  = EduS_SegmentoEstad.UsaAm;
                ModoRangoEstadistica = EduS_ModoRango.Segmento;

                MostrarTabla      = true;
                TablaFontSize     = 10;
                TablaBgOpacity    = 82;
                TablaCorner       = EduS_TablaCorner.TopRight;
                TablaOffsetX      = 10;
                TablaOffsetY      = 30;

                AddPlot(Brushes.DarkGreen,     "PApertura");  // 0
                AddPlot(Brushes.DarkGreen,     "yHigh");      // 1
                AddPlot(Brushes.DarkGreen,     "PreHigh");    // 2
                AddPlot(Brushes.Red,           "HistMax");    // 3
                AddPlot(Brushes.DarkGreen,     "yLow");       // 4
                AddPlot(Brushes.DarkGreen,     "PreLow");     // 5
                AddPlot(Brushes.DarkGreen,     "PCierre");    // 6
                AddPlot(Brushes.Cyan,          "ORB_High");   // 7
                AddPlot(Brushes.Magenta,       "ORB_Low");    // 8
                AddPlot(Brushes.Gold,          "ORB_Mid");    // 9
                AddPlot(Brushes.Yellow,        "yPOC");       // 10
                AddPlot(Brushes.GreenYellow,   "yVAH");       // 11
                AddPlot(Brushes.GreenYellow,   "yVAL");       // 12
                AddPlot(Brushes.DeepSkyBlue,   "onPOC");      // 13
                AddPlot(Brushes.CornflowerBlue,"onVAH");      // 14
                AddPlot(Brushes.CornflowerBlue,"onVAL");      // 15

                for (int i = 0; i < Plots.Length; i++)
                {
                    bool isDot=(i==3); bool isMP=(i>=10); bool isORB=(i>=7&&i<=9);
                    Plots[i].DashStyleHelper = isDot?DashStyleHelper.Dot:isMP?DashStyleHelper.Solid:isORB?DashStyleHelper.Solid:DashStyleHelper.DashDot;
                    Plots[i].Width=2;
                }
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Second, OrbSeconds);
            }
            else if (State == State.DataLoaded)
            {
                labelCulture    = new CultureInfo(CultureName);
                if (globalEvents==null)         globalEvents=new List<EduS_TimeEvent>();
                else                            globalEvents.Clear();
                if (processedEventsToday==null) processedEventsToday=new HashSet<string>();
                else                            processedEventsToday.Clear();
                tickSz          = (Instrument!=null)?Instrument.MasterInstrument.TickSize:0.25;
                valueAreaFactor = Math.Max(0.50, Math.Min(0.99, ValueAreaPercent/100.0));
                vpRTH.Clear(); vpON.Clear();
                sessionHistory.Clear();
                for (int i=0;i<5;i++){curSegHigh[i]=double.NaN;curSegLow[i]=double.NaN;}
            }
            else if (State == State.Historical)
            {
                if (globalEvents!=null)         globalEvents.Clear();
                if (processedEventsToday!=null) processedEventsToday.Clear();
            }
            else if (State == State.Terminated)
            {
                if (cachedTimeBrush!=null){cachedTimeBrush.Dispose();cachedTimeBrush=null;}
            }
        }

        // =========================================================
        // OnBarUpdate
        // =========================================================
        protected override void OnBarUpdate()
        {
            // BIP 1: ORB
            if (BarsInProgress==1)
            {
                DateTime tBar=Time[0]; int tBarInt=ToTime(tBar);
                TimeSpan openTS=new TimeSpan(RthOpenTime/10000,(RthOpenTime%10000)/100,RthOpenTime%100);
                TimeSpan targetTS=openTS.Add(TimeSpan.FromSeconds(OrbSeconds));
                int targetTimeInt=targetTS.Hours*10000+targetTS.Minutes*100+targetTS.Seconds;
                if (tBar.Date==currentSessionDate && tBarInt==targetTimeInt)
                {
                    orb30High=High[0]; orb30Low=Low[0];
                    orb30Mid=Math.Round((orb30High+orb30Low)/2.0/TickSize)*TickSize;
                }
                return;
            }
            if (BarsInProgress!=0) return;

            bool fastMode=(State==State.Historical);

            // Horarios globales
            if (ShowGlobalTimes && (IsFirstTickOfBar||Calculate==Calculate.OnBarClose))
            {
                DateTime barTime=Time[0];
                TimeZoneInfo ez;
                try{ez=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");}
                catch{ez=TimeZoneInfo.Local;}
                DateTime et=TimeZoneInfo.ConvertTime(barTime,Core.Globals.GeneralOptions.TimeZoneInfo,ez);
                TimeSpan etTOD=et.TimeOfDay;
                string ds=et.ToString("yyyyMMdd");
                if (Bars.IsFirstBarOfSession) processedEventsToday.Clear();
                CheckAndAddEvent("EurexOpen",   new TimeSpan(3, 0,0),"3:00 Eurex Open",    ds,etTOD,barTime);
                CheckAndAddEvent("LondonClose", new TimeSpan(11,30,0),"11:30 London Close", ds,etTOD,barTime);
                CheckAndAddEvent("AsiaOpen",    new TimeSpan(19,0,0),"19:00 Asia Open",     ds,etTOD,barTime);
                CheckAndAddEvent("AsiaClose",   new TimeSpan(2, 0,0),"2:00 Asia Close",     ds,etTOD,barTime);
                CheckAndAddEvent("LondonOpen",  new TimeSpan(4, 0,0),"4:00 London Open",    ds,etTOD,barTime);
            }

            if (CurrentBar==0)
            {
                histMax=(HistMode==EduS_HistMode.Manual_Fijo)?ManualHistMax:High[0];
                if (ToTime(Time[0])<RthOpenTime){tmpPreHigh=High[0];tmpPreLow=Low[0];preHigh=tmpPreHigh;preLow=tmpPreLow;}
                SetAllPlots(); return;
            }

            int tNow=ToTime(Time[0]); int tPrev=ToTime(Time[1]);
            UpdateSegment();

            histMax=(HistMode==EduS_HistMode.Dinamico)?Math.Max(histMax,High[0]):ManualHistMax;

            // Cambio de día
            if (Time[0].Date>Time[1].Date)
            {
                if (!double.IsNaN(lastCompletedRthHigh)) yHigh=lastCompletedRthHigh;
                if (!double.IsNaN(lastCompletedRthLow))  yLow=lastCompletedRthLow;
                if (!double.IsNaN(tempPCierre))           pCierre=tempPCierre;
                if (!double.IsNaN(pendYPOC)){yPOC=pendYPOC;yVAH=pendYVAH;yVAL=pendYVAL;}
                tmpPreHigh=High[0];tmpPreLow=Low[0];preHigh=tmpPreHigh;preLow=tmpPreLow;
                orb30High=double.NaN;orb30Low=double.NaN;orb30Mid=double.NaN;
                currentSessionDate=Time[0].Date;
                sessionHigh=High[0];sessionLow=Low[0];
                vpON.Clear();
            }

            // Pre-Market
            if (tNow<RthOpenTime)
            {
                tmpPreHigh=double.IsNaN(tmpPreHigh)?High[0]:Math.Max(tmpPreHigh,High[0]);
                tmpPreLow =double.IsNaN(tmpPreLow) ?Low[0] :Math.Min(tmpPreLow, Low[0]);
                preHigh=tmpPreHigh; preLow=tmpPreLow;
            }

            // Overnight VP
            bool isOnBar=(tNow>=GlobexStartTime||tNow<RthOpenTime);
            if (isOnBar)
            {
                AccumVP(vpON,Volume[0],High[0],Low[0],Close[0],Open[0],fastMode);
                CalcProfile(vpON,out double oPoc,out double oVah,out double oVal);
                if (!double.IsNaN(oPoc)){onPOC=oPoc;onVAH=oVah;onVAL=oVal;}
            }

            // Apertura RTH
            bool crossedOpen=(tPrev<RthOpenTime&&tNow>=RthOpenTime);
            if (crossedOpen)
            {
                pApertura=Open[0];

                // Cerrar registro previo y guardarlo en historial
                if (recordOpen&&!double.IsNaN(curRthHigh)&&!double.IsNaN(curRthLow))
                {
                    currentRecord.SessionHigh = curRthHigh;
                    currentRecord.SessionLow  = curRthLow;
                    currentRecord.SegHigh     = (double[])curSegHigh.Clone();
                    currentRecord.SegLow      = (double[])curSegLow.Clone();
                    // V3.0 — ATR del día como rango sesión en ticks
                    currentRecord.AtrTicks    = (curRthHigh - curRthLow) / tickSz;
                    sessionHistory.Add(currentRecord);

                    // V3.0 — Ventana deslizante
                    if (MaxSesionesHistorial > 0 && sessionHistory.Count > MaxSesionesHistorial)
                        sessionHistory.RemoveAt(0);
                }

                // Contexto AMT
                if (!double.IsNaN(yVAH)&&!double.IsNaN(yVAL))
                {
                    if      (pApertura>yVAH) currentOpenContext=EduS_OpenContext.AboveVA;
                    else if (pApertura<yVAL) currentOpenContext=EduS_OpenContext.BelowVA;
                    else                     currentOpenContext=EduS_OpenContext.InsideVA;
                }
                else currentOpenContext=EduS_OpenContext.Unknown;

                snapOpen=pApertura;
                snapYHigh=yHigh; snapYLow=yLow; snapPCierre=pCierre;
                snapPreH=preHigh; snapPreL=preLow;
                snapYPOC=yPOC; snapYVAH=yVAH; snapYVAL=yVAL;
                snapOnPOC=onPOC; snapOnVAH=onVAH; snapOnVAL=onVAL;

                lastStatSegment = currentSegment; // V3.0
                if (MostrarProbabilidad&&sessionHistory.Count>=MinSessions) RecalcProbabilities();
                else ResetProbabilities();

                currentRecord=new EduS_UMC_SesionRecord{
                    Open=pApertura, YHigh=yHigh, YLow=yLow, PCierre=pCierre,
                    PreHigh=preHigh, PreLow=preLow, YPOC=yPOC, YVAH=yVAH, YVAL=yVAL,
                    OnPOC=onPOC, OnVAH=onVAH, OnVAL=onVAL,
                    SessionHigh=double.NaN, SessionLow=double.NaN,
                    AtrTicks=double.NaN,
                    SegHigh=new double[]{double.NaN,double.NaN,double.NaN,double.NaN,double.NaN},
                    SegLow =new double[]{double.NaN,double.NaN,double.NaN,double.NaN,double.NaN},
                    OpenContext=currentOpenContext
                };
                recordOpen=true;
                vpRTH.Clear(); curRthHigh=High[0]; curRthLow=Low[0];
                tmpPreHigh=double.NaN; tmpPreLow=double.NaN;
                vpON.Clear();
                for (int i=0;i<5;i++){curSegHigh[i]=double.NaN;curSegLow[i]=double.NaN;}
                segHigh=double.NaN; segLow=double.NaN;
            }

            // RTH
            if (InRth(tNow))
            {
                curRthHigh=double.IsNaN(curRthHigh)?High[0]:Math.Max(curRthHigh,High[0]);
                curRthLow =double.IsNaN(curRthLow) ?Low[0] :Math.Min(curRthLow, Low[0]);
                AccumVP(vpRTH,Volume[0],High[0],Low[0],Close[0],Open[0],fastMode);

                int si=SegmentToIndex(currentSegment);
                if (si>=0)
                {
                    curSegHigh[si]=double.IsNaN(curSegHigh[si])?High[0]:Math.Max(curSegHigh[si],High[0]);
                    curSegLow[si] =double.IsNaN(curSegLow[si]) ?Low[0] :Math.Min(curSegLow[si], Low[0]);
                    segHigh=curSegHigh[si]; segLow=curSegLow[si];
                }

                // V3.0 — Recálculo en modo Auto si cambió el segmento durante la sesión
                if (SegmentoEstadistica == EduS_SegmentoEstad.Auto
                    && currentSegment != lastStatSegment
                    && currentSegment != EduS_Segment.None
                    && MostrarProbabilidad
                    && sessionHistory.Count >= MinSessions)
                {
                    lastStatSegment = currentSegment;
                    RecalcProbabilities();
                }
            }

            sessionHigh=double.IsNaN(sessionHigh)?High[0]:Math.Max(sessionHigh,High[0]);
            sessionLow =double.IsNaN(sessionLow) ?Low[0] :Math.Min(sessionLow, Low[0]);

            // Cierre RTH — V3.0 corregido: Close[0] es la barra que acaba de cerrar
            bool crossedClose=(tPrev<RthCloseTime&&tNow>=RthCloseTime);
            if (crossedClose)
            {
                tempPCierre=Close[0]; // V3.0: era Close[Math.Min(1,CurrentBar)]
                if (!double.IsNaN(curRthHigh)) lastCompletedRthHigh=curRthHigh;
                if (!double.IsNaN(curRthLow))  lastCompletedRthLow=curRthLow;
                CalcProfile(vpRTH,out double pPoc,out double pVah,out double pVal);
                if (!double.IsNaN(pPoc)){pendYPOC=pPoc;pendYVAH=pVah;pendYVAL=pVal;}
            }

            SetAllPlots();
        }

        // ── UpdateSegment ──────────────────────────────────────
        private void UpdateSegment()
        {
            DateTime barTime=Time[0];
            TimeZoneInfo ez;
            try{ez=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");}
            catch{ez=TimeZoneInfo.Local;}
            DateTime et=TimeZoneInfo.ConvertTime(barTime,Core.Globals.GeneralOptions.TimeZoneInfo,ez);
            int etInt=et.Hour*10000+et.Minute*100+et.Second;
            EduS_Segment ns=GetSegmentForTime(etInt);
            if (ns!=currentSegment){segHigh=double.NaN;segLow=double.NaN;currentSegment=ns;}
        }

        // =========================================================
        // VOLUME PROFILE
        // =========================================================
        private void AccumVP(Dictionary<double,double> vp,double vol,double high,double low,double close,double open,bool fast)
        {
            if (vol<=0) return;
            if (fast)
            {
                double v4=vol*0.25;
                void Add(double p){double pr=Instrument.MasterInstrument.RoundToTickSize(p);if(!vp.ContainsKey(pr))vp[pr]=0;vp[pr]+=v4;}
                Add(open);Add(high);Add(low);Add(close);
            }
            else
            {
                int ticks=Math.Max(1,(int)Math.Round((high-low)/tickSz)+1);
                double vt=vol/ticks;
                for (int i=0;i<ticks;i++){double pr=Instrument.MasterInstrument.RoundToTickSize(low+i*tickSz);if(!vp.ContainsKey(pr))vp[pr]=0;vp[pr]+=vt;}
            }
        }

        private void CalcProfile(Dictionary<double,double> vp,out double poc,out double vah,out double val)
        {
            poc=vah=val=double.NaN;
            if (vp==null||vp.Count==0) return;
            double maxV=-1;
            foreach (var kv in vp) if (kv.Value>maxV){maxV=kv.Value;poc=kv.Key;}
            var sorted=vp.OrderBy(kv=>kv.Key).ToList();
            double pocSnap=poc;
            int pocIdx=sorted.FindIndex(kv=>kv.Key==pocSnap);
            if (pocIdx<0) pocIdx=0;
            double totalVol=sorted.Sum(kv=>kv.Value);
            double target=totalVol*valueAreaFactor;
            double acc=sorted[pocIdx].Value;
            int lo=pocIdx,hi=pocIdx;
            while (acc<target&&(lo>0||hi<sorted.Count-1))
            {
                double vu=(hi+1<sorted.Count)?sorted[hi+1].Value:0;
                double vd=(lo-1>=0)?sorted[lo-1].Value:0;
                if (vu==0&&vd==0) break;
                if (vu>=vd) acc+=sorted[++hi].Value; else acc+=sorted[--lo].Value;
            }
            vah=sorted[hi].Key; val=sorted[lo].Key;
        }

        // =========================================================
        // PROBABILIDADES V3.0
        // =========================================================
        private void RecalcProbabilities()
        {
            int segIdx = EstadistaSegIdx();

            probYHigh  = CalcProbAdaptive(snapYHigh,   snapOpen, EduS_LevelType.YHigh,   segIdx, out nYHigh,   out capaYHigh);
            probYLow   = CalcProbAdaptive(snapYLow,    snapOpen, EduS_LevelType.YLow,    segIdx, out nYLow,    out capaYLow);
            probPCierre= CalcProbAdaptive(snapPCierre, snapOpen, EduS_LevelType.PCierre, segIdx, out nPCierre, out capaPCierre);
            probPreH   = CalcProbAdaptive(snapPreH,    snapOpen, EduS_LevelType.PreHigh, segIdx, out nPreH,    out capaPreH);
            probPreL   = CalcProbAdaptive(snapPreL,    snapOpen, EduS_LevelType.PreLow,  segIdx, out nPreL,    out capaPreL);
            probYPOC   = CalcProbAdaptive(snapYPOC,    snapOpen, EduS_LevelType.YPOC,    segIdx, out nYPOC,    out capaYPOC);
            probYVAH   = CalcProbAdaptive(snapYVAH,    snapOpen, EduS_LevelType.YVAH,    segIdx, out nYVAH,    out capaYVAH);
            probYVAL   = CalcProbAdaptive(snapYVAL,    snapOpen, EduS_LevelType.YVAL,    segIdx, out nYVAL,    out capaYVAL);
            probOnPOC  = CalcProbAdaptive(snapOnPOC,   snapOpen, EduS_LevelType.OnPOC,   segIdx, out nOnPOC,   out capaOnPOC);
            probOnVAH  = CalcProbAdaptive(snapOnVAH,   snapOpen, EduS_LevelType.OnVAH,   segIdx, out nOnVAH,   out capaOnVAH);
            probOnVAL  = CalcProbAdaptive(snapOnVAL,   snapOpen, EduS_LevelType.OnVAL,   segIdx, out nOnVAL,   out capaOnVAL);

            // V3.0 — nSampleGlobal = MÍNIMO real (antes era máximo, engañoso)
            int[] ns = { nYHigh,nYLow,nPCierre,nPreH,nPreL,nYPOC,nYVAH,nYVAL,nOnPOC,nOnVAH,nOnVAL };
            nSampleGlobal = int.MaxValue;
            foreach (int v in ns) if (v > 0 && v < nSampleGlobal) nSampleGlobal = v;
            if (nSampleGlobal == int.MaxValue) nSampleGlobal = 0;
        }

        private void ResetProbabilities()
        {
            probYHigh=probYLow=probPCierre=probPreH=probPreL=
            probYPOC=probYVAH=probYVAL=probOnPOC=probOnVAH=probOnVAL=double.NaN;
            nYHigh=nYLow=nPCierre=nPreH=nPreL=nYPOC=nYVAH=nYVAL=nOnPOC=nOnVAH=nOnVAL=nSampleGlobal=0;
            capaYHigh=capaYLow=capaPCierre=capaPreH=capaPreL=capaYPOC=capaYVAH=capaYVAL=capaOnPOC=capaOnVAH=capaOnVAL=0;
        }

        // ─────────────────────────────────────────────────────────────────────
        // V3.0 — Motor principal: filtros por capas adaptativas
        // Capa 0: banda estrecha + AMT (si activo)   → muestra fina
        // Capa 1: banda media   + sin AMT             → muestra media
        // Capa 2: banda amplia  + sin AMT             → muestra máxima
        // Se avanza de capa hasta alcanzar MinSampleThreshold o agotar capas.
        // ─────────────────────────────────────────────────────────────────────
        private double CalcProbAdaptive(double levelNow, double openNow,
            EduS_LevelType levelType, int segIdx,
            out int condCount, out int capaUsada)
        {
            condCount = 0; capaUsada = 0;
            if (double.IsNaN(levelNow)||double.IsNaN(openNow)||tickSz<=0) return double.NaN;

            // Multiplicadores de banda por capa (capa 0 = más estricto)
            double[] bandMult  = { 1.0, 1.75, 3.0 };
            bool[]   usarAMT   = { true, false, false };

            for (int capa = 0; capa < 3; capa++)
            {
                int cnt, touches;
                CalcProbCore(levelNow, openNow, levelType, segIdx,
                             bandMult[capa], usarAMT[capa],
                             out cnt, out touches);

                bool suficiente = cnt >= MinSampleThreshold;
                bool ultimaCapa = (capa == 2);

                if (suficiente || ultimaCapa)
                {
                    condCount  = cnt;
                    capaUsada  = capa;
                    return (cnt >= MinSessions) ? (double)touches / cnt : double.NaN;
                }
                // Si no es suficiente, relajar a la siguiente capa
            }

            condCount = 0; capaUsada = 2; return double.NaN;
        }

        // ─────────────────────────────────────────────────────────────────────
        // V3.0 — Núcleo de cálculo con criterio de toque RELATIVO
        //
        // Para cada sesión histórica comparable se pregunta:
        //   "¿Recorrió el precio esa sesión la distancia equivalente al nivel
        //    (en ticks relativos al open), dentro del rango del segmento/sesión?"
        //
        // Banda proporcional: Max(DistanceBandTicks * bandMult, |distNow| * BandPct)
        // Normalización ATR opcional: compara distancias como % del rango diario.
        // ─────────────────────────────────────────────────────────────────────
        private void CalcProbCore(double levelNow, double openNow,
            EduS_LevelType levelType, int segIdx,
            double bandMult, bool usarAMT,
            out int condCount, out int touchCount)
        {
            condCount = 0; touchCount = 0;
            bool levelAbove = levelNow > openNow;
            // Distancia del nivel al open de hoy en ticks (con signo)
            double distNowTicks = (levelNow - openNow) / tickSz;

            // ATR actual (rango de la sesión en curso o último registrado)
            double atrActual = (!double.IsNaN(curRthHigh) && !double.IsNaN(curRthLow) && curRthHigh > curRthLow)
                ? (curRthHigh - curRthLow) / tickSz
                : 100.0; // fallback neutral

            foreach (var s in sessionHistory)
            {
                if (double.IsNaN(s.Open)) continue;

                double levelThen = GetLevelByType(s, levelType);
                if (double.IsNaN(levelThen)) continue;

                // Filtro 1 — Mismo lado del open
                if ((levelThen > s.Open) != levelAbove) continue;

                // Distancia del nivel histórico al open de esa sesión en ticks
                double distThenTicks = (levelThen - s.Open) / tickSz;

                double distNowCmp, distThenCmp;
                if (UsarNormalizacionATR && !double.IsNaN(s.AtrTicks) && s.AtrTicks > 0 && atrActual > 0)
                {
                    // Normalizar por ATR: distancia como % del rango diario
                    distNowCmp  = distNowTicks  / atrActual;
                    distThenCmp = distThenTicks / s.AtrTicks;
                }
                else
                {
                    distNowCmp  = distNowTicks;
                    distThenCmp = distThenTicks;
                }

                // Filtro 2 — Banda proporcional adaptativa
                double absDistNow = Math.Abs(distNowCmp);
                double banda = Math.Max(
                    DistanceBandTicks * bandMult,
                    absDistNow * (BandPctOfDist / 100.0)
                );
                if (UsarNormalizacionATR && atrActual > 0)
                    banda = Math.Max(banda, (DistanceBandTicks * bandMult) / atrActual);

                if (Math.Abs(distThenCmp - distNowCmp) > banda) continue;

                // Filtro 3 — Contexto AMT (solo en capa 0 y si está habilitado)
                if (usarAMT && FiltrarPorContextoAMT
                    && currentOpenContext != EduS_OpenContext.Unknown
                    && s.OpenContext != EduS_OpenContext.Unknown
                    && s.OpenContext != currentOpenContext) continue;

                // Determinar rango de precio alcanzado en esa sesión histórica
                double rangeHigh, rangeLow;
                bool useSegRange = (ModoRangoEstadistica == EduS_ModoRango.Segmento)
                                   && segIdx >= 0
                                   && s.SegHigh != null && s.SegLow != null
                                   && !double.IsNaN(s.SegHigh[segIdx])
                                   && !double.IsNaN(s.SegLow[segIdx]);
                if (useSegRange)
                {
                    rangeHigh = s.SegHigh[segIdx];
                    rangeLow  = s.SegLow[segIdx];
                }
                else
                {
                    if (double.IsNaN(s.SessionHigh)||double.IsNaN(s.SessionLow)) continue;
                    rangeHigh = s.SessionHigh;
                    rangeLow  = s.SessionLow;
                }

                condCount++;

                // V3.0 — Criterio de toque RELATIVO:
                // ¿Recorrió el precio de esa sesión la distancia equivalente a distThenTicks?
                // Es decir: ¿llegó el precio hasta el nivel en esa sesión?
                double targetPrice = s.Open + distThenTicks * tickSz;
                double tol         = tickSz * 0.5;
                bool touched = levelAbove
                    ? rangeHigh >= targetPrice - tol   // nivel arriba: ¿subió hasta ahí?
                    : rangeLow  <= targetPrice + tol;  // nivel abajo: ¿bajó hasta ahí?

                if (touched) touchCount++;
            }
        }

        private double GetLevelByType(EduS_UMC_SesionRecord s, EduS_LevelType t)
        {
            switch(t){
                case EduS_LevelType.YHigh:   return s.YHigh;
                case EduS_LevelType.YLow:    return s.YLow;
                case EduS_LevelType.PCierre: return s.PCierre;
                case EduS_LevelType.PreHigh: return s.PreHigh;
                case EduS_LevelType.PreLow:  return s.PreLow;
                case EduS_LevelType.YPOC:    return s.YPOC;
                case EduS_LevelType.YVAH:    return s.YVAH;
                case EduS_LevelType.YVAL:    return s.YVAL;
                case EduS_LevelType.OnPOC:   return s.OnPOC;
                case EduS_LevelType.OnVAH:   return s.OnVAH;
                case EduS_LevelType.OnVAL:   return s.OnVAL;
                default:                     return double.NaN;
            }
        }

        // V3.0 — Detección de solapamiento de niveles
        private void DetectarSolapamientos(List<(string name,double val,double prob,int n,int pi,int capa)> rows)
        {
            nivelesSolapados.Clear();
            double minDist = MinDistSolapTicks * tickSz;
            for (int i = 0; i < rows.Count - 1; i++)
            {
                if (double.IsNaN(rows[i].val)) continue;
                for (int j = i + 1; j < rows.Count; j++)
                {
                    if (double.IsNaN(rows[j].val)) continue;
                    if (Math.Abs(rows[i].val - rows[j].val) < minDist)
                    {
                        nivelesSolapados.Add(i);
                        nivelesSolapados.Add(j);
                    }
                }
            }
        }

        private void SetAllPlots()
        {
            Values[0][0]=pApertura; Values[1][0]=yHigh;  Values[2][0]=preHigh;
            Values[3][0]=histMax;   Values[4][0]=yLow;   Values[5][0]=preLow;
            Values[6][0]=pCierre;   Values[7][0]=orb30High; Values[8][0]=orb30Low;
            Values[9][0]=orb30Mid;  Values[10][0]=yPOC;  Values[11][0]=yVAH;
            Values[12][0]=yVAL;     Values[13][0]=onPOC; Values[14][0]=onVAH;
            Values[15][0]=onVAL;
        }

        // V3.0 — CheckAndAddEvent: tolerancia dinámica según TimeFrame
        private void CheckAndAddEvent(string id,TimeSpan target,string label,string ds,TimeSpan cur,DateTime bTime)
        {
            string key=id+"_"+ds;
            if (processedEventsToday.Contains(key)) return;
            // Tolerancia mínima 5 min o el timeframe del chart (para TF grandes)
            int tfMinutes = Math.Max(1, (int)(BarsPeriod.Value));
            TimeSpan tol = TimeSpan.FromMinutes(Math.Max(5, tfMinutes));
            if (cur>=target&&cur<target.Add(tol))
            {
                double a=(GlobalTimeTextPosition==EduS_TextPos.Arriba)?High[0]:Low[0];
                globalEvents.Add(new EduS_TimeEvent{Time=bTime,Label=label,AnchorPrice=a});
                processedEventsToday.Add(key);
            }
        }

        // =========================================================
        // OnRender
        // =========================================================
        protected override void OnRender(ChartControl cc, ChartScale cs)
        {
            if (ChartBars==null||cc==null||cs==null) return;

            // ── 1. Horarios Globales ──────────────────────────────
            if (ShowGlobalTimes&&globalEvents!=null&&globalEvents.Count>0)
            {
                if (cachedTimeBrush==null||cachedTimeBrush.IsDisposed)
                {
                    var db=GlobalTimeColor.ToDxBrush(RenderTarget);
                    db.Opacity=(float)(GlobalTimeOpacity/100.0);
                    cachedTimeBrush=db;
                }
                var tfmt=new TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory,
                    "Arial",FontWeight.Bold,FontStyle.Normal,(float)GlobalTimeFontSize);
                float yT=(float)ChartPanel.Y, yB=(float)(ChartPanel.Y+ChartPanel.H);
                foreach (var ev in globalEvents)
                {
                    int idx=ChartBars.GetBarIdxByTime(cc,ev.Time);
                    if (idx<ChartBars.FromIndex||idx>ChartBars.ToIndex) continue;
                    float x=(float)cc.GetXByBarIndex(ChartBars,idx);
                    RenderTarget.DrawLine(new Vector2(x,yT),new Vector2(x,yB),cachedTimeBrush,GlobalTimeWidth);
                    using (var lay=new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory,ev.Label,tfmt,300f,tfmt.FontSize))
                    {
                        double fp=(GlobalTimeTextPosition==EduS_TextPos.Arriba)
                            ?ev.AnchorPrice+(GlobalTimeTextOffsetPoints*TickSize)
                            :ev.AnchorPrice-(GlobalTimeTextOffsetPoints*TickSize);
                        float yp=(float)cs.GetYByValue(fp);
                        if (GlobalTimeTextPosition==EduS_TextPos.Abajo&&GlobalTimeTextOrientation==EduS_TextOrient.Horizontal) yp-=lay.Metrics.Height;
                        if (GlobalTimeTextOrientation==EduS_TextOrient.Horizontal)
                            RenderTarget.DrawTextLayout(new Vector2(x+3,yp),lay,cachedTimeBrush);
                        else
                        {
                            var old=RenderTarget.Transform;
                            float py=yp+(GlobalTimeTextPosition==EduS_TextPos.Abajo?2:-2);
                            RenderTarget.Transform=Matrix3x2.Rotation((float)(-Math.PI/2),new Vector2(x,py))*old;
                            RenderTarget.DrawTextLayout(GlobalTimeTextPosition==EduS_TextPos.Arriba?new Vector2(x,py):new Vector2(x-lay.Metrics.Width,py),lay,cachedTimeBrush);
                            RenderTarget.Transform=old;
                        }
                    }
                }
                tfmt.Dispose();
            }

            // ── 2. Niveles (líneas + badges) ──────────────────────
            // V3.0 — tuple extendido con capa
            var levels=new (double val,string name,int pi,double prob,int n,int capa,bool vis)[]
            {
                (pApertura,"Open RTH",0,double.NaN,0,0,true),
                (yHigh,    "Y-High",  1,probYHigh, nYHigh, capaYHigh, true),
                (preHigh,  "Pre-High",2,probPreH,  nPreH,  capaPreH,  true),
                (histMax,  "HistMax", 3,double.NaN,0,0,true),
                (yLow,     "Y-Low",   4,probYLow,  nYLow,  capaYLow,  true),
                (preLow,   "Pre-Low", 5,probPreL,  nPreL,  capaPreL,  true),
                (pCierre,  "Y-Close", 6,probPCierre,nPCierre,capaPCierre,true),
                (orb30High,"ORB-H",   7,double.NaN,0,0,true),
                (orb30Low, "ORB-L",   8,double.NaN,0,0,true),
                (orb30Mid, "ORB-M",   9,double.NaN,0,0,true),
                (yPOC,"yPOC",10,probYPOC, nYPOC, capaYPOC, MostrarPerfilRTHAyer),
                (yVAH,"yVAH",11,probYVAH, nYVAH, capaYVAH, MostrarPerfilRTHAyer),
                (yVAL,"yVAL",12,probYVAL, nYVAL, capaYVAL, MostrarPerfilRTHAyer),
                (onPOC,"onPOC",13,probOnPOC,nOnPOC,capaOnPOC,MostrarPerfilON),
                (onVAH,"onVAH",14,probOnVAH,nOnVAH,capaOnVAH,MostrarPerfilON),
                (onVAL,"onVAL",15,probOnVAL,nOnVAL,capaOnVAL,MostrarPerfilON),
            };

            float xLeft=(float)cc.CanvasLeft+2f;
            int lastIdx=ChartBars.ToIndex;
            float xLast=(float)cc.GetXByBarIndex(ChartBars,lastIdx);
            float xBS=xLast+RightLabelPadding;

            var bfmt=new TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory,
                "Consolas",FontWeight.Bold,FontStyle.Normal,(float)BadgeFontSize);
            float bAlpha=BadgeBgOpacity/100f;

            using (var dkBrush=new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(0.08f,0.08f,0.08f,1f)))
            {
                var badgeItems=new List<(float yC,float bW,float bH,string txt,int pi)>();

                for (int i=0;i<levels.Length;i++)
                {
                    var lv=levels[i];
                    if (!lv.vis||double.IsNaN(lv.val)||Math.Abs(lv.val)<double.Epsilon) continue;
                    float y=(float)cs.GetYByValue(lv.val);
                    var plot=Plots[lv.pi];
                    RenderTarget.DrawLine(new Vector2(xLeft,y),new Vector2(xBS,y),plot.BrushDX,plot.Width,plot.StrokeStyle);

                    if (!MostrarBadge) continue;
                    bool tch=LevelTouched(lv.val);
                    string fmt2=lv.val.ToString("N"+Decimals,labelCulture??CultureInfo.InvariantCulture);
                    string bTxt=(MostrarProbabilidad&&!double.IsNaN(lv.prob))
                        ?$"{lv.name} {fmt2} {(int)Math.Round(lv.prob*100)}%{(tch?" ✓":"")}"
                        :$"{lv.name} {fmt2}{(tch?" ✓":"")}";
                    using (var lay=new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory,bTxt,bfmt,500f,bfmt.FontSize))
                        badgeItems.Add((y,lay.Metrics.Width+10f,lay.Metrics.Height+4f,bTxt,lv.pi));
                }

                float maxBW=0f; foreach (var b in badgeItems) if (b.bW>maxBW) maxBW=b.bW;
                float cStep=maxBW+3f;
                var assigned=new List<(int col,SharpDX.RectangleF rect)>();
                var finalBadges=new List<(float bx,float by,float bw,float bh,string txt,int pi)>();

                foreach (var b in badgeItems)
                {
                    float yt=b.yC-b.bH/2f; int col=0;
                    while (true)
                    {
                        float xt=xBS+col*cStep;
                        var r=new SharpDX.RectangleF(xt,yt,b.bW,b.bH);
                        bool coll=false;
                        foreach (var a in assigned)
                            if (a.col==col){float ab=a.rect.Top+a.rect.Height;float bb=r.Top+r.Height;if(r.Top<ab+1f&&bb>a.rect.Top-1f){coll=true;break;}}
                        if (!coll){assigned.Add((col,r));finalBadges.Add((xt,yt,b.bW,b.bH,b.txt,b.pi));break;}
                        col++;
                    }
                }
                foreach (var fb in finalBadges)
                {
                    var pl=Plots[fb.pi];
                    var bgR=new SharpDX.RectangleF(fb.bx,fb.by,fb.bw,fb.bh);
                    pl.BrushDX.Opacity=bAlpha; RenderTarget.FillRectangle(bgR,pl.BrushDX);
                    pl.BrushDX.Opacity=1f;     RenderTarget.DrawRectangle(bgR,pl.BrushDX,1f);
                    using (var lay=new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory,fb.txt,bfmt,500f,bfmt.FontSize))
                        RenderTarget.DrawTextLayout(new Vector2(fb.bx+5f,fb.by+2f),lay,dkBrush);
                }
            }
            bfmt.Dispose();

            // ── 3. TABLA V3.0 ─────────────────────────────────────
            if (!MostrarTabla) return;

            double refPrice = !double.IsNaN(pApertura) ? pApertura : Close[0];

            // Construir lista de filas con capa
            var rows=new List<(string name,double val,double prob,int n,int pi,int capa)>();
            foreach (var lv in levels)
                if (lv.vis&&!double.IsNaN(lv.val)&&Math.Abs(lv.val)>double.Epsilon)
                    rows.Add((lv.name,lv.val,lv.prob,lv.n,lv.pi,lv.capa));
            if (rows.Count==0) return;

            rows=rows.OrderByDescending(r=>r.val).ToList();

            // V3.0 — Detectar solapamientos
            DetectarSolapamientos(rows);

            // ── Textos de header ──────────────────────────────────
            string ctxTxt; SharpDX.Color4 ctxClr;
            switch (currentOpenContext)
            {
                case EduS_OpenContext.AboveVA:  ctxTxt="Open>VA ↑"; ctxClr=new SharpDX.Color4(0.2f,0.9f,0.3f,1f);  break;
                case EduS_OpenContext.InsideVA: ctxTxt="Open=VA ↔"; ctxClr=new SharpDX.Color4(1f,0.85f,0.1f,1f);   break;
                case EduS_OpenContext.BelowVA:  ctxTxt="Open<VA ↓"; ctxClr=new SharpDX.Color4(0.9f,0.3f,0.2f,1f);  break;
                default:                        ctxTxt="ctx: --";   ctxClr=new SharpDX.Color4(0.55f,0.55f,0.6f,1f); break;
            }

            string modoStr=(ModoRangoEstadistica==EduS_ModoRango.Segmento)?"Seg":"Full";
            string segNom=EstadistaSegNombre();
            string openStr=!double.IsNaN(pApertura)
                ?pApertura.ToString("N"+Decimals,labelCulture??CultureInfo.InvariantCulture):"--";

            // V3.0 — indicador de ventana de historial
            string histStr = MaxSesionesHistorial > 0
                ? $"n≥{nSampleGlobal} [{sessionHistory.Count}/{MaxSesionesHistorial}]"
                : $"n≥{nSampleGlobal} [{sessionHistory.Count}]";

            float fSz=(float)TablaFontSize;
            var tfNorm=new TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory,
                "Consolas",FontWeight.Normal,FontStyle.Normal,fSz);
            var tfBold=new TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory,
                "Consolas",FontWeight.Bold,FontStyle.Normal,fSz);
            var tfSmall=new TextFormat(NinjaTrader.Core.Globals.DirectWriteFactory,
                "Consolas",FontWeight.Normal,FontStyle.Normal,fSz-1f);

            float rowH    = fSz + 4f;
            float hdrRowH = fSz + 5f;
            float padX    = 5f;
            float padYH   = 3f;

            float cAr   = fSz + 2f;
            float cNm   = 55f;
            float cPr   = 56f;
            float cPct  = 46f;  // V3.0: ligeramente más ancho para mostrar capa (★)
            float tableW= padX*2f + cAr + cNm + cPr + cPct;

            float hdrH   = hdrRowH*4f + padYH*2f;
            float tableH = padYH*2f + hdrH + rows.Count*rowH;

            float pLeft=(float)ChartPanel.X, pTop=(float)ChartPanel.Y;
            float pBot=(float)(ChartPanel.Y+ChartPanel.H), pRight=(float)(ChartPanel.X+ChartPanel.W);
            float tX,tY;
            switch (TablaCorner)
            {
                case EduS_TablaCorner.TopLeft:     tX=pLeft  +TablaOffsetX; tY=pTop+TablaOffsetY;        break;
                case EduS_TablaCorner.BottomLeft:  tX=pLeft  +TablaOffsetX; tY=pBot-tableH-TablaOffsetY; break;
                case EduS_TablaCorner.BottomRight: tX=pRight -tableW-TablaOffsetX; tY=pBot-tableH-TablaOffsetY; break;
                default:                           tX=pRight -tableW-TablaOffsetX; tY=pTop+TablaOffsetY;  break;
            }

            float bgA=TablaBgOpacity/100f;

            using (var bgBr    =new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(0.05f,0.05f,0.08f,bgA)))
            using (var hdrBr   =new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(0.1f,0.1f,0.15f,0.97f)))
            using (var bordBr  =new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(0.32f,0.32f,0.42f,1f)))
            using (var whtBr   =new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(1f,1f,1f,0.95f)))
            using (var gryBr   =new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(0.65f,0.65f,0.7f,1f)))
            using (var dimBr   =new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(0.38f,0.38f,0.42f,1f))) // V3.0 solapados
            using (var segBr   =new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(0.38f,0.82f,1f,1f)))
            using (var ctxBr   =new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,ctxClr))
            using (var upBr    =new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(0.2f,0.88f,0.35f,1f)))
            using (var dnBr    =new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(0.95f,0.28f,0.22f,1f)))
            using (var statBr  =new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(0.68f,0.68f,0.72f,1f)))
            // V3.0 — brushes semáforo confianza
            using (var cnfAltBr=new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(0.1f,0.92f,0.3f,1f)))   // verde
            using (var cnfMedBr=new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(1f,0.82f,0.1f,1f)))    // amarillo
            using (var cnfLowBr=new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(0.92f,0.28f,0.18f,1f)))// rojo
            {
                var tRect=new SharpDX.RectangleF(tX,tY,tableW,tableH);
                RenderTarget.FillRectangle(tRect,bgBr);
                RenderTarget.DrawRectangle(tRect,bordBr,1f);

                var hRect=new SharpDX.RectangleF(tX,tY,tableW,padYH+hdrH);
                RenderTarget.FillRectangle(hRect,hdrBr);

                float sepY=tY+padYH+hdrH;
                RenderTarget.DrawLine(new Vector2(tX+2f,sepY),new Vector2(tX+tableW-2f,sepY),bordBr,1f);

                void DC(string s, float cx, float cy, float cw,
                        SharpDX.Direct2D1.Brush br, TextFormat fmt, bool ra=false)
                {
                    if (string.IsNullOrEmpty(s)) return;
                    using (var lay=new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory,s,fmt,cw+4f,rowH+2f))
                    {
                        float dx=ra?cx+cw-lay.Metrics.Width:cx;
                        RenderTarget.DrawTextLayout(new Vector2(dx,cy),lay,br);
                    }
                }

                float hy1=tY+padYH;
                DC(segNom, tX+padX, hy1, tableW-padX*2f, segBr, tfBold);

                float hy2=hy1+hdrRowH;
                // V3.0 — muestra el n mínimo y conteo de historial
                DC(histStr, tX+padX, hy2, tableW-padX*2f-2f, statBr, tfSmall);

                // V3.0 — segunda línea: open + modo + ATR indicator
                float hy2b=hy2+hdrRowH*0.6f;
                string h2b=$"Open:{openStr}  [{modoStr}]{(UsarNormalizacionATR?" ATR":"")}";
                DC(h2b, tX+padX, hy2b+hdrRowH*0.4f, tableW-padX*2f-2f, statBr, tfSmall);

                float hy3=hy2+hdrRowH;
                DC(ctxTxt, tX+padX, hy3+hdrRowH*0.5f, tableW-padX*2f-2f, ctxBr, tfSmall);

                float hColY=hy3+hdrRowH*1.1f;
                RenderTarget.DrawLine(new Vector2(tX+2f,hColY-1f),new Vector2(tX+tableW-2f,hColY-1f),bordBr,0.5f);

                float xAr=tX+padX;
                float xNm=xAr+cAr;
                float xPr=xNm+cNm;
                float xPt=xPr+cPr;

                DC("↕",      xAr, hColY, cAr,  gryBr, tfBold);
                DC("Nivel",  xNm, hColY, cNm,  gryBr, tfSmall);
                DC("Precio", xPr, hColY, cPr,  gryBr, tfSmall, true);
                DC("%/n",    xPt, hColY, cPct, gryBr, tfSmall, true);

                for (int i=0;i<rows.Count;i++)
                {
                    var (nm,val,prob,n,pi,capa)=rows[i];
                    float ry=sepY+i*rowH+1f;

                    bool solapado = nivelesSolapados.Contains(i);

                    if (i%2==1)
                        using (var altB=new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(1f,1f,1f,0.025f)))
                            RenderTarget.FillRectangle(new SharpDX.RectangleF(tX+1.5f,ry,tableW-3f,rowH-0.5f),altB);

                    RenderTarget.DrawLine(new Vector2(tX+2f,ry+1f),new Vector2(tX+2f,ry+rowH-2f),Plots[pi].BrushDX,3f);

                    bool aboveOpen = val > refPrice;
                    DC(aboveOpen?"▲":"▼", xAr, ry, cAr, aboveOpen?upBr:dnBr, tfBold);

                    // V3.0 — nombre en gris si solapado, con prefijo "≈"
                    string nmStr = solapado ? "≈"+nm : nm;
                    DC(nmStr, xNm, ry, cNm, solapado?dimBr:whtBr, tfNorm);

                    DC(val.ToString("N"+Decimals,labelCulture??CultureInfo.InvariantCulture),
                       xPr, ry, cPr, gryBr, tfNorm, true);

                    bool touched = (ModoRangoEstadistica==EduS_ModoRango.Segmento)
                        ? LevelTouchedInSegment(val)
                        : LevelTouched(val);

                    string pStr; SharpDX.Direct2D1.Brush pBr;
                    if (MostrarProbabilidad&&!double.IsNaN(prob)&&n>=MinSessions)
                    {
                        int pct=(int)Math.Round(prob*100);

                        // V3.0 — indicador de capa: ★★★ fino, ★★☆ medio, ★☆☆ relajado
                        string capaStr = capa==0 ? "●" : capa==1 ? "◑" : "○";
                        // solapado → añadir ≈ al %
                        string solapStr = solapado ? "≈" : "";
                        pStr = touched
                            ? $"{pct}%✓{capaStr}"
                            : $"{solapStr}{pct}%{capaStr}";

                        // V3.0 — semáforo de confianza según n individual
                        if (solapado)
                            pBr = dimBr;
                        else if (n >= MinConfianzaAlta)
                            pBr = cnfAltBr;   // verde — alta confianza
                        else if (n >= MinSampleThreshold)
                            pBr = cnfMedBr;   // amarillo — confianza media
                        else
                            pBr = cnfLowBr;   // rojo — baja confianza
                    }
                    else
                    {
                        pStr=touched?"✓":"-";
                        pBr=new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,touched
                            ?new SharpDX.Color4(0.2f,0.92f,0.35f,1f)
                            :new SharpDX.Color4(0.38f,0.38f,0.42f,1f));
                    }
                    DC(pStr, xPt, ry, cPct, pBr, tfBold, true);
                    // Solo disponer si es un brush creado localmente (el else)
                    if (!MostrarProbabilidad||double.IsNaN(prob)||n<MinSessions) pBr.Dispose();

                    if (i<rows.Count-1)
                        using (var linBr=new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(0.5f,0.5f,0.6f,0.08f)))
                            RenderTarget.DrawLine(new Vector2(tX+4f,ry+rowH-0.5f),new Vector2(tX+tableW-4f,ry+rowH-0.5f),linBr,0.5f);
                }

                using (var sepVBr=new SharpDX.Direct2D1.SolidColorBrush(RenderTarget,new SharpDX.Color4(0.55f,0.55f,0.65f,0.12f)))
                {
                    float lTop=tY+1f, lBot=tY+tableH-1f;
                    RenderTarget.DrawLine(new Vector2(xNm-2f,lTop),new Vector2(xNm-2f,lBot),sepVBr,0.5f);
                    RenderTarget.DrawLine(new Vector2(xPr-2f,lTop),new Vector2(xPr-2f,lBot),sepVBr,0.5f);
                    RenderTarget.DrawLine(new Vector2(xPt-2f,lTop),new Vector2(xPt-2f,lBot),sepVBr,0.5f);
                }
            }

            tfNorm.Dispose(); tfBold.Dispose(); tfSmall.Dispose();
        }

        public override void OnRenderTargetChanged()
        {
            if (cachedTimeBrush!=null){cachedTimeBrush.Dispose();cachedTimeBrush=null;}
        }

        // =========================================================
        // PROPIEDADES
        // =========================================================
        #region Properties

        [NinjaScriptProperty][Display(Name="Inicio RTH (HHmmss)",GroupName="1. Horarios",Order=0)]
        public int RthOpenTime{get;set;}
        [NinjaScriptProperty][Display(Name="Cierre RTH (HHmmss)",GroupName="1. Horarios",Order=1)]
        public int RthCloseTime{get;set;}
        [NinjaScriptProperty][Display(Name="GlobexStartTime (HHmmss)",GroupName="1. Horarios",Order=2)]
        public int GlobexStartTime{get;set;}
        [NinjaScriptProperty][Display(Name="Segundos ORB",GroupName="1. Horarios",Order=3)]
        public int OrbSeconds{get;set;}

        [NinjaScriptProperty][Display(Name="Cultura (Formato)",GroupName="2. Visual",Order=3)]
        public string CultureName{get;set;}
        [NinjaScriptProperty][Range(0,8)][Display(Name="Decimales",GroupName="2. Visual",Order=4)]
        public int Decimals{get;set;}
        [NinjaScriptProperty][Range(1,10)][Display(Name="Grosor Línea",GroupName="2. Visual",Order=5)]
        public int LineWidth{get;set;}
        [NinjaScriptProperty][Range(0,500)][Display(Name="Margen Etiqueta (Der)",GroupName="2. Visual",Order=6)]
        public int RightLabelPadding{get;set;}
        [NinjaScriptProperty][Range(-50,50)][Display(Name="Altura Texto (Offset)",GroupName="2. Visual",Order=7)]
        public int LabelAbove{get;set;}

        [NinjaScriptProperty][Display(Name="Modo Max Histórico",GroupName="3. Niveles Históricos",Order=0)]
        public EduS_HistMode HistMode{get;set;}
        [NinjaScriptProperty][Display(Name="Precio Max Manual",GroupName="3. Niveles Históricos",Order=1)]
        public double ManualHistMax{get;set;}

        [NinjaScriptProperty][Display(Name="Mostrar Horarios Globales",Order=1,GroupName="4. Horarios Globales")]
        public bool ShowGlobalTimes{get;set;}
        [NinjaScriptProperty][XmlIgnore][Display(Name="Color Líneas",Order=2,GroupName="4. Horarios Globales")]
        public Brush GlobalTimeColor{get;set;}
        [Browsable(false)]
        public string GlobalTimeColorSerializable
        {get{return Serialize.BrushToString(GlobalTimeColor);}set{GlobalTimeColor=Serialize.StringToBrush(value);}}
        [NinjaScriptProperty][Display(Name="Estilo Línea",Order=3,GroupName="4. Horarios Globales")]
        public DashStyleHelper GlobalTimeStyle{get;set;}
        [NinjaScriptProperty][Range(1,10)][Display(Name="Grosor Línea",Order=4,GroupName="4. Horarios Globales")]
        public int GlobalTimeWidth{get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Opacidad (%)",Order=5,GroupName="4. Horarios Globales")]
        public int GlobalTimeOpacity{get;set;}
        [NinjaScriptProperty][Range(5,50)][Display(Name="Tamaño Fuente",Order=6,GroupName="4. Horarios Globales")]
        public int GlobalTimeFontSize{get;set;}
        [NinjaScriptProperty][Display(Name="Orientación Texto",Order=7,GroupName="4. Horarios Globales")]
        public EduS_TextOrient GlobalTimeTextOrientation{get;set;}
        [NinjaScriptProperty][Display(Name="Posición Texto",Order=8,GroupName="4. Horarios Globales")]
        public EduS_TextPos GlobalTimeTextPosition{get;set;}
        [NinjaScriptProperty][Range(0,500)][Display(Name="Offset (Puntos)",Order=9,GroupName="4. Horarios Globales")]
        public double GlobalTimeTextOffsetPoints{get;set;}

        [NinjaScriptProperty][Range(50,95)][Display(Name="Value Area %",GroupName="5. Market Profile",Order=0)]
        public int ValueAreaPercent{get;set;}
        [NinjaScriptProperty][Display(Name="Perfil RTH Ayer",GroupName="5. Market Profile",Order=1)]
        public bool MostrarPerfilRTHAyer{get;set;}
        [NinjaScriptProperty][Display(Name="Perfil Overnight",GroupName="5. Market Profile",Order=2)]
        public bool MostrarPerfilON{get;set;}

        // ── Grupo 6: Probabilidad ─────────────────────────────
        [NinjaScriptProperty][Display(Name="Mostrar Probabilidad",GroupName="6. Probabilidad",Order=0)]
        public bool MostrarProbabilidad{get;set;}

        [NinjaScriptProperty][Range(5,500)]
        [Display(Name="Mínimo de Sesiones (display)",
                 Description="Mínimo de sesiones comparables para mostrar un % (evita mostrar stats con muy poca muestra).",
                 GroupName="6. Probabilidad",Order=1)]
        public int MinSessions{get;set;}

        [NinjaScriptProperty][Range(1,500)]
        [Display(Name="Banda Distancia Base (ticks)",
                 Description="Banda base para comparar distancias. Se amplía proporcionalmente con BandPctOfDist.",
                 GroupName="6. Probabilidad",Order=2)]
        public int DistanceBandTicks{get;set;}

        [NinjaScriptProperty][Display(Name="Filtrar por Contexto AMT",GroupName="6. Probabilidad",Order=3)]
        public bool FiltrarPorContextoAMT{get;set;}

        [NinjaScriptProperty]
        [Display(Name="Segmento para Estadística",
                 Description="Elige el segmento horario para las probabilidades. Auto = segmento del reloj (recalcula al cambiar).",
                 GroupName="6. Probabilidad",Order=4)]
        public EduS_SegmentoEstad SegmentoEstadistica{get;set;}

        [NinjaScriptProperty]
        [Display(Name="Modo Rango Estadística",
                 Description="Segmento = toque dentro del horario del segmento. SesionCompleta = toque en cualquier momento RTH.",
                 GroupName="6. Probabilidad",Order=5)]
        public EduS_ModoRango ModoRangoEstadistica{get;set;}

        // ── V3.0 — Nuevas propiedades estadísticas ────────────
        [NinjaScriptProperty][Range(0,100)]
        [Display(Name="Banda % de Distancia",
                 Description="V3.0: La banda se amplía al Max(BandaBase, |dist|*BandPct/100). Recomendado 20 (=20%).",
                 GroupName="6. Probabilidad",Order=6)]
        public int BandPctOfDist{get;set;}

        [NinjaScriptProperty][Range(20,500)]
        [Display(Name="Umbral Muestra Mínima",
                 Description="V3.0: Si condCount < umbral se relajan filtros automáticamente (primero AMT, luego banda). Recomendado NQ/ES: 120.",
                 GroupName="6. Probabilidad",Order=7)]
        public int MinSampleThreshold{get;set;}

        [NinjaScriptProperty][Range(50,2000)]
        [Display(Name="Umbral Confianza Alta (verde)",
                 Description="V3.0: n >= este valor → color verde en tabla. Recomendado NQ/ES: 300.",
                 GroupName="6. Probabilidad",Order=8)]
        public int MinConfianzaAlta{get;set;}

        [NinjaScriptProperty]
        [Display(Name="Normalizar por ATR (volatilidad)",
                 Description="V3.0: Normaliza distancias por el rango diario histórico. Útil para comparar regímenes de baja/alta volatilidad (NQ 2022 vs 2024).",
                 GroupName="6. Probabilidad",Order=9)]
        public bool UsarNormalizacionATR{get;set;}

        [NinjaScriptProperty][Range(0,2000)]
        [Display(Name="Máx. Sesiones Historial (0=ilimitado)",
                 Description="V3.0: Ventana deslizante. 0=sin límite. Recomendado NQ/ES para ponderar mercado reciente: 500.",
                 GroupName="6. Probabilidad",Order=10)]
        public int MaxSesionesHistorial{get;set;}

        [NinjaScriptProperty][Range(1,50)]
        [Display(Name="Dist. Mín. Solapamiento (ticks)",
                 Description="V3.0: Niveles más cercanos que este valor se marcan como solapados (≈) en la tabla. Sus probabilidades no son independientes.",
                 GroupName="6. Probabilidad",Order=11)]
        public int MinDistSolapTicks{get;set;}

        [NinjaScriptProperty][Display(Name="Mostrar Etiquetas Badge",GroupName="7. Visual Badge",Order=0)]
        public bool MostrarBadge{get;set;}
        [NinjaScriptProperty][Range(5,30)][Display(Name="Tamaño Fuente Badge",GroupName="7. Visual Badge",Order=1)]
        public int BadgeFontSize{get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Opacidad Fondo Badge (%)",GroupName="7. Visual Badge",Order=2)]
        public int BadgeBgOpacity{get;set;}

        [NinjaScriptProperty]
        [Display(Name="Mostrar Tabla",GroupName="8. Tabla V3.0",Order=0)]
        public bool MostrarTabla{get;set;}
        [NinjaScriptProperty][Range(5,24)][Display(Name="Tamaño Fuente Tabla",GroupName="8. Tabla V3.0",Order=1)]
        public int TablaFontSize{get;set;}
        [NinjaScriptProperty][Range(0,100)][Display(Name="Opacidad Fondo Tabla (%)",GroupName="8. Tabla V3.0",Order=2)]
        public int TablaBgOpacity{get;set;}
        [NinjaScriptProperty][Display(Name="Posición (Esquina)",GroupName="8. Tabla V3.0",Order=3)]
        public EduS_TablaCorner TablaCorner{get;set;}
        [NinjaScriptProperty][Range(0,800)][Display(Name="Offset X (px)",GroupName="8. Tabla V3.0",Order=4)]
        public int TablaOffsetX{get;set;}
        [NinjaScriptProperty][Range(0,800)][Display(Name="Offset Y (px)",GroupName="8. Tabla V3.0",Order=5)]
        public int TablaOffsetY{get;set;}

        [Browsable(false)][XmlIgnore] public Series<double> PApertura=>Values[0];
        [Browsable(false)][XmlIgnore] public Series<double> YHigh    =>Values[1];
        [Browsable(false)][XmlIgnore] public Series<double> PreHigh  =>Values[2];
        [Browsable(false)][XmlIgnore] public Series<double> HistMax  =>Values[3];
        [Browsable(false)][XmlIgnore] public Series<double> YLow     =>Values[4];
        [Browsable(false)][XmlIgnore] public Series<double> PreLow   =>Values[5];
        [Browsable(false)][XmlIgnore] public Series<double> PCierre  =>Values[6];
        [Browsable(false)][XmlIgnore] public Series<double> OrbHigh  =>Values[7];
        [Browsable(false)][XmlIgnore] public Series<double> OrbLow   =>Values[8];
        [Browsable(false)][XmlIgnore] public Series<double> OrbMid   =>Values[9];
        [Browsable(false)][XmlIgnore] public Series<double> YPOC     =>Values[10];
        [Browsable(false)][XmlIgnore] public Series<double> YVAH     =>Values[11];
        [Browsable(false)][XmlIgnore] public Series<double> YVAL     =>Values[12];
        [Browsable(false)][XmlIgnore] public Series<double> OnPOC    =>Values[13];
        [Browsable(false)][XmlIgnore] public Series<double> OnVAH    =>Values[14];
        [Browsable(false)][XmlIgnore] public Series<double> OnVAL    =>Values[15];

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduS_Unified_Market_Context_V2[] cacheEduS_Unified_Market_Context_V2;
		public EduS_Trader.EduS_Unified_Market_Context_V2 EduS_Unified_Market_Context_V2(int rthOpenTime, int rthCloseTime, int globexStartTime, int orbSeconds, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, EduS_HistMode histMode, double manualHistMax, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints, int valueAreaPercent, bool mostrarPerfilRTHAyer, bool mostrarPerfilON, bool mostrarProbabilidad, int minSessions, int distanceBandTicks, bool filtrarPorContextoAMT, EduS_SegmentoEstad segmentoEstadistica, EduS_ModoRango modoRangoEstadistica, int bandPctOfDist, int minSampleThreshold, int minConfianzaAlta, bool usarNormalizacionATR, int maxSesionesHistorial, int minDistSolapTicks, bool mostrarBadge, int badgeFontSize, int badgeBgOpacity, bool mostrarTabla, int tablaFontSize, int tablaBgOpacity, EduS_TablaCorner tablaCorner, int tablaOffsetX, int tablaOffsetY)
		{
			return EduS_Unified_Market_Context_V2(Input, rthOpenTime, rthCloseTime, globexStartTime, orbSeconds, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, histMode, manualHistMax, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints, valueAreaPercent, mostrarPerfilRTHAyer, mostrarPerfilON, mostrarProbabilidad, minSessions, distanceBandTicks, filtrarPorContextoAMT, segmentoEstadistica, modoRangoEstadistica, bandPctOfDist, minSampleThreshold, minConfianzaAlta, usarNormalizacionATR, maxSesionesHistorial, minDistSolapTicks, mostrarBadge, badgeFontSize, badgeBgOpacity, mostrarTabla, tablaFontSize, tablaBgOpacity, tablaCorner, tablaOffsetX, tablaOffsetY);
		}

		public EduS_Trader.EduS_Unified_Market_Context_V2 EduS_Unified_Market_Context_V2(ISeries<double> input, int rthOpenTime, int rthCloseTime, int globexStartTime, int orbSeconds, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, EduS_HistMode histMode, double manualHistMax, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints, int valueAreaPercent, bool mostrarPerfilRTHAyer, bool mostrarPerfilON, bool mostrarProbabilidad, int minSessions, int distanceBandTicks, bool filtrarPorContextoAMT, EduS_SegmentoEstad segmentoEstadistica, EduS_ModoRango modoRangoEstadistica, int bandPctOfDist, int minSampleThreshold, int minConfianzaAlta, bool usarNormalizacionATR, int maxSesionesHistorial, int minDistSolapTicks, bool mostrarBadge, int badgeFontSize, int badgeBgOpacity, bool mostrarTabla, int tablaFontSize, int tablaBgOpacity, EduS_TablaCorner tablaCorner, int tablaOffsetX, int tablaOffsetY)
		{
			if (cacheEduS_Unified_Market_Context_V2 != null)
				for (int idx = 0; idx < cacheEduS_Unified_Market_Context_V2.Length; idx++)
					if (cacheEduS_Unified_Market_Context_V2[idx] != null && cacheEduS_Unified_Market_Context_V2[idx].RthOpenTime == rthOpenTime && cacheEduS_Unified_Market_Context_V2[idx].RthCloseTime == rthCloseTime && cacheEduS_Unified_Market_Context_V2[idx].GlobexStartTime == globexStartTime && cacheEduS_Unified_Market_Context_V2[idx].OrbSeconds == orbSeconds && cacheEduS_Unified_Market_Context_V2[idx].CultureName == cultureName && cacheEduS_Unified_Market_Context_V2[idx].Decimals == decimals && cacheEduS_Unified_Market_Context_V2[idx].LineWidth == lineWidth && cacheEduS_Unified_Market_Context_V2[idx].RightLabelPadding == rightLabelPadding && cacheEduS_Unified_Market_Context_V2[idx].LabelAbove == labelAbove && cacheEduS_Unified_Market_Context_V2[idx].HistMode == histMode && cacheEduS_Unified_Market_Context_V2[idx].ManualHistMax == manualHistMax && cacheEduS_Unified_Market_Context_V2[idx].ShowGlobalTimes == showGlobalTimes && cacheEduS_Unified_Market_Context_V2[idx].GlobalTimeColor == globalTimeColor && cacheEduS_Unified_Market_Context_V2[idx].GlobalTimeStyle == globalTimeStyle && cacheEduS_Unified_Market_Context_V2[idx].GlobalTimeWidth == globalTimeWidth && cacheEduS_Unified_Market_Context_V2[idx].GlobalTimeOpacity == globalTimeOpacity && cacheEduS_Unified_Market_Context_V2[idx].GlobalTimeFontSize == globalTimeFontSize && cacheEduS_Unified_Market_Context_V2[idx].GlobalTimeTextOrientation == globalTimeTextOrientation && cacheEduS_Unified_Market_Context_V2[idx].GlobalTimeTextPosition == globalTimeTextPosition && cacheEduS_Unified_Market_Context_V2[idx].GlobalTimeTextOffsetPoints == globalTimeTextOffsetPoints && cacheEduS_Unified_Market_Context_V2[idx].ValueAreaPercent == valueAreaPercent && cacheEduS_Unified_Market_Context_V2[idx].MostrarPerfilRTHAyer == mostrarPerfilRTHAyer && cacheEduS_Unified_Market_Context_V2[idx].MostrarPerfilON == mostrarPerfilON && cacheEduS_Unified_Market_Context_V2[idx].MostrarProbabilidad == mostrarProbabilidad && cacheEduS_Unified_Market_Context_V2[idx].MinSessions == minSessions && cacheEduS_Unified_Market_Context_V2[idx].DistanceBandTicks == distanceBandTicks && cacheEduS_Unified_Market_Context_V2[idx].FiltrarPorContextoAMT == filtrarPorContextoAMT && cacheEduS_Unified_Market_Context_V2[idx].SegmentoEstadistica == segmentoEstadistica && cacheEduS_Unified_Market_Context_V2[idx].ModoRangoEstadistica == modoRangoEstadistica && cacheEduS_Unified_Market_Context_V2[idx].BandPctOfDist == bandPctOfDist && cacheEduS_Unified_Market_Context_V2[idx].MinSampleThreshold == minSampleThreshold && cacheEduS_Unified_Market_Context_V2[idx].MinConfianzaAlta == minConfianzaAlta && cacheEduS_Unified_Market_Context_V2[idx].UsarNormalizacionATR == usarNormalizacionATR && cacheEduS_Unified_Market_Context_V2[idx].MaxSesionesHistorial == maxSesionesHistorial && cacheEduS_Unified_Market_Context_V2[idx].MinDistSolapTicks == minDistSolapTicks && cacheEduS_Unified_Market_Context_V2[idx].MostrarBadge == mostrarBadge && cacheEduS_Unified_Market_Context_V2[idx].BadgeFontSize == badgeFontSize && cacheEduS_Unified_Market_Context_V2[idx].BadgeBgOpacity == badgeBgOpacity && cacheEduS_Unified_Market_Context_V2[idx].MostrarTabla == mostrarTabla && cacheEduS_Unified_Market_Context_V2[idx].TablaFontSize == tablaFontSize && cacheEduS_Unified_Market_Context_V2[idx].TablaBgOpacity == tablaBgOpacity && cacheEduS_Unified_Market_Context_V2[idx].TablaCorner == tablaCorner && cacheEduS_Unified_Market_Context_V2[idx].TablaOffsetX == tablaOffsetX && cacheEduS_Unified_Market_Context_V2[idx].TablaOffsetY == tablaOffsetY && cacheEduS_Unified_Market_Context_V2[idx].EqualsInput(input))
						return cacheEduS_Unified_Market_Context_V2[idx];
			return CacheIndicator<EduS_Trader.EduS_Unified_Market_Context_V2>(new EduS_Trader.EduS_Unified_Market_Context_V2(){ RthOpenTime = rthOpenTime, RthCloseTime = rthCloseTime, GlobexStartTime = globexStartTime, OrbSeconds = orbSeconds, CultureName = cultureName, Decimals = decimals, LineWidth = lineWidth, RightLabelPadding = rightLabelPadding, LabelAbove = labelAbove, HistMode = histMode, ManualHistMax = manualHistMax, ShowGlobalTimes = showGlobalTimes, GlobalTimeColor = globalTimeColor, GlobalTimeStyle = globalTimeStyle, GlobalTimeWidth = globalTimeWidth, GlobalTimeOpacity = globalTimeOpacity, GlobalTimeFontSize = globalTimeFontSize, GlobalTimeTextOrientation = globalTimeTextOrientation, GlobalTimeTextPosition = globalTimeTextPosition, GlobalTimeTextOffsetPoints = globalTimeTextOffsetPoints, ValueAreaPercent = valueAreaPercent, MostrarPerfilRTHAyer = mostrarPerfilRTHAyer, MostrarPerfilON = mostrarPerfilON, MostrarProbabilidad = mostrarProbabilidad, MinSessions = minSessions, DistanceBandTicks = distanceBandTicks, FiltrarPorContextoAMT = filtrarPorContextoAMT, SegmentoEstadistica = segmentoEstadistica, ModoRangoEstadistica = modoRangoEstadistica, BandPctOfDist = bandPctOfDist, MinSampleThreshold = minSampleThreshold, MinConfianzaAlta = minConfianzaAlta, UsarNormalizacionATR = usarNormalizacionATR, MaxSesionesHistorial = maxSesionesHistorial, MinDistSolapTicks = minDistSolapTicks, MostrarBadge = mostrarBadge, BadgeFontSize = badgeFontSize, BadgeBgOpacity = badgeBgOpacity, MostrarTabla = mostrarTabla, TablaFontSize = tablaFontSize, TablaBgOpacity = tablaBgOpacity, TablaCorner = tablaCorner, TablaOffsetX = tablaOffsetX, TablaOffsetY = tablaOffsetY }, input, ref cacheEduS_Unified_Market_Context_V2);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduS_Unified_Market_Context_V2 EduS_Unified_Market_Context_V2(int rthOpenTime, int rthCloseTime, int globexStartTime, int orbSeconds, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, EduS_HistMode histMode, double manualHistMax, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints, int valueAreaPercent, bool mostrarPerfilRTHAyer, bool mostrarPerfilON, bool mostrarProbabilidad, int minSessions, int distanceBandTicks, bool filtrarPorContextoAMT, EduS_SegmentoEstad segmentoEstadistica, EduS_ModoRango modoRangoEstadistica, int bandPctOfDist, int minSampleThreshold, int minConfianzaAlta, bool usarNormalizacionATR, int maxSesionesHistorial, int minDistSolapTicks, bool mostrarBadge, int badgeFontSize, int badgeBgOpacity, bool mostrarTabla, int tablaFontSize, int tablaBgOpacity, EduS_TablaCorner tablaCorner, int tablaOffsetX, int tablaOffsetY)
		{
			return indicator.EduS_Unified_Market_Context_V2(Input, rthOpenTime, rthCloseTime, globexStartTime, orbSeconds, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, histMode, manualHistMax, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints, valueAreaPercent, mostrarPerfilRTHAyer, mostrarPerfilON, mostrarProbabilidad, minSessions, distanceBandTicks, filtrarPorContextoAMT, segmentoEstadistica, modoRangoEstadistica, bandPctOfDist, minSampleThreshold, minConfianzaAlta, usarNormalizacionATR, maxSesionesHistorial, minDistSolapTicks, mostrarBadge, badgeFontSize, badgeBgOpacity, mostrarTabla, tablaFontSize, tablaBgOpacity, tablaCorner, tablaOffsetX, tablaOffsetY);
		}

		public Indicators.EduS_Trader.EduS_Unified_Market_Context_V2 EduS_Unified_Market_Context_V2(ISeries<double> input , int rthOpenTime, int rthCloseTime, int globexStartTime, int orbSeconds, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, EduS_HistMode histMode, double manualHistMax, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints, int valueAreaPercent, bool mostrarPerfilRTHAyer, bool mostrarPerfilON, bool mostrarProbabilidad, int minSessions, int distanceBandTicks, bool filtrarPorContextoAMT, EduS_SegmentoEstad segmentoEstadistica, EduS_ModoRango modoRangoEstadistica, int bandPctOfDist, int minSampleThreshold, int minConfianzaAlta, bool usarNormalizacionATR, int maxSesionesHistorial, int minDistSolapTicks, bool mostrarBadge, int badgeFontSize, int badgeBgOpacity, bool mostrarTabla, int tablaFontSize, int tablaBgOpacity, EduS_TablaCorner tablaCorner, int tablaOffsetX, int tablaOffsetY)
		{
			return indicator.EduS_Unified_Market_Context_V2(input, rthOpenTime, rthCloseTime, globexStartTime, orbSeconds, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, histMode, manualHistMax, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints, valueAreaPercent, mostrarPerfilRTHAyer, mostrarPerfilON, mostrarProbabilidad, minSessions, distanceBandTicks, filtrarPorContextoAMT, segmentoEstadistica, modoRangoEstadistica, bandPctOfDist, minSampleThreshold, minConfianzaAlta, usarNormalizacionATR, maxSesionesHistorial, minDistSolapTicks, mostrarBadge, badgeFontSize, badgeBgOpacity, mostrarTabla, tablaFontSize, tablaBgOpacity, tablaCorner, tablaOffsetX, tablaOffsetY);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduS_Unified_Market_Context_V2 EduS_Unified_Market_Context_V2(int rthOpenTime, int rthCloseTime, int globexStartTime, int orbSeconds, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, EduS_HistMode histMode, double manualHistMax, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints, int valueAreaPercent, bool mostrarPerfilRTHAyer, bool mostrarPerfilON, bool mostrarProbabilidad, int minSessions, int distanceBandTicks, bool filtrarPorContextoAMT, EduS_SegmentoEstad segmentoEstadistica, EduS_ModoRango modoRangoEstadistica, int bandPctOfDist, int minSampleThreshold, int minConfianzaAlta, bool usarNormalizacionATR, int maxSesionesHistorial, int minDistSolapTicks, bool mostrarBadge, int badgeFontSize, int badgeBgOpacity, bool mostrarTabla, int tablaFontSize, int tablaBgOpacity, EduS_TablaCorner tablaCorner, int tablaOffsetX, int tablaOffsetY)
		{
			return indicator.EduS_Unified_Market_Context_V2(Input, rthOpenTime, rthCloseTime, globexStartTime, orbSeconds, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, histMode, manualHistMax, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints, valueAreaPercent, mostrarPerfilRTHAyer, mostrarPerfilON, mostrarProbabilidad, minSessions, distanceBandTicks, filtrarPorContextoAMT, segmentoEstadistica, modoRangoEstadistica, bandPctOfDist, minSampleThreshold, minConfianzaAlta, usarNormalizacionATR, maxSesionesHistorial, minDistSolapTicks, mostrarBadge, badgeFontSize, badgeBgOpacity, mostrarTabla, tablaFontSize, tablaBgOpacity, tablaCorner, tablaOffsetX, tablaOffsetY);
		}

		public Indicators.EduS_Trader.EduS_Unified_Market_Context_V2 EduS_Unified_Market_Context_V2(ISeries<double> input , int rthOpenTime, int rthCloseTime, int globexStartTime, int orbSeconds, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, EduS_HistMode histMode, double manualHistMax, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints, int valueAreaPercent, bool mostrarPerfilRTHAyer, bool mostrarPerfilON, bool mostrarProbabilidad, int minSessions, int distanceBandTicks, bool filtrarPorContextoAMT, EduS_SegmentoEstad segmentoEstadistica, EduS_ModoRango modoRangoEstadistica, int bandPctOfDist, int minSampleThreshold, int minConfianzaAlta, bool usarNormalizacionATR, int maxSesionesHistorial, int minDistSolapTicks, bool mostrarBadge, int badgeFontSize, int badgeBgOpacity, bool mostrarTabla, int tablaFontSize, int tablaBgOpacity, EduS_TablaCorner tablaCorner, int tablaOffsetX, int tablaOffsetY)
		{
			return indicator.EduS_Unified_Market_Context_V2(input, rthOpenTime, rthCloseTime, globexStartTime, orbSeconds, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, histMode, manualHistMax, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints, valueAreaPercent, mostrarPerfilRTHAyer, mostrarPerfilON, mostrarProbabilidad, minSessions, distanceBandTicks, filtrarPorContextoAMT, segmentoEstadistica, modoRangoEstadistica, bandPctOfDist, minSampleThreshold, minConfianzaAlta, usarNormalizacionATR, maxSesionesHistorial, minDistSolapTicks, mostrarBadge, badgeFontSize, badgeBgOpacity, mostrarTabla, tablaFontSize, tablaBgOpacity, tablaCorner, tablaOffsetX, tablaOffsetY);
		}
	}
}

#endregion
