#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

// ================================================================
//  EduS_MasterPanel  –  NinjaTrader 8
//
//  Unifica los tres semáforos del sistema EduS_Trader:
//
//  CAPA 1 – Dirección (4 señales de tendencia):
//    SMA20 pendiente · SMA80 pendiente · LinReg89 pendiente · AVWAP leg activo
//
//  CAPA 2 – Zona AVWAP (proximidad al VWAP activo en unidades ATR):
//    A+  : Pin bar / rechazo exacto en el VWAP
//    A   : Precio dentro de ZoneVerde * ATR del VWAP
//    B+  : Undercut/Overcut leve (posible fakeout)
//    B   : Precio extendido entre ZoneVerde y ZoneAmarilla
//    --  : Fuera de zona o sin estructura
//
//  SEMÁFORO COMBINADO:
//    VERDE   : 4/4 señales alineadas + zona A o A+  → Alta probabilidad
//    AMARILLO: 4/4 + zona B/B+  ó  3/4 + zona A/A+ → Media probabilidad
//    ROJO    : señales mixtas o fuera de zona
//
//  SECCIONES (cada una activable/desactivable):
//    1. Semáforo     – bolas + dirección + grado de zona
//    2. 4 Señales    – tabla SMA20/80 · LinReg · AVWAP + zona
//    3. Zona Entrada – precios de entrada/stop/target (solo si VERDE)
//    4. Keltner S/T  – ancho de banda, stop y target en pts/tks/$
//
//  Calculate.OnBarClose → mínimo impacto en performance
// ================================================================
namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader
{
    // ── Enumeraciones internas ───────────────────────────────────
    internal enum MasterEstado { VerdeAlcista, VerdeBajista, AmarilloAlcista, AmarilloBajista, Rojo }
    internal enum ZonaGrado   { APlus, A, BPlus, B, Ninguna }

    public class EduS_MasterPanel : Indicator
    {
        // ── Indicadores internos ─────────────────────────────────
        private SMA    sma20, sma80;
        private LinReg lr89;
        private ATR    atrInd;
        private EduS_Trader.EduS_DynamicSwingAVWAP avwap;

        // Keltner interno
        private Series<double> kDiff;
        private EMA kEmaDiff, kEmaTypical;

        // ── Estado calculado – Capa 1 (tendencia) ────────────────
        private MasterEstado estadoActual = MasterEstado.Rojo;
        private bool   v_sma20Up, v_sma80Up, v_lrUp, v_vwapUp;
        private double v_sma20Val, v_sma80Val, v_lrVal, v_vwapVal;
        private int    tendenciaCount = 0;   // cuántas de 4 señales coinciden

        // ── Estado calculado – Capa 2 (zona AVWAP) ───────────────
        private ZonaGrado zonaActual   = ZonaGrado.Ninguna;
        private string    zonaLabel    = "--";
        private double    atrVal       = 0;

        // ── Estado calculado – Keltner ───────────────────────────
        private double k_bandWidth = 0;
        private double k_stopPts = 0, k_targetPts = 0;
        private double k_stopTks = 0, k_targetTks = 0;
        private double k_stopUsd = 0, k_targetUsd = 0;

        // ── Zona de Entrada (solo si VERDE) ──────────────────────
        // Entrada conservadora = Close de la barra
        // Stop primario = AVWAP_activo ± (ATR × StopMult)
        // T1 = banda Keltner opuesta (objetivo natural)
        // T2 = extensión: proyecta distancia AVWAP→Keltner desde la banda
        private double e_entrada  = double.NaN;
        private double e_avwap    = double.NaN;   // AVWAP activo (referencia estructural)
        private double e_stop     = double.NaN;   // Stop primario
        private double e_stopPts  = 0;            // distancia Entrada–Stop en puntos
        private double e_t1       = double.NaN;   // Target 1 (Keltner opuesto)
        private double e_t1Pts    = 0;
        private double e_rr1      = 0;            // R:R para T1
        private double e_t2       = double.NaN;   // Target 2 (extensión)
        private double e_t2Pts    = 0;
        private double e_rr2      = 0;            // R:R para T2

        // ── SharpDX recursos ─────────────────────────────────────
        private SharpDX.Direct2D1.Brush dxFondo, dxBorde, dxTitulo, dxGris;
        private SharpDX.Direct2D1.Brush dxVerde, dxAmarillo, dxRojo, dxApagado;
        private SharpDX.Direct2D1.Brush dxCian, dxNaranja;

        private SharpDX.DirectWrite.TextFormat fmtHeader;
        private SharpDX.DirectWrite.TextFormat fmtNormal;
        private SharpDX.DirectWrite.TextFormat fmtBold;
        private SharpDX.DirectWrite.TextFormat fmtMono;
        private SharpDX.DirectWrite.TextFormat fmtSmall;

        private bool dxReady = false;

        // ── Layout ───────────────────────────────────────────────
        private const float PAD_H   = 10f;
        private const float PAD_V   = 7f;
        private const float ROW_H   = 13f;
        private const float SEC_GAP = 3f;
        private const float MARGIN  = 8f;

        // ────────────────────────────────────────────────────────
        #region OnStateChange
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "EduS Master Panel: Semaforo unificado (tendencia + zona AVWAP + Keltner entrada)";
                Name                     = "EduS_MasterPanel";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = true;
                DisplayInDataBox         = false;
                DrawOnPricePanel         = true;
                IsSuspendedWhileInactive = true;
                ScaleJustification       = ScaleJustification.Right;

                // Secciones
                Mostrar_Semaforo  = true;
                Mostrar_Senales   = true;
                Mostrar_Entrada   = true;
                Mostrar_Keltner   = true;

                // Periodos indicadores
                Period_SMA20      = 20;
                Period_SMA80      = 80;
                Period_LR89       = 89;
                SlopeLookback     = 3;
                AVWAP_SwingPeriod = 50;
                AVWAP_BaseAPT     = 20;
                ATR_Period        = 14;

                // Zonas AVWAP (en múltiplos de ATR)
                ZoneVerde         = 0.5;    // precio dentro de 0.5*ATR del VWAP = zona A
                ZoneAmarilla      = 2.0;    // precio entre 0.5 y 2.0*ATR = zona B
                Stop_ATRMult      = 0.5;    // multiplicador ATR para stop primario (AVWAP ± ATR×mult)

                // Keltner
                Kelt_Period       = 52;
                Kelt_Multiplier   = 3.5;
                Kelt_StopPct      = 0.30;
                Kelt_RiskReward   = 2.0;
                Kelt_Contracts    = 1;

                // Visual
                Tamano_Bolas  = 20;
                Fondo_Opacidad = 40;
                Panel_Posicion = 1;   // 0=TopLeft 1=TopRight 2=BottomLeft 3=BottomRight
                Margen_Escala  = 70;
            }
            else if (State == State.Configure)
            {
                sma20      = SMA(Period_SMA20);
                sma80      = SMA(Period_SMA80);
                lr89       = LinReg(Input, Period_LR89);
                atrInd     = ATR(ATR_Period);

                avwap = EduS_DynamicSwingAVWAP(
                    AVWAP_SwingPeriod, AVWAP_BaseAPT,
                    true, 10.0, 3, 1, 15, true, 3.0, " ");

                kDiff       = new Series<double>(this);
                kEmaDiff    = EMA(kDiff, Kelt_Period);
                kEmaTypical = EMA(Typical, Kelt_Period);
            }
            else if (State == State.Terminated)
            {
                DisposeDXResources();
            }
        }
        #endregion

        // ────────────────────────────────────────────────────────
        #region OnBarUpdate
        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;

            int minBars = Math.Max(Math.Max(Period_SMA20, Period_SMA80),
                          Math.Max(Math.Max(Period_LR89,  Kelt_Period),
                          Math.Max(ATR_Period, SlopeLookback + 1)));
            if (CurrentBar < minBars) return;

            // ════════════════════════════════════════════════════
            //   CAPA 1 – DIRECCIÓN (4 señales de tendencia)
            // ════════════════════════════════════════════════════
            v_sma20Up  = sma20[0] > sma20[SlopeLookback];
            v_sma80Up  = sma80[0] > sma80[SlopeLookback];
            v_lrUp     = lr89[0]  > lr89[SlopeLookback];
            v_sma20Val = sma20[0];
            v_sma80Val = sma80[0];
            v_lrVal    = lr89[0];

            // Dirección AVWAP
            bool avwapUp   = !double.IsNaN(avwap.VWAP_Up[0]);
            bool avwapDown = !double.IsNaN(avwap.VWAP_Down[0]);

            if      (avwapUp && !avwapDown)  { v_vwapUp = true;  v_vwapVal = avwap.VWAP_Up[0];   }
            else if (avwapDown && !avwapUp)  { v_vwapUp = false; v_vwapVal = avwap.VWAP_Down[0]; }
            else if (avwapUp && avwapDown)
            {
                v_vwapUp  = avwap.VWAP_Up[0] > avwap.VWAP_Down[0];
                v_vwapVal = v_vwapUp ? avwap.VWAP_Up[0] : avwap.VWAP_Down[0];
            }
            else { v_vwapUp = false; v_vwapVal = Close[0]; }

            // ════════════════════════════════════════════════════
            //   CAPA 2 – ZONA AVWAP (distancia al VWAP activo)
            // ════════════════════════════════════════════════════
            atrVal = Math.Max(atrInd[0], TickSize);
            double vwapRef  = v_vwapVal;
            double price    = Close[0];
            double hi       = High[0];
            double lo       = Low[0];

            // Distancia del cierre al VWAP en múltiplos de ATR
            double distAbs  = Math.Abs(price - vwapRef);
            double distATR  = distAbs / atrVal;

            // Pin bar: mecha tocó el VWAP pero el cierre quedó del lado correcto
            bool pinLong  = v_vwapUp  && lo <= vwapRef && price > vwapRef;
            bool pinShort = !v_vwapUp && hi >= vwapRef && price < vwapRef;
            bool esPinBar = pinLong || pinShort;

            // ¿El precio está del lado correcto del VWAP?
            bool ladoCorrecto = v_vwapUp ? (price >= vwapRef) : (price <= vwapRef);

            if (esPinBar)
            {
                zonaActual = ZonaGrado.APlus;
                zonaLabel  = "A+";
            }
            else if (ladoCorrecto && distATR <= ZoneVerde)
            {
                zonaActual = ZonaGrado.A;
                zonaLabel  = "A";
            }
            else if (!ladoCorrecto && distATR <= ZoneVerde)
            {
                // Pequeño undercut/overcut = posible fakeout
                zonaActual = ZonaGrado.BPlus;
                zonaLabel  = "B+";
            }
            else if (ladoCorrecto && distATR > ZoneVerde && distATR <= ZoneAmarilla)
            {
                zonaActual = ZonaGrado.B;
                zonaLabel  = "B";
            }
            else
            {
                zonaActual = ZonaGrado.Ninguna;
                zonaLabel  = "--";
            }

            // ════════════════════════════════════════════════════
            //   SEMÁFORO COMBINADO (Capa1 × Capa2)
            // ════════════════════════════════════════════════════
            bool all4Alcista  = v_sma20Up &&  v_sma80Up &&  v_lrUp &&  v_vwapUp;
            bool all4Bajista  = !v_sma20Up && !v_sma80Up && !v_lrUp && !v_vwapUp;
            bool mix3Alcista  = v_sma20Up && !v_sma80Up &&  v_lrUp &&  v_vwapUp;  // SMA80 rezagada
            bool mix3Bajista  = !v_sma20Up && v_sma80Up && !v_lrUp && !v_vwapUp;

            bool zonaAlta   = zonaActual == ZonaGrado.APlus || zonaActual == ZonaGrado.A;
            bool zonaMed    = zonaActual == ZonaGrado.BPlus || zonaActual == ZonaGrado.B;

            if      (all4Alcista && zonaAlta)  estadoActual = MasterEstado.VerdeAlcista;
            else if (all4Bajista && zonaAlta)  estadoActual = MasterEstado.VerdeBajista;
            else if (all4Alcista && zonaMed)   estadoActual = MasterEstado.AmarilloAlcista;
            else if (all4Bajista && zonaMed)   estadoActual = MasterEstado.AmarilloBajista;
            else if (mix3Alcista && zonaAlta)  estadoActual = MasterEstado.AmarilloAlcista;
            else if (mix3Bajista && zonaAlta)  estadoActual = MasterEstado.AmarilloBajista;
            else                               estadoActual = MasterEstado.Rojo;

            // Contador de señales alineadas (para mostrar "3/4" o "4/4")
            bool esAlcista = v_vwapUp;
            tendenciaCount = (v_sma20Up == esAlcista ? 1 : 0)
                           + (v_sma80Up == esAlcista ? 1 : 0)
                           + (v_lrUp    == esAlcista ? 1 : 0)
                           + 1; // AVWAP siempre cuenta (es la referencia)

            // ════════════════════════════════════════════════════
            //   KELTNER STOP / TARGET
            // ════════════════════════════════════════════════════
            kDiff[0] = High[0] - Low[0];
            double kMid    = kEmaTypical[0];
            double kOff    = kEmaDiff[0] * Kelt_Multiplier;
            k_bandWidth    = (kMid + kOff) - (kMid - kOff);
            k_stopPts      = k_bandWidth * Kelt_StopPct;
            k_targetPts    = k_stopPts   * Kelt_RiskReward;
            double ts      = Instrument.MasterInstrument.TickSize;
            double pv      = Instrument.MasterInstrument.PointValue;
            k_stopTks      = k_stopPts   / ts;
            k_targetTks    = k_targetPts / ts;
            k_stopUsd      = k_stopPts   * pv * Kelt_Contracts;
            k_targetUsd    = k_targetPts * pv * Kelt_Contracts;

            // ════════════════════════════════════════════════════
            //   ZONA DE ENTRADA (solo si semáforo en VERDE)
            //   Entrada  = Close[0]  (conservador)
            //   Stop     = AVWAP_activo ± (ATR × Stop_ATRMult)
            //   T1       = Keltner opuesto (objetivo natural del swing)
            //   T2       = Extensión: misma distancia AVWAP→Keltner, proyectada desde Keltner
            //   R:R      = (T - Entrada) / (Entrada - Stop)
            // ════════════════════════════════════════════════════
            bool esVerde = estadoActual == MasterEstado.VerdeAlcista
                        || estadoActual == MasterEstado.VerdeBajista;

            double kUpper = kEmaTypical[0] + kEmaDiff[0] * Kelt_Multiplier;
            double kLower = kEmaTypical[0] - kEmaDiff[0] * Kelt_Multiplier;
            double ts2    = Instrument.MasterInstrument.TickSize;

            if (esVerde)
            {
                bool alcista = estadoActual == MasterEstado.VerdeAlcista;
                e_entrada = Close[0];

                if (alcista && !double.IsNaN(avwap.VWAP_Up[0]))
                {
                    e_avwap   = avwap.VWAP_Up[0];
                    // Stop: por debajo del AVWAP, con buffer ATR
                    e_stop    = e_avwap - atrVal * Stop_ATRMult;
                    e_stopPts = Math.Max(0, e_entrada - e_stop);

                    // T1 = Keltner superior (resistencia dinámica natural)
                    e_t1      = kUpper;
                    e_t1Pts   = Math.Max(0, e_t1 - e_entrada);

                    // T2 = extensión: distancia AVWAP→kUpper proyectada desde kUpper
                    double avwapToKelt = kUpper - e_avwap;
                    e_t2      = kUpper + Math.Max(0, avwapToKelt);
                    e_t2Pts   = Math.Max(0, e_t2 - e_entrada);
                }
                else if (!alcista && !double.IsNaN(avwap.VWAP_Down[0]))
                {
                    e_avwap   = avwap.VWAP_Down[0];
                    // Stop: por encima del AVWAP, con buffer ATR
                    e_stop    = e_avwap + atrVal * Stop_ATRMult;
                    e_stopPts = Math.Max(0, e_stop - e_entrada);

                    // T1 = Keltner inferior
                    e_t1      = kLower;
                    e_t1Pts   = Math.Max(0, e_entrada - e_t1);

                    // T2 = extensión: distancia AVWAP→kLower proyectada desde kLower
                    double avwapToKelt = e_avwap - kLower;
                    e_t2      = kLower - Math.Max(0, avwapToKelt);
                    e_t2Pts   = Math.Max(0, e_entrada - e_t2);
                }
                else
                {
                    // AVWAP no disponible — resetear
                    e_entrada = e_avwap = e_stop = e_t1 = e_t2 = double.NaN;
                    e_stopPts = e_t1Pts = e_t2Pts = e_rr1 = e_rr2 = 0;
                }

                // R:R (evitar division por cero)
                if (e_stopPts > ts2)
                {
                    e_rr1 = e_t1Pts / e_stopPts;
                    e_rr2 = e_t2Pts / e_stopPts;
                }
                else
                {
                    e_rr1 = e_rr2 = 0;
                }
            }
            else
            {
                e_entrada = e_avwap = e_stop = e_t1 = e_t2 = double.NaN;
                e_stopPts = e_t1Pts = e_t2Pts = e_rr1 = e_rr2 = 0;
            }
        }
        #endregion

        // ================================================================
        //   SharpDX – Recursos
        // ================================================================
        #region OnRenderTargetChanged
        public override void OnRenderTargetChanged()
        {
            DisposeDXResources();
            if (RenderTarget != null)
            {
                try   { CreateDXResources(RenderTarget); dxReady = true; }
                catch { dxReady = false; }
            }
        }
        #endregion

        #region CreateDXResources
        private void CreateDXResources(RenderTarget rt)
        {
            dxFondo    = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(12,  14,  20,  230));
            dxBorde    = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(70,  80,  110, 200));
            dxTitulo   = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(200, 200, 220, 255));
            dxGris     = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(150, 155, 165, 255));
            dxVerde    = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(0,   200, 80,  255));
            dxAmarillo = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(255, 210, 0,   255));
            dxRojo     = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(220, 50,  50,  255));
            dxApagado  = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(45,  45,  50,  160));
            dxCian     = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(80,  200, 220, 255));
            dxNaranja  = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(255, 140, 0,   255));

            var dwf = new SharpDX.DirectWrite.Factory();
            fmtHeader = new SharpDX.DirectWrite.TextFormat(dwf, "Arial",
                            SharpDX.DirectWrite.FontWeight.Bold,
                            SharpDX.DirectWrite.FontStyle.Normal, 10f);
            fmtNormal = new SharpDX.DirectWrite.TextFormat(dwf, "Arial",
                            SharpDX.DirectWrite.FontWeight.Normal,
                            SharpDX.DirectWrite.FontStyle.Normal, 10f);
            fmtBold   = new SharpDX.DirectWrite.TextFormat(dwf, "Arial",
                            SharpDX.DirectWrite.FontWeight.Bold,
                            SharpDX.DirectWrite.FontStyle.Normal, 12f);
            fmtMono   = new SharpDX.DirectWrite.TextFormat(dwf, "Consolas",
                            SharpDX.DirectWrite.FontWeight.Normal,
                            SharpDX.DirectWrite.FontStyle.Normal, 10f);
            fmtSmall  = new SharpDX.DirectWrite.TextFormat(dwf, "Arial",
                            SharpDX.DirectWrite.FontWeight.Normal,
                            SharpDX.DirectWrite.FontStyle.Normal, 9f);
            dwf.Dispose();

            SetAlign(fmtHeader, SharpDX.DirectWrite.TextAlignment.Center);
            SetAlign(fmtNormal, SharpDX.DirectWrite.TextAlignment.Leading);
            SetAlign(fmtBold,   SharpDX.DirectWrite.TextAlignment.Center);
            SetAlign(fmtMono,   SharpDX.DirectWrite.TextAlignment.Leading);
            SetAlign(fmtSmall,  SharpDX.DirectWrite.TextAlignment.Leading);
        }

        private void SetAlign(SharpDX.DirectWrite.TextFormat fmt,
                               SharpDX.DirectWrite.TextAlignment align)
        {
            fmt.TextAlignment      = align;
            fmt.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center;
            fmt.WordWrapping       = SharpDX.DirectWrite.WordWrapping.NoWrap;
        }
        #endregion

        // ================================================================
        //   OnRender
        // ================================================================
        #region OnRender
        protected override void OnRender(ChartControl cc, ChartScale cs)
        {
            if (!dxReady || RenderTarget == null || dxFondo == null)
            {
                if (RenderTarget != null)
                {
                    try   { DisposeDXResources(); CreateDXResources(RenderTarget); dxReady = true; }
                    catch { return; }
                }
                else return;
            }

            try { DrawPanel(cc, cs); }
            catch { dxReady = false; }
        }
        #endregion

        #region DrawPanel
        private void DrawPanel(ChartControl cc, ChartScale cs)
        {
            float diam   = Tamano_Bolas * 2f;
            float gap    = 8f;
            float panelW = Math.Max(PAD_H * 2f + diam * 3f + gap * 2f, 245f);

            bool esVerde    = estadoActual == MasterEstado.VerdeAlcista
                           || estadoActual == MasterEstado.VerdeBajista;
            bool esAmarillo = estadoActual == MasterEstado.AmarilloAlcista
                           || estadoActual == MasterEstado.AmarilloBajista;
            bool esRojo     = estadoActual == MasterEstado.Rojo;

            // ── Calcular alto del panel ──────────────────────────
            float panelH = PAD_V + ROW_H + SEC_GAP;  // título

            if (Mostrar_Semaforo)
            {
                panelH += ROW_H;               // encabezado
                panelH += diam + 4f;           // bolas
                panelH += ROW_H + 2f;          // dirección + zona
                panelH += SEC_GAP;
            }
            if (Mostrar_Senales)
            {
                if (Mostrar_Semaforo) panelH += ROW_H;   // separador
                panelH += ROW_H;               // encabezado
                panelH += ROW_H * 4f + SEC_GAP;
            }
            if (Mostrar_Entrada && esVerde)
            {
                if (Mostrar_Semaforo || Mostrar_Senales) panelH += ROW_H;  // separador
                panelH += ROW_H;               // encabezado
                panelH += ROW_H * 3f + SEC_GAP;  // entrada + stop + target
            }
            if (Mostrar_Keltner)
            {
                if (Mostrar_Semaforo || Mostrar_Senales || (Mostrar_Entrada && esVerde))
                    panelH += ROW_H;  // separador
                panelH += ROW_H;               // encabezado
                panelH += ROW_H * 3f + SEC_GAP;
            }
            panelH += PAD_V;

            // ── Posición del panel ───────────────────────────────
            float chartW = (float)cc.ActualWidth;
            float chartH = (float)cs.Height;
            float escOff = (float)Margen_Escala;
            float px, py;
            switch (Panel_Posicion)
            {
                case 0: px = MARGIN;                          py = MARGIN;                         break;
                case 1: px = chartW - panelW - MARGIN - escOff; py = MARGIN;                      break;
                case 2: px = MARGIN;                          py = chartH - panelH - MARGIN;       break;
                default:px = chartW - panelW - MARGIN - escOff; py = chartH - panelH - MARGIN;   break;
            }
            px = Math.Max(0f, px);
            py = Math.Max(0f, py);

            // ── Fondo ────────────────────────────────────────────
            SharpDX.RectangleF bg = new SharpDX.RectangleF(px, py, panelW, panelH);
            float opOrig = dxFondo.Opacity;
            dxFondo.Opacity = Fondo_Opacidad / 100f;
            RenderTarget.FillRectangle(bg, dxFondo);
            dxFondo.Opacity = opOrig;
            RenderTarget.DrawRectangle(bg, dxBorde, 1.5f);

            float cy = py + PAD_V;

            // ── Título general ───────────────────────────────────
            Txt("EduS · Master Panel", fmtHeader, dxTitulo, px + PAD_H, cy, panelW - PAD_H * 2f, ROW_H);
            cy += ROW_H + SEC_GAP;

            // ════════════════════════════════════════════════════
            //   SECCIÓN 1 – SEMÁFORO
            // ════════════════════════════════════════════════════
            if (Mostrar_Semaforo)
            {
                Header("── Semaforo ──", px, cy, panelW); cy += ROW_H;

                // Bolas
                float totalW = diam * 3f + gap * 2f;
                float bx     = px + (panelW - totalW) / 2f;
                Circle(bx,              cy, diam, esVerde    ? dxVerde    : dxApagado);
                Circle(bx + diam + gap, cy, diam, esAmarillo ? dxAmarillo : dxApagado);
                Circle(bx + (diam+gap)*2f, cy, diam, esRojo  ? dxRojo     : dxApagado);
                cy += diam + 4f;

                // Dirección + grado zona en la misma línea
                string dirTxt;
                SharpDX.Direct2D1.Brush dirBrush;
                switch (estadoActual)
                {
                    case MasterEstado.VerdeAlcista:    dirTxt = "ALCISTA";  dirBrush = dxVerde;    break;
                    case MasterEstado.VerdeBajista:    dirTxt = "BAJISTA";  dirBrush = dxVerde;    break;
                    case MasterEstado.AmarilloAlcista: dirTxt = "ALCISTA";  dirBrush = dxAmarillo; break;
                    case MasterEstado.AmarilloBajista: dirTxt = "BAJISTA";  dirBrush = dxAmarillo; break;
                    default:                           dirTxt = "MIXTO";    dirBrush = dxRojo;     break;
                }

                // Color grado zona
                SharpDX.Direct2D1.Brush zonaBrush;
                switch (zonaActual)
                {
                    case ZonaGrado.APlus:  zonaBrush = dxVerde;    break;
                    case ZonaGrado.A:      zonaBrush = dxVerde;    break;
                    case ZonaGrado.BPlus:  zonaBrush = dxAmarillo; break;
                    case ZonaGrado.B:      zonaBrush = dxNaranja;  break;
                    default:               zonaBrush = dxGris;     break;
                }

                // Dirección centrada
                Txt(dirTxt, fmtBold, dirBrush, px + PAD_H, cy, panelW * 0.55f - PAD_H, ROW_H);

                // Zona a la derecha en la misma fila
                SetAlign(fmtBold, SharpDX.DirectWrite.TextAlignment.Trailing);
                Txt("[" + zonaLabel + "]", fmtBold, zonaBrush,
                    px + panelW * 0.55f, cy, panelW * 0.45f - PAD_H, ROW_H);
                SetAlign(fmtBold, SharpDX.DirectWrite.TextAlignment.Center);

                cy += ROW_H + SEC_GAP;
            }

            // ════════════════════════════════════════════════════
            //   SECCIÓN 2 – 4 SEÑALES
            // ════════════════════════════════════════════════════
            if (Mostrar_Senales)
            {
                if (Mostrar_Semaforo) { Sep(px + PAD_H, cy, px + panelW - PAD_H); cy += ROW_H; }

                // Encabezado con contador de señales alineadas
                string hdr = "── Señales  " + tendenciaCount + "/4 · Zona " + zonaLabel + " ──";
                Header(hdr, px, cy, panelW); cy += ROW_H;

                string[] noms = { "SMA 20", "SMA 80", "LinReg 89", "AVWAP" };
                bool[]   ups  = { v_sma20Up, v_sma80Up, v_lrUp, v_vwapUp };
                double[] vals = { v_sma20Val, v_sma80Val, v_lrVal, v_vwapVal };

                float cValX = px + panelW - PAD_H - 88f;
                float cValW = 88f;

                for (int i = 0; i < 4; i++)
                {
                    // Indicador de color
                    RenderTarget.FillRectangle(
                        new SharpDX.RectangleF(px + PAD_H, cy + ROW_H / 2f - 4f, 5f, 8f),
                        ups[i] ? dxVerde : dxRojo);

                    Txt(noms[i], fmtNormal, dxGris, px + PAD_H + 8f, cy, 75f, ROW_H);

                    // Valor con flecha
                    SetAlign(fmtMono, SharpDX.DirectWrite.TextAlignment.Trailing);
                    string vs = (ups[i] ? "+ " : "- ") + vals[i].ToString("0.00");
                    Txt(vs, fmtMono, ups[i] ? dxVerde : dxRojo, cValX, cy, cValW, ROW_H);
                    SetAlign(fmtMono, SharpDX.DirectWrite.TextAlignment.Leading);

                    cy += ROW_H;
                }
                cy += SEC_GAP;
            }

            // ════════════════════════════════════════════════════
            //   SECCIÓN 3 – ZONA DE ENTRADA (solo si VERDE)
            // ════════════════════════════════════════════════════
            if (Mostrar_Entrada && esVerde && !double.IsNaN(e_entrada))
            {
                if (Mostrar_Semaforo || Mostrar_Senales)
                { Sep(px + PAD_H, cy, px + panelW - PAD_H); cy += ROW_H; }

                bool alcista = estadoActual == MasterEstado.VerdeAlcista;
                Header("── Zona Entrada (AVWAP) ──", px, cy, panelW); cy += ROW_H;

                // Entrada (Close)
                EntradaRow("Entrada:", e_entrada.ToString("0.00"), px, cy, panelW, dxCian);
                cy += ROW_H;

                // AVWAP activo — referencia estructural del swing
                if (!double.IsNaN(e_avwap))
                {
                    string avwapDir = alcista ? "(soporte)" : "(resist.)";
                    EntradaRow("AVWAP:", e_avwap.ToString("0.00") + "  " + avwapDir,
                               px, cy, panelW, dxGris);
                    cy += ROW_H;
                }

                // Stop primario (AVWAP ± ATR×mult)
                if (!double.IsNaN(e_stop))
                {
                    string stopStr = e_stop.ToString("0.00")
                                   + "  (" + (alcista ? "-" : "+")
                                   + e_stopPts.ToString("0.00") + " pts)";
                    EntradaRow("Stop:", stopStr, px, cy, panelW, dxRojo);
                    cy += ROW_H;
                }

                // T1 — Keltner opuesto (objetivo natural)
                if (!double.IsNaN(e_t1))
                {
                    string t1Str = e_t1.ToString("0.00")
                                 + "  (" + (alcista ? "+" : "-")
                                 + e_t1Pts.ToString("0.00") + " pts)"
                                 + "  R:R " + e_rr1.ToString("0.1");
                    EntradaRow("T1:", t1Str, px, cy, panelW, dxVerde);
                    cy += ROW_H;
                }

                // T2 — Extensión (distancia AVWAP→Keltner proyectada)
                if (!double.IsNaN(e_t2))
                {
                    string t2Str = e_t2.ToString("0.00")
                                 + "  (" + (alcista ? "+" : "-")
                                 + e_t2Pts.ToString("0.00") + " pts)"
                                 + "  R:R " + e_rr2.ToString("0.1");
                    EntradaRow("T2:", t2Str, px, cy, panelW, dxNaranja);
                    cy += ROW_H;
                }

                cy += SEC_GAP;
            }

            // ════════════════════════════════════════════════════
            //   SECCIÓN 4 – KELTNER STOP / TARGET
            // ════════════════════════════════════════════════════
            if (Mostrar_Keltner)
            {
                if (Mostrar_Semaforo || Mostrar_Senales || (Mostrar_Entrada && esVerde))
                { Sep(px + PAD_H, cy, px + panelW - PAD_H); cy += ROW_H; }

                Header("── Keltner Stop / Target ──", px, cy, panelW); cy += ROW_H;

                KRow("Width:", k_bandWidth.ToString("0.00") + " pts", px, cy, panelW, dxCian); cy += ROW_H;

                string stopK  = k_stopPts.ToString("0.00") + " pts  "
                              + k_stopTks.ToString("0") + " tks  $" + k_stopUsd.ToString("0.00");
                KRow("Stop " + (Kelt_StopPct * 100).ToString("0") + "%:", stopK, px, cy, panelW, dxRojo); cy += ROW_H;

                string tgtK   = k_targetPts.ToString("0.00") + " pts  "
                              + k_targetTks.ToString("0") + " tks  $" + k_targetUsd.ToString("0.00");
                KRow("Target " + Kelt_RiskReward.ToString("0.0") + ":1:", tgtK, px, cy, panelW, dxVerde);
            }
        }
        #endregion

        // ── Helpers de dibujo ────────────────────────────────────
        #region Helpers
        private void Txt(string s, SharpDX.DirectWrite.TextFormat fmt,
                         SharpDX.Direct2D1.Brush b,
                         float x, float y, float w, float h)
        {
            if (fmt == null || string.IsNullOrEmpty(s)) return;
            RenderTarget.DrawText(s, fmt, new SharpDX.RectangleF(x, y, w, h), b);
        }

        private void Circle(float x, float y, float diam, SharpDX.Direct2D1.Brush fill)
        {
            var e = new SharpDX.Direct2D1.Ellipse(
                new SharpDX.Vector2(x + diam/2f, y + diam/2f), diam/2f, diam/2f);
            RenderTarget.FillEllipse(e, fill);
            RenderTarget.DrawEllipse(e, dxBorde, 1.2f);
        }

        private void Sep(float x1, float y, float x2)
        {
            RenderTarget.DrawLine(
                new SharpDX.Vector2(x1, y + ROW_H/2f),
                new SharpDX.Vector2(x2, y + ROW_H/2f),
                dxBorde, 0.8f);
        }

        private void Header(string txt, float x, float y, float panelW)
        {
            SetAlign(fmtHeader, SharpDX.DirectWrite.TextAlignment.Center);
            Txt(txt, fmtHeader, dxTitulo, x + PAD_H, y, panelW - PAD_H*2f, ROW_H);
        }

        private void KRow(string lbl, string val, float x, float y, float pw,
                          SharpDX.Direct2D1.Brush vBrush)
        {
            SetAlign(fmtNormal, SharpDX.DirectWrite.TextAlignment.Leading);
            Txt(lbl, fmtNormal, dxGris, x + PAD_H, y, 62f, ROW_H);
            SetAlign(fmtMono, SharpDX.DirectWrite.TextAlignment.Leading);
            Txt(val, fmtMono, vBrush, x + PAD_H + 64f, y, pw - PAD_H*2f - 64f, ROW_H);
        }

        private void EntradaRow(string lbl, string val, float x, float y, float pw,
                                SharpDX.Direct2D1.Brush vBrush)
        {
            SetAlign(fmtNormal, SharpDX.DirectWrite.TextAlignment.Leading);
            Txt(lbl, fmtNormal, dxGris, x + PAD_H, y, 55f, ROW_H);
            SetAlign(fmtMono, SharpDX.DirectWrite.TextAlignment.Leading);
            Txt(val, fmtMono, vBrush, x + PAD_H + 57f, y, pw - PAD_H*2f - 57f, ROW_H);
        }
        #endregion

        // ── Dispose ──────────────────────────────────────────────
        #region Dispose
        private void DisposeDXResources()
        {
            dxReady = false;
            SharpDX.Direct2D1.Brush[] bb =
                { dxFondo, dxBorde, dxTitulo, dxGris,
                  dxVerde, dxAmarillo, dxRojo, dxApagado, dxCian, dxNaranja };
            foreach (var b in bb) b?.Dispose();
            dxFondo = dxBorde = dxTitulo = dxGris =
            dxVerde = dxAmarillo = dxRojo = dxApagado = dxCian = dxNaranja = null;

            SharpDX.DirectWrite.TextFormat[] ff =
                { fmtHeader, fmtNormal, fmtBold, fmtMono, fmtSmall };
            foreach (var f in ff) f?.Dispose();
            fmtHeader = fmtNormal = fmtBold = fmtMono = fmtSmall = null;
        }
        #endregion

        // ── Propiedades ──────────────────────────────────────────
        #region Properties

        // 1. Secciones
        [NinjaScriptProperty]
        [Display(Name = "Semaforo", Order = 1, GroupName = "1. Secciones")]
        public bool Mostrar_Semaforo { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "4 Senales", Order = 2, GroupName = "1. Secciones")]
        public bool Mostrar_Senales { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Zona Entrada (solo si verde)", Order = 3, GroupName = "1. Secciones")]
        public bool Mostrar_Entrada { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Keltner Stop/Target", Order = 4, GroupName = "1. Secciones")]
        public bool Mostrar_Keltner { get; set; }

        // 2. Indicadores
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA20", Order = 1, GroupName = "2. Periodos")]
        public int Period_SMA20 { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "SMA80", Order = 2, GroupName = "2. Periodos")]
        public int Period_SMA80 { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "LinReg", Order = 3, GroupName = "2. Periodos")]
        public int Period_LR89 { get; set; }

        [NinjaScriptProperty]
        [Range(1, 20)]
        [Display(Name = "Barras atras pendiente",
                 Description = "Comparacion actual vs N barras atras para calcular pendiente.",
                 Order = 4, GroupName = "2. Periodos")]
        public int SlopeLookback { get; set; }

        [NinjaScriptProperty]
        [Range(2, int.MaxValue)]
        [Display(Name = "AVWAP Swing Period", Order = 5, GroupName = "2. Periodos")]
        public int AVWAP_SwingPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 10000)]
        [Display(Name = "AVWAP Base APT", Order = 6, GroupName = "2. Periodos")]
        public int AVWAP_BaseAPT { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "ATR Periodo (zona AVWAP)", Order = 7, GroupName = "2. Periodos")]
        public int ATR_Period { get; set; }

        // 3. Zonas AVWAP
        [NinjaScriptProperty]
        [Range(0.05, 5.0)]
        [Display(Name = "Zona VERDE (ATR x)",
                 Description = "Precio dentro de N*ATR del AVWAP activo = zona A (alta prob).",
                 Order = 1, GroupName = "3. Zonas AVWAP")]
        public double ZoneVerde { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 15.0)]
        [Display(Name = "Zona AMARILLA (ATR x)",
                 Description = "Precio entre ZoneVerde y N*ATR = zona B (media prob).",
                 Order = 2, GroupName = "3. Zonas AVWAP")]
        public double ZoneAmarilla { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Stop Mult ATR",
                 Description = "El stop se coloca en: AVWAP ± (ATR × StopMult). 0.5 = medio ATR de buffer sobre el AVWAP.",
                 Order = 3, GroupName = "3. Zonas AVWAP")]
        public double Stop_ATRMult { get; set; }

        // 4. Keltner
        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Periodo", Order = 1, GroupName = "4. Keltner")]
        public int Kelt_Period { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, double.MaxValue)]
        [Display(Name = "Multiplicador", Order = 2, GroupName = "4. Keltner")]
        public double Kelt_Multiplier { get; set; }

        [NinjaScriptProperty]
        [Range(0.01, 1.0)]
        [Display(Name = "Stop % de banda", Order = 3, GroupName = "4. Keltner")]
        public double Kelt_StopPct { get; set; }

        [NinjaScriptProperty]
        [Range(0.5, double.MaxValue)]
        [Display(Name = "Risk/Reward ratio", Order = 4, GroupName = "4. Keltner")]
        public double Kelt_RiskReward { get; set; }

        [NinjaScriptProperty]
        [Range(1, int.MaxValue)]
        [Display(Name = "Contratos", Order = 5, GroupName = "4. Keltner")]
        public int Kelt_Contracts { get; set; }

        // 5. Visual
        [NinjaScriptProperty]
        [Range(10, 40)]
        [Display(Name = "Tamano bolas (px)", Order = 1, GroupName = "5. Visual")]
        public int Tamano_Bolas { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Opacidad fondo %", Order = 2, GroupName = "5. Visual")]
        public int Fondo_Opacidad { get; set; }

        [NinjaScriptProperty]
        [Range(0, 3)]
        [Display(Name = "Posicion (0=TL 1=TR 2=BL 3=BR)",
                 Description = "0=Arriba Izq  1=Arriba Der  2=Abajo Izq  3=Abajo Der",
                 Order = 3, GroupName = "5. Visual")]
        public int Panel_Posicion { get; set; }

        [NinjaScriptProperty]
        [Range(0, 200)]
        [Display(Name = "Margen escala precio (px)",
                 Description = "Pixeles extra izquierda en posiciones derechas. Ajustar segun resolucion.",
                 Order = 4, GroupName = "5. Visual")]
        public int Margen_Escala { get; set; }

        #endregion
    }
}


// ================================================================
//   Codigo generado por NinjaScript (no modificar)
// ================================================================

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduS_MasterPanel[] cacheEduS_MasterPanel;
		public EduS_Trader.EduS_MasterPanel EduS_MasterPanel(bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, int period_SMA20, int period_SMA80, int period_LR89, int slopeLookback, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, int aTR_Period, double zoneVerde, double zoneAmarilla, double stop_ATRMult, int kelt_Period, double kelt_Multiplier, double kelt_StopPct, double kelt_RiskReward, int kelt_Contracts, int tamano_Bolas, int fondo_Opacidad, int panel_Posicion, int margen_Escala)
		{
			return EduS_MasterPanel(Input, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, period_SMA20, period_SMA80, period_LR89, slopeLookback, aVWAP_SwingPeriod, aVWAP_BaseAPT, aTR_Period, zoneVerde, zoneAmarilla, stop_ATRMult, kelt_Period, kelt_Multiplier, kelt_StopPct, kelt_RiskReward, kelt_Contracts, tamano_Bolas, fondo_Opacidad, panel_Posicion, margen_Escala);
		}

		public EduS_Trader.EduS_MasterPanel EduS_MasterPanel(ISeries<double> input, bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, int period_SMA20, int period_SMA80, int period_LR89, int slopeLookback, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, int aTR_Period, double zoneVerde, double zoneAmarilla, double stop_ATRMult, int kelt_Period, double kelt_Multiplier, double kelt_StopPct, double kelt_RiskReward, int kelt_Contracts, int tamano_Bolas, int fondo_Opacidad, int panel_Posicion, int margen_Escala)
		{
			if (cacheEduS_MasterPanel != null)
				for (int idx = 0; idx < cacheEduS_MasterPanel.Length; idx++)
					if (cacheEduS_MasterPanel[idx] != null && cacheEduS_MasterPanel[idx].Mostrar_Semaforo == mostrar_Semaforo && cacheEduS_MasterPanel[idx].Mostrar_Senales == mostrar_Senales && cacheEduS_MasterPanel[idx].Mostrar_Entrada == mostrar_Entrada && cacheEduS_MasterPanel[idx].Mostrar_Keltner == mostrar_Keltner && cacheEduS_MasterPanel[idx].Period_SMA20 == period_SMA20 && cacheEduS_MasterPanel[idx].Period_SMA80 == period_SMA80 && cacheEduS_MasterPanel[idx].Period_LR89 == period_LR89 && cacheEduS_MasterPanel[idx].SlopeLookback == slopeLookback && cacheEduS_MasterPanel[idx].AVWAP_SwingPeriod == aVWAP_SwingPeriod && cacheEduS_MasterPanel[idx].AVWAP_BaseAPT == aVWAP_BaseAPT && cacheEduS_MasterPanel[idx].ATR_Period == aTR_Period && cacheEduS_MasterPanel[idx].ZoneVerde == zoneVerde && cacheEduS_MasterPanel[idx].ZoneAmarilla == zoneAmarilla && cacheEduS_MasterPanel[idx].Stop_ATRMult == stop_ATRMult && cacheEduS_MasterPanel[idx].Kelt_Period == kelt_Period && cacheEduS_MasterPanel[idx].Kelt_Multiplier == kelt_Multiplier && cacheEduS_MasterPanel[idx].Kelt_StopPct == kelt_StopPct && cacheEduS_MasterPanel[idx].Kelt_RiskReward == kelt_RiskReward && cacheEduS_MasterPanel[idx].Kelt_Contracts == kelt_Contracts && cacheEduS_MasterPanel[idx].Tamano_Bolas == tamano_Bolas && cacheEduS_MasterPanel[idx].Fondo_Opacidad == fondo_Opacidad && cacheEduS_MasterPanel[idx].Panel_Posicion == panel_Posicion && cacheEduS_MasterPanel[idx].Margen_Escala == margen_Escala && cacheEduS_MasterPanel[idx].EqualsInput(input))
						return cacheEduS_MasterPanel[idx];
			return CacheIndicator<EduS_Trader.EduS_MasterPanel>(new EduS_Trader.EduS_MasterPanel(){ Mostrar_Semaforo = mostrar_Semaforo, Mostrar_Senales = mostrar_Senales, Mostrar_Entrada = mostrar_Entrada, Mostrar_Keltner = mostrar_Keltner, Period_SMA20 = period_SMA20, Period_SMA80 = period_SMA80, Period_LR89 = period_LR89, SlopeLookback = slopeLookback, AVWAP_SwingPeriod = aVWAP_SwingPeriod, AVWAP_BaseAPT = aVWAP_BaseAPT, ATR_Period = aTR_Period, ZoneVerde = zoneVerde, ZoneAmarilla = zoneAmarilla, Stop_ATRMult = stop_ATRMult, Kelt_Period = kelt_Period, Kelt_Multiplier = kelt_Multiplier, Kelt_StopPct = kelt_StopPct, Kelt_RiskReward = kelt_RiskReward, Kelt_Contracts = kelt_Contracts, Tamano_Bolas = tamano_Bolas, Fondo_Opacidad = fondo_Opacidad, Panel_Posicion = panel_Posicion, Margen_Escala = margen_Escala }, input, ref cacheEduS_MasterPanel);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduS_MasterPanel EduS_MasterPanel(bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, int period_SMA20, int period_SMA80, int period_LR89, int slopeLookback, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, int aTR_Period, double zoneVerde, double zoneAmarilla, double stop_ATRMult, int kelt_Period, double kelt_Multiplier, double kelt_StopPct, double kelt_RiskReward, int kelt_Contracts, int tamano_Bolas, int fondo_Opacidad, int panel_Posicion, int margen_Escala)
		{
			return indicator.EduS_MasterPanel(Input, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, period_SMA20, period_SMA80, period_LR89, slopeLookback, aVWAP_SwingPeriod, aVWAP_BaseAPT, aTR_Period, zoneVerde, zoneAmarilla, stop_ATRMult, kelt_Period, kelt_Multiplier, kelt_StopPct, kelt_RiskReward, kelt_Contracts, tamano_Bolas, fondo_Opacidad, panel_Posicion, margen_Escala);
		}

		public Indicators.EduS_Trader.EduS_MasterPanel EduS_MasterPanel(ISeries<double> input , bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, int period_SMA20, int period_SMA80, int period_LR89, int slopeLookback, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, int aTR_Period, double zoneVerde, double zoneAmarilla, double stop_ATRMult, int kelt_Period, double kelt_Multiplier, double kelt_StopPct, double kelt_RiskReward, int kelt_Contracts, int tamano_Bolas, int fondo_Opacidad, int panel_Posicion, int margen_Escala)
		{
			return indicator.EduS_MasterPanel(input, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, period_SMA20, period_SMA80, period_LR89, slopeLookback, aVWAP_SwingPeriod, aVWAP_BaseAPT, aTR_Period, zoneVerde, zoneAmarilla, stop_ATRMult, kelt_Period, kelt_Multiplier, kelt_StopPct, kelt_RiskReward, kelt_Contracts, tamano_Bolas, fondo_Opacidad, panel_Posicion, margen_Escala);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduS_MasterPanel EduS_MasterPanel(bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, int period_SMA20, int period_SMA80, int period_LR89, int slopeLookback, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, int aTR_Period, double zoneVerde, double zoneAmarilla, double stop_ATRMult, int kelt_Period, double kelt_Multiplier, double kelt_StopPct, double kelt_RiskReward, int kelt_Contracts, int tamano_Bolas, int fondo_Opacidad, int panel_Posicion, int margen_Escala)
		{
			return indicator.EduS_MasterPanel(Input, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, period_SMA20, period_SMA80, period_LR89, slopeLookback, aVWAP_SwingPeriod, aVWAP_BaseAPT, aTR_Period, zoneVerde, zoneAmarilla, stop_ATRMult, kelt_Period, kelt_Multiplier, kelt_StopPct, kelt_RiskReward, kelt_Contracts, tamano_Bolas, fondo_Opacidad, panel_Posicion, margen_Escala);
		}

		public Indicators.EduS_Trader.EduS_MasterPanel EduS_MasterPanel(ISeries<double> input , bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, int period_SMA20, int period_SMA80, int period_LR89, int slopeLookback, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, int aTR_Period, double zoneVerde, double zoneAmarilla, double stop_ATRMult, int kelt_Period, double kelt_Multiplier, double kelt_StopPct, double kelt_RiskReward, int kelt_Contracts, int tamano_Bolas, int fondo_Opacidad, int panel_Posicion, int margen_Escala)
		{
			return indicator.EduS_MasterPanel(input, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, period_SMA20, period_SMA80, period_LR89, slopeLookback, aVWAP_SwingPeriod, aVWAP_BaseAPT, aTR_Period, zoneVerde, zoneAmarilla, stop_ATRMult, kelt_Period, kelt_Multiplier, kelt_StopPct, kelt_RiskReward, kelt_Contracts, tamano_Bolas, fondo_Opacidad, panel_Posicion, margen_Escala);
		}
	}
}

#endregion
