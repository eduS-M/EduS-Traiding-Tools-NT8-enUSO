/*
 * EduS_Unified_Market_Context — Indicador NT8 (V2 - Market Profile Integrado)
 * Autor: EduS Trader
 * ------------------------------------------------
 * INTEGRACIÓN V2: RTH Levels V4 fusionado en este indicador.
 *
 * NUEVOS NIVELES (Market Profile):
 *   RTH Ayer:
 *     yPOC  — POC del RTH de ayer        (plot 10, amarillo)
 *     yVAH  — Value Area High RTH ayer   (plot 11, verde lima)
 *     yVAL  — Value Area Low  RTH ayer   (plot 12, verde lima)
 *   Overnight (18:00 → 09:30 Globex):
 *     onPOC — POC overnight              (plot 13, azul cielo)
 *     onVAH — Value Area High ON         (plot 14, azul aciano)
 *     onVAL — Value Area Low  ON         (plot 15, azul aciano)
 *
 * PROBABILIDADES (RTH Levels V4):
 *   Cada etiqueta muestra "  |  XX%  n=NNN" al cruzar la apertura RTH.
 *   Filtro doble: posición relativa al OPEN + distancia similar en ticks.
 *   Cubre todos los niveles: clásicos + Market Profile.
 *
 * VOLUME PROFILE (misma lógica que EduSTraderNodesV8Institutional):
 *   Histórico → modo OHLC (vol × 0.25 en OHLC)
 *   Tiempo real → modo Full (tick a tick entre Low y High)
 *   Value Area → expande desde POC hasta cubrir ValueAreaPercent del volumen.
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
#endregion

// Enums globales
public enum EduS_HistMode   { Dinamico, Manual_Fijo }
public enum EduS_TextOrient { Horizontal, Vertical }
public enum EduS_TextPos    { Arriba, Abajo }

namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader
{
    // =========================================================
    // REGISTRO HISTÓRICO POR SESIÓN (probabilidades)
    // =========================================================

    // ---------------------------------------------------------
    // V3 — Contexto de apertura según Teoría de Subasta (AMT)
    //
    // La posición del Open respecto al Value Area de ayer
    // determina el tipo de día probable y el sesgo direccional:
    //
    //   AboveVA  → Open por encima del yVAH
    //              Sesgo: actividad iniciativa alcista.
    //              El mercado está probando si puede aceptar
    //              precios más altos. Alta probabilidad de día
    //              tendencial alcista o rechazo fuerte.
    //
    //   InsideVA → Open dentro del VA (entre yVAL y yVAH)
    //              Sesgo: mercado en equilibrio/rotación.
    //              Alta probabilidad de que el precio explore
    //              ambos extremos del VA durante la sesión.
    //
    //   BelowVA  → Open por debajo del yVAL
    //              Sesgo: actividad iniciativa bajista.
    //              El mercado está probando precios más bajos.
    //              Alta probabilidad de día tendencial bajista
    //              o rechazo fuerte hacia arriba.
    //
    // Esto es fundamental: el mismo nivel a la misma distancia
    // tiene una probabilidad de toque DIFERENTE según el contexto
    // de apertura. Mezclarlos en una sola estadística distorsiona
    // el resultado.
    // ---------------------------------------------------------
    public enum EduS_OpenContext
    {
        AboveVA,   // Open > yVAH de ayer
        InsideVA,  // yVAL ≤ Open ≤ yVAH de ayer
        BelowVA,   // Open < yVAL de ayer
        Unknown    // Sin datos de VA (primeras sesiones del historial)
    }

    // ---------------------------------------------------------
    // V3 — Tipo de nivel para búsqueda específica en historial
    //
    // En lugar de buscar "el nivel más cercano en distancia"
    // (que mezcla tipos), ahora cada nivel busca su equivalente
    // EXACTO en el historial. Así yPOC compara contra yPOC,
    // yVAH contra yVAH, etc.
    //
    // Esto elimina la contaminación estadística donde en una
    // sesión histórica el "equivalente" del yPOC de hoy podía
    // ser el onVAH de aquella sesión porque estaba más cerca.
    // ---------------------------------------------------------
    public enum EduS_LevelType
    {
        YHigh, YLow, PCierre, PreHigh, PreLow,
        YPOC, YVAH, YVAL,
        OnPOC, OnVAH, OnVAL
    }

    internal struct EduS_UMC_SesionRecord
    {
        public double Open;
        public double YHigh, YLow, PCierre, PreHigh, PreLow;
        public double YPOC,  YVAH,  YVAL;
        public double OnPOC, OnVAH, OnVAL;
        public double SessionHigh, SessionLow;

        // V3 — Contexto de apertura de ESTA sesión
        // (calculado al cruzar las 09:30 comparando Open vs VA del día anterior)
        public EduS_OpenContext OpenContext;
    }

    // =========================================================
    // INDICADOR PRINCIPAL
    // =========================================================
    public class EduS_Unified_Market_Context : Indicator
    {
        // ──────────────────────────────────────────────────────
        // ESTRUCTURAS ORIGINALES
        // ──────────────────────────────────────────────────────
        public class EduS_TimeEvent
        {
            public DateTime Time        { get; set; }
            public string   Label       { get; set; }
            public double   AnchorPrice { get; set; }
        }

        private List<EduS_TimeEvent>  globalEvents         = new List<EduS_TimeEvent>();
        private HashSet<string>       processedEventsToday = new HashSet<string>();

        // ──────────────────────────────────────────────────────
        // VARIABLES — niveles clásicos (originales)
        // ──────────────────────────────────────────────────────
        private double pApertura = double.NaN;
        private double yHigh     = double.NaN;
        private double yLow      = double.NaN;
        private double preHigh   = double.NaN;
        private double preLow    = double.NaN;
        private double pCierre   = double.NaN;
        private double histMax   = double.NaN;

        private double curRthHigh           = double.NaN;
        private double curRthLow            = double.NaN;
        private double tmpPreHigh           = double.NaN;
        private double tmpPreLow            = double.NaN;
        private double lastCompletedRthHigh = double.NaN;
        private double lastCompletedRthLow  = double.NaN;
        private double tempPCierre          = double.NaN;

        // ORB
        private double orb30High = double.NaN;
        private double orb30Low  = double.NaN;
        private double orb30Mid  = double.NaN;

        private DateTime currentSessionDate = DateTime.MinValue;
        private CultureInfo labelCulture;
        private SharpDX.Direct2D1.Brush cachedTimeBrush;

        // ──────────────────────────────────────────────────────
        // VARIABLES — Market Profile (V2)
        // ──────────────────────────────────────────────────────
        private double yPOC = double.NaN, yVAH = double.NaN, yVAL = double.NaN;
        private double pendYPOC = double.NaN, pendYVAH = double.NaN, pendYVAL = double.NaN;
        private double onPOC = double.NaN, onVAH = double.NaN, onVAL = double.NaN;

        private Dictionary<double, double> vpRTH = new Dictionary<double, double>();
        private Dictionary<double, double> vpON  = new Dictionary<double, double>();

        private double tickSz          = 0.25;
        private double valueAreaFactor = 0.70;

        // ──────────────────────────────────────────────────────
        // VARIABLES — Probabilidades (V2)
        // ──────────────────────────────────────────────────────
        private List<EduS_UMC_SesionRecord> sessionHistory = new List<EduS_UMC_SesionRecord>();
        private EduS_UMC_SesionRecord       currentRecord;
        private bool                        recordOpen = false;

        private double snapOpen  = double.NaN;
        private double snapYHigh = double.NaN, snapYLow   = double.NaN, snapPCierre = double.NaN;
        private double snapPreH  = double.NaN, snapPreL   = double.NaN;
        private double snapYPOC  = double.NaN, snapYVAH   = double.NaN, snapYVAL    = double.NaN;
        private double snapOnPOC = double.NaN, snapOnVAH  = double.NaN, snapOnVAL   = double.NaN;

        // V3 — Contexto de apertura de la sesión actual (AMT)
        // Se calcula al cruzar las 09:30 comparando el Open con el VA de ayer (yVAH/yVAL)
        private EduS_OpenContext currentOpenContext = EduS_OpenContext.Unknown;

        private double probYHigh  = double.NaN, probYLow   = double.NaN, probPCierre = double.NaN;
        private double probPreH   = double.NaN, probPreL   = double.NaN;
        private double probYPOC   = double.NaN, probYVAH   = double.NaN, probYVAL    = double.NaN;
        private double probOnPOC  = double.NaN, probOnVAH  = double.NaN, probOnVAL   = double.NaN;

        private int nYHigh = 0, nYLow = 0, nPCierre = 0, nPreH = 0, nPreL = 0;
        private int nYPOC  = 0, nYVAH = 0, nYVAL    = 0;
        private int nOnPOC = 0, nOnVAH= 0, nOnVAL   = 0;

        // ── Rango sesión actual para detección de niveles tocados (V3) ─
        // Solo rango RTH (curRthHigh/curRthLow) — pre-market/ON no cuenta
        private double sessionHigh = double.NaN;  // mantenido para otros usos internos
        private double sessionLow  = double.NaN;
        private bool LevelTouched(double level)
        {
            // Usa SOLO el rango RTH de la sesión actual
            if (double.IsNaN(level) || double.IsNaN(curRthHigh) || double.IsNaN(curRthLow))
                return false;
            double tol = tickSz * 0.5;
            return level <= curRthHigh + tol && level >= curRthLow - tol;
        }

        // ──────────────────────────────────────────────────────
        // HELPERS
        // ──────────────────────────────────────────────────────
        private bool InRth(int t) => t >= RthOpenTime && t < RthCloseTime;

        // =========================================================
        // OnStateChange
        // =========================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name             = "EduS_Unified_Market_Context";
                Description      = "EdusTrader V2 — Niveles Estructurales + ORB + Market Profile (POC/VAH/VAL RTH Ayer y ON) + Probabilidades.";
                Calculate        = Calculate.OnBarClose;
                IsOverlay        = true;
                DisplayInDataBox = true;
                DrawOnPricePanel = false;
                PaintPriceMarkers= true;
                IsAutoScale      = false;
                ScaleJustification = ScaleJustification.Right;

                // Horarios
                RthOpenTime     = 93000;
                RthCloseTime    = 160000;
                GlobexStartTime = 180000;
                OrbSeconds      = 30;

                // HistMax
                HistMode      = EduS_HistMode.Dinamico;
                ManualHistMax = 0;

                // Visual
                CultureName       = "es-ES";
                Decimals          = 2;
                LineWidth         = 2;
                RightLabelPadding = 15;
                LabelAbove        = 2;

                // Horarios Globales
                ShowGlobalTimes            = true;
                GlobalTimeColor            = Brushes.DimGray;
                GlobalTimeStyle            = DashStyleHelper.Dash;
                GlobalTimeWidth            = 1;
                GlobalTimeOpacity          = 60;
                GlobalTimeFontSize         = 10;
                GlobalTimeTextOrientation  = EduS_TextOrient.Vertical;
                GlobalTimeTextPosition     = EduS_TextPos.Arriba;
                GlobalTimeTextOffsetPoints = 5;

                // Market Profile (V2)
                ValueAreaPercent     = 70;
                MostrarPerfilRTHAyer = true;
                MostrarPerfilON      = true;

                // Probabilidades (V2)
                MostrarProbabilidad = true;
                MinSessions         = 20;
                DistanceBandTicks   = 50;

                // V3 — Filtro AMT (desactivado por defecto para compatibilidad
                // con historiales cortos; activar cuando haya 200+ sesiones)
                FiltrarPorContextoAMT = false;

                // Badge / Tabla (V3)
                MostrarBadge    = true;
                BadgeFontSize   = 11;
                BadgeBgOpacity  = 65;
                MostrarTabla    = true;
                TablaFontSize   = 11;
                TablaBgOpacity  = 75;

                // ── Plots ──────────────────────────────────────────────
                // Clásicos
                AddPlot(Brushes.DarkGreen,     "PApertura");    // 0
                AddPlot(Brushes.DarkGreen,     "yHigh");        // 1
                AddPlot(Brushes.DarkGreen,     "PreHigh");      // 2
                AddPlot(Brushes.Red,           "HistMax");      // 3
                AddPlot(Brushes.DarkGreen,     "yLow");         // 4
                AddPlot(Brushes.DarkGreen,     "PreLow");       // 5
                AddPlot(Brushes.DarkGreen,     "PCierre");      // 6
                // ORB
                AddPlot(Brushes.Cyan,          "ORB_High");     // 7
                AddPlot(Brushes.Magenta,       "ORB_Low");      // 8
                AddPlot(Brushes.Gold,          "ORB_Mid");      // 9
                // RTH Ayer — Market Profile
                AddPlot(Brushes.Yellow,        "yPOC");         // 10
                AddPlot(Brushes.GreenYellow,   "yVAH");         // 11
                AddPlot(Brushes.GreenYellow,   "yVAL");         // 12
                // Overnight — Market Profile
                AddPlot(Brushes.DeepSkyBlue,   "onPOC");        // 13
                AddPlot(Brushes.CornflowerBlue,"onVAH");        // 14
                AddPlot(Brushes.CornflowerBlue,"onVAL");        // 15

                for (int i = 0; i < Plots.Length; i++)
                {
                    bool isDot = (i == 3);
                    bool isMP  = (i >= 10);
                    bool isORB = (i >= 7 && i <= 9);
                    Plots[i].DashStyleHelper = isDot  ? DashStyleHelper.Dot
                                             : isMP   ? DashStyleHelper.Solid
                                             : isORB  ? DashStyleHelper.Solid
                                             : DashStyleHelper.DashDot;
                    Plots[i].Width = 2; // todos por defecto grosor 2
                }
            }
            else if (State == State.Configure)
            {
                // BIP 1: ORB (serie de segundos)
                AddDataSeries(BarsPeriodType.Second, OrbSeconds);
            }
            else if (State == State.DataLoaded)
            {
                labelCulture = new CultureInfo(CultureName);
                if (globalEvents == null)       globalEvents = new List<EduS_TimeEvent>();
                else                            globalEvents.Clear();
                if (processedEventsToday == null) processedEventsToday = new HashSet<string>();
                else                            processedEventsToday.Clear();

                tickSz          = (Instrument != null) ? Instrument.MasterInstrument.TickSize : 0.25;
                valueAreaFactor = Math.Max(0.50, Math.Min(0.99, ValueAreaPercent / 100.0));
                vpRTH.Clear();
                vpON.Clear();
                sessionHistory.Clear();
            }
            else if (State == State.Historical)
            {
                if (globalEvents != null)       globalEvents.Clear();
                if (processedEventsToday != null) processedEventsToday.Clear();
            }
            else if (State == State.Terminated)
            {
                if (cachedTimeBrush != null) { cachedTimeBrush.Dispose(); cachedTimeBrush = null; }
            }
        }

        // =========================================================
        // OnBarUpdate
        // =========================================================
        protected override void OnBarUpdate()
        {
            // ── BIP 1: ORB ────────────────────────────────────────────
            if (BarsInProgress == 1)
            {
                DateTime tBar    = Time[0];
                int      tBarInt = ToTime(tBar);
                TimeSpan openTS  = new TimeSpan(RthOpenTime / 10000, (RthOpenTime % 10000) / 100, RthOpenTime % 100);
                TimeSpan targetTS= openTS.Add(TimeSpan.FromSeconds(OrbSeconds));
                int targetTimeInt= targetTS.Hours * 10000 + targetTS.Minutes * 100 + targetTS.Seconds;

                if (tBar.Date == currentSessionDate && tBarInt == targetTimeInt)
                {
                    orb30High = High[0];
                    orb30Low  = Low[0];
                    orb30Mid  = Math.Round((orb30High + orb30Low) / 2.0 / TickSize) * TickSize;
                }
                return;
            }

            // ── BIP 0: Serie principal ────────────────────────────────
            if (BarsInProgress != 0) return;

            bool fastMode = (State == State.Historical);

            // ── Horarios globales ─────────────────────────────────────
            if (ShowGlobalTimes && (IsFirstTickOfBar || Calculate == Calculate.OnBarClose))
            {
                DateTime barTime = Time[0];
                TimeZoneInfo easternZone;
                try   { easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
                catch { easternZone = TimeZoneInfo.Local; }

                DateTime easternTime = TimeZoneInfo.ConvertTime(barTime, Core.Globals.GeneralOptions.TimeZoneInfo, easternZone);
                TimeSpan etTimeOfDay = easternTime.TimeOfDay;
                string   dateStr     = easternTime.ToString("yyyyMMdd");

                if (Bars.IsFirstBarOfSession) processedEventsToday.Clear();

                CheckAndAddEvent("EurexOpen",   new TimeSpan(3,  0, 0), "3:00 (ET) Apertura Eurex",  dateStr, etTimeOfDay, barTime);
                CheckAndAddEvent("LondonClose", new TimeSpan(11,30, 0), "11:30 (ET) Cierre Londres", dateStr, etTimeOfDay, barTime);
                CheckAndAddEvent("AsiaOpen",    new TimeSpan(19, 0, 0), "19:00 (ET) Apertura Asia",  dateStr, etTimeOfDay, barTime);
                CheckAndAddEvent("AsiaClose",   new TimeSpan(2,  0, 0), "2:00 (ET) Cierre Asia",     dateStr, etTimeOfDay, barTime);
                CheckAndAddEvent("LondonOpen",  new TimeSpan(4,  0, 0), "4:00 (ET) Apertura Londres",dateStr, etTimeOfDay, barTime);
            }

            if (CurrentBar == 0)
            {
                histMax = (HistMode == EduS_HistMode.Manual_Fijo) ? ManualHistMax : High[0];
                if (ToTime(Time[0]) < RthOpenTime)
                { tmpPreHigh = High[0]; tmpPreLow = Low[0]; preHigh = tmpPreHigh; preLow = tmpPreLow; }
                SetAllPlots(); return;
            }

            int tNow  = ToTime(Time[0]);
            int tPrev = ToTime(Time[1]);

            // Max Histórico
            histMax = (HistMode == EduS_HistMode.Dinamico) ? Math.Max(histMax, High[0]) : ManualHistMax;

            // ── Cambio de día (00:00) ─────────────────────────────────
            if (Time[0].Date > Time[1].Date)
            {
                if (!double.IsNaN(lastCompletedRthHigh)) yHigh   = lastCompletedRthHigh;
                if (!double.IsNaN(lastCompletedRthLow))  yLow    = lastCompletedRthLow;
                if (!double.IsNaN(tempPCierre))           pCierre = tempPCierre;

                // Publicar Market Profile RTH ayer
                if (!double.IsNaN(pendYPOC)) { yPOC = pendYPOC; yVAH = pendYVAH; yVAL = pendYVAL; }

                tmpPreHigh = High[0]; tmpPreLow = Low[0];
                preHigh = tmpPreHigh; preLow = tmpPreLow;
                orb30High = double.NaN; orb30Low = double.NaN; orb30Mid = double.NaN;
                currentSessionDate = Time[0].Date;

                // Reiniciar rango de sesión y ON para nuevo ciclo
                sessionHigh = High[0]; sessionLow = Low[0];
                vpON.Clear();
            }

            // ── Pre-Market dinámico (00:00 → 09:30) ──────────────────
            if (tNow < RthOpenTime)
            {
                tmpPreHigh = double.IsNaN(tmpPreHigh) ? High[0] : Math.Max(tmpPreHigh, High[0]);
                tmpPreLow  = double.IsNaN(tmpPreLow)  ? Low[0]  : Math.Min(tmpPreLow,  Low[0]);
                preHigh = tmpPreHigh; preLow = tmpPreLow;
            }

            // ── Acumulación Overnight VP (18:00 → 09:30) ─────────────
            bool isOnBar = (tNow >= GlobexStartTime || tNow < RthOpenTime);
            if (isOnBar)
            {
                AccumVP(vpON, Volume[0], High[0], Low[0], Close[0], Open[0], fastMode);
                CalcProfile(vpON, out double oPoc, out double oVah, out double oVal);
                if (!double.IsNaN(oPoc)) { onPOC = oPoc; onVAH = oVah; onVAL = oVal; }
            }

            // ── Apertura RTH (09:30) ──────────────────────────────────
            bool crossedOpen = (tPrev < RthOpenTime && tNow >= RthOpenTime);
            if (crossedOpen)
            {
                pApertura = Open[0];

                // Cerrar registro histórico anterior
                if (recordOpen && !double.IsNaN(curRthHigh) && !double.IsNaN(curRthLow))
                {
                    currentRecord.SessionHigh = curRthHigh;
                    currentRecord.SessionLow  = curRthLow;
                    sessionHistory.Add(currentRecord);
                }

                // ---------------------------------------------------------
                // V3 — Calcular contexto de apertura (AMT)
                //
                // Comparamos el Open de hoy contra el VA de ayer (yVAH / yVAL)
                // para clasificar el tipo de apertura según la Teoría de Subasta.
                //
                // Este contexto se guarda en el registro histórico para que
                // en el futuro solo se comparen sesiones con el mismo contexto,
                // produciendo probabilidades estadísticamente más puras.
                // ---------------------------------------------------------
                if (!double.IsNaN(yVAH) && !double.IsNaN(yVAL))
                {
                    if      (pApertura > yVAH) currentOpenContext = EduS_OpenContext.AboveVA;
                    else if (pApertura < yVAL) currentOpenContext = EduS_OpenContext.BelowVA;
                    else                       currentOpenContext = EduS_OpenContext.InsideVA;
                }
                else
                {
                    // Sin VA de ayer disponible (primeras sesiones del historial)
                    currentOpenContext = EduS_OpenContext.Unknown;
                }

                // Snapshots para probabilidades
                snapOpen  = pApertura;
                snapYHigh = yHigh;  snapYLow  = yLow;   snapPCierre = pCierre;
                snapPreH  = preHigh; snapPreL = preLow;
                snapYPOC  = yPOC;   snapYVAH  = yVAH;   snapYVAL    = yVAL;
                snapOnPOC = onPOC;  snapOnVAH = onVAH;  snapOnVAL   = onVAL;

                if (MostrarProbabilidad && sessionHistory.Count >= MinSessions)
                    RecalcProbabilities();
                else
                    ResetProbabilities();

                // Abrir nuevo registro — incluye el contexto de apertura calculado arriba
                currentRecord = new EduS_UMC_SesionRecord
                {
                    Open    = pApertura,
                    YHigh   = yHigh,  YLow   = yLow,   PCierre = pCierre,
                    PreHigh = preHigh, PreLow = preLow,
                    YPOC    = yPOC,   YVAH   = yVAH,   YVAL    = yVAL,
                    OnPOC   = onPOC,  OnVAH  = onVAH,  OnVAL   = onVAL,
                    SessionHigh  = double.NaN, SessionLow = double.NaN,
                    OpenContext  = currentOpenContext   // V3 — contexto AMT de esta sesión
                };
                recordOpen = true;

                // Reiniciar RTH VP y rangos
                vpRTH.Clear();
                curRthHigh = High[0]; curRthLow = Low[0];
                tmpPreHigh = double.NaN; tmpPreLow = double.NaN;
                vpON.Clear();
            }

            // ── RTH (09:30 → 16:00) ───────────────────────────────────
            if (InRth(tNow))
            {
                curRthHigh = double.IsNaN(curRthHigh) ? High[0] : Math.Max(curRthHigh, High[0]);
                curRthLow  = double.IsNaN(curRthLow)  ? Low[0]  : Math.Min(curRthLow,  Low[0]);
                AccumVP(vpRTH, Volume[0], High[0], Low[0], Close[0], Open[0], fastMode);
            }

            // ── Rango de sesión completa (todo el día) ────────────────
            sessionHigh = double.IsNaN(sessionHigh) ? High[0] : Math.Max(sessionHigh, High[0]);
            sessionLow  = double.IsNaN(sessionLow)  ? Low[0]  : Math.Min(sessionLow,  Low[0]);

            // ── Cierre RTH (16:00) ────────────────────────────────────
            bool crossedClose = (tPrev < RthCloseTime && tNow >= RthCloseTime);
            if (crossedClose)
            {
                tempPCierre = Close[Math.Min(1, CurrentBar)];
                if (!double.IsNaN(curRthHigh)) lastCompletedRthHigh = curRthHigh;
                if (!double.IsNaN(curRthLow))  lastCompletedRthLow  = curRthLow;

                CalcProfile(vpRTH, out double pPoc, out double pVah, out double pVal);
                if (!double.IsNaN(pPoc)) { pendYPOC = pPoc; pendYVAH = pVah; pendYVAL = pVal; }
            }

            SetAllPlots();
        }

        // =========================================================
        // VOLUME PROFILE
        // =========================================================
        private void AccumVP(Dictionary<double, double> vp, double vol,
                             double high, double low, double close, double open, bool fastMode)
        {
            if (vol <= 0) return;
            if (fastMode)
            {
                double v4 = vol * 0.25;
                void Add(double p) {
                    double pr = Instrument.MasterInstrument.RoundToTickSize(p);
                    if (!vp.ContainsKey(pr)) vp[pr] = 0;
                    vp[pr] += v4;
                }
                Add(open); Add(high); Add(low); Add(close);
            }
            else
            {
                int    ticks = Math.Max(1, (int)Math.Round((high - low) / tickSz) + 1);
                double vt    = vol / ticks;
                for (int i = 0; i < ticks; i++)
                {
                    double pr = Instrument.MasterInstrument.RoundToTickSize(low + i * tickSz);
                    if (!vp.ContainsKey(pr)) vp[pr] = 0;
                    vp[pr] += vt;
                }
            }
        }

        private void CalcProfile(Dictionary<double, double> vp,
                                 out double poc, out double vah, out double val)
        {
            poc = vah = val = double.NaN;
            if (vp == null || vp.Count == 0) return;

            double maxVol = -1;
            foreach (var kv in vp)
                if (kv.Value > maxVol) { maxVol = kv.Value; poc = kv.Key; }

            var    sorted  = vp.OrderBy(kv => kv.Key).ToList();
            double pocSnap = poc;  // captura local para lambda (CS1628)
            int    pocIdx  = sorted.FindIndex(kv => kv.Key == pocSnap);
            if (pocIdx < 0) pocIdx = 0;

            double totalVol    = sorted.Sum(kv => kv.Value);
            double target      = totalVol * valueAreaFactor;
            double accumulated = sorted[pocIdx].Value;
            int lo = pocIdx, hi = pocIdx;

            while (accumulated < target && (lo > 0 || hi < sorted.Count - 1))
            {
                double volUp   = (hi + 1 < sorted.Count) ? sorted[hi + 1].Value : 0;
                double volDown = (lo - 1 >= 0)           ? sorted[lo - 1].Value : 0;
                if (volUp == 0 && volDown == 0) break;
                if (volUp >= volDown) accumulated += sorted[++hi].Value;
                else                 accumulated += sorted[--lo].Value;
            }

            vah = sorted[hi].Key;
            val = sorted[lo].Key;
        }

        // =========================================================
        // PROBABILIDADES — V3 (AMT-aware, tipo de nivel exacto)
        // =========================================================

        private void RecalcProbabilities()
        {
            // ---------------------------------------------------------
            // V3 — Cada nivel llama a CalcProb con su tipo exacto.
            //
            // Esto garantiza que yPOC solo se compare contra yPOC
            // histórico, yVAH contra yVAH, etc.
            // Además se filtra por el mismo contexto de apertura AMT
            // (AboveVA / InsideVA / BelowVA) para que la estadística
            // sea homogénea: días similares contra días similares.
            // ---------------------------------------------------------
            probYHigh  = CalcProb(snapYHigh,   snapOpen, EduS_LevelType.YHigh,   out nYHigh);
            probYLow   = CalcProb(snapYLow,    snapOpen, EduS_LevelType.YLow,    out nYLow);
            probPCierre= CalcProb(snapPCierre, snapOpen, EduS_LevelType.PCierre, out nPCierre);
            probPreH   = CalcProb(snapPreH,    snapOpen, EduS_LevelType.PreHigh, out nPreH);
            probPreL   = CalcProb(snapPreL,    snapOpen, EduS_LevelType.PreLow,  out nPreL);
            probYPOC   = CalcProb(snapYPOC,    snapOpen, EduS_LevelType.YPOC,    out nYPOC);
            probYVAH   = CalcProb(snapYVAH,    snapOpen, EduS_LevelType.YVAH,    out nYVAH);
            probYVAL   = CalcProb(snapYVAL,    snapOpen, EduS_LevelType.YVAL,    out nYVAL);
            probOnPOC  = CalcProb(snapOnPOC,   snapOpen, EduS_LevelType.OnPOC,   out nOnPOC);
            probOnVAH  = CalcProb(snapOnVAH,   snapOpen, EduS_LevelType.OnVAH,   out nOnVAH);
            probOnVAL  = CalcProb(snapOnVAL,   snapOpen, EduS_LevelType.OnVAL,   out nOnVAL);
        }

        private void ResetProbabilities()
        {
            probYHigh = probYLow = probPCierre = probPreH = probPreL =
            probYPOC  = probYVAH = probYVAL =
            probOnPOC = probOnVAH = probOnVAL = double.NaN;
            nYHigh = nYLow = nPCierre = nPreH = nPreL =
            nYPOC  = nYVAH = nYVAL =
            nOnPOC = nOnVAH = nOnVAL = 0;
        }

        // ---------------------------------------------------------
        // CalcProb — V3
        //
        // Parámetros nuevos respecto a V2:
        //   levelType → tipo exacto del nivel (YPOC, YVAH, YHigh, etc.)
        //               Así no buscamos "el más cercano" sino el mismo
        //               tipo en cada sesión histórica.
        //
        // Filtros aplicados en orden:
        //   1. El nivel equivalente existe en la sesión histórica (no NaN)
        //   2. Mismo lado del Open (arriba/abajo) — igual que V2
        //   3. Misma banda de distancia en ticks — igual que V2
        //   4. NUEVO: Mismo contexto de apertura AMT (AboveVA/InsideVA/BelowVA)
        //             Si el contexto actual es Unknown, se omite este filtro
        //             para no perder todas las sesiones antiguas del historial.
        //
        // Resultado:
        //   touchCount / condCount  si condCount >= MinSessions
        //   double.NaN              si hay pocas sesiones comparables
        // ---------------------------------------------------------
        private double CalcProb(double levelNow, double openNow,
                                EduS_LevelType levelType, out int condCount)
        {
            condCount = 0;
            if (double.IsNaN(levelNow) || double.IsNaN(openNow) || tickSz <= 0)
                return double.NaN;

            bool   levelAbove = levelNow > openNow;
            double distNow    = Math.Abs(levelNow - openNow) / tickSz;
            int    touchCount = 0;

            foreach (var s in sessionHistory)
            {
                // Datos mínimos necesarios
                if (double.IsNaN(s.Open) ||
                    double.IsNaN(s.SessionHigh) ||
                    double.IsNaN(s.SessionLow)) continue;

                // -------------------------------------------------
                // Filtro 1 — Obtener el nivel del MISMO TIPO en la
                // sesión histórica (búsqueda exacta, no por distancia)
                // -------------------------------------------------
                double levelThen = GetLevelByType(s, levelType);
                if (double.IsNaN(levelThen)) continue;

                // -------------------------------------------------
                // Filtro 2 — Mismo lado del Open (dirección)
                // Un nivel alcista en el historial no es comparable
                // a un nivel bajista de hoy aunque estén a la misma
                // distancia — el contexto de subasta es opuesto.
                // -------------------------------------------------
                if ((levelThen > s.Open) != levelAbove) continue;

                // -------------------------------------------------
                // Filtro 3 — Banda de distancia en ticks
                // Solo sesiones donde el nivel estaba a una distancia
                // similar al Open, para comparar situaciones análogas.
                // -------------------------------------------------
                double distThen = Math.Abs(levelThen - s.Open) / tickSz;
                if (Math.Abs(distThen - distNow) > DistanceBandTicks) continue;

                // -------------------------------------------------
                // Filtro 4 — Contexto de apertura AMT (V3 NUEVO)
                //
                // Solo incluir sesiones con el mismo contexto de
                // apertura que hoy. La probabilidad de tocar yPOC
                // es significativamente diferente si el Open está
                // dentro del VA vs fuera de él.
                //
                // Solo se aplica si FiltrarPorContextoAMT = true.
                // Si el contexto actual es Unknown (sin datos de VA)
                // se omite para no excluir todo el historial.
                // -------------------------------------------------
                if (FiltrarPorContextoAMT                          &&
                    currentOpenContext != EduS_OpenContext.Unknown  &&
                    s.OpenContext      != EduS_OpenContext.Unknown  &&
                    s.OpenContext      != currentOpenContext) continue;

                // La sesión pasó todos los filtros — contar
                condCount++;
                if (levelThen >= s.SessionLow  - tickSz * 0.5 &&
                    levelThen <= s.SessionHigh + tickSz * 0.5)
                    touchCount++;
            }

            return (condCount >= MinSessions)
                ? (double)touchCount / condCount
                : double.NaN;
        }

        // ---------------------------------------------------------
        // GetLevelByType — V3 (reemplaza GetEquivLevel)
        //
        // Antes: buscaba el nivel más cercano en distancia entre
        //        todos los candidatos → contaminación estadística.
        //
        // Ahora: retorna directamente el nivel del tipo solicitado
        //        de la sesión histórica. Si ese tipo no existe en
        //        esa sesión (NaN), retorna NaN y la sesión se ignora.
        //
        // Esto es el cambio más importante de V3:
        //   yPOC hoy  →  compara contra s.YPOC  (no contra lo que sea)
        //   yVAH hoy  →  compara contra s.YVAH
        //   onPOC hoy →  compara contra s.OnPOC
        //   etc.
        // ---------------------------------------------------------
        private double GetLevelByType(EduS_UMC_SesionRecord s, EduS_LevelType t)
        {
            switch (t)
            {
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

        // =========================================================
        // SetAllPlots
        // =========================================================
        private void SetAllPlots()
        {
            Values[0][0]  = pApertura;
            Values[1][0]  = yHigh;
            Values[2][0]  = preHigh;
            Values[3][0]  = histMax;
            Values[4][0]  = yLow;
            Values[5][0]  = preLow;
            Values[6][0]  = pCierre;
            Values[7][0]  = orb30High;
            Values[8][0]  = orb30Low;
            Values[9][0]  = orb30Mid;
            Values[10][0] = yPOC;
            Values[11][0] = yVAH;
            Values[12][0] = yVAL;
            Values[13][0] = onPOC;
            Values[14][0] = onVAH;
            Values[15][0] = onVAL;
        }

        // =========================================================
        // CheckAndAddEvent (original sin cambios)
        // =========================================================
        private void CheckAndAddEvent(string eventId, TimeSpan targetTime, string label,
                                      string dateStr, TimeSpan currentEtTime, DateTime barTime)
        {
            string uniqueKey = eventId + "_" + dateStr;
            if (processedEventsToday.Contains(uniqueKey)) return;

            TimeSpan tolerance = TimeSpan.FromMinutes(5);
            if (currentEtTime >= targetTime && currentEtTime < targetTime.Add(tolerance))
            {
                double anchor = (GlobalTimeTextPosition == EduS_TextPos.Arriba) ? High[0] : Low[0];
                globalEvents.Add(new EduS_TimeEvent { Time = barTime, Label = label, AnchorPrice = anchor });
                processedEventsToday.Add(uniqueKey);
            }
        }

        // =========================================================
        // OnRender — motor DirectX completo
        // =========================================================
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (ChartBars == null || chartControl == null || chartScale == null) return;

            // ── 1. Horarios Globales (original sin cambios) ───────────
            if (ShowGlobalTimes && globalEvents != null && globalEvents.Count > 0)
            {
                if (cachedTimeBrush == null || cachedTimeBrush.IsDisposed)
                {
                    var dxBrush = GlobalTimeColor.ToDxBrush(RenderTarget);
                    dxBrush.Opacity = (float)(GlobalTimeOpacity / 100.0);
                    cachedTimeBrush = dxBrush;
                }

                TextFormat timeTextFormat = new TextFormat(
                    NinjaTrader.Core.Globals.DirectWriteFactory,
                    "Arial", SharpDX.DirectWrite.FontWeight.Bold,
                    SharpDX.DirectWrite.FontStyle.Normal, (float)GlobalTimeFontSize);

                float yTop    = (float)ChartPanel.Y;
                float yBottom = (float)(ChartPanel.Y + ChartPanel.H);

                foreach (var evt in globalEvents)
                {
                    int idx = ChartBars.GetBarIdxByTime(chartControl, evt.Time);
                    if (idx < ChartBars.FromIndex || idx > ChartBars.ToIndex) continue;
                    float x = (float)chartControl.GetXByBarIndex(ChartBars, idx);

                    RenderTarget.DrawLine(new Vector2(x, yTop), new Vector2(x, yBottom),
                        cachedTimeBrush, GlobalTimeWidth);

                    string txt = evt.Label;
                    using (var layout = new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory,
                        txt, timeTextFormat, 300f, timeTextFormat.FontSize))
                    {
                        double priceBase  = evt.AnchorPrice;
                        double finalPrice = (GlobalTimeTextPosition == EduS_TextPos.Arriba)
                            ? priceBase + (GlobalTimeTextOffsetPoints * TickSize)
                            : priceBase - (GlobalTimeTextOffsetPoints * TickSize);

                        float yPrice = (float)chartScale.GetYByValue(finalPrice);
                        if (GlobalTimeTextPosition == EduS_TextPos.Abajo &&
                            GlobalTimeTextOrientation == EduS_TextOrient.Horizontal)
                            yPrice -= layout.Metrics.Height;

                        float txtX = x + 3, txtY = yPrice;

                        if (GlobalTimeTextOrientation == EduS_TextOrient.Horizontal)
                        {
                            RenderTarget.DrawTextLayout(new Vector2(txtX, txtY), layout, cachedTimeBrush);
                        }
                        else
                        {
                            var oldTransform = RenderTarget.Transform;
                            float pivotY = txtY + (GlobalTimeTextPosition == EduS_TextPos.Abajo ? 2 : -2);
                            RenderTarget.Transform = Matrix3x2.Rotation((float)(-Math.PI / 2),
                                new Vector2(x, pivotY)) * oldTransform;
                            if (GlobalTimeTextPosition == EduS_TextPos.Arriba)
                                RenderTarget.DrawTextLayout(new Vector2(x, pivotY), layout, cachedTimeBrush);
                            else
                                RenderTarget.DrawTextLayout(new Vector2(x - layout.Metrics.Width, pivotY),
                                    layout, cachedTimeBrush);
                            RenderTarget.Transform = oldTransform;
                        }
                    }
                }
                timeTextFormat.Dispose();
            }

            // ── 2. Niveles estructurales + Market Profile ─────────────
            // (valor, nombre, plotIdx, prob, n, visible)
            var levels = new (double val, string name, int pi, double prob, int n, bool vis)[]
            {
                // Clásicos
                (pApertura, "Open RTH", 0, double.NaN,  0,       true),
                (yHigh,     "Y-High",   1, probYHigh,   nYHigh,  true),
                (preHigh,   "Pre-High", 2, probPreH,    nPreH,   true),
                (histMax,   "HistMax",  3, double.NaN,  0,       true),
                (yLow,      "Y-Low",    4, probYLow,    nYLow,   true),
                (preLow,    "Pre-Low",  5, probPreL,    nPreL,   true),
                (pCierre,   "Y-Close",  6, probPCierre, nPCierre,true),
                // ORB
                (orb30High, "ORB H",    7, double.NaN,  0,       true),
                (orb30Low,  "ORB L",    8, double.NaN,  0,       true),
                (orb30Mid,  "ORB Mid",  9, double.NaN,  0,       true),
                // RTH Ayer — Market Profile
                (yPOC, "yPOC", 10, probYPOC, nYPOC, MostrarPerfilRTHAyer),
                (yVAH, "yVAH", 11, probYVAH, nYVAH, MostrarPerfilRTHAyer),
                (yVAL, "yVAL", 12, probYVAL, nYVAL, MostrarPerfilRTHAyer),
                // Overnight — Market Profile
                (onPOC, "onPOC", 13, probOnPOC, nOnPOC, MostrarPerfilON),
                (onVAH, "onVAH", 14, probOnVAH, nOnVAH, MostrarPerfilON),
                (onVAL, "onVAL", 15, probOnVAL, nOnVAL, MostrarPerfilON),
            };

            float xLeft      = (float)chartControl.CanvasLeft + 2f;
            float xRight     = (float)chartControl.CanvasRight;
            float panelRight = (float)(ChartPanel.X + ChartPanel.W);

            // X de la última barra visible — la línea termina aquí y el badge arranca aquí
            int   lastBarIdx  = ChartBars.ToIndex;
            float xLastBar    = (float)chartControl.GetXByBarIndex(ChartBars, lastBarIdx);
            // Pequeño gap visual entre la última barra y el badge (medio ancho de barra aprox.)
            float xBadgeStart = xLastBar + RightLabelPadding; // (float)(chartControl.BarWidth * 0.5) + 10f;

            // ── Formatos de texto ──────────────────────────────────────
            var badgeFormat = new TextFormat(
                NinjaTrader.Core.Globals.DirectWriteFactory,
                "Arial", SharpDX.DirectWrite.FontWeight.Bold,
                SharpDX.DirectWrite.FontStyle.Normal, (float)BadgeFontSize);

            float badgeBgAlpha = BadgeBgOpacity / 100f;

            using (var darkTextBrush = new SharpDX.Direct2D1.SolidColorBrush(
                RenderTarget, new SharpDX.Color4(0.1f, 0.1f, 0.1f, 1f)))
            {
                // ── Paso 1: medir todos los badges ────────────────────
                float padX = 6f;
                float padY = 3f;

                var badgeItems = new System.Collections.Generic.List<
                    (float yCenter, float badgeW, float badgeH, string text, int pi, bool touched)>();

                for (int i = 0; i < levels.Length; i++)
                {
                    var lv = levels[i];
                    if (!lv.vis || double.IsNaN(lv.val) || Math.Abs(lv.val) < double.Epsilon) continue;

                    float y    = (float)chartScale.GetYByValue(lv.val);
                    var   plot = Plots[lv.pi];

                    // Línea horizontal  (Vector2(xLastBar, y),)
                    RenderTarget.DrawLine(new Vector2(xLeft, y), new Vector2(xBadgeStart, y),
                        plot.BrushDX, plot.Width, plot.StrokeStyle);

                    if (!MostrarBadge) continue;

                    bool   touched   = LevelTouched(lv.val);
                    string fmt       = lv.val.ToString("N" + Decimals, labelCulture ?? CultureInfo.InvariantCulture);
                    string check     = touched ? " ✓" : "";
                    string badgeText = (MostrarProbabilidad && !double.IsNaN(lv.prob))
                        ? $"{lv.name}  {fmt}  {(int)Math.Round(lv.prob * 100)}%{check}"
                        : $"{lv.name}  {fmt}{check}";

                    using (var lay = new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory,
                        badgeText, badgeFormat, 500f, badgeFormat.FontSize))
                    {
                        float bw = lay.Metrics.Width  + padX * 2f;
                        float bh = lay.Metrics.Height + padY * 2f;
                        badgeItems.Add((y, bw, bh, badgeText, lv.pi, touched));
                    }
                }

                // ── Paso 2: asignar columnas para evitar solapamiento ─
                // Cada badge arranca en xBadgeStart + col * (maxBadgeW + gap)
                float colGap = 4f;
                // Calcular ancho máximo de badge para definir paso de columna
                float maxBW = 0f;
                foreach (var b in badgeItems) if (b.badgeW > maxBW) maxBW = b.badgeW;
                float colStep = maxBW + colGap;

                // Para cada badge, buscar la columna más a la izquierda que no colisione
                var assigned = new System.Collections.Generic.List<(int col, SharpDX.RectangleF rect)>();

                var finalBadges = new System.Collections.Generic.List<
                    (float xBadge, float yBadge, float badgeW, float badgeH, string text, int pi)>();

                foreach (var b in badgeItems)
                {
                    float yTop = b.yCenter - b.badgeH / 2f;

                    // Probar columnas 0, 1, 2... hasta encontrar una libre
                    int col = 0;
                    while (true)
                    {
                        float xTry = xBadgeStart + col * colStep;
                        var   rect = new SharpDX.RectangleF(xTry, yTop, b.badgeW, b.badgeH);

                        bool collision = false;
                        foreach (var a in assigned)
                        {
                            if (a.col == col)
                            {
                                // Solapan verticalmente?
                                float aBottom = a.rect.Top + a.rect.Height;
                                float bBottom = rect.Top   + rect.Height;
                                if (rect.Top < aBottom + 1f && bBottom > a.rect.Top - 1f)
                                { collision = true; break; }
                            }
                        }
                        if (!collision)
                        {
                            assigned.Add((col, rect));
                            finalBadges.Add((xTry, yTop, b.badgeW, b.badgeH, b.text, b.pi));
                            break;
                        }
                        col++;
                    }
                }

                // ── Paso 3: dibujar los badges en su posición final ───
                foreach (var fb in finalBadges)
                {
                    var plot   = Plots[fb.pi];
                    var bgRect = new SharpDX.RectangleF(fb.xBadge, fb.yBadge, fb.badgeW, fb.badgeH);

                    plot.BrushDX.Opacity = badgeBgAlpha;
                    RenderTarget.FillRectangle(bgRect, plot.BrushDX);
                    plot.BrushDX.Opacity = 1f;
                    RenderTarget.DrawRectangle(bgRect, plot.BrushDX, 1f);

                    using (var lay = new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory,
                        fb.text, badgeFormat, 500f, badgeFormat.FontSize))
                    {
                        RenderTarget.DrawTextLayout(
                            new Vector2(fb.xBadge + padX, fb.yBadge + padY),
                            lay, darkTextBrush);
                    }
                }
            }
            badgeFormat.Dispose();

            // ── 3. Tabla resumen — esquina superior derecha ───────────
            if (MostrarTabla)
            {
                // Recoger niveles válidos con su info
                var tablaRows = new System.Collections.Generic.List<
                    (string name, double val, double prob, int n, int pi, bool vis)>();
                foreach (var lv in levels)
                {
                    if (!lv.vis || double.IsNaN(lv.val) || Math.Abs(lv.val) < double.Epsilon) continue;
                    tablaRows.Add((lv.name, lv.val, lv.prob, lv.n, lv.pi, lv.vis));
                }

                if (tablaRows.Count > 0)
                {
                    // V3 — Texto de contexto AMT para mostrar en cabecera de tabla
                    string ctxLabel;
                    switch (currentOpenContext)
                    {
                        case EduS_OpenContext.AboveVA:  ctxLabel = "Open > VA ↑"; break;
                        case EduS_OpenContext.InsideVA: ctxLabel = "Open en VA ↔"; break;
                        case EduS_OpenContext.BelowVA:  ctxLabel = "Open < VA ↓"; break;
                        default:                        ctxLabel = "Contexto: -"; break;
                    }

                    var tablaFormat = new TextFormat(
                        NinjaTrader.Core.Globals.DirectWriteFactory,
                        "Arial", SharpDX.DirectWrite.FontWeight.Normal,
                        SharpDX.DirectWrite.FontStyle.Normal, (float)TablaFontSize);
                    var tablaBoldFormat = new TextFormat(
                        NinjaTrader.Core.Globals.DirectWriteFactory,
                        "Arial", SharpDX.DirectWrite.FontWeight.Bold,
                        SharpDX.DirectWrite.FontStyle.Normal, (float)TablaFontSize);

                    float rowH       = (float)TablaFontSize + 5f;
                    float colName    = 68f;
                    float colPrice   = 60f;
                    float colPct     = 40f;
                    float padX       = 8f;
                    float padY       = 6f;
                    float tableW     = padX * 2f + colName + colPrice + colPct;
                    // V3 — fila extra en cabecera para mostrar contexto AMT
                    float headerH    = rowH * 2f + 2f;
                    float tableH     = padY * 2f + headerH + tablaRows.Count * rowH;

                    // Esquina superior derecha
                    float tX = panelRight - tableW - 10f;
                    float tY = (float)ChartPanel.Y + 10f;

                    float tablaBgAlpha = TablaBgOpacity / 100f;

                    using (var tablaBg = new SharpDX.Direct2D1.SolidColorBrush(
                        RenderTarget, new SharpDX.Color4(0f, 0f, 0f, tablaBgAlpha)))
                    using (var whiteT = new SharpDX.Direct2D1.SolidColorBrush(
                        RenderTarget, new SharpDX.Color4(1f, 1f, 1f, 0.95f)))
                    using (var grayT = new SharpDX.Direct2D1.SolidColorBrush(
                        RenderTarget, new SharpDX.Color4(0.65f, 0.65f, 0.65f, 1f)))
                    using (var headerBg = new SharpDX.Direct2D1.SolidColorBrush(
                        RenderTarget, new SharpDX.Color4(0.1f, 0.1f, 0.1f, 0.9f)))
                    using (var borderBrush = new SharpDX.Direct2D1.SolidColorBrush(
                        RenderTarget, new SharpDX.Color4(0.4f, 0.4f, 0.4f, 1f)))
                    {
                        // Fondo panel
                        var tableRect = new SharpDX.RectangleF(tX, tY, tableW, tableH);
                        RenderTarget.FillRectangle(tableRect, tablaBg);
                        RenderTarget.DrawRectangle(tableRect, borderBrush, 1f);

                        // Cabecera
                        var hRect = new SharpDX.RectangleF(tX, tY, tableW, padY + headerH);
                        RenderTarget.FillRectangle(hRect, headerBg);

                        void DrawCell(string txt, float cx, float cy, float cw,
                                      SharpDX.Direct2D1.Brush brush, TextFormat fmt,
                                      bool rightAlign = false)
                        {
                            using (var lay = new TextLayout(
                                NinjaTrader.Core.Globals.DirectWriteFactory, txt, fmt, cw, rowH))
                            {
                                float dx = rightAlign ? cx + cw - lay.Metrics.Width : cx;
                                RenderTarget.DrawTextLayout(new Vector2(dx, cy), lay, brush);
                            }
                        }

                        float hRow = tY + padY;
                        DrawCell("Nivel",  tX + padX,                    hRow, colName,  whiteT, tablaBoldFormat);
                        DrawCell("Precio", tX + padX + colName,           hRow, colPrice, whiteT, tablaBoldFormat, true);
                        DrawCell("%",      tX + padX + colName + colPrice, hRow, colPct,  whiteT, tablaBoldFormat, true);

                        // V3 — Segunda fila de cabecera: contexto AMT
                        // Colorea según el tipo: verde=AboveVA, amarillo=InsideVA, rojo=BelowVA
                        float ctxRowY = hRow + rowH;
                        SharpDX.Color4 ctxColor;
                        switch (currentOpenContext)
                        {
                            case EduS_OpenContext.AboveVA:  ctxColor = new SharpDX.Color4(0.2f, 0.9f, 0.3f, 1f); break;
                            case EduS_OpenContext.BelowVA:  ctxColor = new SharpDX.Color4(0.9f, 0.3f, 0.2f, 1f); break;
                            case EduS_OpenContext.InsideVA: ctxColor = new SharpDX.Color4(1f,   0.85f,0.1f, 1f); break;
                            default:                        ctxColor = new SharpDX.Color4(0.6f, 0.6f, 0.6f, 1f); break;
                        }
                        using (var ctxBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, ctxColor))
                        {
                            DrawCell(ctxLabel, tX + padX, ctxRowY, tableW - padX * 2f, ctxBrush, tablaBoldFormat);
                        }

                        // Línea separadora debajo de toda la cabecera (V3: doble fila)
                        float sepY = tY + padY + headerH;
                        RenderTarget.DrawLine(
                            new Vector2(tX + 2f, sepY),
                            new Vector2(tX + tableW - 2f, sepY),
                            borderBrush, 1f);

                        // Filas de datos
                        for (int i = 0; i < tablaRows.Count; i++)
                        {
                            var (name, val, prob, n, pi, _) = tablaRows[i];
                            float ry = sepY + i * rowH + 2f;

                            // Indicador de color del nivel (banda izquierda)
                            RenderTarget.DrawLine(
                                new Vector2(tX + 2f, ry),
                                new Vector2(tX + 2f, ry + rowH - 2f),
                                Plots[pi].BrushDX, 2.5f);

                            // Nombre
                            DrawCell(name, tX + padX, ry, colName, whiteT, tablaFormat);

                            // Precio
                            string priceStr = val.ToString("N" + Decimals, labelCulture ?? CultureInfo.InvariantCulture);
                            DrawCell(priceStr, tX + padX + colName, ry, colPrice, grayT, tablaFormat, true);

                            // Probabilidad
                            string pctStr;
                            SharpDX.Direct2D1.Brush pctBrush;
                            bool rowTouched = LevelTouched(val);
                            if (MostrarProbabilidad && !double.IsNaN(prob) && n >= MinSessions)
                            {
                                int pct = (int)Math.Round(prob * 100);
                                pctStr = rowTouched ? $"{pct}% ✓" : $"{pct}%";
                                float r = pct >= 60 ? 0.2f : pct >= 40 ? 1f : 0.9f;
                                float g = pct >= 60 ? 0.85f : pct >= 40 ? 0.85f : 0.25f;
                                float b = 0.2f;
                                pctBrush = new SharpDX.Direct2D1.SolidColorBrush(
                                    RenderTarget, new SharpDX.Color4(r, g, b, 1f));
                            }
                            else
                            {
                                pctStr   = rowTouched ? "✓" : "-";
                                pctBrush = rowTouched
                                    ? new SharpDX.Direct2D1.SolidColorBrush(
                                        RenderTarget, new SharpDX.Color4(0.2f, 0.85f, 0.2f, 1f))
                                    : new SharpDX.Direct2D1.SolidColorBrush(
                                        RenderTarget, new SharpDX.Color4(0.45f, 0.45f, 0.45f, 1f));
                            }
                            DrawCell(pctStr, tX + padX + colName + colPrice, ry, colPct, pctBrush, tablaBoldFormat, true);
                            pctBrush.Dispose();
                        }
                    }
                    tablaFormat.Dispose();
                    tablaBoldFormat.Dispose();
                }
            }
        }

        public override void OnRenderTargetChanged()
        {
            if (cachedTimeBrush != null) { cachedTimeBrush.Dispose(); cachedTimeBrush = null; }
        }

        // =========================================================
        // PROPIEDADES
        // =========================================================
        #region Properties

        // 1. Horarios
        [NinjaScriptProperty]
        [Display(Name = "Inicio RTH (HHmmss)", GroupName = "1. Horarios", Order = 0)]
        public int RthOpenTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cierre RTH (HHmmss)", GroupName = "1. Horarios", Order = 1)]
        public int RthCloseTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "GlobexStartTime (HHmmss) — inicio ON",
                 Description = "Inicio sesión Overnight. Default 180000 = 18:00 (ES/NQ).",
                 GroupName = "1. Horarios", Order = 2)]
        public int GlobexStartTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Segundos ORB", Description = "Duración para cálculo inicial (ej: 30)",
                 GroupName = "1. Horarios", Order = 3)]
        public int OrbSeconds { get; set; }

        // 2. Visual
        [NinjaScriptProperty]
        [Display(Name = "Cultura (Formato)", GroupName = "2. Visual", Order = 3)]
        public string CultureName { get; set; }

        [NinjaScriptProperty]
        [Range(0, 8)]
        [Display(Name = "Decimales", GroupName = "2. Visual", Order = 4)]
        public int Decimals { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Grosor Línea", GroupName = "2. Visual", Order = 5)]
        public int LineWidth { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Margen Etiqueta (Der)", GroupName = "2. Visual", Order = 6)]
        public int RightLabelPadding { get; set; }

        [NinjaScriptProperty]
        [Range(-50, 50)]
        [Display(Name = "Altura Texto (Offset)", GroupName = "2. Visual", Order = 7)]
        public int LabelAbove { get; set; }

        // 3. Niveles Históricos
        [NinjaScriptProperty]
        [Display(Name = "Modo Max Histórico",
                 Description = "Dinamico: basado en velas cargadas\nManual_Fijo: usa el valor ingresado abajo",
                 GroupName = "3. Niveles Históricos", Order = 0)]
        public EduS_HistMode HistMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Precio Max Manual", Description = "Valor fijo si Modo = Manual_Fijo",
                 GroupName = "3. Niveles Históricos", Order = 1)]
        public double ManualHistMax { get; set; }

        // 4. Horarios Globales
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Horarios Globales",
                 Description = "Activa líneas para Eurex, Londres y Asia",
                 Order = 1, GroupName = "4. Horarios Globales")]
        public bool ShowGlobalTimes { get; set; }

        [NinjaScriptProperty]
        [XmlIgnore]
        [Display(Name = "Color Líneas Globales", Order = 2, GroupName = "4. Horarios Globales")]
        public Brush GlobalTimeColor { get; set; }
        [Browsable(false)]
        public string GlobalTimeColorSerializable
        { get { return Serialize.BrushToString(GlobalTimeColor); } set { GlobalTimeColor = Serialize.StringToBrush(value); } }

        [NinjaScriptProperty]
        [Display(Name = "Estilo de Línea", Order = 3, GroupName = "4. Horarios Globales")]
        public DashStyleHelper GlobalTimeStyle { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Grosor de Línea", Order = 4, GroupName = "4. Horarios Globales")]
        public int GlobalTimeWidth { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Opacidad (%)", Order = 5, GroupName = "4. Horarios Globales")]
        public int GlobalTimeOpacity { get; set; }

        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "Tamaño Fuente", Order = 6, GroupName = "4. Horarios Globales")]
        public int GlobalTimeFontSize { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Orientación Texto", Order = 7, GroupName = "4. Horarios Globales")]
        public EduS_TextOrient GlobalTimeTextOrientation { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Posición Texto", Order = 8, GroupName = "4. Horarios Globales")]
        public EduS_TextPos GlobalTimeTextPosition { get; set; }

        [NinjaScriptProperty]
        [Range(0, 500)]
        [Display(Name = "Offset (Puntos)", Order = 9, GroupName = "4. Horarios Globales")]
        public double GlobalTimeTextOffsetPoints { get; set; }

        // 5. Market Profile
        [NinjaScriptProperty]
        [Range(50, 95)]
        [Display(Name = "Value Area % (estándar = 70)",
                 Description = "Porcentaje del volumen total que cubre el Value Area (VAH−VAL).",
                 GroupName = "5. Market Profile", Order = 0)]
        public int ValueAreaPercent { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Perfil RTH Ayer (yPOC / yVAH / yVAL)",
                 GroupName = "5. Market Profile", Order = 1)]
        public bool MostrarPerfilRTHAyer { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Perfil Overnight (onPOC / onVAH / onVAL)",
                 GroupName = "5. Market Profile", Order = 2)]
        public bool MostrarPerfilON { get; set; }

        // 6. Probabilidades
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Probabilidad en Etiqueta",
                 GroupName = "6. Probabilidad", Order = 0)]
        public bool MostrarProbabilidad { get; set; }

        [NinjaScriptProperty]
        [Range(5, 500)]
        [Display(Name = "Mínimo de Sesiones (n mínimo)",
                 Description = "Sesiones mínimas para mostrar el %.",
                 GroupName = "6. Probabilidad", Order = 1)]
        public int MinSessions { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "Banda de Distancia (ticks)",
                 Description = "Tolerancia en ticks para comparar distancias nivel-OPEN entre sesiones.",
                 GroupName = "6. Probabilidad", Order = 2)]
        public int DistanceBandTicks { get; set; }

        // ---------------------------------------------------------
        // V3 — Filtro por contexto AMT (Teoría de Subasta)
        //
        // Cuando está activo, las probabilidades solo se calculan
        // comparando sesiones con el mismo contexto de apertura:
        //   · Open > VA de ayer  (iniciativa alcista)
        //   · Open dentro del VA (rotación / equilibrio)
        //   · Open < VA de ayer  (iniciativa bajista)
        //
        // Resultado: estadísticas más homogéneas y representativas.
        // Contra: requiere más sesiones históricas para tener
        //         n >= MinSessions en cada contexto por separado.
        //
        // Recomendación: activar con al menos 200 sesiones cargadas.
        // ---------------------------------------------------------
        [NinjaScriptProperty]
        [Display(Name = "Filtrar por Contexto AMT",
                 Description = "V3: Solo compara sesiones con el mismo contexto de apertura respecto al VA de ayer (AboveVA / InsideVA / BelowVA). Estadísticas más puras pero requiere más historial.",
                 GroupName = "6. Probabilidad", Order = 3)]
        public bool FiltrarPorContextoAMT { get; set; }

        // 7. Visual Badge / Tabla
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Etiquetas Badge",
                 Description = "Muestra etiquetas con fondo de color junto a cada nivel.",
                 GroupName = "7. Visual Badge / Tabla", Order = 0)]
        public bool MostrarBadge { get; set; }

        [NinjaScriptProperty]
        [Range(5, 30)]
        [Display(Name = "Tamaño Fuente Badge",
                 GroupName = "7. Visual Badge / Tabla", Order = 1)]
        public int BadgeFontSize { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Opacidad Fondo Badge (%)",
                 Description = "0 = transparente, 100 = sólido.",
                 GroupName = "7. Visual Badge / Tabla", Order = 2)]
        public int BadgeBgOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Tabla Superior Derecha",
                 Description = "Muestra tabla resumen con todos los niveles y probabilidades.",
                 GroupName = "7. Visual Badge / Tabla", Order = 3)]
        public bool MostrarTabla { get; set; }

        [NinjaScriptProperty]
        [Range(5, 30)]
        [Display(Name = "Tamaño Fuente Tabla",
                 GroupName = "7. Visual Badge / Tabla", Order = 4)]
        public int TablaFontSize { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Opacidad Fondo Tabla (%)",
                 GroupName = "7. Visual Badge / Tabla", Order = 5)]
        public int TablaBgOpacity { get; set; }

        // Series públicas
        [Browsable(false)] [XmlIgnore] public Series<double> PApertura => Values[0];
        [Browsable(false)] [XmlIgnore] public Series<double> YHigh     => Values[1];
        [Browsable(false)] [XmlIgnore] public Series<double> PreHigh   => Values[2];
        [Browsable(false)] [XmlIgnore] public Series<double> HistMax   => Values[3];
        [Browsable(false)] [XmlIgnore] public Series<double> YLow      => Values[4];
        [Browsable(false)] [XmlIgnore] public Series<double> PreLow    => Values[5];
        [Browsable(false)] [XmlIgnore] public Series<double> PCierre   => Values[6];
        [Browsable(false)] [XmlIgnore] public Series<double> OrbHigh   => Values[7];
        [Browsable(false)] [XmlIgnore] public Series<double> OrbLow    => Values[8];
        [Browsable(false)] [XmlIgnore] public Series<double> OrbMid    => Values[9];
        [Browsable(false)] [XmlIgnore] public Series<double> YPOC      => Values[10];
        [Browsable(false)] [XmlIgnore] public Series<double> YVAH      => Values[11];
        [Browsable(false)] [XmlIgnore] public Series<double> YVAL      => Values[12];
        [Browsable(false)] [XmlIgnore] public Series<double> OnPOC     => Values[13];
        [Browsable(false)] [XmlIgnore] public Series<double> OnVAH     => Values[14];
        [Browsable(false)] [XmlIgnore] public Series<double> OnVAL     => Values[15];

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduS_Unified_Market_Context[] cacheEduS_Unified_Market_Context;
		public EduS_Trader.EduS_Unified_Market_Context EduS_Unified_Market_Context(int rthOpenTime, int rthCloseTime, int globexStartTime, int orbSeconds, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, EduS_HistMode histMode, double manualHistMax, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints, int valueAreaPercent, bool mostrarPerfilRTHAyer, bool mostrarPerfilON, bool mostrarProbabilidad, int minSessions, int distanceBandTicks, bool filtrarPorContextoAMT, bool mostrarBadge, int badgeFontSize, int badgeBgOpacity, bool mostrarTabla, int tablaFontSize, int tablaBgOpacity)
		{
			return EduS_Unified_Market_Context(Input, rthOpenTime, rthCloseTime, globexStartTime, orbSeconds, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, histMode, manualHistMax, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints, valueAreaPercent, mostrarPerfilRTHAyer, mostrarPerfilON, mostrarProbabilidad, minSessions, distanceBandTicks, filtrarPorContextoAMT, mostrarBadge, badgeFontSize, badgeBgOpacity, mostrarTabla, tablaFontSize, tablaBgOpacity);
		}

		public EduS_Trader.EduS_Unified_Market_Context EduS_Unified_Market_Context(ISeries<double> input, int rthOpenTime, int rthCloseTime, int globexStartTime, int orbSeconds, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, EduS_HistMode histMode, double manualHistMax, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints, int valueAreaPercent, bool mostrarPerfilRTHAyer, bool mostrarPerfilON, bool mostrarProbabilidad, int minSessions, int distanceBandTicks, bool filtrarPorContextoAMT, bool mostrarBadge, int badgeFontSize, int badgeBgOpacity, bool mostrarTabla, int tablaFontSize, int tablaBgOpacity)
		{
			if (cacheEduS_Unified_Market_Context != null)
				for (int idx = 0; idx < cacheEduS_Unified_Market_Context.Length; idx++)
					if (cacheEduS_Unified_Market_Context[idx] != null && cacheEduS_Unified_Market_Context[idx].RthOpenTime == rthOpenTime && cacheEduS_Unified_Market_Context[idx].RthCloseTime == rthCloseTime && cacheEduS_Unified_Market_Context[idx].GlobexStartTime == globexStartTime && cacheEduS_Unified_Market_Context[idx].OrbSeconds == orbSeconds && cacheEduS_Unified_Market_Context[idx].CultureName == cultureName && cacheEduS_Unified_Market_Context[idx].Decimals == decimals && cacheEduS_Unified_Market_Context[idx].LineWidth == lineWidth && cacheEduS_Unified_Market_Context[idx].RightLabelPadding == rightLabelPadding && cacheEduS_Unified_Market_Context[idx].LabelAbove == labelAbove && cacheEduS_Unified_Market_Context[idx].HistMode == histMode && cacheEduS_Unified_Market_Context[idx].ManualHistMax == manualHistMax && cacheEduS_Unified_Market_Context[idx].ShowGlobalTimes == showGlobalTimes && cacheEduS_Unified_Market_Context[idx].GlobalTimeColor == globalTimeColor && cacheEduS_Unified_Market_Context[idx].GlobalTimeStyle == globalTimeStyle && cacheEduS_Unified_Market_Context[idx].GlobalTimeWidth == globalTimeWidth && cacheEduS_Unified_Market_Context[idx].GlobalTimeOpacity == globalTimeOpacity && cacheEduS_Unified_Market_Context[idx].GlobalTimeFontSize == globalTimeFontSize && cacheEduS_Unified_Market_Context[idx].GlobalTimeTextOrientation == globalTimeTextOrientation && cacheEduS_Unified_Market_Context[idx].GlobalTimeTextPosition == globalTimeTextPosition && cacheEduS_Unified_Market_Context[idx].GlobalTimeTextOffsetPoints == globalTimeTextOffsetPoints && cacheEduS_Unified_Market_Context[idx].ValueAreaPercent == valueAreaPercent && cacheEduS_Unified_Market_Context[idx].MostrarPerfilRTHAyer == mostrarPerfilRTHAyer && cacheEduS_Unified_Market_Context[idx].MostrarPerfilON == mostrarPerfilON && cacheEduS_Unified_Market_Context[idx].MostrarProbabilidad == mostrarProbabilidad && cacheEduS_Unified_Market_Context[idx].MinSessions == minSessions && cacheEduS_Unified_Market_Context[idx].DistanceBandTicks == distanceBandTicks && cacheEduS_Unified_Market_Context[idx].FiltrarPorContextoAMT == filtrarPorContextoAMT && cacheEduS_Unified_Market_Context[idx].MostrarBadge == mostrarBadge && cacheEduS_Unified_Market_Context[idx].BadgeFontSize == badgeFontSize && cacheEduS_Unified_Market_Context[idx].BadgeBgOpacity == badgeBgOpacity && cacheEduS_Unified_Market_Context[idx].MostrarTabla == mostrarTabla && cacheEduS_Unified_Market_Context[idx].TablaFontSize == tablaFontSize && cacheEduS_Unified_Market_Context[idx].TablaBgOpacity == tablaBgOpacity && cacheEduS_Unified_Market_Context[idx].EqualsInput(input))
						return cacheEduS_Unified_Market_Context[idx];
			return CacheIndicator<EduS_Trader.EduS_Unified_Market_Context>(new EduS_Trader.EduS_Unified_Market_Context(){ RthOpenTime = rthOpenTime, RthCloseTime = rthCloseTime, GlobexStartTime = globexStartTime, OrbSeconds = orbSeconds, CultureName = cultureName, Decimals = decimals, LineWidth = lineWidth, RightLabelPadding = rightLabelPadding, LabelAbove = labelAbove, HistMode = histMode, ManualHistMax = manualHistMax, ShowGlobalTimes = showGlobalTimes, GlobalTimeColor = globalTimeColor, GlobalTimeStyle = globalTimeStyle, GlobalTimeWidth = globalTimeWidth, GlobalTimeOpacity = globalTimeOpacity, GlobalTimeFontSize = globalTimeFontSize, GlobalTimeTextOrientation = globalTimeTextOrientation, GlobalTimeTextPosition = globalTimeTextPosition, GlobalTimeTextOffsetPoints = globalTimeTextOffsetPoints, ValueAreaPercent = valueAreaPercent, MostrarPerfilRTHAyer = mostrarPerfilRTHAyer, MostrarPerfilON = mostrarPerfilON, MostrarProbabilidad = mostrarProbabilidad, MinSessions = minSessions, DistanceBandTicks = distanceBandTicks, FiltrarPorContextoAMT = filtrarPorContextoAMT, MostrarBadge = mostrarBadge, BadgeFontSize = badgeFontSize, BadgeBgOpacity = badgeBgOpacity, MostrarTabla = mostrarTabla, TablaFontSize = tablaFontSize, TablaBgOpacity = tablaBgOpacity }, input, ref cacheEduS_Unified_Market_Context);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduS_Unified_Market_Context EduS_Unified_Market_Context(int rthOpenTime, int rthCloseTime, int globexStartTime, int orbSeconds, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, EduS_HistMode histMode, double manualHistMax, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints, int valueAreaPercent, bool mostrarPerfilRTHAyer, bool mostrarPerfilON, bool mostrarProbabilidad, int minSessions, int distanceBandTicks, bool filtrarPorContextoAMT, bool mostrarBadge, int badgeFontSize, int badgeBgOpacity, bool mostrarTabla, int tablaFontSize, int tablaBgOpacity)
		{
			return indicator.EduS_Unified_Market_Context(Input, rthOpenTime, rthCloseTime, globexStartTime, orbSeconds, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, histMode, manualHistMax, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints, valueAreaPercent, mostrarPerfilRTHAyer, mostrarPerfilON, mostrarProbabilidad, minSessions, distanceBandTicks, filtrarPorContextoAMT, mostrarBadge, badgeFontSize, badgeBgOpacity, mostrarTabla, tablaFontSize, tablaBgOpacity);
		}

		public Indicators.EduS_Trader.EduS_Unified_Market_Context EduS_Unified_Market_Context(ISeries<double> input , int rthOpenTime, int rthCloseTime, int globexStartTime, int orbSeconds, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, EduS_HistMode histMode, double manualHistMax, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints, int valueAreaPercent, bool mostrarPerfilRTHAyer, bool mostrarPerfilON, bool mostrarProbabilidad, int minSessions, int distanceBandTicks, bool filtrarPorContextoAMT, bool mostrarBadge, int badgeFontSize, int badgeBgOpacity, bool mostrarTabla, int tablaFontSize, int tablaBgOpacity)
		{
			return indicator.EduS_Unified_Market_Context(input, rthOpenTime, rthCloseTime, globexStartTime, orbSeconds, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, histMode, manualHistMax, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints, valueAreaPercent, mostrarPerfilRTHAyer, mostrarPerfilON, mostrarProbabilidad, minSessions, distanceBandTicks, filtrarPorContextoAMT, mostrarBadge, badgeFontSize, badgeBgOpacity, mostrarTabla, tablaFontSize, tablaBgOpacity);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduS_Unified_Market_Context EduS_Unified_Market_Context(int rthOpenTime, int rthCloseTime, int globexStartTime, int orbSeconds, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, EduS_HistMode histMode, double manualHistMax, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints, int valueAreaPercent, bool mostrarPerfilRTHAyer, bool mostrarPerfilON, bool mostrarProbabilidad, int minSessions, int distanceBandTicks, bool filtrarPorContextoAMT, bool mostrarBadge, int badgeFontSize, int badgeBgOpacity, bool mostrarTabla, int tablaFontSize, int tablaBgOpacity)
		{
			return indicator.EduS_Unified_Market_Context(Input, rthOpenTime, rthCloseTime, globexStartTime, orbSeconds, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, histMode, manualHistMax, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints, valueAreaPercent, mostrarPerfilRTHAyer, mostrarPerfilON, mostrarProbabilidad, minSessions, distanceBandTicks, filtrarPorContextoAMT, mostrarBadge, badgeFontSize, badgeBgOpacity, mostrarTabla, tablaFontSize, tablaBgOpacity);
		}

		public Indicators.EduS_Trader.EduS_Unified_Market_Context EduS_Unified_Market_Context(ISeries<double> input , int rthOpenTime, int rthCloseTime, int globexStartTime, int orbSeconds, string cultureName, int decimals, int lineWidth, int rightLabelPadding, int labelAbove, EduS_HistMode histMode, double manualHistMax, bool showGlobalTimes, Brush globalTimeColor, DashStyleHelper globalTimeStyle, int globalTimeWidth, int globalTimeOpacity, int globalTimeFontSize, EduS_TextOrient globalTimeTextOrientation, EduS_TextPos globalTimeTextPosition, double globalTimeTextOffsetPoints, int valueAreaPercent, bool mostrarPerfilRTHAyer, bool mostrarPerfilON, bool mostrarProbabilidad, int minSessions, int distanceBandTicks, bool filtrarPorContextoAMT, bool mostrarBadge, int badgeFontSize, int badgeBgOpacity, bool mostrarTabla, int tablaFontSize, int tablaBgOpacity)
		{
			return indicator.EduS_Unified_Market_Context(input, rthOpenTime, rthCloseTime, globexStartTime, orbSeconds, cultureName, decimals, lineWidth, rightLabelPadding, labelAbove, histMode, manualHistMax, showGlobalTimes, globalTimeColor, globalTimeStyle, globalTimeWidth, globalTimeOpacity, globalTimeFontSize, globalTimeTextOrientation, globalTimeTextPosition, globalTimeTextOffsetPoints, valueAreaPercent, mostrarPerfilRTHAyer, mostrarPerfilON, mostrarProbabilidad, minSessions, distanceBandTicks, filtrarPorContextoAMT, mostrarBadge, badgeFontSize, badgeBgOpacity, mostrarTabla, tablaFontSize, tablaBgOpacity);
		}
	}
}

#endregion
