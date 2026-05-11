#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Xml.Serialization;

using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
#endregion

// Indicador CalcOnly inspirado en EduS_Trader_Nodes_V8_Institutional
// - Usa ventana por minutos (WindowMinutes)
// - Calcula un Volume Profile de ventana
// - Expone POC, AP_Low (Anti-POC inferior), AP_High (Anti-POC superior)
// - Expone LVNs estructurales ("precio1|precio2|...")
// - No dibuja nada (es para usarlo dentro de estrategias / loggers)
namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader_Calc_Only
{
    public class EduS_Trader_Nodes_V8_CalcOnly : Indicator
    {
        #region Parámetros

        [NinjaScriptProperty, Range(1, 240)]
        [Display(Name = "Minutos Ventana", GroupName = "1. Configuración", Order = 0)]
        public int WindowMinutes { get; set; } = 60;

        [NinjaScriptProperty, Range(1, 100)]
        [Display(Name = "Fuerza LVN estructural", Description = "Número de vecinos a cada lado para detectar valles locales.", GroupName = "1. Configuración", Order = 1)]
        public int StructuralStrength { get; set; } = 5;

        [NinjaScriptProperty]
        [Display(Name = "Modo Distribución", Description = "Full: distribuye el volumen por todo el rango de la barra; Close/OHLC simplificados.", GroupName = "1. Configuración", Order = 2)]
        public EduS_DistMode_Institutional DistributionMode { get; set; } = EduS_DistMode_Institutional.Full;

		
		//Para ATR dinámico (INICIO 1)
//		[NinjaScriptProperty]
//		[Display(Name = "Usar Fuerza Dinámica (ATR)", GroupName = "1. Configuración", Order = 3)]
//		public bool UseDynamicStrength { get; set; } = true;
		
//		[NinjaScriptProperty, Range(1, 50)]
//		[Display(Name = "ATR Period", GroupName = "1. Configuración", Order = 4)]
//		public int ATR_Period { get; set; } = 14;
		
//		[NinjaScriptProperty, Range(0.1, 10.0)]
//		[Display(Name = "ATR Factor", GroupName = "1. Configuración", Order = 5)]
//		public double ATR_Factor { get; set; } = 1.5;		
		//Para ATR dinámico (FIN 1)

		
        #endregion

        #region Internos

        private Dictionary<double, double> vpWindow;   // precio -> volumen
        private Queue<Dictionary<double, double>> fifoBars;
        private double tickSize;

        private int windowBars; // barras efectivas calculadas a partir de WindowMinutes
        private bool windowBarsInitialized = false;

        // Salidas internas
        private double pocValue;
        private double apLower;
        private double apUpper;
        private List<double> lvnsStruct;
		
		//Para ATR dinámico (INICIO 2)
//		private ATR atrSeries;
//		private int dynamicStrength;   // fuerza calculada dinámicamente
		//Para ATR dinámico (FIN 2)
		
        #endregion

        #region Salidas públicas

        [Browsable(false), XmlIgnore]
        public double POC_Value => pocValue;

        [Browsable(false), XmlIgnore]
        public double AP_Lower_Value => apLower;

        [Browsable(false), XmlIgnore]
        public double AP_Upper_Value => apUpper;

        [Browsable(false), XmlIgnore]
        public string WindowLVNs_String
        {
            get
            {
                if (lvnsStruct == null || lvnsStruct.Count == 0)
                    return string.Empty;

                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < lvnsStruct.Count; i++)
                {
                    if (i > 0) sb.Append("|");
                    sb.Append(Instrument.MasterInstrument.FormatPrice(lvnsStruct[i]));
                }
                return sb.ToString();
            }
        }

        #endregion

        #region State Machine

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "EduS_Trader_Nodes_V8_CalcOnly";
                Description = "Calculo de POC/AP/LVNs de ventana (por minutos) sin dibujo, para uso en strategies.";
                IsOverlay   = false;
                Calculate   = Calculate.OnBarClose;
                IsSuspendedWhileInactive = true;

                BarsRequiredToPlot = 20; // se ajustará de facto cuando windowBars esté listo
            }
            else if (State == State.DataLoaded)
            {
                vpWindow = new Dictionary<double, double>();
                fifoBars = new Queue<Dictionary<double, double>>();

                tickSize = (Instrument != null) ? Instrument.MasterInstrument.TickSize : 0.25;

                pocValue   = double.NaN;
                apLower    = double.NaN;
                apUpper    = double.NaN;
                lvnsStruct = new List<double>();
            }
        }

        #endregion

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 1 || tickSize <= 0)
                return;

            // 1) Inicializar windowBars basado en WindowMinutes solo una vez con datos reales
            if (!windowBarsInitialized)
            {
                AjustarWindowBarsPorTiempo();
                BarsRequiredToPlot = Math.Max(20, windowBars + 5);
                windowBarsInitialized = true;
            }

            // 2) Construir perfil de esta barra
            double vol   = Math.Max(Volume[0], 1);   // evita volumen 0
            double high  = High[0];
            double low   = Low[0];
            double open  = Open[0];
            double close = Close[0];

            var barProfile = CalculateBarProfile(vol, high, low, close, open, DistributionMode);

            // 3) Acumular en vpWindow
            foreach (var kv in barProfile)
            {
                if (!vpWindow.ContainsKey(kv.Key))
                    vpWindow[kv.Key] = 0.0;
                vpWindow[kv.Key] += kv.Value;
            }

            // 4) Encolar este perfil y podar si superamos windowBars
            fifoBars.Enqueue(barProfile);
            while (fifoBars.Count > windowBars)
            {
                var old = fifoBars.Dequeue();
                foreach (var kv in old)
                {
                    if (vpWindow.ContainsKey(kv.Key))
                    {
                        vpWindow[kv.Key] -= kv.Value;
                        if (vpWindow[kv.Key] <= 0.0001)
                            vpWindow.Remove(kv.Key);
                    }
                }
            }

            // 5) Si no hay suficientes precios en la ventana, limpiar salidas
            if (vpWindow.Count < 10)
            {
                pocValue = double.NaN;
                apLower  = double.NaN;
                apUpper  = double.NaN;
                lvnsStruct.Clear();
                return;
            }

            // 6) Recalcular POC/AP/LVNs en cada barra
            ComputeNodesFromProfile();
        }

        #region Helpers

        // Estimación de WindowBars a partir de WindowMinutes y BarsPeriod
        private void AjustarWindowBarsPorTiempo()
        {
            int barrasPorMinuto = 1; // default

            if (BarsPeriod.BarsPeriodType == BarsPeriodType.Minute)
            {
                // Ej: 1-min -> 1 barra/min; 5-min -> 0.2 barras/min (mín 1)
                barrasPorMinuto = 1 / BarsPeriod.Value;
                if (barrasPorMinuto < 1) barrasPorMinuto = 1;
            }
            else
            {
                // Tick/volumen/otros: estimación basada en últimos X minutos
                int lookbackMinutes = 10;
                int barsBack = Bars.GetBar(Time[0].AddMinutes(-lookbackMinutes));
                if (barsBack > 0)
                {
                    barrasPorMinuto = (CurrentBar - barsBack) / lookbackMinutes;
                    if (barrasPorMinuto < 1) barrasPorMinuto = 1;
                }
            }

            windowBars = WindowMinutes * barrasPorMinuto;
            Print($"[INFO CalcOnly] Ventana: {WindowMinutes} min → {windowBars} barras (Barras/min: {barrasPorMinuto})");
        }

        // Perfil de barra similar al original (Full / OHLC / Close)
        private Dictionary<double, double> CalculateBarProfile(double val, double high, double low, double close, double open, EduS_DistMode_Institutional mode)
        {
            var r = new Dictionary<double, double>();
            if (Math.Abs(val) <= 0.000001) return r;

            switch (mode)
            {
                case EduS_DistMode_Institutional.Close:
                    {
                        double pr = Instrument.MasterInstrument.RoundToTickSize(close);
                        r[pr] = val;
                    }
                    break;

                case EduS_DistMode_Institutional.OHLC:
                    {
                        double v4 = val * 0.25;
                        void Add(double p)
                        {
                            double pr = Instrument.MasterInstrument.RoundToTickSize(p);
                            if (!r.ContainsKey(pr)) r[pr] = 0;
                            r[pr] += v4;
                        }
                        Add(open); Add(high); Add(low); Add(close);
                    }
                    break;

                default: // Full
                    {
                        int ticks = (int)Math.Round((high - low) / tickSize) + 1;
                        if (ticks < 1) ticks = 1;
                        double vt = val / ticks;
                        for (int i = 0; i < ticks; i++)
                        {
                            double pr = Instrument.MasterInstrument.RoundToTickSize(low + i * tickSize);
                            if (!r.ContainsKey(pr)) r[pr] = 0;
                            r[pr] += vt;
                        }
                    }
                    break;
            }

            return r;
        }

        // Lógica "Extendida" y estructural (inspirada en FindExtendedNodes + FindStructuralLVNs)
        private void ComputeNodesFromProfile()
        {
            // Ordenar niveles de precio
            var sorted = vpWindow.OrderBy(kv => kv.Key).ToList();
            int n = sorted.Count;
            if (n < 3)
            {
                pocValue = double.NaN;
                apLower  = double.NaN;
                apUpper  = double.NaN;
                lvnsStruct.Clear();
                return;
            }

            // POC: precio con max volumen
            double maxVol = double.MinValue;
            double poc = double.NaN;
            foreach (var kv in sorted)
            {
                if (kv.Value > maxVol)
                {
                    maxVol = kv.Value;
                    poc    = kv.Key;
                }
            }
            pocValue = poc;

            // AP Lower / Upper: mínimos en tercio inferior / superior
            int tercio = n / 3;
            if (tercio < 1) tercio = 1;

            double minLowerVol = double.MaxValue;
            double minLower    = double.NaN;
            for (int i = 0; i < tercio; i++)
            {
                var kv = sorted[i];
                if (kv.Value < minLowerVol)
                {
                    minLowerVol = kv.Value;
                    minLower    = kv.Key;
                }
            }
            apLower = minLower;

            double minUpperVol = double.MaxValue;
            double minUpper    = double.NaN;
            for (int i = 2 * tercio; i < n; i++)
            {
                var kv = sorted[i];
                if (kv.Value < minUpperVol)
                {
                    minUpperVol = kv.Value;
                    minUpper    = kv.Key;
                }
            }
            apUpper = minUpper;

            // LVNs estructurales: valles locales con "strength" vecinos a cada lado
            lvnsStruct.Clear();
            int strength = Math.Max(1, Math.Min(StructuralStrength, (n - 1) / 2));
            for (int i = strength; i < n - strength; i++)
            {
                double cv = sorted[i].Value;
                bool valley = true;
                for (int j = 1; j <= strength; j++)
                {
                    if (sorted[i - j].Value <= cv || sorted[i + j].Value <= cv)
                    {
                        valley = false;
                        break;
                    }
                }
                if (valley)
                    lvnsStruct.Add(sorted[i].Key);
            }
        }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader_Calc_Only.EduS_Trader_Nodes_V8_CalcOnly[] cacheEduS_Trader_Nodes_V8_CalcOnly;
		public EduS_Trader_Calc_Only.EduS_Trader_Nodes_V8_CalcOnly EduS_Trader_Nodes_V8_CalcOnly(int windowMinutes, int structuralStrength, EduS_DistMode_Institutional distributionMode)
		{
			return EduS_Trader_Nodes_V8_CalcOnly(Input, windowMinutes, structuralStrength, distributionMode);
		}

		public EduS_Trader_Calc_Only.EduS_Trader_Nodes_V8_CalcOnly EduS_Trader_Nodes_V8_CalcOnly(ISeries<double> input, int windowMinutes, int structuralStrength, EduS_DistMode_Institutional distributionMode)
		{
			if (cacheEduS_Trader_Nodes_V8_CalcOnly != null)
				for (int idx = 0; idx < cacheEduS_Trader_Nodes_V8_CalcOnly.Length; idx++)
					if (cacheEduS_Trader_Nodes_V8_CalcOnly[idx] != null && cacheEduS_Trader_Nodes_V8_CalcOnly[idx].WindowMinutes == windowMinutes && cacheEduS_Trader_Nodes_V8_CalcOnly[idx].StructuralStrength == structuralStrength && cacheEduS_Trader_Nodes_V8_CalcOnly[idx].DistributionMode == distributionMode && cacheEduS_Trader_Nodes_V8_CalcOnly[idx].EqualsInput(input))
						return cacheEduS_Trader_Nodes_V8_CalcOnly[idx];
			return CacheIndicator<EduS_Trader_Calc_Only.EduS_Trader_Nodes_V8_CalcOnly>(new EduS_Trader_Calc_Only.EduS_Trader_Nodes_V8_CalcOnly(){ WindowMinutes = windowMinutes, StructuralStrength = structuralStrength, DistributionMode = distributionMode }, input, ref cacheEduS_Trader_Nodes_V8_CalcOnly);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader_Calc_Only.EduS_Trader_Nodes_V8_CalcOnly EduS_Trader_Nodes_V8_CalcOnly(int windowMinutes, int structuralStrength, EduS_DistMode_Institutional distributionMode)
		{
			return indicator.EduS_Trader_Nodes_V8_CalcOnly(Input, windowMinutes, structuralStrength, distributionMode);
		}

		public Indicators.EduS_Trader_Calc_Only.EduS_Trader_Nodes_V8_CalcOnly EduS_Trader_Nodes_V8_CalcOnly(ISeries<double> input , int windowMinutes, int structuralStrength, EduS_DistMode_Institutional distributionMode)
		{
			return indicator.EduS_Trader_Nodes_V8_CalcOnly(input, windowMinutes, structuralStrength, distributionMode);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader_Calc_Only.EduS_Trader_Nodes_V8_CalcOnly EduS_Trader_Nodes_V8_CalcOnly(int windowMinutes, int structuralStrength, EduS_DistMode_Institutional distributionMode)
		{
			return indicator.EduS_Trader_Nodes_V8_CalcOnly(Input, windowMinutes, structuralStrength, distributionMode);
		}

		public Indicators.EduS_Trader_Calc_Only.EduS_Trader_Nodes_V8_CalcOnly EduS_Trader_Nodes_V8_CalcOnly(ISeries<double> input , int windowMinutes, int structuralStrength, EduS_DistMode_Institutional distributionMode)
		{
			return indicator.EduS_Trader_Nodes_V8_CalcOnly(input, windowMinutes, structuralStrength, distributionMode);
		}
	}
}

#endregion
