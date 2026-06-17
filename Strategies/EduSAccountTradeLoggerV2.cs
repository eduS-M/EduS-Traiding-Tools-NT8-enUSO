#region Using declarations
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using System.Windows;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Indicators.EduS_Trader;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class EduS_AccountTradeLogger_V2 : Strategy
    {
        // ===================================================
        // 1) IDs, POSICIONES, ARCHIVO
        // ===================================================

        private int    currentEntryId = 0;
        private int    fillCounter    = 0;
        private string logFilePath;

        private Dictionary<string, int> activeEntryIdByKey = new Dictionary<string, int>();
        private Dictionary<string, int> netPositionByKey    = new Dictionary<string, int>();
        private List<Account>           monitoredAccounts   = new List<Account>();

        // ===== Panel flotante de posición =====
        private System.Windows.Window                     _panelWindow;
        private System.Windows.Threading.DispatcherTimer  _panelTimer;
        private System.Windows.Controls.TextBlock         _tbTitulo, _tbInstr, _tbDirQty, _tbPL, _tbRiesgo, _tbTarget, _tbRR, _tbVacio;
        private System.Windows.Controls.StackPanel        _datosPanel;
        private System.Windows.Media.Brush                _bNavy, _bGold, _bGoldL, _bWhite, _bGreen, _bRed, _bMuted;
        private bool     _panelPedido = false;
        private bool     _terminado   = false;
        private DateTime _lastGeoSave = DateTime.MinValue;

        // ===================================================
        // 2) PARÁMETROS DE LA ESTRATEGIA (UI)
        // ===================================================

        [NinjaScriptProperty]
        [Display(Name = "Nombre Estrategia / Setup", GroupName = "01. Logger", Order = 0)]
        public string TradeName { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Ruta Archivo CSV", GroupName = "01. Logger", Order = 1)]
        public string CsvPath { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Cuentas a monitorear (coma separadas)", GroupName = "01. Logger", Order = 2)]
        public string AccountsToMonitor { get; set; }

        // ===================================================
        // 2b) PARÁMETROS DEL PANEL FLOTANTE DE POSICIÓN
        // ===================================================

        [NinjaScriptProperty]
        [Display(Name = "Mostrar panel de posición", GroupName = "02. Panel Posición", Order = 0)]
        public bool MostrarPanelPosicion { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Panel siempre visible (Topmost)", GroupName = "02. Panel Posición", Order = 1)]
        public bool PanelTopmost { get; set; }

        [NinjaScriptProperty]
        [Range(9, 48)]
        [Display(Name = "Tamaño de fuente base", GroupName = "02. Panel Posición", Order = 2)]
        public int PanelFontSize { get; set; }

        [NinjaScriptProperty]
        [Range(0.4, 1.0)]
        [Display(Name = "Opacidad del panel (0.4 - 1.0)", GroupName = "02. Panel Posición", Order = 3)]
        public double PanelOpacity { get; set; }

        public EduS_AccountTradeLogger_V2()
        {
            TradeName         = "Setup_Manual";
            CsvPath           = @"C:\Users\eduar\OneDrive - Desarrollo Personal\Documents\NinjaTrader 8\incoming\EduS_TradesContext.csv";
            AccountsToMonitor = "Sim101";

            MostrarPanelPosicion = false;   // OFF por defecto (en charts con HUD V6 no se activa)
            PanelTopmost         = true;
            PanelFontSize        = 15;
            PanelOpacity         = 1.0;
        }

        // ===================================================
        // 3) STATE MACHINE
        // ===================================================

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Estrategia V2 de registro de operaciones integrada y sincronizada con el HUD V5.";
                Name        = "EduS_AccountTradeLogger_V2";
                Calculate   = Calculate.OnEachTick;
                EntriesPerDirection = 10;
                EntryHandling       = EntryHandling.AllEntries;
                IsUnmanaged         = false;
                IsOverlay           = true;

                BarsRequiredToTrade                      = 0;
                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.DataLoaded)
            {
                _terminado   = false;
                _panelPedido = false;
                logFilePath  = CsvPath;
                EnsureHeader();

                monitoredAccounts.Clear();
                if (AccountsToMonitor != null)
                {
                    string[] split = AccountsToMonitor.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                    List<string> cleanList = new List<string>();
                    foreach (string s in split)
                        cleanList.Add(s.Trim().ToUpperInvariant());

                    foreach (Account acc in Account.All)
                    {
                        if (cleanList.Contains(acc.Name.ToUpperInvariant()))
                        {
                            monitoredAccounts.Add(acc);
                            Print("LOGGER V2: Monitoreando cuenta " + acc.Name);
                        }
                    }
                }

                foreach (Account acc in monitoredAccounts)
                {
                    acc.ExecutionUpdate += OnAccountExecutionUpdate;
                }
            }
            else if (State == State.Terminated)
            {
                _terminado = true;
                foreach (Account acc in monitoredAccounts)
                {
                    acc.ExecutionUpdate -= OnAccountExecutionUpdate;
                }
                CerrarPanel();
            }
        }

        protected override void OnBarUpdate()
        {
            // El logging es 100% por eventos de cuenta (no depende de OnBarUpdate).
            // Acá solo creamos el panel flotante en tiempo real si está habilitado.
            if (MostrarPanelPosicion && State == State.Realtime
                && ChartControl != null && _panelWindow == null && !_panelPedido)
            {
                CrearPanel();
            }
        }

        // ===================================================
        // EVENTO DE EJECUCIONES
        // ===================================================

        private void OnAccountExecutionUpdate(object sender, ExecutionEventArgs e)
        {
            var exec = e.Execution;
            if (exec == null || exec.Order == null)
                return;

            TriggerCustomEvent(o => HandleExecution(exec), null);
        }

        private void HandleExecution(Execution exec)
        {
            try
            {
                if (exec == null || exec.Order == null)
                    return;

                // Cada instancia de la estrategia solo procesa el instrumento de su propio chart
                if (exec.Instrument.FullName != Instrument.FullName)
                    return;

                string accountName = exec.Order.Account.Name;
                string instrName   = exec.Instrument.FullName;
                string key         = accountName + "|" + instrName;

                int prevQty = 0;
                if (!netPositionByKey.TryGetValue(key, out prevQty))
                    prevQty = 0;

                int signedChange = 0;
                if (exec.Order.OrderAction == OrderAction.Buy ||
                    exec.Order.OrderAction == OrderAction.BuyToCover)
                    signedChange = exec.Quantity;
                else if (exec.Order.OrderAction == OrderAction.Sell ||
                         exec.Order.OrderAction == OrderAction.SellShort)
                    signedChange = -exec.Quantity;

                int newQty = prevQty + signedChange;
                netPositionByKey[key] = newQty;

                string fillType = "Other";
                int absPrev = Math.Abs(prevQty);
                int absNew  = Math.Abs(newQty);

                if (absNew > absPrev && newQty != 0) fillType = "Entry";
                else if (absNew < absPrev)           fillType = "Exit";

                int entryId = 0;
                activeEntryIdByKey.TryGetValue(key, out entryId);

                if (prevQty == 0 && newQty != 0)
                {
                    currentEntryId++;
                    entryId = currentEntryId;
                    activeEntryIdByKey[key] = entryId;
                }
                else if (newQty == 0)
                {
                    activeEntryIdByKey[key] = 0;
                }

                string instrumentPrefix = exec.Instrument.MasterInstrument.Name;
                string uniqueEntryId = $"{instrumentPrefix}_{entryId}";
                fillCounter++;
                string fillId = $"F_{instrumentPrefix}_{entryId}_{fillCounter}";

                int      qty      = exec.Quantity;
                double   price    = exec.Price;
                DateTime execTime = exec.Time;
                int      legSeq   = (fillType == "Exit") ? 1 : 0;

                Print(string.Format(
                    "LOGGER V2: Ejecución detectada - Cuenta={0}, Instrumento={1}, Accion={2}, PrevQty={3}, NewQty={4}, Tipo={5}",
                    accountName, instrName, exec.Order.OrderAction, prevQty, newQty, fillType));

                LogFillWithContext(uniqueEntryId, fillId, fillType, execTime, qty, price, legSeq, exec);
            }
            catch (Exception ex)
            {
                Print("ERROR en HandleExecution V2: " + ex.Message);
            }
        }

        // ===================================================
        // LogFillWithContext
        // ===================================================

        private void LogFillWithContext(string entryId, string fillId, string fillType, DateTime time,
                                        int qty, double price, int legSeq, Execution exec)
        {
            try
            {
                string direction = (exec.Order.OrderAction == OrderAction.Buy ||
                                    exec.Order.OrderAction == OrderAction.BuyToCover) ? "Long" : "Short";

                var hud = BuscarHudV5EnChart();
                if (hud == null)
                {
                    Print("[WARN] LOGGER V2: No se encontró la instancia activa de 'EduS_MasterPanel_HUD_V5' en el gráfico. Se omite el logueo de contexto.");
                    return;
                }

                // Extracción de indicadores del HUD V5 (Sincronización Total)
                double sma20Val = hud.v_sma20Val;
                double sma80Val = hud.v_sma80Val;
                double lr89Val  = hud.v_lrVal;
                double precioVWAP = hud.v_vwapVal;
                double mbKeltner = hud._keltnerVolBM;
                double keltWidth = hud._keltnerVolWidth;
                double atrValue  = hud._atrVal;

                double pendienteSMA20     = hud.Sma20Slope;
                double pendienteSMA80     = hud.Sma80Slope;
                double pendienteLinReg89 = hud.LinRegSlope;
                string vwapDir            = hud.v_vwapUp ? "UP" : "DOWN";

                double distSMA20 = price - sma20Val;
                double distSMA80 = price - sma80Val;

                double pocWin   = double.IsNaN(hud._pocVal) ? 0.0 : hud._pocVal;
                double apUpper  = double.IsNaN(hud._antiPocUpVal) ? 0.0 : hud._antiPocUpVal;
                double apLower  = double.IsNaN(hud._antiPocDownVal) ? 0.0 : hud._antiPocDownVal;
                double lvnVal   = double.IsNaN(hud._lvnVal) ? 0.0 : hud._lvnVal;

                // --- CSV ---
                string line = string.Join(";", new string[]
                {
                    entryId,
                    fillId,
                    exec.Order.Account.Name,
                    fillType,
                    time.ToString("yyyy-MM-dd HH:mm:ss"),
                    exec.Instrument.FullName.Replace(";", " "),
                    TradeName,
                    direction,
                    qty.ToString(),
                    price.ToString("0.00"),
                    legSeq.ToString(),

                    pendienteSMA20.ToString("0.0000"),
                    pendienteSMA80.ToString("0.0000"),
                    pendienteLinReg89.ToString("0.0000"),
                    vwapDir,
                    lr89Val.ToString("0.00"),

                    sma20Val.ToString("0.00"),
                    sma80Val.ToString("0.00"),
                    distSMA20.ToString("0.00"),
                    distSMA80.ToString("0.00"),

                    double.IsNaN(precioVWAP) ? "" : precioVWAP.ToString("0.00"),
                    mbKeltner.ToString("0.00"),
                    keltWidth.ToString("0.00"),
                    atrValue.ToString("0.00"),

                    pocWin.ToString("0.00"),
                    apUpper.ToString("0.00"),
                    apLower.ToString("0.00"),
                    lvnVal.ToString("0.00")
                });

                SafeAppendToFile(logFilePath, line, false);
            }
            catch (Exception ex)
            {
                Print("ERROR logeando fill en V2: " + ex.Message);
            }
        }

        // ===================================================
        // ESCRIBIR ARCHIVO SEGURO (Resiliente a Excel)
        // ===================================================

        private void EnsureHeader()
        {
            try
            {
                bool writeHeader = false;
                if (!File.Exists(logFilePath))
                    writeHeader = true;
                else
                {
                    var fi = new FileInfo(logFilePath);
                    if (fi.Length == 0) writeHeader = true;
                }

                if (writeHeader)
                {
                    string header =
                        "Entry_ID;Fill_ID;Cuenta;Fill_Type;Fecha_Hora;Instrumento;Trade_Name;Direccion;Qty;Precio_Ejecucion;Leg_Seq;" +
                        "Pendiente_SMA20;Pendiente_SMA80;Pendiente_LinReg89;VWAP_Dir;Valor_LR89;" +
                        "Precio_SMA20;Precio_SMA80;Distancia_SMA20;Distancia_SMA80;" +
                        "Precio_VWAP;MB_Keltner;Keltner_Width;ATR_Valor;" +
                        "Window_POC;Window_AP_High;Window_AP_Low;Window_LVNs";

                    SafeAppendToFile(logFilePath, header, true);
                }
            }
            catch (Exception ex)
            {
                Print("ERROR creando cabecera CSV en V2: " + ex.Message);
            }
        }

        private void SafeAppendToFile(string path, string content, bool isHeader = false)
        {
            try
            {
                File.AppendAllText(path, content + Environment.NewLine, Encoding.UTF8);
                if (isHeader)
                    Print("LOG DEBUG V2: Cabecera CSV escrita en " + path);
                else
                    Print("LOG DEBUG V2: Escribiendo CSV -> " + content);
            }
            catch (IOException ioEx)
            {
                Print(string.Format("[CRITICAL ERROR V2] No se pudo escribir en el archivo CSV principal ({0}) porque está bloqueado o en uso (¿Abierto en Excel?). Detalle: {1}", Path.GetFileName(path), ioEx.Message));
                
                try
                {
                    string dir = Path.GetDirectoryName(path);
                    string fileName = Path.GetFileNameWithoutExtension(path);
                    string ext = Path.GetExtension(path);
                    string backupPath = Path.Combine(dir, fileName + "_BACKUP" + ext);

                    if (!isHeader && (!File.Exists(backupPath) || new FileInfo(backupPath).Length == 0))
                    {
                        string header =
                            "Entry_ID;Fill_ID;Cuenta;Fill_Type;Fecha_Hora;Instrumento;Trade_Name;Direccion;Qty;Precio_Ejecucion;Leg_Seq;" +
                            "Pendiente_SMA20;Pendiente_SMA80;Pendiente_LinReg89;VWAP_Dir;Valor_LR89;" +
                            "Precio_SMA20;Precio_SMA80;Distancia_SMA20;Distancia_SMA80;" +
                            "Precio_VWAP;MB_Keltner;Keltner_Width;ATR_Valor;" +
                            "Window_POC;Window_AP_High;Window_AP_Low;Window_LVNs";
                        File.AppendAllText(backupPath, header + Environment.NewLine, Encoding.UTF8);
                    }

                    File.AppendAllText(backupPath, content + Environment.NewLine, Encoding.UTF8);
                    Print("[INFO V2] ¡OPERACIÓN SALVADA! Registro guardado en archivo de respaldo: " + backupPath);
                }
                catch (Exception exBackup)
                {
                    Print("[CRITICAL V2] Falló la escritura en respaldo. Datos perdidos. Error: " + exBackup.Message);
                }
            }
            catch (Exception ex)
            {
                Print("ERROR general en SafeAppendToFile V2: " + ex.Message);
            }
        }

        // ===================================================
        // BuscarHudV5EnChart: Localizar HUD V5 activo
        // ===================================================

        private EduS_MasterPanel_HUD_V5 BuscarHudV5EnChart()
        {
            if (ChartControl == null) return null;

            foreach (var ind in ChartControl.Indicators)
            {
                if (ind is EduS_MasterPanel_HUD_V5)
                {
                    var casted = ind as EduS_MasterPanel_HUD_V5;
                    if (casted.State == State.Active || casted.State == State.Realtime)
                    {
                        return casted;
                    }
                }
            }
            return null;
        }

        // ===================================================
        // PANEL FLOTANTE DE POSICIÓN (estilo EduS)
        // ===================================================

        private struct PosInfo
        {
            public bool   TienePos, EsLong, TieneStop, TieneTarget, RiesgoIlimitado, GananciaAsegurada;
            public int    Cantidad;
            public double PrecioEntrada, PLdolar, PLpuntos, StopRisk, TargetProfit, RR;
            public string Etiqueta;
        }

        // Réplica del BuildPosSnap() del HUD V6: lee la posición de la Account de la
        // estrategia y sus órdenes de stop/limit activas para calcular riesgo y target en $.
        private PosInfo BuildPosInfo()
        {
            var info = new PosInfo();
            Account acct = Account;
            if (acct == null || Instrument == null) return info;

            string instrName = Instrument.MasterInstrument.Name;
            Position pos = null;
            try { pos = acct.Positions.FirstOrDefault(p => p.Instrument.MasterInstrument.Name == instrName && p.MarketPosition != MarketPosition.Flat); }
            catch { return info; }
            if (pos == null) return info;

            info.TienePos      = true;
            info.EsLong        = pos.MarketPosition == MarketPosition.Long;
            info.Cantidad      = Math.Abs(pos.Quantity);
            info.PrecioEntrada = pos.AveragePrice;

            double pv = pos.Instrument.MasterInstrument.PointValue;
            try
            {
                info.PLdolar  = pos.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
                info.PLpuntos = pv > 0 ? info.PLdolar / Math.Abs(pos.Quantity) / pv : 0;
            }
            catch { }

            try
            {
                var orders = acct.Orders.Where(o => o.Instrument.MasterInstrument.Name == instrName
                    && (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted)
                    && (o.OrderType == OrderType.StopMarket || o.OrderType == OrderType.StopLimit || o.OrderType == OrderType.Limit)).ToList();

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
                    info.TieneStop = true;
                    double avgStop = totalStopP / totalStopQ;
                    info.StopRisk = info.EsLong ? Math.Abs(pos.AveragePrice - avgStop) * totalStopQ * pv
                                                : Math.Abs(avgStop - pos.AveragePrice) * totalStopQ * pv;
                    info.GananciaAsegurada = info.EsLong ? (avgStop > pos.AveragePrice) : (avgStop < pos.AveragePrice);
                    info.RiesgoIlimitado   = totalStopQ < Math.Abs(pos.Quantity);
                }
                if (totalTgtQ > 0)
                {
                    info.TieneTarget = true;
                    double avgTgt = totalTgtP / totalTgtQ;
                    info.TargetProfit = info.EsLong ? Math.Abs(avgTgt - pos.AveragePrice) * totalTgtQ * pv
                                                    : Math.Abs(pos.AveragePrice - avgTgt) * totalTgtQ * pv;
                }
                if (info.StopRisk > 0.01 && info.TargetProfit > 0.01) info.RR = info.TargetProfit / info.StopRisk;
                info.Etiqueta = hayATM ? "ATM" : (orders.Count > 0 ? "Manual" : "");
            }
            catch { }

            return info;
        }

        private System.Windows.Controls.TextBlock PanelTb(string text, double size, System.Windows.Media.Brush color, bool bold)
        {
            return new System.Windows.Controls.TextBlock
            {
                Text       = text,
                FontSize   = size,
                Foreground = color,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                Margin     = new Thickness(0, 1, 0, 1)
            };
        }

        private void CrearPanel()
        {
            if (ChartControl == null) return;
            _panelPedido = true;

            ChartControl.Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (_terminado || _panelWindow != null) return;

                    var bc = new System.Windows.Media.BrushConverter();
                    _bNavy  = (System.Windows.Media.Brush)bc.ConvertFrom("#0D1F26");
                    _bGold  = (System.Windows.Media.Brush)bc.ConvertFrom("#B38665");
                    _bGoldL = (System.Windows.Media.Brush)bc.ConvertFrom("#D4A982");
                    _bWhite = System.Windows.Media.Brushes.White;
                    _bGreen = (System.Windows.Media.Brush)bc.ConvertFrom("#3DC8A0");
                    _bRed   = (System.Windows.Media.Brush)bc.ConvertFrom("#E06E60");
                    _bMuted = (System.Windows.Media.Brush)bc.ConvertFrom("#96A2AC");

                    double fs = PanelFontSize;

                    _tbTitulo = PanelTb("POSICIÓN ACTIVA", fs + 2, _bGold, true);
                    _tbInstr  = PanelTb("", fs - 1, _bMuted, false);
                    _tbDirQty = PanelTb("", fs + 1, _bWhite, true);
                    _tbPL     = PanelTb("", fs, _bWhite, false);
                    _tbRiesgo = PanelTb("", fs, _bGoldL, false);
                    _tbTarget = PanelTb("", fs, _bWhite, false);
                    _tbRR     = PanelTb("", fs, _bWhite, false);
                    _tbVacio  = PanelTb("Sin posición", fs, _bMuted, false);

                    _datosPanel = new System.Windows.Controls.StackPanel();
                    _datosPanel.Children.Add(_tbDirQty);
                    _datosPanel.Children.Add(_tbPL);
                    _datosPanel.Children.Add(_tbRiesgo);
                    _datosPanel.Children.Add(_tbTarget);
                    _datosPanel.Children.Add(_tbRR);

                    var footer = PanelTb("edustrader.com", fs - 5, _bGold, true);
                    footer.Margin = new Thickness(0, 6, 0, 0);

                    var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(14, 12, 14, 10) };
                    stack.Children.Add(_tbTitulo);
                    stack.Children.Add(_tbInstr);
                    stack.Children.Add(_datosPanel);
                    stack.Children.Add(_tbVacio);
                    stack.Children.Add(footer);

                    var vb = new System.Windows.Controls.Viewbox
                    {
                        Stretch             = System.Windows.Media.Stretch.Uniform,
                        StretchDirection    = System.Windows.Controls.StretchDirection.Both,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment   = VerticalAlignment.Top,
                        Child               = stack
                    };

                    var border = new System.Windows.Controls.Border
                    {
                        Background      = _bNavy,
                        BorderBrush     = _bGold,
                        BorderThickness = new Thickness(2),
                        Child           = vb
                    };

                    double l, t, w, h;
                    CargarGeometria(out l, out t, out w, out h);

                    _panelWindow = new System.Windows.Window
                    {
                        Title         = "EduS · Posición",
                        Content       = border,
                        Background    = _bNavy,
                        Width         = w,
                        Height        = h,
                        Left          = l,
                        Top           = t,
                        MinWidth      = 170,
                        MinHeight     = 110,
                        WindowStyle   = System.Windows.WindowStyle.SingleBorderWindow,
                        ResizeMode    = System.Windows.ResizeMode.CanResize,
                        ShowInTaskbar = false,
                        Topmost       = PanelTopmost,
                        Opacity       = PanelOpacity
                    };

                    _panelWindow.LocationChanged += (s, e) => GuardarGeometria();
                    _panelWindow.SizeChanged     += (s, e) => GuardarGeometria();
                    _panelWindow.Closing         += (s, e) => GuardarGeometria();

                    _panelWindow.Show();

                    _panelTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                    _panelTimer.Tick += (s, e) => RefreshPanel();
                    _panelTimer.Start();

                    RefreshPanel();
                }
                catch (Exception ex) { Print("ERROR creando panel V2: " + ex.Message); _panelPedido = false; }
            });
        }

        private void RefreshPanel()
        {
            try
            {
                if (_panelWindow == null) return;

                string acctName = Account != null ? Account.Name : "—";
                _tbTitulo.Text  = "POSICIÓN ACTIVA · " + acctName;

                var info = BuildPosInfo();
                if (!info.TienePos)
                {
                    _datosPanel.Visibility = Visibility.Collapsed;
                    _tbInstr.Visibility    = Visibility.Collapsed;
                    _tbVacio.Visibility    = Visibility.Visible;
                    _tbVacio.Text = "Sin posición · " + (Instrument != null ? Instrument.MasterInstrument.Name : "");
                    return;
                }

                _tbVacio.Visibility    = Visibility.Collapsed;
                _tbInstr.Visibility    = Visibility.Visible;
                _datosPanel.Visibility = Visibility.Visible;

                string etiq = string.IsNullOrEmpty(info.Etiqueta) ? "" : "   [" + info.Etiqueta + "]";
                _tbInstr.Text = (Instrument != null ? Instrument.FullName : "") + etiq;

                _tbDirQty.Text       = (info.EsLong ? "LONG" : "SHORT") + "  " + info.Cantidad + " @ " + info.PrecioEntrada.ToString("0.00");
                _tbDirQty.Foreground = info.EsLong ? _bGreen : _bRed;

                _tbPL.Text       = "P&L     " + (info.PLdolar >= 0 ? "+" : "-") + "$" + Math.Abs(info.PLdolar).ToString("N0")
                                 + "   (" + (info.PLpuntos >= 0 ? "+" : "") + info.PLpuntos.ToString("0.0") + " pts)";
                _tbPL.Foreground = info.PLdolar >= 0 ? _bGreen : _bRed;

                if (info.TieneStop)
                {
                    string extra = info.GananciaAsegurada ? "   ⛊ BE" : (info.RiesgoIlimitado ? "   ⚠ parcial" : "");
                    _tbRiesgo.Text       = "Riesgo  $" + info.StopRisk.ToString("N0") + extra;
                    _tbRiesgo.Foreground = info.GananciaAsegurada ? _bGreen : _bGoldL;
                }
                else
                {
                    _tbRiesgo.Text       = "Riesgo  — (sin stop)";
                    _tbRiesgo.Foreground = _bRed;
                }

                _tbTarget.Text = info.TieneTarget ? "Target  $" + info.TargetProfit.ToString("N0") : "Target  —";
                _tbRR.Text     = info.RR > 0 ? "R:R     " + info.RR.ToString("0.0") : "R:R     —";
            }
            catch { }
        }

        private void CerrarPanel()
        {
            try
            {
                var w = _panelWindow;
                var t = _panelTimer;
                _panelWindow = null;
                _panelTimer  = null;
                _panelPedido = false;
                if (w != null)
                    w.Dispatcher.InvokeAsync(() => { try { if (t != null) t.Stop(); w.Close(); } catch { } });
            }
            catch { }
        }

        private string PosFilePath()
        {
            try
            {
                string dir   = Path.GetDirectoryName(CsvPath);
                string instr = Instrument != null ? Instrument.MasterInstrument.Name : "GEN";
                return Path.Combine(dir, "EduS_PanelPos_" + instr + ".txt");
            }
            catch { return null; }
        }

        private void CargarGeometria(out double l, out double t, out double w, out double h)
        {
            l = 140; t = 140; w = 300; h = 210;
            try
            {
                string p = PosFilePath();
                if (p != null && File.Exists(p))
                {
                    var parts = File.ReadAllText(p).Split(';');
                    if (parts.Length >= 4)
                    {
                        var ci = System.Globalization.CultureInfo.InvariantCulture;
                        var ns = System.Globalization.NumberStyles.Any;
                        double.TryParse(parts[0], ns, ci, out l);
                        double.TryParse(parts[1], ns, ci, out t);
                        double.TryParse(parts[2], ns, ci, out w);
                        double.TryParse(parts[3], ns, ci, out h);
                    }
                }
            }
            catch { }
            if (w < 170) w = 170;
            if (h < 110) h = 110;
        }

        private void GuardarGeometria()
        {
            try
            {
                if (_panelWindow == null) return;
                if ((DateTime.Now - _lastGeoSave).TotalMilliseconds < 600) return;
                _lastGeoSave = DateTime.Now;
                string p = PosFilePath();
                if (p == null) return;
                File.WriteAllText(p, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0};{1:0};{2:0};{3:0}", _panelWindow.Left, _panelWindow.Top, _panelWindow.Width, _panelWindow.Height));
            }
            catch { }
        }
    }
}
