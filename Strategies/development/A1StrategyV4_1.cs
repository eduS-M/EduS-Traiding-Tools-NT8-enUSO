#region Using
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows.Media;
#endregion

/*
====================================================================
 ESTRATEGIA:   A1StrategyV4_1
 AUTOR:        Eduardo Silva Medina ("EdusTrader")
 DESCRIPCIÓN:  Estrategia A1 con gestión de riesgo configurable
====================================================================

RESUMEN DE LA A1:
-----------------
La A1 busca capturar el PRIMER impulso de una nueva tendencia inmediatamente
después del cruce de la SMA20 sobre/ bajo la SMA80. Opera el PRIMER retroceso
hacia la Midline del canal Keltner, siempre que la tendencia esté alineada y
la regresión lineal (LR) esté pegada al precio.

ESTA VERSIÓN V2 AGREGA:
-----------------------
1. Gestión de riesgo profesional y configurable:
   - Stop inicial configurable (Dynamic / Fixed / ATR)
   - Target configurable (RR o ticks fijos)
   - BreakEven configurable (a qué múltiplo RR mover a BE)
   - Trailing Stop configurable (Disabled / Ticks / Percent / Dynamic)

2. Misma lógica de entrada que la A1 original (A1StrategyV1):
   - Se habilita permiso en el cruce SMA20↔SMA80.
   - Solo se permite UNA entrada por ciclo post-cruce.
   - El precio debe hacer pullback hacia la midline del Keltner.
   - La LR debe estar en la misma dirección de la señal y
     estar "pegada/detrás" del precio (no sobreextendida).
   - Se respeta horario operativo.

3. Marcadores visuales:
   - Dibuja flecha y etiqueta "A1" en cada entrada.
   - Útil para backtest visual y revisión manual.

--------------------------------------------------------------------
LÓGICA DE ENTRADA (A1)
--------------------------------------------------------------------
1. Detección de cambio de tendencia:
   - Se detecta un cruce SMA20 ↔ SMA80 (hacia arriba o hacia abajo).
   - Eso activa permisoActive = true y resetea entryTakenAfterXover = false.
   - Este cruce marca el "inicio de ciclo de tendencia A1".

2. Condición de setup LONG:
   - SMA20 > SMA80 y el Close actual está por encima de la midline (tendencia alcista activa).
   - LR(89) con pendiente positiva (lrUp).
   - LR "pegada o por detrás" del precio:
       Close - LR <= LRToleranceTicks * TickSize.
     Esto evita entrar si el precio ya se despegó demasiado.
   - El precio hace pullback cerca de la midline del Keltner:
       |Close - midline| <= 2 ticks.
   - permissionActive == true y todavía no tomamos trade en este ciclo.
   - Dentro del horario.

   → Se lanza orden LIMIT de compra ligeramente por encima de la midline.

3. Condición de setup SHORT:
   - SMA20 < SMA80 y Close < midline (tendencia bajista activa).
   - LR pendiente negativa (lrDown).
   - LR "pegada o por detrás" del precio bajista:
       LR - Close <= LRToleranceTicks * TickSize.
   - Pullback cerca de la midline.
   - permissionActive == true y sin trade previo en este ciclo.
   - Dentro del horario.

   → Se lanza orden LIMIT de venta ligeramente por debajo de la midline.

4. Solo una entrada por ciclo:
   - Después de enviar la orden de entrada de la A1, marcamos
     entryTakenAfterXover = true para no repetir trades en esta misma
     fase de tendencia.

--------------------------------------------------------------------
GESTIÓN DE RIESGO CONFIGURABLE (V2)
--------------------------------------------------------------------
A) STOP INICIAL:
   Controlado por StopMode:
   - TrailModeEnum.Dynamic:
        Stop basado en el rango del canal Keltner (30% del ancho).
        Tiene un piso en ticks (StopTicksFloor).
   - StopModeEnum.Fixed:
        Stop fijo, en ticks (FixedStopTicks).
   - StopModeEnum.ATR:
        Stop basado en ATR(ATRPeriod) * ATRMultiplier, convertido a ticks.

   Guardamos el número de ticks de riesgo inicial en initialRiskTicks.
   Esto es crítico para BE y trailing.

B) TARGET INICIAL:
   Controlado por TargetMode:
   - TargetModeEnum.RR:
        targetTicks = initialRiskTicks * RRRatio
   - TargetModeEnum.FixedTicks:
        targetTicks = FixedTargetTicks

C) BREAK EVEN:
   MoveToBEAtRR indica cuándo mover el stop a BreakEven.
   Ejemplo:
      MoveToBEAtRR = 1.0 → mover stop a BE cuando ganancia flotante >= 1R.
   BEOffsetTicks:
      LONG: Stop se mueve a Entry + BEOffsetTicks
      SHORT: Stop se mueve a Entry - BEOffsetTicks

D) TRAILING STOP:
   Controlado por TrailMode:
    - TrailModeEnum.Disabled: no trailing, solo BE.
    - TrailModeEnum.Ticks: mantiene el stop a TrailTicks por detrás/encima del precio.
    - TrailModeEnum.Percent: trailing proporcional al avance (TrailPercent).
    - TrailModeEnum.Dynamic: trailing por bloques del riesgo inicial (tipo swing).

--------------------------------------------------------------------
HORARIO:
--------
No se generan nuevas entradas fuera del rango [HoraInicio, HoraFin].

====================================================================
*/

namespace NinjaTrader.NinjaScript.Strategies
{
    public class A1StrategyV4_1 : Strategy
    {
        // =====================================================
        // ENUMS (listas desplegables para propiedades fijas)
        // =====================================================
        public enum StopModeEnum   { Dynamic, Fixed, ATR }
        public enum TargetModeEnum { RR, FixedTicks }
        public enum TrailModeEnum  { Disabled, Ticks, Dynamic, Percent  }
        public enum RiskModelEnum  { Conservative, Standard, Aggressive }

        // ========================================================
        // =============== PARÁMETROS CONFIGURABLES ===============
        // ========================================================

        // --- Canal Keltner / estructura base del pullback ---
        [NinjaScriptProperty]
        [Display(Name="OffsetMultiplier", Order=0, GroupName="Keltner")]
        public double OffsetMultiplier { get; set; } = 3.5;

        [NinjaScriptProperty]
        [Display(Name="KeltnerPeriod", Order=1, GroupName="Keltner")]
        public int KeltnerPeriod { get; set; } = 52;

        // --- Medias para cruce de tendencia ---
        [NinjaScriptProperty]
        [Display(Name="SMAfast (SMA20)", Order=2, GroupName="Medias")]
        public int SMAfast { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name="SMAslow (SMA80)", Order=3, GroupName="Medias")]
        public int SMAslow { get; set; } = 80;

        // --- Condiciones finas de la A1 ---
        [NinjaScriptProperty]
        [Display(Name="LRToleranceTicks", Order=4, GroupName="Condición LR pegada")]
        public int LRToleranceTicks { get; set; } = 3;
        // LRToleranceTicks controla cuántos ticks de separación se toleran
        // entre el precio y la LR. Mientras más pequeño, más estricta la entrada.

        // --- Horario ---
        [NinjaScriptProperty]
        [Display(Name="HoraInicio", Order=5, GroupName="Horario")]
        public int HoraInicio { get; set; } = 930;

        [NinjaScriptProperty]
        [Display(Name="HoraFin", Order=6, GroupName="Horario")]
        public int HoraFin { get; set; } = 1545;

        // --- Gestión de riesgo configurable (idéntico esquema A80 V1_2) ---

        // Stop Mode
        [NinjaScriptProperty]
        [Display(Name="StopMode", Order=7, GroupName="Risk/Stops")]
        public StopModeEnum StopMode { get; set; } = StopModeEnum.Dynamic;  // StopModeEnum.Dynamic, StopModeEnum.Fixed, StopModeEnum.ATR

        [NinjaScriptProperty]
        [Display(Name="FixedStopTicks", Order=8, GroupName="Risk/Stops")]
        public int FixedStopTicks { get; set; } = 12;      // solo si StopMode=StopModeEnum.Fixed

        [NinjaScriptProperty]
        [Display(Name="ATRPeriod", Order=9, GroupName="Risk/Stops")]
        public int ATRPeriod { get; set; } = 14;           // solo si StopMode=StopModeEnum.ATR

        [NinjaScriptProperty]
        [Display(Name="ATRMultiplier", Order=10, GroupName="Risk/Stops")]
        public double ATRMultiplier { get; set; } = 1.5;   // solo si StopMode=StopModeEnum.ATR

        [NinjaScriptProperty]
        [Display(Name="StopTicksFloor (mínimo dinámico)", Order=11, GroupName="Risk/Stops")]
        public int StopTicksFloor { get; set; } = 8;       // piso mínimo para modo Dynamic

        // Target Mode
        [NinjaScriptProperty]
        [Display(Name="TargetMode", Order=12, GroupName="Risk/Targets")]
        public TargetModeEnum TargetMode { get; set; } = TargetModeEnum.RR;     // TargetModeEnum.RR, TargetModeEnum.FixedTicks

        [NinjaScriptProperty]
        [Display(Name="RRRatio", Order=13, GroupName="Risk/Targets")]
        public double RRRatio { get; set; } = 2.0;         // usado si TargetMode=TargetModeEnum.RR

        [NinjaScriptProperty]
        [Display(Name="FixedTargetTicks", Order=14, GroupName="Risk/Targets")]
        public int FixedTargetTicks { get; set; } = 24;    // usado si TargetMode=TargetModeEnum.FixedTicks

        // BE + Trailing
        [NinjaScriptProperty]
        [Display(Name="MoveToBEAtRR", Order=15, GroupName="Risk/BE+Trail")]
        public double MoveToBEAtRR { get; set; } = 1.0;    // mover a BE al alcanzar este múltiplo de R

        [NinjaScriptProperty]
        [Display(Name="BEOffsetTicks", Order=16, GroupName="Risk/BE+Trail")]
        public int BEOffsetTicks { get; set; } = 2;        // Long: +ticks, Short: -ticks

        [NinjaScriptProperty]
        [Display(Name="TrailMode", Order=17, GroupName="Risk/BE+Trail")]
        public TrailModeEnum TrailMode { get; set; } = TrailModeEnum.Ticks;   // TrailModeEnum.Disabled,TrailModeEnum.Ticks,TrailModeEnum.Percent,TrailModeEnum.Dynamic

        [NinjaScriptProperty]
        [Display(Name="TrailTicks", Order=18, GroupName="Risk/BE+Trail")]
        public int TrailTicks { get; set; } = 4;           // usado en TrailModeEnum.Ticks y como colchón en TrailModeEnum.Dynamic

        [NinjaScriptProperty]
        [Display(Name="TrailPercent", Order=19, GroupName="Risk/BE+Trail")]
        public double TrailPercent { get; set; } = 0.25;   // usado en TrailModeEnum.Percent

        // --- Debug ---
        [NinjaScriptProperty]
        [Display(Name="EnableDebug (mensajes Output)", Order=20, GroupName="Debug")]
        public bool EnableDebug { get; set; } = true;

        // --- Gestión de cancelación / tolerancia de ruptura ---
        [NinjaScriptProperty]
        [Display(Name="CancelGraceTicks", Order=21, GroupName="Trade/Cancelación")]
        public int CancelGraceTicks { get; set; } = 4; // espera 4 ticks antes de cancelar



        // ========================================================
        
        private Order entryOrder; // Referencia a la orden de entrada para cancelación selectiva
// ================= VARIABLES INTERNAS ===================
        // ========================================================

        // Series internas calculadas en tiempo real
        private Series<double> midline, upperBand, lowerBand;
        private Series<double> sma20, sma80;

        // Indicadores
        private LinReg linReg;
        private ATR atr; // para StopMode=StopModeEnum.ATR

        // Suavizado interno tipo EMA de midline (canal Keltner)
        private double alphaK;

        // Flags de control de ciclo A1
        private bool permissionActive = false;       // true después del cruce SMA20↔SMA80
        private bool entryTakenAfterXover = false;   // evita más de una A1 en el ciclo

        // Control de BE / Trailing
        private bool movedToBE = false;
        private double currentStop = double.NaN;
        private double initialRiskTicks = 0.0;
        private double entryAvgPrice = 0.0;


        // ========================================================
        // ======================== INIT ==========================
        // ========================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "A1StrategyV4_1";
                Calculate = Calculate.OnEachTick;

                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
            }
            else if (State == State.DataLoaded)
            {
                // Inicializamos las series que calculamos manualmente
                midline   = new Series<double>(this);
                upperBand = new Series<double>(this);
                lowerBand = new Series<double>(this);

                sma20 = new Series<double>(this);
                sma80 = new Series<double>(this);

                // Instanciamos indicadores nativos
                linReg = LinReg(Close, 89);
                atr    = ATR(ATRPeriod);

                // alphaK: suavizado EMA para la midline del Keltner
                alphaK = 2.0 / (KeltnerPeriod + 1.0);
            }
        }


        // ========================================================
        // ================= FUNCIONES AUXILIARES =================
        // ========================================================

        private bool DentroHorarioOperacion()
        {
            int hhmm = ToTime(Time[0]) / 100;
            return hhmm >= HoraInicio && hhmm <= HoraFin;
        }

        // Cálculo del stop inicial basado en StopMode.
        private double CalcularStopInicial(MarketPosition dir, double fillPrice)
        {
            double stopTicks;

            if (StopMode == StopModeEnum.Fixed)
            {
                // Stop fijo en ticks
                stopTicks = FixedStopTicks;
            }
            else if (StopMode == StopModeEnum.ATR)
            {
                // Stop basado en ATR*Multiplicador
                double atrTicks = (atr[0] / TickSize) * ATRMultiplier;
                stopTicks = Math.Max(atrTicks, StopTicksFloor);
            }
            else
            {
                // TrailModeEnum.Dynamic (por defecto)
                // Basado en ancho del canal Keltner
                double rangeTicks = (upperBand[0] - lowerBand[0]) / TickSize;
                double dynStopCalc = Math.Round(rangeTicks * 0.30);
                stopTicks = Math.Max(StopTicksFloor, dynStopCalc);
            }

            // Construye el precio del stop
            double stopPrice;
            if (dir == MarketPosition.Long)
                stopPrice = fillPrice - (stopTicks * TickSize);
            else
                stopPrice = fillPrice + (stopTicks * TickSize);

            // Guardamos el riesgo inicial en ticks para usar más tarde (BE, target y trailing)
            initialRiskTicks = stopTicks;
            return stopPrice;
        }

        // Cálculo del target inicial basado en TargetMode.
        private double CalcularTargetInicial(MarketPosition dir, double fillPrice)
        {
            double targetTicks;

            if (TargetMode == TargetModeEnum.FixedTicks)
            {
                targetTicks = FixedTargetTicks;
            }
            else
            {
                // TargetModeEnum.RR (default)
                targetTicks = initialRiskTicks * RRRatio;
            }

            double targetPrice;
            if (dir == MarketPosition.Long)
                targetPrice = fillPrice + (targetTicks * TickSize);
            else
                targetPrice = fillPrice - (targetTicks * TickSize);

            return targetPrice;
        }

        // Mueve a BE cuando se alcanza cierto múltiplo de R y luego aplica trailing configurable.
        private void GestionarBEyTrailing()
        {
            if (Position.MarketPosition == MarketPosition.Flat)
                return;

            entryAvgPrice = Position.AveragePrice;

            // Ganancia flotante actual en ticks
            double profitTicks = (Position.MarketPosition == MarketPosition.Long)
                ? (Close[0] - entryAvgPrice) / TickSize
                : (entryAvgPrice - Close[0]) / TickSize;

            // Trigger para BE: MoveToBEAtRR * initialRiskTicks
            double beTriggerTicks = initialRiskTicks * MoveToBEAtRR;

            // 1. Rompe a BE
            if (!movedToBE && profitTicks >= beTriggerTicks)
            {
                double beStop;
                if (Position.MarketPosition == MarketPosition.Long)
                    beStop = entryAvgPrice + (BEOffsetTicks * TickSize); // BE + offset
                else
                    beStop = entryAvgPrice - (BEOffsetTicks * TickSize); // BE - offset

                if (Position.MarketPosition == MarketPosition.Long)
                    SetStopLoss("A1Long", CalculationMode.Price, beStop, false);
                else
                    SetStopLoss("A1Short", CalculationMode.Price, beStop, false);

                currentStop = beStop;
                movedToBE = true;

                if (EnableDebug)
                    Print($"[A1 BE] Stop movido a BE +/- offset en {beStop:F2}");
            }

            // Si no hemos movido a BE, no avanzamos con trailing todavía
            if (!movedToBE)
                return;

            // Si trailing desactivado, nos quedamos en BE
            if (TrailMode == TrailModeEnum.Disabled)
                return;

            double newStopCandidate = currentStop;

            // 2. Trailing por Ticks (mantener stop a TrailTicks del precio actual)
            if (TrailMode == TrailModeEnum.Ticks)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    double trailPrice = Close[0] - (TrailTicks * TickSize);
                    if (trailPrice > currentStop + TickSize)
                        newStopCandidate = trailPrice;
                }
                else
                {
                    double trailPrice = Close[0] + (TrailTicks * TickSize);
                    if (trailPrice < currentStop - TickSize)
                        newStopCandidate = trailPrice;
                }
            }
            // 3. Trailing por Porcentaje del avance actual
            else if (TrailMode == TrailModeEnum.Percent)
            {
                double allowedDrawbackTicks = profitTicks * TrailPercent;

                if (Position.MarketPosition == MarketPosition.Long)
                {
                    double targetStop = Close[0] - (allowedDrawbackTicks * TickSize);
                    if (targetStop > currentStop + TickSize)
                        newStopCandidate = targetStop;
                }
                else
                {
                    double targetStop = Close[0] + (allowedDrawbackTicks * TickSize);
                    if (targetStop < currentStop - TickSize)
                        newStopCandidate = targetStop;
                }
            }
            // 4. Trailing TrailModeEnum.Dynamic: por bloques del riesgo inicial
            //    Cada múltiplo de initialRiskTicks que el precio avanza,
            //    subimos/bajamos el stop en bloques, dejando TrailTicks como colchón.
            else if (TrailMode == TrailModeEnum.Dynamic)
            {
                double blocks = Math.Floor(profitTicks / initialRiskTicks);

                if (blocks >= 1)
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        double dynTrail = entryAvgPrice
                                        + (blocks * initialRiskTicks * TickSize)
                                        - (TrailTicks * TickSize); // TrailTicks como colchón
                        if (dynTrail > currentStop + TickSize)
                            newStopCandidate = dynTrail;
                    }
                    else
                    {
                        double dynTrail = entryAvgPrice
                                        - (blocks * initialRiskTicks * TickSize)
                                        + (TrailTicks * TickSize);
                        if (dynTrail < currentStop - TickSize)
                            newStopCandidate = dynTrail;
                    }
                }
            }

            // ¿Hay que actualizar el stop real?
            if (newStopCandidate != currentStop)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    SetStopLoss("A1Long", CalculationMode.Price, newStopCandidate, false);
                else
                    SetStopLoss("A1Short", CalculationMode.Price, newStopCandidate, false);

                if (EnableDebug)
                    Print($"[A1 TRAIL] Stop actualizado a {newStopCandidate:F2} (antes {currentStop:F2})");

                currentStop = newStopCandidate;
            }
        }


        // ========================================================
        // =================== EJECUCIÓN DE ORDEN =================
        // ========================================================
        
        // ========================================================
        // Captura de referencia a la orden de entrada para poder cancelarla selectivamente
        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
                                              double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (order == null) return;
            // Guardamos SOLO la orden de esta estrategia con las etiquetas A1Long/A1Short
            if (order.Name == "A1Long" || order.Name == "A1Short")
                entryOrder = order;
        }
protected override void OnExecutionUpdate(Execution execution, string executionId, double price,
                                                  int quantity, MarketPosition marketPosition,
                                                  string orderId, DateTime time)
        {
            // Esta rutina se llama cada vez que se llena una orden.
            // Aquí seteamos el StopLoss y ProfitTarget iniciales.
            if (execution == null || execution.Order == null) return;
            if (execution.Quantity <= 0) return;
            if (execution.Order.OrderState != OrderState.Filled) return;

            // Calcula stop inicial según el modo configurado
            double stopPrice = CalcularStopInicial(marketPosition, price);

            // Calcula el target inicial según el modo configurado
            double targetPrice = CalcularTargetInicial(marketPosition, price);

            // Enviamos las órdenes de protección (stop/target) etiquetadas
            if (marketPosition == MarketPosition.Long)
            {
                SetStopLoss("A1Long", CalculationMode.Price, stopPrice, false);
                SetProfitTarget("A1Long", CalculationMode.Price, targetPrice);
            }
            else if (marketPosition == MarketPosition.Short)
            {
                SetStopLoss("A1Short", CalculationMode.Price, stopPrice, false);
                SetProfitTarget("A1Short", CalculationMode.Price, targetPrice);
            }

            // Reset de control de BE/trailing para el nuevo trade
            currentStop = stopPrice;
            movedToBE = false;
            entryAvgPrice = price;

            if (EnableDebug)
            {
                Print($"[A1 FILL {marketPosition}] Entry={price:F2} Stop={stopPrice:F2} Target={targetPrice:F2} RiskTicks={initialRiskTicks:F2}");
            }
        }


        // ========================================================
        // ==================== LÓGICA PRINCIPAL ==================
        // ========================================================
        protected override void OnBarUpdate()
        {
            // Necesitamos al menos SMAslow barras para SMA80, y algo más para LR(89)
            if (CurrentBar < Math.Max(SMAslow, 90))
                return;

            // ------------------------------------------------
            // 1. Canal tipo Keltner (midline, upperBand, lowerBand)
            // ------------------------------------------------
            // midline se calcula como una EMA sobre el Typical Price (H+L+C)/3
            double tp = (High[0] + Low[0] + Close[0]) / 3.0;
            midline[0] = alphaK * tp + (1 - alphaK) * (CurrentBar > 0 ? midline[1] : tp);

            double range = (High[0] - Low[0]);
            double offset = range * OffsetMultiplier;

            upperBand[0] = midline[0] + offset;
            lowerBand[0] = midline[0] - offset;

            // ------------------------------------------------
            // 2. SMAs y regresión lineal
            // ------------------------------------------------
            sma20[0] = SMA(Close, SMAfast)[0];
            sma80[0] = SMA(Close, SMAslow)[0];

            // Pendiente de LR (usamos 5 barras atrás para estimar slope local)
            double lrNow  = linReg[0];
            double lrPast = linReg[Math.Min(CurrentBar, 5)];
            double lrSlope = lrNow - lrPast;
            bool lrUp   = lrSlope > 0;
            bool lrDown = lrSlope < 0;

            // LR pegada al precio:
            // Largos: precio debe estar cerca o por encima de LR pero no demasiado alejado
            bool lrCloseToPriceLong  = (Close[0] - linReg[0]) <= (LRToleranceTicks * TickSize);
            // Cortos: precio por debajo pero no demasiado lejos
            bool lrCloseToPriceShort = (linReg[0] - Close[0]) <= (LRToleranceTicks * TickSize);

            // ------------------------------------------------
            // 3. Definición de estructura de tendencia y pullback
            // ------------------------------------------------
            bool trendLong =
                sma20[0] > sma80[0] &&     // media rápida sobre lenta
                Close[0]  > midline[0];    // precio por encima de la midline => estructura alcista

            bool trendShort =
                sma20[0] < sma80[0] &&     // media rápida bajo lenta
                Close[0]  < midline[0];    // precio por debajo de la midline => estructura bajista

            // Pullback cerca de la midline: ±2 ticks
            bool pullbackNear = Math.Abs(Close[0] - midline[0]) / TickSize <= 2;

            // ------------------------------------------------
            // 4. Detección de cruce SMA20↔SMA80
            //    Este cruce habilita el ciclo A1 (permisoActive = true)
            // ------------------------------------------------
            bool crossUp20over80    = (sma20[1] <= sma80[1]) && (sma20[0] > sma80[0]);
            bool crossDown20under80 = (sma20[1] >= sma80[1]) && (sma20[0] < sma80[0]);

            if (crossUp20over80 || crossDown20under80)
            {
                permissionActive = true;
                entryTakenAfterXover = false;

                if (EnableDebug)
                    Print($"[A1 PERMISO] Cruce SMA20↔SMA80 detectado @ {Time[0]}");
            }

            // ------------------------------------------------
            // 5. Intentar ENTRADA A1 (una sola por ciclo)
            // ------------------------------------------------
            bool puedeIntentarEntrada =
                Position.MarketPosition == MarketPosition.Flat &&
                permissionActive &&
                !entryTakenAfterXover &&
                pullbackNear &&
                DentroHorarioOperacion();

            if (puedeIntentarEntrada)
            {
                // ---------- SETUP LARGO ----------
                if (trendLong && lrUp && lrCloseToPriceLong)
                {
                    double limitPriceLong = midline[0] + TickSize;

                    EnterLongLimit(limitPriceLong, "A1Long");

                    Draw.ArrowUp(
                        this,
                        "A1Long_" + CurrentBar,
                        false,
                        0,
                        Low[0] - 2 * TickSize,
                        Brushes.Lime);

                    Draw.Text(
                        this,
                        "A1TextLong_" + CurrentBar,
                        "A1",
                        0,
                        Low[0] - 5 * TickSize,
                        Brushes.Lime);

                    entryTakenAfterXover = true;

                    if (EnableDebug)
                    {
                        double distLR = Close[0] - linReg[0];
                        Print($"[SIGNAL LONG A1] {Time[0]} @ {limitPriceLong:F2} | distLR={distLR:F2} ticksTol={LRToleranceTicks}");
                    }
                }
                // ---------- SETUP CORTO ----------
                else if (trendShort && lrDown && lrCloseToPriceShort)
                {
                    double limitPriceShort = midline[0] - TickSize;

                    EnterShortLimit(limitPriceShort, "A1Short");

                    Draw.ArrowDown(
                        this,
                        "A1Short_" + CurrentBar,
                        false,
                        0,
                        High[0] + 2 * TickSize,
                        Brushes.Red);

                    Draw.Text(
                        this,
                        "A1TextShort_" + CurrentBar,
                        "A1",
                        0,
                        High[0] + 5 * TickSize,
                        Brushes.Red);

                    entryTakenAfterXover = true;

                    if (EnableDebug)
                    {
                        double distLR = linReg[0] - Close[0];
                        Print($"[SIGNAL SHORT A1] {Time[0]} @ {limitPriceShort:F2} | distLR={distLR:F2} ticksTol={LRToleranceTicks}");
                    }
                }
            }

            // ------------------------------------------------
            
            // ------------------------------------------------
            // 5.b CANCELACIÓN CON GRACIA (por ticks) SOBRE LA MB
            //     Si el precio se aleja de la midline más de CancelGraceTicks
            //     mientras la orden está trabajando, cancelamos SOLO esa orden.
            // ------------------------------------------------
            if (entryOrder != null && entryOrder.OrderState == OrderState.Working)
            {
                double distTicksToMB = Math.Abs(Close[0] - midline[0]) / TickSize;
                if (distTicksToMB > CancelGraceTicks)
                {
                    CancelOrder(entryOrder);
                    entryOrder = null;
                    if (EnableDebug) Print($"[A1] Cancelación por ruptura MB > {CancelGraceTicks}t @ {Time[0]}"); 
                }
            }
// 6. GESTIÓN BE + TRAILING dinámico
            // ------------------------------------------------
            GestionarBEyTrailing();

            // Nota importante:
            // En esta versión A1StrategyV4_RiskManager seguimos con la política
            // de "una sola entrada por ciclo". Eso significa que incluso si
            // volvemos a estar Flat, NO reiniciamos entryTakenAfterXover hasta
            // que ocurra un NUEVO cruce SMA20↔SMA80.
        }
    }
}
