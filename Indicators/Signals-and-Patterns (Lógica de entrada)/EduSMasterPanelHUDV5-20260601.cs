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



public enum V5_MercadoTipo { Auto, NQ, MNQ, ES, MES, CL, GC, Desconocido }

public enum V5_DeteccionModo { KeltnerWidth, AtrRange }

public enum V5_DireccionModo { Ambos, SoloLong, SoloShort }  // V5.1: filtro de dirección operativa



namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader

{

    internal enum V5_TendenciaColor { Rojo, Amarillo, VerdeClaro, VerdeOscuro }



    internal enum V5_ZonaTipo { A, Aplus, LR, LRplus, Z20, Z20plus, Z80, Z80plus, M1, M1plus, M2, M2plus, M3, M3plus, Ninguna }



    internal enum V5_PerfilModo { SwingRecalc, SwingPrevio, DesdHora }



    internal struct V5_HudSnap

    {

        public string Instrumento, Version, EscenarioActivo;

        public int TendenciaCount;

        public V5_TendenciaColor ColorSemaforo;

        public bool LinRegUp, AVWAPUp, SMA20Up, SMA80Up;

        public double LinRegVal, AVWAPVal, SMA20Val, SMA80Val;

        public string CruceSMA_Dir;

        public int CruceSMA_Barras;

        public bool HTF_Activo, HTF_Pasa, Capa3_Activo, Capa3_Pasa, Capa4_Activo, Capa4_Pasa;

        public V5_ZonaTipo ZonaActiva;

        public string ZonaLabel;

        public int ZonaRanking;

        public bool ZonaPlus;

        public double ZonaDistATR;

        public int ZonasActivasCount;

        public string ZonasActivasResumen;

        public double Entrada, Stop, T1, T2, RR1, RR2;

        public bool EntradaValida, EntradaAlcista;

        public bool UltimaEntradaValida, UltimaEntradaActiva;

        public string UltimaFechaHora, UltimaZonaLabel;

        public double UltimaEntrada, UltimaStop, UltimaT1, UltimaT2;

        public double UltimaStopPts, UltimaT1Pts, UltimaT2Pts, UltimaRR1, UltimaRR2;

        public double KeltnerVolWidth, KeltnerVolBM;

        public double KeltnerSignalWidth, KeltnerSignalBM;

        public double ATR, VolumenNegociado, SessionVolume;

        public double POC, AntiPOC_Up, AntiPOC_Down, LVN;

        public bool NakedPOC_Activo;

        public string PerfilModoLabel;

        public bool MostrarSemaforo, MostrarSenales, MostrarEntrada, MostrarKeltner, MostrarPosicion;

        public bool InstrumentoNoReconocido;

        public bool ConfigNoEncontrada;

    }



    internal struct V5_ZonaInfo

    {

        public V5_ZonaTipo Tipo;

        public string Label;

        public bool Plus;

        public double DistATR;

        public int Ranking;

    }



    internal struct V5_PosSnap

    {

        public bool TienePos, EsLong;

        public int Cantidad;

        public double PrecioEntrada, PLdolar, PLpuntos;

        public bool TieneStop, TieneTarget;

        public double StopRisk, TargetProfit, RR;

        public bool RiesgoIlimitado, GananciaAsegurada;

        public string Etiqueta;

    }



    internal struct V5_EscenarioConfig

    {

        public string Market;

        public int Timeframe;

        public string ScenarioName;

        public double KeltWidth_Min, KeltWidth_Max;

        public double Atr_Min, Atr_Max;

        public int AVWAP_SwingPeriod, AVWAP_BaseAPT;

        public bool AVWAP_AdptAPT;

        public double AVWAP_VolBIAS;

        public int TrialBars;

        public double LabelOffset;

        public double ZoneVerde_ATR, ZoneAmarillo_ATR;

        public double StopATRx, StopPct;

        public int StopMaxTicks;

        public double RR1, RR2;

        public int Kelt_Period_Signal;

        public double Kelt_Mult_Signal;

    }



    internal sealed class V5_MasterHudPanel : Border

    {

        internal static readonly SolidColorBrush C_BG     = Frz(new SolidColorBrush(Color.FromArgb(245, 12, 14, 20)));

        internal static readonly SolidColorBrush C_BORDER = Frz(new SolidColorBrush(Color.FromRgb(70, 80, 110)));

        private static readonly SolidColorBrush C_TITLE  = Frz(new SolidColorBrush(Color.FromRgb(200, 200, 220)));

        private static readonly SolidColorBrush C_GRAY   = Frz(new SolidColorBrush(Color.FromRgb(150, 155, 165)));

        private static readonly SolidColorBrush C_GREEN  = Frz(new SolidColorBrush(Color.FromRgb(0, 200, 80)));

        private static readonly SolidColorBrush C_GREEN2 = Frz(new SolidColorBrush(Color.FromRgb(0, 255, 100)));

        private static readonly SolidColorBrush C_YELLOW = Frz(new SolidColorBrush(Color.FromRgb(255, 210, 0)));

        private static readonly SolidColorBrush C_RED    = Frz(new SolidColorBrush(Color.FromRgb(220, 50, 50)));

        private static readonly SolidColorBrush C_DIM    = Frz(new SolidColorBrush(Color.FromArgb(100, 45, 45, 50)));

        private static readonly SolidColorBrush C_CYAN   = Frz(new SolidColorBrush(Color.FromRgb(80, 200, 220)));

        private static readonly SolidColorBrush C_ORANGE = Frz(new SolidColorBrush(Color.FromRgb(255, 140, 0)));

        private static readonly SolidColorBrush C_VIOLET = Frz(new SolidColorBrush(Color.FromRgb(180, 130, 255)));

        private static readonly SolidColorBrush C_LIME   = Frz(new SolidColorBrush(Color.FromRgb(100, 255, 120)));

        private static readonly SolidColorBrush C_ORANG2 = Frz(new SolidColorBrush(Color.FromRgb(255, 160, 40)));

        private static readonly SolidColorBrush C_BLUE   = Frz(new SolidColorBrush(Color.FromRgb(60, 120, 220)));



        private static SolidColorBrush Frz(SolidColorBrush b) { b.Freeze(); return b; }



        private TextBlock _tbTitle, _tbVersion, _tbEscenario;

        private Ellipse _elRojo, _elAmarillo, _elVerdeClaro, _elVerdeOscuro;

        private TextBlock _tbSemaforoLabel, _tbSemaforoCount, _tbCruceSMA;

        private TextBlock _tbHTF, _tbCapa3, _tbCapa4;

        private StackPanel _spSemaforo, _spSenales, _spEntrada, _spKeltner, _spPosicion, _spHeaderInfo;

        private Border _sep1, _sep2, _sep3, _sep4, _sep5;

        private TextBlock _tbHdrSenales;

        private readonly Rectangle[] _sigRect = new Rectangle[7];

        private readonly TextBlock[] _sigLbl = new TextBlock[7];

        private readonly TextBlock[] _sigVal = new TextBlock[7];

        private TextBlock _tbEntradaBadge, _tbEntradaTS;

        private TextBlock _tbEntrada, _tbStop, _tbT1, _tbT2;

        private TextBlock _tbKVolWidth, _tbKVolBM, _tbKSigWidth, _tbKSigBM, _tbATR, _tbVolumen, _tbSesVol;

        private TextBlock _tbPosDir, _tbPosPL, _tbPosOrdenes;

        private TextBlock _tbAlertaMercado;

        private TextBlock _tbIndSMA20, _tbIndSMA80, _tbIndLR, _tbIndAVWAP;



        public V5_MasterHudPanel(int ancho) { Build(ancho); }



        private void Build(int ancho)

        {

            Background = C_BG;

            BorderBrush = C_BORDER;

            BorderThickness = new Thickness(1.5);

            CornerRadius = new CornerRadius(5);

            Padding = new Thickness(10, 7, 10, 8);

            MinWidth = ancho;
            Width = ancho;



            var root = new StackPanel { Orientation = Orientation.Vertical };

            Child = root;



            _spHeaderInfo = new StackPanel { Margin = new Thickness(0, 0, 0, 2) };

            root.Children.Add(_spHeaderInfo);

            var hRow = new Grid { Margin = new Thickness(0, 0, 0, 2) };

            hRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            hRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            hRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _tbTitle = Tb("EduS . ---", 12, FontWeights.Bold, C_TITLE);

            Grid.SetColumn(_tbTitle, 0);

            hRow.Children.Add(_tbTitle);

            _tbVersion = Tb("[V5.1]", 9, FontWeights.Normal, C_VIOLET);

            _tbVersion.VerticalAlignment = VerticalAlignment.Center;

            _tbVersion.Margin = new Thickness(0, 0, 6, 0);

            Grid.SetColumn(_tbVersion, 1);

            hRow.Children.Add(_tbVersion);

            _tbEscenario = Tb("", 9, FontWeights.Normal, C_CYAN);

            _tbEscenario.VerticalAlignment = VerticalAlignment.Center;

            Grid.SetColumn(_tbEscenario, 2);

            hRow.Children.Add(_tbEscenario);

            _spHeaderInfo.Children.Add(hRow);

            _tbAlertaMercado = Tb("", 10, FontWeights.Bold, C_RED);

            _tbAlertaMercado.TextAlignment = TextAlignment.Center;

            _tbAlertaMercado.Visibility = Visibility.Collapsed;

            _spHeaderInfo.Children.Add(_tbAlertaMercado);

            root.Children.Add(HSep());



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



            _sep1 = HSep(); root.Children.Add(_sep1);

            _spSemaforo = new StackPanel();

            root.Children.Add(_spSemaforo);

            var hS = Tb("-- Semaforo de Tendencia --", 10, FontWeights.Bold, C_TITLE);

            hS.TextAlignment = TextAlignment.Center;

            _spSemaforo.Children.Add(hS);

            var ballRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 3) };

            _elRojo = Circ(16); ballRow.Children.Add(_elRojo);

            _elAmarillo = Circ(16); ballRow.Children.Add(_elAmarillo);

            _elVerdeClaro = Circ(16); ballRow.Children.Add(_elVerdeClaro);

            _elVerdeOscuro = Circ(16); ballRow.Children.Add(_elVerdeOscuro);

            _spSemaforo.Children.Add(ballRow);

            var dRow = new Grid { Margin = new Thickness(0, 2, 0, 2) };

            dRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.5, GridUnitType.Star) });

            dRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.5, GridUnitType.Star) });

            _tbSemaforoLabel = Tb("SIN DIRECCION", 12, FontWeights.Bold, C_RED);

            _tbSemaforoCount = Tb("[0/4]", 12, FontWeights.Bold, C_GRAY);

            _tbSemaforoCount.TextAlignment = TextAlignment.Right;

            Grid.SetColumn(_tbSemaforoLabel, 0); Grid.SetColumn(_tbSemaforoCount, 1);

            dRow.Children.Add(_tbSemaforoLabel); dRow.Children.Add(_tbSemaforoCount);

            _spSemaforo.Children.Add(dRow);

            _tbCruceSMA = Tb("", 9, FontWeights.Normal, C_GRAY);

            _tbCruceSMA.TextAlignment = TextAlignment.Center;

            _spSemaforo.Children.Add(_tbCruceSMA);

            var indRow1 = new Grid { Margin = new Thickness(0, 1, 0, 0) };

            indRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.5, GridUnitType.Star) });

            indRow1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.5, GridUnitType.Star) });

            _tbIndSMA20 = Tb("SMA20: ---", 9, FontWeights.Normal, C_GRAY);

            _tbIndSMA80 = Tb("SMA80: ---", 9, FontWeights.Normal, C_GRAY);

            _tbIndSMA80.TextAlignment = TextAlignment.Right;

            Grid.SetColumn(_tbIndSMA20, 0); Grid.SetColumn(_tbIndSMA80, 1);

            indRow1.Children.Add(_tbIndSMA20); indRow1.Children.Add(_tbIndSMA80);

            _spSemaforo.Children.Add(indRow1);

            var indRow2 = new Grid { Margin = new Thickness(0, 1, 0, 2) };

            indRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.5, GridUnitType.Star) });

            indRow2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.5, GridUnitType.Star) });

            _tbIndLR = Tb("LR89: ---", 9, FontWeights.Normal, C_GRAY);

            _tbIndAVWAP = Tb("AVWAP: ---", 9, FontWeights.Normal, C_GRAY);

            _tbIndAVWAP.TextAlignment = TextAlignment.Right;

            Grid.SetColumn(_tbIndLR, 0); Grid.SetColumn(_tbIndAVWAP, 1);

            indRow2.Children.Add(_tbIndLR); indRow2.Children.Add(_tbIndAVWAP);

            _spSemaforo.Children.Add(indRow2);

            var filtRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };

            _tbHTF = Tb("", 9, FontWeights.Normal, C_GRAY);

            _tbHTF.Margin = new Thickness(0, 0, 8, 0);

            _tbCapa3 = Tb("", 9, FontWeights.Normal, C_GRAY);

            _tbCapa3.Margin = new Thickness(0, 0, 8, 0);

            _tbCapa4 = Tb("", 9, FontWeights.Normal, C_GRAY);

            filtRow.Children.Add(_tbHTF); filtRow.Children.Add(_tbCapa3); filtRow.Children.Add(_tbCapa4);

            _spSemaforo.Children.Add(filtRow);



            _sep2 = HSep(); root.Children.Add(_sep2);

            _spSenales = new StackPanel(); root.Children.Add(_spSenales);

            _tbHdrSenales = Tb("-- Senales 7 Zonas --", 10, FontWeights.Bold, C_TITLE);

            _tbHdrSenales.TextAlignment = TextAlignment.Center;

            _spSenales.Children.Add(_tbHdrSenales);

            string[] sNames = { "Zona A/A+", "Zona LR/LR+", "Zona 20/20+", "Zona 80/80+", "M1/M1+", "M2/M2+", "M3/M3+" };

            for (int i = 0; i < 7; i++)

            {

                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };

                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });

                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                _sigRect[i] = new Rectangle { Width = 5, Height = 8, Fill = C_DIM, VerticalAlignment = VerticalAlignment.Center };

                _sigLbl[i] = Tb(sNames[i], 10, FontWeights.Normal, C_GRAY);

                _sigLbl[i].Margin = new Thickness(4, 0, 0, 0);

                _sigVal[i] = Tb("", 10, FontWeights.Normal, C_GRAY);

                _sigVal[i].TextAlignment = TextAlignment.Right;

                Grid.SetColumn(_sigRect[i], 0); Grid.SetColumn(_sigLbl[i], 1); Grid.SetColumn(_sigVal[i], 2);

                row.Children.Add(_sigRect[i]); row.Children.Add(_sigLbl[i]); row.Children.Add(_sigVal[i]);

                _spSenales.Children.Add(row);

            }



            _sep3 = HSep(); root.Children.Add(_sep3);

            _spEntrada = new StackPanel(); root.Children.Add(_spEntrada);

            var hE = Tb("-- Entrada Activa --", 10, FontWeights.Bold, C_TITLE);

            hE.TextAlignment = TextAlignment.Center;

            _spEntrada.Children.Add(hE);

            var badgeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 2) };

            _tbEntradaBadge = Tb("[ -- ]", 10, FontWeights.Bold, C_GRAY);

            _tbEntradaBadge.Margin = new Thickness(0, 0, 8, 0);

            _tbEntradaTS = Tb("", 9, FontWeights.Normal, C_GRAY);

            badgeRow.Children.Add(_tbEntradaBadge); badgeRow.Children.Add(_tbEntradaTS);

            _spEntrada.Children.Add(badgeRow);

            _tbEntrada = ERow(_spEntrada, "Entrada:", C_CYAN);

            _tbStop = ERow(_spEntrada, "Stop:", C_RED);

            _tbT1 = ERow(_spEntrada, "T1:", C_GREEN);

            _tbT2 = ERow(_spEntrada, "T2:", C_ORANGE);



            _sep4 = HSep(); root.Children.Add(_sep4);

            _spKeltner = new StackPanel(); root.Children.Add(_spKeltner);

            var hK = Tb("-- Market & Keltner --", 10, FontWeights.Bold, C_TITLE);
            hK.TextAlignment = TextAlignment.Center;
            _spKeltner.Children.Add(hK);

            (_tbATR, _) = KRow(_spKeltner, "ATR:", C_CYAN);
            (_tbVolumen, _) = KRow(_spKeltner, "Vol. Barra:", C_GRAY);
            (_tbSesVol, _) = KRow(_spKeltner, "Vol. Sesión:", C_CYAN);
            (_tbKVolWidth, _) = KRow(_spKeltner, "Vol. Width:", C_CYAN);
            (_tbKVolBM, _) = KRow(_spKeltner, "Vol. BM:", C_GRAY);
            (_tbKSigWidth, _) = KRow(_spKeltner, "Sig. Width:", C_CYAN);
            (_tbKSigBM, _) = KRow(_spKeltner, "Sig. BM:", C_GRAY);

        }



        public void Update(V5_HudSnap s)

        {

            _spPosicion.Visibility = s.MostrarPosicion ? Visibility.Visible : Visibility.Collapsed;

            _sep1.Visibility = (s.MostrarPosicion && s.MostrarSemaforo) ? Visibility.Visible : Visibility.Collapsed;

            _spSemaforo.Visibility = s.MostrarSemaforo ? Visibility.Visible : Visibility.Collapsed;

            _sep2.Visibility = (s.MostrarSemaforo && s.MostrarSenales) ? Visibility.Visible : Visibility.Collapsed;

            _spSenales.Visibility = s.MostrarSenales ? Visibility.Visible : Visibility.Collapsed;

            _sep3.Visibility = (s.MostrarSenales && s.MostrarEntrada && s.UltimaEntradaValida) ? Visibility.Visible : Visibility.Collapsed;

            _spEntrada.Visibility = (s.MostrarEntrada && s.UltimaEntradaValida) ? Visibility.Visible : Visibility.Collapsed;

            _sep4.Visibility = (s.MostrarEntrada && s.MostrarKeltner) ? Visibility.Visible : Visibility.Collapsed;

            _spKeltner.Visibility = s.MostrarKeltner ? Visibility.Visible : Visibility.Collapsed;



            _tbTitle.Text = "EduS . " + s.Instrumento;

            _tbVersion.Text = "[V5.1 " + s.Version + "]";

            _tbEscenario.Text = s.EscenarioActivo;



            if (s.InstrumentoNoReconocido)

            {

                _tbAlertaMercado.Text = "! MERCADO NO RECONOCIDO - Configure Market_Type manualmente !";

                _tbAlertaMercado.Visibility = Visibility.Visible;

            }

            else if (s.ConfigNoEncontrada)

            {

                _tbAlertaMercado.Text = "! CONFIG NO ENCONTRADA para este mercado - Usando defaults NQ !";

                _tbAlertaMercado.Visibility = Visibility.Visible;

            }

            else _tbAlertaMercado.Visibility = Visibility.Collapsed;



            if (s.MostrarSemaforo)

            {

                _elRojo.Fill = C_DIM; _elAmarillo.Fill = C_DIM; _elVerdeClaro.Fill = C_DIM; _elVerdeOscuro.Fill = C_DIM;

                switch (s.ColorSemaforo)

                {

                    case V5_TendenciaColor.Rojo: _elRojo.Fill = C_RED; _tbSemaforoLabel.Text = "SIN DIRECCION"; _tbSemaforoLabel.Foreground = C_RED; break;

                    case V5_TendenciaColor.Amarillo: _elAmarillo.Fill = C_YELLOW; _tbSemaforoLabel.Text = "TENDENCIAL"; _tbSemaforoLabel.Foreground = C_YELLOW; break;

                    case V5_TendenciaColor.VerdeClaro: _elVerdeClaro.Fill = C_GREEN; _tbSemaforoLabel.Text = "OK ENTRADAS"; _tbSemaforoLabel.Foreground = C_GREEN; break;

                    case V5_TendenciaColor.VerdeOscuro: _elVerdeOscuro.Fill = C_GREEN2; _tbSemaforoLabel.Text = "OPTIMO"; _tbSemaforoLabel.Foreground = C_GREEN2; break;

                }

                _tbSemaforoCount.Text = "[" + s.TendenciaCount + "/4]";

                _tbCruceSMA.Text = s.CruceSMA_Dir.Contains("Cruce") ? s.CruceSMA_Dir + " (" + s.CruceSMA_Barras + "b)" : "Cruce 20/80: " + s.CruceSMA_Dir;

                _tbCruceSMA.Foreground = s.CruceSMA_Dir.Contains("Alcista") ? C_GREEN : s.CruceSMA_Dir.Contains("Bajista") ? C_RED : C_GRAY;



                _tbIndSMA20.Text = "SMA20: " + s.SMA20Val.ToString("0.00") + (s.SMA20Up ? " ↑" : " ↓");

                _tbIndSMA20.Foreground = s.SMA20Up ? C_GREEN : C_RED;

                _tbIndSMA80.Text = "SMA80: " + s.SMA80Val.ToString("0.00") + (s.SMA80Up ? " ↑" : " ↓");

                _tbIndSMA80.Foreground = s.SMA80Up ? C_GREEN : C_RED;

                _tbIndLR.Text = "LR89: " + s.LinRegVal.ToString("0.00") + (s.LinRegUp ? " ↑" : " ↓");

                _tbIndLR.Foreground = s.LinRegUp ? C_GREEN : C_RED;

                _tbIndAVWAP.Text = "AVWAP: " + s.AVWAPVal.ToString("0.00") + (s.AVWAPUp ? " ↑" : " ↓");

                _tbIndAVWAP.Foreground = s.AVWAPUp ? C_GREEN : C_RED;



                _tbHTF.Visibility = Visibility.Visible;

                _tbCapa3.Visibility = Visibility.Visible;

                _tbCapa4.Visibility = Visibility.Visible;

                _tbHTF.Text = s.HTF_Activo ? ("HTF:ON " + (s.HTF_Pasa ? "✓" : "✗")) : "HTF:OFF";

                _tbCapa3.Text = s.Capa3_Activo ? ("C3:ON " + (s.Capa3_Pasa ? "✓" : "✗")) : "C3:OFF";

                _tbCapa4.Text = s.Capa4_Activo ? ("C4:ON " + (s.Capa4_Pasa ? "✓" : "✗")) : "C4:OFF";

            }



            if (s.MostrarSenales)

            {

                _tbHdrSenales.Text = s.ZonasActivasCount > 1

                    ? "-- Activas: " + s.ZonasActivasResumen + " --"

                    : "-- Senales: " + s.ZonaLabel + " (#" + s.ZonaRanking + ") --";

                V5_ZonaTipo[] zonas = { V5_ZonaTipo.A, V5_ZonaTipo.LR, V5_ZonaTipo.Z20, V5_ZonaTipo.Z80, V5_ZonaTipo.M1, V5_ZonaTipo.M2, V5_ZonaTipo.M3 };

                for (int i = 0; i < 7; i++)

                {

                    bool activa = s.ZonaActiva == zonas[i] || s.ZonaActiva == (V5_ZonaTipo)((int)zonas[i] + 1);

                    _sigRect[i].Fill = activa ? C_GREEN : C_DIM;

                    _sigLbl[i].Foreground = activa ? C_GREEN : C_GRAY;

                    _sigVal[i].Foreground = activa ? C_CYAN : C_GRAY;

                    _sigVal[i].Text = activa ? (s.ZonaPlus ? "+ " : "  ") + s.ZonaDistATR.ToString("0.00") + "σ" : "";

                }

            }



            if (s.MostrarEntrada && s.UltimaEntradaValida)

            {

                _tbEntradaBadge.Text = "[" + s.UltimaZonaLabel + "]";

                _tbEntradaBadge.Foreground = s.UltimaEntradaActiva ? C_GREEN : C_ORANGE;

                _tbEntradaTS.Text = s.UltimaFechaHora + (s.UltimaEntradaActiva ? " [ACTIVA]" : " [HIST]");

                _tbEntrada.Text = s.UltimaEntrada.ToString("0.00");

                _tbStop.Text = s.UltimaStop.ToString("0.00") + "  (" + s.UltimaStopPts.ToString("0.00") + " pts)";

                _tbT1.Text = s.UltimaT1.ToString("0.00") + "  (" + s.UltimaT1Pts.ToString("0.00") + " pts)  R:R " + s.UltimaRR1.ToString("0.1");

                _tbT2.Text = s.UltimaT2.ToString("0.00") + "  (" + s.UltimaT2Pts.ToString("0.00") + " pts)  R:R " + s.UltimaRR2.ToString("0.1");

            }



            if (s.MostrarKeltner)
            {
                _tbATR.Text = s.ATR.ToString("0.00") + " pts";
                _tbVolumen.Text = s.VolumenNegociado.ToString("N0");
                _tbSesVol.Text = s.SessionVolume.ToString("N0");
                _tbKVolWidth.Text = s.KeltnerVolWidth.ToString("0.00") + " pts";
                _tbKVolBM.Text = s.KeltnerVolBM.ToString("0.00");
                _tbKSigWidth.Text = s.KeltnerSignalWidth.ToString("0.00") + " pts";
                _tbKSigBM.Text = s.KeltnerSignalBM.ToString("0.00");
            }

        }



        public void UpdatePosition(V5_PosSnap p)

        {

            if (!p.TienePos && !string.IsNullOrEmpty(p.Etiqueta) && p.Etiqueta.StartsWith("ERR"))

            {

                _tbPosDir.Text = p.Etiqueta; _tbPosDir.Foreground = C_RED;

                _tbPosPL.Text = ""; _tbPosOrdenes.Text = ""; return;

            }

            if (!p.TienePos) { _tbPosDir.Text = "No Position"; _tbPosDir.Foreground = C_GRAY; _tbPosPL.Text = ""; _tbPosOrdenes.Text = ""; return; }

            string dir = p.EsLong ? "LONG" : "SHORT";

            _tbPosDir.Text = dir + " " + p.Cantidad + " @ " + p.PrecioEntrada.ToString("0.00");

            _tbPosDir.Foreground = p.EsLong ? C_LIME : C_ORANG2;

            string sgn = p.PLdolar >= 0 ? "+" : "-";

            string sgnPts = p.PLpuntos >= 0 ? "+" : "-";

            _tbPosPL.Text = sgn + "$" + Math.Abs(p.PLdolar).ToString("N0") + " (" + sgnPts + Math.Abs(p.PLpuntos).ToString("0.00") + " pts)";

            _tbPosPL.Foreground = Math.Abs(p.PLdolar) < 0.01 ? C_GRAY : p.PLdolar > 0 ? C_GREEN : C_RED;

            string txt = "";

            if (p.GananciaAsegurada && p.TieneStop) txt += "Locked $" + p.StopRisk.ToString("N0");

            else if (p.TieneStop) txt += "Risk $" + p.StopRisk.ToString("N0");

            else txt += "Risk: Sin stop";

            if (p.TieneTarget) txt += " | T $" + p.TargetProfit.ToString("N0");

            if (p.RR > 0) txt += " | R:R " + p.RR.ToString("0.1") + ":1";

            if (!string.IsNullOrEmpty(p.Etiqueta)) txt = "[" + p.Etiqueta + "] " + txt;

            _tbPosOrdenes.Text = txt; _tbPosOrdenes.Foreground = C_GRAY;

        }



        private static TextBlock Tb(string t, double sz, FontWeight w, SolidColorBrush fg) =>

            new TextBlock { Text = t, FontSize = sz, FontWeight = w, Foreground = fg, VerticalAlignment = VerticalAlignment.Center };

        private static Ellipse Circ(double d) =>

            new Ellipse { Width = d, Height = d, Fill = C_DIM, Stroke = C_BORDER, StrokeThickness = 1.2, Margin = new Thickness(3, 0, 3, 0) };

        private static Border HSep() => new Border { Height = 1, Background = C_BORDER, Margin = new Thickness(0, 3, 0, 3) };

        private static TextBlock ERow(StackPanel sp, string lbl, SolidColorBrush vc)

        {

            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tL = Tb(lbl, 10, FontWeights.Normal, C_GRAY);

            var tV = Tb("---", 10, FontWeights.Normal, vc);

            Grid.SetColumn(tL, 0); Grid.SetColumn(tV, 1);

            row.Children.Add(tL); row.Children.Add(tV);

            sp.Children.Add(row);

            return tV;

        }

        private static (TextBlock val, TextBlock lbl) KRow(StackPanel sp, string lbl, SolidColorBrush vc)

        {

            var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(65) });

            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tL = Tb(lbl, 10, FontWeights.Normal, C_GRAY);

            var tV = Tb("---", 10, FontWeights.Normal, vc);

            Grid.SetColumn(tL, 0); Grid.SetColumn(tV, 1);

            row.Children.Add(tL); row.Children.Add(tV);

            sp.Children.Add(row);

            return (tV, tL);

        }

    }



    public class EduS_MasterPanel_HUD_V5 : Indicator

    {

        #region Indicadores internos (sin dependencias externas)



        private class V5_InternalSMA

        {

            private int _period;

            private double[] _values;

            private double[] _smaValues;

            private int _idx;

            private double _sum;

            private bool _filled;

            public V5_InternalSMA(int period) { _period = period; _values = new double[period]; _smaValues = new double[period]; _idx = 0; _sum = 0; _filled = false; }

            public double Update(double val)

            {

                if (_filled) _sum -= _values[_idx];

                

                _values[_idx] = val;

                _sum += val;

                

                double sma = _filled ? _sum / _period : _sum / (_idx + 1);

                _smaValues[_idx] = sma;



                _idx = (_idx + 1) % _period;

                if (!_filled && _idx == 0) _filled = true;

                return sma;

            }

            public double Current

            {

                get

                {

                    if (!_filled) return double.NaN;

                    int lastIdx = (_idx == 0) ? _period - 1 : _idx - 1;

                    return _smaValues[lastIdx];

                }

            }

            public double this[int offset]

            {

                get

                {

                    if (!_filled || offset < 0 || offset >= _period) return double.NaN;

                    int idx = (_idx - 1 - offset + _period) % _period;

                    return _smaValues[idx];

                }

            }

            public bool IsReady => _filled;

        }



        // === V5.2: Clase EMA interna — misma lógica que EduSMDCMasterIndicator ===
        private class V5_InternalEMA
        {
            private readonly double _k;       // factor de suavizado = 2/(period+1)
            private double _ema;
            private bool _filled;
            private int _warmup;              // barras recibidas antes de tener el primer SMA
            private int _period;
            private double _sum;

            public V5_InternalEMA(int period)
            {
                _period = period;
                _k      = 2.0 / (period + 1);
                _ema    = 0;
                _sum    = 0;
                _filled = false;
                _warmup = 0;
            }

            public double Update(double val)
            {
                if (!_filled)
                {
                    // Fase de calentamiento: acumular hasta tener 'period' muestras para seed inicial
                    _sum += val;
                    _warmup++;
                    if (_warmup >= _period)
                    {
                        _ema    = _sum / _period;
                        _filled = true;
                    }
                    return double.NaN;
                }
                _ema = val * _k + _ema * (1.0 - _k);
                return _ema;
            }

            public double Current  => _filled ? _ema : double.NaN;
            public bool   IsReady  => _filled;
        }
        // ==========================================================================


        private class V5_InternalLinReg

        {

            private int _period;

            private double[] _values;

            private int _idx;

            private bool _filled;

            private int _count;

            public V5_InternalLinReg(int period) { _period = period; _values = new double[period]; _idx = 0; _filled = false; _count = 0; }

            public void Update(double val)

            {

                _values[_idx] = val;

                _idx = (_idx + 1) % _period;

                if (_count < _period) _count++;

                if (_count >= _period) _filled = true;

            }

            public double Slope

            {

                get

                {

                    if (!_filled) return 0;

                    int n = _period;

                    double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0;

                    for (int i = 0; i < n; i++)

                    {

                        int idx = (_idx - 1 - i + _period) % _period;

                        double y = _values[idx];

                        double x = n - 1 - i;

                        sumX += x; sumY += y; sumXY += x * y; sumX2 += x * x;

                    }

                    return (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);

                }

            }

            public double Intercept

            {

                get

                {

                    if (!_filled) return 0;

                    int n = _period;

                    double sumX = 0, sumY = 0;

                    for (int i = 0; i < n; i++)

                    {

                        int idx = (_idx - 1 - i + _period) % _period;

                        sumY += _values[idx];

                        sumX += n - 1 - i;

                    }

                    return (sumY - Slope * sumX) / n;

                }

            }

            public double Current => _filled ? Intercept + Slope * (_period - 1) : double.NaN;

            public bool IsReady => _filled;

        }



        private class V5_InternalATR

        {

            private int _period;

            private double[] _trs;

            private int _idx;

            private double _sum;

            private bool _filled;

            private double _prevClose;

            private bool _hasPrev;

            public V5_InternalATR(int period) { _period = period; _trs = new double[period]; _idx = 0; _sum = 0; _filled = false; _hasPrev = false; }

            public double Update(double high, double low, double close)

            {

                if (!_hasPrev) { _prevClose = close; _hasPrev = true; return 0; }

                double tr = Math.Max(high - low, Math.Max(Math.Abs(high - _prevClose), Math.Abs(low - _prevClose)));

                _prevClose = close;

                if (_filled) _sum -= _trs[_idx];

                _trs[_idx] = tr;

                _sum += tr;

                _idx = (_idx + 1) % _period;

                if (!_filled && _idx == 0) _filled = true;

                return _filled ? _sum / _period : _sum / (_idx == 0 ? _period : _idx);

            }

            public double Current

            {

                get

                {

                    if (!_filled) return 0;

                    return _sum / _period;

                }

            }

            public bool IsReady => _filled;

        }



        #endregion



        #region Escenario detection



        private class V5_InternalKeltner

        {

            private int _period;

            private double _mult;

            // V5.2: Reemplazado de SMA a EMA para coincidir con EduSMDCMasterIndicator
            private V5_InternalEMA _emaTypical;

            private V5_InternalEMA _emaRange;

            public V5_InternalKeltner(int period, double mult)

            {

                _period = period; _mult = mult;

                _emaTypical = new V5_InternalEMA(period);

                _emaRange   = new V5_InternalEMA(period);

            }

            public void UpdateParams(int period, double mult)

            {

                _mult = mult;

                if (period != _period && period > 0)

                {

                    _period     = period;

                    _emaTypical = new V5_InternalEMA(period);

                    _emaRange   = new V5_InternalEMA(period);

                }

            }

            public void Update(double high, double low, double close)

            {

                double typical = (high + low + close) / 3.0;

                _emaTypical.Update(typical);

                _emaRange.Update(high - low);

            }

            public double Mid => _emaTypical.Current;

            public double Width

            {

                get

                {

                    if (!_emaRange.IsReady || !_emaTypical.IsReady) return 0;

                    double offset = _emaRange.Current * _mult;

                    return offset * 2;

                }

            }

            public double Upper => _emaTypical.Current + (_emaRange.IsReady ? _emaRange.Current * _mult : 0);

            public double Lower => _emaTypical.Current - (_emaRange.IsReady ? _emaRange.Current * _mult : 0);

            public double BandOffset => _emaRange.IsReady ? _emaRange.Current * _mult : 0;

            public bool IsReady => _emaRange.IsReady && _emaTypical.IsReady;

        }



        #endregion



        #region V5 Volume Profile Engine (Fase 4)



        private class V5_VolumeProfile

        {

            private double _tickSize;

            private int _modo; // 0=SwingRecalc, 1=SwingPrevio, 2=DesdHora

            private int _desdeHora;

            private int _windowMinutes;

            private Dictionary<double, double> _volProfile = new Dictionary<double, double>();

            private double _profileHigh = double.NaN, _profileLow = double.NaN;

            private double _poc = double.NaN, _antiPocUp = double.NaN, _antiPocDown = double.NaN;

            private double _lvn = double.NaN;

            private double _prevSessionPOC = double.NaN;

            private bool _prevPOCSet = false;

            private bool _nakedPOCAlert = false;

            private bool _prevPocTouched = false;

            private DateTime _currentSessionDate = DateTime.MinValue;

            private double _swingAnchorPrice = double.NaN;

            private double _swingAnchorLow = double.NaN;

            private int _swingAnchorBar = 0;

            private int _currentBar = 0;

            private double _lvnThreshold = 0.4;



            public V5_VolumeProfile(double tickSize, int modo, int desdeHora, int windowMinutes)

            {

                _tickSize = tickSize;

                _modo = modo;

                _desdeHora = desdeHora;

                _windowMinutes = windowMinutes;

            }



            public void SetAnchoring(int modo, int desdeHora, int windowMinutes)

            {

                _modo = modo;

                _desdeHora = desdeHora;

                _windowMinutes = windowMinutes;

            }



            public bool CheckSwingAnchor(double swingHigh, double swingLow, int bar)

            {

                if (_modo == 0 && !double.IsNaN(swingHigh) && swingHigh != _swingAnchorPrice)

                {

                    if (!double.IsNaN(_swingAnchorPrice))

                        FinalizeProfile();

                    _swingAnchorPrice = swingHigh;

                    _swingAnchorLow = swingLow;

                    _swingAnchorBar = bar;

                    ResetProfile();

                    return true;

                }

                return false;

            }



            public void Update(double high, double low, double volume, DateTime time, int bar)

            {

                _currentBar = bar;

                bool shouldReset = false;

                int hhmm = time.Hour * 100 + time.Minute;



                if (_modo == 2 && _desdeHora > 0)

                {

                    if (hhmm >= _desdeHora && hhmm < _desdeHora + _windowMinutes)

                    {

                        if (_currentSessionDate != time.Date)

                            shouldReset = true;

                    }

                }



                if (time.Date != _currentSessionDate)

                {

                    if (_modo == 0 && _currentSessionDate != DateTime.MinValue)

                    {

                        if (_prevSessionDate != DateTime.MinValue)

                        {

                            _prevSessionPOC = _poc;

                            _prevPOCSet = true;

                            _nakedPOCAlert = true;

                        }

                        _prevSessionDate = _currentSessionDate;

                    }

                    _currentSessionDate = time.Date;

                    if (_modo != 0)

                        shouldReset = true;

                }



                if (_modo == 1 && _currentSessionDate != DateTime.MinValue && time.Date != _currentSessionDate)

                {

                    if (!double.IsNaN(_poc))

                    {

                        _prevSessionPOC = _poc;

                        _prevPOCSet = true;

                        _nakedPOCAlert = true;

                    }

                    shouldReset = true;

                }



                if (shouldReset)

                    ResetProfile();



                int steps = Math.Max(1, (int)Math.Round((high - low) / _tickSize));

                double vpt = volume / (double)(steps + 1);

                for (int i = 0; i <= steps; i++)

                {

                    double price = Math.Round((low + i * _tickSize) / _tickSize) * _tickSize;

                    if (!_volProfile.ContainsKey(price))

                        _volProfile[price] = 0;

                    _volProfile[price] += vpt;

                }



                Recalc();

            }



            private DateTime _prevSessionDate = DateTime.MinValue;



            private void ResetProfile()

            {

                _volProfile.Clear();

                _poc = double.NaN;

                _antiPocUp = double.NaN;

                _antiPocDown = double.NaN;

                _lvn = double.NaN;

                _profileHigh = double.NaN;

                _profileLow = double.NaN;

                _nakedPOCAlert = false;

            }



            private void FinalizeProfile()

            {

                if (_volProfile.Count == 0) return;

                _prevSessionPOC = _poc;

                _prevPOCSet = true;

                _nakedPOCAlert = true;

            }



            private void Recalc()

            {

                if (_volProfile.Count == 0) return;



                var sorted = _volProfile.OrderBy(kv => kv.Key).ToList();

                _profileHigh = sorted.Last().Key;

                _profileLow = sorted.First().Key;



                double maxVol = -1;

                _poc = double.NaN;

                foreach (var kv in _volProfile)

                {

                    if (kv.Value > maxVol) { maxVol = kv.Value; _poc = kv.Key; }

                }



                double range = _profileHigh - _profileLow;

                double third = range / 3.0;

                double lowerThirdMax = _profileLow + third;

                double upperThirdMin = _profileHigh - third;



                double minVol = maxVol * 0.3;

                double lvnVol = maxVol;

                _lvn = _poc;

                _antiPocDown = double.NaN;

                _antiPocUp = double.NaN;



                foreach (var kv in _volProfile)

                {

                    if (kv.Value < lvnVol && kv.Value <= minVol)

                    {

                        lvnVol = kv.Value;

                        _lvn = kv.Key;

                    }

                    if (kv.Key <= lowerThirdMax && kv.Value <= minVol && kv.Value < maxVol * 0.5)

                    {

                        if (double.IsNaN(_antiPocDown) || kv.Value < GetVolAt(_antiPocDown))

                            _antiPocDown = kv.Key;

                    }

                    if (kv.Key >= upperThirdMin && kv.Value <= minVol && kv.Value < maxVol * 0.5)

                    {

                        if (double.IsNaN(_antiPocUp) || kv.Value < GetVolAt(_antiPocUp))

                            _antiPocUp = kv.Key;

                    }

                }

            }



            private double GetVolAt(double price)

            {

                return _volProfile.ContainsKey(price) ? _volProfile[price] : 0;

            }



            public double POC => _poc;

            public double AntiPOC_Up => _antiPocUp;

            public double AntiPOC_Down => _antiPocDown;

            public double LVN => _lvn;

            public double PrevSessionPOC => _prevSessionPOC;

            public bool PrevPOCSet => _prevPOCSet;

            public bool IsNakedPOC(double currentPrice, double distTicks)

            {

                if (!_prevPOCSet || double.IsNaN(_prevSessionPOC)) return false;

                double dist = distTicks * _tickSize;

                return Math.Abs(currentPrice - _prevSessionPOC) > dist;

            }

            public bool CheckPocTouched(double high, double low)

            {

                if (!_prevPOCSet || double.IsNaN(_prevSessionPOC)) return true;

                return high >= _prevSessionPOC && low <= _prevSessionPOC;

            }

            public int VolProfileCount => _volProfile.Count;

        }



        #endregion



        #region V5 Internal HTF (Fase 10)



        private class V5_InternalSMA_HTF

        {

            private int _period;

            private List<double> _values = new List<double>();

            public V5_InternalSMA_HTF(int period) { _period = period; }

            public void Update(double val)

            {

                _values.Add(val);

                if (_values.Count > _period * 2) _values.RemoveRange(0, _period);

            }

            public double Current

            {

                get

                {

                    if (_values.Count < _period) return double.NaN;

                    int start = _values.Count - _period;

                    double sum = 0;

                    for (int i = start; i < _values.Count; i++) sum += _values[i];

                    return sum / _period;

                }

            }

            public bool IsReady => _values.Count >= _period;

        }



        #endregion



        #region V5 Internal Nodes & POCs (Fase 10)



        private class V5_NodeInfo { public double Price; }

        private class V5_PocLevel { public double Price; public bool IsActive; }



        private class V5_NodePocEngine

        {

            private double _tickSize;

            private Dictionary<double, double> _nodeVolume = new Dictionary<double, double>();

            private Dictionary<double, double> _pocVolume = new Dictionary<double, double>();

            private List<V5_NodeInfo> _nodes = new List<V5_NodeInfo>();

            private List<V5_PocLevel> _pocs = new List<V5_PocLevel>();

            private DateTime _lastPocDate = DateTime.MinValue;

            private DateTime _lastNodeDate = DateTime.MinValue;

            private double _nodeThreshold = 0.4;

            private double _pocThreshold = 0.4;



            public V5_NodePocEngine(double tickSize) { _tickSize = tickSize; }



            public void UpdateNodes(double high, double low, double volume, DateTime time)

            {

                DistributeVolume(_nodeVolume, high, low, volume);

                if (time.Date != _lastNodeDate)

                {

                    CalcNodes();

                    _nodeVolume.Clear();

                    _lastNodeDate = time.Date;

                }

            }



            public void UpdatePocs(double high, double low, double volume, DateTime time)

            {

                DistributeVolume(_pocVolume, high, low, volume);

                if (time.Date != _lastPocDate)

                {

                    CalcPocs();

                    if (time.Date != _lastPocDate)

                    {

                        _pocVolume.Clear();

                        _lastPocDate = time.Date;

                    }

                }

                CheckPocTouches(high, low, time);

            }



            private void DistributeVolume(Dictionary<double, double> dict, double high, double low, double volume)

            {

                int steps = Math.Max(1, (int)Math.Round((high - low) / _tickSize));

                double vpt = volume / (double)(steps + 1);

                for (int i = 0; i <= steps; i++)

                {

                    double price = Math.Round((low + i * _tickSize) / _tickSize) * _tickSize;

                    if (!dict.ContainsKey(price)) dict[price] = 0;

                    dict[price] += vpt;

                }

            }



            private void CalcNodes()

            {

                if (_nodeVolume.Count == 0) return;

                var sorted = _nodeVolume.OrderByDescending(kv => kv.Value).ToList();

                double maxV = sorted[0].Value;

                double minVol = maxV * _nodeThreshold;

                _nodes.Clear();

                foreach (var kv in sorted)

                {

                    if (kv.Value < minVol) break;

                    bool merged = false;

                    foreach (var n in _nodes)

                        if (Math.Abs(kv.Key - n.Price) <= _tickSize * 3) { merged = true; break; }

                    if (!merged) _nodes.Add(new V5_NodeInfo { Price = kv.Key });

                }

            }



            private void CalcPocs()

            {

                if (_pocVolume.Count == 0) return;

                double maxVol = -1, pocPrice = 0;

                foreach (var kv in _pocVolume)

                    if (kv.Value > maxVol) { maxVol = kv.Value; pocPrice = kv.Key; }

                if (maxVol > 0)

                    _pocs.Add(new V5_PocLevel { Price = pocPrice, IsActive = true });

            }



            private void CheckPocTouches(double high, double low, DateTime time)

            {

                int hhmm = time.Hour * 100 + time.Minute;

                bool rth = hhmm >= 930 && hhmm < 1600;

                for (int i = _pocs.Count - 1; i >= 0; i--)

                {

                    var p = _pocs[i];

                    if (!p.IsActive) continue;

                    if (high >= p.Price && low <= p.Price)

                    {

                        if (!rth) continue;

                        p.IsActive = false;

                    }

                }

            }



            public bool CheckNodeProximity(double price, double distTicks)

            {

                double dist = distTicks * _tickSize;

                foreach (var n in _nodes)

                    if (Math.Abs(price - n.Price) <= dist) return false;

                return true;

            }



            public bool CheckPocProximity(double price, double distTicks)

            {

                double dist = distTicks * _tickSize;

                foreach (var p in _pocs)

                    if (p.IsActive && Math.Abs(price - p.Price) <= dist) return false;

                return true;

            }



            public int NodeCount => _nodes.Count;

            public int PocCount => _pocs.Count;

        }



        #endregion



        private V5_EscenarioConfig[] _escenarios;

        internal string _escenarioActual = "";

        internal V5_EscenarioConfig _escenarioConfigActual;

        private bool _configNoEncontrada = false;



        private string MercadoToString(V5_MercadoTipo m)

        {

            switch (m)

            {

                case V5_MercadoTipo.NQ: return "NQ";

                case V5_MercadoTipo.MNQ: return "MNQ";

                case V5_MercadoTipo.ES: return "ES";

                case V5_MercadoTipo.MES: return "MES";

                case V5_MercadoTipo.CL: return "CL";

                case V5_MercadoTipo.GC: return "GC";

                default: return "";

            }

        }



        private int GetCurrentTimeframe()

        {

            if (BarsPeriod == null) return 0;

            return BarsPeriod.BarsPeriodType == BarsPeriodType.Tick ? BarsPeriod.Value : BarsPeriod.Value;

        }



        private V5_EscenarioConfig[] FiltrarEscenariosPorMercado(V5_EscenarioConfig[] todos, string mercado, int tf)

        {

            return todos.Where(e => string.Equals(e.Market, mercado, StringComparison.OrdinalIgnoreCase) && e.Timeframe == tf).ToArray();

        }



        private void CargarEscenarios()

        {

            _escenarios = GetDefaultEscenarios();

            if (string.IsNullOrWhiteSpace(RutaLog)) return;

            string csvPath = System.IO.Path.Combine(RutaLog, string.IsNullOrWhiteSpace(ConfigCSV) ? "V5_VolatilityConfig.csv" : ConfigCSV);

            if (File.Exists(csvPath))

            {

                try

                {

                    var lines = File.ReadAllLines(csvPath);

                    if (lines.Length > 1)

                    {

                        string hdr = lines[0].Trim().ToUpperInvariant();

                        // Auto-detect delimiter: comma or semicolon
                        char delim = hdr.Contains(';') ? ';' : ',';

                        bool newFormat = hdr.Replace(";", ",").StartsWith("MARKET,");

                        var lista = new List<V5_EscenarioConfig>();

                        for (int i = 1; i < lines.Length; i++)

                        {

                            if (string.IsNullOrWhiteSpace(lines[i])) continue;

                            var parts = lines[i].Split(delim);

                            if (newFormat && parts.Length >= 20)

                            {

                                var ec = new V5_EscenarioConfig();

                                ec.Market = parts[0].Trim().ToUpperInvariant();

                                int.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.Timeframe);

                                ec.ScenarioName = parts[2].Trim();

                                double.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.KeltWidth_Min);

                                double.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.KeltWidth_Max);

                                int.TryParse(parts[5], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.AVWAP_SwingPeriod);

                                int.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.AVWAP_BaseAPT);

                                bool.TryParse(parts[7], out ec.AVWAP_AdptAPT);

                                double.TryParse(parts[8], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.AVWAP_VolBIAS);

                                int.TryParse(parts[9], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.TrialBars);

                                double.TryParse(parts[10], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.LabelOffset);

                                double.TryParse(parts[11], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.ZoneVerde_ATR);

                                double.TryParse(parts[12], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.ZoneAmarillo_ATR);

                                double.TryParse(parts[13], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.StopATRx);

                                double.TryParse(parts[14], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.StopPct);

                                int.TryParse(parts[15], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.StopMaxTicks);

                                double.TryParse(parts[16], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.RR1);

                                double.TryParse(parts[17], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.RR2);

                                int.TryParse(parts[18], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.Kelt_Period_Signal);

                                double.TryParse(parts[19], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.Kelt_Mult_Signal);

                                if (parts.Length >= 22)
                                {
                                    double.TryParse(parts[20], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.Atr_Min);
                                    double.TryParse(parts[21], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.Atr_Max);
                                }
                                else
                                {
                                    ec.Atr_Min = ec.KeltWidth_Min;
                                    ec.Atr_Max = ec.KeltWidth_Max;
                                }

                                if (!string.IsNullOrWhiteSpace(ec.Market) && !string.IsNullOrWhiteSpace(ec.ScenarioName))

                                    lista.Add(ec);

                            }

                            else if (!newFormat && parts.Length >= 18)

                            {

                                var ec = new V5_EscenarioConfig();

                                ec.Market = "";

                                ec.Timeframe = 0;

                                ec.ScenarioName = parts[0].Trim();

                                double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.KeltWidth_Min);

                                double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.KeltWidth_Max);

                                int.TryParse(parts[3], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.AVWAP_SwingPeriod);

                                int.TryParse(parts[4], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.AVWAP_BaseAPT);

                                bool.TryParse(parts[5], out ec.AVWAP_AdptAPT);

                                double.TryParse(parts[6], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.AVWAP_VolBIAS);

                                int.TryParse(parts[7], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.TrialBars);

                                double.TryParse(parts[8], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.LabelOffset);

                                double.TryParse(parts[9], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.ZoneVerde_ATR);

                                double.TryParse(parts[10], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.ZoneAmarillo_ATR);

                                double.TryParse(parts[11], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.StopATRx);

                                double.TryParse(parts[12], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.StopPct);

                                int.TryParse(parts[13], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.StopMaxTicks);

                                double.TryParse(parts[14], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.RR1);

                                double.TryParse(parts[15], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.RR2);

                                int.TryParse(parts[16], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.Kelt_Period_Signal);

                                double.TryParse(parts[17], NumberStyles.Any, CultureInfo.InvariantCulture, out ec.Kelt_Mult_Signal);

                                ec.Atr_Min = ec.KeltWidth_Min;
                                ec.Atr_Max = ec.KeltWidth_Max;

                                if (!string.IsNullOrWhiteSpace(ec.ScenarioName))

                                    lista.Add(ec);

                            }

                        }

                        if (lista.Count > 0) _escenarios = lista.ToArray();

                    }

                }

                catch { }

            }

            else

            {

                try

                {

                    string templatePath = csvPath;

                    if (!File.Exists(templatePath))

                        GenerarTemplateCSV(templatePath);

                }

                catch { }

            }

        }



        private void GenerarTemplateCSV(string templatePath)

        {

            var header = "Market,Timeframe,ScenarioName,KeltWidth_Min,KeltWidth_Max,AVWAP_SwingPeriod,AVWAP_BaseAPT,AVWAP_AdptAPT,AVWAP_VolBIAS,TrialBars,LabelOffset,ZoneVerde_ATR,ZoneAmarillo_ATR,StopATRx,StopPct,StopMaxTicks,RR1,RR2,Kelt_Period_Signal,Kelt_Mult_Signal,Atr_Min,Atr_Max";

            var lines = new List<string> { header };

            var mercados = new[] { "NQ", "MNQ", "ES", "MES", "CL", "GC" };

            int[] tfs = { 377, 377, 610, 610, 5, 5 };

            string[] names = { "Baja", "Media", "Alta", "Extrema", "SuperExtrema" };

            double[] mins = { 0, 18, 22, 27, 32 };

            double[] maxs = { 18, 22, 27, 32, -1 };

            for (int m = 0; m < mercados.Length; m++)

            {

                bool minsOnly = mercados[m] == "CL" || mercados[m] == "GC";

                var defaults = GetDefaultEscenarioParams();

                for (int s = 0; s < names.Length; s++)

                {

                    lines.Add(string.Format(CultureInfo.InvariantCulture,

                        "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21}",

                        mercados[m], tfs[m], names[s], mins[s], maxs[s],

                        defaults.AVWAP_SwingPeriod, defaults.AVWAP_BaseAPT, defaults.AVWAP_AdptAPT, defaults.AVWAP_VolBIAS,

                        defaults.TrialBars, defaults.LabelOffset, defaults.ZoneVerde_ATR, defaults.ZoneAmarillo_ATR,

                        defaults.StopATRx, defaults.StopPct, defaults.StopMaxTicks, defaults.RR1, defaults.RR2,

                        defaults.Kelt_Period_Signal, defaults.Kelt_Mult_Signal, mins[s], maxs[s]));

                }

            }

            File.WriteAllLines(templatePath, lines);

        }



        private V5_EscenarioConfig GetDefaultEscenarioParams()

        {

            return new V5_EscenarioConfig { AVWAP_SwingPeriod = 50, AVWAP_BaseAPT = 20, AVWAP_AdptAPT = true, AVWAP_VolBIAS = 10.0, TrialBars = 2, LabelOffset = 0, ZoneVerde_ATR = 0.5, ZoneAmarillo_ATR = 2.0, StopATRx = 0.5, StopPct = 0.003, StopMaxTicks = 0, RR1 = 2.0, RR2 = 3.0, Kelt_Period_Signal = 20, Kelt_Mult_Signal = 2.0 };

        }



        private V5_EscenarioConfig[] GetDefaultEscenarios()

        {

            var def = GetDefaultEscenarioParams();

            var lista = new List<V5_EscenarioConfig>();

            var mercados = new[] { "NQ", "MNQ", "ES", "MES", "CL", "GC" };

            int[] tfs = { 377, 377, 610, 610, 5, 5 };

            string[] names = { "Baja", "Media", "Alta", "Extrema", "SuperExtrema" };

            double[] mins = { 0, 18, 22, 27, 32 };

            double[] maxs = { 18, 22, 27, 32, -1 };

            for (int m = 0; m < mercados.Length; m++)

            {

                for (int s = 0; s < names.Length; s++)

                {

                    lista.Add(new V5_EscenarioConfig

                    {

                        Market = mercados[m],

                        Timeframe = tfs[m],

                        ScenarioName = names[s],

                        KeltWidth_Min = mins[s],

                        KeltWidth_Max = maxs[s],

                        Atr_Min = mins[s],

                        Atr_Max = maxs[s],

                        AVWAP_SwingPeriod = def.AVWAP_SwingPeriod,

                        AVWAP_BaseAPT = def.AVWAP_BaseAPT,

                        AVWAP_AdptAPT = def.AVWAP_AdptAPT,

                        AVWAP_VolBIAS = def.AVWAP_VolBIAS,

                        TrialBars = def.TrialBars,

                        LabelOffset = def.LabelOffset,

                        ZoneVerde_ATR = def.ZoneVerde_ATR,

                        ZoneAmarillo_ATR = def.ZoneAmarillo_ATR,

                        StopATRx = def.StopATRx,

                        StopPct = def.StopPct,

                        StopMaxTicks = def.StopMaxTicks,

                        RR1 = def.RR1,

                        RR2 = def.RR2,

                        Kelt_Period_Signal = def.Kelt_Period_Signal,

                        Kelt_Mult_Signal = def.Kelt_Mult_Signal

                    });

                }

            }

            return lista.ToArray();

        }



        private void DetectarEscenario()

        {

            if (_keltnerVol == null || !_keltnerVol.IsReady || _escenarios == null || _escenarios.Length == 0) return;

            double valorMedido = ModoDeteccionVol == V5_DeteccionModo.AtrRange ? _atrVal : _keltnerVol.Width;



            string mercadoStr = MercadoToString(_mercadoDetectado);

            int tf = GetCurrentTimeframe();



            var filtrados = FiltrarEscenariosPorMercado(_escenarios, mercadoStr, tf);

            if (filtrados.Length == 0)

            {

                filtrados = FiltrarEscenariosPorMercado(_escenarios, "NQ", 377);

                if (filtrados.Length == 0) { filtrados = _escenarios; }

                _configNoEncontrada = true;

            }

            else _configNoEncontrada = false;



            _escenarioActual = filtrados[0].ScenarioName;

            _escenarioConfigActual = filtrados[0];

            for (int i = 0; i < filtrados.Length; i++)

            {

                var e = filtrados[i];

                if (ModoDeteccionVol == V5_DeteccionModo.AtrRange)

                {

                    if (e.Atr_Max < 0)

                    {

                        if (valorMedido >= e.Atr_Min)

                        {

                            _escenarioActual = e.ScenarioName;

                            _escenarioConfigActual = e;

                        }

                    }

                    else if (valorMedido >= e.Atr_Min && valorMedido < e.Atr_Max)

                    {

                        _escenarioActual = e.ScenarioName;

                        _escenarioConfigActual = e;

                        break;

                    }

                }

                else

                {

                    if (e.KeltWidth_Max < 0)

                    {

                        if (valorMedido >= e.KeltWidth_Min)

                        {

                            _escenarioActual = e.ScenarioName;

                            _escenarioConfigActual = e;

                        }

                    }

                    else if (valorMedido >= e.KeltWidth_Min && valorMedido < e.KeltWidth_Max)

                    {

                        _escenarioActual = e.ScenarioName;

                        _escenarioConfigActual = e;

                        break;

                    }

                }

            }



            if (_avwap != null && _escenarioConfigActual.AVWAP_SwingPeriod > 0)

            {

                _avwap.UpdateParams(_escenarioConfigActual.AVWAP_SwingPeriod, _escenarioConfigActual.AVWAP_BaseAPT, _escenarioConfigActual.AVWAP_AdptAPT, _escenarioConfigActual.AVWAP_VolBIAS);

            }

            if (_keltnerSignal != null && _escenarioConfigActual.Kelt_Period_Signal > 0)

            {

                _keltnerSignal.UpdateParams(_escenarioConfigActual.Kelt_Period_Signal, _escenarioConfigActual.Kelt_Mult_Signal);

            }

        }



        #region Auto-deteccion de mercado



        private V5_MercadoTipo _mercadoDetectado = V5_MercadoTipo.Auto;

        private int _autoTickCount = 377;

        private bool _mercadoNoReconocido = false;



        private void DetectarMercado()

        {

            if (Market_Type != V5_MercadoTipo.Auto)

            {

                _mercadoDetectado = Market_Type;

                _mercadoNoReconocido = false;

                AsignarParametrosMercado();

                return;

            }

            _mercadoNoReconocido = true;

            if (Instrument == null) return;

            string name = Instrument.MasterInstrument.Name.ToUpperInvariant();

            if (name == "NQ" || name == "NQ FUTURE" || name.Contains("NQ"))

            {

                _mercadoDetectado = V5_MercadoTipo.NQ; _mercadoNoReconocido = false;

            }

            else if (name == "MNQ" || name.Contains("MNQ"))

            {

                _mercadoDetectado = V5_MercadoTipo.MNQ; _mercadoNoReconocido = false;

            }

            else if (name == "ES" || name == "ES FUTURE" || name.Contains("ES "))

            {

                _mercadoDetectado = V5_MercadoTipo.ES; _mercadoNoReconocido = false;

            }

            else if (name == "MES" || name.Contains("MES"))

            {

                _mercadoDetectado = V5_MercadoTipo.MES; _mercadoNoReconocido = false;

            }

            else if (name == "CL" || name == "CRUDE OIL" || name.Contains("CL "))

            {

                _mercadoDetectado = V5_MercadoTipo.CL; _mercadoNoReconocido = false;

            }

            else if (name == "GC" || name == "GOLD" || name.Contains("GC "))

            {

                _mercadoDetectado = V5_MercadoTipo.GC; _mercadoNoReconocido = false;

            }

            AsignarParametrosMercado();

        }



        private void AsignarParametrosMercado()

        {

            switch (_mercadoDetectado)

            {

                case V5_MercadoTipo.NQ: _autoTickCount = 377; break;

                case V5_MercadoTipo.MNQ: _autoTickCount = 377; break;

                case V5_MercadoTipo.ES: _autoTickCount = 610; break;

                case V5_MercadoTipo.MES: _autoTickCount = 610; break;

                case V5_MercadoTipo.CL: _autoTickCount = 0; break; // minute-based

                case V5_MercadoTipo.GC: _autoTickCount = 0; break;

            }

        }



        #endregion



        #region Internal AVWAP (ported from EduS_DynamicSwingAVWAP)



        private class V5_InternalAVWAP

        {

            private int _swingPeriod, _baseAPT;

            private bool _adptAPT;

            private double _volBIAS;

            private List<double> _prices = new List<double>();

            private List<double> _volumes = new List<double>();

            private double _swingHigh = double.NaN, _swingLow = double.NaN;

            private double _currentVWAP = double.NaN;

            private int _barsSinceSwing = 0;

            private bool _directionUp = true;

            private double _prevVWAP = double.NaN;



            public V5_InternalAVWAP(int swingPeriod, int baseAPT, bool adptAPT, double volBIAS)

            {

                _swingPeriod = swingPeriod; _baseAPT = baseAPT; _adptAPT = adptAPT; _volBIAS = volBIAS;

            }



            public void UpdateParams(int swingPeriod, int baseAPT, bool adptAPT, double volBIAS)

            {

                _swingPeriod = swingPeriod;

                _baseAPT = baseAPT;

                _adptAPT = adptAPT;

                _volBIAS = volBIAS;

            }



            public void Update(double high, double low, double close, double volume)

            {

                _prices.Add(close);

                _volumes.Add(volume);

                int lookback = _adptAPT ? Math.Max(_baseAPT, (int)(volume / (_volBIAS > 0 ? _volBIAS : 1))) : _baseAPT;

                if (_prices.Count > Math.Max(200, lookback * 2)) { _prices.RemoveAt(0); _volumes.RemoveAt(0); }



                double sumPV = 0, sumV = 0;

                int start = Math.Max(0, _prices.Count - lookback);

                for (int i = start; i < _prices.Count; i++)

                {

                    sumPV += _prices[i] * _volumes[i];

                    sumV += _volumes[i];

                }

                _currentVWAP = sumV > 0 ? sumPV / sumV : close;

                _directionUp = _currentVWAP >= _prevVWAP;

                _prevVWAP = _currentVWAP;



                _barsSinceSwing++;

                if (_barsSinceSwing >= _swingPeriod)

                {

                    _swingHigh = _currentVWAP;

                    _swingLow = _currentVWAP;

                    _barsSinceSwing = 0;

                }

                if (!double.IsNaN(_swingHigh)) _swingHigh = Math.Max(_swingHigh, _currentVWAP);

                if (!double.IsNaN(_swingLow)) _swingLow = Math.Min(_swingLow, _currentVWAP);

            }



            public double Value => _currentVWAP;

            public bool Up => _directionUp;

            public double SwingHigh => _swingHigh;

            public double SwingLow => _swingLow;

            public bool IsReady => _prices.Count >= _baseAPT;

        }



        #endregion



        #region Internal state



        private V5_InternalSMA _sma20, _sma80;

        private V5_InternalLinReg _linReg89;

        private V5_InternalATR _atr;

        private V5_InternalAVWAP _avwap;

        private V5_InternalAVWAP _avwapSignal;

        private V5_InternalKeltner _keltnerVol, _keltnerSignal;



        internal bool v_sma20Up, v_sma80Up, v_lrUp, v_lrDown, v_vwapUp;

        internal double v_sma20Val, v_sma80Val, v_lrVal, v_vwapVal;

        internal double _atrVal;

        internal double Sma20Slope => (_sma20 != null && _sma20.IsReady) ? (_sma20[0] - _sma20[1]) : 0;
        internal double Sma80Slope => (_sma80 != null && _sma80.IsReady) ? (_sma80[0] - _sma80[1]) : 0;
        internal double LinRegSlope => (_linReg89 != null && _linReg89.IsReady) ? _linReg89.Slope : 0;

        private int _tendenciaCount;

        private V5_TendenciaColor _colorSemaforo = V5_TendenciaColor.Rojo;

        private V5_ZonaTipo _zonaActual = V5_ZonaTipo.Ninguna;

        private string _zonaLabel = "--";

        private bool _zonaPlus;

        private double _zonaDistATR;

        private int _zonaRanking;

        private List<V5_ZonaInfo> _zonasActivas = new List<V5_ZonaInfo>();

        internal double _keltnerVolWidth, _keltnerSignalWidth;

        internal double _keltnerVolBM, _keltnerSignalBM;



        // ── Variables de cruce SMA20/80 ─────────────────────────────────────────

        private double _cruceSMA20_80_Dir = 0;      // +1 alcista / -1 bajista / 0 sin cruce

        private int _cruceSMA20_80_Barras = 0;

        private int _retrocesoCount = 0;

        private double _ultimoCrucePrecio = double.NaN;

        private double _ultimoCruceSMA20 = double.NaN;

        private bool _huboCruce = false;            // true cuando SMA20 y SMA80 están cruzadas (cualquier dirección)

        private bool _cruceInicializado = false;



        // FIX-001 (2026-05-13): Variables nuevas para detección de retrocesos REALES a la BM.

        // Bug original: _retrocesoCount subía cada barra donde el precio estuviera "cerca" de la BM,

        // causando que M1/M2/M3 se activaran inmediatamente y de forma continua sin un retroceso real.

        // Fix: ahora se detecta la entrada Y salida de la zona BM (ciclo completo = 1 retroceso).

        private bool _enZonaBM = false;             // precio actualmente dentro de la zona tolerancia BM

        private bool _salidaZonaBMPendiente = false; // flag auxiliar para confirmar salida limpia



        // FIX-002 (2026-05-13): Cooldown post-señal para evitar múltiples señales consecutivas

        // en la misma zona. Bug original: sin cooldown se generaban decenas de señales ABRE por

        // sesión (el CSV llegó a 57K señales en 1 instrumento). Ahora hay un período de espera

        // configurable (CooldownBars) antes de poder generar una nueva señal.

        private bool _enCooldown = false;

        private int _barrasDesdeUltimaSenal = 0;



        internal bool _entradaValida, _esAlcista;

        internal double _eEntrada = double.NaN, _eStop = double.NaN, _eStopPts;

        internal double _eT1 = double.NaN, _eT1Pts, _eRR1;

        internal double _eT2 = double.NaN, _eT2Pts, _eRR2;

        internal bool _prevEntradaValida = false;

        private int _prevTendenciaCount4 = 0;



        private bool _ultimaEntradaValida = false, _ultimaEntradaActiva = false;

        private string _ultimaFechaHora = "", _ultimaZonaLabel = "";

        private double _ultimaEntrada = double.NaN, _ultimaStop = double.NaN, _ultimaStopPts = 0;

        private double _ultimaT1 = double.NaN, _ultimaT1Pts = 0, _ultimaRR1 = 0;

        private double _ultimaT2 = double.NaN, _ultimaT2Pts = 0, _ultimaRR2 = 0;



        private double _tickSize;

        private double _pointValue;



        private string _instrKey = "";

        private string _logDirectory, _logFilePath;

        private readonly object _logLock = new object();



        private V5_VolumeProfile _volProfile;

        private V5_NodePocEngine _nodePocEngine;

        private V5_InternalSMA_HTF _smaHTF;

        internal double _pocVal, _antiPocUpVal, _antiPocDownVal, _lvnVal;
        private double _sessionVolume = 0;

        private bool _nakedPocActivo;

        private string _perfilModoLabel = "";

        private bool _pasaHTF = true, _pasaNodos = true, _pasaPocs = true;



        private Account _account;

        private DispatcherTimer _posTimer;

        private bool _windowCreated = false;

        private System.Windows.Window _ownWindow;

        private V5_MasterHudPanel _ownPanel;

        private bool _ownWindowClosed = false;



        #endregion



        protected override void OnStateChange()

        {

            if (State == State.SetDefaults)

            {

                Description = "EduS Master Panel HUD V5 -- Independiente, 7 zonas, ranking, escenarios volatilidad.";

                Name = "EduS_MasterPanel_HUD_V5";

                Calculate = Calculate.OnBarClose;

                IsOverlay = true;

                DisplayInDataBox = false;

                DrawOnPricePanel = true;

                IsSuspendedWhileInactive = true;

                ScaleJustification = ScaleJustification.Right;



                Market_Type = V5_MercadoTipo.Auto;

                BarType_Tick = true;

                RutaLog = @"C:\Users\eduar\OneDrive - Desarrollo Personal\Documents - Operativa Diaria\EduSTrader - Local Free";

                NombreArchivoLog = "senales-v5.csv";

                RegistrarSenales = true;

                LoggearHistorico = false;

                // Fix #9: parámetros de simulación para el modelo de backtesting
                BT_LimitSlippageATRs  = 0.2;   // mismo default que la estrategia
                BT_BEPlusTicks        = 4;     // mismo default que la estrategia
                BT_LimitTimeoutBarras = 5;     // máx barras esperando fill antes de CANCELADA

                ConfigCSV = "V5_VolatilityConfig.csv";

                ModoDeteccionVol = V5_DeteccionModo.KeltnerWidth;

                DireccionOperacion = V5_DireccionModo.Ambos;  // V5.1: por defecto opera en ambas direcciones



                Ventana_Flotante = true;

                Mostrar_En_Chart = true;

                Hud_X = 20; Hud_Y = 20;
                Hud_Ancho = 340;

                Mostrar_Semaforo = true;

                Mostrar_Senales = true;

                Mostrar_Entrada = true;

                Mostrar_Keltner = true;

                Mostrar_Posicion = true;

                AccountName = "Sim101";

                Pos_UpdateSeg = 3;



                SMA20_Period = 20;

                SMA80_Period = 80;

                LinReg_Period = 89;

                LinReg_SlopeThreshold = 0.0;

                ATR_Period = 14;

                AVWAP_SwingPeriod = 50;

                AVWAP_BaseAPT = 20;

                AVWAP_AdptAPT = true;

                AVWAP_VolBIAS = 10.0;

                Kelt_Period_Vol = 52;

                Kelt_Mult_Vol = 3.5;



                Ranking_A = 1; Ranking_LR = 2; Ranking_20 = 3; Ranking_80 = 4;

                Ranking_M1 = 5; Ranking_M2 = 6; Ranking_M3 = 7;

                ZonaToleranciaPct = 0.3;



                StopATRx = 0.5;

                StopPct = 0.003;

                StopMaxTicks = 0;

                StopMinTicks = 4;

                Kelt_StopPct = 0.30;

                RR1_Ratio = 2.0;

                RR2_Ratio = 3.0;

                RR_MinFiltro = 1.5;



                // FIX-002 (2026-05-13): CooldownBars — barras mínimas entre señales consecutivas

                // de la misma zona para evitar el problema de ~57K señales por instrumento.

                CooldownBars = 3;



                UsarCapa3 = false;

                UsarCapa4 = false;

                ToleranciaBM_ATR = 1.5;

                UsarFiltroHTF = false;

                HTF_Ticks = 987;

                HTF_Minutos = 15;

                UsarNodos = false;

                Nodo_DistTicks = 8;

                UsarPocs = false;

                Poc_DistTicks = 8;



                PerfilModo = 0;

                Perfil_DesdeHora = 930;

                WindowMinutes = 60;

                UsarRanking = true;



                // Fix #10: rutas corregidas a los archivos de sonido reales definidos en la carpeta NinjaTrader 8\sounds
                SonidoGreenZone = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "sounds", "green zone.wav");
                SonidoInZone = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "sounds", "in zone.wav");

                Senales_OffsetIzq = 100;
                Senales_FondoColor = Brushes.Transparent;
                Senales_LineStyle = DashStyleHelper.Solid;
            }

            else if (State == State.Configure)

            {
                // Fix #10b: Si la plantilla o el usuario guardó solo el nombre del archivo sin ruta absoluta, resolver la ruta real:
                if (!string.IsNullOrEmpty(SonidoGreenZone) && !System.IO.Path.IsPathRooted(SonidoGreenZone))
                    SonidoGreenZone = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "sounds", SonidoGreenZone);

                if (!string.IsNullOrEmpty(SonidoInZone) && !System.IO.Path.IsPathRooted(SonidoInZone))
                    SonidoInZone = System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "sounds", SonidoInZone);

                _sma20 = new V5_InternalSMA(SMA20_Period);

                _sma80 = new V5_InternalSMA(SMA80_Period);

                _linReg89 = new V5_InternalLinReg(LinReg_Period);

                _atr = new V5_InternalATR(ATR_Period);

                _avwap = new V5_InternalAVWAP(AVWAP_SwingPeriod, AVWAP_BaseAPT, AVWAP_AdptAPT, AVWAP_VolBIAS);

                _avwapSignal = new V5_InternalAVWAP(50, 20, true, 10.0);

                _keltnerVol = new V5_InternalKeltner(Kelt_Period_Vol, Kelt_Mult_Vol);

                _keltnerSignal = new V5_InternalKeltner(20, 2.0);



                DetectarMercado();

                CargarEscenarios();



                double ts = Instrument != null ? Instrument.MasterInstrument.TickSize : 1;

                _volProfile = new V5_VolumeProfile(ts, PerfilModo, Perfil_DesdeHora, WindowMinutes);

                _nodePocEngine = new V5_NodePocEngine(ts);



                if (UsarFiltroHTF && BarsPeriod != null)

                {

                    bool chartIsTick = BarsPeriod.BarsPeriodType == BarsPeriodType.Tick;

                    if (chartIsTick)

                        AddDataSeries(BarsPeriodType.Tick, Math.Max(1, HTF_Ticks));

                    else

                        AddDataSeries(BarsPeriodType.Minute, Math.Max(1, HTF_Minutos));

                }

                _smaHTF = new V5_InternalSMA_HTF(SMA20_Period);



                if (RegistrarSenales)

                {

                    try

                    {

                        _logDirectory = string.IsNullOrWhiteSpace(RutaLog) ? System.IO.Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "EduS_Logs") : RutaLog;

                        System.IO.Directory.CreateDirectory(_logDirectory);

                        string fileName = string.IsNullOrWhiteSpace(NombreArchivoLog) ? "senales-v5.csv" : NombreArchivoLog;

                        if (!fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) fileName += ".csv";

                        _logFilePath = System.IO.Path.Combine(_logDirectory, fileName);

                        if (!File.Exists(_logFilePath))

                        {

                            var header = "Tipo,SignalID,FechaHora,Mercado,Cuenta,Timeframe,Direccion,Escenario,Zona,ZonaRanking,Capas_Activas,HTF_OK,Nodos_OK,Pocs_OK,Entrada,Stop,T1,T2,RR1,RR2,StopTicks,StopUSD,ATR,KeltnerWidth,AVWAP,LinReg,SMA20,SMA80,TendenciaCount,FechaCierre,Resultado,T1_Tocado,T2_Tocado,PrecioSalida,BarrasDuracion";

                            File.WriteAllText(_logFilePath, header + Environment.NewLine);

                        }

                    }

                    catch { }

                }

            }

            else if (State == State.DataLoaded)

            {

                _tickSize = Instrument != null ? Instrument.MasterInstrument.TickSize : 1;

                _pointValue = Instrument != null ? Instrument.MasterInstrument.PointValue : 1;

                _instrKey = Instrument != null ? Instrument.MasterInstrument.Name : "---";



                if (Ventana_Flotante && !_windowCreated)

                {

                    _windowCreated = true;

                    var disp = GetUI();

                    if (disp != null) disp.Invoke((Action)(() => AbrirVentana(_instrKey)));

                }

            }

            else if (State == State.Terminated)

            {

                try { if (_posTimer != null) { _posTimer.Stop(); _posTimer = null; } } catch { }

                try { if (_ownWindow != null && !_ownWindowClosed) _ownWindow.Close(); } catch { }

                _ownPanel = null; _ownWindow = null;

                DisposeDXResources();

            }

        }



        protected override void OnBarUpdate()

        {

            if (BarsInProgress == 1 && UsarFiltroHTF && _smaHTF != null)

            {

                _smaHTF.Update(Close[0]);

                return;

            }

            if (BarsInProgress != 0) return;

            if (Bars.IsFirstBarOfSession) _sessionVolume = 0;
            _sessionVolume += Volume[0];

            if (CurrentBar < Math.Max(SMA20_Period, Math.Max(SMA80_Period, Math.Max(LinReg_Period, ATR_Period + 5)))) return;



            double h = High[0], l = Low[0], c = Close[0], v = Volume[0];



            _sma20.Update(c);

            _sma80.Update(c);

            _linReg89.Update(c);

            _atr.Update(h, l, c);

            _avwap.Update(h, l, c, v);

            _avwapSignal.Update(h, l, c, v);

            _keltnerVol.Update(h, l, c);

            _keltnerSignal.Update(h, l, c);



            // V5.1 Fix Bug#5: agregar _keltnerSignal.IsReady para evitar bmMid=0 durante warm-up
            if (!_sma20.IsReady || !_sma80.IsReady || !_linReg89.IsReady || !_atr.IsReady || !_keltnerVol.IsReady || !_keltnerSignal.IsReady) return;



            _atrVal = Math.Max(_atr.Current, TickSize);

            v_sma20Val = _sma20.Current;

            v_sma80Val = _sma80.Current;

            v_lrVal = _linReg89.Current;

            v_vwapVal = _avwap.Value;



            // FIX-BUG2: Medir si el precio está POR ENCIMA de la SMA (posición), no si la SMA está subiendo (pendiente).
            // Una SMA puede seguir subiendo varios ticks mientras el precio ya cae, inflando el semáforo incorrectamente.
            v_sma20Up = Close[0] > _sma20.Current;
            v_sma80Up = Close[0] > _sma80.Current;

            double slope = _linReg89.Slope;

            double slopeThreshold = LinReg_SlopeThreshold;

            v_lrUp = slope > slopeThreshold;
            v_lrDown = slope < -slopeThreshold;

            // lrFlat variable removed as it is not used here

            v_vwapUp = _avwap.Up;



            DetectarEscenario();



            _volProfile.Update(h, l, v, Time[0], CurrentBar);

            _volProfile.CheckSwingAnchor(_avwap.SwingHigh, _avwap.SwingLow, CurrentBar);

            _pocVal = _volProfile.POC;

            _antiPocUpVal = _volProfile.AntiPOC_Up;

            _antiPocDownVal = _volProfile.AntiPOC_Down;

            _lvnVal = _volProfile.LVN;

            _nakedPocActivo = _volProfile.PrevPOCSet && !_volProfile.CheckPocTouched(h, l);

            switch (PerfilModo)

            {

                case 0: _perfilModoLabel = "SWING"; break;

                case 1: _perfilModoLabel = "PREV"; break;

                case 2: _perfilModoLabel = "HR" + Perfil_DesdeHora; break;

            }



            if (UsarFiltroHTF && _smaHTF != null && _smaHTF.IsReady)

            {

                double htfCurrent = _smaHTF.Current;

                // FIX-BUG3: htfUp debe ser TRUE cuando el precio está POR ENCIMA de la SMA HTF (condición alcista).
                // ANTES: htfUp = htfCurrent > c  → era TRUE cuando el precio estaba BAJO la SMA (bajista = invertido).
                bool htfUp = c > htfCurrent;

                _pasaHTF = v_vwapUp == htfUp;

            }

            if (UsarNodos && _nodePocEngine != null)

            {

                _nodePocEngine.UpdateNodes(h, l, v, Time[0]);

                _pasaNodos = _nodePocEngine.CheckNodeProximity(c, Nodo_DistTicks);

            }

            if (UsarPocs && _nodePocEngine != null)

            {

                _nodePocEngine.UpdatePocs(h, l, v, Time[0]);

                _pasaPocs = _nodePocEngine.CheckPocProximity(c, Poc_DistTicks);

            }



            _keltnerVolWidth = _keltnerVol.Width;

            _keltnerVolBM = _keltnerVol.Mid;

            _keltnerSignalWidth = _keltnerSignal.Width;

            _keltnerSignalBM = _keltnerSignal.Mid;



            double bmTol = ToleranciaBM_ATR * _atrVal;

            _v_vwapSobreBM = v_vwapUp ? (v_vwapVal > _keltnerVolBM - bmTol) : (v_vwapVal < _keltnerVolBM + bmTol);

            _v_lrSobreBM = v_vwapUp ? (v_lrVal > _keltnerVolBM - bmTol) : (v_lrVal < _keltnerVolBM + bmTol);



            ActualizarCruceSMA();

            _tendenciaCount = CalcularSemaforo();

            _colorSemaforo = EvaluarColorSemaforo();



            DetectarZonaActiva();

            EvaluarEntrada();

            Logging();

            CheckActiveEntries();



            if (State == State.Realtime)

            {

                // Fix #10: Verificar existencia del archivo de sonido antes de Alert();
                //          loggear en el Output si no se encuentra para diagnóstico.
                if (_tendenciaCount >= 4 && _prevTendenciaCount4 < 4)

                {

                    try

                    {

                        if (!string.IsNullOrEmpty(SonidoGreenZone) && File.Exists(SonidoGreenZone))
                        {
                            Alert("V5_GreenZone_" + CurrentBar, Priority.High, "4/4 " + Instrument.FullName + " " + _zonaLabel, SonidoGreenZone, 10, Brushes.Lime, Brushes.Black);
                            PlaySound(SonidoGreenZone);
                        }

                        else

                            Print("[HUD V5] Fix#10 AVISO: Archivo sonido GreenZone no encontrado: " + SonidoGreenZone);

                    }

                    catch (Exception ex) { Print("[HUD V5] Fix#10 Error alerta GreenZone: " + ex.Message); }

                }

                if (_entradaValida && !_prevEntradaValida)

                {

                    try

                    {

                        if (!string.IsNullOrEmpty(SonidoInZone) && File.Exists(SonidoInZone))
                        {
                            Alert("V5_InZone_" + CurrentBar, Priority.Medium, "Señal " + _zonaLabel + " " + Instrument.FullName, SonidoInZone, 10, Brushes.Gold, Brushes.Black);
                            PlaySound(SonidoInZone);
                        }

                        else

                            Print("[HUD V5] Fix#10 AVISO: Archivo sonido InZone no encontrado: " + SonidoInZone);

                    }

                    catch (Exception ex) { Print("[HUD V5] Fix#10 Error alerta InZone: " + ex.Message); }

                }

                _prevTendenciaCount4 = _tendenciaCount;

            }

            _prevEntradaValida = _entradaValida;



            if (Ventana_Flotante && _ownPanel != null && !_ownWindowClosed)

            {

                var snap = BuildSnap();

                GetUI()?.InvokeAsync(() => { try { _ownPanel.Update(snap); } catch { } });

            }



            if (Mostrar_En_Chart)

            {

                try { ChartControl?.InvalidateVisual(); } catch { }

            }

        }



        // V5.1 FIX Bug#1: Semáforo bidireccional — cuenta indicadores alineados con la tendencia AVWAP
        // En bajista, precio < SMA20/80 y LR bajando = todos alineados DOWN → count=4 → VerdeOscuro → señales SHORT habilitadas
        private int CalcularSemaforo()
        {
            bool esAlcista = v_vwapUp;  // AVWAP define la tendencia
            bool lrAlin    = esAlcista ? v_lrUp   : v_lrDown;    // LR a favor de la tendencia
            bool sma20Alin = esAlcista ? v_sma20Up : !v_sma20Up; // precio vs SMA20 a favor
            bool sma80Alin = esAlcista ? v_sma80Up : !v_sma80Up; // precio vs SMA80 a favor
            // AVWAP siempre cuenta 1 (es el ancla que define la tendencia)

            // V5.2: LR Flat (débil pero a favor) suma puntos
            bool lrFlat    = esAlcista 
                ? (_linReg89.Slope > 0 && _linReg89.Slope <= LinReg_SlopeThreshold)
                : (_linReg89.Slope < 0 && _linReg89.Slope >= -LinReg_SlopeThreshold);
            bool lrOk = lrAlin || lrFlat;

            return (lrOk ? 1 : 0) + 1 + (sma20Alin ? 1 : 0) + (sma80Alin ? 1 : 0);
        }

        // V5.1 FIX Bug#2: VerdeClaro acepta LR+SMA80 (Zona 80 no requiere SMA20 a favor)
        private V5_TendenciaColor EvaluarColorSemaforo()
        {
            bool esAlcista = v_vwapUp;
            bool lrAlin    = esAlcista ? v_lrUp   : v_lrDown;
            bool sma20Alin = esAlcista ? v_sma20Up : !v_sma20Up;
            bool sma80Alin = esAlcista ? v_sma80Up : !v_sma80Up;

            // V5.2: LR Flat (débil pero a favor) es válido para semáforo Amarillo/VerdeClaro
            bool lrFlat    = esAlcista 
                ? (_linReg89.Slope > 0 && _linReg89.Slope <= LinReg_SlopeThreshold)
                : (_linReg89.Slope < 0 && _linReg89.Slope >= -LinReg_SlopeThreshold);
            bool lrOk = lrAlin || lrFlat;

            int count = (lrOk ? 1 : 0) + 1 + (sma20Alin ? 1 : 0) + (sma80Alin ? 1 : 0); // AVWAP=1 siempre

            if (count == 4 && lrAlin)                    return V5_TendenciaColor.VerdeOscuro;
            if (count == 4 && lrFlat)                    return V5_TendenciaColor.VerdeClaro;
            if (count == 3 && lrOk && sma20Alin)         return V5_TendenciaColor.VerdeClaro;
            if (count == 3 && lrOk && sma80Alin)         return V5_TendenciaColor.VerdeClaro; // V5.1: Zona 80 valida sin SMA20
            if (count >= 2 && lrOk)                      return V5_TendenciaColor.Amarillo;
            return V5_TendenciaColor.Rojo;
        }



        // =====================================================================

        // FIX-001 (2026-05-13): ActualizarCruceSMA — Cruce bilateral + retrocesos reales

        //

        // Bugs originales corregidos:

        //   A) _huboCruce solo era true cuando SMA20 > SMA80 (cruce alcista). Las zonas

        //      20/M1/M2/M3 NUNCA se activaban en tendencias bajistas porque _huboCruce

        //      era false cuando SMA20 < SMA80.

        //      Fix: _huboCruce = true cuando CUALQUIER cruce está vigente (alcista O bajista).

        //      La dirección del cruce se guarda en _cruceSMA20_80_Dir (+1 / -1).

        //

        //   B) _retrocesoCount subía 1 cada barra donde Close estuviera cerca de la BM,

        //      sin importar si el precio había retrocedido y rebotado de verdad.

        //      Consecuencia: después de 3 barras laterales cerca de la BM, _retrocesoCount

        //      llegaba a 3 y M3 era la zona activa permanentemente.

        //      Fix: se detecta ENTRADA a zona BM (_enZonaBM = true) y luego SALIDA

        //      (_enZonaBM = false). Solo al salir se incrementa _retrocesoCount.

        //      Eso garantiza que 1 retroceso = 1 toque + 1 rebote completo.

        // =====================================================================

        private void ActualizarCruceSMA()

        {

            if (!_sma20.IsReady || !_sma80.IsReady) return;

            double sma20Cur = _sma20[0];

            double sma80Cur = _sma80[0];

            if (double.IsNaN(sma20Cur) || double.IsNaN(sma80Cur)) return;



            // Estado actual del cruce: +1 si SMA20>SMA80, -1 si SMA20<SMA80, 0 si son iguales

            bool cruceAlcista  = sma20Cur > sma80Cur;

            bool cruceBajista  = sma20Cur < sma80Cur;

            double dirActual   = cruceAlcista ? 1.0 : (cruceBajista ? -1.0 : 0.0);

            bool hayCrece      = cruceAlcista || cruceBajista;



            if (!_cruceInicializado)

            {

                // Primera barra con ambas SMAs listas: registrar estado inicial sin contar retrocesos

                _cruceInicializado        = true;

                _huboCruce                = hayCrece;

                _cruceSMA20_80_Dir        = dirActual;

                _cruceSMA20_80_Barras     = 0;

                _retrocesoCount           = 0;

                _enZonaBM                 = false;

                _salidaZonaBMPendiente    = false;

                return;

            }



            // FIX-001-A: Detectar cambio de dirección del cruce → reset completo de contadores

            // Un cambio de +1 a -1 o viceversa significa que las SMAs se volvieron a cruzar.

            bool cambioDeDir = (dirActual != 0.0) && (dirActual != _cruceSMA20_80_Dir);

            if (cambioDeDir)

            {

                _huboCruce             = true;

                _cruceSMA20_80_Dir     = dirActual;

                _cruceSMA20_80_Barras  = 0;

                _retrocesoCount        = 0;               // reset: nuevo cruce = contar retrocesos desde cero

                _enZonaBM              = false;

                _salidaZonaBMPendiente = false;

                _ultimoCrucePrecio     = Close[0];

                _ultimoCruceSMA20      = sma20Cur;

                return;

            }



            // Sin cambio de dirección: actualizar contadores

            _huboCruce = hayCrece;

            if (!_huboCruce || _keltnerSignal == null) return;



            _cruceSMA20_80_Barras++;



            // FIX-001-B: Detección de retrocesos REALES a la BM del Keltner Volatilidad

            // V5.2: Cambiado de _keltnerSignalBM → _keltnerVolBM para usar el canal ancho

            // Un retroceso real = precio ENTRA a zona BM → precio SALE de zona BM (rebota)

            double bmMid = _keltnerVolBM;

            if (double.IsNaN(bmMid) || _atrVal <= 0) return;



            double distBM     = Math.Abs(Close[0] - bmMid);

            double tolerancia = _atrVal * ZonaToleranciaPct;

            bool enZonaAhora  = distBM <= tolerancia;



            if (!_enZonaBM && enZonaAhora)

            {

                // Precio acaba de entrar a la zona BM → inicio del retroceso

                _enZonaBM              = true;

                _salidaZonaBMPendiente = false;

            }

            else if (_enZonaBM && !enZonaAhora)

            {

                // Precio salió de la zona BM → retroceso completado (toque + rebote)

                _enZonaBM              = false;

                _salidaZonaBMPendiente = true;



                if (_retrocesoCount < 3)

                    _retrocesoCount++;      // FIX: se incrementa SOLO al salir, no cada barra dentro

            }

        }



        // =====================================================================

        // FIX-005 (2026-05-13): DetectarZonaActiva — Correcciones de lógica de zonas

        //

        // Bugs originales corregidos:

        //   A) Zonas 20 y M: no verificaban que el cruce SMA20/80 estuviera a favor

        //      de la tendencia AVWAP. Bug: en tendencia bajista (AVWAP down) con

        //      SMA20 > SMA80 (cruce alcista), la zona 20 se activaba igualmente, lo

        //      cual es incorrecto según la definición del sistema.

        //      Fix: se valida que _cruceSMA20_80_Dir concuerde con la dirección de AVWAP.

        //

        //   B) Zona 80: no verifica que el precio esté del lado correcto de SMA80.

        //      Fix: se agrega validación explícita ladoCorrecto80 usando AVWAP como

        //      referencia de tendencia, consistente con el resto del sistema.

        // =====================================================================

        private void DetectarZonaActiva()

        {

            _zonaActual    = V5_ZonaTipo.Ninguna;

            _zonaLabel     = "--";

            _zonaPlus      = false;

            _zonaDistATR   = 0;

            _zonaRanking   = 999;

            _zonasActivas.Clear();



            double pr      = Close[0];

            double atr     = _atrVal;

            double tol     = atr * ZonaToleranciaPct;



            // V5.1: Variables alineadas con la tendencia (definida por AVWAP)
            bool esAlcista = v_vwapUp; 
            bool lrAlin    = esAlcista ? v_lrUp    : v_lrDown;
            bool sma20Alin = esAlcista ? v_sma20Up : !v_sma20Up;
            bool sma80Alin = esAlcista ? v_sma80Up : !v_sma80Up;
            // V5.2: lrFlat ahora es direccional y estricto (cercano a 0, pero NO 0 y NUNCA en contra)
            bool lrFlat    = esAlcista 
                ? (_linReg89.Slope > 0 && _linReg89.Slope <= LinReg_SlopeThreshold)
                : (_linReg89.Slope < 0 && _linReg89.Slope >= -LinReg_SlopeThreshold);

            double lrVal   = v_lrVal;
            double avwapVal= v_vwapVal;
            double sma20Val= v_sma20Val;
            double sma80Val= v_sma80Val;
            double bmMid   = _keltnerVolBM; // V5.2: usa KeltnerVol BM (antes era _keltnerSignalBM)

            // Alcista si AVWAP apunta arriba (precio sobre AVWAP)
            bool alcista   = esAlcista;

            void AddZona(V5_ZonaTipo tipo, string label, bool plus, double distATR, int ranking)
            {
                if (ranking == 0) return;
                _zonasActivas.Add(new V5_ZonaInfo { Tipo = tipo, Label = label, Plus = plus, DistATR = distATR, Ranking = ranking });
            }

            // ── Zona A / A+ (retroceso a AVWAP) ─────────────────────────────
            // Requiere: todos los 4 indicadores a favor de la tendencia (LR puede estar plana)
            if (sma20Alin && sma80Alin && (lrAlin || lrFlat))
            {
                double dA  = Math.Abs(pr - avwapVal) / atr;
                // V5.1 FIX: Tolerancia usa Close[0] para asegurar cierre a favor
                bool pinA  = alcista  ? (Low[0]  <= avwapVal && Close[0] > avwapVal)
                                      : (High[0] >= avwapVal && Close[0] < avwapVal);
                if (pinA)              AddZona(V5_ZonaTipo.Aplus, "A+", true,  dA, Ranking_A);
                else if (dA <= tol)    AddZona(V5_ZonaTipo.A,     "A",  false, dA, Ranking_A);
            }

            // ── Zona LR / LR+ (retroceso a LinReg89) ────────────────────
            if (sma20Alin && sma80Alin && lrAlin)
            {
                double dLR = Math.Abs(pr - lrVal) / atr;
                // V5.1 FIX: Tolerancia usa Close[0] para asegurar cierre a favor
                bool pinLR = alcista  ? (Low[0]  <= lrVal && Close[0] > lrVal)
                                      : (High[0] >= lrVal && Close[0] < lrVal);
                if (pinLR)             AddZona(V5_ZonaTipo.LRplus, "LR+", true,  dLR, Ranking_LR);
                else if (dLR <= tol)   AddZona(V5_ZonaTipo.LR,    "LR",  false, dLR, Ranking_LR);
            }

            // ── Zona 20 / 20+ (retroceso a SMA20 tras cruce) ────────────────
            // Requiere: cruce vigente A FAVOR de la tendencia AVWAP + LR + SMA20
            // V5.2: SMA20 debe estar fuera del canal KeltnerVol por al menos 1.1 * ATR
            if (_huboCruce && sma20Alin && lrAlin)
            {
                bool cruceAFavorAvwap = alcista ? (_cruceSMA20_80_Dir > 0)
                                                : (_cruceSMA20_80_Dir < 0);
                // V5.2 CORRECCIÓN: SMA20 debe estar DENTRO del canal KeltnerVol
                // con al menos 1.1 * ATR de separación a la banda exterior.
                // Long: distancia SMA20 → Banda Superior >= 1.1 * ATR
                // Short: distancia SMA20 → Banda Inferior >= 1.1 * ATR
                bool espacioZ20 = alcista
                    ? (_keltnerVol.Upper - sma20Val) >= (1.1 * atr)
                    : (sma20Val - _keltnerVol.Lower) >= (1.1 * atr);
                if (cruceAFavorAvwap && espacioZ20)
                {
                    double d20 = Math.Abs(pr - sma20Val) / atr;
                    // V5.1 FIX: Tolerancia usa Close[0] para asegurar cierre a favor
                    bool pin20 = alcista ? (Low[0]  <= sma20Val && Close[0] > sma20Val)
                                         : (High[0] >= sma20Val && Close[0] < sma20Val);
                    if (pin20)           AddZona(V5_ZonaTipo.Z20plus, "20+", true,  d20, Ranking_20);
                    else if (d20 <= tol) AddZona(V5_ZonaTipo.Z20,    "20",  false, d20, Ranking_20);
                }
            }

            // ── Zona 80 / 80+ (retroceso a SMA80) ───────────────────────────
            // Requiere: SMA80 a favor. LR puede estar plana. Ya NO requiere SMA20 a favor.
            // V5.2: SMA80 debe estar dentro del canal KeltnerVol en la mitad de la tendencia
            if (sma80Alin && (lrAlin || lrFlat))
            {
                // V5.2: Filtro de posición vs KeltnerVol (SMA80 en la mitad interior del canal)
                bool posicionZ80 = alcista
                    ? (sma80Val >= _keltnerVol.Lower && sma80Val <= _keltnerVol.Mid)
                    : (sma80Val <= _keltnerVol.Upper && sma80Val >= _keltnerVol.Mid);
                // Verificar que precio esté encima de SMA80 (alcista) o debajo (bajista)
                bool ladoCorrecto80 = alcista ? (pr >= sma80Val) : (pr <= sma80Val);
                if (ladoCorrecto80 && posicionZ80)
                {
                    double d80 = Math.Abs(pr - sma80Val) / atr;
                    // V5.1 FIX: Tolerancia usa Close[0] para asegurar cierre a favor
                    bool pin80 = alcista ? (Low[0]  <= sma80Val && Close[0] > sma80Val)
                                         : (High[0] >= sma80Val && Close[0] < sma80Val);
                    if (pin80)           AddZona(V5_ZonaTipo.Z80plus, "80+", true,  d80, Ranking_80);
                    else if (d80 <= tol) AddZona(V5_ZonaTipo.Z80,    "80",  false, d80, Ranking_80);
                }
            }

            // ── Zonas M1 / M2 / M3 (retrocesos a BM Keltner Volatilidad) ─────────
            // V5.2: Usa KeltnerVol BM como referencia de entrada y LR posición para discriminar
            // bmMid ahora usa _keltnerVolBM (actualizado abajo en esta función)
            if (_huboCruce && _retrocesoCount >= 1 && sma20Alin && !double.IsNaN(bmMid))
            {
                bool cruceAFavorAvwap = alcista ? (_cruceSMA20_80_Dir > 0)
                                                : (_cruceSMA20_80_Dir < 0);
                if (cruceAFavorAvwap)
                {
                    double dBm = Math.Abs(pr - bmMid) / atr;
                    // V5.1 FIX: Tolerancia usa Close[0] para asegurar cierre a favor
                    bool pinBM = alcista ? (Low[0]  <= bmMid && Close[0] > bmMid)
                                         : (High[0] >= bmMid && Close[0] < bmMid);

                    // V5.2: Discriminación M1/M2/M3 según posición de la LR
                    // M1: LR alineada + LR por debajo/encima de la BM del canal (LR a favor pero no supera BM)
                    // M2: LR alineada + LR ya superó el precio (precio retrocedió más que la LR)
                    // M3: LR plana + LR superó el precio (consolidación profunda)
                    bool lrBajoBM      = alcista ? (v_lrVal <= _keltnerVolBM) : (v_lrVal >= _keltnerVolBM);
                    bool lrSobrePrecio = alcista ? (v_lrVal >  Close[0])      : (v_lrVal <  Close[0]);

                    V5_ZonaTipo mType  = V5_ZonaTipo.M1;
                    string      mLabel = "M1";
                    int         mRank  = Ranking_M1;

                    if (lrAlin && lrBajoBM)
                    {
                        mType = V5_ZonaTipo.M1; mLabel = "M1"; mRank = Ranking_M1;
                    }
                    else if (lrAlin && lrSobrePrecio)
                    {
                        mType = V5_ZonaTipo.M2; mLabel = "M2"; mRank = Ranking_M2;
                    }
                    else if (lrFlat && lrSobrePrecio)
                    {
                        mType = V5_ZonaTipo.M3; mLabel = "M3"; mRank = Ranking_M3;
                    }
                    else
                    {
                        mRank = 0; // Ningún criterio M aplica, ignorar señal
                    }

                    if (mRank != 0)
                    {
                        if (pinBM)           AddZona((V5_ZonaTipo)((int)mType + 1), mLabel + "+", true,  dBm, mRank);
                        else if (dBm <= tol) AddZona(mType,                          mLabel,       false, dBm, mRank);
                    }

                }

            }



            // ── Seleccionar zona primaria según ranking ───────────────────────

            if (_zonasActivas.Count > 0)

            {

                if (UsarRanking)

                {

                    var primaria   = _zonasActivas.OrderBy(z => z.Ranking).ThenBy(z => (int)z.Tipo).First();

                    _zonaActual    = primaria.Tipo;

                    _zonaLabel     = primaria.Label;

                    _zonaPlus      = primaria.Plus;

                    _zonaDistATR   = primaria.DistATR;

                    _zonaRanking   = primaria.Ranking;

                }

                else

                {

                    var primera    = _zonasActivas.First();

                    _zonaActual    = primera.Tipo;

                    _zonaLabel     = primera.Label;

                    _zonaPlus      = primera.Plus;

                    _zonaDistATR   = primera.DistATR;

                    _zonaRanking   = primera.Ranking;

                }

            }

        }



        private bool _v_vwapSobreBM = false;

        private bool _v_lrSobreBM = false;



        // =====================================================================

        // FIX-006 (2026-05-13): EvaluarEntrada — Stop usa Keltner de VOLATILIDAD

        //

        // Bug original: el stop usaba _keltnerSignalWidth (periodo 20×2.0, muy estrecho)

        // como base principal. Eso generaba stops de 4-8 ticks en muchos casos.

        // Fix: el stop ahora usa _keltnerVolWidth (periodo 52×3.5, el Keltner de

        // volatilidad que define el escenario). Ese ancho es coherente con el escenario

        // que se está operando (Baja/Media/Alta/Extrema).

        //

        // FIX-007 (2026-05-13): StopPct del CSV no actúa como cap si es demasiado

        // restrictivo. Bug: StopPct=0.003 en tick charts dejaba el stop muy pequeño

        // porque 0.3% del precio en muchas barras pequeñas es menos que el ATR.

        // Fix: StopPct actúa como CAP solo si el valor resultante es >= 50% del stopBase.

        //

        // FIX-002 (2026-05-13): Cooldown post-señal integrado aquí.

        // =====================================================================

        private void EvaluarEntrada()

        {

            // ── Gestión del cooldown ─────────────────────────────────────────

            // FIX-002: Incrementar contador y desactivar cooldown cuando expira

            if (_enCooldown)

            {

                _barrasDesdeUltimaSenal++;

                if (_barrasDesdeUltimaSenal >= CooldownBars)

                {

                    _enCooldown             = false;

                    _barrasDesdeUltimaSenal = 0;

                }

            }



            bool esVerde   = _colorSemaforo == V5_TendenciaColor.VerdeClaro

                          || _colorSemaforo == V5_TendenciaColor.VerdeOscuro;

            bool zonaActiva= _zonasActivas.Count > 0;

            _esAlcista     = v_vwapUp;



            bool capa3Ok   = !UsarCapa3 || _v_vwapSobreBM;
            bool capa4Ok   = !UsarCapa4 || _v_lrSobreBM;

            // FIX-BUG1: Los filtros HTF/Nodos/POCs se calculaban pero NUNCA se aplicaban en la condición de entrada.
            // Resultado: las señales se generaban aunque el HTF, Nodos o POCs estuvieran en contra.
            bool htfOk    = !UsarFiltroHTF || _pasaHTF;
            bool nodosOk  = !UsarNodos     || _pasaNodos;
            bool pocsOk   = !UsarPocs      || _pasaPocs;

            // V5.1: Filtro de dirección operativa
            bool dirOk = true;
            if (DireccionOperacion == V5_DireccionModo.SoloLong && !_esAlcista) dirOk = false;
            if (DireccionOperacion == V5_DireccionModo.SoloShort && _esAlcista) dirOk = false;

            // FIX-002: La señal no se genera si estamos en cooldown
            if (esVerde && zonaActiva && capa3Ok && capa4Ok && htfOk && nodosOk && pocsOk && dirOk && !_enCooldown)

            {

                // ── Precio de entrada según zona ─────────────────────────────

                double entryPrice = Close[0];

                switch (_zonaActual)

                {

                    case V5_ZonaTipo.A:   case V5_ZonaTipo.Aplus:   entryPrice = v_vwapVal;        break;

                    case V5_ZonaTipo.LR:  case V5_ZonaTipo.LRplus:  entryPrice = v_lrVal;          break;

                    case V5_ZonaTipo.Z20: case V5_ZonaTipo.Z20plus: entryPrice = v_sma20Val;       break;

                    case V5_ZonaTipo.Z80: case V5_ZonaTipo.Z80plus: entryPrice = v_sma80Val;       break;

                    case V5_ZonaTipo.M1:  case V5_ZonaTipo.M1plus:

                    case V5_ZonaTipo.M2:  case V5_ZonaTipo.M2plus:

                    case V5_ZonaTipo.M3:  case V5_ZonaTipo.M3plus:  entryPrice = _keltnerVolBM; break; // V5.2: usa KeltnerVol BM

                }



                // ── Cálculo del Stop ─────────────────────────────────────────

                // FIX-006: Usar _keltnerVolWidth (Keltner de Volatilidad 52×3.5)

                //          en lugar del Signal Keltner (20×2.0) que era demasiado estrecho.

                double stopKeltner = _keltnerVolWidth * Kelt_StopPct;           // 30% del ancho VOL
                double stopAtrMult = _escenarioConfigActual.StopATRx > 0 ? _escenarioConfigActual.StopATRx : StopATRx;
                double stopATR     = stopAtrMult * _atrVal;                        // X veces el ATR
                double stopBase    = Math.Max(stopKeltner, stopATR);             double stopBaseUncapped = stopBase;
// tomar el mayor



                // FIX-007: Aplicar cap por porcentaje SOLO si no trunca demasiado el stop.

                // Se aplica solo si el cap resulta en >= 50% del stopBase calculado.

                if (_escenarioConfigActual.StopPct > 0 && entryPrice > 0)

                {

                    double stopPctMax = _escenarioConfigActual.StopPct * entryPrice;

                    if (stopPctMax >= stopBase * 0.5)   // FIX: solo si no es demasiado restrictivo

                        stopBase = Math.Min(stopBase, stopPctMax);

                }



                // Cap máximo en ticks (si está configurado)

                int stopMaxT = _escenarioConfigActual.StopMaxTicks > 0 ? _escenarioConfigActual.StopMaxTicks : StopMaxTicks;
                if (stopMaxT > 0)
                    stopBase = Math.Min(stopBase, stopMaxT * _tickSize);



                // Mínimo absoluto en ticks

                double stopMinAbs = Math.Max(1, StopMinTicks) * _tickSize;

                _eStopPts = Math.Max(stopBase, stopMinAbs);



                // ── Targets y R:R ────────────────────────────────────────────

                _eEntrada = entryPrice;

                _eStop    = _esAlcista ? _eEntrada - _eStopPts : _eEntrada + _eStopPts;

                // Usar ratios del escenario si están definidos, si no usar parámetros UI

                double rr1Used = _escenarioConfigActual.RR1 > 0 ? _escenarioConfigActual.RR1 : RR1_Ratio;

                double rr2Used = _escenarioConfigActual.RR2 > 0 ? _escenarioConfigActual.RR2 : RR2_Ratio;

                // El Target usa el stopBaseUncapped (el mayor) para no reducir el beneficio si el stop fue cortado
                double tBasePts = Math.Max(stopBaseUncapped, stopMinAbs);
                _eT1Pts   = tBasePts * rr1Used;
                _eT2Pts   = tBasePts * rr2Used;

                _eT1      = _esAlcista ? _eEntrada + _eT1Pts : _eEntrada - _eT1Pts;

                _eT2      = _esAlcista ? _eEntrada + _eT2Pts : _eEntrada - _eT2Pts;

                _eRR1     = _eStopPts > 0 ? _eT1Pts / _eStopPts : 0;

                _eRR2     = _eStopPts > 0 ? _eT2Pts / _eStopPts : 0;



                _entradaValida = _eRR1 >= RR_MinFiltro;

                if (!_entradaValida)

                {

                    _eEntrada = _eStop = _eT1 = _eT2 = double.NaN;

                    _eStopPts = _eT1Pts = _eT2Pts = _eRR1 = _eRR2 = 0;

                }

            }

            else

            {

                _entradaValida = false;

                _eEntrada      = _eStop = _eT1 = _eT2 = double.NaN;

                _eStopPts      = _eT1Pts = _eT2Pts = _eRR1 = _eRR2 = 0;

            }



            // ── Guardar última señal persistente ─────────────────────────────

            // FIX-002: Activar cooldown cuando se registra una nueva señal (transición false→true)

            if (_entradaValida && !_prevEntradaValida)
            {
                Print(string.Format("[HUD V5] Señal Emitida: {0} {1} | Zona={2} | Entrada={3:F2} Stop={4:F2} T1={5:F2} T2={6:F2}",
                    _esAlcista ? "LONG" : "SHORT", Instrument.FullName, _zonaLabel, _eEntrada, _eStop, _eT1, _eT2));
                
                _ultimaEntradaValida  = true;

                _ultimaEsAlcista      = _esAlcista;

                _ultimaFechaHora      = Time[0].ToString("dd/MM HH:mm");

                _ultimaZonaLabel      = _zonaLabel;

                _ultimaEntrada        = _eEntrada;

                _ultimaStop           = _eStop;   _ultimaStopPts = _eStopPts;

                _ultimaT1             = _eT1;     _ultimaT1Pts   = _eT1Pts;  _ultimaRR1 = _eRR1;

                _ultimaT2             = _eT2;     _ultimaT2Pts   = _eT2Pts;  _ultimaRR2 = _eRR2;



                // FIX-002: Iniciar cooldown para evitar señales inmediatamente consecutivas

                _enCooldown             = true;

                _barrasDesdeUltimaSenal = 0;

            }



            _ultimaEntradaActiva = _entradaValida;

        }



        private void Logging()

        {

            if (!RegistrarSenales || string.IsNullOrEmpty(_logFilePath)) return;

            bool puedeLoggear = State == State.Realtime || LoggearHistorico;

            if (!puedeLoggear) return;



            // Fix #9: Al detectar nueva señal, escribir PENDIENTE (no ABRE).
            //         El ABRE se escribe en CheckActiveEntries() cuando el precio
            //         alcanza el nivel de la Limit calculado con BT_LimitSlippageATRs.
            if (_entradaValida && !_prevEntradaValida)

            {

                try

                {

                    var zonasALoggear = (UsarRanking || _zonasActivas.Count <= 1)

                        ? new List<V5_ZonaInfo> { new V5_ZonaInfo { Label = _zonaLabel, Ranking = _zonaRanking } }

                        : _zonasActivas;



                    foreach (var z in zonasALoggear)

                    {

                        string id     = Guid.NewGuid().ToString("N").Substring(0, 8);

                        string dir    = _esAlcista ? "LONG" : "SHORT";

                        string tf     = BarsPeriod.Value + BarsPeriod.BarsPeriodType.ToString();

                        string fecha  = Time[0].ToString("yyyy-MM-dd HH:mm:ss");

                        string mercado = Instrument != null ? Instrument.FullName : _instrKey;

                        double stopTks = _eStopPts / _tickSize;

                        double stopUsd = _eStopPts * _pointValue;

                        string capas  = (UsarCapa3 ? "C3" : "") + (UsarCapa4 ? "C4" : "");

                        // Fix #9: calcular precio Limit para el modelo BT
                        //   Long:  Limit = entrada - BT_Slippage*ATR  (soporte = precio baja hasta zona)
                        //   Short: Limit = entrada + BT_Slippage*ATR  (resistencia = precio sube hasta zona)
                        double atrMargenBT = (BT_LimitSlippageATRs > 0 && _atrVal > 0)
                            ? BT_LimitSlippageATRs * _atrVal : 0;
                        double limitPrecioBT = _esAlcista
                            ? _eEntrada - atrMargenBT
                            : _eEntrada + atrMargenBT;
                        limitPrecioBT = Math.Round(limitPrecioBT / _tickSize) * _tickSize;

                        // Escribir fila PENDIENTE (la señal acaba de salir, aún no ejecutada)
                        string lineaPend = string.Format(CultureInfo.InvariantCulture,

                            "PENDIENTE,{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13:F4},{14:F4},{15:F4},{16:F4},{17:F2},{18:F2},{19:F0},{20:F2},{21:F4},{22:F4},{23:F4},{24:F4},{25:F4},{26:F4},{27},,PENDIENTE,NO,NO,{28:F4},0",

                            id, fecha, mercado, AccountName, tf, dir, _escenarioActual, z.Label, z.Ranking, capas,

                            "SI", "SI", "SI",

                            limitPrecioBT, _eStop, _eT1, _eT2, _eRR1, _eRR2, stopTks, stopUsd,

                            _atrVal, _keltnerVolWidth, v_vwapVal, v_lrVal, v_sma20Val, v_sma80Val, _tendenciaCount,

                            limitPrecioBT);

                        lock (_logLock) { File.AppendAllText(_logFilePath, lineaPend + Environment.NewLine); }



                        // Registrar entrada pendiente para seguimiento en CheckActiveEntries()
                        var reg = new V5_EntryRegistro

                        {

                            SignalID = id, FechaHora = fecha, Mercado = mercado, Cuenta = AccountName,

                            Timeframe = tf, Direccion = dir, Escenario = _escenarioActual,

                            Zona = z.Label, ZonaRanking = z.Ranking, CapasActivas = capas,

                            HTF_OK = true, Nodos_OK = true, Pocs_OK = true,

                            // Entrada = LimitPrecio (será confirmado cuando se llene)
                            Entrada = limitPrecioBT, Stop = _eStop, T1 = _eT1, T2 = _eT2,

                            RR1 = _eRR1, RR2 = _eRR2, StopTicks = stopTks, StopUSD = stopUsd,

                            ATR = _atrVal, KeltnerWidth = _keltnerVolWidth,

                            AVWAP = v_vwapVal, LinReg = v_lrVal, SMA20 = v_sma20Val, SMA80 = v_sma80Val,

                            TendenciaCount = _tendenciaCount, Barras = 0,

                            // Fix #9: campos BT
                            LimitPrecio = limitPrecioBT,
                            eStopPts    = _eStopPts,
                            eT1Pts      = _eT1Pts,
                            eT2Pts      = _eT2Pts,
                            Ejecutada   = false,
                            BarrasPendientes = 0,
                            StopBE      = double.NaN,
                            T1HaBE      = false

                        };

                        _activeEntries.Add(reg);

                    }

                }

                catch { }

            }

        }



        private V5_HudSnap BuildSnap()

        {

            bool esV = _colorSemaforo == V5_TendenciaColor.VerdeClaro || _colorSemaforo == V5_TendenciaColor.VerdeOscuro;

            return new V5_HudSnap

            {

                Instrumento = Instrument != null ? Instrument.MasterInstrument.Name : "---",

                Version = "1.0",  // V5.1 FIX (2026-05-27): bidireccionalidad full, fix tolerancias Close[0], jerarquía M2>M3

                EscenarioActivo = _escenarioActual,

                TendenciaCount = _tendenciaCount,

                ColorSemaforo = _colorSemaforo,

                LinRegUp = v_lrUp, AVWAPUp = v_vwapUp, SMA20Up = v_sma20Up, SMA80Up = v_sma80Up,

                LinRegVal = v_lrVal, AVWAPVal = v_vwapVal, SMA20Val = v_sma20Val, SMA80Val = v_sma80Val,

                CruceSMA_Dir = _cruceSMA20_80_Dir > 0 ? "Cruce Alcista" : _cruceSMA20_80_Dir < 0 ? "Cruce Bajista" : "Sin cruce",

                CruceSMA_Barras = _cruceSMA20_80_Barras,

                HTF_Activo = UsarFiltroHTF, HTF_Pasa = _pasaHTF,

                Capa3_Activo = UsarCapa3, Capa3_Pasa = _v_vwapSobreBM,

                Capa4_Activo = UsarCapa4, Capa4_Pasa = _v_lrSobreBM,

                ZonaActiva = _zonaActual,

                ZonaLabel = _zonaLabel,

                ZonaRanking = _zonaRanking,

                ZonaPlus = _zonaPlus,

                ZonaDistATR = _zonaDistATR,

                Entrada = _eEntrada, Stop = _eStop, T1 = _eT1, T2 = _eT2,

                RR1 = _eRR1, RR2 = _eRR2,

                EntradaValida = _entradaValida, EntradaAlcista = _esAlcista,

                UltimaEntradaValida = _ultimaEntradaValida,

                UltimaEntradaActiva = _ultimaEntradaActiva,

                UltimaFechaHora = _ultimaFechaHora,

                UltimaZonaLabel = _ultimaZonaLabel,

                UltimaEntrada = _ultimaEntrada, UltimaStop = _ultimaStop, UltimaStopPts = _ultimaStopPts,

                UltimaT1 = _ultimaT1, UltimaT1Pts = _ultimaT1Pts, UltimaRR1 = _ultimaRR1,

                UltimaT2 = _ultimaT2, UltimaT2Pts = _ultimaT2Pts, UltimaRR2 = _ultimaRR2,

                KeltnerVolWidth = _keltnerVolWidth, KeltnerVolBM = _keltnerVolBM,

                KeltnerSignalWidth = _keltnerSignalWidth, KeltnerSignalBM = _keltnerSignalBM,

                ATR = _atrVal, VolumenNegociado = Volume[0], SessionVolume = _sessionVolume,

                POC = _pocVal, AntiPOC_Up = _antiPocUpVal, AntiPOC_Down = _antiPocDownVal, LVN = _lvnVal,

                NakedPOC_Activo = _nakedPocActivo,

                PerfilModoLabel = _perfilModoLabel,

                MostrarSemaforo = Mostrar_Semaforo,

                MostrarSenales = Mostrar_Senales,

                MostrarEntrada = Mostrar_Entrada,

                MostrarKeltner = Mostrar_Keltner,

                MostrarPosicion = Mostrar_Posicion,

                InstrumentoNoReconocido = _mercadoNoReconocido,

                ConfigNoEncontrada = _configNoEncontrada,

                ZonasActivasCount = _zonasActivas.Count,

                ZonasActivasResumen = _zonasActivas.Count > 0 ? string.Join(", ", _zonasActivas.Select(z => z.Label + "(#" + z.Ranking + ")")) : "--",

            };

        }



        #region SharpDX Chart Overlay (Fase 9)



        private SharpDX.Direct2D1.SolidColorBrush dxFondo, dxBorde, dxTitulo, dxGris;

        private SharpDX.Direct2D1.SolidColorBrush dxVerde, dxAmarillo, dxRojo, dxApagado;

        private SharpDX.Direct2D1.SolidColorBrush dxCian, dxNaranja, dxViolet, dxAzul;

        private SharpDX.Direct2D1.SolidColorBrush dxVerdeClaro, dxVerdeMedio, dxVerdeOscuro;

        private SharpDX.DirectWrite.TextFormat fmtLabel, fmtPrice, fmtSmall;
        private SharpDX.Direct2D1.StrokeStyle customStrokeStyle;
        private SharpDX.Direct2D1.SolidColorBrush dxSenalesFondo;
        private bool dxReady = false;



        public override void OnRenderTargetChanged()

        {

            DisposeDXResources();

            if (RenderTarget != null)

            { try { CreateDXResources(RenderTarget); dxReady = true; } catch { dxReady = false; } }

        }



        private void CreateDXResources(SharpDX.Direct2D1.RenderTarget rt)

        {

            dxFondo = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(12, 14, 20, 200));

            dxBorde = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(70, 80, 110, 200));

            dxTitulo = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(200, 200, 220, 255));

            dxGris = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(150, 155, 165, 255));

            dxVerde = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(0, 200, 80, 255));

            dxAmarillo = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(255, 210, 0, 255));

            dxRojo = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(220, 50, 50, 255));

            dxApagado = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(45, 45, 50, 160));

            dxCian = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(80, 200, 220, 255));

            dxNaranja = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(255, 140, 0, 255));

            dxViolet = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(180, 130, 255, 200));

            dxAzul = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(60, 120, 220, 255));

            dxVerdeClaro = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(100, 255, 120, 255));

            dxVerdeMedio = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(0, 220, 100, 255));
            dxVerdeOscuro = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(0, 180, 60, 255));

            var wpfColor = ((System.Windows.Media.SolidColorBrush)Senales_FondoColor).Color;
            dxSenalesFondo = new SharpDX.Direct2D1.SolidColorBrush(rt, new SharpDX.Color(wpfColor.R, wpfColor.G, wpfColor.B, wpfColor.A));

            var ds = SharpDX.Direct2D1.DashStyle.Solid;
            if (Senales_LineStyle == DashStyleHelper.Dash) ds = SharpDX.Direct2D1.DashStyle.Dash;
            else if (Senales_LineStyle == DashStyleHelper.Dot) ds = SharpDX.Direct2D1.DashStyle.Dot;
            else if (Senales_LineStyle == DashStyleHelper.DashDot) ds = SharpDX.Direct2D1.DashStyle.DashDot;
            
            var strokeProps = new SharpDX.Direct2D1.StrokeStyleProperties() { DashStyle = ds };
            customStrokeStyle = new SharpDX.Direct2D1.StrokeStyle(rt.Factory, strokeProps);

            var dwf = new SharpDX.DirectWrite.Factory();

            fmtLabel = new SharpDX.DirectWrite.TextFormat(dwf, "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, 10f);

            fmtPrice = new SharpDX.DirectWrite.TextFormat(dwf, "Consolas", SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, 10f);

            fmtSmall = new SharpDX.DirectWrite.TextFormat(dwf, "Arial", SharpDX.DirectWrite.FontWeight.Normal, SharpDX.DirectWrite.FontStyle.Normal, 9f);

            dwf.Dispose();

            foreach (var f in new[] { fmtLabel, fmtPrice, fmtSmall })

            { f.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading; f.ParagraphAlignment = SharpDX.DirectWrite.ParagraphAlignment.Center; f.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap; }

        }



        protected override void OnRender(ChartControl cc, ChartScale cs)

        {

            if (!Mostrar_En_Chart) return;

            if (!dxReady || RenderTarget == null)

            { if (RenderTarget != null) { try { DisposeDXResources(); CreateDXResources(RenderTarget); dxReady = true; } catch { return; } } else return; }

            try { DrawOverlay(cc, cs); } catch { dxReady = false; }

        }



        private void DrawOverlay(ChartControl cc, ChartScale cs)

        {

            float px = 10f, py = 10f;



            SharpDX.Direct2D1.Brush ZonaColor(V5_ZonaTipo z)

            {

                switch (z)

                {

                    case V5_ZonaTipo.A: case V5_ZonaTipo.Aplus: return dxCian;

                    case V5_ZonaTipo.LR: case V5_ZonaTipo.LRplus: return dxAzul;

                    case V5_ZonaTipo.Z20: case V5_ZonaTipo.Z20plus: return dxAmarillo;

                    case V5_ZonaTipo.Z80: case V5_ZonaTipo.Z80plus: return dxNaranja;

                    case V5_ZonaTipo.M1: case V5_ZonaTipo.M1plus: return dxVerdeClaro;

                    case V5_ZonaTipo.M2: case V5_ZonaTipo.M2plus: return dxVerdeMedio;

                    case V5_ZonaTipo.M3: case V5_ZonaTipo.M3plus: return dxVerdeOscuro;

                    default: return dxGris;

                }

            }



            double refPrice = _zonaActual != V5_ZonaTipo.Ninguna ? Close[0] : 0;

            double[] drawPrices = { _eStop, _eT1, _eT2 };

            SharpDX.Direct2D1.Brush[] drawBrushes = { dxRojo, dxVerde, dxNaranja };

            string[] drawLabels = { "Stop", "T1", "T2" };



            for (int i = 0; i < 3; i++)
            {
                if (double.IsNaN(drawPrices[i]) || drawPrices[i] <= 0) continue;
                float y = (float)cs.GetYByValue(drawPrices[i]);
                float xL = (float)cc.CanvasLeft + (float)Senales_OffsetIzq;
                float xR = (float)(cc.CanvasRight) - 2f;

                RenderTarget.DrawLine(new SharpDX.Vector2(xL, y), new SharpDX.Vector2(xR, y), drawBrushes[i], 1.5f, customStrokeStyle);

                string lbl = drawLabels[i] + " " + drawPrices[i].ToString("0.00");
                var bg = new SharpDX.RectangleF(xL, y - 8f, xL + 70f, y + 8f);
                RenderTarget.FillRectangle(bg, dxSenalesFondo);
                RenderTarget.DrawText(lbl, fmtLabel, new SharpDX.RectangleF(xL + 2f, y - 8f, xL + 68f, y + 8f), drawBrushes[i]);
            }



            if (_zonaActual != V5_ZonaTipo.Ninguna && !double.IsNaN(Close[0]))

            {

                float y = (float)cs.GetYByValue(Close[0]);

                float x = (float)cc.GetXByBarIndex(ChartBars, ChartBars.Count - 1);

                var col = ZonaColor(_zonaActual);

                string tag = _zonaLabel + " #" + _zonaRanking;

                RenderTarget.DrawText(tag, fmtLabel, new SharpDX.RectangleF(x + 5f, y - 20f, x + 100f, y), col);

                float arrowSize = 6f;

                float arrowDir = _esAlcista ? -1f : 1f;

                var p1 = new SharpDX.Vector2(x, y);

                var p2 = new SharpDX.Vector2(x - arrowSize, y + arrowDir * arrowSize);

                var p3 = new SharpDX.Vector2(x + arrowSize, y + arrowDir * arrowSize);

                RenderTarget.DrawLine(p1, p2, col, 2f);

                RenderTarget.DrawLine(p1, p3, col, 2f);

            }



            if (!double.IsNaN(_pocVal) && _pocVal > 0)

            {

                float yPoc = (float)cs.GetYByValue(_pocVal);

                float xL = (float)cc.CanvasLeft + 2f;

                RenderTarget.DrawLine(new SharpDX.Vector2(xL, yPoc), new SharpDX.Vector2(xL + 30f, yPoc), dxApagado, 1f);

                RenderTarget.DrawText("POC", fmtSmall, new SharpDX.RectangleF(xL + 32f, yPoc - 7f, xL + 70f, yPoc + 7f), dxApagado);

            }



            if (_nakedPocActivo && !double.IsNaN(_volProfile.PrevSessionPOC))

            {

                float yNp = (float)cs.GetYByValue(_volProfile.PrevSessionPOC);

                float xL = (float)cc.CanvasLeft + 2f;

                RenderTarget.DrawLine(new SharpDX.Vector2(xL, yNp), new SharpDX.Vector2(xL + 30f, yNp), dxNaranja, 1f);

                RenderTarget.DrawText("NPOC", fmtSmall, new SharpDX.RectangleF(xL + 32f, yNp - 7f, xL + 80f, yNp + 7f), dxNaranja);

            }

        }



        private void DisposeDXResources()

        {
            dxReady = false;
            var brushes = new[] { dxFondo, dxBorde, dxTitulo, dxGris, dxVerde, dxAmarillo, dxRojo, dxApagado, dxCian, dxNaranja, dxViolet, dxAzul, dxVerdeClaro, dxVerdeMedio, dxVerdeOscuro, dxSenalesFondo };
            foreach (var b in brushes) b?.Dispose();
            dxFondo = dxBorde = dxTitulo = dxGris = dxVerde = dxAmarillo = dxRojo = dxApagado = dxCian = dxNaranja = dxViolet = dxAzul = dxVerdeClaro = dxVerdeMedio = dxVerdeOscuro = dxSenalesFondo = null;

            if (customStrokeStyle != null) { customStrokeStyle.Dispose(); customStrokeStyle = null; }

            var formats = new[] { fmtLabel, fmtPrice, fmtSmall };

            foreach (var f in formats) f?.Dispose();

            fmtLabel = fmtPrice = fmtSmall = null;

        }



        #endregion



        private void AbrirVentana(string instr)

        {

            var disp = GetUI();

            if (disp == null) return;

            disp.Invoke((Action)(() =>

            {

                try

                {

                    _ownPanel = new V5_MasterHudPanel(Hud_Ancho);

                    _ownWindow = new System.Windows.Window

                    {

                        Title = "EduS HUD V5 . " + instr,

                        Content = _ownPanel,

                        Background = V5_MasterHudPanel.C_BG,

                        SizeToContent = SizeToContent.WidthAndHeight,

                        ResizeMode = ResizeMode.CanMinimize,

                        Topmost = true,

                        ShowInTaskbar = true,

                        WindowStyle = WindowStyle.ToolWindow,

                        Left = Hud_X,

                        Top = Hud_Y,

                    };

                    _ownWindow.Closed += (s, e) => _ownWindowClosed = true;

                    _ownWindow.Show();

                    IniciarPosTimer();

                }

                catch { }

            }));

        }



        private void IniciarPosTimer()

        {

            if (!Mostrar_Posicion) return;

            if (_posTimer != null) { _posTimer.Stop(); _posTimer = null; }

            ResolverCuenta();

            _posTimer = new DispatcherTimer(TimeSpan.FromSeconds(Math.Max(1, Pos_UpdateSeg)), DispatcherPriority.Normal, PosTimer_Tick, GetUI());

            _posTimer.Start();

            PosTimer_Tick(null, null);

        }



        private void ResolverCuenta()

        {

            _account = null;

            if (!string.IsNullOrEmpty(AccountName))

                _account = Account.All.FirstOrDefault(a => a.Name == AccountName);

        }



        private void PosTimer_Tick(object sender, EventArgs e)

        {

            if (_ownPanel == null || _ownWindowClosed) return;

            try

            {

                if (_account == null || (!string.IsNullOrEmpty(AccountName) && _account.Name != AccountName))

                    ResolverCuenta();

                _ownPanel.UpdatePosition(BuildPosSnap());

            }

            catch { }

        }



        private V5_PosSnap BuildPosSnap()

        {

            var snap = new V5_PosSnap();

            if (_account == null)

            {

                snap.Etiqueta = string.IsNullOrEmpty(AccountName) ? "Config AccountName" : "Cuenta no encontrada";

                return snap;

            }

            string instrName = Instrument != null ? Instrument.MasterInstrument.Name : "";

            Position pos = null;

            try { pos = _account.Positions.FirstOrDefault(p => p.Instrument.MasterInstrument.Name == instrName && p.MarketPosition != MarketPosition.Flat); }

            catch { return snap; }

            if (pos == null) return snap;

            snap.TienePos = true;

            snap.EsLong = pos.MarketPosition == MarketPosition.Long;

            snap.Cantidad = Math.Abs(pos.Quantity);

            snap.PrecioEntrada = pos.AveragePrice;

            try

            {

                snap.PLdolar = pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency);

                double pv = pos.Instrument.MasterInstrument.PointValue;

                snap.PLpuntos = pv > 0 ? snap.PLdolar / Math.Abs(pos.Quantity) / pv : 0;

            }

            catch { }

            try

            {

                var orders = _account.Orders.Where(o => o.Instrument == pos.Instrument && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted) && (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit || o.OrderType == OrderType.Limit)).ToList();

                double pv = pos.Instrument.MasterInstrument.PointValue;

                double totalStopP = 0, totalStopQ = 0, totalTgtP = 0, totalTgtQ = 0;

                bool hayATM = false;

                foreach (var o in orders)

                {

                    if (!string.IsNullOrEmpty(o.Name) && o.Name.StartsWith("ATM", StringComparison.OrdinalIgnoreCase)) hayATM = true;

                    if (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit)

                    { double sp = o.StopPrice > 0 ? o.StopPrice : o.LimitPrice; totalStopP += sp * o.Quantity; totalStopQ += o.Quantity; }

                    else if (o.OrderType == OrderType.Limit)

                    { totalTgtP += o.LimitPrice * o.Quantity; totalTgtQ += o.Quantity; }

                }

                if (totalStopQ > 0)

                {

                    snap.TieneStop = true;

                    double avgStop = totalStopP / totalStopQ;

                    if (snap.EsLong) snap.StopRisk = Math.Abs(pos.AveragePrice - avgStop) * totalStopQ * pv;

                    else snap.StopRisk = Math.Abs(avgStop - pos.AveragePrice) * totalStopQ * pv;

                    snap.GananciaAsegurada = snap.EsLong ? (avgStop > pos.AveragePrice) : (avgStop < pos.AveragePrice);

                    snap.RiesgoIlimitado = totalStopQ < Math.Abs(pos.Quantity);

                }

                if (totalTgtQ > 0)

                {

                    snap.TieneTarget = true;

                    double avgTgt = totalTgtP / totalTgtQ;

                    if (snap.EsLong) snap.TargetProfit = Math.Abs(avgTgt - pos.AveragePrice) * totalTgtQ * pv;

                    else snap.TargetProfit = Math.Abs(pos.AveragePrice - avgTgt) * totalTgtQ * pv;

                }

                if (snap.StopRisk > 0.01 && snap.TargetProfit > 0.01) snap.RR = snap.TargetProfit / snap.StopRisk;

                snap.Etiqueta = hayATM ? "ATM" : (orders.Count > 0 ? "Manual" : "");

            }

            catch { }

            return snap;

        }



        private bool _ultimaEsAlcista;



        #region Active entry tracking for close logging



        private class V5_EntryRegistro

        {

            public string Tipo, SignalID, FechaHora, Mercado, Cuenta, Timeframe, Direccion, Escenario, Zona, CapasActivas;

            public int ZonaRanking;

            public double Entrada, Stop, T1, T2, RR1, RR2, StopTicks, StopUSD, ATR, KeltnerWidth, AVWAP, LinReg, SMA20, SMA80;

            public int TendenciaCount, Barras;

            public bool HTF_OK, Nodos_OK, Pocs_OK;

            // Fix #9: campos para modelo de backtesting realista
            public double LimitPrecio;      // precio real de la orden Limit (con margen ATR)
            public double eStopPts;         // distancia en puntos al stop (para recalcular tras fill)
            public double eT1Pts;           // distancia en puntos a T1
            public double eT2Pts;           // distancia en puntos a T2
            public bool   Ejecutada;        // true = precio alcanzó la Limit → ABRE escrito
            public int    BarrasPendientes; // barras transcurridas desde la señal hasta fill o cancel
            public double StopBE;           // nivel de stop BE (tras T1)
            public bool   T1HaBE;           // true = T1 tocado, stop ya movido a BE

        }



        private List<V5_EntryRegistro> _activeEntries = new List<V5_EntryRegistro>();



        private void WriteCloseEntry(V5_EntryRegistro e, string resultado, bool t1Tocado, bool t2Tocado, double precioSalida)

        {

            if (string.IsNullOrEmpty(_logFilePath)) return;

            try

            {

                string linea = string.Format(CultureInfo.InvariantCulture,

                    "CIERRE,{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13:F4},{14:F4},{15:F4},{16:F4},{17:F2},{18:F2},{19:F0},{20:F2},{21:F4},{22:F4},{23:F4},{24:F4},{25:F4},{26:F4},{27},{28},{29},{30},{31},{32:F4},{33}",

                    e.SignalID, e.FechaHora, e.Mercado, e.Cuenta, e.Timeframe, e.Direccion,

                    e.Escenario, e.Zona, e.ZonaRanking, e.CapasActivas,

                    e.HTF_OK ? "SI" : "NO", e.Nodos_OK ? "SI" : "NO", e.Pocs_OK ? "SI" : "NO",

                    e.Entrada, e.Stop, e.T1, e.T2, e.RR1, e.RR2, e.StopTicks, e.StopUSD,

                    e.ATR, e.KeltnerWidth, e.AVWAP, e.LinReg, e.SMA20, e.SMA80, e.TendenciaCount,

                    Time[0].ToString("yyyy-MM-dd HH:mm:ss"), resultado,

                    t1Tocado ? "SI" : "NO", t2Tocado ? "SI" : "NO",

                    precioSalida, e.Barras);

                lock (_logLock) { File.AppendAllText(_logFilePath, linea + Environment.NewLine); }

            }

            catch { }

        }



        // Fix #9: Escribe la fila ABRE cuando el precio alcanza el nivel Limit.
        private void WriteAbreEntry(V5_EntryRegistro e)

        {

            if (string.IsNullOrEmpty(_logFilePath)) return;

            try

            {

                string linea = string.Format(CultureInfo.InvariantCulture,

                    "ABRE,{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13:F4},{14:F4},{15:F4},{16:F4},{17:F2},{18:F2},{19:F0},{20:F2},{21:F4},{22:F4},{23:F4},{24:F4},{25:F4},{26:F4},{27},,,,,,",

                    e.SignalID, e.FechaHora, e.Mercado, e.Cuenta, e.Timeframe, e.Direccion,

                    e.Escenario, e.Zona, e.ZonaRanking, e.CapasActivas,

                    e.HTF_OK ? "SI" : "NO", e.Nodos_OK ? "SI" : "NO", e.Pocs_OK ? "SI" : "NO",

                    e.Entrada, e.Stop, e.T1, e.T2, e.RR1, e.RR2, e.StopTicks, e.StopUSD,

                    e.ATR, e.KeltnerWidth, e.AVWAP, e.LinReg, e.SMA20, e.SMA80, e.TendenciaCount);

                lock (_logLock) { File.AppendAllText(_logFilePath, linea + Environment.NewLine); }

            }

            catch { }

        }



        private void CheckActiveEntries()

        {

            if (!RegistrarSenales || string.IsNullOrEmpty(_logFilePath)) return;

            bool puedeLoggear = State == State.Realtime || LoggearHistorico;

            if (!puedeLoggear) return;



            for (int i = _activeEntries.Count - 1; i >= 0; i--)

            {

                var e = _activeEntries[i];

                bool alcista = e.Direccion == "LONG";



                // ── FASE 1: Entrada PENDIENTE — verificar si el precio llegó a la Limit ──
                if (!e.Ejecutada)

                {

                    e.BarrasPendientes++;

                    // ¿Llegó el precio al nivel Limit?
                    bool fillAlcista = alcista  && Low[0]  <= e.LimitPrecio;
                    bool fillBajista = !alcista && High[0] >= e.LimitPrecio;

                    if (fillAlcista || fillBajista)

                    {

                        // Precio llenado: recalcular Stop/T1/T2 relativos al precio real de la Limit
                        e.Ejecutada  = true;
                        e.FechaHora  = Time[0].ToString("yyyy-MM-dd HH:mm:ss");  // fecha real de fill
                        e.Barras     = 0;

                        // Recalcular niveles desde el precio de fill (LimitPrecio)
                        e.Stop = alcista ? e.LimitPrecio - e.eStopPts : e.LimitPrecio + e.eStopPts;
                        e.T1   = alcista ? e.LimitPrecio + e.eT1Pts   : e.LimitPrecio - e.eT1Pts;
                        e.T2   = alcista ? e.LimitPrecio + e.eT2Pts   : e.LimitPrecio - e.eT2Pts;

                        // Recalcular ticks y USD del stop al nuevo precio de entrada
                        e.StopTicks = e.eStopPts / _tickSize;
                        e.StopUSD   = e.eStopPts * _pointValue;

                        WriteAbreEntry(e);  // registrar el ABRE con los niveles recalculados

                    }

                    else if (e.BarrasPendientes >= BT_LimitTimeoutBarras)

                    {

                        // Expiró sin fill: registrar CANCELADA y eliminar
                        WriteCloseEntry(e, "CANCELADA", false, false, 0);

                        _activeEntries.RemoveAt(i);

                    }

                    // Si aún está pendiente y no expiró, no hacer nada más esta barra
                    continue;

                }



                // ── FASE 2: Entrada EJECUTADA — tracking de Stop/T1/T2 con modelo BE ──
                e.Barras++;

                // Stop activo: si T1 ya fue tocado, usar StopBE; si no, Stop original
                double stopActivo = e.T1HaBE ? e.StopBE : e.Stop;

                bool hitStop = alcista ? Low[0]  <= stopActivo : High[0] >= stopActivo;
                bool hitT1   = !e.T1HaBE && (alcista ? High[0] >= e.T1 : Low[0] <= e.T1);
                bool hitT2   = alcista ? High[0] >= e.T2 : Low[0] <= e.T2;

                // Detectar hit de T1: activar BE y continuar tracking (para T2 o StopBE)
                if (hitT1)

                {

                    e.T1HaBE = true;
                    e.StopBE = alcista
                        ? e.LimitPrecio + (BT_BEPlusTicks * _tickSize)
                        : e.LimitPrecio - (BT_BEPlusTicks * _tickSize);

                    // Si en esta misma barra también tocó T2 o StopBE, se resuelve abajo
                    stopActivo = e.StopBE;
                    hitStop = alcista ? Low[0] <= stopActivo : High[0] >= stopActivo;

                }

                // Prioridad de cierre: T2 > T1solo > StopBE/Stop
                if (hitT2)

                {

                    WriteCloseEntry(e, "T2", true, true, e.T2);
                    _activeEntries.RemoveAt(i);

                }

                else if (hitStop)

                {

                    string resultado = e.T1HaBE ? "BE" : "STOP";
                    WriteCloseEntry(e, resultado, e.T1HaBE, false, stopActivo);
                    _activeEntries.RemoveAt(i);

                }

                else if (hitT1 && !hitT2)

                {

                    // Solo T1 tocado esta barra (sin T2 ni StopBE en misma barra): continuar tracking
                    // No se escribe CIERRE todavía — se espera T2 o StopBE en barras siguientes

                }

            }

        }



        #endregion



        #region Properties



        [NinjaScriptProperty][Display(Name = "Market Type", Order = 1, GroupName = "0. General")]

        public V5_MercadoTipo Market_Type { get; set; }



        [NinjaScriptProperty][Display(Name = "BarType (Tick=true, Minute=false)", Order = 2, GroupName = "0. General")]

        public bool BarType_Tick { get; set; }



        [NinjaScriptProperty][Display(Name = "Ruta Log", Order = 3, GroupName = "0. General")]

        public string RutaLog { get; set; }



        [NinjaScriptProperty][Display(Name = "Archivo Log", Order = 4, GroupName = "0. General")]

        public string NombreArchivoLog { get; set; }



        [NinjaScriptProperty][Display(Name = "Registrar Senales", Order = 5, GroupName = "0. General")]

        public bool RegistrarSenales { get; set; }



        [NinjaScriptProperty][Display(Name = "Loggear Historico", Order = 6, GroupName = "0. General")]

        public bool LoggearHistorico { get; set; }



        // Fix #9: parámetros para el modelo de backtesting realista
        [NinjaScriptProperty]
        [Range(0.0, 2.0)]
        [Display(Name = "BT Limit Slippage (x ATR)", Order = 6, GroupName = "0.1 Backtesting",
            Description = "Margen ATR para calcular el precio Limit en backtesting. " +
                          "Igual que LimitSlippageATRs de la estrategia. Default 0.2")]
        public double BT_LimitSlippageATRs { get; set; }

        [NinjaScriptProperty]
        [Range(0, 50)]
        [Display(Name = "BT BE Plus Ticks", Order = 7, GroupName = "0.1 Backtesting",
            Description = "Ticks de beneficio sobre el precio de entrada para el Stop BE en backtesting. " +
                          "Igual que BEPlusTicks de la estrategia. Default 4")]
        public int BT_BEPlusTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "BT Limit Timeout (barras)", Order = 8, GroupName = "0.1 Backtesting",
            Description = "Máx barras esperando fill de la Limit antes de registrar CANCELADA. " +
                          "Igual que LimitTimeoutBarras de la estrategia. Default 5")]
        public int BT_LimitTimeoutBarras { get; set; }



        [NinjaScriptProperty][Display(Name = "Config CSV", Order = 7, GroupName = "0. General")]

        public string ConfigCSV { get; set; }



        [Display(Name = "Modo Detección Volatilidad", Order = 8, GroupName = "0. General")]
        public V5_DeteccionModo ModoDeteccionVol { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Dirección Operación", Order = 9, GroupName = "0. General")]
        public V5_DireccionModo DireccionOperacion { get; set; }



        [NinjaScriptProperty][Display(Name = "Ventana Flotante", Order = 1, GroupName = "0. UI")]

        public bool Ventana_Flotante { get; set; }



        [NinjaScriptProperty][Display(Name = "Mostrar En Chart", Order = 2, GroupName = "0. UI")]

        public bool Mostrar_En_Chart { get; set; }



        [NinjaScriptProperty][Range(0, 9999)][Display(Name = "Pos X", Order = 3, GroupName = "0. UI")]

        public int Hud_X { get; set; }



        [NinjaScriptProperty][Range(0, 9999)][Display(Name = "Pos Y", Order = 4, GroupName = "0. UI")]

        public int Hud_Y { get; set; }



        [NinjaScriptProperty][Range(150, 1000)][Display(Name = "Ancho HUD", Order = 5, GroupName = "0. UI")]

        public int Hud_Ancho { get; set; }



        [NinjaScriptProperty][Display(Name = "Mostrar Semaforo", Order = 1, GroupName = "1. Secciones")]

        public bool Mostrar_Semaforo { get; set; }



        [NinjaScriptProperty][Display(Name = "Mostrar Senales", Order = 2, GroupName = "1. Secciones")]

        public bool Mostrar_Senales { get; set; }



        [NinjaScriptProperty][Display(Name = "Mostrar Entrada", Order = 3, GroupName = "1. Secciones")]

        public bool Mostrar_Entrada { get; set; }



        [NinjaScriptProperty][Display(Name = "Mostrar Keltner", Order = 4, GroupName = "1. Secciones")]

        public bool Mostrar_Keltner { get; set; }



        [NinjaScriptProperty][Display(Name = "Mostrar Posicion", Order = 5, GroupName = "1. Secciones")]

        public bool Mostrar_Posicion { get; set; }



        [NinjaScriptProperty][Display(Name = "Account Name", Order = 6, GroupName = "1. Secciones")]

        public string AccountName { get; set; }



        [NinjaScriptProperty][Range(1, 60)][Display(Name = "Pos Update (seg)", Order = 7, GroupName = "1. Secciones")]

        public int Pos_UpdateSeg { get; set; }



        [NinjaScriptProperty][Range(1, 500)][Display(Name = "SMA20 Period", Order = 1, GroupName = "2. Indicadores")]

        public int SMA20_Period { get; set; }



        [NinjaScriptProperty][Range(1, 500)][Display(Name = "SMA80 Period", Order = 2, GroupName = "2. Indicadores")]

        public int SMA80_Period { get; set; }



        [NinjaScriptProperty][Range(1, 500)][Display(Name = "LinReg Period", Order = 3, GroupName = "2. Indicadores")]

        public int LinReg_Period { get; set; }



        [NinjaScriptProperty][Display(Name = "LinReg Slope Threshold", Order = 4, GroupName = "2. Indicadores")]

        public double LinReg_SlopeThreshold { get; set; }



        [NinjaScriptProperty][Range(1, 100)][Display(Name = "ATR Period", Order = 5, GroupName = "2. Indicadores")]

        public int ATR_Period { get; set; }



        [NinjaScriptProperty][Range(5, 500)][Display(Name = "AVWAP SwingPeriod", Order = 6, GroupName = "2. Indicadores")]

        public int AVWAP_SwingPeriod { get; set; }



        [NinjaScriptProperty][Range(1, 100)][Display(Name = "AVWAP BaseAPT", Order = 7, GroupName = "2. Indicadores")]

        public int AVWAP_BaseAPT { get; set; }



        [NinjaScriptProperty][Display(Name = "AVWAP AdptAPT", Order = 8, GroupName = "2. Indicadores")]

        public bool AVWAP_AdptAPT { get; set; }



        [NinjaScriptProperty][Display(Name = "AVWAP VolBIAS", Order = 9, GroupName = "2. Indicadores")]

        public double AVWAP_VolBIAS { get; set; }



        [NinjaScriptProperty][Range(1, 200)][Display(Name = "Kelt Vol Period", Order = 10, GroupName = "2. Indicadores")]

        public int Kelt_Period_Vol { get; set; }



        [NinjaScriptProperty][Range(0.1, 10)][Display(Name = "Kelt Vol Mult", Order = 11, GroupName = "2. Indicadores")]

        public double Kelt_Mult_Vol { get; set; }



        [NinjaScriptProperty][Display(Name = "Usar Ranking", Order = 0, GroupName = "3. Zonas y Ranking")]

        public bool UsarRanking { get; set; }



        [NinjaScriptProperty][Range(0, 7)][Display(Name = "Ranking A", Order = 1, GroupName = "3. Zonas y Ranking")]

        public int Ranking_A { get; set; }



        [NinjaScriptProperty][Range(0, 7)][Display(Name = "Ranking LR", Order = 2, GroupName = "3. Zonas y Ranking")]

        public int Ranking_LR { get; set; }



        [NinjaScriptProperty][Range(0, 7)][Display(Name = "Ranking 20", Order = 3, GroupName = "3. Zonas y Ranking")]

        public int Ranking_20 { get; set; }



        [NinjaScriptProperty][Range(0, 7)][Display(Name = "Ranking 80", Order = 4, GroupName = "3. Zonas y Ranking")]

        public int Ranking_80 { get; set; }



        [NinjaScriptProperty][Range(0, 7)][Display(Name = "Ranking M1", Order = 5, GroupName = "3. Zonas y Ranking")]

        public int Ranking_M1 { get; set; }



        [NinjaScriptProperty][Range(0, 7)][Display(Name = "Ranking M2", Order = 6, GroupName = "3. Zonas y Ranking")]

        public int Ranking_M2 { get; set; }



        [NinjaScriptProperty][Range(0, 7)][Display(Name = "Ranking M3", Order = 7, GroupName = "3. Zonas y Ranking")]

        public int Ranking_M3 { get; set; }



        [NinjaScriptProperty][Range(0.1, 5)][Display(Name = "Zona Tolerancia (x ATR)", Order = 8, GroupName = "3. Zonas y Ranking")]

        public double ZonaToleranciaPct { get; set; }



        [NinjaScriptProperty][Range(0.1, 5)][Display(Name = "Stop ATRx", Order = 1, GroupName = "4. Stop y Targets")]

        public double StopATRx { get; set; }



        [NinjaScriptProperty][Range(0.0001, 0.05)][Display(Name = "Stop %", Order = 2, GroupName = "4. Stop y Targets")]

        public double StopPct { get; set; }



        [NinjaScriptProperty][Range(0, 500)][Display(Name = "Stop Max Ticks (0=off)", Order = 3, GroupName = "4. Stop y Targets")]

        public int StopMaxTicks { get; set; }



        [NinjaScriptProperty][Range(1, 100)][Display(Name = "Stop Min Ticks", Order = 4, GroupName = "4. Stop y Targets")]

        public int StopMinTicks { get; set; }



        [NinjaScriptProperty][Range(0.05, 1.0)][Display(Name = "Kelt Stop %", Order = 5, GroupName = "4. Stop y Targets")]

        public double Kelt_StopPct { get; set; }



        [NinjaScriptProperty][Range(0.5, 10)][Display(Name = "RR1 Ratio", Order = 6, GroupName = "4. Stop y Targets")]

        public double RR1_Ratio { get; set; }



        [NinjaScriptProperty][Range(0.5, 10)][Display(Name = "RR2 Ratio", Order = 5, GroupName = "4. Stop y Targets")]

        public double RR2_Ratio { get; set; }



        [NinjaScriptProperty][Range(0.5, 5)][Display(Name = "RR Min Filter", Order = 6, GroupName = "4. Stop y Targets")]

        public double RR_MinFiltro { get; set; }



        // FIX-002 (2026-05-13): Nueva propiedad CooldownBars para evitar señales consecutivas

        // en la misma zona. 0 = sin cooldown (comportamiento original, que generaba ~57K señales).

        // Recomendado: 5-10 barras según el timeframe.

        [NinjaScriptProperty][Range(0, 100)][Display(Name = "Cooldown Barras Post-Senal", Order = 7, GroupName = "4. Stop y Targets",

            Description = "Barras de espera entre señales para evitar duplicados. 0 = sin cooldown. Recomendado: 5-10.")]

        public int CooldownBars { get; set; }



        [NinjaScriptProperty][Display(Name = "Usar Capa 3", Order = 1, GroupName = "5. Filtros")]

        public bool UsarCapa3 { get; set; }



        [NinjaScriptProperty][Display(Name = "Usar Capa 4", Order = 2, GroupName = "5. Filtros")]

        public bool UsarCapa4 { get; set; }



        [NinjaScriptProperty][Range(0, 5)][Display(Name = "BM Tolerancia (ATR)", Order = 3, GroupName = "5. Filtros")]

        public double ToleranciaBM_ATR { get; set; }



        [NinjaScriptProperty][Display(Name = "Usar HTF", Order = 4, GroupName = "5. Filtros")]

        public bool UsarFiltroHTF { get; set; }



        [NinjaScriptProperty][Range(1, 50000)][Display(Name = "HTF Ticks", Order = 5, GroupName = "5. Filtros")]

        public int HTF_Ticks { get; set; }



        [NinjaScriptProperty][Range(1, 240)][Display(Name = "HTF Minutos", Order = 6, GroupName = "5. Filtros")]

        public int HTF_Minutos { get; set; }



        [NinjaScriptProperty][Display(Name = "Usar Nodos", Order = 7, GroupName = "5. Filtros")]

        public bool UsarNodos { get; set; }



        [NinjaScriptProperty][Range(0, 50)][Display(Name = "Nodo Dist (Ticks)", Order = 8, GroupName = "5. Filtros")]

        public int Nodo_DistTicks { get; set; }



        [NinjaScriptProperty][Display(Name = "Usar POCs", Order = 9, GroupName = "5. Filtros")]

        public bool UsarPocs { get; set; }



        [NinjaScriptProperty][Range(0, 50)][Display(Name = "POC Dist (Ticks)", Order = 10, GroupName = "5. Filtros")]

        public int Poc_DistTicks { get; set; }



        [NinjaScriptProperty][Range(0, 2)][Display(Name = "Perfil Modo (0=SwingRecalc,1=SwingPrevio,2=DesdeHora)", Order = 1, GroupName = "6. Perfil Volumen")]

        public int PerfilModo { get; set; }



        [NinjaScriptProperty][Range(0, 2400)][Display(Name = "Perfil Desde Hora (HHMM)", Order = 2, GroupName = "6. Perfil Volumen")]

        public int Perfil_DesdeHora { get; set; }



        [NinjaScriptProperty][Range(1, 480)][Display(Name = "Window Minutes", Order = 3, GroupName = "6. Perfil Volumen")]

        public int WindowMinutes { get; set; }



        [NinjaScriptProperty][Display(Name = "Sonido Green Zone", Order = 1, GroupName = "7. Audio")]
        public string SonidoGreenZone { get; set; }

        [NinjaScriptProperty][Display(Name = "Sonido In Zone", Order = 2, GroupName = "7. Audio")]
        public string SonidoInZone { get; set; }

        [NinjaScriptProperty][Range(0, 2000)][Display(Name = "Offset Izquierda (Pixels)", Order = 1, GroupName = "8. Visual Señales")]
        public int Senales_OffsetIzq { get; set; }

        [NinjaScriptProperty][System.Xml.Serialization.XmlIgnore()]
        [Display(Name = "Color Fondo Etiquetas", Order = 2, GroupName = "8. Visual Señales")]
        public System.Windows.Media.Brush Senales_FondoColor { get; set; }

        // Serialize the Brush
        [Browsable(false)]
        public string Senales_FondoColorSerialize
        {
            get { return Serialize.BrushToString(Senales_FondoColor); }
            set { Senales_FondoColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty][Display(Name = "Estilo de Linea", Order = 3, GroupName = "8. Visual Señales")]
        public DashStyleHelper Senales_LineStyle { get; set; }

        #endregion



        private System.Windows.Threading.Dispatcher GetUI()

        {

            try { return ChartControl?.Dispatcher; } catch { }

            try { return System.Windows.Application.Current?.Dispatcher; } catch { }

            return null;

        }

    }

}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduS_MasterPanel_HUD_V5[] cacheEduS_MasterPanel_HUD_V5;
		public EduS_Trader.EduS_MasterPanel_HUD_V5 EduS_MasterPanel_HUD_V5(V5_MercadoTipo market_Type, bool barType_Tick, string rutaLog, string nombreArchivoLog, bool registrarSenales, bool loggearHistorico, string configCSV, V5_DireccionModo direccionOperacion, bool ventana_Flotante, bool mostrar_En_Chart, int hud_X, int hud_Y, int hud_Ancho, bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, bool mostrar_Posicion, string accountName, int pos_UpdateSeg, int sMA20_Period, int sMA80_Period, int linReg_Period, double linReg_SlopeThreshold, int aTR_Period, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, bool aVWAP_AdptAPT, double aVWAP_VolBIAS, int kelt_Period_Vol, double kelt_Mult_Vol, bool usarRanking, int ranking_A, int ranking_LR, int ranking_20, int ranking_80, int ranking_M1, int ranking_M2, int ranking_M3, double zonaToleranciaPct, double stopATRx, double stopPct, int stopMaxTicks, int stopMinTicks, double kelt_StopPct, double rR1_Ratio, double rR2_Ratio, double rR_MinFiltro, int cooldownBars, bool usarCapa3, bool usarCapa4, double toleranciaBM_ATR, bool usarFiltroHTF, int hTF_Ticks, int hTF_Minutos, bool usarNodos, int nodo_DistTicks, bool usarPocs, int poc_DistTicks, int perfilModo, int perfil_DesdeHora, int windowMinutes, string sonidoGreenZone, string sonidoInZone, int senales_OffsetIzq, System.Windows.Media.Brush senales_FondoColor, DashStyleHelper senales_LineStyle)
		{
			return EduS_MasterPanel_HUD_V5(Input, market_Type, barType_Tick, rutaLog, nombreArchivoLog, registrarSenales, loggearHistorico, configCSV, direccionOperacion, ventana_Flotante, mostrar_En_Chart, hud_X, hud_Y, hud_Ancho, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, mostrar_Posicion, accountName, pos_UpdateSeg, sMA20_Period, sMA80_Period, linReg_Period, linReg_SlopeThreshold, aTR_Period, aVWAP_SwingPeriod, aVWAP_BaseAPT, aVWAP_AdptAPT, aVWAP_VolBIAS, kelt_Period_Vol, kelt_Mult_Vol, usarRanking, ranking_A, ranking_LR, ranking_20, ranking_80, ranking_M1, ranking_M2, ranking_M3, zonaToleranciaPct, stopATRx, stopPct, stopMaxTicks, stopMinTicks, kelt_StopPct, rR1_Ratio, rR2_Ratio, rR_MinFiltro, cooldownBars, usarCapa3, usarCapa4, toleranciaBM_ATR, usarFiltroHTF, hTF_Ticks, hTF_Minutos, usarNodos, nodo_DistTicks, usarPocs, poc_DistTicks, perfilModo, perfil_DesdeHora, windowMinutes, sonidoGreenZone, sonidoInZone, senales_OffsetIzq, senales_FondoColor, senales_LineStyle);
		}

		public EduS_Trader.EduS_MasterPanel_HUD_V5 EduS_MasterPanel_HUD_V5(ISeries<double> input, V5_MercadoTipo market_Type, bool barType_Tick, string rutaLog, string nombreArchivoLog, bool registrarSenales, bool loggearHistorico, string configCSV, V5_DireccionModo direccionOperacion, bool ventana_Flotante, bool mostrar_En_Chart, int hud_X, int hud_Y, int hud_Ancho, bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, bool mostrar_Posicion, string accountName, int pos_UpdateSeg, int sMA20_Period, int sMA80_Period, int linReg_Period, double linReg_SlopeThreshold, int aTR_Period, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, bool aVWAP_AdptAPT, double aVWAP_VolBIAS, int kelt_Period_Vol, double kelt_Mult_Vol, bool usarRanking, int ranking_A, int ranking_LR, int ranking_20, int ranking_80, int ranking_M1, int ranking_M2, int ranking_M3, double zonaToleranciaPct, double stopATRx, double stopPct, int stopMaxTicks, int stopMinTicks, double kelt_StopPct, double rR1_Ratio, double rR2_Ratio, double rR_MinFiltro, int cooldownBars, bool usarCapa3, bool usarCapa4, double toleranciaBM_ATR, bool usarFiltroHTF, int hTF_Ticks, int hTF_Minutos, bool usarNodos, int nodo_DistTicks, bool usarPocs, int poc_DistTicks, int perfilModo, int perfil_DesdeHora, int windowMinutes, string sonidoGreenZone, string sonidoInZone, int senales_OffsetIzq, System.Windows.Media.Brush senales_FondoColor, DashStyleHelper senales_LineStyle)
		{
			if (cacheEduS_MasterPanel_HUD_V5 != null)
				for (int idx = 0; idx < cacheEduS_MasterPanel_HUD_V5.Length; idx++)
					if (cacheEduS_MasterPanel_HUD_V5[idx] != null && cacheEduS_MasterPanel_HUD_V5[idx].Market_Type == market_Type && cacheEduS_MasterPanel_HUD_V5[idx].BarType_Tick == barType_Tick && cacheEduS_MasterPanel_HUD_V5[idx].RutaLog == rutaLog && cacheEduS_MasterPanel_HUD_V5[idx].NombreArchivoLog == nombreArchivoLog && cacheEduS_MasterPanel_HUD_V5[idx].RegistrarSenales == registrarSenales && cacheEduS_MasterPanel_HUD_V5[idx].LoggearHistorico == loggearHistorico && cacheEduS_MasterPanel_HUD_V5[idx].ConfigCSV == configCSV && cacheEduS_MasterPanel_HUD_V5[idx].DireccionOperacion == direccionOperacion && cacheEduS_MasterPanel_HUD_V5[idx].Ventana_Flotante == ventana_Flotante && cacheEduS_MasterPanel_HUD_V5[idx].Mostrar_En_Chart == mostrar_En_Chart && cacheEduS_MasterPanel_HUD_V5[idx].Hud_X == hud_X && cacheEduS_MasterPanel_HUD_V5[idx].Hud_Y == hud_Y && cacheEduS_MasterPanel_HUD_V5[idx].Hud_Ancho == hud_Ancho && cacheEduS_MasterPanel_HUD_V5[idx].Mostrar_Semaforo == mostrar_Semaforo && cacheEduS_MasterPanel_HUD_V5[idx].Mostrar_Senales == mostrar_Senales && cacheEduS_MasterPanel_HUD_V5[idx].Mostrar_Entrada == mostrar_Entrada && cacheEduS_MasterPanel_HUD_V5[idx].Mostrar_Keltner == mostrar_Keltner && cacheEduS_MasterPanel_HUD_V5[idx].Mostrar_Posicion == mostrar_Posicion && cacheEduS_MasterPanel_HUD_V5[idx].AccountName == accountName && cacheEduS_MasterPanel_HUD_V5[idx].Pos_UpdateSeg == pos_UpdateSeg && cacheEduS_MasterPanel_HUD_V5[idx].SMA20_Period == sMA20_Period && cacheEduS_MasterPanel_HUD_V5[idx].SMA80_Period == sMA80_Period && cacheEduS_MasterPanel_HUD_V5[idx].LinReg_Period == linReg_Period && cacheEduS_MasterPanel_HUD_V5[idx].LinReg_SlopeThreshold == linReg_SlopeThreshold && cacheEduS_MasterPanel_HUD_V5[idx].ATR_Period == aTR_Period && cacheEduS_MasterPanel_HUD_V5[idx].AVWAP_SwingPeriod == aVWAP_SwingPeriod && cacheEduS_MasterPanel_HUD_V5[idx].AVWAP_BaseAPT == aVWAP_BaseAPT && cacheEduS_MasterPanel_HUD_V5[idx].AVWAP_AdptAPT == aVWAP_AdptAPT && cacheEduS_MasterPanel_HUD_V5[idx].AVWAP_VolBIAS == aVWAP_VolBIAS && cacheEduS_MasterPanel_HUD_V5[idx].Kelt_Period_Vol == kelt_Period_Vol && cacheEduS_MasterPanel_HUD_V5[idx].Kelt_Mult_Vol == kelt_Mult_Vol && cacheEduS_MasterPanel_HUD_V5[idx].UsarRanking == usarRanking && cacheEduS_MasterPanel_HUD_V5[idx].Ranking_A == ranking_A && cacheEduS_MasterPanel_HUD_V5[idx].Ranking_LR == ranking_LR && cacheEduS_MasterPanel_HUD_V5[idx].Ranking_20 == ranking_20 && cacheEduS_MasterPanel_HUD_V5[idx].Ranking_80 == ranking_80 && cacheEduS_MasterPanel_HUD_V5[idx].Ranking_M1 == ranking_M1 && cacheEduS_MasterPanel_HUD_V5[idx].Ranking_M2 == ranking_M2 && cacheEduS_MasterPanel_HUD_V5[idx].Ranking_M3 == ranking_M3 && cacheEduS_MasterPanel_HUD_V5[idx].ZonaToleranciaPct == zonaToleranciaPct && cacheEduS_MasterPanel_HUD_V5[idx].StopATRx == stopATRx && cacheEduS_MasterPanel_HUD_V5[idx].StopPct == stopPct && cacheEduS_MasterPanel_HUD_V5[idx].StopMaxTicks == stopMaxTicks && cacheEduS_MasterPanel_HUD_V5[idx].StopMinTicks == stopMinTicks && cacheEduS_MasterPanel_HUD_V5[idx].Kelt_StopPct == kelt_StopPct && cacheEduS_MasterPanel_HUD_V5[idx].RR1_Ratio == rR1_Ratio && cacheEduS_MasterPanel_HUD_V5[idx].RR2_Ratio == rR2_Ratio && cacheEduS_MasterPanel_HUD_V5[idx].RR_MinFiltro == rR_MinFiltro && cacheEduS_MasterPanel_HUD_V5[idx].CooldownBars == cooldownBars && cacheEduS_MasterPanel_HUD_V5[idx].UsarCapa3 == usarCapa3 && cacheEduS_MasterPanel_HUD_V5[idx].UsarCapa4 == usarCapa4 && cacheEduS_MasterPanel_HUD_V5[idx].ToleranciaBM_ATR == toleranciaBM_ATR && cacheEduS_MasterPanel_HUD_V5[idx].UsarFiltroHTF == usarFiltroHTF && cacheEduS_MasterPanel_HUD_V5[idx].HTF_Ticks == hTF_Ticks && cacheEduS_MasterPanel_HUD_V5[idx].HTF_Minutos == hTF_Minutos && cacheEduS_MasterPanel_HUD_V5[idx].UsarNodos == usarNodos && cacheEduS_MasterPanel_HUD_V5[idx].Nodo_DistTicks == nodo_DistTicks && cacheEduS_MasterPanel_HUD_V5[idx].UsarPocs == usarPocs && cacheEduS_MasterPanel_HUD_V5[idx].Poc_DistTicks == poc_DistTicks && cacheEduS_MasterPanel_HUD_V5[idx].PerfilModo == perfilModo && cacheEduS_MasterPanel_HUD_V5[idx].Perfil_DesdeHora == perfil_DesdeHora && cacheEduS_MasterPanel_HUD_V5[idx].WindowMinutes == windowMinutes && cacheEduS_MasterPanel_HUD_V5[idx].SonidoGreenZone == sonidoGreenZone && cacheEduS_MasterPanel_HUD_V5[idx].SonidoInZone == sonidoInZone && cacheEduS_MasterPanel_HUD_V5[idx].Senales_OffsetIzq == senales_OffsetIzq && cacheEduS_MasterPanel_HUD_V5[idx].Senales_FondoColor == senales_FondoColor && cacheEduS_MasterPanel_HUD_V5[idx].Senales_LineStyle == senales_LineStyle && cacheEduS_MasterPanel_HUD_V5[idx].EqualsInput(input))
						return cacheEduS_MasterPanel_HUD_V5[idx];
			return CacheIndicator<EduS_Trader.EduS_MasterPanel_HUD_V5>(new EduS_Trader.EduS_MasterPanel_HUD_V5(){ Market_Type = market_Type, BarType_Tick = barType_Tick, RutaLog = rutaLog, NombreArchivoLog = nombreArchivoLog, RegistrarSenales = registrarSenales, LoggearHistorico = loggearHistorico, ConfigCSV = configCSV, DireccionOperacion = direccionOperacion, Ventana_Flotante = ventana_Flotante, Mostrar_En_Chart = mostrar_En_Chart, Hud_X = hud_X, Hud_Y = hud_Y, Hud_Ancho = hud_Ancho, Mostrar_Semaforo = mostrar_Semaforo, Mostrar_Senales = mostrar_Senales, Mostrar_Entrada = mostrar_Entrada, Mostrar_Keltner = mostrar_Keltner, Mostrar_Posicion = mostrar_Posicion, AccountName = accountName, Pos_UpdateSeg = pos_UpdateSeg, SMA20_Period = sMA20_Period, SMA80_Period = sMA80_Period, LinReg_Period = linReg_Period, LinReg_SlopeThreshold = linReg_SlopeThreshold, ATR_Period = aTR_Period, AVWAP_SwingPeriod = aVWAP_SwingPeriod, AVWAP_BaseAPT = aVWAP_BaseAPT, AVWAP_AdptAPT = aVWAP_AdptAPT, AVWAP_VolBIAS = aVWAP_VolBIAS, Kelt_Period_Vol = kelt_Period_Vol, Kelt_Mult_Vol = kelt_Mult_Vol, UsarRanking = usarRanking, Ranking_A = ranking_A, Ranking_LR = ranking_LR, Ranking_20 = ranking_20, Ranking_80 = ranking_80, Ranking_M1 = ranking_M1, Ranking_M2 = ranking_M2, Ranking_M3 = ranking_M3, ZonaToleranciaPct = zonaToleranciaPct, StopATRx = stopATRx, StopPct = stopPct, StopMaxTicks = stopMaxTicks, StopMinTicks = stopMinTicks, Kelt_StopPct = kelt_StopPct, RR1_Ratio = rR1_Ratio, RR2_Ratio = rR2_Ratio, RR_MinFiltro = rR_MinFiltro, CooldownBars = cooldownBars, UsarCapa3 = usarCapa3, UsarCapa4 = usarCapa4, ToleranciaBM_ATR = toleranciaBM_ATR, UsarFiltroHTF = usarFiltroHTF, HTF_Ticks = hTF_Ticks, HTF_Minutos = hTF_Minutos, UsarNodos = usarNodos, Nodo_DistTicks = nodo_DistTicks, UsarPocs = usarPocs, Poc_DistTicks = poc_DistTicks, PerfilModo = perfilModo, Perfil_DesdeHora = perfil_DesdeHora, WindowMinutes = windowMinutes, SonidoGreenZone = sonidoGreenZone, SonidoInZone = sonidoInZone, Senales_OffsetIzq = senales_OffsetIzq, Senales_FondoColor = senales_FondoColor, Senales_LineStyle = senales_LineStyle }, input, ref cacheEduS_MasterPanel_HUD_V5);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduS_MasterPanel_HUD_V5 EduS_MasterPanel_HUD_V5(V5_MercadoTipo market_Type, bool barType_Tick, string rutaLog, string nombreArchivoLog, bool registrarSenales, bool loggearHistorico, string configCSV, V5_DireccionModo direccionOperacion, bool ventana_Flotante, bool mostrar_En_Chart, int hud_X, int hud_Y, int hud_Ancho, bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, bool mostrar_Posicion, string accountName, int pos_UpdateSeg, int sMA20_Period, int sMA80_Period, int linReg_Period, double linReg_SlopeThreshold, int aTR_Period, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, bool aVWAP_AdptAPT, double aVWAP_VolBIAS, int kelt_Period_Vol, double kelt_Mult_Vol, bool usarRanking, int ranking_A, int ranking_LR, int ranking_20, int ranking_80, int ranking_M1, int ranking_M2, int ranking_M3, double zonaToleranciaPct, double stopATRx, double stopPct, int stopMaxTicks, int stopMinTicks, double kelt_StopPct, double rR1_Ratio, double rR2_Ratio, double rR_MinFiltro, int cooldownBars, bool usarCapa3, bool usarCapa4, double toleranciaBM_ATR, bool usarFiltroHTF, int hTF_Ticks, int hTF_Minutos, bool usarNodos, int nodo_DistTicks, bool usarPocs, int poc_DistTicks, int perfilModo, int perfil_DesdeHora, int windowMinutes, string sonidoGreenZone, string sonidoInZone, int senales_OffsetIzq, System.Windows.Media.Brush senales_FondoColor, DashStyleHelper senales_LineStyle)
		{
			return indicator.EduS_MasterPanel_HUD_V5(Input, market_Type, barType_Tick, rutaLog, nombreArchivoLog, registrarSenales, loggearHistorico, configCSV, direccionOperacion, ventana_Flotante, mostrar_En_Chart, hud_X, hud_Y, hud_Ancho, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, mostrar_Posicion, accountName, pos_UpdateSeg, sMA20_Period, sMA80_Period, linReg_Period, linReg_SlopeThreshold, aTR_Period, aVWAP_SwingPeriod, aVWAP_BaseAPT, aVWAP_AdptAPT, aVWAP_VolBIAS, kelt_Period_Vol, kelt_Mult_Vol, usarRanking, ranking_A, ranking_LR, ranking_20, ranking_80, ranking_M1, ranking_M2, ranking_M3, zonaToleranciaPct, stopATRx, stopPct, stopMaxTicks, stopMinTicks, kelt_StopPct, rR1_Ratio, rR2_Ratio, rR_MinFiltro, cooldownBars, usarCapa3, usarCapa4, toleranciaBM_ATR, usarFiltroHTF, hTF_Ticks, hTF_Minutos, usarNodos, nodo_DistTicks, usarPocs, poc_DistTicks, perfilModo, perfil_DesdeHora, windowMinutes, sonidoGreenZone, sonidoInZone, senales_OffsetIzq, senales_FondoColor, senales_LineStyle);
		}

		public Indicators.EduS_Trader.EduS_MasterPanel_HUD_V5 EduS_MasterPanel_HUD_V5(ISeries<double> input , V5_MercadoTipo market_Type, bool barType_Tick, string rutaLog, string nombreArchivoLog, bool registrarSenales, bool loggearHistorico, string configCSV, V5_DireccionModo direccionOperacion, bool ventana_Flotante, bool mostrar_En_Chart, int hud_X, int hud_Y, int hud_Ancho, bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, bool mostrar_Posicion, string accountName, int pos_UpdateSeg, int sMA20_Period, int sMA80_Period, int linReg_Period, double linReg_SlopeThreshold, int aTR_Period, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, bool aVWAP_AdptAPT, double aVWAP_VolBIAS, int kelt_Period_Vol, double kelt_Mult_Vol, bool usarRanking, int ranking_A, int ranking_LR, int ranking_20, int ranking_80, int ranking_M1, int ranking_M2, int ranking_M3, double zonaToleranciaPct, double stopATRx, double stopPct, int stopMaxTicks, int stopMinTicks, double kelt_StopPct, double rR1_Ratio, double rR2_Ratio, double rR_MinFiltro, int cooldownBars, bool usarCapa3, bool usarCapa4, double toleranciaBM_ATR, bool usarFiltroHTF, int hTF_Ticks, int hTF_Minutos, bool usarNodos, int nodo_DistTicks, bool usarPocs, int poc_DistTicks, int perfilModo, int perfil_DesdeHora, int windowMinutes, string sonidoGreenZone, string sonidoInZone, int senales_OffsetIzq, System.Windows.Media.Brush senales_FondoColor, DashStyleHelper senales_LineStyle)
		{
			return indicator.EduS_MasterPanel_HUD_V5(input, market_Type, barType_Tick, rutaLog, nombreArchivoLog, registrarSenales, loggearHistorico, configCSV, direccionOperacion, ventana_Flotante, mostrar_En_Chart, hud_X, hud_Y, hud_Ancho, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, mostrar_Posicion, accountName, pos_UpdateSeg, sMA20_Period, sMA80_Period, linReg_Period, linReg_SlopeThreshold, aTR_Period, aVWAP_SwingPeriod, aVWAP_BaseAPT, aVWAP_AdptAPT, aVWAP_VolBIAS, kelt_Period_Vol, kelt_Mult_Vol, usarRanking, ranking_A, ranking_LR, ranking_20, ranking_80, ranking_M1, ranking_M2, ranking_M3, zonaToleranciaPct, stopATRx, stopPct, stopMaxTicks, stopMinTicks, kelt_StopPct, rR1_Ratio, rR2_Ratio, rR_MinFiltro, cooldownBars, usarCapa3, usarCapa4, toleranciaBM_ATR, usarFiltroHTF, hTF_Ticks, hTF_Minutos, usarNodos, nodo_DistTicks, usarPocs, poc_DistTicks, perfilModo, perfil_DesdeHora, windowMinutes, sonidoGreenZone, sonidoInZone, senales_OffsetIzq, senales_FondoColor, senales_LineStyle);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduS_MasterPanel_HUD_V5 EduS_MasterPanel_HUD_V5(V5_MercadoTipo market_Type, bool barType_Tick, string rutaLog, string nombreArchivoLog, bool registrarSenales, bool loggearHistorico, string configCSV, V5_DireccionModo direccionOperacion, bool ventana_Flotante, bool mostrar_En_Chart, int hud_X, int hud_Y, int hud_Ancho, bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, bool mostrar_Posicion, string accountName, int pos_UpdateSeg, int sMA20_Period, int sMA80_Period, int linReg_Period, double linReg_SlopeThreshold, int aTR_Period, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, bool aVWAP_AdptAPT, double aVWAP_VolBIAS, int kelt_Period_Vol, double kelt_Mult_Vol, bool usarRanking, int ranking_A, int ranking_LR, int ranking_20, int ranking_80, int ranking_M1, int ranking_M2, int ranking_M3, double zonaToleranciaPct, double stopATRx, double stopPct, int stopMaxTicks, int stopMinTicks, double kelt_StopPct, double rR1_Ratio, double rR2_Ratio, double rR_MinFiltro, int cooldownBars, bool usarCapa3, bool usarCapa4, double toleranciaBM_ATR, bool usarFiltroHTF, int hTF_Ticks, int hTF_Minutos, bool usarNodos, int nodo_DistTicks, bool usarPocs, int poc_DistTicks, int perfilModo, int perfil_DesdeHora, int windowMinutes, string sonidoGreenZone, string sonidoInZone, int senales_OffsetIzq, System.Windows.Media.Brush senales_FondoColor, DashStyleHelper senales_LineStyle)
		{
			return indicator.EduS_MasterPanel_HUD_V5(Input, market_Type, barType_Tick, rutaLog, nombreArchivoLog, registrarSenales, loggearHistorico, configCSV, direccionOperacion, ventana_Flotante, mostrar_En_Chart, hud_X, hud_Y, hud_Ancho, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, mostrar_Posicion, accountName, pos_UpdateSeg, sMA20_Period, sMA80_Period, linReg_Period, linReg_SlopeThreshold, aTR_Period, aVWAP_SwingPeriod, aVWAP_BaseAPT, aVWAP_AdptAPT, aVWAP_VolBIAS, kelt_Period_Vol, kelt_Mult_Vol, usarRanking, ranking_A, ranking_LR, ranking_20, ranking_80, ranking_M1, ranking_M2, ranking_M3, zonaToleranciaPct, stopATRx, stopPct, stopMaxTicks, stopMinTicks, kelt_StopPct, rR1_Ratio, rR2_Ratio, rR_MinFiltro, cooldownBars, usarCapa3, usarCapa4, toleranciaBM_ATR, usarFiltroHTF, hTF_Ticks, hTF_Minutos, usarNodos, nodo_DistTicks, usarPocs, poc_DistTicks, perfilModo, perfil_DesdeHora, windowMinutes, sonidoGreenZone, sonidoInZone, senales_OffsetIzq, senales_FondoColor, senales_LineStyle);
		}

		public Indicators.EduS_Trader.EduS_MasterPanel_HUD_V5 EduS_MasterPanel_HUD_V5(ISeries<double> input , V5_MercadoTipo market_Type, bool barType_Tick, string rutaLog, string nombreArchivoLog, bool registrarSenales, bool loggearHistorico, string configCSV, V5_DireccionModo direccionOperacion, bool ventana_Flotante, bool mostrar_En_Chart, int hud_X, int hud_Y, int hud_Ancho, bool mostrar_Semaforo, bool mostrar_Senales, bool mostrar_Entrada, bool mostrar_Keltner, bool mostrar_Posicion, string accountName, int pos_UpdateSeg, int sMA20_Period, int sMA80_Period, int linReg_Period, double linReg_SlopeThreshold, int aTR_Period, int aVWAP_SwingPeriod, int aVWAP_BaseAPT, bool aVWAP_AdptAPT, double aVWAP_VolBIAS, int kelt_Period_Vol, double kelt_Mult_Vol, bool usarRanking, int ranking_A, int ranking_LR, int ranking_20, int ranking_80, int ranking_M1, int ranking_M2, int ranking_M3, double zonaToleranciaPct, double stopATRx, double stopPct, int stopMaxTicks, int stopMinTicks, double kelt_StopPct, double rR1_Ratio, double rR2_Ratio, double rR_MinFiltro, int cooldownBars, bool usarCapa3, bool usarCapa4, double toleranciaBM_ATR, bool usarFiltroHTF, int hTF_Ticks, int hTF_Minutos, bool usarNodos, int nodo_DistTicks, bool usarPocs, int poc_DistTicks, int perfilModo, int perfil_DesdeHora, int windowMinutes, string sonidoGreenZone, string sonidoInZone, int senales_OffsetIzq, System.Windows.Media.Brush senales_FondoColor, DashStyleHelper senales_LineStyle)
		{
			return indicator.EduS_MasterPanel_HUD_V5(input, market_Type, barType_Tick, rutaLog, nombreArchivoLog, registrarSenales, loggearHistorico, configCSV, direccionOperacion, ventana_Flotante, mostrar_En_Chart, hud_X, hud_Y, hud_Ancho, mostrar_Semaforo, mostrar_Senales, mostrar_Entrada, mostrar_Keltner, mostrar_Posicion, accountName, pos_UpdateSeg, sMA20_Period, sMA80_Period, linReg_Period, linReg_SlopeThreshold, aTR_Period, aVWAP_SwingPeriod, aVWAP_BaseAPT, aVWAP_AdptAPT, aVWAP_VolBIAS, kelt_Period_Vol, kelt_Mult_Vol, usarRanking, ranking_A, ranking_LR, ranking_20, ranking_80, ranking_M1, ranking_M2, ranking_M3, zonaToleranciaPct, stopATRx, stopPct, stopMaxTicks, stopMinTicks, kelt_StopPct, rR1_Ratio, rR2_Ratio, rR_MinFiltro, cooldownBars, usarCapa3, usarCapa4, toleranciaBM_ATR, usarFiltroHTF, hTF_Ticks, hTF_Minutos, usarNodos, nodo_DistTicks, usarPocs, poc_DistTicks, perfilModo, perfil_DesdeHora, windowMinutes, sonidoGreenZone, sonidoInZone, senales_OffsetIzq, senales_FondoColor, senales_LineStyle);
		}
	}
}

#endregion
