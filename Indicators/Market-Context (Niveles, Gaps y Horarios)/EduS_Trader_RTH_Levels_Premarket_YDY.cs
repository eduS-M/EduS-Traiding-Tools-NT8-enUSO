/*
 * EduS_Trader_RTH_Levels_Premarket_YDY — Indicador NT8 (Modificado V2)
 * ------------------------------------------------
 * Qué dibuja:
 * • PApertura  : Precio de apertura RTH del día (09:30 por defecto)
 * • yHigh      : Máximo del RTH de Ayer (09:30–16:00 del día previo) -> Se publica a las 00:00
 * • yLow       : Mínimo  del RTH de Ayer -> Se publica a las 00:00
 * • PreHigh    : Máximo del Pre‑Market (desde 00:00 hasta 09:30 de hoy) -> Dinámico
 * • PreLow     : Mínimo del Pre‑Market (00:00 → 09:30) -> Dinámico
 * • PCierre    : Cierre de Ayer a las 16:00 -> Se publica a las 00:00
 * • HistMax    : Máximo histórico dentro de los datos cargados en el gráfico
 *
 * Cómo se pinta:
 * • Las líneas se trazan manualmente en OnRender de borde a borde del panel, por lo que
 * NO dependen del zoom ni de cuántas barras haya visibles.
 * • Las etiquetas se ubican a la derecha del panel y pueden ajustarse con RightLabelPadding
 * (distancia al eje) y LabelAbove (altura respecto de la línea).
 * • El estilo por defecto es DashDot para todos, excepto HistMax que es Dot.
 *
 * MODIFICACIONES V2 (Solicitud Usuario):
 * 1. Niveles de Ayer (yHigh, yLow, PCierre): Se calculan al cierre (16:00) pero se PUBLICAN visualmente 
 * recién al cambio de día (00:00), no a las 09:30.
 * 2. Pre-Market: Se resetea a las 00:00 y se dibuja dinámicamente (se mueve) entre las 00:00 y las 09:30.
 * A las 09:30 se congelan.
 * 3. PApertura: Se mantiene fijo a las 09:30.
 *
 * Importante:
 * • Sólo se añadieron COMENTARIOS de la nueva lógica; se conservaron los originales.
 */

#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Xml.Serialization;

using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;       // Extensiones de UI: ToDirectWriteTextFormat(), ToVector2()
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using SharpDX;                   // Vector2 para RenderTarget
using SharpDX.DirectWrite;       // Texto (TextFormat/TextLayout)
#endregion

// Namespace requerido por NinjaTrader
namespace NinjaTrader.NinjaScript.Indicators.Edus_Trader
{
    public class EduS_Trader_RTH_Levels_Premarket_YDY : Indicator
    {
        // ================================
        //  CAMPOS (estado interno)
        // ================================
        // Niveles principales que se muestran en el chart. Se inicializan como NaN
        // y se van fijando conforme se completa cada tramo temporal.
        private double pApertura = double.NaN;   // 09:30 open (PApertura)
        private double yHigh     = double.NaN;   // Máximo del RTH de AYER
        private double yLow      = double.NaN;   // Mínimo  del RTH de AYER
        private double preHigh   = double.NaN;   // Máximo del Pre‑Market (00:00 → 09:30)
        private double preLow    = double.NaN;   // Mínimo  del Pre‑Market (00:00 → 09:30)
        private double pCierre   = double.NaN;   // Cierre de AYER a las 16:00
        private double histMax   = double.NaN;   // Máximo histórico en los datos cargados

        // Acumuladores del RTH del día EN CURSO (para formar yHigh/yLow al día siguiente)
        private double curRthHigh = double.NaN;
        private double curRthLow  = double.NaN;

        // Acumuladores del pre‑market (se consolida al abrir 09:30)
        private double tmpPreHigh = double.NaN;
        private double tmpPreLow  = double.NaN;

        // Último rango RTH COMPLETADO (se guarda a las 16:00 y se “publica” como yHigh/yLow a las 00:00)
        private double lastCompletedRthHigh = double.NaN;
        private double lastCompletedRthLow  = double.NaN;
        // Variable temporal para guardar el cierre de las 16:00 hasta publicarlo a las 00:00
        private double tempPCierre = double.NaN; 

        // Marca de día RTH más reciente; útil si quisieras condicionar el render a “día actual listo”.
        private DateTime lastRthSessionDate = NinjaTrader.Core.Globals.MinDate;

        // Soporte para una lógica visual tipo Pivots (inicio de sesiones visibles, etc.)
        private readonly List<int> newSessionBarIdxArr = new List<int>();

        // Cultura para formatear etiquetas de precio
        private CultureInfo labelCulture;

        // Helper horario: TRUE si una hora HHmmss cae entre Open y Close RTH
        private bool InRth(int hhmmss) => hhmmss >= RthOpenTime && hhmmss < RthCloseTime;

        // ================================
        //  CICLO DE VIDA DEL INDICADOR
        // ================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                // ─ Configuración por defecto en el panel de propiedades ─
                Name                = "EduS_Trader_RTH_Levels_Premarket_YDY";
                Description         = "EdusTrader - Líneas RTH/PreMarket (Mod V2: Niveles Ayer a las 00:00)" + 
										"\n• PApertura: Precio de apertura RTH del día (09:30 por defecto)," +
										"\n• yHigh: Máximo del RTH de Ayer (visible desde las 00:00)," +
										"\n• yLow: Mínimo  del RTH de Ayer (visible desde las 00:00)," + 
										"\n• PreHigh: Máximo del Pre‑Market (Dinámico 00:00 → 09:30)," +
										"\n• PreLow: Mínimo del Pre‑Market (Dinámico 00:00 → 09:30)," +
										"\n• PCierre: Cierre de Ayer a las 16:00 (visible desde las 00:00)," +
										"\n• HistMax: Máximo histórico dentro de los datos cargados en el gráfico";
                Calculate           = Calculate.OnBarClose;   // cálculo al cierre de barra (estable)
                IsOverlay           = true;                    // dibuja en el panel de precio
                DisplayInDataBox    = true;
                DrawOnPricePanel    = false;
                PaintPriceMarkers   = true;                    // burbuja en el eje de precio
                IsAutoScale         = false;                   // auto‑escala desactivada por defecto
				MostrarParametros = false;   // valor por defecto
                ScaleJustification  = ScaleJustification.Right;

                // Horario por defecto (NYSE, Eastern Time)
                RthOpenTime         = 93000;   // 09:30:00
                RthCloseTime        = 160000;  // 16:00:00

                // Formato de etiquetas y estilo de línea
                CultureName         = "es-ES"; // 6.900,50
                Decimals            = 2;
                LineWidth           = 2;

                // ─ Plots (colores y nombres) ─
                //   El orden se conserva para mapear luego en SetPlots().
                AddPlot(Brushes.OrangeRed,  "PApertura"); // idx 0
                AddPlot(Brushes.Lime,       "yHigh");     // idx 1
                AddPlot(Brushes.Lime,       "PreHigh");   // idx 2
                AddPlot(Brushes.Red,        "HistMax");   // idx 3
                AddPlot(Brushes.Crimson,    "yLow");      // idx 4
                AddPlot(Brushes.Crimson,    "PreLow");    // idx 5
                AddPlot(Brushes.OrangeRed,  "PCierre");   // idx 6

                // ─ Estilos por defecto ─
                //   • HistMax (idx 3) con trazo punteado (Dot)   // 0: PApertura, 1: yHigh, 2: PreHigh, 3: HistMax, 4: yLow, 5: PreLow, 6: PCierre
                //   • El resto con DashDot
                for (int i = 0; i < Plots.Length; i++)
                {
                    Plots[i].DashStyleHelper = (i == 3) ? DashStyleHelper.Dot : DashStyleHelper.DashDot;
                    Plots[i].Width = LineWidth;
                }
            }
            else if (State == State.DataLoaded)
            {
                // Inicializa cultura para formatear números en etiquetas
                labelCulture = new CultureInfo(CultureName);
            }
            else if (State == State.Historical)
            {
                // Refuerza estilos al cargar histórico/serie
                for (int i = 0; i < Plots.Length; i++)
                {
                    Plots[i].DashStyleHelper = (i == 3) ? DashStyleHelper.Dot : DashStyleHelper.DashDot;
                    Plots[i].Width = LineWidth;
                }
            }
        }
		// Aquí puedes poner DisplayName
		public override string DisplayName
        {
            get
            {
                     // ON  -> comportamiento nativo de NinjaTrader (Name + parámetros)
       				 // OFF -> solo Name
       				 return MostrarParametros ? base.DisplayName : Name;
            }
        }
		
        // ================================
        //  CÁLCULO EN CADA BARRA
        // ================================
        protected override void OnBarUpdate()
        {
            if (CurrentBar == 0)
            {
                // Primera barra: inicializa histMax.
                histMax = High[0];

                // MODIFICADO: Si arrancamos antes de las 9:30, inicializamos acumuladores Pre
                int t0 = ToTime(Time[0]);
                if (t0 < RthOpenTime)
                {
                    tmpPreHigh = High[0];
                    tmpPreLow  = Low[0];
                    preHigh    = tmpPreHigh;
                    preLow     = tmpPreLow;
                }
                SetPlots();
                return;
            }

            int tPrev = ToTime(Time[1]);
            int tNow  = ToTime(Time[0]);

            // Máximo histórico observado en los datos cargados (independiente de sesiones)
            histMax = Math.Max(histMax, High[0]);

            // -------------------------------------------------------------------------
            // NUEVA LÓGICA 1: CRUCE DE DÍA (00:00) -> Publicar niveles de Ayer
            // -------------------------------------------------------------------------
            // Detectamos si cambió la fecha respecto a la barra anterior.
            if (Time[0].Date > Time[1].Date)
            {
                 // Publica el rango RTH de AYER (guardado a las 16:00) si está disponible
                if (!double.IsNaN(lastCompletedRthHigh)) yHigh = lastCompletedRthHigh;
                if (!double.IsNaN(lastCompletedRthLow))  yLow  = lastCompletedRthLow;
                
                // Publica el Cierre de Ayer (guardado a las 16:00)
                if (!double.IsNaN(tempPCierre))          pCierre = tempPCierre;

                // Reiniciamos acumuladores del Pre-Market para el nuevo día
                tmpPreHigh = High[0];
                tmpPreLow  = Low[0];

                // Asignamos visualmente de inmediato para que se vea desde la barra 00:00
                preHigh = tmpPreHigh;
                preLow  = tmpPreLow;
            }

            // -------------------------------------------------------------------------
            // NUEVA LÓGICA 2: PRE-MARKET DINÁMICO (00:00 -> 09:30)
            // -------------------------------------------------------------------------
            // Mientras estemos antes de la apertura, actualizamos PreHigh/PreLow constantemente
            // para que se muevan en el gráfico.
            if (tNow < RthOpenTime)
            {
                // Acumulamos máximos y mínimos de la sesión nocturna/madrugada
                if (double.IsNaN(tmpPreHigh)) tmpPreHigh = High[0];
                else tmpPreHigh = Math.Max(tmpPreHigh, High[0]);

                if (double.IsNaN(tmpPreLow)) tmpPreLow = Low[0];
                else tmpPreLow = Math.Min(tmpPreLow, Low[0]);

                // Actualizamos la variable visual (Plots) en cada barra
                preHigh = tmpPreHigh;
                preLow  = tmpPreLow;
            }

            // ─ Cruce a APERTURA RTH (09:30) ─
            // Se detecta cuando la hora anterior < 09:30 y la actual ≥ 09:30.
            bool crossedToRthOpen = (tPrev < RthOpenTime && tNow >= RthOpenTime);
            if (crossedToRthOpen)
            {
                // Apertura del día: el Open de la barra que abre 09:30
                pApertura = Open[0];

                // NOTA: Al llegar a 09:30, dejamos de entrar en el bloque "if (tNow < RthOpenTime)",
                // por lo que preHigh y preLow quedan congelados con el último valor alcanzado.

                // Inicia rango RTH del día actual (para ir calculando el nuevo High/Low del día)
                curRthHigh = High[0];
                curRthLow  = Low[0];

                // Limpia acumuladores de pre‑market (aunque ya no se usarán hasta las 00:00 sig)
                tmpPreHigh = double.NaN;
                tmpPreLow  = double.NaN;

                // Guarda el índice de inicio de sesión (útil si quisieras dibujar como Pivots por tramos)
                if (newSessionBarIdxArr.Count == 0 || CurrentBar > newSessionBarIdxArr[newSessionBarIdxArr.Count - 1])
                    newSessionBarIdxArr.Add(CurrentBar);

                lastRthSessionDate = Time[0].Date;
            }

            // Durante RTH, actualiza el rango intradía del día en curso
            if (InRth(tNow))
            {
                curRthHigh = double.IsNaN(curRthHigh) ? High[0] : Math.Max(curRthHigh, High[0]);
                curRthLow  = double.IsNaN(curRthLow)  ? Low[0]  : Math.Min(curRthLow,  Low[0]);
            }

            // ─ Cruce a CIERRE RTH (16:00) ─
            // Fija PCierre y congela el rango RTH del día que terminó.
            // MODIFICADO: Ya no publicamos yHigh/yLow aquí inmediatamente. Los guardamos para las 00:00.
            bool crossedToRthClose = (tPrev < RthCloseTime && tNow >= RthCloseTime);
            if (crossedToRthClose)
            {
                // En series de 1 minuto suele usarse Close[1] para reflejar el precio justo antes de 16:00.
                tempPCierre = Close[Math.Min(1, CurrentBar)]; // Guardamos en variable temporal

                if (!double.IsNaN(curRthHigh)) lastCompletedRthHigh = curRthHigh;
                if (!double.IsNaN(curRthLow))  lastCompletedRthLow  = curRthLow;
                
                // NOTA: La acumulación del próximo pre-market comenzará formalmente tras las 00:00,
                // o si quisieras incluir el After-Market (16:00-00:00) en el Pre, deberías iniciar tmpPre aquí.
                // Según instrucciones, movemos desde las 00:00, así que aquí solo guardamos datos de cierre.
            }

            // Publica valores en las Series (para DataBox, exportación, etc.)
            SetPlots();
        }

        // Mapea los campos calculados a las 7 Series del indicador, manteniendo el orden
        private void SetPlots()
        {
            Values[0][0] = pApertura; // 0: PP -> PApertura
            Values[1][0] = yHigh;     // 1: R1 -> yHigh
            Values[2][0] = preHigh;   // 2: R2 -> PreHigh
            Values[3][0] = histMax;   // 3: R3 -> HistMax
            Values[4][0] = yLow;      // 4: S1 -> yLow
            Values[5][0] = preLow;    // 5: S2 -> PreLow
            Values[6][0] = pCierre;   // 6: S3 -> PCierre
        }

        #region OnRender (estilo "línea fija" a todo el panel)
        // Dibuja manualmente las líneas y etiquetas. Se evita base.OnRender para que
        // las líneas NO dependan de cuántas barras hay visibles: siempre van de borde a borde.
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (ChartBars == null || chartControl == null || chartScale == null)
                return;

            var textFormat = chartControl.Properties.LabelFont.ToDirectWriteTextFormat();

            // Pares (valor, nombre) en el mismo orden de los Plots
            (double val, string name)[] levels = new (double, string)[]
            {
                (pApertura, "PApertura"),
                (yHigh,     "yHigh"),
                (preHigh,   "PreHigh"),
                (histMax,   "HistMax"),
                (yLow,      "yLow"),
                (preLow,    "PreLow"),
                (pCierre,   "PCierre")
            };

            // Extremos horizontales del panel de precio
            float xLeft  = (float)chartControl.CanvasLeft  + 6f; // pequeño margen visual
            float xRight = (float)chartControl.CanvasRight;

            for (int i = 0; i < levels.Length; i++)
            {
                double v = levels[i].val;
                if (double.IsNaN(v))
                    continue; // si el nivel aún no está disponible, no se dibuja

                float y = (float)chartScale.GetYByValue(v);

                // 1) Línea horizontal de lado a lado
                var p0 = new Vector2(xLeft - 6f, y);
                var p1 = new Vector2(xRight,     y);
                RenderTarget.DrawLine(p0, p1, Plots[i].BrushDX, Plots[i].Width, Plots[i].StrokeStyle);

                // 2) Etiqueta a la derecha del panel, con formato regional
                string formatted = v.ToString("N" + Decimals, labelCulture ?? System.Globalization.CultureInfo.InvariantCulture);
                string label     = $"{levels[i].name} ({formatted})";

                using (var layout = new TextLayout(NinjaTrader.Core.Globals.DirectWriteFactory, label, textFormat, ChartPanel.W, textFormat.FontSize))
                {
                    float panelRight = (float)(ChartPanel.X + ChartPanel.W);
                    float xLabel     = panelRight - RightLabelPadding - layout.Metrics.Width;  // distancia ajustable al eje
                    float yText      = (float)(y - layout.Metrics.Height - LabelAbove);        // altura ajustable respecto a la línea

                    RenderTarget.DrawTextLayout(new SharpDX.Vector2(xLabel, yText), layout, Plots[i].BrushDX);
                }
            }

            textFormat.Dispose();
        }
        #endregion

        // ================================
        //  PROPIEDADES EXPUESTAS (UI)
        // ================================
        [NinjaScriptProperty]
        [Display(Name = "RthOpenTime (HHmmss)", GroupName = "Horario", Order = 0)]
        public int RthOpenTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "RthCloseTime (HHmmss)", GroupName = "Horario", Order = 1)]
        public int RthCloseTime { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "CultureName (etiquetas)", GroupName = "Formato", Order = 2)]
        public string CultureName { get; set; }

        [NinjaScriptProperty]
        [Range(0, 8)]
        [Display(Name = "Decimals", GroupName = "Formato", Order = 3)]
        public int Decimals { get; set; }

        [NinjaScriptProperty]
        [Range(1, 6)]
        [Display(Name = "LineWidth (grosor líneas)", GroupName = "Estilo", Order = 4)]
        public int LineWidth { get; set; } = 2;

        [NinjaScriptProperty]
        [Range(50, 5000)]
        [Display(Name = "RenderLookback (barras)", GroupName = "Estilo", Order = 5)]
        public int RenderLookback { get; set; } = 500;      // reservado si se quisiera imitar tramos por sesión

        [NinjaScriptProperty]                    // Distancia de la etiqueta al eje de precio (derecha)
        [Range(0, 300)]
        [Display(Name = "RightLabelPadding", GroupName = "Estilo", Order = 6)]
        public int RightLabelPadding { get; set; } = 15;    // píxeles desde el borde derecho

        [NinjaScriptProperty]                    // Altura de la etiqueta respecto a la línea
        [Range(-10, 20)]		//Original en -100,200 lo cambio para no separarlo tanto
        [Display(Name = "LabelAbove (px)", GroupName = "Estilo", Order = 7)]
        public int LabelAbove { get; set; } = 1;            // +arriba / −abajo
		
		[NinjaScriptProperty]
        [Display(Name = "Mostrar parámetros en etiqueta", GroupName = "Visual", Order = 0)]
        public bool MostrarParametros { get; set; }        //Mostrar parámetros en etiqueta

        // Series públicas (lectura) para DataBox y otras referencias externas
        [Browsable(false)]
        [XmlIgnore] public Series<double> PApertura => Values[0];
        [Browsable(false)]
        [XmlIgnore] public Series<double> YHigh     => Values[1];
        [Browsable(false)]
        [XmlIgnore] public Series<double> PreHigh   => Values[2];
        [Browsable(false)]
        [XmlIgnore] public Series<double> HistMax   => Values[3];
        [Browsable(false)]
        [XmlIgnore] public Series<double> YLow      => Values[4];
        [Browsable(false)]
        [XmlIgnore] public Series<double> PreLow    => Values[5];
        [Browsable(false)]
        [XmlIgnore] public Series<double> PCierre   => Values[6];
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private Edus_Trader.EduS_Trader_RTH_Levels_Premarket_YDY[] cacheEduS_Trader_RTH_Levels_Premarket_YDY;
		public Edus_Trader.EduS_Trader_RTH_Levels_Premarket_YDY EduS_Trader_RTH_Levels_Premarket_YDY(int rthOpenTime, int rthCloseTime, string cultureName, int decimals, int lineWidth, int renderLookback, int rightLabelPadding, int labelAbove, bool mostrarParametros)
		{
			return EduS_Trader_RTH_Levels_Premarket_YDY(Input, rthOpenTime, rthCloseTime, cultureName, decimals, lineWidth, renderLookback, rightLabelPadding, labelAbove, mostrarParametros);
		}

		public Edus_Trader.EduS_Trader_RTH_Levels_Premarket_YDY EduS_Trader_RTH_Levels_Premarket_YDY(ISeries<double> input, int rthOpenTime, int rthCloseTime, string cultureName, int decimals, int lineWidth, int renderLookback, int rightLabelPadding, int labelAbove, bool mostrarParametros)
		{
			if (cacheEduS_Trader_RTH_Levels_Premarket_YDY != null)
				for (int idx = 0; idx < cacheEduS_Trader_RTH_Levels_Premarket_YDY.Length; idx++)
					if (cacheEduS_Trader_RTH_Levels_Premarket_YDY[idx] != null && cacheEduS_Trader_RTH_Levels_Premarket_YDY[idx].RthOpenTime == rthOpenTime && cacheEduS_Trader_RTH_Levels_Premarket_YDY[idx].RthCloseTime == rthCloseTime && cacheEduS_Trader_RTH_Levels_Premarket_YDY[idx].CultureName == cultureName && cacheEduS_Trader_RTH_Levels_Premarket_YDY[idx].Decimals == decimals && cacheEduS_Trader_RTH_Levels_Premarket_YDY[idx].LineWidth == lineWidth && cacheEduS_Trader_RTH_Levels_Premarket_YDY[idx].RenderLookback == renderLookback && cacheEduS_Trader_RTH_Levels_Premarket_YDY[idx].RightLabelPadding == rightLabelPadding && cacheEduS_Trader_RTH_Levels_Premarket_YDY[idx].LabelAbove == labelAbove && cacheEduS_Trader_RTH_Levels_Premarket_YDY[idx].MostrarParametros == mostrarParametros && cacheEduS_Trader_RTH_Levels_Premarket_YDY[idx].EqualsInput(input))
						return cacheEduS_Trader_RTH_Levels_Premarket_YDY[idx];
			return CacheIndicator<Edus_Trader.EduS_Trader_RTH_Levels_Premarket_YDY>(new Edus_Trader.EduS_Trader_RTH_Levels_Premarket_YDY(){ RthOpenTime = rthOpenTime, RthCloseTime = rthCloseTime, CultureName = cultureName, Decimals = decimals, LineWidth = lineWidth, RenderLookback = renderLookback, RightLabelPadding = rightLabelPadding, LabelAbove = labelAbove, MostrarParametros = mostrarParametros }, input, ref cacheEduS_Trader_RTH_Levels_Premarket_YDY);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.Edus_Trader.EduS_Trader_RTH_Levels_Premarket_YDY EduS_Trader_RTH_Levels_Premarket_YDY(int rthOpenTime, int rthCloseTime, string cultureName, int decimals, int lineWidth, int renderLookback, int rightLabelPadding, int labelAbove, bool mostrarParametros)
		{
			return indicator.EduS_Trader_RTH_Levels_Premarket_YDY(Input, rthOpenTime, rthCloseTime, cultureName, decimals, lineWidth, renderLookback, rightLabelPadding, labelAbove, mostrarParametros);
		}

		public Indicators.Edus_Trader.EduS_Trader_RTH_Levels_Premarket_YDY EduS_Trader_RTH_Levels_Premarket_YDY(ISeries<double> input , int rthOpenTime, int rthCloseTime, string cultureName, int decimals, int lineWidth, int renderLookback, int rightLabelPadding, int labelAbove, bool mostrarParametros)
		{
			return indicator.EduS_Trader_RTH_Levels_Premarket_YDY(input, rthOpenTime, rthCloseTime, cultureName, decimals, lineWidth, renderLookback, rightLabelPadding, labelAbove, mostrarParametros);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.Edus_Trader.EduS_Trader_RTH_Levels_Premarket_YDY EduS_Trader_RTH_Levels_Premarket_YDY(int rthOpenTime, int rthCloseTime, string cultureName, int decimals, int lineWidth, int renderLookback, int rightLabelPadding, int labelAbove, bool mostrarParametros)
		{
			return indicator.EduS_Trader_RTH_Levels_Premarket_YDY(Input, rthOpenTime, rthCloseTime, cultureName, decimals, lineWidth, renderLookback, rightLabelPadding, labelAbove, mostrarParametros);
		}

		public Indicators.Edus_Trader.EduS_Trader_RTH_Levels_Premarket_YDY EduS_Trader_RTH_Levels_Premarket_YDY(ISeries<double> input , int rthOpenTime, int rthCloseTime, string cultureName, int decimals, int lineWidth, int renderLookback, int rightLabelPadding, int labelAbove, bool mostrarParametros)
		{
			return indicator.EduS_Trader_RTH_Levels_Premarket_YDY(input, rthOpenTime, rthCloseTime, cultureName, decimals, lineWidth, renderLookback, rightLabelPadding, labelAbove, mostrarParametros);
		}
	}
}

#endregion
