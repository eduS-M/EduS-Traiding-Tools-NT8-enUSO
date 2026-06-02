#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators.EduS_Trader;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    /// <summary>
    /// EduS AutoTrader V1 — Rev 8
    /// Fix #1: Nombres únicos por trade para evitar confusión de referencias entre señales.
    /// Fix #2: Cancelar T1/T2 cuando el Stop se ejecuta (y viceversa).
    /// Fix #3: ChangeOrder correcto al mover stop a BE cuando T1 toca.
    /// Fix #4: Cooldown post-cierre externo.
    /// Fix #5 (Rev6): MoverStopABreakeven usa Cancel+Resubmit en lugar de ChangeOrder.
    ///          ChangeOrder en modo Unmanaged NO garantiza reducir la cantidad, causando
    ///          que el stop original de N contratos dispare y cree posición corta fantasma.
    /// Fix #6 (Rev6): Guardia de seguridad en OnBarUpdate: si la estrategia cree estar
    ///          flat pero hay posición abierta, cierra inmediatamente.
    /// Fix #7 (Rev7): Orden de entrada cambiada de Market → Limit.
    ///          El precio Limit = EntradaHUD - (X * ATR) para Long  (entra en soporte = precio baja hasta zona).
    ///                           EntradaHUD + (X * ATR) para Short (entra en resistencia = precio sube hasta zona).
    ///          Si el HUD invalida la señal o vence el timeout, la Limit se cancela.
    /// Fix #8 (Rev8): Corrección de la dirección del precio Limit (era al revés).
    ///          Nuevo parámetro LimitCancelCooldownBarras: barras mínimas antes de permitir
    ///          la cancelación por señal HUD inválida, dando tiempo al precio de llegar a la zona.
    /// Fix #9 (Rev9): Corrección de spam en log del cooldown (impresión 1 vez por barra).
    ///          Agregado nombre del instrumento en logs para evitar confusión en multi-chart.
    /// </summary>
    public class EduSAutoTraderV1 : Strategy
    {
        // ─── HUD ─────────────────────────────────────────────────────────────
        private EduS_MasterPanel_HUD_V5 _hud;

        // ─── ÓRDENES ─────────────────────────────────────────────────────────
        private Order _entryOrder = null;
        private Order _stopOrder  = null;
        private Order _t1Order    = null;
        private Order _t2Order    = null;

        // ─── NOMBRES ÚNICOS POR TRADE (Fix #1) ───────────────────────────────
        private int    _tradeId    = 0;
        private string _nameEntry  = "";
        private string _nameStop   = "";   // puede cambiar al mover BE (Fix #5)
        private string _nameT1     = "";
        private string _nameT2     = "";

        // ─── ESTADO ──────────────────────────────────────────────────────────
        private bool   _esperandoFillEntrada = false;
        private bool   _entryFilled          = false;
        private bool   _enOperacion          = false;
        private bool   _t1Ejecutado          = false;
        private bool   _stopMovidoABE        = false;
        private bool   _prevHudSignal        = false;

        // Fix #4: cooldown post-cierre externo
        private int    _barCierreExterno     = -1;
        private const int COOLDOWN_BARS      = 3;

        // Fix #6: guardia de seguridad contra posición fantasma
        private bool   _enviandoSafeClose    = false;
        private bool   _esperandoSincronizacionFlat = false;
        private int    _ticksEsperandoFlat   = 0;

        private double _precioEntrada = double.NaN;
        private double _stopInicial   = double.NaN;
        private double _targetT1      = double.NaN;
        private double _targetT2      = double.NaN;
        private bool   _esAlcista     = false;
        private int    _contratosT1   = 0;
        private int    _contratosT2   = 0;
        private int    _totalContratos= 0;

        // Fix #7: Limit entry — seguimiento de barra de envío para timeout
        private int    _barEnvioEntrada = -1;

        // ─── PROPIEDADES UI ──────────────────────────────────────────────────

        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "Contratos por Señal", Order = 1, GroupName = "01. Gestión")]
        public int ContratosPorSenal { get; set; }

        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "BE+ Ticks al llegar T1", Order = 2, GroupName = "01. Gestión")]
        public int BEPlusTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Solo en Realtime", Order = 3, GroupName = "01. Gestión")]
        public bool SoloRealtime { get; set; }

        // Fix #7/#8: Limit entry
        [NinjaScriptProperty]
        [Range(0.0, 2.0)]
        [Display(Name = "Limit Slippage (x ATR)", Order = 4, GroupName = "01. Gestión",
            Description = "Margen de la orden Limit respecto al precio del HUD, en múltiplos de ATR. " +
                          "Long: precio - X*ATR (compra cuando baja hasta soporte). " +
                          "Short: precio + X*ATR (vende cuando sube hasta resistencia). Default 0.2")]
        public double LimitSlippageATRs { get; set; }

        [NinjaScriptProperty]
        [Range(1, 50)]
        [Display(Name = "Limit Timeout (barras)", Order = 5, GroupName = "01. Gestión",
            Description = "Máximo de barras a esperar fill de la Limit antes de cancelarla automáticamente. Default 5")]
        public int LimitTimeoutBarras { get; set; }

        // Fix #8: Cooldown mínimo antes de cancelar la Limit por señal HUD inválida
        [NinjaScriptProperty]
        [Range(0, 20)]
        [Display(Name = "Limit Cancel Cooldown (barras)", Order = 6, GroupName = "01. Gestión",
            Description = "Barras mínimas que debe estar activa la Limit antes de poder ser cancelada por " +
                          "señal HUD inválida. Permite que el precio llegue a la zona aunque el HUD haya " +
                          "actualizado el indicador. El timeout absoluto (LimitTimeoutBarras) siempre aplica. Default 3")]
        public int LimitCancelCooldownBarras { get; set; }

        // ─── CICLO DE VIDA ───────────────────────────────────────────────────

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                               = "AutoTrader que ejecuta señales del EduSMasterPanelHUDV5.";
                Name                                      = "EduS_AutoTrader_V1";
                Calculate                                 = Calculate.OnEachTick;
                IsUnmanaged                               = true;
                IsExitOnSessionCloseStrategy              = true;
                ExitOnSessionCloseSeconds                 = 30;
                BarsRequiredToTrade                       = 1;
                IsInstantiatedOnEachOptimizationIteration = false;

                ContratosPorSenal        = 2;
                BEPlusTicks              = 4;
                SoloRealtime             = true;
                LimitSlippageATRs        = 0.2;  // Fix #7/#8: margen Limit = 0.2 * ATR
                LimitTimeoutBarras       = 5;    // Fix #7: cancelar si pasan 5 barras sin fill (límite duro)
                LimitCancelCooldownBarras = 3;   // Fix #8: esperar 3 barras mínimo antes de cancelar por HUD inválido
            }
            else if (State == State.DataLoaded)
            {
                _hud = null;
            }
            else if (State == State.Realtime)
            {
                // Fix #4: Sincronizar estado inicial al arrancar/reiniciar para no perder señales activas
                _hud = BuscarHud();
                if (_hud != null)
                {
                    _prevHudSignal = _hud._entradaValida;
                }
            }
            else if (State == State.Terminated)
            {
                CerrarTodo();
            }
        }

        // ─── BUSCADOR HUD ────────────────────────────────────────────────────

        private EduS_MasterPanel_HUD_V5 BuscarHud()
        {
            if (ChartControl == null) return null;
            foreach (var ind in ChartControl.Indicators)
                if (ind is EduS_MasterPanel_HUD_V5 h &&
                   (h.State == State.Active || h.State == State.Realtime))
                    return h;
            return null;
        }

        // ─── ON BAR UPDATE ───────────────────────────────────────────────────

        protected override void OnBarUpdate()
        {
            if (BarsInProgress != 0) return;
            if (SoloRealtime && State != State.Realtime) return;

            if (_hud == null)
            {
                _hud = BuscarHud();
                if (_hud == null) return;
            }

            // ── Esperando sincronización de broker a Flat ──
            if (_esperandoSincronizacionFlat)
            {
                _ticksEsperandoFlat++;
                // Darle ~50 ticks o esperar que NT confirme Flat
                if (Position.MarketPosition == MarketPosition.Flat || _ticksEsperandoFlat > 50)
                {
                    ResetearEstado();
                    _barCierreExterno = CurrentBar;
                }
                return; // Bloquea Fix #6 y nuevas señales hasta sincronizar
            }

            // ── Fix #6: Guardia de seguridad contra posición fantasma ────────
            // Si la estrategia cree estar flat/idle pero hay posición real abierta,
            // es una posición fantasma creada por una orden residual. Cerrar inmediatamente.
            if (!_enOperacion && !_esperandoFillEntrada && !_enviandoSafeClose
                && Position.MarketPosition != MarketPosition.Flat)
            {
                int qtyFantasma = Math.Abs(Position.Quantity);
                OrderAction flatAction = Position.MarketPosition == MarketPosition.Long
                    ? OrderAction.Sell
                    : OrderAction.BuyToCover;

                Print(string.Format(
                    "[AutoTrader-{0}] FIX#6 GUARDIA: Posición fantasma {1} {2} ctr detectada. Cerrando.",
                    Instrument.MasterInstrument.Name, Position.MarketPosition, qtyFantasma));

                _enviandoSafeClose = true;
                SubmitOrderUnmanaged(0, flatAction, OrderType.Market, qtyFantasma, 0, 0, "", "SafeClose");
                _barCierreExterno = CurrentBar;
                return;
            }
            bool hudSignal    = _hud._entradaValida;
            bool esNuevaSenal = hudSignal && !_prevHudSignal;
            _prevHudSignal    = hudSignal;

            // ── Fix #7/#8: Vigilancia de Limit pendiente ─────────────────────
            // Si hay una Limit de entrada esperando fill, revisar si debe cancelarse.
            // Fix #8: La cancelación por señal HUD inválida respeta un cooldown mínimo
            //         (LimitCancelCooldownBarras) para dar tiempo al precio de llegar a la zona.
            //         El timeout absoluto (LimitTimeoutBarras) siempre cancela sin importar el cooldown.
            if (_esperandoFillEntrada && !_entryFilled && _entryOrder != null)
            {
                int barrasActivas  = CurrentBar - _barEnvioEntrada;
                bool timeout       = barrasActivas >= LimitTimeoutBarras;             // límite duro

                // Fix #2: NO cancelar la Limit prematuramente por fluctuaciones del HUD. 
                if (timeout)
                {
                    string motivo = string.Format("timeout {0} barras sin fill", LimitTimeoutBarras);
                    Print(string.Format("[AutoTrader-{0}#{1}] Cancelando Limit entrada — {2}.", Instrument.MasterInstrument.Name, _tradeId, motivo));
                    CancelarOrden(ref _entryOrder);
                    ResetearEstado();
                    return;
                }
                // Limit aún dentro del período de vida — esperar fill
                return;
            }

            // Detectar cierre externo de posición
            if (_enOperacion && Position.MarketPosition == MarketPosition.Flat && !_esperandoFillEntrada)
            {
                Print(string.Format("[AutoTrader-{0}#{1}] Cierre externo detectado. Cooldown {2} barras.", Instrument.MasterInstrument.Name, _tradeId, COOLDOWN_BARS));
                CancelarOrdenesPendientes(cancelarStop: true, cancelarT1: true, cancelarT2: true);
                _esperandoSincronizacionFlat = true;
                _ticksEsperandoFlat = 0;
                return;
            }

            // Fix #4: guardia de cooldown post-cierre externo
            if (esNuevaSenal && !_enOperacion && !_esperandoFillEntrada)
            {
                if (_barCierreExterno >= 0 && (CurrentBar - _barCierreExterno) < COOLDOWN_BARS)
                {
                    if (IsFirstTickOfBar)
                    {
                        Print(string.Format("[AutoTrader-{0}] Señal en espera — cooldown post-cierre ({1}/{2} barras).",
                            Instrument.MasterInstrument.Name, CurrentBar - _barCierreExterno, COOLDOWN_BARS));
                    }
                    _prevHudSignal = false; // Fix #11: No consumir la señal; reintentar en el próximo tick
                    return;
                }
                EnviarEntrada();
            }
        }

        // ─── PASO 1: Enviar entrada ───────────────────────────────────────────

        private void EnviarEntrada()
        {
            // Redondear todos los precios calculados al TickSize del instrumento.
            // Si tienen más decimales que los permitidos, el broker/sistema rechazará la orden.
            double entrada = Instrument.MasterInstrument.RoundToTickSize(_hud._eEntrada);
            double stop    = Instrument.MasterInstrument.RoundToTickSize(_hud._eStop);
            double t1      = Instrument.MasterInstrument.RoundToTickSize(_hud._eT1);
            double t2      = Instrument.MasterInstrument.RoundToTickSize(_hud._eT2);
            bool   alcista = _hud._esAlcista;

            if (double.IsNaN(entrada) || double.IsNaN(stop) || double.IsNaN(t1) || double.IsNaN(t2)) return;
            if (entrada <= 0 || stop <= 0 || t1 <= 0 || t2 <= 0) return;

            int total = ContratosPorSenal;
            int qt1, qt2;
            if (total == 1)       { qt1 = 1; qt2 = 0; }
            else if (total == 2)  { qt1 = 1; qt2 = 1; }
            else                  { qt1 = (total * 2) / 3; if (qt1 < 1) qt1 = 1; qt2 = total - qt1; }

            _tradeId++;
            _nameEntry = "HUD_En_" + _tradeId;
            _nameStop  = "HUD_St_" + _tradeId;
            _nameT1    = "HUD_T1_" + _tradeId;
            _nameT2    = "HUD_T2_" + _tradeId;

            _precioEntrada        = entrada;
            _stopInicial          = stop;
            _targetT1             = t1;
            _targetT2             = t2;
            _esAlcista            = alcista;
            _contratosT1          = qt1;
            _contratosT2          = qt2;
            _totalContratos       = total;
            _t1Ejecutado          = false;
            _stopMovidoABE        = false;
            _entryFilled          = false;
            _esperandoFillEntrada = true;

            // Fix #7/#8: Calcular precio Limit con margen ATR según dirección
            // Fix #8: Dirección CORREGIDA:
            //   Long  → Limit = entrada - ATRmargen  (compra cuando el precio BAJA hasta el soporte)
            //   Short → Limit = entrada + ATRmargen  (vende cuando el precio SUBE hasta la resistencia)
            double atrVal      = _hud._atrVal;  // ATR del HUD (campo interno del indicador)
            double atrMargen   = (double.IsNaN(atrVal) || atrVal <= 0) ? 0 : LimitSlippageATRs * atrVal;
            double limitPrecio = alcista ? entrada - atrMargen : entrada + atrMargen;
            limitPrecio        = Instrument.MasterInstrument.RoundToTickSize(limitPrecio);

            string dirMargen = alcista
                ? string.Format("{0:F2} - {1:F2} = {2:F2}", entrada, atrMargen, limitPrecio)
                : string.Format("{0:F2} + {1:F2} = {2:F2}", entrada, atrMargen, limitPrecio);
            Print(string.Format("[AutoTrader-{0}#{1}] Señal {2} {3}x → Limit={4:F2} ({5} | {6:F4}xATR) Stop={7:F2} T1={8:F2} T2={9:F2} (qt1={10} qt2={11}) CooldownCancel={12}b",
                Instrument.MasterInstrument.Name, _tradeId, alcista ? "LONG" : "SHORT", total,
                limitPrecio, dirMargen, LimitSlippageATRs,
                stop, t1, t2, qt1, qt2, LimitCancelCooldownBarras));

            OrderAction action = alcista ? OrderAction.Buy : OrderAction.SellShort;
            _entryOrder = SubmitOrderUnmanaged(0, action, OrderType.Limit, total, limitPrecio, 0, "", _nameEntry);
            _barEnvioEntrada  = CurrentBar;
            _esperandoFillEntrada = true;
        }

        // ─── PASO 2: Enviar stop y targets tras fill ──────────────────────────

        private void EnviarStopYTargets()
        {
            OrderAction exitAction = _esAlcista ? OrderAction.Sell : OrderAction.BuyToCover;

            Print(string.Format("[AutoTrader-{0}#{1}] Fill confirmado. Stop={2:F2} T1={3:F2} T2={4:F2}",
                Instrument.MasterInstrument.Name, _tradeId, _stopInicial, _targetT1, _targetT2));

            // Stop cubre TODOS los contratos — se cancelará y reemplazará cuando T1 toque
            _stopOrder = SubmitOrderUnmanaged(0, exitAction, OrderType.StopMarket,
                _totalContratos, 0, _stopInicial, "", _nameStop);

            // Target T1
            _t1Order = SubmitOrderUnmanaged(0, exitAction, OrderType.Limit,
                _contratosT1, _targetT1, 0, "", _nameT1);

            // Target T2
            if (_contratosT2 > 0)
                _t2Order = SubmitOrderUnmanaged(0, exitAction, OrderType.Limit,
                    _contratosT2, _targetT2, 0, "", _nameT2);

            _esperandoFillEntrada = false;
            _enOperacion          = true;
        }

        // ─── PASO 3: Mover stop a BE+N cuando T1 se ejecuta ──────────────────
        // Fix #5 (Rev6): Usar Cancel + Resubmit en lugar de ChangeOrder.
        // ChangeOrder en modo Unmanaged NO reduce la cantidad de forma fiable.
        // Si el stop original (N contratos) permanece activo y dispara, la porción
        // que excede la posición restante crea una posición corta/larga fantasma.

        private void MoverStopABreakeven()
        {
            if (_stopMovidoABE) return;

            double nuevoStop = _esAlcista
                ? _precioEntrada + (BEPlusTicks * TickSize)
                : _precioEntrada - (BEPlusTicks * TickSize);

            int qtyRestante = _contratosT2 > 0 ? _contratosT2 : _contratosT1;

            Print(string.Format("[AutoTrader-{0}#{1}] T1 hit → Cancelando stop original ({2} ctr) y enviando nuevo Stop BE+{3}t = {4:F2} (qty={5})",
                Instrument.MasterInstrument.Name, _tradeId, _totalContratos, BEPlusTicks, nuevoStop, qtyRestante));

            // PASO A: Cancelar el stop ORIGINAL (que cubre _totalContratos)
            // Esto evita que una orden de N contratos dispare cuando sólo quedan M < N
            CancelarOrden(ref _stopOrder);

            // PASO B: Enviar UN NUEVO stop con la cantidad correcta y un nombre nuevo
            // Usamos nombre distinto para que OnOrderUpdate no confunda con el anterior
            _nameStop = "HUD_StBE_" + _tradeId;
            OrderAction exitAction = _esAlcista ? OrderAction.Sell : OrderAction.BuyToCover;
            _stopOrder = SubmitOrderUnmanaged(0, exitAction, OrderType.StopMarket,
                qtyRestante, 0, nuevoStop, "", _nameStop);

            _stopMovidoABE = true;
        }

        // ─── ON ORDER UPDATE ─────────────────────────────────────────────────

        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice,
            int quantity, int filled, double averageFillPrice,
            OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (order == null) return;

            // Fix #3: Ignorar errores internos asíncronos del broker (ej. Unable to change order por carrera de Fill/Cancel)
            if (error != ErrorCode.NoError)
            {
                Print(string.Format("[AutoTrader-{0}#{1}] Error asíncrono en orden '{2}': {3} ({4}). Ignorando para evitar crash.",
                    Instrument.MasterInstrument.Name, _tradeId, order.Name, error, nativeError));
                return;
            }

            // Actualizar referencias usando los nombres del trade activo
            // _nameStop puede haber cambiado a "HUD_StBE_N" tras mover BE (Fix #5)
            if      (order.Name == _nameEntry) _entryOrder = order;
            else if (order.Name == _nameStop)  _stopOrder  = order;
            else if (order.Name == _nameT1)    _t1Order    = order;
            else if (order.Name == _nameT2)    _t2Order    = order;
            else
            {
                // Detectar CloseAll externo o SafeClose (Fix #6)
                if (order.Name == "CloseAll" || order.Name == "SafeClose")
                {
                    if (orderState == OrderState.Filled)
                    {
                        Print(string.Format("[AutoTrader-{0}#{1}] {2} completado → esperando sincronización flat.", Instrument.MasterInstrument.Name, _tradeId, order.Name));
                        CancelarOrdenesPendientes(cancelarStop: true, cancelarT1: true, cancelarT2: true);
                        _esperandoSincronizacionFlat = true;
                        _ticksEsperandoFlat = 0;
                    }
                    else if (orderState == OrderState.Cancelled || orderState == OrderState.Rejected)
                    {
                        _enviandoSafeClose = false; // Permitir reintento
                    }
                }
                return;
            }

            // ── Limit entrada cancelada o rechazada antes del fill (Fix #7) ──
            if (order.Name == _nameEntry && !_entryFilled &&
                (orderState == OrderState.Cancelled || orderState == OrderState.Rejected))
            {
                Print(string.Format("[AutoTrader-{0}#{1}] Limit entrada {2} por broker/sistema. Reseteando.",
                    Instrument.MasterInstrument.Name, _tradeId, orderState));
                ResetearEstado();
                return;
            }

            // ── Entrada ejecutada → colocar stop y targets ────────────────
            if (order.Name == _nameEntry && orderState == OrderState.Filled)
            {
                if (!_entryFilled)
                {
                    _entryFilled   = true;
                    _precioEntrada = averageFillPrice;
                    EnviarStopYTargets();
                }
                return;
            }

            // ── T1 ejecutado → cancelar stop original y enviar nuevo BE ───
            if (order.Name == _nameT1 && orderState == OrderState.Filled)
            {
                if (!_t1Ejecutado)
                {
                    _t1Ejecutado = true;
                    MoverStopABreakeven(); // Fix #5: Cancel + Resubmit

                    if (_contratosT2 == 0)
                    {
                        CancelarOrdenesPendientes(cancelarStop: true);
                        _esperandoSincronizacionFlat = true;
                        _ticksEsperandoFlat = 0;
                    }
                }
                return;
            }

            // ── Stop ejecutado → cancelar T1 y T2 ────────────────────────
            if (order.Name == _nameStop && orderState == OrderState.Filled)
            {
                Print(string.Format("[AutoTrader-{0}#{1}] Stop ejecutado. Cancelando targets restantes.", Instrument.MasterInstrument.Name, _tradeId));
                CancelarOrdenesPendientes(cancelarT1: true, cancelarT2: true);

                _esperandoSincronizacionFlat = true;
                _ticksEsperandoFlat = 0;
                return;
            }

            // ── T2 ejecutado → cancelar stop BE ──────────────────────────
            if (order.Name == _nameT2 && orderState == OrderState.Filled)
            {
                Print(string.Format("[AutoTrader-{0}#{1}] T2 ejecutado. Cancelando stop BE residual.", Instrument.MasterInstrument.Name, _tradeId));
                CancelarOrdenesPendientes(cancelarStop: true);

                _esperandoSincronizacionFlat = true;
                _ticksEsperandoFlat = 0;
                return;
            }
        }

        // ─── CANCELAR ÓRDENES PENDIENTES ─────────────────────────────────────

        private void CancelarOrdenesPendientes(bool cancelarStop = false, bool cancelarT1 = false, bool cancelarT2 = false)
        {
            if (cancelarT1)   CancelarOrden(ref _t1Order);
            if (cancelarT2)   CancelarOrden(ref _t2Order);
            if (cancelarStop) CancelarOrden(ref _stopOrder);
        }

        private void CancelarOrden(ref Order ord)
        {
            if (ord == null) return;
            try
            {
                if (ord.OrderState == OrderState.Working   ||
                    ord.OrderState == OrderState.Accepted  ||
                    ord.OrderState == OrderState.Submitted ||
                    ord.OrderState == OrderState.Initialized)
                {
                    CancelOrder(ord);
                    Print(string.Format("[AutoTrader-{0}] Cancelada orden: {1}", Instrument.MasterInstrument.Name, ord.Name));
                }
            }
            catch (Exception ex)
            {
                Print(string.Format("[AutoTrader-{0}] Error cancelando orden: {1}", Instrument.MasterInstrument.Name, ex.Message));
            }
            ord = null;
        }

        // ─── CIERRE DE EMERGENCIA ────────────────────────────────────────────

        private void CerrarTodo()
        {
            CancelarOrdenesPendientes(cancelarStop: true, cancelarT1: true, cancelarT2: true);

            try
            {
                if (Position != null && Position.MarketPosition != MarketPosition.Flat)
                {
                    OrderAction flat = _esAlcista ? OrderAction.Sell : OrderAction.BuyToCover;
                    SubmitOrderUnmanaged(0, flat, OrderType.Market, Position.Quantity, 0, 0, "", "CloseAll");
                }
            }
            catch { }

            ResetearEstado();
        }

        // ─── RESET ───────────────────────────────────────────────────────────

        private void ResetearEstado()
        {
            _esperandoSincronizacionFlat = false;
            _ticksEsperandoFlat   = 0;
            _esperandoFillEntrada = false;
            _entryFilled          = false;
            _enOperacion          = false;
            _t1Ejecutado          = false;
            _stopMovidoABE        = false;
            _enviandoSafeClose    = false;
            // Fix #1: marcar señal como ya vista para evitar re-entrada inmediata
            _prevHudSignal        = true;
            _precioEntrada        = double.NaN;
            _stopInicial          = double.NaN;
            _targetT1             = double.NaN;
            _targetT2             = double.NaN;
            _totalContratos       = 0;
            _contratosT1          = 0;
            _contratosT2          = 0;
            _entryOrder           = null;
            _stopOrder            = null;
            _t1Order              = null;
            _t2Order              = null;
            _barEnvioEntrada      = -1;    // Fix #7: resetear contador de timeout
        }
    }
}
