#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
// SharpDX: sin using directives -- todos los tipos calificados completamente
// para evitar colisiones CS0104 con System.Windows.Media / Shapes.
#endregion

// ================================================================
//  EduS_MasterPanel_HUD_V2  --  NinjaTrader 8
//
//  HUD flotante V2: agrega seccion POSICION al HUD V1.
//  La seccion Posicion muestra (via DispatcherTimer cada 3 seg):
//    - Instrumento  LONG/SHORT qty @ precio | No Position
//    - P&L no realizado en $ y puntos
//    - Stop Risk $ | Target Profit $ | R:R
//    - ATM o manual, segun ordenes activas
//
//  Todo lo demas (semaforo, senales, entrada, keltner) es
//  identico al HUD V1.  Se compila como clase independiente.
// ================================================================
namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader
{
    // ─── Enumeraciones ──────────────────────────────────────────
    internal enum MasterEstado_HUD2  { VerdeAlcista, VerdeBajista, AmarilloAlcista, AmarilloBajista, Rojo }
    internal enum ZonaGrado_HUD2     { APlus, A, LRPlus, LR, BPlus, B, Ninguna }

    // ─── Snapshot calculo (hilo NT -> hilo UI) ──────────────────
    internal struct HudSnap2
    {
        public string           Instrumento;
        public MasterEstado_HUD2 Estado;
        public string           ZonaLabel;
        public ZonaGrado_HUD2   Zona;
        public int              TendenciaCount;
        public bool             SMA20Up, SMA80Up, LRUp, VWAPUp;
        public double           SMA20Val, SMA80Val, LRVal, VWAPVal;
        public bool             AVWAPsobreBM;
        public double           KeltnerBM;
        public bool             EntradaValida, EntradaAlcista;
        public double           Entrada, AVWAPprice, Stop, StopPts;
        public double           T1, T1Pts, RR1, T2, T2Pts, RR2;
        public double           BandWidth, KStopPts, KStopTks, KStopUsd;
        public double           KTargetPts, KTargetTks, KTargetUsd;
        public double           KStopPct, KRR;
        public bool             MostrarSemaforo, MostrarSenales, MostrarEntrada, MostrarKeltner, MostrarPosicion;
        // Ultima señal persistente
        public bool             UltimaEntradaValida;   // hay al menos una señal guardada
        public bool             UltimaEntradaActiva;   // sigue en verde ahora mismo
        public string           UltimaFechaHora;       // timestamp de cuando se activó
        public bool             UltimaEsAlcista;
        public string           UltimaZonaLabel;       // A+, A, LR+, LR
        public bool             UltimaEsZonaLR;        // true si la zona es LR o LR+
        public double           UltimaEntrada, UltimaAVWAP, UltimaStop, UltimaStopPts;
        public double           UltimaT1, UltimaT1Pts, UltimaRR1, UltimaT2, UltimaT2Pts, UltimaRR2;
        public bool             LRsobreBM;             // Capa 4: LinReg89 vs BM Keltner
        public double           LRVal2;                // valor LinReg89 para mostrar en señales Capa4
    }

    // ─── Snapshot posicion (timer -> hilo UI) ───────────────────
    internal struct PosSnap
    {
        public bool   TienePos;          // hay posicion abierta
        public bool   EsLong;
        public int    Cantidad;
        public double PrecioEntrada;
        public double PLdolar;           // P&L no realizado $
        public double PLpuntos;          // P&L no realizado pts
        public bool   TieneStop;
        public double StopRisk;          // riesgo en $ segun ordenes stop
        public bool   TieneTarget;
        public double TargetProfit;      // beneficio potencial $ segun target
        public double RR;                // ratio riesgo/beneficio
        public bool   RiesgoIlimitado;   // no hay stop cubriendo toda la posicion
        public bool   GananciaAsegurada; // stop por encima del precio de entrada (long)
        public string Etiqueta;          // "ATM" | "Manual" | ""
        public string InstrFull;         // nombre completo del instrumento
    }

    // ================================================================
    //   Panel WPF  (sin ningun tipo SharpDX)
    // ================================================================
    internal sealed class MasterHudPanel2 : Border
    {
        // Paleta WPF
        internal static readonly SolidColorBrush C_BG     = Frz(new SolidColorBrush(Color.FromArgb(245, 12, 14, 20)));
        private  static readonly SolidColorBrush C_BORDER = Frz(new SolidColorBrush(Color.FromRgb(70, 80, 110)));
        private  static readonly SolidColorBrush C_TITLE  = Frz(new SolidColorBrush(Color.FromRgb(200, 200, 220)));
        private  static readonly SolidColorBrush C_GRAY   = Frz(new SolidColorBrush(Color.FromRgb(150, 155, 165)));
        private  static readonly SolidColorBrush C_GREEN  = Frz(new SolidColorBrush(Color.FromRgb(0, 200, 80)));
        private  static readonly SolidColorBrush C_YELLOW = Frz(new SolidColorBrush(Color.FromRgb(255, 210, 0)));
        private  static readonly SolidColorBrush C_RED    = Frz(new SolidColorBrush(Color.FromRgb(220, 50, 50)));
        private  static readonly SolidColorBrush C_DIM    = Frz(new SolidColorBrush(Color.FromArgb(100, 45, 45, 50)));
        private  static readonly SolidColorBrush C_CYAN   = Frz(new SolidColorBrush(Color.FromRgb(80, 200, 220)));
        private  static readonly SolidColorBrush C_ORANGE = Frz(new SolidColorBrush(Color.FromRgb(255, 140, 0)));
        private  static readonly SolidColorBrush C_V2TAG  = Frz(new SolidColorBrush(Color.FromRgb(180, 130, 255)));
        private  static readonly SolidColorBrush C_LIME   = Frz(new SolidColorBrush(Color.FromRgb(100, 255, 120)));
        private  static readonly SolidColorBrush C_ORANG2 = Frz(new SolidColorBrush(Color.FromRgb(255, 160, 40)));
        private static SolidColorBrush Frz(SolidColorBrush b) { b.Freeze(); return b; }

        // ─── Semaforo ─────────────────────────────────────────
        private TextBlock  _tbTitle;
        private Ellipse    _elVerde, _elAmarillo, _elRojo;
        private TextBlock  _tbDir, _tbZona;
        private StackPanel _spSemaforo, _spSenales, _spEntrada, _spKeltner, _spPosicion;
        private Border     _sep2, _sep3, _sep4, _sep5;
        private TextBlock  _tbHdr2;
        private readonly Rectangle[]  _sigRect = new Rectangle[6];
        private readonly TextBlock[]  _sigLbl  = new TextBlock[6];
        private readonly TextBlock[]  _sigVal  = new TextBlock[6];
        private TextBlock _tbEntrada, _tbAVWAP, _tbStop, _tbT1, _tbT2;
        private TextBlock _tbAVWAPLbl;  // label dinamico "AVWAP:" / "LinReg:"
        private TextBlock _tbEntradaBadge, _tbEntradaTS;
        private TextBlock _tbKWidthLbl, _tbKWidth, _tbKStopLbl, _tbKStop, _tbKTgtLbl, _tbKTgt;
        // ─── Posicion ─────────────────────────────────────────
        private TextBlock _tbPosDir;       // "LONG 2 @ 5234.00" | "No Position"
        private TextBlock _tbPosPL;        // "$+120  (+2.40 pts)"
        private TextBlock _tbPosOrdenes;   // "Stop Risk: $150  |  Target: $300  |  R:R 2.0:1"

        public MasterHudPanel2() { Build(); }

        private void Build()
        {
            Background      = C_BG;
            BorderBrush     = C_BORDER;
            BorderThickness = new Thickness(1.5);
            CornerRadius    = new CornerRadius(5);
            Padding         = new Thickness(10, 7, 10, 8);
            MinWidth        = 310;

            var root = new StackPanel { Orientation = Orientation.Vertical };
            Child = root;

            // Titulo
            var tRow = new Grid { Margin = new Thickness(0, 0, 0, 3) };
            tRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _tbTitle = Tb("EduS . ---", 12, FontWeights.Bold, C_TITLE);
            Grid.SetColumn(_tbTitle, 0);
            tRow.Children.Add(_tbTitle);
            var tbTag = Tb("[HUD v3]", 9, FontWeights.Normal, C_V2TAG);
            tbTag.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(tbTag, 1);
            tRow.Children.Add(tbTag);
            root.Children.Add(tRow);
            root.Children.Add(HSep());

            // SECCION 0: POSICION
            _spPosicion = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            root.Children.Add(_spPosicion);
            var hP = Tb("-- Posicion --", 10, FontWeights.Bold, C_TITLE);
            hP.TextAlignment = TextAlignment.Center;
            _spPosicion.Children.Add(hP);

            _tbPosDir = Tb("---", 11, FontWeights.Bold, C_GRAY);
            _tbPosDir.TextAlignment = TextAlignment.Center;
            _tbPosDir.Margin = new Thickness(0, 2, 0, 0);
            _spPosicion.Children.Add(_tbPosDir);

            _tbPosPL = Tb("", 11, FontWeights.Bold, C_GRAY);
            _tbPosPL.TextAlignment = TextAlignment.Center;
            _spPosicion.Children.Add(_tbPosPL);

            _tbPosOrdenes = Tb("", 9, FontWeights.Normal, C_GRAY);
            _tbPosOrdenes.TextAlignment = TextAlignment.Center;
            _tbPosOrdenes.Margin = new Thickness(0, 1, 0, 2);
            _spPosicion.Children.Add(_tbPosOrdenes);

            // SECCION 1: SEMAFORO
            _sep2 = HSep(); root.Children.Add(_sep2);
            _spSemaforo = new StackPanel();
            root.Children.Add(_spSemaforo);
            var hS = Tb("-- Semaforo --", 10, FontWeights.Bold, C_TITLE);
            hS.TextAlignment = TextAlignment.Center;
            _spSemaforo.Children.Add(hS);

            var ballRow = new StackPanel { Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,5,0,3) };
            _elVerde    = Circ(20); ballRow.Children.Add(_elVerde);
            _elAmarillo = Circ(20); ballRow.Children.Add(_elAmarillo);
            _elRojo     = Circ(20); ballRow.Children.Add(_elRojo);
            _spSemaforo.Children.Add(ballRow);

            var dRow = new Grid { Margin = new Thickness(0,2,0,2) };
            dRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.55, GridUnitType.Star) });
            dRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.45, GridUnitType.Star) });
            _tbDir  = Tb("MIXTO", 12, FontWeights.Bold, C_RED);
            _tbZona = Tb("[--]",  12, FontWeights.Bold, C_GRAY);
            _tbZona.TextAlignment = TextAlignment.Right;
            Grid.SetColumn(_tbDir, 0); Grid.SetColumn(_tbZona, 1);
            dRow.Children.Add(_tbDir); dRow.Children.Add(_tbZona);
            _spSemaforo.Children.Add(dRow);

            // SECCION 2: 4 SENALES
            _sep3 = HSep(); root.Children.Add(_sep3);
            _spSenales = new StackPanel(); root.Children.Add(_spSenales);
            _tbHdr2 = Tb("-- Senales 4/4 . Zona -- --", 10, FontWeights.Bold, C_TITLE);
            _tbHdr2.TextAlignment = TextAlignment.Center;
            _spSenales.Children.Add(_tbHdr2);
            string[] sNames = { "SMA 20", "SMA 80", "LinReg 89", "AVWAP", "AVWAP vs BM", "LR89 vs BM" };
            for (int i = 0; i < 6; i++)
            {
                var row = new Grid { Margin = new Thickness(0,1,0,1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                _sigRect[i] = new Rectangle { Width = 5, Height = 8, Fill = C_GRAY, VerticalAlignment = VerticalAlignment.Center };
                _sigLbl[i]  = Tb(sNames[i], 10, FontWeights.Normal, C_GRAY);
                _sigLbl[i].Margin = new Thickness(4,0,0,0);
                _sigVal[i]  = Tb("", 10, FontWeights.Normal, C_GRAY);
                _sigVal[i].TextAlignment = TextAlignment.Right;
                Grid.SetColumn(_sigRect[i], 0); Grid.SetColumn(_sigLbl[i], 1); Grid.SetColumn(_sigVal[i], 2);
                row.Children.Add(_sigRect[i]); row.Children.Add(_sigLbl[i]); row.Children.Add(_sigVal[i]);
                _spSenales.Children.Add(row);
            }

            // SECCION 3: ZONA ENTRADA
            _sep4 = HSep(); root.Children.Add(_sep4);
            _spEntrada = new StackPanel(); root.Children.Add(_spEntrada);
            var hE = Tb("-- Zona Entrada (AVWAP) --", 10, FontWeights.Bold, C_TITLE);
            hE.TextAlignment = TextAlignment.Center;
            _spEntrada.Children.Add(hE);
            // Fila: badge estado + timestamp
            var badgeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0,2,0,2) };
            _tbEntradaBadge = Tb("[ ACTIVA ]", 10, FontWeights.Bold, C_GREEN);
            _tbEntradaBadge.Margin = new Thickness(0,0,8,0);
            _tbEntradaTS = Tb("", 9, FontWeights.Normal, C_GRAY);
            badgeRow.Children.Add(_tbEntradaBadge);
            badgeRow.Children.Add(_tbEntradaTS);
            _spEntrada.Children.Add(badgeRow);
            _tbEntrada = ERow(_spEntrada, "Entrada:", C_CYAN);
            (_tbAVWAPLbl, _tbAVWAP) = ERow2(_spEntrada, "AVWAP:", C_GRAY);
            _tbStop    = ERow(_spEntrada, "Stop:",    C_RED);
            _tbT1      = ERow(_spEntrada, "T1:",      C_GREEN);
            _tbT2      = ERow(_spEntrada, "T2:",      C_ORANGE);

            // SECCION 4: KELTNER
            _sep5 = HSep(); root.Children.Add(_sep5);
            _spKeltner = new StackPanel(); root.Children.Add(_spKeltner);
            var hK = Tb("-- Keltner Stop / Target --", 10, FontWeights.Bold, C_TITLE);
            hK.TextAlignment = TextAlignment.Center;
            _spKeltner.Children.Add(hK);
            (_tbKWidthLbl, _tbKWidth) = KRow(_spKeltner, "Width:",  C_CYAN);
            (_tbKStopLbl,  _tbKStop)  = KRow(_spKeltner, "Stop:",   C_RED);
            (_tbKTgtLbl,   _tbKTgt)   = KRow(_spKeltner, "Target:", C_GREEN);
        }

        // ── Update semaforo + señales + entrada + keltner ────────
        public void Update(HudSnap2 s)
        {
            // La seccion entrada ahora se muestra si hay alguna señal guardada (activa o historica)
            bool showE = s.MostrarEntrada && s.UltimaEntradaValida;

            _spPosicion.Visibility = s.MostrarPosicion  ? Visibility.Visible : Visibility.Collapsed;
            _sep2.Visibility       = (s.MostrarPosicion && s.MostrarSemaforo) ? Visibility.Visible : Visibility.Collapsed;
            _spSemaforo.Visibility = s.MostrarSemaforo  ? Visibility.Visible : Visibility.Collapsed;
            _sep3.Visibility       = (s.MostrarSemaforo && s.MostrarSenales) ? Visibility.Visible : Visibility.Collapsed;
            _spSenales.Visibility  = s.MostrarSenales   ? Visibility.Visible : Visibility.Collapsed;
            _sep4.Visibility       = (showE && (s.MostrarSemaforo || s.MostrarSenales)) ? Visibility.Visible : Visibility.Collapsed;
            _spEntrada.Visibility  = showE              ? Visibility.Visible : Visibility.Collapsed;
            _sep5.Visibility       = s.MostrarKeltner   ? Visibility.Visible : Visibility.Collapsed;
            _spKeltner.Visibility  = s.MostrarKeltner   ? Visibility.Visible : Visibility.Collapsed;

            _tbTitle.Text = "EduS . " + s.Instrumento;

            if (s.MostrarSemaforo)
            {
                bool esV = s.Estado == MasterEstado_HUD2.VerdeAlcista    || s.Estado == MasterEstado_HUD2.VerdeBajista;
                bool esA = s.Estado == MasterEstado_HUD2.AmarilloAlcista || s.Estado == MasterEstado_HUD2.AmarilloBajista;
                bool esR = s.Estado == MasterEstado_HUD2.Rojo;
                _elVerde.Fill    = esV ? C_GREEN  : C_DIM;
                _elAmarillo.Fill = esA ? C_YELLOW : C_DIM;
                _elRojo.Fill     = esR ? C_RED    : C_DIM;
                switch (s.Estado)
                {
                    case MasterEstado_HUD2.VerdeAlcista:    _tbDir.Text = "ALCISTA"; _tbDir.Foreground = C_GREEN;  break;
                    case MasterEstado_HUD2.VerdeBajista:    _tbDir.Text = "BAJISTA"; _tbDir.Foreground = C_GREEN;  break;
                    case MasterEstado_HUD2.AmarilloAlcista: _tbDir.Text = "ALCISTA"; _tbDir.Foreground = C_YELLOW; break;
                    case MasterEstado_HUD2.AmarilloBajista: _tbDir.Text = "BAJISTA"; _tbDir.Foreground = C_YELLOW; break;
                    default:                               _tbDir.Text = "MIXTO";   _tbDir.Foreground = C_RED;    break;
                }
                SolidColorBrush zb;
                switch (s.Zona)
                {
                    case ZonaGrado_HUD2.APlus: zb = C_GREEN;  break;
                    case ZonaGrado_HUD2.A:     zb = C_GREEN;  break;
                    case ZonaGrado_HUD2.BPlus: zb = C_YELLOW; break;
                    case ZonaGrado_HUD2.B:     zb = C_ORANGE; break;
                    default:                   zb = C_GRAY;   break;
                }
                _tbZona.Text = "[" + s.ZonaLabel + "]";
                _tbZona.Foreground = zb;
            }

            if (s.MostrarSenales)
            {
                _tbHdr2.Text = "-- Senales  " + s.TendenciaCount + "/4 . Zona " + s.ZonaLabel + " --";
                bool[]   ups = { s.SMA20Up, s.SMA80Up, s.LRUp, s.VWAPUp };
                double[] vs  = { s.SMA20Val, s.SMA80Val, s.LRVal, s.VWAPVal };
                for (int i = 0; i < 4; i++)
                {
                    _sigRect[i].Fill      = ups[i] ? C_GREEN : C_RED;
                    _sigLbl[i].Foreground = C_GRAY;
                    _sigVal[i].Text       = (ups[i] ? "+ " : "- ") + vs[i].ToString("0.00");
                    _sigVal[i].Foreground = ups[i] ? C_GREEN : C_RED;
                }
                // Fila 4: AVWAP vs BM (Capa 3)
                string bmLbl = s.VWAPUp ? "AVWAP > BM Kelt" : "AVWAP < BM Kelt";
                _sigRect[4].Fill      = s.AVWAPsobreBM ? C_GREEN : C_YELLOW;
                _sigLbl[4].Text       = bmLbl + (s.AVWAPsobreBM ? " OK" : " --");
                _sigLbl[4].Foreground = s.AVWAPsobreBM ? C_GREEN : C_YELLOW;
                _sigVal[4].Text       = "BM=" + s.KeltnerBM.ToString("0.00");
                _sigVal[4].Foreground = C_GRAY;
                // Fila 5: LR89 vs BM (Capa 4)
                string lrBmLbl = s.LRUp ? "LR89 > BM Kelt" : "LR89 < BM Kelt";
                _sigRect[5].Fill      = s.LRsobreBM ? C_GREEN : C_YELLOW;
                _sigLbl[5].Text       = lrBmLbl + (s.LRsobreBM ? " OK" : " --");
                _sigLbl[5].Foreground = s.LRsobreBM ? C_GREEN : C_YELLOW;
                _sigVal[5].Text       = "LR=" + s.LRVal2.ToString("0.00");
                _sigVal[5].Foreground = C_GRAY;
            }

            if (showE)
            {
                // Badge: ACTIVA (verde) o HISTORICA (naranja/gris)
                if (s.UltimaEntradaActiva)
                {
                    _tbEntradaBadge.Text       = "[ ACTIVA ]";
                    _tbEntradaBadge.Foreground = C_GREEN;
                    _tbEntradaTS.Foreground    = C_GRAY;
                }
                else
                {
                    _tbEntradaBadge.Text       = "[ HISTORICA ]";
                    _tbEntradaBadge.Foreground = C_ORANGE;
                    _tbEntradaTS.Foreground    = C_GRAY;
                }
                _tbEntradaTS.Text = s.UltimaFechaHora + "  [" + s.UltimaZonaLabel + "]";

                bool alc      = s.UltimaEsAlcista;
                bool esLR     = s.UltimaEsZonaLR;
                string refSuf = esLR ? (alc ? " (soporte LR)" : " (resist. LR)") : (alc ? " (soporte)" : " (resist.)");
                _tbAVWAPLbl.Text = esLR ? "LinReg:" : "AVWAP:";
                _tbEntrada.Text = s.UltimaEntrada.ToString("0.00");
                _tbAVWAP.Text   = s.UltimaAVWAP.ToString("0.00") + refSuf;
                _tbStop.Text    = s.UltimaStop.ToString("0.00") + "  (" + (alc ? "-" : "+") + s.UltimaStopPts.ToString("0.00") + " pts)";
                _tbT1.Text      = s.UltimaT1.ToString("0.00") + "  (" + (alc ? "+" : "-") + s.UltimaT1Pts.ToString("0.00") + " pts)  R:R " + s.UltimaRR1.ToString("0.1");
                _tbT2.Text      = s.UltimaT2.ToString("0.00") + "  (" + (alc ? "+" : "-") + s.UltimaT2Pts.ToString("0.00") + " pts)  R:R " + s.UltimaRR2.ToString("0.1");
                // Actualizar label de la fila AVWAP/LinReg segun zona
                // (el label está en el texto fijo del ERow, lo sobreescribimos via tag)
            }

            if (s.MostrarKeltner)
            {
                _tbKStopLbl.Text = "Stop " + (s.KStopPct * 100).ToString("0") + "%:";
                _tbKTgtLbl.Text  = "T " + s.KRR.ToString("0.0") + ":1:";
                _tbKWidth.Text   = s.BandWidth.ToString("0.00") + " pts";
                _tbKStop.Text    = s.KStopPts.ToString("0.00") + " pts  " + s.KStopTks.ToString("0") + " tks  $" + s.KStopUsd.ToString("0.00");
                _tbKTgt.Text     = s.KTargetPts.ToString("0.00") + " pts  " + s.KTargetTks.ToString("0") + " tks  $" + s.KTargetUsd.ToString("0.00");
            }
        }

        // ── Update posicion (llamado desde DispatcherTimer) ──────
        public void UpdatePosition(PosSnap p)
        {
            // Caso: error o mensaje especial en Etiqueta sin posicion
            if (!p.TienePos && !string.IsNullOrEmpty(p.Etiqueta))
            {
                _tbPosDir.Text       = p.Etiqueta;
                _tbPosDir.Foreground = p.Etiqueta.StartsWith("ERR") ? C_RED : C_GRAY;
                _tbPosPL.Text        = "";
                _tbPosOrdenes.Text   = "";
                return;
            }

            if (!p.TienePos)
            {
                _tbPosDir.Text       = "No Position";
                _tbPosDir.Foreground = C_GRAY;
                _tbPosPL.Text        = "";
                _tbPosOrdenes.Text   = "";
                return;
            }

            // Linea 1: direccion + cantidad + precio
            string dir = p.EsLong ? "LONG" : "SHORT";
            _tbPosDir.Text       = dir + " " + p.Cantidad + " @ " + p.PrecioEntrada.ToString("0.00");
            _tbPosDir.Foreground = p.EsLong ? C_LIME : C_ORANG2;

            // Linea 2: P&L
            string signo  = p.PLdolar >= 0 ? "+" : "-";
            string signoPts = p.PLpuntos >= 0 ? "+" : "-";
            _tbPosPL.Text       = signo + "$" + Math.Abs(p.PLdolar).ToString("N0")
                                + "  (" + signoPts + Math.Abs(p.PLpuntos).ToString("0.00") + " pts)";
            _tbPosPL.Foreground = Math.Abs(p.PLdolar) < 0.01 ? C_GRAY
                                : p.PLdolar > 0               ? C_GREEN
                                :                               C_RED;

            // Linea 3: ordenes stop / target / R:R
            string txt = "";
            if (p.GananciaAsegurada && p.TieneStop)
                txt += "Locked $" + p.StopRisk.ToString("N0");
            else if (p.TieneStop)
                txt += "Risk $" + p.StopRisk.ToString("N0");
            else
                txt += "Risk: Sin stop";

            if (p.RiesgoIlimitado && p.TieneStop)
                txt += "(U)";

            if (p.TieneTarget)
                txt += "  |  T $" + p.TargetProfit.ToString("N0");

            if (p.RR > 0)
                txt += "  |  R:R " + p.RR.ToString("0.1") + ":1";

            if (!string.IsNullOrEmpty(p.Etiqueta))
                txt = "[" + p.Etiqueta + "] " + txt;

            _tbPosOrdenes.Text       = txt;
            _tbPosOrdenes.Foreground = C_GRAY;
        }

        // ── Helpers WPF ──────────────────────────────────────────
        private static TextBlock Tb(string t, double sz, FontWeight w, SolidColorBrush fg) =>
            new TextBlock { Text = t, FontSize = sz, FontWeight = w, Foreground = fg, VerticalAlignment = VerticalAlignment.Center };

        private static Ellipse Circ(double d) =>
            new Ellipse { Width = d, Height = d, Fill = C_DIM, Stroke = C_BORDER, StrokeThickness = 1.2, Margin = new Thickness(4,0,4,0) };

        private static Border HSep() =>
            new Border { Height = 1, Background = C_BORDER, Margin = new Thickness(0,3,0,3) };

        private static TextBlock ERow(StackPanel sp, string lbl, SolidColorBrush vc)
        {
            var row = new Grid { Margin = new Thickness(0,1,0,1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tL = Tb(lbl, 10, FontWeights.Normal, C_GRAY);
            var tV = Tb("---", 10, FontWeights.Normal, vc);
            Grid.SetColumn(tL, 0); Grid.SetColumn(tV, 1);
            row.Children.Add(tL); row.Children.Add(tV);
            sp.Children.Add(row);
            return tV;
        }

        private static (TextBlock lbl, TextBlock val) ERow2(StackPanel sp, string lbl, SolidColorBrush vc)
        {
            var row = new Grid { Margin = new Thickness(0,1,0,1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tL = Tb(lbl, 10, FontWeights.Normal, C_GRAY);
            var tV = Tb("---", 10, FontWeights.Normal, vc);
            Grid.SetColumn(tL, 0); Grid.SetColumn(tV, 1);
            row.Children.Add(tL); row.Children.Add(tV);
            sp.Children.Add(row);
            return (tL, tV);
        }

        private static (TextBlock lbl, TextBlock val) KRow(StackPanel sp, string lbl, SolidColorBrush vc)
        {
            var row = new Grid { Margin = new Thickness(0,1,0,1) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tL = Tb(lbl, 10, FontWeights.Normal, C_GRAY);
            var tV = Tb("---", 10, FontWeights.Normal, vc);
            Grid.SetColumn(tL, 0); Grid.SetColumn(tV, 1);
            row.Children.Add(tL); row.Children.Add(tV);
            sp.Children.Add(row);
            return (tL, tV);
        }
    }

    // ================================================================
    //   EduS_MasterPanel_HUD_V2 -- Indicador principal
    // ================================================================
    public class EduS_MasterPanel_HUD_V2 : Indicator
    {
        // ── Indicadores NT ───────────────────────────────────────
        private NinjaTrader.NinjaScript.Indicators.SMA    sma20, sma80;
        private NinjaTrader.NinjaScript.Indicators.LinReg lr89;
        private NinjaTrader.NinjaScript.Indicators.ATR    atrInd;
        private EduS_Trader.EduS_DynamicSwingAVWAP        avwap;
        private NinjaTrader.NinjaScript.Series<double>    kDiff;
        private NinjaTrader.NinjaScript.Indicators.EMA    kEmaDiff, kEmaTypical;

        // ── Estado semaforo ──────────────────────────────────────
        private MasterEstado_HUD2 estadoActual = MasterEstado_HUD2.Rojo;
        private bool   v_sma20Up, v_sma80Up, v_lrUp, v_vwapUp;
        private double v_sma20Val, v_sma80Val, v_lrVal, v_vwapVal;
        private int    tendenciaCount = 0;
        private ZonaGrado_HUD2 zonaActual = ZonaGrado_HUD2.Ninguna;
        private string         zonaLabel  = "--";
        private double         atrVal     = 0;
        private bool   v_avwapSobreBM = false;
        private bool   v_lrSobreBM    = false;
        private double v_keltnerBM    = 0;
        private double k_bandWidth = 0, k_stopPts = 0, k_targetPts = 0;
        private double k_stopTks = 0, k_targetTks = 0, k_stopUsd = 0, k_targetUsd = 0;
        private double e_entrada = double.NaN, e_avwap = double.NaN;
        private double e_stop = double.NaN, e_stopPts = 0;
        private double e_t1 = double.NaN, e_t1Pts = 0, e_rr1 = 0;
        private double e_t2 = double.NaN, e_t2Pts = 0, e_rr2 = 0;

        // ── SharpDX overlay ──────────────────────────────────────
        private SharpDX.Direct2D1.SolidColorBrush dxFondo, dxBorde, dxTitulo, dxGris;
        private SharpDX.Direct2D1.SolidColorBrush dxVerde, dxAmarillo, dxRojo, dxApagado;
        private SharpDX.Direct2D1.SolidColorBrush dxCian, dxNaranja, dxV2Tag;
        private SharpDX.DirectWrite.TextFormat fmtHeader, fmtNormal, fmtBold, fmtMono, fmtSmall;
        private bool dxReady = false;
        private const float PAD_H = 10f, PAD_V = 7f, ROW_H = 13f, SEC_GAP = 3f, MARGIN = 8f;

        // ── WPF HUD ──────────────────────────────────────────────
        private System.Windows.Window  _ownWindow;
        private MasterHudPanel2        _ownPanel;
        private bool                   _ownWindowClosed = false;
        private static System.Windows.Window          s_sharedWindow;
        private static StackPanel                     s_sharedStack;
        private static readonly Dictionary<string, MasterHudPanel2> s_sharedPanels
            = new Dictionary<string, MasterHudPanel2>();
        private string _instrKey = "";

        // ── Posicion: cuenta y timer ─────────────────────────────
        private Account         _account;
        private DispatcherTimer _posTimer;

        // ── Logging y V5 ─────────────────────────────────────
        private bool   prevEntradaValida = false;
        private string logDirectory;
        private string logFilePath;
        private readonly object logLock = new object();
        private List<SignalActivo> señalesActivas = new List<SignalActivo>();
        private int prevTendenciaCount = 0;
        private ZonaGrado_HUD2 prevZona = ZonaGrado_HUD2.Ninguna;
        private int barrasDesdeSenal = 0;
        private bool senalPendiente = false;

        // ── Ultima señal persistente (Opcion B) ─────────────────
        private bool   _ultimaEntradaValida  = false;
        private bool   _ultimaEsAlcista      = false;
        private bool   _ultimaEsZonaLR       = false;
        private string _ultimaFechaHora      = "";
        private string _ultimaZonaLabel      = "";
        private double _ultimaEntrada        = double.NaN;
        private double _ultimaAVWAP          = double.NaN;
        private double _ultimaStop           = double.NaN, _ultimaStopPts = 0;
        private double _ultimaT1             = double.NaN, _ultimaT1Pts = 0, _ultimaRR1 = 0;
        private double _ultimaT2             = double.NaN, _ultimaT2Pts = 0, _ultimaRR2 = 0;

        // ================================================================
        #region OnStateChange
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description              = "EduS Master Panel HUD V3 -- ventana flotante con seccion de posicion en vivo.";
                Name                     = "EduS_MasterPanel_HUD_V3";
                Calculate                = Calculate.OnBarClose;
                IsOverlay                = true;
                DisplayInDataBox         = false;
                DrawOnPricePanel         = true;
                IsSuspendedWhileInactive = true;
                ScaleJustification       = ScaleJustification.Right;

                Ventana_Flotante   = true;
                Ventana_Compartida = true;
                Hud_X              = 20;
                Hud_Y              = 20;
                Mostrar_En_Chart   = false;

                Mostrar_Posicion   = true;
                AccountName        = "Sim101";
                Pos_UpdateSeg      = 3;
                RegistrarSenales   = true;
                RutaLog            = @"C:\Users\eduar\OneDrive - Desarrollo Personal\Documents - Operativa Diaria\EduSTrader - Local Free";
                NombreArchivoLog   = "senales-edus.csv";
                LoggearHistorico   = false;
                Alerta4de4         = true;
                Sonido4de4         = "Alert1.wav";
                AlertaZonaA        = true;
                SonidoZonaA        = "Alert2.wav";
                UsarCapa3          = false;
                UsarCapa4          = false;
                UsarStopKeltner    = true;
                FiltroApertura     = true;
                MinKeltnerVsATR    = 1.8;
                EsperarConfirmacion= true;
                BarrasConfirmacion = 2;

                Mostrar_Semaforo   = true;
                Mostrar_Senales    = true;
                Mostrar_Entrada    = true;
                Mostrar_Keltner    = true;

                Period_SMA20       = 20;
                Period_SMA80       = 80;
                Period_LR89        = 89;
                SlopeLookback      = 3;
                AVWAP_SwingPeriod  = 50;
                AVWAP_BaseAPT      = 20;
                ATR_Period         = 14;

                ZoneVerde          = 0.5;
                ZoneAmarilla       = 2.0;
                Stop_ATRMult       = 0.5;

                Kelt_Period        = 52;
                Kelt_Multiplier    = 3.5;
                Kelt_StopPct       = 0.30;
                Kelt_RiskReward    = 2.0;
                Kelt_Contracts     = 1;

                Tamano_Bolas       = 15;
                Fondo_Opacidad     = 75;
                Panel_Posicion     = 1;
                Margen_Escala      = 70;
            }
            else if (State == State.Configure)
            {
                sma20       = SMA(Period_SMA20);
                sma80       = SMA(Period_SMA80);
                lr89        = LinReg(Input, Period_LR89);
                atrInd      = ATR(ATR_Period);
                avwap       = EduS_DynamicSwingAVWAP(AVWAP_SwingPeriod, AVWAP_BaseAPT, true, 10.0, 3, 1, 15, true, 3.0, " ");
                kDiff       = new NinjaTrader.NinjaScript.Series<double>(this);
                kEmaDiff    = EMA(kDiff, Kelt_Period);
                kEmaTypical = EMA(Typical, Kelt_Period);
            }
            else if (State == State.Historical)
            {
                // Cuenta se resuelve en hilo UI al abrir la ventana
            }
            else if (State == State.DataLoaded)
            {
                string instr = Instrument != null ? Instrument.MasterInstrument.Name : "---";
                _instrKey = instr;
                if (RegistrarSenales)
                {
                    try
                    {
                        logDirectory = string.IsNullOrWhiteSpace(RutaLog) ? System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "EduS_Logs") : RutaLog;
                        System.IO.Directory.CreateDirectory(logDirectory);
                        string fileName = string.IsNullOrWhiteSpace(NombreArchivoLog) ? "Senales_EduS.csv" : NombreArchivoLog;
                        if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) fileName += ".csv";
                        logFilePath = System.IO.Path.Combine(logDirectory, fileName);
                        lock (logLock) { if (!System.IO.File.Exists(logFilePath)) { System.IO.File.WriteAllText(logFilePath, "Tipo,SignalID,FechaHora,Mercado,Cuenta,Timeframe,Direccion,Estado,Zona,Entrada,Stop,T1,T2,RR1,RR2,FechaCierre,Resultado,T1_Tocado,PrecioSalida,BarrasDuracion" + Environment.NewLine); } }
                    } catch { }
                }

                if (Ventana_Flotante)
                {
                    // El timer se inicia DENTRO de AbrirVentana*, garantizando que
                    // _ownPanel ya este asignado cuando el primer tick llegue.
                    if (Ventana_Compartida) AbrirVentanaCompartida(instr);
                    else                    AbrirVentanaPropia(instr);
                }
            }
            else if (State == State.Terminated)
            {
                // Detener timer en hilo UI (fue creado en hilo UI)
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (_posTimer != null) { _posTimer.Stop(); _posTimer = null; }
                });
                CerrarHUD();
                DisposeDXResources();
            }
        }
        #endregion

        // ── Resolucion de cuenta ─────────────────────────────────
        private void ResolverCuenta()
        {
            // Debe llamarse desde el hilo UI
            _account = null;
            if (!string.IsNullOrEmpty(AccountName))
                _account = Account.All.FirstOrDefault(a => a.Name == AccountName);
        }

        // Crea y arranca el timer en el hilo UI actual.
        // Llamar SIEMPRE desde dentro de un InvokeAsync de Application.Current.Dispatcher.
        private void IniciarPosTimer()
        {
            if (!Mostrar_Posicion) return;

            // Parar timer anterior si existe
            if (_posTimer != null) { _posTimer.Stop(); _posTimer = null; }

            // Resolver cuenta ahora que estamos en hilo UI
            ResolverCuenta();

            _posTimer = new DispatcherTimer(
                TimeSpan.FromSeconds(Math.Max(1, Pos_UpdateSeg)),
                DispatcherPriority.Normal,
                PosTimer_Tick,
                System.Windows.Application.Current.Dispatcher);
            _posTimer.Start();

            // Primer tick inmediato (sin esperar el intervalo)
            PosTimer_Tick(null, null);
        }

        // ── Timer posicion ───────────────────────────────────────
        private void PosTimer_Tick(object sender, EventArgs e)
        {
            if (_ownPanel == null || _ownWindowClosed) return;
            try
            {
                // Re-resolver cuenta si cambió o no se encontró antes
                if (_account == null
                    || (!string.IsNullOrEmpty(AccountName)
                        && _account.Name != AccountName))
                {
                    ResolverCuenta();
                }

                PosSnap snap = BuildPosSnap();
                _ownPanel.UpdatePosition(snap);
            }
            catch (Exception ex)
            {
                // Mostrar error en el panel en lugar de ignorarlo silenciosamente
                try
                {
                    _ownPanel.UpdatePosition(new PosSnap
                    {
                        TienePos  = false,
                        Etiqueta  = "ERR: " + ex.Message.Substring(0, Math.Min(40, ex.Message.Length))
                    });
                }
                catch { }
            }
        }

        private PosSnap BuildPosSnap()
        {
            var snap = new PosSnap();

            if (_account == null)
            {
                snap.TienePos = false;
                snap.Etiqueta = string.IsNullOrEmpty(AccountName)
                    ? "Configura AccountName"
                    : "Cuenta no encontrada";
                return snap;
            }

            // Buscar posicion del instrumento de este chart
            string instrName = Instrument != null ? Instrument.MasterInstrument.Name : "";
            Position pos = null;
            try
            {
                pos = _account.Positions.FirstOrDefault(p =>
                    p.Instrument.MasterInstrument.Name == instrName &&
                    p.MarketPosition != MarketPosition.Flat);
            }
            catch { return snap; }

            if (pos == null) return snap;

            snap.TienePos      = true;
            snap.EsLong        = pos.MarketPosition == MarketPosition.Long;
            snap.Cantidad      = Math.Abs(pos.Quantity);
            snap.PrecioEntrada = pos.AveragePrice;
            snap.InstrFull     = pos.Instrument.FullName;

            try
            {
                snap.PLdolar  = pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
                double pv     = pos.Instrument.MasterInstrument.PointValue;
                snap.PLpuntos = pv > 0 ? snap.PLdolar / Math.Abs(pos.Quantity) / pv : 0;
            }
            catch { }

            // Analizar ordenes stop / target activas
            try
            {
                var orders = _account.Orders
                    .Where(o => o.Instrument == pos.Instrument
                             && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted)
                             && (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit
                              || o.OrderType == OrderType.Limit))
                    .ToList();

                double pv         = pos.Instrument.MasterInstrument.PointValue;
                double totalStopP = 0, totalStopQ = 0;
                double totalTgtP  = 0, totalTgtQ  = 0;
                bool   hayATM     = false;

                foreach (var o in orders)
                {
                    // Si la orden pertenece a una estrategia ATM
                    if (!string.IsNullOrEmpty(o.Name) && o.Name.StartsWith("ATM", StringComparison.OrdinalIgnoreCase))
                        hayATM = true;

                    if (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)
                    {
                        double sp = o.StopPrice > 0 ? o.StopPrice : o.LimitPrice;
                        totalStopP += sp * o.Quantity;
                        totalStopQ += o.Quantity;
                    }
                    else if (o.OrderType == OrderType.Limit)
                    {
                        totalTgtP += o.LimitPrice * o.Quantity;
                        totalTgtQ += o.Quantity;
                    }
                }

                if (totalStopQ > 0)
                {
                    snap.TieneStop = true;
                    double avgStop = totalStopP / totalStopQ;
                    if (snap.EsLong)
                        snap.StopRisk = Math.Abs(pos.AveragePrice - avgStop) * totalStopQ * pv;
                    else
                        snap.StopRisk = Math.Abs(avgStop - pos.AveragePrice) * totalStopQ * pv;

                    // Stop locking profit?
                    snap.GananciaAsegurada = snap.EsLong
                        ? (avgStop > pos.AveragePrice)
                        : (avgStop < pos.AveragePrice);

                    // Riesgo ilimitado si el stop no cubre toda la posicion
                    snap.RiesgoIlimitado = totalStopQ < Math.Abs(pos.Quantity);
                }

                if (totalTgtQ > 0)
                {
                    snap.TieneTarget = true;
                    double avgTgt = totalTgtP / totalTgtQ;
                    if (snap.EsLong)
                        snap.TargetProfit = Math.Abs(avgTgt - pos.AveragePrice) * totalTgtQ * pv;
                    else
                        snap.TargetProfit = Math.Abs(pos.AveragePrice - avgTgt) * totalTgtQ * pv;
                }

                if (snap.StopRisk > 0.01 && snap.TargetProfit > 0.01)
                    snap.RR = snap.TargetProfit / snap.StopRisk;

                snap.Etiqueta = hayATM ? "ATM" : (orders.Count > 0 ? "Manual" : "");
            }
            catch { }

            return snap;
        }

        // ================================================================
        #region OnBarUpdate
        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;
            int minBars = Math.Max(Math.Max(Period_SMA20, Period_SMA80),
                          Math.Max(Math.Max(Period_LR89,  Kelt_Period),
                          Math.Max(ATR_Period, SlopeLookback + 1)));
            if (CurrentBar < minBars) return;

            // Capa 1
            v_sma20Up = sma20[0] > sma20[SlopeLookback];
            v_sma80Up = sma80[0] > sma80[SlopeLookback];
            v_lrUp    = lr89[0]  > lr89[SlopeLookback];
            v_sma20Val = sma20[0]; v_sma80Val = sma80[0]; v_lrVal = lr89[0];

            bool au = !double.IsNaN(avwap.VWAP_Up[0]);
            bool ad = !double.IsNaN(avwap.VWAP_Down[0]);
            if      (au && !ad) { v_vwapUp = true;  v_vwapVal = avwap.VWAP_Up[0]; }
            else if (ad && !au) { v_vwapUp = false; v_vwapVal = avwap.VWAP_Down[0]; }
            else if (au && ad)  { v_vwapUp = avwap.VWAP_Up[0] > avwap.VWAP_Down[0];
                                  v_vwapVal = v_vwapUp ? avwap.VWAP_Up[0] : avwap.VWAP_Down[0]; }
            else                { v_vwapUp = false; v_vwapVal = Close[0]; }

            // Capa 2 — AVWAP zones (A+, A) y LinReg zones (LR+, LR)
            atrVal = Math.Max(atrInd[0], TickSize);
            double vr  = v_vwapVal,  pr = Close[0];
            double lr  = lr89[0];
            double dA  = Math.Abs(pr - vr) / atrVal;
            double dLR = Math.Abs(pr - lr)  / atrVal;
            // AVWAP pins/side
            bool pinL  = v_vwapUp  && Low[0]  <= vr && pr > vr;
            bool pinS  = !v_vwapUp && High[0] >= vr && pr < vr;
            bool ladoA = v_vwapUp ? (pr >= vr) : (pr <= vr);
            // LinReg pins/side
            bool pinLRL = v_lrUp  && Low[0]  <= lr && pr > lr;
            bool pinLRS = !v_lrUp && High[0] >= lr && pr < lr;
            bool ladoLR = v_lrUp  ? (pr >= lr) : (pr <= lr);

            if      (pinL  || pinS)               { zonaActual = ZonaGrado_HUD2.APlus;   zonaLabel = "A+";  }
            else if (ladoA  && dA  <= ZoneVerde)  { zonaActual = ZonaGrado_HUD2.A;       zonaLabel = "A";   }
            else if (pinLRL || pinLRS)             { zonaActual = ZonaGrado_HUD2.LRPlus;  zonaLabel = "LR+"; }
            else if (ladoLR && dLR <= ZoneVerde)  { zonaActual = ZonaGrado_HUD2.LR;      zonaLabel = "LR";  }
            else if (!ladoA && dA  <= ZoneVerde)  { zonaActual = ZonaGrado_HUD2.BPlus;   zonaLabel = "B+";  }
            else if (ladoA  && dA  <= ZoneAmarilla){ zonaActual = ZonaGrado_HUD2.B;      zonaLabel = "B";   }
            else                                  { zonaActual = ZonaGrado_HUD2.Ninguna; zonaLabel = "--";  }

            // Keltner
            kDiff[0]    = High[0] - Low[0];
            double kMid = kEmaTypical[0];
            double kOff = kEmaDiff[0] * Kelt_Multiplier;
            k_bandWidth  = kMid + kOff - (kMid - kOff);
            k_stopPts    = k_bandWidth * Kelt_StopPct;
            k_targetPts  = k_stopPts * Kelt_RiskReward;
            double ts    = Instrument.MasterInstrument.TickSize;
            double pv    = Instrument.MasterInstrument.PointValue;
            k_stopTks    = k_stopPts / ts;   k_targetTks = k_targetPts / ts;
            k_stopUsd    = k_stopPts * pv * Kelt_Contracts;
            k_targetUsd  = k_targetPts * pv * Kelt_Contracts;
            v_keltnerBM  = kMid;

            // Capa 3: AVWAP vs BM Keltner
            v_avwapSobreBM = v_vwapUp ? (v_vwapVal > kMid) : (v_vwapVal < kMid);
            // Capa 4: LinReg89 vs BM Keltner
            v_lrSobreBM    = v_lrUp   ? (lr89[0]   > kMid) : (lr89[0]   < kMid);

            // Semaforo
            bool a4a = v_sma20Up &&  v_sma80Up &&  v_lrUp &&  v_vwapUp;
            bool a4b = !v_sma20Up && !v_sma80Up && !v_lrUp && !v_vwapUp;
            bool m3a = v_sma20Up && !v_sma80Up &&  v_lrUp &&  v_vwapUp;
            bool m3b = !v_sma20Up && v_sma80Up && !v_lrUp && !v_vwapUp;

            bool zA  = zonaActual == ZonaGrado_HUD2.APlus  || zonaActual == ZonaGrado_HUD2.A;
            bool zLR = zonaActual == ZonaGrado_HUD2.LRPlus || zonaActual == ZonaGrado_HUD2.LR;
            bool zM  = zonaActual == ZonaGrado_HUD2.BPlus  || zonaActual == ZonaGrado_HUD2.B;

            // Confirmacion por zona: A/A+ usa Capa3, LR/LR+ usa Capa4
            bool capa3Ok = !UsarCapa3 || v_avwapSobreBM;
            bool capa4Ok = !UsarCapa4 || v_lrSobreBM;

            bool verdeA  = a4a && zA  && capa3Ok;
            bool verdeB  = a4b && zA  && capa3Ok;
            bool verdeLRA = a4a && zLR && capa4Ok;
            bool verdeLRB = a4b && zLR && capa4Ok;

            if      (verdeA  || verdeLRA) estadoActual = MasterEstado_HUD2.VerdeAlcista;
            else if (verdeB  || verdeLRB) estadoActual = MasterEstado_HUD2.VerdeBajista;
            // Amarillo: 4/4 + zona premium sin capa, o 4/4 + zona B/B+, o 3/4 + cualquier zona premium
            else if (a4a && zA)           estadoActual = MasterEstado_HUD2.AmarilloAlcista;
            else if (a4b && zA)           estadoActual = MasterEstado_HUD2.AmarilloBajista;
            else if (a4a && zLR)          estadoActual = MasterEstado_HUD2.AmarilloAlcista;
            else if (a4b && zLR)          estadoActual = MasterEstado_HUD2.AmarilloBajista;
            else if (a4a && zM)           estadoActual = MasterEstado_HUD2.AmarilloAlcista;
            else if (a4b && zM)           estadoActual = MasterEstado_HUD2.AmarilloBajista;
            else if (m3a && (zA || zLR))  estadoActual = MasterEstado_HUD2.AmarilloAlcista;
            else if (m3b && (zA || zLR))  estadoActual = MasterEstado_HUD2.AmarilloBajista;
            else                          estadoActual = MasterEstado_HUD2.Rojo;

            bool esAlc = v_vwapUp;
            tendenciaCount = (v_sma20Up == esAlc ? 1 : 0) + (v_sma80Up == esAlc ? 1 : 0)
                           + (v_lrUp    == esAlc ? 1 : 0) + 1;

            // Zona Entrada - V5 con Keltner y filtros
            bool esVerde = estadoActual == MasterEstado_HUD2.VerdeAlcista || estadoActual == MasterEstado_HUD2.VerdeBajista;
            double kUp = kMid + kOff, kLo = kMid - kOff;
            
            // V5: control de confirmación
            if (esVerde) { if (!senalPendiente) { senalPendiente = true; barrasDesdeSenal = 0; } else { barrasDesdeSenal++; } }
            else { senalPendiente = false; barrasDesdeSenal = 0; }
            
            bool pasaFiltroAncho = !FiltroApertura || (k_bandWidth >= MinKeltnerVsATR * atrVal);
            bool pasaConfirmacion = !EsperarConfirmacion || (barrasDesdeSenal >= BarrasConfirmacion);
            
            if (esVerde && pasaFiltroAncho && pasaConfirmacion)
            {
                bool alc = estadoActual == MasterEstado_HUD2.VerdeAlcista;
                e_entrada = Close[0];
                e_avwap = alc ? avwap.VWAP_Up[0] : avwap.VWAP_Down[0];
                
                // V5: Stop por Keltner (30%) o por ATR según propiedad
                if (UsarStopKeltner)
                {
                    e_stopPts = k_stopPts; // ya es 30% del ancho
                    e_stop = alc ? e_entrada - e_stopPts : e_entrada + e_stopPts;
                    e_t1Pts = e_stopPts * 2.0; // RR 1:2
                    e_t2Pts = e_stopPts * 3.0; // RR 1:3
                    e_t1 = alc ? e_entrada + e_t1Pts : e_entrada - e_t1Pts;
                    e_t2 = alc ? e_entrada + e_t2Pts : e_entrada - e_t2Pts;
                }
                else // lógica original ATR
                {
                    if (alc && !double.IsNaN(e_avwap)) { e_stop = e_avwap - atrVal * Stop_ATRMult; e_stopPts = Math.Max(0, e_entrada - e_stop); e_t1 = kUp; e_t1Pts = Math.Max(0, e_t1 - e_entrada); e_t2 = kUp + Math.Max(0, kUp - e_avwap); e_t2Pts = Math.Max(0, e_t2 - e_entrada); }
                    else if (!alc && !double.IsNaN(e_avwap)) { e_stop = e_avwap + atrVal * Stop_ATRMult; e_stopPts = Math.Max(0, e_stop - e_entrada); e_t1 = kLo; e_t1Pts = Math.Max(0, e_entrada - e_t1); e_t2 = kLo - Math.Max(0, e_avwap - kLo); e_t2Pts = Math.Max(0, e_entrada - e_t2); }
                    else { e_entrada = e_avwap = e_stop = e_t1 = e_t2 = double.NaN; e_stopPts = e_t1Pts = e_t2Pts = 0; }
                }
                if (e_stopPts > ts) { e_rr1 = e_t1Pts / e_stopPts; e_rr2 = e_t2Pts / e_stopPts; } else { e_rr1 = e_rr2 = 0; }
            }
            else { e_entrada = e_avwap = e_stop = e_t1 = e_t2 = double.NaN; e_stopPts = e_t1Pts = e_t2Pts = e_rr1 = e_rr2 = 0; }

            // ── LOGGING V5 ─────────────────────
            bool puedeLoggear = RegistrarSenales && (State == State.Realtime || LoggearHistorico);
            bool entradaAhora = esVerde && !double.IsNaN(e_entrada) && pasaFiltroAncho && pasaConfirmacion;
            bool esNuevaSenal = entradaAhora && !prevEntradaValida;
            if (puedeLoggear && esNuevaSenal) { try { LogSenalApertura(); } catch { } }
            if (puedeLoggear && señalesActivas.Count > 0) { try { RevisarSenalesActivas(); } catch { } }

            // ── GUARDAR ULTIMA SEÑAL PERSISTENTE ───────────────
            if (esNuevaSenal)
            {
                _ultimaEntradaValida = true;
                _ultimaEsAlcista     = estadoActual == MasterEstado_HUD2.VerdeAlcista;
                _ultimaEsZonaLR      = zLR;
                _ultimaFechaHora     = Time[0].ToString("dd/MM HH:mm");
                _ultimaZonaLabel     = zonaLabel;
                _ultimaEntrada       = e_entrada;
                _ultimaAVWAP         = e_avwap;
                _ultimaStop          = e_stop;   _ultimaStopPts = e_stopPts;
                _ultimaT1            = e_t1;     _ultimaT1Pts   = e_t1Pts;   _ultimaRR1 = e_rr1;
                _ultimaT2            = e_t2;     _ultimaT2Pts   = e_t2Pts;   _ultimaRR2 = e_rr2;
            }
            prevEntradaValida = entradaAhora;

            // ── ALERTAS ─────────────────
            if (State == State.Realtime)
            {
                if (Alerta4de4 && tendenciaCount == 4 && prevTendenciaCount < 4) { Alert("EduS_4de4", Priority.High, "4/4 Senales " + Instrument.FullName, Sonido4de4, 10, Brushes.Lime, Brushes.Black); }
                bool esZonaA = (zonaActual == ZonaGrado_HUD2.APlus || zonaActual == ZonaGrado_HUD2.A);
                bool eraZonaA = (prevZona == ZonaGrado_HUD2.APlus || prevZona == ZonaGrado_HUD2.A);
                if (AlertaZonaA && esZonaA && !eraZonaA) { Alert("EduS_ZonaA", Priority.Medium, "Zona A " + Instrument.FullName + " [" + zonaLabel + "]", SonidoZonaA, 10, Brushes.Gold, Brushes.Black); }
                prevTendenciaCount = tendenciaCount; prevZona = zonaActual;
            }

            // Actualizar HUD (solo semaforo; posicion la actualiza el timer)
            if (Ventana_Flotante && _ownPanel != null && !_ownWindowClosed)
            {
                HudSnap2 snap = BuildSnap2();
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try { _ownPanel.Update(snap); } catch { }
                });
            }
        }
        #endregion

        
        // V5: Clase para seguimiento de señales
        private class SignalActivo
        {
            public string Id; public DateTime Apertura; public int BarraApertura; public string Mercado; public string Cuenta; public string Timeframe;
            public bool EsLong; public string Estado; public string Zona; public double Entrada, Stop, T1, T2, RR1, RR2; public bool T1Tocado;
        }
        private void LogSenalApertura() { if (string.IsNullOrEmpty(logFilePath)) return; try { string id = Guid.NewGuid().ToString("N").Substring(0,8); bool esLong = estadoActual == MasterEstado_HUD2.VerdeAlcista; string direccion = esLong ? "LONG" : "SHORT"; string cuenta = string.IsNullOrWhiteSpace(AccountName) ? "N/A" : AccountName; string tf = BarsPeriod.Value + BarsPeriod.BarsPeriodType.ToString(); string fecha = Time[0].ToString("yyyy-MM-dd HH:mm:ss"); string mercado = Instrument != null ? Instrument.FullName : _instrKey; var s = new SignalActivo { Id = id, Apertura = Time[0], BarraApertura = CurrentBar, Mercado = mercado, Cuenta = cuenta, Timeframe = tf, EsLong = esLong, Estado = estadoActual.ToString(), Zona = zonaLabel, Entrada = e_entrada, Stop = e_stop, T1 = e_t1, T2 = e_t2, RR1 = e_rr1, RR2 = e_rr2, T1Tocado = false }; señalesActivas.Add(s); string linea = string.Format(System.Globalization.CultureInfo.InvariantCulture, "ABRE,{0},{1},{2},{3},{4},{5},{6},{7},{8:F4},{9:F4},{10:F4},{11:F4},{12:F2},{13:F2},,,,," , id, fecha, mercado, cuenta, tf, direccion, s.Estado, s.Zona, s.Entrada, s.Stop, s.T1, s.T2, s.RR1, s.RR2); lock (logLock) { System.IO.File.AppendAllText(logFilePath, linea + Environment.NewLine); } } catch { } }
        private void RevisarSenalesActivas() { for (int i = señalesActivas.Count - 1; i >= 0; i--) { var s = señalesActivas[i]; bool cerrar = false; string resultado = ""; double precioSalida = 0; if (!s.T1Tocado) { if (s.EsLong && High[0] >= s.T1) s.T1Tocado = true; if (!s.EsLong && Low[0] <= s.T1) s.T1Tocado = true; } if (s.EsLong) { if (Low[0] <= s.Stop) { cerrar = true; resultado = "STOP"; precioSalida = s.Stop; } else if (High[0] >= s.T2) { cerrar = true; resultado = "T2"; precioSalida = s.T2; } } else { if (High[0] >= s.Stop) { cerrar = true; resultado = "STOP"; precioSalida = s.Stop; } else if (Low[0] <= s.T2) { cerrar = true; resultado = "T2"; precioSalida = s.T2; } } if (cerrar) { string fechaCierre = Time[0].ToString("yyyy-MM-dd HH:mm:ss"); int barras = CurrentBar - s.BarraApertura; string linea = string.Format(System.Globalization.CultureInfo.InvariantCulture, "CIERRA,{0},{1},{2},{3},{4},{5},{6},{7},{8:F4},{9:F4},{10:F4},{11:F4},{12:F2},{13:F2},{14},{15},{16},{17:F4},{18}", s.Id, s.Apertura.ToString("yyyy-MM-dd HH:mm:ss"), s.Mercado, s.Cuenta, s.Timeframe, s.EsLong ? "LONG" : "SHORT", s.Estado, s.Zona, s.Entrada, s.Stop, s.T1, s.T2, s.RR1, s.RR2, fechaCierre, resultado, s.T1Tocado ? "SI" : "NO", precioSalida, barras); lock (logLock) { System.IO.File.AppendAllText(logFilePath, linea + Environment.NewLine); } señalesActivas.RemoveAt(i); } } }

        private HudSnap2 BuildSnap2()
        {
            bool esV = estadoActual == MasterEstado_HUD2.VerdeAlcista || estadoActual == MasterEstado_HUD2.VerdeBajista;
            bool entradaActiva = esV && !double.IsNaN(e_entrada);
            return new HudSnap2
            {
                Instrumento = Instrument != null ? Instrument.MasterInstrument.Name : "---",
                Estado = estadoActual, ZonaLabel = zonaLabel, Zona = zonaActual,
                TendenciaCount = tendenciaCount,
                SMA20Up = v_sma20Up, SMA80Up = v_sma80Up, LRUp = v_lrUp, VWAPUp = v_vwapUp,
                SMA20Val = v_sma20Val, SMA80Val = v_sma80Val, LRVal = v_lrVal, VWAPVal = v_vwapVal,
                AVWAPsobreBM = v_avwapSobreBM, KeltnerBM = v_keltnerBM,
                LRsobreBM   = v_lrSobreBM,
                LRVal2      = v_lrVal,
                EntradaValida = esV && !double.IsNaN(e_entrada),
                EntradaAlcista = estadoActual == MasterEstado_HUD2.VerdeAlcista,
                Entrada = e_entrada, AVWAPprice = e_avwap, Stop = e_stop, StopPts = e_stopPts,
                T1 = e_t1, T1Pts = e_t1Pts, RR1 = e_rr1, T2 = e_t2, T2Pts = e_t2Pts, RR2 = e_rr2,
                BandWidth = k_bandWidth,
                KStopPts = k_stopPts, KStopTks = k_stopTks, KStopUsd = k_stopUsd,
                KTargetPts = k_targetPts, KTargetTks = k_targetTks, KTargetUsd = k_targetUsd,
                KStopPct = Kelt_StopPct, KRR = Kelt_RiskReward,
                MostrarSemaforo = Mostrar_Semaforo, MostrarSenales = Mostrar_Senales,
                MostrarEntrada = Mostrar_Entrada, MostrarKeltner = Mostrar_Keltner,
                MostrarPosicion = Mostrar_Posicion,
                // Ultima señal persistente
                UltimaEntradaValida = _ultimaEntradaValida,
                UltimaEntradaActiva = entradaActiva,
                UltimaFechaHora     = _ultimaFechaHora,
                UltimaEsAlcista     = _ultimaEsAlcista,
                UltimaZonaLabel     = _ultimaZonaLabel,
                UltimaEsZonaLR      = _ultimaEsZonaLR,
                UltimaEntrada       = _ultimaEntrada,
                UltimaAVWAP         = _ultimaAVWAP,
                UltimaStop          = _ultimaStop,   UltimaStopPts = _ultimaStopPts,
                UltimaT1            = _ultimaT1,     UltimaT1Pts   = _ultimaT1Pts,   UltimaRR1 = _ultimaRR1,
                UltimaT2            = _ultimaT2,     UltimaT2Pts   = _ultimaT2Pts,   UltimaRR2 = _ultimaRR2,
            };
        }

        // ================================================================
        #region HUD Window Lifecycle
        private void AbrirVentanaPropia(string instr)
        {
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _ownPanel  = new MasterHudPanel2();
                    _ownWindow = new System.Windows.Window
                    {
                        Title = "EduS HUD v3 . " + instr, Content = _ownPanel,
                        Background = MasterHudPanel2.C_BG,
                        SizeToContent = SizeToContent.WidthAndHeight,
                        ResizeMode = ResizeMode.CanMinimize, Topmost = true,
                        ShowInTaskbar = true, WindowStyle = WindowStyle.ToolWindow,
                        Left = Hud_X, Top = Hud_Y,
                    };
                    _ownWindow.Closed += (s, e) => _ownWindowClosed = true;
                    _ownWindow.Show();

                    // Timer arranca aqui: _ownPanel ya esta asignado, estamos en hilo UI
                    IniciarPosTimer();
                }
                catch { }
            });
        }

        private void AbrirVentanaCompartida(string instr)
        {
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (s_sharedWindow == null || !s_sharedWindow.IsLoaded)
                    {
                        s_sharedStack = new StackPanel { Orientation = Orientation.Vertical };
                        var scroll = new ScrollViewer
                        {
                            Content = s_sharedStack, Background = MasterHudPanel2.C_BG,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                        };
                        s_sharedWindow = new System.Windows.Window
                        {
                            Title = "EduS Trader. Master HUD v3", Content = scroll,
                            Background = MasterHudPanel2.C_BG,
                            SizeToContent = SizeToContent.Width,
                            MinHeight = 120, MaxHeight = 1200,
                            ResizeMode = ResizeMode.CanResize, Topmost = true,
                            ShowInTaskbar = true, WindowStyle = WindowStyle.ToolWindow,
                            Left = Hud_X, Top = Hud_Y,
                        };
                        s_sharedWindow.Closed += (snd, ev) =>
                        { s_sharedWindow = null; s_sharedStack = null; s_sharedPanels.Clear(); };
                        s_sharedWindow.Show();
                    }
                    if (!s_sharedPanels.ContainsKey(instr) && s_sharedStack != null)
                    {
                        if (s_sharedStack.Children.Count > 0)
                            s_sharedStack.Children.Add(new Border { Height = 3, Background = new SolidColorBrush(Color.FromRgb(40, 45, 65)) });
                        var panel = new MasterHudPanel2();
                        s_sharedPanels[instr] = panel;
                        s_sharedStack.Children.Add(panel);
                    }
                    _ownPanel = s_sharedPanels.ContainsKey(instr) ? s_sharedPanels[instr] : null;

                    // Timer arranca aqui: _ownPanel ya esta asignado, estamos en hilo UI
                    IniciarPosTimer();
                }
                catch { }
            });
        }

        private void CerrarHUD()
        {
            if (!Ventana_Flotante) return;
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (!Ventana_Compartida)
                    {
                        if (_ownWindow != null && !_ownWindowClosed) _ownWindow.Close();
                        _ownWindow = null; _ownPanel = null;
                    }
                    else
                    {
                        if (s_sharedPanels.ContainsKey(_instrKey) && s_sharedStack != null)
                        {
                            var panel = s_sharedPanels[_instrKey];
                            int idx = s_sharedStack.Children.IndexOf(panel);
                            if (idx > 0) s_sharedStack.Children.RemoveAt(idx - 1);
                            s_sharedStack.Children.Remove(panel);
                            s_sharedPanels.Remove(_instrKey);
                        }
                        _ownPanel = null;
                        if (s_sharedPanels.Count == 0 && s_sharedWindow != null)
                        { s_sharedWindow.Close(); s_sharedWindow = null; s_sharedStack = null; }
                    }
                }
                catch { }
            });
        }
        #endregion

        // ================================================================
        #region SharpDX Chart Overlay
        public override void OnRenderTargetChanged()
        {
            DisposeDXResources();
            if (RenderTarget != null)
            { try { CreateDXResources(RenderTarget); dxReady = true; } catch { dxReady = false; } }
        }

        private void CreateDXResources(SharpDX.Direct2D1.RenderTarget rt)
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
            dxV2Tag    = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(180, 130, 255, 200));
            var dwf = new SharpDX.DirectWrite.Factory();
            fmtHeader = new SharpDX.DirectWrite.TextFormat(dwf, "Arial",    SharpDX.DirectWrite.FontWeight.Bold,   SharpDX.DirectWrite.FontStyle.Normal, 10f);
            fmtNormal = new SharpDX.DirectWrite.TextFormat(dwf, "Arial",    SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, 10f);
            fmtBold   = new SharpDX.DirectWrite.TextFormat(dwf, "Arial",    SharpDX.DirectWrite.FontWeight.Bold,   SharpDX.DirectWrite.FontStyle.Normal, 12f);
            fmtMono   = new SharpDX.DirectWrite.TextFormat(dwf, "Consolas", SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, 10f);
            fmtSmall  = new SharpDX.DirectWrite.TextFormat(dwf, "Arial",    SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal,  9f);
            dwf.Dispose();
            void SA(SharpDX.DirectWrite.TextFormat f, SharpDX.DirectWrite.TextAlignment a)
            { f.TextAlignment = a; f.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center; f.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap; }
            SA(fmtHeader, SharpDX.DirectWrite.TextAlignment.Center);
            SA(fmtNormal, SharpDX.DirectWrite.TextAlignment.Leading);
            SA(fmtBold,   SharpDX.DirectWrite.TextAlignment.Center);
            SA(fmtMono,   SharpDX.DirectWrite.TextAlignment.Leading);
            SA(fmtSmall,  SharpDX.DirectWrite.TextAlignment.Leading);
        }

        protected override void OnRender(ChartControl cc, ChartScale cs)
        {
            if (!Mostrar_En_Chart) return;
            if (!dxReady || RenderTarget == null || dxFondo == null)
            { if (RenderTarget != null) { try { DisposeDXResources(); CreateDXResources(RenderTarget); dxReady = true; } catch { return; } } else return; }
            try { DrawPanel(cc, cs); } catch { dxReady = false; }
        }

        private void DrawPanel(ChartControl cc, ChartScale cs)
        {
            float diam = Tamano_Bolas * 2f, gap = 8f;
            float pW = Math.Max(PAD_H * 2f + diam * 3f + gap * 2f, 245f);
            bool esV = estadoActual == MasterEstado_HUD2.VerdeAlcista || estadoActual == MasterEstado_HUD2.VerdeBajista;
            bool esA = estadoActual == MasterEstado_HUD2.AmarilloAlcista || estadoActual == MasterEstado_HUD2.AmarilloBajista;
            bool esR = estadoActual == MasterEstado_HUD2.Rojo;

            float pH = PAD_V + ROW_H + SEC_GAP;
            if (Mostrar_Semaforo) pH += ROW_H + diam + 4f + ROW_H + 2f + SEC_GAP;
            if (Mostrar_Senales) { if (Mostrar_Semaforo) pH += ROW_H; pH += ROW_H + ROW_H * 6f + SEC_GAP; }
            bool mostrarEntradaPanel = Mostrar_Entrada && _ultimaEntradaValida;
            if (Mostrar_Entrada && esV) { if (Mostrar_Semaforo || Mostrar_Senales) pH += ROW_H; pH += ROW_H * 2f + ROW_H * 5f + SEC_GAP; }
            else if (mostrarEntradaPanel) { if (Mostrar_Semaforo || Mostrar_Senales) pH += ROW_H; pH += ROW_H * 2f + ROW_H * 5f + SEC_GAP; }
            if (Mostrar_Keltner) { if (Mostrar_Semaforo || Mostrar_Senales || mostrarEntradaPanel) pH += ROW_H; pH += ROW_H + ROW_H * 3f + SEC_GAP; }
            pH += PAD_V;

            float cW = (float)cc.ActualWidth, cH = (float)cs.Height, eO = (float)Margen_Escala;
            float px, py;
            switch (Panel_Posicion) { case 0: px=MARGIN; py=MARGIN; break; case 1: px=cW-pW-MARGIN-eO; py=MARGIN; break; case 2: px=MARGIN; py=cH-pH-MARGIN; break; default: px=cW-pW-MARGIN-eO; py=cH-pH-MARGIN; break; }
            px = Math.Max(0f, Math.Min(px, cW - pW - 1f));
            py = Math.Max(0f, Math.Min(py, cH - pH - 1f));

            var bg = new SharpDX.RectangleF(px, py, pW, pH);
            float opO = dxFondo.Opacity; dxFondo.Opacity = Fondo_Opacidad / 100f;
            RenderTarget.FillRectangle(bg, dxFondo); dxFondo.Opacity = opO;
            RenderTarget.DrawRectangle(bg, dxBorde, 1.5f);
            float cy = py + PAD_V;

            void Tx(string s, SharpDX.DirectWrite.TextFormat f, SharpDX.Direct2D1.Brush b, float x, float y, float w, float h)
            { if (f != null && !string.IsNullOrEmpty(s)) RenderTarget.DrawText(s, f, new SharpDX.RectangleF(x, y, w, h), b); }
            void SA(SharpDX.DirectWrite.TextFormat f, SharpDX.DirectWrite.TextAlignment a) { f.TextAlignment = a; }
            void Sp(float x1, float y2) { RenderTarget.DrawLine(new SharpDX.Vector2(x1, y2+ROW_H/2f), new SharpDX.Vector2(px+pW-PAD_H, y2+ROW_H/2f), dxBorde, 0.8f); }
            void Hd(string t) { SA(fmtHeader, SharpDX.DirectWrite.TextAlignment.Center); Tx(t, fmtHeader, dxTitulo, px+PAD_H, cy, pW-PAD_H*2f, ROW_H); }
            void KR(string l, string v, SharpDX.Direct2D1.Brush vb) { SA(fmtNormal, SharpDX.DirectWrite.TextAlignment.Leading); Tx(l, fmtNormal, dxGris, px+PAD_H, cy, 62f, ROW_H); SA(fmtMono, SharpDX.DirectWrite.TextAlignment.Leading); Tx(v, fmtMono, vb, px+PAD_H+64f, cy, pW-PAD_H*2f-64f, ROW_H); }
            void ER(string l, string v, SharpDX.Direct2D1.Brush vb) { SA(fmtNormal, SharpDX.DirectWrite.TextAlignment.Leading); Tx(l, fmtNormal, dxGris, px+PAD_H, cy, 55f, ROW_H); SA(fmtMono, SharpDX.DirectWrite.TextAlignment.Leading); Tx(v, fmtMono, vb, px+PAD_H+57f, cy, pW-PAD_H*2f-57f, ROW_H); }

            string inm = Instrument != null ? Instrument.MasterInstrument.Name : "---";
            SA(fmtHeader, SharpDX.DirectWrite.TextAlignment.Leading);
            Tx("EduS . " + inm, fmtHeader, dxTitulo, px+PAD_H, cy, pW-50f, ROW_H);
            SA(fmtSmall, SharpDX.DirectWrite.TextAlignment.Trailing);
            Tx("[HUDv2]", fmtSmall, dxV2Tag, px+PAD_H, cy, pW-PAD_H*2f, ROW_H);
            cy += ROW_H + SEC_GAP;

            if (Mostrar_Semaforo)
            {
                Hd("-- Semaforo --"); cy += ROW_H;
                float tw = diam*3f+gap*2f, bx = px+(pW-tw)/2f;
                void Ci(float cx2, float cy2, SharpDX.Direct2D1.Brush fill)
                { var el = new SharpDX.Direct2D1.Ellipse(new SharpDX.Vector2(cx2+diam/2f, cy2+diam/2f), diam/2f, diam/2f); RenderTarget.FillEllipse(el, fill); RenderTarget.DrawEllipse(el, dxBorde, 1.2f); }
                Ci(bx, cy, esV?dxVerde:dxApagado); Ci(bx+diam+gap, cy, esA?dxAmarillo:dxApagado); Ci(bx+(diam+gap)*2f, cy, esR?dxRojo:dxApagado);
                cy += diam + 4f;
                string dT; SharpDX.Direct2D1.Brush dB;
                switch (estadoActual) { case MasterEstado_HUD2.VerdeAlcista: dT="ALCISTA"; dB=dxVerde; break; case MasterEstado_HUD2.VerdeBajista: dT="BAJISTA"; dB=dxVerde; break; case MasterEstado_HUD2.AmarilloAlcista: dT="ALCISTA"; dB=dxAmarillo; break; case MasterEstado_HUD2.AmarilloBajista: dT="BAJISTA"; dB=dxAmarillo; break; default: dT="MIXTO"; dB=dxRojo; break; }
                SharpDX.Direct2D1.Brush zb;
                switch (zonaActual) { case ZonaGrado_HUD2.APlus: zb=dxVerde; break; case ZonaGrado_HUD2.A: zb=dxVerde; break; case ZonaGrado_HUD2.BPlus: zb=dxAmarillo; break; case ZonaGrado_HUD2.B: zb=dxNaranja; break; default: zb=dxGris; break; }
                SA(fmtBold, SharpDX.DirectWrite.TextAlignment.Center); Tx(dT, fmtBold, dB, px+PAD_H, cy, pW*0.55f-PAD_H, ROW_H);
                SA(fmtBold, SharpDX.DirectWrite.TextAlignment.Trailing); Tx("["+zonaLabel+"]", fmtBold, zb, px+pW*0.55f, cy, pW*0.45f-PAD_H, ROW_H);
                cy += ROW_H + SEC_GAP;
            }

            if (Mostrar_Senales)
            {
                if (Mostrar_Semaforo) { Sp(px+PAD_H, cy); cy += ROW_H; }
                Hd("-- Senales  "+tendenciaCount+"/4 . Zona "+zonaLabel+" --"); cy += ROW_H;
                string[] noms = {"SMA 20","SMA 80","LinReg 89","AVWAP"};
                bool[] ups = {v_sma20Up,v_sma80Up,v_lrUp,v_vwapUp};
                double[] vs2 = {v_sma20Val,v_sma80Val,v_lrVal,v_vwapVal};
                float cvx = px+pW-PAD_H-88f;
                for (int i = 0; i < 4; i++) {
                    RenderTarget.FillRectangle(new SharpDX.RectangleF(px+PAD_H, cy+ROW_H/2f-4f, 5f, 8f), ups[i]?dxVerde:dxRojo);
                    Tx(noms[i], fmtNormal, dxGris, px+PAD_H+8f, cy, 75f, ROW_H);
                    SA(fmtMono, SharpDX.DirectWrite.TextAlignment.Trailing);
                    Tx((ups[i]?"+ ":"- ")+vs2[i].ToString("0.00"), fmtMono, ups[i]?dxVerde:dxRojo, cvx, cy, 88f, ROW_H);
                    SA(fmtMono, SharpDX.DirectWrite.TextAlignment.Leading); cy += ROW_H;
                }
                string bmL = v_vwapUp ? "AVWAP > BM Kelt" : "AVWAP < BM Kelt";
                SharpDX.Direct2D1.Brush bmB = v_avwapSobreBM ? dxVerde : dxAmarillo;
                RenderTarget.FillRectangle(new SharpDX.RectangleF(px+PAD_H, cy+ROW_H/2f-4f, 5f, 8f), bmB);
                Tx(bmL+(v_avwapSobreBM?" OK":" --"), fmtNormal, bmB, px+PAD_H+8f, cy, 110f, ROW_H);
                SA(fmtMono, SharpDX.DirectWrite.TextAlignment.Trailing);
                Tx("BM="+v_keltnerBM.ToString("0.00"), fmtMono, dxGris, cvx, cy, 88f, ROW_H);
                SA(fmtMono, SharpDX.DirectWrite.TextAlignment.Leading); cy += ROW_H;
                // Capa 4: LinReg89 vs BM Keltner
                string lrBmL = v_lrUp ? "LR89 > BM Kelt" : "LR89 < BM Kelt";
                SharpDX.Direct2D1.Brush lrBmB = v_lrSobreBM ? dxVerde : dxAmarillo;
                RenderTarget.FillRectangle(new SharpDX.RectangleF(px+PAD_H, cy+ROW_H/2f-4f, 5f, 8f), lrBmB);
                Tx(lrBmL+(v_lrSobreBM?" OK":" --"), fmtNormal, lrBmB, px+PAD_H+8f, cy, 110f, ROW_H);
                SA(fmtMono, SharpDX.DirectWrite.TextAlignment.Trailing);
                Tx("LR="+v_lrVal.ToString("0.00"), fmtMono, dxGris, cvx, cy, 88f, ROW_H);
                SA(fmtMono, SharpDX.DirectWrite.TextAlignment.Leading); cy += ROW_H + SEC_GAP;
            }

            if (mostrarEntradaPanel)
            {
                if (Mostrar_Semaforo || Mostrar_Senales) { Sp(px+PAD_H, cy); cy += ROW_H; }
                bool alc   = _ultimaEsAlcista;
                bool esLR  = _ultimaEsZonaLR;
                string refLblDx = esLR ? "LinReg:" : "AVWAP:";
                string refSufDx = esLR ? (alc ? " (soporte LR)" : " (resist. LR)") : (alc ? " (soporte)" : " (resist.)");
                Hd("-- Zona Entrada [" + _ultimaZonaLabel + "] --"); cy += ROW_H;
                // Badge ACTIVA / HISTORICA + timestamp
                bool activa = esV && !double.IsNaN(e_entrada);
                string badgeTxt = (activa ? "[ ACTIVA ]" : "[ HISTORICA ]") + "  " + _ultimaFechaHora + "  [" + _ultimaZonaLabel + "]";
                SharpDX.Direct2D1.Brush badgeBrush = activa ? dxVerde : dxNaranja;
                SA(fmtNormal, SharpDX.DirectWrite.TextAlignment.Center);
                Tx(badgeTxt, fmtNormal, badgeBrush, px+PAD_H, cy, pW-PAD_H*2f, ROW_H); cy += ROW_H;
                ER("Entrada:", _ultimaEntrada.ToString("0.00"), dxCian); cy += ROW_H;
                if (!double.IsNaN(_ultimaAVWAP)) { ER(refLblDx, _ultimaAVWAP.ToString("0.00")+refSufDx, dxGris); cy += ROW_H; }
                if (!double.IsNaN(_ultimaStop))  { ER("Stop:",  _ultimaStop.ToString("0.00")+"  ("+(alc?"-":"+")+_ultimaStopPts.ToString("0.00")+" pts)", dxRojo); cy += ROW_H; }
                if (!double.IsNaN(_ultimaT1))    { ER("T1:", _ultimaT1.ToString("0.00")+"  ("+(alc?"+":"-")+_ultimaT1Pts.ToString("0.00")+" pts)  R:R "+_ultimaRR1.ToString("0.1"), dxVerde); cy += ROW_H; }
                if (!double.IsNaN(_ultimaT2))    { ER("T2:", _ultimaT2.ToString("0.00")+"  ("+(alc?"+":"-")+_ultimaT2Pts.ToString("0.00")+" pts)  R:R "+_ultimaRR2.ToString("0.1"), dxNaranja); cy += ROW_H; }
                cy += SEC_GAP;
            }

            if (Mostrar_Keltner)
            {
                if (Mostrar_Semaforo || Mostrar_Senales || mostrarEntradaPanel) { Sp(px+PAD_H, cy); cy += ROW_H; }
                Hd("-- Keltner Stop / Target --"); cy += ROW_H;
                KR("Width:", k_bandWidth.ToString("0.00")+" pts", dxCian); cy += ROW_H;
                KR("Stop "+(Kelt_StopPct*100).ToString("0")+"%:", k_stopPts.ToString("0.00")+" pts  "+k_stopTks.ToString("0")+" tks  $"+k_stopUsd.ToString("0.00"), dxRojo); cy += ROW_H;
                KR("T "+Kelt_RiskReward.ToString("0.0")+":1:", k_targetPts.ToString("0.00")+" pts  "+k_targetTks.ToString("0")+" tks  $"+k_targetUsd.ToString("0.00"), dxVerde);
            }
        }

        private void DisposeDXResources()
        {
            dxReady = false;
            SharpDX.Direct2D1.Brush[] bb = { dxFondo, dxBorde, dxTitulo, dxGris, dxVerde, dxAmarillo, dxRojo, dxApagado, dxCian, dxNaranja, dxV2Tag };
            foreach (var b in bb) b?.Dispose();
            dxFondo = dxBorde = dxTitulo = dxGris = dxVerde = dxAmarillo = dxRojo = dxApagado = dxCian = dxNaranja = dxV2Tag = null;
            SharpDX.DirectWrite.TextFormat[] ff = { fmtHeader, fmtNormal, fmtBold, fmtMono, fmtSmall };
            foreach (var f in ff) f?.Dispose();
            fmtHeader = fmtNormal = fmtBold = fmtMono = fmtSmall = null;
        }
        #endregion

        // ================================================================
        #region Properties

        // 0. Ventana HUD
        [NinjaScriptProperty][Display(Name="Activar ventana flotante",         Order=1,GroupName="0. Ventana HUD")] public bool Ventana_Flotante   { get; set; }
        [NinjaScriptProperty][Display(Name="Agrupar instrumentos (1 ventana)", Order=2,GroupName="0. Ventana HUD")] public bool Ventana_Compartida { get; set; }
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Posicion X inicial",Order=3,GroupName="0. Ventana HUD")] public int  Hud_X              { get; set; }
        [NinjaScriptProperty][Range(0,9999)][Display(Name="Posicion Y inicial",Order=4,GroupName="0. Ventana HUD")] public int  Hud_Y              { get; set; }
        [NinjaScriptProperty][Display(Name="Mostrar tambien en chart (overlay)",Order=5,GroupName="0. Ventana HUD")] public bool Mostrar_En_Chart  { get; set; }

        // 1. Posicion
        [NinjaScriptProperty][Display(Name="Mostrar seccion Posicion", Order=1,GroupName="1. Posicion")]
        public bool Mostrar_Posicion { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Cuenta (Account)", Order=2, GroupName="1. Posicion",
                 Description="Nombre exacto de la cuenta NT8 (ej: Sim101). Dejar vacio = no mostrar posicion.")]
        [TypeConverter(typeof(AccountNameConverter))]
        public string AccountName { get; set; }

        [NinjaScriptProperty][Range(1,60)][Display(Name="Actualizar cada N segundos",Order=3,GroupName="1. Posicion")]
        public int Pos_UpdateSeg { get; set; }

        // 1b. Logging
        [NinjaScriptProperty][Display(Name="Registrar señales en CSV", Order=4, GroupName="1. Posicion")] 
        public bool RegistrarSenales { get; set; }
        [NinjaScriptProperty][Display(Name="Ruta carpeta logs", Order=5, GroupName="1. Posicion")] 
        public string RutaLog { get; set; }
        [NinjaScriptProperty][Display(Name="Nombre archivo CSV", Order=6, GroupName="1. Posicion")] 
        public string NombreArchivoLog { get; set; }
        [NinjaScriptProperty][Display(Name="Loggear tambien historico", Order=7, GroupName="1. Posicion")] 
        public bool LoggearHistorico { get; set; }

        // 1c. Alertas
        [NinjaScriptProperty][Display(Name="Alerta 4/4 senales", Order=1, GroupName="8. Alertas")] 
        public bool Alerta4de4 { get; set; }
        [NinjaScriptProperty][Display(Name="Sonido 4/4", Order=2, GroupName="8. Alertas")] 
        public string Sonido4de4 { get; set; }
        [NinjaScriptProperty][Display(Name="Alerta Zona A", Order=3, GroupName="8. Alertas")] 
        public bool AlertaZonaA { get; set; }
        [NinjaScriptProperty][Display(Name="Sonido Zona A", Order=4, GroupName="8. Alertas")] 
        public string SonidoZonaA { get; set; }

        // 1d. V5 Optimización
        [NinjaScriptProperty][Display(Name="Usar Capa 3 (AVWAP vs Keltner BM)", Order=0, GroupName="9. V5 Optimizacion", Description="Si true, el semaforo verde con zona A/A+ requiere que AVWAP este sobre/bajo la banda media Keltner.")] 
        public bool UsarCapa3 { get; set; }
        [NinjaScriptProperty][Display(Name="Usar Capa 4 (LinReg89 vs Keltner BM)", Order=1, GroupName="9. V5 Optimizacion", Description="Si true, el semaforo verde con zona LR/LR+ requiere que LinReg89 este sobre/bajo la banda media Keltner.")] 
        public bool UsarCapa4 { get; set; }
        [NinjaScriptProperty][Display(Name="Usar Stop Keltner (30%)", Order=2, GroupName="9. V5 Optimizacion", Description="Si true usa 30% ancho Keltner, si false usa ATR")] 
        public bool UsarStopKeltner { get; set; }
        [NinjaScriptProperty][Display(Name="Filtro Apertura", Order=3, GroupName="9. V5 Optimizacion", Description="Requiere ancho Keltner > X*ATR")] 
        public bool FiltroApertura { get; set; }
        [NinjaScriptProperty][Range(1,5)][Display(Name="Min Keltner vs ATR", Order=4, GroupName="9. V5 Optimizacion")] 
        public double MinKeltnerVsATR { get; set; }
        [NinjaScriptProperty][Display(Name="Esperar confirmacion", Order=5, GroupName="9. V5 Optimizacion")] 
        public bool EsperarConfirmacion { get; set; }
        [NinjaScriptProperty][Range(1,5)][Display(Name="Barras confirmacion", Order=6, GroupName="9. V5 Optimizacion")] 
        public int BarrasConfirmacion { get; set; }

        // 2. Secciones semaforo
        [NinjaScriptProperty][Display(Name="Semaforo",                    Order=1,GroupName="2. Secciones")] public bool Mostrar_Semaforo { get; set; }
        [NinjaScriptProperty][Display(Name="4 Senales",                   Order=2,GroupName="2. Secciones")] public bool Mostrar_Senales  { get; set; }
        [NinjaScriptProperty][Display(Name="Zona Entrada (solo si verde)",Order=3,GroupName="2. Secciones")] public bool Mostrar_Entrada  { get; set; }
        [NinjaScriptProperty][Display(Name="Keltner Stop/Target",         Order=4,GroupName="2. Secciones")] public bool Mostrar_Keltner  { get; set; }

        // 3. Periodos
        [NinjaScriptProperty][Range(1,int.MaxValue)][Display(Name="SMA20",        Order=1,GroupName="3. Periodos")] public int    Period_SMA20      { get; set; }
        [NinjaScriptProperty][Range(1,int.MaxValue)][Display(Name="SMA80",        Order=2,GroupName="3. Periodos")] public int    Period_SMA80      { get; set; }
        [NinjaScriptProperty][Range(1,int.MaxValue)][Display(Name="LinReg89",     Order=3,GroupName="3. Periodos")] public int    Period_LR89       { get; set; }
        [NinjaScriptProperty][Range(1,20)]          [Display(Name="SlopeLookback",Order=4,GroupName="3. Periodos")] public int    SlopeLookback     { get; set; }
        [NinjaScriptProperty][Range(5,500)]         [Display(Name="AVWAP Swing",  Order=5,GroupName="3. Periodos")] public int    AVWAP_SwingPeriod { get; set; }
        [NinjaScriptProperty][Range(1,10000)]       [Display(Name="AVWAP BaseAPT",Order=6,GroupName="3. Periodos")] public int    AVWAP_BaseAPT     { get; set; }
        [NinjaScriptProperty][Range(1,50)]          [Display(Name="ATR Periodo",  Order=7,GroupName="3. Periodos")] public int    ATR_Period        { get; set; }

        // 4. Zonas AVWAP
        [NinjaScriptProperty][Range(0.05,5.0)] [Display(Name="Zona VERDE (ATR x)",    Order=1,GroupName="4. Zonas AVWAP")] public double ZoneVerde    { get; set; }
        [NinjaScriptProperty][Range(0.1,15.0)] [Display(Name="Zona AMARILLA (ATR x)", Order=2,GroupName="4. Zonas AVWAP")] public double ZoneAmarilla { get; set; }
        [NinjaScriptProperty][Range(0.1,5.0)]  [Display(Name="Stop Mult ATR",         Order=3,GroupName="4. Zonas AVWAP")] public double Stop_ATRMult { get; set; }

        // 5. Keltner
        [NinjaScriptProperty][Range(1,int.MaxValue)]    [Display(Name="Periodo",      Order=1,GroupName="5. Keltner")] public int    Kelt_Period      { get; set; }
        [NinjaScriptProperty][Range(0.01,double.MaxValue)][Display(Name="Multiplicador",Order=2,GroupName="5. Keltner")] public double Kelt_Multiplier  { get; set; }
        [NinjaScriptProperty][Range(0.01,1.0)]          [Display(Name="Stop % banda", Order=3,GroupName="5. Keltner")] public double Kelt_StopPct     { get; set; }
        [NinjaScriptProperty][Range(0.5,double.MaxValue)][Display(Name="Risk/Reward", Order=4,GroupName="5. Keltner")] public double Kelt_RiskReward  { get; set; }
        [NinjaScriptProperty][Range(1,int.MaxValue)]    [Display(Name="Contratos",    Order=5,GroupName="5. Keltner")] public int    Kelt_Contracts   { get; set; }

        // 6. Visual chart overlay
        [NinjaScriptProperty][Range(10,40)][Display(Name="Tamano bolas (px)",          Order=1,GroupName="6. Visual (chart)")] public int Tamano_Bolas   { get; set; }
        [NinjaScriptProperty][Range(5,100)][Display(Name="Opacidad fondo %",           Order=2,GroupName="6. Visual (chart)")] public int Fondo_Opacidad { get; set; }
        [NinjaScriptProperty][Range(0,3)]  [Display(Name="Posicion 0=TL 1=TR 2=BL 3=BR",Order=3,GroupName="6. Visual (chart)")] public int Panel_Posicion { get; set; }
        [NinjaScriptProperty][Range(0,200)][Display(Name="Margen escala (px)",         Order=4,GroupName="6. Visual (chart)")] public int Margen_Escala  { get; set; }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduS_MasterPanel_HUD_V2[] cacheEduS_MasterPanel_HUD_V2;
		public EduS_Trader.EduS_MasterPanel_HUD_V2 EduS_MasterPanel_HUD_V2(bool ventana_Flotante, bool ventana_Compartida, int hud_X, int hud_Y, bool mostrar_En_Chart, bool mostrar_Posicion, string accountName, int pos_UpdateSeg, bool registrarSenales, string rutaLog, string nombreArchivoLog, bool loggearHistorico, bool alerta4de4, string sonido4de4, bool alertaZonaA, string sonidoZonaA, bool usarCapa3, bool usarCapa4, bool usarStopKeltner, bool filtroApertura, double minKeltnerVsATR, bool esperarConfirmacion, int barrasConfirmacion, bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, int period_SMA20, int period_SMA80, int period_LR89, int slopeLookback, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, int aTR_Period, double zoneVerde, double zoneAmarilla, double stop_ATRMult, int kelt_Period, double kelt_Multiplier, double kelt_StopPct, double kelt_RiskReward, int kelt_Contracts, int tamano_Bolas, int fondo_Opacidad, int panel_Posicion, int margen_Escala)
		{
			return EduS_MasterPanel_HUD_V2(Input, ventana_Flotante, ventana_Compartida, hud_X, hud_Y, mostrar_En_Chart, mostrar_Posicion, accountName, pos_UpdateSeg, registrarSenales, rutaLog, nombreArchivoLog, loggearHistorico, alerta4de4, sonido4de4, alertaZonaA, sonidoZonaA, usarCapa3, usarCapa4, usarStopKeltner, filtroApertura, minKeltnerVsATR, esperarConfirmacion, barrasConfirmacion, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, period_SMA20, period_SMA80, period_LR89, slopeLookback, aVWAP_SwingPeriod, aVWAP_BaseAPT, aTR_Period, zoneVerde, zoneAmarilla, stop_ATRMult, kelt_Period, kelt_Multiplier, kelt_StopPct, kelt_RiskReward, kelt_Contracts, tamano_Bolas, fondo_Opacidad, panel_Posicion, margen_Escala);
		}

		public EduS_Trader.EduS_MasterPanel_HUD_V2 EduS_MasterPanel_HUD_V2(ISeries<double> input, bool ventana_Flotante, bool ventana_Compartida, int hud_X, int hud_Y, bool mostrar_En_Chart, bool mostrar_Posicion, string accountName, int pos_UpdateSeg, bool registrarSenales, string rutaLog, string nombreArchivoLog, bool loggearHistorico, bool alerta4de4, string sonido4de4, bool alertaZonaA, string sonidoZonaA, bool usarCapa3, bool usarCapa4, bool usarStopKeltner, bool filtroApertura, double minKeltnerVsATR, bool esperarConfirmacion, int barrasConfirmacion, bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, int period_SMA20, int period_SMA80, int period_LR89, int slopeLookback, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, int aTR_Period, double zoneVerde, double zoneAmarilla, double stop_ATRMult, int kelt_Period, double kelt_Multiplier, double kelt_StopPct, double kelt_RiskReward, int kelt_Contracts, int tamano_Bolas, int fondo_Opacidad, int panel_Posicion, int margen_Escala)
		{
			if (cacheEduS_MasterPanel_HUD_V2 != null)
				for (int idx = 0; idx < cacheEduS_MasterPanel_HUD_V2.Length; idx++)
					if (cacheEduS_MasterPanel_HUD_V2[idx] != null && cacheEduS_MasterPanel_HUD_V2[idx].Ventana_Flotante == ventana_Flotante && cacheEduS_MasterPanel_HUD_V2[idx].Ventana_Compartida == ventana_Compartida && cacheEduS_MasterPanel_HUD_V2[idx].Hud_X == hud_X && cacheEduS_MasterPanel_HUD_V2[idx].Hud_Y == hud_Y && cacheEduS_MasterPanel_HUD_V2[idx].Mostrar_En_Chart == mostrar_En_Chart && cacheEduS_MasterPanel_HUD_V2[idx].Mostrar_Posicion == mostrar_Posicion && cacheEduS_MasterPanel_HUD_V2[idx].AccountName == accountName && cacheEduS_MasterPanel_HUD_V2[idx].Pos_UpdateSeg == pos_UpdateSeg && cacheEduS_MasterPanel_HUD_V2[idx].RegistrarSenales == registrarSenales && cacheEduS_MasterPanel_HUD_V2[idx].RutaLog == rutaLog && cacheEduS_MasterPanel_HUD_V2[idx].NombreArchivoLog == nombreArchivoLog && cacheEduS_MasterPanel_HUD_V2[idx].LoggearHistorico == loggearHistorico && cacheEduS_MasterPanel_HUD_V2[idx].Alerta4de4 == alerta4de4 && cacheEduS_MasterPanel_HUD_V2[idx].Sonido4de4 == sonido4de4 && cacheEduS_MasterPanel_HUD_V2[idx].AlertaZonaA == alertaZonaA && cacheEduS_MasterPanel_HUD_V2[idx].SonidoZonaA == sonidoZonaA && cacheEduS_MasterPanel_HUD_V2[idx].UsarCapa3 == usarCapa3 && cacheEduS_MasterPanel_HUD_V2[idx].UsarCapa4 == usarCapa4 && cacheEduS_MasterPanel_HUD_V2[idx].UsarStopKeltner == usarStopKeltner && cacheEduS_MasterPanel_HUD_V2[idx].FiltroApertura == filtroApertura && cacheEduS_MasterPanel_HUD_V2[idx].MinKeltnerVsATR == minKeltnerVsATR && cacheEduS_MasterPanel_HUD_V2[idx].EsperarConfirmacion == esperarConfirmacion && cacheEduS_MasterPanel_HUD_V2[idx].BarrasConfirmacion == barrasConfirmacion && cacheEduS_MasterPanel_HUD_V2[idx].Mostrar_Semaforo == mostrar_Semaforo && cacheEduS_MasterPanel_HUD_V2[idx].Mostrar_Senales == mostrar_Senales && cacheEduS_MasterPanel_HUD_V2[idx].Mostrar_Entrada == mostrar_Entrada && cacheEduS_MasterPanel_HUD_V2[idx].Mostrar_Keltner == mostrar_Keltner && cacheEduS_MasterPanel_HUD_V2[idx].Period_SMA20 == period_SMA20 && cacheEduS_MasterPanel_HUD_V2[idx].Period_SMA80 == period_SMA80 && cacheEduS_MasterPanel_HUD_V2[idx].Period_LR89 == period_LR89 && cacheEduS_MasterPanel_HUD_V2[idx].SlopeLookback == slopeLookback && cacheEduS_MasterPanel_HUD_V2[idx].AVWAP_SwingPeriod == aVWAP_SwingPeriod && cacheEduS_MasterPanel_HUD_V2[idx].AVWAP_BaseAPT == aVWAP_BaseAPT && cacheEduS_MasterPanel_HUD_V2[idx].ATR_Period == aTR_Period && cacheEduS_MasterPanel_HUD_V2[idx].ZoneVerde == zoneVerde && cacheEduS_MasterPanel_HUD_V2[idx].ZoneAmarilla == zoneAmarilla && cacheEduS_MasterPanel_HUD_V2[idx].Stop_ATRMult == stop_ATRMult && cacheEduS_MasterPanel_HUD_V2[idx].Kelt_Period == kelt_Period && cacheEduS_MasterPanel_HUD_V2[idx].Kelt_Multiplier == kelt_Multiplier && cacheEduS_MasterPanel_HUD_V2[idx].Kelt_StopPct == kelt_StopPct && cacheEduS_MasterPanel_HUD_V2[idx].Kelt_RiskReward == kelt_RiskReward && cacheEduS_MasterPanel_HUD_V2[idx].Kelt_Contracts == kelt_Contracts && cacheEduS_MasterPanel_HUD_V2[idx].Tamano_Bolas == tamano_Bolas && cacheEduS_MasterPanel_HUD_V2[idx].Fondo_Opacidad == fondo_Opacidad && cacheEduS_MasterPanel_HUD_V2[idx].Panel_Posicion == panel_Posicion && cacheEduS_MasterPanel_HUD_V2[idx].Margen_Escala == margen_Escala && cacheEduS_MasterPanel_HUD_V2[idx].EqualsInput(input))
						return cacheEduS_MasterPanel_HUD_V2[idx];
			return CacheIndicator<EduS_Trader.EduS_MasterPanel_HUD_V2>(new EduS_Trader.EduS_MasterPanel_HUD_V2(){ Ventana_Flotante = ventana_Flotante, Ventana_Compartida = ventana_Compartida, Hud_X = hud_X, Hud_Y = hud_Y, Mostrar_En_Chart = mostrar_En_Chart, Mostrar_Posicion = mostrar_Posicion, AccountName = accountName, Pos_UpdateSeg = pos_UpdateSeg, RegistrarSenales = registrarSenales, RutaLog = rutaLog, NombreArchivoLog = nombreArchivoLog, LoggearHistorico = loggearHistorico, Alerta4de4 = alerta4de4, Sonido4de4 = sonido4de4, AlertaZonaA = alertaZonaA, SonidoZonaA = sonidoZonaA, UsarCapa3 = usarCapa3, UsarCapa4 = usarCapa4, UsarStopKeltner = usarStopKeltner, FiltroApertura = filtroApertura, MinKeltnerVsATR = minKeltnerVsATR, EsperarConfirmacion = esperarConfirmacion, BarrasConfirmacion = barrasConfirmacion, Mostrar_Semaforo = mostrar_Semaforo, Mostrar_Senales = mostrar_Senales, Mostrar_Entrada = mostrar_Entrada, Mostrar_Keltner = mostrar_Keltner, Period_SMA20 = period_SMA20, Period_SMA80 = period_SMA80, Period_LR89 = period_LR89, SlopeLookback = slopeLookback, AVWAP_SwingPeriod = aVWAP_SwingPeriod, AVWAP_BaseAPT = aVWAP_BaseAPT, ATR_Period = aTR_Period, ZoneVerde = zoneVerde, ZoneAmarilla = zoneAmarilla, Stop_ATRMult = stop_ATRMult, Kelt_Period = kelt_Period, Kelt_Multiplier = kelt_Multiplier, Kelt_StopPct = kelt_StopPct, Kelt_RiskReward = kelt_RiskReward, Kelt_Contracts = kelt_Contracts, Tamano_Bolas = tamano_Bolas, Fondo_Opacidad = fondo_Opacidad, Panel_Posicion = panel_Posicion, Margen_Escala = margen_Escala }, input, ref cacheEduS_MasterPanel_HUD_V2);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduS_MasterPanel_HUD_V2 EduS_MasterPanel_HUD_V2(bool ventana_Flotante, bool ventana_Compartida, int hud_X, int hud_Y, bool mostrar_En_Chart, bool mostrar_Posicion, string accountName, int pos_UpdateSeg, bool registrarSenales, string rutaLog, string nombreArchivoLog, bool loggearHistorico, bool alerta4de4, string sonido4de4, bool alertaZonaA, string sonidoZonaA, bool usarCapa3, bool usarCapa4, bool usarStopKeltner, bool filtroApertura, double minKeltnerVsATR, bool esperarConfirmacion, int barrasConfirmacion, bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, int period_SMA20, int period_SMA80, int period_LR89, int slopeLookback, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, int aTR_Period, double zoneVerde, double zoneAmarilla, double stop_ATRMult, int kelt_Period, double kelt_Multiplier, double kelt_StopPct, double kelt_RiskReward, int kelt_Contracts, int tamano_Bolas, int fondo_Opacidad, int panel_Posicion, int margen_Escala)
		{
			return indicator.EduS_MasterPanel_HUD_V2(Input, ventana_Flotante, ventana_Compartida, hud_X, hud_Y, mostrar_En_Chart, mostrar_Posicion, accountName, pos_UpdateSeg, registrarSenales, rutaLog, nombreArchivoLog, loggearHistorico, alerta4de4, sonido4de4, alertaZonaA, sonidoZonaA, usarCapa3, usarCapa4, usarStopKeltner, filtroApertura, minKeltnerVsATR, esperarConfirmacion, barrasConfirmacion, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, period_SMA20, period_SMA80, period_LR89, slopeLookback, aVWAP_SwingPeriod, aVWAP_BaseAPT, aTR_Period, zoneVerde, zoneAmarilla, stop_ATRMult, kelt_Period, kelt_Multiplier, kelt_StopPct, kelt_RiskReward, kelt_Contracts, tamano_Bolas, fondo_Opacidad, panel_Posicion, margen_Escala);
		}

		public Indicators.EduS_Trader.EduS_MasterPanel_HUD_V2 EduS_MasterPanel_HUD_V2(ISeries<double> input , bool ventana_Flotante, bool ventana_Compartida, int hud_X, int hud_Y, bool mostrar_En_Chart, bool mostrar_Posicion, string accountName, int pos_UpdateSeg, bool registrarSenales, string rutaLog, string nombreArchivoLog, bool loggearHistorico, bool alerta4de4, string sonido4de4, bool alertaZonaA, string sonidoZonaA, bool usarCapa3, bool usarCapa4, bool usarStopKeltner, bool filtroApertura, double minKeltnerVsATR, bool esperarConfirmacion, int barrasConfirmacion, bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, int period_SMA20, int period_SMA80, int period_LR89, int slopeLookback, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, int aTR_Period, double zoneVerde, double zoneAmarilla, double stop_ATRMult, int kelt_Period, double kelt_Multiplier, double kelt_StopPct, double kelt_RiskReward, int kelt_Contracts, int tamano_Bolas, int fondo_Opacidad, int panel_Posicion, int margen_Escala)
		{
			return indicator.EduS_MasterPanel_HUD_V2(input, ventana_Flotante, ventana_Compartida, hud_X, hud_Y, mostrar_En_Chart, mostrar_Posicion, accountName, pos_UpdateSeg, registrarSenales, rutaLog, nombreArchivoLog, loggearHistorico, alerta4de4, sonido4de4, alertaZonaA, sonidoZonaA, usarCapa3, usarCapa4, usarStopKeltner, filtroApertura, minKeltnerVsATR, esperarConfirmacion, barrasConfirmacion, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, period_SMA20, period_SMA80, period_LR89, slopeLookback, aVWAP_SwingPeriod, aVWAP_BaseAPT, aTR_Period, zoneVerde, zoneAmarilla, stop_ATRMult, kelt_Period, kelt_Multiplier, kelt_StopPct, kelt_RiskReward, kelt_Contracts, tamano_Bolas, fondo_Opacidad, panel_Posicion, margen_Escala);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduS_MasterPanel_HUD_V2 EduS_MasterPanel_HUD_V2(bool ventana_Flotante, bool ventana_Compartida, int hud_X, int hud_Y, bool mostrar_En_Chart, bool mostrar_Posicion, string accountName, int pos_UpdateSeg, bool registrarSenales, string rutaLog, string nombreArchivoLog, bool loggearHistorico, bool alerta4de4, string sonido4de4, bool alertaZonaA, string sonidoZonaA, bool usarCapa3, bool usarCapa4, bool usarStopKeltner, bool filtroApertura, double minKeltnerVsATR, bool esperarConfirmacion, int barrasConfirmacion, bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, int period_SMA20, int period_SMA80, int period_LR89, int slopeLookback, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, int aTR_Period, double zoneVerde, double zoneAmarilla, double stop_ATRMult, int kelt_Period, double kelt_Multiplier, double kelt_StopPct, double kelt_RiskReward, int kelt_Contracts, int tamano_Bolas, int fondo_Opacidad, int panel_Posicion, int margen_Escala)
		{
			return indicator.EduS_MasterPanel_HUD_V2(Input, ventana_Flotante, ventana_Compartida, hud_X, hud_Y, mostrar_En_Chart, mostrar_Posicion, accountName, pos_UpdateSeg, registrarSenales, rutaLog, nombreArchivoLog, loggearHistorico, alerta4de4, sonido4de4, alertaZonaA, sonidoZonaA, usarCapa3, usarCapa4, usarStopKeltner, filtroApertura, minKeltnerVsATR, esperarConfirmacion, barrasConfirmacion, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, period_SMA20, period_SMA80, period_LR89, slopeLookback, aVWAP_SwingPeriod, aVWAP_BaseAPT, aTR_Period, zoneVerde, zoneAmarilla, stop_ATRMult, kelt_Period, kelt_Multiplier, kelt_StopPct, kelt_RiskReward, kelt_Contracts, tamano_Bolas, fondo_Opacidad, panel_Posicion, margen_Escala);
		}

		public Indicators.EduS_Trader.EduS_MasterPanel_HUD_V2 EduS_MasterPanel_HUD_V2(ISeries<double> input , bool ventana_Flotante, bool ventana_Compartida, int hud_X, int hud_Y, bool mostrar_En_Chart, bool mostrar_Posicion, string accountName, int pos_UpdateSeg, bool registrarSenales, string rutaLog, string nombreArchivoLog, bool loggearHistorico, bool alerta4de4, string sonido4de4, bool alertaZonaA, string sonidoZonaA, bool usarCapa3, bool usarCapa4, bool usarStopKeltner, bool filtroApertura, double minKeltnerVsATR, bool esperarConfirmacion, int barrasConfirmacion, bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, int period_SMA20, int period_SMA80, int period_LR89, int slopeLookback, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, int aTR_Period, double zoneVerde, double zoneAmarilla, double stop_ATRMult, int kelt_Period, double kelt_Multiplier, double kelt_StopPct, double kelt_RiskReward, int kelt_Contracts, int tamano_Bolas, int fondo_Opacidad, int panel_Posicion, int margen_Escala)
		{
			return indicator.EduS_MasterPanel_HUD_V2(input, ventana_Flotante, ventana_Compartida, hud_X, hud_Y, mostrar_En_Chart, mostrar_Posicion, accountName, pos_UpdateSeg, registrarSenales, rutaLog, nombreArchivoLog, loggearHistorico, alerta4de4, sonido4de4, alertaZonaA, sonidoZonaA, usarCapa3, usarCapa4, usarStopKeltner, filtroApertura, minKeltnerVsATR, esperarConfirmacion, barrasConfirmacion, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, period_SMA20, period_SMA80, period_LR89, slopeLookback, aVWAP_SwingPeriod, aVWAP_BaseAPT, aTR_Period, zoneVerde, zoneAmarilla, stop_ATRMult, kelt_Period, kelt_Multiplier, kelt_StopPct, kelt_RiskReward, kelt_Contracts, tamano_Bolas, fondo_Opacidad, panel_Posicion, margen_Escala);
		}
	}
}

#endregion
