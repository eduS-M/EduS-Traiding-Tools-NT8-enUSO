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
 ESTRATEGIA:   A80StrategyV4_1
 AUTOR:        Eduardo Silva Medina ("EdusTrader")
 DESCRIPCIÓN:  Estrategia A80 con gestión de riesgo configurable
====================================================================

OBJETIVO:
---------
Estrategia de continuación de tendencia tipo "80". Captura retrocesos
profundos cuando el precio cruza la Midline (MB) del canal Keltner y
vuelve hacia la SMA80 en una tendencia confirmada.

La entrada se coloca en la SMA80 +/- un offset configurable.
Esta versión (V1_2) incorpora una gestión flexible de riesgo con
stops/targets/BE/trailing parametrizables.

------------------------------------------------------------
SEÑAL A80 (condiciones para intentar una entrada)
------------------------------------------------------------
1. Tendencia válida:
   - La SMA80 tiene pendiente en una dirección clara (alcista o bajista).
   - La LR(89) tiene pendiente en la MISMA dirección que la SMA80.
   - Ambas pendientes se miden no en 1 barra, sino en las últimas N barras
     (TrendPeriod) y deben superar un umbral mínimo (TrendSlopeTicks * TickSize).
   - Esto evita operar cuando la SMA80 está plana/lateral.

2. Pullback profundo:
   - El precio cruza la Midline (midline) del canal Keltner en contra de la tendencia.
     Ejemplo largos:
       * Tendencia alcista -> Close < midline (retroceso profundo).
     Ejemplo cortos:
       * Tendencia bajista -> Close > midline.

3. Distancia de oportunidad:
   - La SMA80 debe estar a una distancia mínima de la Midline: MinDistanceTicks80.
   - Esto busca que el retroceso realmente "regrese a la zona de valor", no un micro pullback.
     distanceTicks = |midline - SMA80| / TickSize >= MinDistanceTicks80

4. Activación de permiso:
   - Se requiere que haya ocurrido el cruce SMA20 ↔ SMA80 (20 cruza sobre o bajo 80).
   - Ese cruce habilita un "ciclo permitido".
   - Solo se permite 1 operación por ciclo (entryTakenAfterXover).

5. Entrada:
   - Largos:
      * Tendencia alcista válida (SMA80Up && LRUp).
      * Close < midline (pullback).
      * distanceOk.
      * Se envía LIMIT de compra en:
          SMA80 + EntryOffsetTicks * TickSize
   - Cortos:
      * Tendencia bajista válida (SMA80Down && LRDown).
      * Close > midline.
      * distanceOk.
      * Se envía LIMIT de venta en:
          SMA80 - EntryOffsetTicks * TickSize

6. Marcadores:
   - Se dibuja flecha y texto "80" en el gráfico en la barra de la señal.

------------------------------------------------------------
GESTIÓN DE RIESGO (versión 1.2)
------------------------------------------------------------

A) STOP INICIAL:
   StopMode = TrailModeEnum.Dynamic | StopModeEnum.Fixed | StopModeEnum.ATR

   - Dynamic:
       Usa el rango del canal Keltner:
         dynStopTicks = max(StopTicksFloor, round( (UpperBand-LowerBand)/TickSize * 0.30 ))
       StopLong  = entryPrice - dynStopTicks*tick
       StopShort = entryPrice + dynStopTicks*tick

   - Fixed:
       Usa FixedStopTicks directamente.

   - ATR:
       Usa ATRPeriod y ATRMultiplier:
         atrTicks = (ATR(ATRPeriod)[0] / TickSize) * ATRMultiplier
       Usa ese valor en ticks como stop.

   El stop inicial se guarda en currentStop y movedToBE=false.

B) TARGET INICIAL:
   TargetMode = TargetModeEnum.RR | TargetModeEnum.FixedTicks

   - RR:
       RRRatio * stopDistanceTicks
       Ejemplo: stop=10 ticks, RRRatio=2.0 → target=20 ticks
   - FixedTicks:
       FixedTargetTicks directo

C) BREAK EVEN (BE):
   MoveToBEAtRR:
       Cuando la ganancia flotante >= MoveToBEAtRR * riesgoInicialTicks,
       movemos el stop a BE +/- BEOffsetTicks.
       - Largo: BE + BEOffsetTicks
       - Corto: BE - BEOffsetTicks

D) TRAILING (después del BE):
   TrailMode = TrailModeEnum.Disabled | TrailModeEnum.Ticks | TrailModeEnum.Percent | TrailModeEnum.Dynamic

   - TrailModeEnum.Ticks:
        Mantiene el stop a TrailTicks por detrás del precio actual.
        (4 ticks = 1 punto en ES).

   - TrailModeEnum.Percent:
        Stop sigue el precio dejando un % del recorrido desde BE.
        Ejemplo: TrailPercent=0.25 → deja 25% del avance como colchón.

   - TrailModeEnum.Dynamic:
        Usa el stop inicial como base y va subiendo/bajando cuando el
        precio avanza bloques del tamaño de ese riesgo inicial.

   - TrailModeEnum.Disabled:
        No trailing. Se queda en BE una vez movido.

------------------------------------------------------------
HORARIO:
--------
Fuera de HoraInicio-HoraFin no se generan nuevas entradas.

====================================================================
*/

namespace NinjaTrader.NinjaScript.Strategies
{
    public class A80StrategyV4_1 : Strategy
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

        // --- Canal Keltner / estructura base ---
        [NinjaScriptProperty]
        [Display(Name="OffsetMultiplier", Order=0, GroupName="Keltner")]
        public double OffsetMultiplier { get; set; } = 3.5;

        [NinjaScriptProperty]
        [Display(Name="KeltnerPeriod", Order=1, GroupName="Keltner")]
        public int KeltnerPeriod { get; set; } = 52;

        // --- Medias ---
        [NinjaScriptProperty]
        [Display(Name="SMAfast (SMA20)", Order=2, GroupName="Medias")]
        public int SMAfast { get; set; } = 20;

        [NinjaScriptProperty]
        [Display(Name="SMAslow (SMA80)", Order=3, GroupName="Medias")]
        public int SMAslow { get; set; } = 80;

        // --- Condiciones de entrada específicas A80 ---
        [NinjaScriptProperty]
        [Display(Name="MinDistanceTicks80 (MB↔SMA80)", Order=4, GroupName="Entrada A80")]
        public int MinDistanceTicks80 { get; set; } = 6;

        [NinjaScriptProperty]
        [Display(Name="EntryOffsetTicks", Order=5, GroupName="Entrada A80")]
        public int EntryOffsetTicks { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name="TrendPeriod (barras para pendiente)", Order=6, GroupName="Tendencia")]
        public int TrendPeriod { get; set; } = 10;

        [NinjaScriptProperty]
        [Display(Name="TrendSlopeTicks (mínimo)", Order=7, GroupName="Tendencia")]
        public int TrendSlopeTicks { get; set; } = 2;

        // --- Gestión de riesgo configurable ---
        [NinjaScriptProperty]
        [Display(Name="StopMode", Order=8, GroupName="Risk/Stops")]
        public StopModeEnum  StopMode { get; set; } = StopModeEnum.Dynamic; // StopModeEnum.Dynamic, StopModeEnum.Fixed, StopModeEnum.ATR

        [NinjaScriptProperty]
        [Display(Name="FixedStopTicks", Order=9, GroupName="Risk/Stops")]
        public int FixedStopTicks { get; set; } = 12; // solo si StopMode=StopModeEnum.Fixed

        [NinjaScriptProperty]
        [Display(Name="ATRPeriod", Order=10, GroupName="Risk/Stops")]
        public int ATRPeriod { get; set; } = 14; // solo si StopMode=StopModeEnum.ATR

        [NinjaScriptProperty]
        [Display(Name="ATRMultiplier", Order=11, GroupName="Risk/Stops")]
        public double ATRMultiplier { get; set; } = 1.5; // solo si StopMode=StopModeEnum.ATR

        [NinjaScriptProperty]
        [Display(Name="StopTicksFloor (mínimo dinámico)", Order=12, GroupName="Risk/Stops")]
        public int StopTicksFloor { get; set; } = 8; // mínimo para modo Dynamic

        [NinjaScriptProperty]
        [Display(Name="TargetMode", Order=13, GroupName="Risk/Targets")]
        public TargetModeEnum  TargetMode { get; set; } = TargetModeEnum.RR; // TargetModeEnum.RR, TargetModeEnum.FixedTicks

        [NinjaScriptProperty]
        [Display(Name="RRRatio", Order=14, GroupName="Risk/Targets")]
        public double RRRatio { get; set; } = 2.0; // solo si TargetMode=TargetModeEnum.RR

        [NinjaScriptProperty]
        [Display(Name="FixedTargetTicks", Order=15, GroupName="Risk/Targets")]
        public int FixedTargetTicks { get; set; } = 24; // solo si TargetMode=TargetModeEnum.FixedTicks

        [NinjaScriptProperty]
        [Display(Name="MoveToBEAtRR", Order=16, GroupName="Risk/BE+Trail")]
        public double MoveToBEAtRR { get; set; } = 1.0; // múltiplo de riesgo para BE

        [NinjaScriptProperty]
        [Display(Name="BEOffsetTicks", Order=17, GroupName="Risk/BE+Trail")]
        public int BEOffsetTicks { get; set; } = 2;

        [NinjaScriptProperty]
        [Display(Name="TrailMode", Order=18, GroupName="Risk/BE+Trail")]
        public TrailModeEnum  TrailMode { get; set; } = TrailModeEnum.Ticks; // TrailModeEnum.Disabled,TrailModeEnum.Ticks,TrailModeEnum.Percent,TrailModeEnum.Dynamic

        [NinjaScriptProperty]
        [Display(Name="TrailTicks", Order=19, GroupName="Risk/BE+Trail")]
        public int TrailTicks { get; set; } = 4;

        [NinjaScriptProperty]
        [Display(Name="TrailPercent", Order=20, GroupName="Risk/BE+Trail")]
        public double TrailPercent { get; set; } = 0.25;

        // --- Sesión / horarios ---
        [NinjaScriptProperty]
        [Display(Name="HoraInicio", Order=21, GroupName="Horario")]
        public int HoraInicio { get; set; } = 930;

        [NinjaScriptProperty]
        [Display(Name="HoraFin", Order=22, GroupName="Horario")]
        public int HoraFin { get; set; } = 1545;

        [NinjaScriptProperty]
        [Display(Name="EnableDebug (mensajes Output)", Order=23, GroupName="Debug")]
        public bool EnableDebug { get; set; } = true;

        // --- Gestión de cancelación / tolerancia de ruptura ---
        [NinjaScriptProperty]
        [Display(Name="CancelGraceTicks", Order=25, GroupName="Trade/Cancelación")]
        public int CancelGraceTicks { get; set; } = 4; // espera 4 ticks antes de cancelar



        // ========================================================
        
        private Order entryOrder; // referencia a la orden de entrada para cancelación selectiva
// ================= VARIABLES INTERNAS ===================
        // ========================================================

        private Series<double> midline, upperBand, lowerBand;
        private Series<double> sma20, sma80;
        private LinReg linReg;
        private ATR atr; // para StopMode=StopModeEnum.ATR
        private double alphaK;

        // Control de ciclo de entrada
        private bool permissionActive = false;       // activado tras cruce SMA20↔SMA80
        private bool entryTakenAfterXover = false;   // solo 1 trade por ciclo

        // Gestión de la posición actual
        private bool movedToBE = false;              // ya movimos a BE?
        private double currentStop = double.NaN;     // último stop enviado
        private double initialRiskTicks = 0.0;       // riesgo inicial en ticks
        private double entryAvgPrice = 0.0;          // precio promedio de entrada (para trailing/BE)

        // ========================================================
        // ======================= INIT ===========================
        // ========================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "A80StrategyV4_1";
                Calculate = Calculate.OnEachTick;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;

                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
            }
            else if (State == State.DataLoaded)
            {
                // Series internas que calculamos manualmente
                midline = new Series<double>(this);
                upperBand = new Series<double>(this);
                lowerBand = new Series<double>(this);

                sma20 = new Series<double>(this);
                sma80 = new Series<double>(this);

                // Indicadores nativos
                linReg = LinReg(Close, 89);
                atr    = ATR(ATRPeriod);

                // Factor de suavizado tipo EMA para la midline Keltner
                alphaK = 2.0 / (KeltnerPeriod + 1.0);
            }
        }

        // ========================================================
        // ============== FUNCIONES AUXILIARES ====================
        // ========================================================

        private bool DentroHorarioOperacion()
        {
            int hhmm = ToTime(Time[0]) / 100;
            return hhmm >= HoraInicio && hhmm <= HoraFin;
        }

        /// <summary>
        /// Calcula riesgo inicial en ticks según StopMode y devuelve stopPrice.
        /// También setea initialRiskTicks (para BE y trailing).
        /// </summary>
        private double CalcularStopInicial(MarketPosition dir, double fillPrice)
        {
            double stopTicks;

            // 1. Determinar stopTicks según StopMode
            if (StopMode == StopModeEnum.Fixed)
            {
                stopTicks = FixedStopTicks;
            }
            else if (StopMode == StopModeEnum.ATR)
            {
                // ATR en puntos → lo pasamos a ticks y aplicamos multiplicador
                double atrTicks = (atr[0] / TickSize) * ATRMultiplier;
                stopTicks = Math.Max(atrTicks, StopTicksFloor);
            }
            else // TrailModeEnum.Dynamic (default)
            {
                double rangeTicks = (upperBand[0] - lowerBand[0]) / TickSize;
                double dynStopCalc = Math.Round(rangeTicks * 0.30);
                stopTicks = Math.Max(StopTicksFloor, dynStopCalc);
            }

            // 2. Calcular stopPrice usando esos ticks
            double stopPrice;
            if (dir == MarketPosition.Long)
                stopPrice = fillPrice - (stopTicks * TickSize);
            else
                stopPrice = fillPrice + (stopTicks * TickSize);

            // Guardamos riesgo inicial en ticks (para BE/Trailing/Target RR)
            initialRiskTicks = stopTicks;

            return stopPrice;
        }

        /// <summary>
        /// Calcula el target inicial según TargetMode.
        /// Devuelve precio objetivo.
        /// </summary>
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
                // targetTicks = RRRatio * initialRiskTicks
                targetTicks = initialRiskTicks * RRRatio;
            }

            double targetPrice;
            if (dir == MarketPosition.Long)
                targetPrice = fillPrice + (targetTicks * TickSize);
            else
                targetPrice = fillPrice - (targetTicks * TickSize);

            return targetPrice;
        }

        /// <summary>
        /// Lógica de BreakEven + trailing.
        /// Se llama en OnBarUpdate cuando estamos en posición.
        /// </summary>
        private void GestionarBEyTrailing()
        {
            // Nada que hacer si no estamos en mercado
            if (Position.MarketPosition == MarketPosition.Flat)
                return;

            // Precio medio de entrada
            entryAvgPrice = Position.AveragePrice;

            // Ganancia actual en ticks
            double profitTicks = (Position.MarketPosition == MarketPosition.Long)
                ? (Close[0] - entryAvgPrice) / TickSize
                : (entryAvgPrice - Close[0]) / TickSize;

            // 1. MOVER A BE CUANDO SE ALCANZA MoveToBEAtRR * riesgoInicial
            // Por ejemplo: MoveToBEAtRR=1.0 → al llegar a 1:1
            double beTriggerTicks = initialRiskTicks * MoveToBEAtRR;

            if (!movedToBE && profitTicks >= beTriggerTicks)
            {
                double beStop;
                if (Position.MarketPosition == MarketPosition.Long)
                    beStop = entryAvgPrice + (BEOffsetTicks * TickSize); // BE + offset
                else
                    beStop = entryAvgPrice - (BEOffsetTicks * TickSize); // BE - offset

                // Enviamos nuevo StopLoss anclado en beStop
                if (Position.MarketPosition == MarketPosition.Long)
                    SetStopLoss("A80Long", CalculationMode.Price, beStop, false);
                else
                    SetStopLoss("A80Short", CalculationMode.Price, beStop, false);

                currentStop = beStop;
                movedToBE = true;

                if (EnableDebug)
                    Print("[BE] Stop movido a BE +/- offset en " + beStop.ToString("F2"));
            }

            // 2. TRAILING DESPUÉS DEL BE
            if (!movedToBE)
                return; // no hacemos trailing hasta haber movido a BE

            // Si TrailMode está desactivado, no movemos más
            if (TrailMode == TrailModeEnum.Disabled)
                return;

            double newStopCandidate = currentStop;

            if (TrailMode == TrailModeEnum.Ticks)
            {
                // Mantener stop a TrailTicks por detrás del precio actual
                if (Position.MarketPosition == MarketPosition.Long)
                {
                    double trailPrice = Close[0] - (TrailTicks * TickSize);
                    if (trailPrice > currentStop + TickSize) // solo sube
                        newStopCandidate = trailPrice;
                }
                else // Short
                {
                    double trailPrice = Close[0] + (TrailTicks * TickSize);
                    if (trailPrice < currentStop - TickSize) // solo baja
                        newStopCandidate = trailPrice;
                }
            }
            else if (TrailMode == TrailModeEnum.Percent)
            {
                // Mantener stop dejando un % del avance desde BE
                // avanceTicks = profitTicks
                // mantenemos (1 - TrailPercent) de ese avance?
                // Ej: TrailPercent=0.25 → deja 25% "de aire"
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
            else if (TrailMode == TrailModeEnum.Dynamic)
            {
                // Dynamic: trailing en bloques del riesgo inicial.
                // Cada vez que el precio avanza otro "initialRiskTicks",
                // subimos/bajamos el stop por ese bloque.
                double blocks = Math.Floor(profitTicks / initialRiskTicks);

                if (blocks >= 1) // al menos 1 bloque por encima del BE
                {
                    if (Position.MarketPosition == MarketPosition.Long)
                    {
                        double dynTrail = entryAvgPrice
                                          + (blocks * initialRiskTicks * TickSize)
                                          - (TrailTicks * TickSize); // TrailTicks actúa como colchón
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

            // ¿Debemos actualizar el stop?
            if (newStopCandidate != currentStop)
            {
                if (Position.MarketPosition == MarketPosition.Long)
                    SetStopLoss("A80Long", CalculationMode.Price, newStopCandidate, false);
                else
                    SetStopLoss("A80Short", CalculationMode.Price, newStopCandidate, false);

                if (EnableDebug)
                    Print($"[TRAIL] Stop actualizado a {newStopCandidate:F2} (antes {currentStop:F2})");

                currentStop = newStopCandidate;
            }
        }

        // ========================================================
        // ================== EJECUCIÓN DE ORDEN ==================
        // ========================================================
        
        // ========================================================
        // Captura referencia a la orden de entrada (A80Long/A80Short) para cancelación selectiva
        protected override void OnOrderUpdate(Order order, double limitPrice, double stopPrice, int quantity, int filled,
                                              double averageFillPrice, OrderState orderState, DateTime time, ErrorCode error, string nativeError)
        {
            if (order == null) return;
            if (order.Name == "A80Long" || order.Name == "A80Short")
                entryOrder = order;
        }
protected override void OnExecutionUpdate(Execution execution, string executionId, double price,
                                                  int quantity, MarketPosition marketPosition,
                                                  string orderId, DateTime time)
        {
            // Se dispara cuando una orden se llena (fill).
            // CONFIGURAMOS STOP Y TARGET INICIALES AQUÍ.
            if (execution == null || execution.Order == null) return;
            if (execution.Quantity <= 0) return;
            if (execution.Order.OrderState != OrderState.Filled) return;

            // Calculamos y seteamos stop inicial:
            double stopPrice = CalcularStopInicial(marketPosition, price);

            // Calculamos target inicial:
            double targetPrice = CalcularTargetInicial(marketPosition, price);

            // Enviamos StopLoss y ProfitTarget específicos de la etiqueta de entrada:
            if (marketPosition == MarketPosition.Long)
            {
                SetStopLoss("A80Long", CalculationMode.Price, stopPrice, false);
                SetProfitTarget("A80Long", CalculationMode.Price, targetPrice);
            }
            else if (marketPosition == MarketPosition.Short)
            {
                SetStopLoss("A80Short", CalculationMode.Price, stopPrice, false);
                SetProfitTarget("A80Short", CalculationMode.Price, targetPrice);
            }

            currentStop = stopPrice;
            movedToBE = false;
            entryAvgPrice = price;

            if (EnableDebug)
            {
                Print($"[FILL {marketPosition}] Entry={price:F2} Stop={stopPrice:F2} Target={targetPrice:F2} RiskTicks={initialRiskTicks:F2}");
            }
        }

        // ========================================================
        // ==================== LÓGICA PRINCIPAL ==================
        // ========================================================
        protected override void OnBarUpdate()
        {
            // Necesitamos suficientes barras para SMA80 y LinReg
            if (CurrentBar < Math.Max(SMAslow, 90))
                return;

            // ----------------------------
            // 1. Calcular canal Keltner
            // ----------------------------
            // midline = EMA-like del Typical Price
            double tp = (High[0] + Low[0] + Close[0]) / 3.0;
            midline[0] = alphaK * tp + (1 - alphaK) * (CurrentBar > 0 ? midline[1] : tp);

            double range = (High[0] - Low[0]);
            double offset = range * OffsetMultiplier;
            upperBand[0] = midline[0] + offset;
            lowerBand[0] = midline[0] - offset;

            // ----------------------------
            // 2. SMAs y LR
            // ----------------------------
            sma20[0] = SMA(Close, SMAfast)[0];
            sma80[0] = SMA(Close, SMAslow)[0];

            // Pendiente LR medida en TrendPeriod barras
            double lrNow   = linReg[0];
            double lrPast  = linReg[Math.Min(CurrentBar, TrendPeriod)];
            double lrSlope = lrNow - lrPast;

            // Pendiente SMA80 medida en TrendPeriod barras
            double sma80Now  = sma80[0];
            double sma80Past = sma80[Math.Min(CurrentBar, TrendPeriod)];
            double sma80Slope = sma80Now - sma80Past;

            // Confirmación de tendencia REAL, no solo 1 barra
            bool lrUp   = lrSlope   > (TickSize * TrendSlopeTicks);
            bool lrDown = lrSlope   < -(TickSize * TrendSlopeTicks);
            bool sma80Up   = sma80Slope > (TickSize * TrendSlopeTicks);
            bool sma80Down = sma80Slope < -(TickSize * TrendSlopeTicks);

            // ----------------------------
            // 3. Distancia MB ↔ SMA80
            // ----------------------------
            double distanceTicks = Math.Abs(midline[0] - sma80[0]) / TickSize;
            bool distanceOk = distanceTicks >= MinDistanceTicks80;

            // ----------------------------
            // 4. Cruce SMA20 ↔ SMA80 activa permiso
            // ----------------------------
            bool crossUp20over80   = (sma20[1] <= sma80[1]) && (sma20[0] > sma80[0]);
            bool crossDown20under80= (sma20[1] >= sma80[1]) && (sma20[0] < sma80[0]);

            if (crossUp20over80 || crossDown20under80)
            {
                permissionActive = true;
                entryTakenAfterXover = false;

                if (EnableDebug)
                    Print($"[PERMISO A80] Cruce SMA20↔SMA80 detectado @ {Time[0]}");
            }

            // ----------------------------
            // 5. Intentar ENTRADA
            // ----------------------------
            bool puedeIntentarEntrada =
                Position.MarketPosition == MarketPosition.Flat &&
                permissionActive &&
                !entryTakenAfterXover &&
                DentroHorarioOperacion();

            if (puedeIntentarEntrada)
            {
                // LARGOS A80:
                // - SMA80Up y LRUp (misma dirección)
                // - Precio ha caído por debajo de la midline (pullback profundo)
                // - distancia MB↔SMA80 suficiente
                if (sma80Up && lrUp && Close[0] < midline[0] && distanceOk)
                {
                    double entryLongPrice = sma80[0] + (EntryOffsetTicks * TickSize);

                    EnterLongLimit(entryLongPrice, "A80Long");

                    // Visual en el gráfico
                    Draw.ArrowUp(this,   "A80Long_" + CurrentBar,  false, 0, Low[0] - 2*TickSize, Brushes.Lime);
                    Draw.Text   (this,   "A80TxtLong_" + CurrentBar, "80", 0, Low[0] - 5*TickSize, Brushes.Lime);

                    entryTakenAfterXover = true;

                    if (EnableDebug)
                        Print($"[SIGNAL LONG A80] {Time[0]} Entry@{entryLongPrice:F2} DistTicks={distanceTicks:F1}");
                }

                // CORTOS A80:
                // - SMA80Down y LRDown (misma dirección)
                // - Precio ha subido por encima de la midline
                // - distancia MB↔SMA80 suficiente
                else if (sma80Down && lrDown && Close[0] > midline[0] && distanceOk)
                {
                    double entryShortPrice = sma80[0] - (EntryOffsetTicks * TickSize);

                    EnterShortLimit(entryShortPrice, "A80Short");

                    // Visual en el gráfico
                    Draw.ArrowDown(this, "A80Short_" + CurrentBar, false, 0, High[0] + 2*TickSize, Brushes.Red);
                    Draw.Text    (this, "A80TxtShort_" + CurrentBar, "80", 0, High[0] + 5*TickSize, Brushes.Red);

                    entryTakenAfterXover = true;

                    if (EnableDebug)
                        Print($"[SIGNAL SHORT A80] {Time[0]} Entry@{entryShortPrice:F2} DistTicks={distanceTicks:F1}");
                }
            }

            // ----------------------------
            
            // ------------------------------------------------
            // 5.b CANCELACIÓN CON GRACIA (por ticks) SOBRE LA SMA80
            //     Si el precio se aleja de la SMA80 más de CancelGraceTicks
            //     mientras la orden está trabajando, cancelamos SOLO esa orden.
            // ------------------------------------------------
            if (entryOrder != null && entryOrder.OrderState == OrderState.Working)
            {
                double distTicksTo80 = Math.Abs(Close[0] - sma80[0]) / TickSize;
                if (distTicksTo80 > CancelGraceTicks)
                {
                    CancelOrder(entryOrder);
                    entryOrder = null;
                    if (EnableDebug) Print($"[A80] Cancelación por ruptura SMA80 > {CancelGraceTicks}t @ {Time[0]}"); 
                }
            }
// 6. GESTIÓN BE + TRAILING
            // ----------------------------
            GestionarBEyTrailing();

            // Nota: NO reseteamos permissionActive aquí,
            // porque esta V1_2 es "solo primera entrada por ciclo".
            // Para multi-entradas haríamos otra versión y cambiaríamos
            // la lógica de entryTakenAfterXover.
        }
    }
}
