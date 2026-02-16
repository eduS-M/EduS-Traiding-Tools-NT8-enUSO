#region Using declarations
using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class EduS_Keltner_LimitExecutive : Strategy
    {
        // --- Keltner ---
        private EMA emaDiff;
        private EMA emaTypical;
        private Series<double> diff;

        // --- ATM ---
        private string atmStrategyId       = string.Empty;
        private string entryOrderId        = string.Empty;
        private bool   protectiveOrdersSet = false;

        // Contexto para stop / target y log
        private double lastBandWidth;
        private double lastStopPoints;
        private double lastTargetPoints;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Estrategia Keltner que lanza automáticamente una ATM, " +
                              "calcula Stop = % del ancho de banda y Target = RR*Stop, " +
                              "ajusta los niveles en la ATM y registra todo en CSV.";

                Name      = "EduS_Keltner_LimitExecutive";

                Calculate = Calculate.OnEachTick;

                // Que la estrategia no afecte el auto-scale del gráfico
                IsOverlay   = true;
                IsAutoScale = false;

                // Parámetros por defecto
                Period            = 52;
                OffsetMultiplier  = 3.5;
                StopPercentOfBand = 0.30;   // 30% ancho banda
                RiskRewardRatio   = 3.0;    // 3:1
                Contracts         = 1;

                // Nombre de la plantilla ATM
                AtmTemplateName   = "EduS_ATM_Template";

                IsInstantiatedOnEachOptimizationIteration = false;
            }
            else if (State == State.Configure)
            {
                diff       = new Series<double>(this);
                emaDiff    = EMA(diff, Period);
                emaTypical = EMA(Typical, Period);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < Period)
                return;

            // Rango de la barra
            diff[0] = High[0] - Low[0];

            // Lógica ATM SOLO en tiempo real (no en histórico)[6](https://www.reddit.com/r/ninjatrader/comments/174os4u/nt_chart_scaling_help/)[7](http://www.ninjatrader.com/support/helpGuides/nt8/submitting_orders4.htm)
            if (State != State.Realtime)
                return;

            // 1) Si todavía no existe una ATM activa, buscamos condición de entrada
            if (atmStrategyId == string.Empty)
            {
                // *** EJEMPLO SIMPLE DE ENTRADA LONG ***
                if (Close[0] > emaTypical[0])
                {
                    double middle    = emaTypical[0];
                    double offset    = emaDiff[0] * OffsetMultiplier;
                    double upperBand = middle + offset;
                    double lowerBand = middle - offset;

                    double bandWidth    = upperBand - lowerBand;
                    double stopPoints   = bandWidth * StopPercentOfBand;
                    double targetPoints = stopPoints * RiskRewardRatio;

                    lastBandWidth    = bandWidth;
                    lastStopPoints   = stopPoints;
                    lastTargetPoints = targetPoints;

                    double entryPrice = Close[0];

                    // IDs únicos para orden de entrada y ATM
                    entryOrderId  = GetAtmStrategyUniqueId();
                    atmStrategyId = GetAtmStrategyUniqueId();

                    // 2) Creamos la ATM usando la plantilla (entry LIMIT)[8](http://www.ninjatrader.com/support/helpGuides/nt8/tutorial_atm_strategy_example_.htm)[9](https://www.xabcdtrading.com/blog/best-atm-strategy-for-ninjatrader-8/)
                    AtmStrategyCreate(
                        OrderAction.Buy,
                        OrderType.Limit,
                        entryPrice,
                        0,
                        TimeInForce.Day,
                        entryOrderId,
                        AtmTemplateName,
                        atmStrategyId,
                        (atmCallbackErrorCode, atmCallbackId) =>
                        {
                            if (atmCallbackErrorCode != ErrorCode.NoError)
                                Print("Error al crear ATM: " + atmCallbackErrorCode + "  Id:" + atmCallbackId);
                        });

                    protectiveOrdersSet = false;

                    LogTradeContextATM("Long", entryPrice, bandWidth, stopPoints, targetPoints);
                }
            }
	//            else
	//            {
	//                // 3) Ya hay una ATM. Miramos su posición.[10](https://www.udemy.com/course/ninjatraderatm/)[11](https://ninjatraderecosystem.com/user-app-share-download/sample-atm-strategy-reversal/)
	//                MarketPosition atmPos = GetAtmStrategyMarketPosition(atmStrategyId);
	
	//                // Si la ATM está flat, limpiamos estado y salimos
	//                if (atmPos == MarketPosition.Flat)
	//                {
	//                    atmStrategyId       = string.Empty;
	//                    entryOrderId        = string.Empty;
	//                    protectiveOrdersSet = false;
	//                    return;
	//                }
	
	//                // 4) Si ya hay posición pero aún no hemos ajustado Stop/Target dinámicos:
	//                if (!protectiveOrdersSet)
	//                {
	//                    // IMPORTANTE:
	//                    // Antes de cambiar nada, verificamos que EXISTEN Stop1 / Target1
	//                    // usando GetAtmStrategyStopTargetOrderStatus.[4](http://www.ninjatrader.com/support/helpGuides/nt8/getatmstrategystoptargetorders.htm)[5](https://developer.ninjatrader.com/docs/desktop/getatmstrategystoptargetorderstatus)
	//                    var stopStatus   = GetAtmStrategyStopTargetOrderStatus("Stop1",   atmStrategyId);
	//                    var targetStatus = GetAtmStrategyStopTargetOrderStatus("Target1", atmStrategyId);
	
	//                    if (stopStatus == null  || stopStatus.Length == 0 ||
	//                        targetStatus == null || targetStatus.Length == 0)
	//                    {
	//                        // Aún no se han creado las órdenes de protección; esperamos al siguiente tick.
	//                        Print("Aún no existen Stop1/Target1 en la ATM, se espera al siguiente OnBarUpdate.");
	//                        return;
	//                    }
	
	//                    // Ahora sí, ya existen Stop1/Target1. Calculamos precios sobre el promedio REAL del ATM[12](https://www.quagensia.com/help/when-ninjatrader-strategy-ninjascript-onexecutionupdate-occurs-section)[13](https://ninjatrader.com/support/helpGuides/nt8/using_onorderupdate_and_onexec.htm)
	//                    double avgPrice = GetAtmStrategyPositionAveragePrice(atmStrategyId);
	//                    if (avgPrice <= 0)
	//                    {
	//                        Print("avgPrice <= 0, no se ajusta Stop/Target.");
	//                        return;
	//                    }
	
	//                    double stopPrice   = 0.0;
	//                    double targetPrice = 0.0;
	
	//                    if (atmPos == MarketPosition.Long)
	//                    {
	//                        stopPrice   = avgPrice - lastStopPoints;
	//                        targetPrice = avgPrice + lastTargetPoints;
	
	//                        // seguridad: que el stop quede por debajo del Bid
	//                        double bid = GetCurrentBid();
	//                        if (stopPrice >= bid)
	//                            stopPrice = bid - 2 * TickSize;
	//                    }
	//                    else if (atmPos == MarketPosition.Short)
	//                    {
	//                        stopPrice   = avgPrice + lastStopPoints;
	//                        targetPrice = avgPrice - lastTargetPoints;
	
	//                        // seguridad: que el stop de recompra quede por encima del Ask
	//                        double ask = GetCurrentAsk();
	//                        if (stopPrice <= ask)
	//                            stopPrice = ask + 2 * TickSize;
	//                    }
	
	//                    // 5) Cambiamos los precios de Stop1 y Target1.[2](https://github.com/MicroTrendsLtd/NinjaTrader8/issues/45)[13](https://ninjatrader.com/support/helpGuides/nt8/using_onorderupdate_and_onexec.htm)
	//                    bool stopOk = AtmStrategyChangeStopTarget(
	//                        0,
	//                        stopPrice,
	//                        "Stop1",
	//                        atmStrategyId);
	
	//                    bool targetOk = AtmStrategyChangeStopTarget(
	//                        targetPrice,
	//                        0,
	//                        "Target1",
	//                        atmStrategyId);
	
	//                    Print("Ajuste dinámico ATM -> StopOK: " + stopOk + "  TargetOK: " + targetOk +
	//                          " | StopPrice: " + stopPrice.ToString("F2") +
	//                          " | TargetPrice: " + targetPrice.ToString("F2"));
	
	//                    protectiveOrdersSet = true;
	//                }
	//            }
			
					else
		{
		    // 3) Ya hay una ATM. Miramos su posición.
		    MarketPosition atmPos = GetAtmStrategyMarketPosition(atmStrategyId);
		
		    // Si la ATM está flat, limpiamos estado
		    if (atmPos == MarketPosition.Flat)
		    {
		        atmStrategyId       = string.Empty;
		        entryOrderId        = string.Empty;
		        protectiveOrdersSet = false;
		        return;
		    }
		
		    if (!protectiveOrdersSet)
		    {
		        // --- DEBUG 1: Estado de la ATM
		        Print("ATM DEBUG | StrategyId=" + atmStrategyId +
		              " | Pos=" + atmPos +
		              " | AvgPrice=" + GetAtmStrategyPositionAveragePrice(atmStrategyId).ToString("F2"));
		
		        // 1) ¿EXISTEN Stop1 / Target1?
		        var stopStatus   = GetAtmStrategyStopTargetOrderStatus("Stop1",   atmStrategyId);
		        var targetStatus = GetAtmStrategyStopTargetOrderStatus("Target1", atmStrategyId);
		
		        int stopCount   = (stopStatus   == null ? 0 : stopStatus.Length);
		        int targetCount = (targetStatus == null ? 0 : targetStatus.Length);
		
		        // --- DEBUG 2: Cantidad de órdenes encontradas
		        Print("ATM DEBUG | Stop1 count=" + stopCount + " | Target1 count=" + targetCount);
		
		        if (stopCount == 0 || targetCount == 0)
		        {
		            // Aún no existen las órdenes de protección, esperamos al siguiente tick
		            return;
		        }
		
		        // 2) Ya existen Stop1 / Target1 → calculamos precios
		        double avgPrice = GetAtmStrategyPositionAveragePrice(atmStrategyId);
		        if (avgPrice <= 0)
		        {
		            Print("ATM DEBUG | avgPrice <= 0, no se ajusta.");
		            return;
		        }
		
		        double stopPrice   = 0.0;
		        double targetPrice = 0.0;
		
		        if (atmPos == MarketPosition.Long)
		        {
		            stopPrice   = avgPrice - lastStopPoints;
		            targetPrice = avgPrice + lastTargetPoints;
		        }
		        else if (atmPos == MarketPosition.Short)
		        {
		            stopPrice   = avgPrice + lastStopPoints;
		            targetPrice = avgPrice - lastTargetPoints;
		        }
		
		        // --- DEBUG 3: Precios calculados
		        Print("ATM DEBUG | Calc Stop=" + stopPrice.ToString("F2") +
		              " | Target=" + targetPrice.ToString("F2"));
		
		        // 3) Intentamos mover Stop1 / Target1
		        bool stopOk = AtmStrategyChangeStopTarget(
		            0,
		            stopPrice,
		            "Stop1",
		            atmStrategyId);
		
		        bool targetOk = AtmStrategyChangeStopTarget(
		            targetPrice,
		            0,
		            "Target1",
		            atmStrategyId);
		
		        // --- DEBUG 4: Resultado de los cambios
		        Print("ATM DEBUG | ChangeStopTarget -> stopOk=" + stopOk +
		              " | targetOk=" + targetOk);
		
		        protectiveOrdersSet = true;
		    }
		}
        }

        /// <summary>
        /// Loguea contexto del trade en CSV.
        /// </summary>
        private void LogTradeContextATM(string side, double entryPrice, double bWidth, double sPts, double tPts)
        {
            string path   = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "EduS_Executive_Log.csv");
            bool   exists = File.Exists(path);

            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using (StreamWriter sw = new StreamWriter(path, true))
                    {
                        if (!exists)
                        {
                            sw.WriteLine("Fecha;Instrumento;Lado;PrecioEntrada;AnchoKeltner;StopSugerido;TargetSugerido;Cuenta;ATMId");
                        }

                        string line = string.Format("{0};{1};{2};{3};{4};{5};{6};{7};{8}",
                            DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                            Instrument != null ? Instrument.FullName : "N/A",
                            side,
                            entryPrice.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                            bWidth.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                            sPts.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                            tPts.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                            Account != null ? Account.Name : "N/A",
                            atmStrategyId
                        );

                        sw.WriteLine(line);
                    }
                }
                catch (Exception ex)
                {
                    Print("Error Log ATM: " + ex.Message);
                }
            });
        }

        #region Propiedades

        [NinjaScriptProperty]
        [Display(Name = "Period", GroupName = "1. Keltner", Order = 1)]
        public int Period { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Multiplier", GroupName = "1. Keltner", Order = 2)]
        public double OffsetMultiplier { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Stop % (de la banda)", GroupName = "2. Riesgo", Order = 1)]
        public double StopPercentOfBand { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "RR Ratio (Target/Stop)", GroupName = "2. Riesgo", Order = 2)]
        public double RiskRewardRatio { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Contracts", GroupName = "2. Riesgo", Order = 3)]
        public int Contracts { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ATM Template Name", GroupName = "3. ATM", Order = 1)]
        public string AtmTemplateName { get; set; }

        #endregion
    }
}