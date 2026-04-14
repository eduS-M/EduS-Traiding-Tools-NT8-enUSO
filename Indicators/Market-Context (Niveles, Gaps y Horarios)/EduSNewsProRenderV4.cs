#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Windows.Media; 
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.DrawingTools;
using System.Windows;
// Librerías DirectX
using SharpDX;
using SharpDX.Direct2D1;
using SharpDX.DirectWrite;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.EduS_Trader
{
    public class EduS_News_Pro_Render_V4 : Indicator
    {
        private class NewsEvent
        {
            public DateTime EventTime { get; set; }
            public string Impact { get; set; }
            public string Title { get; set; }
            public bool AlertTriggered { get; set; }
        }

        private volatile List<NewsEvent> newsList = new List<NewsEvent>();
        private string fullPath;
        private FileSystemWatcher watcher;

        // Recursos Gráficos
        private SharpDX.Direct2D1.Brush redBrushDX;
        private SharpDX.Direct2D1.Brush orangeBrushDX;
        private SharpDX.Direct2D1.Brush yellowBrushDX;
        private SharpDX.Direct2D1.Brush textBrushDX;
        private SharpDX.DirectWrite.TextFormat textFormat;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = "Zona de Exclusión con Auto-Actualización, al modificar el Archivo:" + 
								"\n Se actualiza con el archivo EduS_News_Sync.py Ubicación" +				
								"\n		Ruta: Documentos-NinjaTrader8-Incoming" + 
								"\n		Crae el Archivo con el Nombre exacto: Noticias.csv" + 
								"\n	 el Archivo: Info_manual.csv  esta explicado como actualizar cada semana" + 				
								"\n		# Formato: Fecha;Hora;Impacto;Titulo" + 
								"\n		# Ejemplo de datos Macro (Asegurese de usar su Huso Horario)" + 
								"\n		2025-12-10;08:30;Rojo;CPI Core Inflation" + 
								"\n		2025-12-10;10:30;Naranja;Inventarios Crudo" + 
								"\n		2025-12-10;14:00;Rojo;FOMC Rate Decision";
                Name = "EduS_News_Alert_Zone_Auto";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                
                FileName = @"C:\Users\eduar\OneDrive - Desarrollo Personal\Documents\NinjaTrader 8\incoming\Noticias.csv";
				RutaName = @"C:\Users\eduar\OneDrive - Desarrollo Personal\Documents\NinjaTrader 8\incoming\EduS_News_Sync.py";
                ZoneMinutes = 5;
                AlertMinutesBefore = 10;
                
                ShowHighImpact = true;
                ShowMediumImpact = true;
                ShowLowImpact = false;
                
                ZoneOpacity = 20; 
                TextColor = System.Windows.Media.Brushes.White;
                TextSize = 8;
                
                // NUEVO: Margen para bajar el texto y que no choque con el techo
                TextTopMargin = 15; 
            }
            else if (State == State.Configure)
            {
                fullPath = Path.Combine(NinjaTrader.Core.Globals.UserDataDir, "Incoming", FileName);
                SetupWatcher();
            }
            else if (State == State.DataLoaded)
            {
                LoadNewsData();
            }
            else if (State == State.Terminated)
            {
                if (watcher != null)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                    watcher = null;
                }
                DisposeDXResources();
            }
        }

        protected override void OnBarUpdate()
        {
            if (newsList == null || newsList.Count == 0) return;

            DateTime chartTime = Time[0];

            if (State == State.Realtime)
            {
                for (int i = 0; i < newsList.Count; i++)
                {
                    var news = newsList[i];
                    TimeSpan timeDiff = news.EventTime - chartTime;

                    if (timeDiff.TotalMinutes <= AlertMinutesBefore && 
                        timeDiff.TotalMinutes > 0 && 
                        !news.AlertTriggered)
                    {
                        string msg = $"EduS News: {news.Title} ({news.Impact}) en {timeDiff.TotalMinutes:F0} min.";
                        Alert("NewsAlert_" + news.EventTime.Ticks, Priority.High, msg, 
                              NinjaTrader.Core.Globals.InstallDir + @"\sounds\Alert1.wav", 
                              10, System.Windows.Media.Brushes.Black, System.Windows.Media.Brushes.Yellow);
                        news.AlertTriggered = true;
                    }
                }
            }
        }

		//Nuevo ingreso 06-01-2026
		public override void OnRenderTargetChanged()
		{
    		// 1. Destruimos los recursos vinculados al objetivo anterior
    		DisposeDXResources();

    		// 2. Creamos los recursos nuevos para el objetivo actual
    		if (RenderTarget != null)
    		{
        		CreateDXResources(RenderTarget);
    		}
		}
		// Fin Nuevo ingreso 06-01-2026
		
        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            if (Bars == null || ChartBars == null || newsList == null || newsList.Count == 0) return;

            //CreateDXResources(RenderTarget);

            float panelHeight = (float)chartScale.Height;

            foreach (var news in newsList)
            {
                DateTime zoneStart = news.EventTime.AddMinutes(-ZoneMinutes);
                DateTime zoneEnd = news.EventTime.AddMinutes(ZoneMinutes);

                float xTime = (float)chartControl.GetXByTime(news.EventTime);
                float xStart = (float)chartControl.GetXByTime(zoneStart);
                float xEnd = (float)chartControl.GetXByTime(zoneEnd);

                if (xEnd < 0 || xStart > chartControl.ActualWidth) continue;

                // --- Selección de Color ---
                SharpDX.Direct2D1.Brush currentBrush = orangeBrushDX;
                if (news.Impact.Equals("Rojo", StringComparison.OrdinalIgnoreCase)) currentBrush = redBrushDX;
                else if (news.Impact.Equals("Amarilla", StringComparison.OrdinalIgnoreCase)) currentBrush = yellowBrushDX;

                // --- 1. Dibujar Zona (Caja) ---
                float width = xEnd - xStart;
                if (width < 1) width = 1;

                SharpDX.RectangleF rect = new SharpDX.RectangleF(xStart, 0, width, panelHeight);
                
                float originalOpacity = currentBrush.Opacity;
                currentBrush.Opacity = (float)ZoneOpacity / 100f;
                RenderTarget.FillRectangle(rect, currentBrush);
                currentBrush.Opacity = originalOpacity;

                // --- 2. Línea Central ---
                using (var strokeStyle = new StrokeStyle(RenderTarget.Factory, new StrokeStyleProperties 
                { 
                    DashStyle = SharpDX.Direct2D1.DashStyle.Dash 
                }))
                {
                    RenderTarget.DrawLine(new Vector2(xTime, 0), new Vector2(xTime, panelHeight), currentBrush, 1f, strokeStyle);
                }

                // --- 3. Texto Vertical (Ajuste Geométrico) ---
                string text = $"{news.Title.ToUpper()} ({news.Impact.ToUpper()})";
                
                Matrix3x2 oldTransform = RenderTarget.Transform;
                
                // Usamos el Margen configurable por el usuario
                float textY = (float)TextTopMargin; 
                Vector2 pivotPoint = new Vector2(xTime, textY);

                // CAMBIO CLAVE: Rotación positiva (+90 grados = PI/2)
                // Esto hace que el texto se escriba de Arriba hacia Abajo (entra en el gráfico)
                float rotationAngle = (float)(Math.PI / 2.0);
                
                RenderTarget.Transform = Matrix3x2.Rotation(rotationAngle, pivotPoint) * oldTransform;

                // Definimos una caja de texto larga hacia la derecha (que visualmente será hacia abajo)
                // El 'Alignment.Center' en CreateDXResources centra el texto en la altura de la caja (30px), 
                // alineándolo perfectamente con la línea vertical.
                SharpDX.RectangleF textRect = new SharpDX.RectangleF(xTime, textY, 600, 30);
                
                // Pequeño ajuste visual para centrar verticalmente respecto a la línea (ahora eje Y es X)
                // Movemos el rectángulo hacia "arriba" (izquierda visual) mitad de su altura
                textRect.Y -= 15; 

                RenderTarget.DrawText(text, textFormat, textRect, textBrushDX);

                // Restaurar rotación
                RenderTarget.Transform = oldTransform;
				
				//Inicio: Nuevo ingreso 09-01-2026
				
										
						// --- BLOQUE: Reloj de Cuenta Regresiva ---
						// Filtramos la noticia más próxima que esté dentro del rango de prealerta
						DateTime now = DateTime.Now;
						var nextNews = newsList
						    .Where(n => (n.EventTime - now).TotalMinutes > 0 && (n.EventTime - now).TotalMinutes <= AlertMinutesBefore)
						    .OrderBy(n => n.EventTime)
						    .FirstOrDefault();
						
						if (nextNews != null)
						{
						    TimeSpan countdown = nextNews.EventTime - now;
						    string countdownText = $"NEWS: {countdown.Minutes:D2}:{countdown.Seconds:D2}";
						
						    // Posición del reloj (arriba a la derecha)
						    
							float clockWidth = 130f;
    						float clockHeight = 20f;

						    float clockX = (float)chartControl.ActualWidth - clockWidth - 10f; // margen derecho
						    float clockY = 30f; // margen superior
						
						    SharpDX.RectangleF clockRect = new SharpDX.RectangleF(clockX, clockY, clockWidth, clockHeight);
						
						    // Color dinámico según impacto
						    SharpDX.Color bgColor = SharpDX.Color.Black;
						    if (nextNews.Impact.Equals("Rojo", StringComparison.OrdinalIgnoreCase)) bgColor = SharpDX.Color.Red;
						    else if (nextNews.Impact.Equals("Naranja", StringComparison.OrdinalIgnoreCase)) bgColor = SharpDX.Color.Orange;
						    else if (nextNews.Impact.Equals("Amarilla", StringComparison.OrdinalIgnoreCase)) bgColor = SharpDX.Color.Yellow;
						
						    // Fondo semitransparente
						    using (var bgBrush = new SharpDX.Direct2D1.SolidColorBrush(RenderTarget, bgColor))
						    {
						        bgBrush.Opacity = 0.4f;
						        RenderTarget.FillRectangle(clockRect, bgBrush);
						    }
						
						    // Texto centrado dentro del rectángulo
						    using (var layout = new SharpDX.DirectWrite.TextLayout(
						        new SharpDX.DirectWrite.Factory(),
						        countdownText,
						        textFormat,
						        clockWidth,
						        clockHeight))
						    {
						        RenderTarget.DrawTextLayout(new SharpDX.Vector2(clockX + 10, clockY + 1), layout, textBrushDX);
						    }
						}
						
				
				//Fin: Nuevo ingreso 09-01-2026
				
				
            }
        }

        private void CreateDXResources(RenderTarget renderTarget)
        {
            //if (redBrushDX != null && !redBrushDX.IsDisposed) return;

            redBrushDX = new SharpDX.Direct2D1.SolidColorBrush(renderTarget, SharpDX.Color.Red);
            orangeBrushDX = new SharpDX.Direct2D1.SolidColorBrush(renderTarget, SharpDX.Color.Orange);
            yellowBrushDX = new SharpDX.Direct2D1.SolidColorBrush(renderTarget, SharpDX.Color.Yellow);
            
            System.Windows.Media.Color wpfColor = ((System.Windows.Media.SolidColorBrush)TextColor).Color;
            textBrushDX = new SharpDX.Direct2D1.SolidColorBrush(renderTarget, new SharpDX.Color(wpfColor.R, wpfColor.G, wpfColor.B, wpfColor.A));

//            textFormat = new TextFormat(new SharpDX.DirectWrite.Factory(), "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, (float)TextSize);
            
//            // Alineación Izquierda (Start) = Arriba visualmente (donde empieza el texto)
//            textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
            
//            // Alineación Párrafo Centro = Centrado sobre la línea vertical
//            textFormat.ParagraphAlignment = ParagraphAlignment.Center;
			
			// Solo creamos el formato de texto si no existe (el formato de texto NO depende del RenderTarget, así que este sí se puede reusar)
    if (textFormat == null || textFormat.IsDisposed)
    {
        textFormat = new TextFormat(new SharpDX.DirectWrite.Factory(), "Arial", SharpDX.DirectWrite.FontWeight.Bold, SharpDX.DirectWrite.FontStyle.Normal, (float)TextSize);
        textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
        textFormat.ParagraphAlignment = ParagraphAlignment.Center;
			}
        }

        private void DisposeDXResources()
        {
            if (redBrushDX != null) redBrushDX.Dispose();
            if (orangeBrushDX != null) orangeBrushDX.Dispose();
            if (yellowBrushDX != null) yellowBrushDX.Dispose();
            if (textBrushDX != null) textBrushDX.Dispose();
            if (textFormat != null) textFormat.Dispose();
        }

        private void SetupWatcher()
        {
            if (watcher != null) return;
            try 
            {
                string folder = Path.GetDirectoryName(fullPath);
                if (Directory.Exists(folder))
                {
                    watcher = new FileSystemWatcher(folder, FileName);
                    watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName;
                    watcher.Changed += OnFileChanged;
                    watcher.EnableRaisingEvents = true; 
                }
            }
            catch (Exception ex) { Print("EduS Watcher Error: " + ex.Message); }
        }

        private void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            System.Threading.Thread.Sleep(200); 
            LoadNewsData();
            if (ChartControl != null)
                ChartControl.Dispatcher.InvokeAsync(() => { ChartControl.InvalidateVisual(); });
        }

        private void LoadNewsData()
        {
            if (!File.Exists(fullPath)) return;
            List<NewsEvent> tempList = new List<NewsEvent>();
            try
            {
                using (var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#")) continue;
                        var parts = line.Split(';');
                        if (parts.Length < 4) continue;

                        DateTime dt;
                        string dateTimeStr = parts[0] + " " + parts[1]; 
                        
                        if (DateTime.TryParse(dateTimeStr, out dt))
                        {
                            string impact = parts[2].Trim();
                            bool add = false;
                            if (impact.Equals("Rojo", StringComparison.OrdinalIgnoreCase) && ShowHighImpact) add = true;
                            else if (impact.Equals("Naranja", StringComparison.OrdinalIgnoreCase) && ShowMediumImpact) add = true;
                            else if (impact.Equals("Amarilla", StringComparison.OrdinalIgnoreCase) && ShowLowImpact) add = true;

                            if (add)
                            {
                                tempList.Add(new NewsEvent 
                                { 
                                    EventTime = dt, 
                                    Impact = impact, 
                                    Title = parts[3].Trim(),
                                    AlertTriggered = false
                                });
                            }
                        }
                    }
                }
                newsList = tempList;
                Print($"EduS News V4: {tempList.Count} eventos cargados.");
            }
            catch (Exception ex) { Print("EduS Load Error: " + ex.Message); }
        }

        #region Properties
        [NinjaScriptProperty]
        [Display(Name = "Nombre Archivo CSV", Order = 1, GroupName = "Configuración Datos", Description = "Archivo que lee para las News")]
        public string FileName { get; set; }
		
		[NinjaScriptProperty]
        [Display(Name = "Ruta Archivo .py (Ejec. Semanal)", Order = 1, GroupName = "Configuración Datos", Description = "Archivo para ejecutar en Pyton para actualizar News (Noticias.cvs)")]
        public string RutaName { get; set; }

        [NinjaScriptProperty]
        [Range(1, 120)]
        [Display(Name = "Minutos Zona (+/-)", Order = 2, GroupName = "Configuración Zona")]
        public int ZoneMinutes { get; set; }

        [NinjaScriptProperty]
        [Range(1, 60)]
        [Display(Name = "Minutos Pre-Alerta", Order = 4, GroupName = "Configuración Alerta")]
        public int AlertMinutesBefore { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Impacto Rojo", Order = 5, GroupName = "Filtros")]
        public bool ShowHighImpact { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Mostrar Impacto Naranja", Order = 6, GroupName = "Filtros")]
        public bool ShowMediumImpact { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "Mostrar Impacto Amarillo", Order = 7, GroupName = "Filtros")]
        public bool ShowLowImpact { get; set; }
        
        [Range(1, 100)]
        [Display(Name = "Opacidad Zona %", GroupName = "Visual")]
        public int ZoneOpacity { get; set; }
        
        [XmlIgnore]
        [Display(Name = "Color Texto", GroupName = "Visual")]
        public System.Windows.Media.Brush TextColor { get; set; }

        [Range(8, 40)]
        [Display(Name = "Tamaño Fuente", GroupName = "Visual")]
        public int TextSize { get; set; }

        [Range(0, 500)]
        [Display(Name = "Margen Superior (Píxeles)", Description = "Distancia desde el techo para iniciar el texto", GroupName = "Visual")]
        public int TextTopMargin { get; set; }
        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private EduS_Trader.EduS_News_Pro_Render_V4[] cacheEduS_News_Pro_Render_V4;
		public EduS_Trader.EduS_News_Pro_Render_V4 EduS_News_Pro_Render_V4(string fileName, string rutaName, int zoneMinutes, int alertMinutesBefore, bool showHighImpact, bool showMediumImpact, bool showLowImpact)
		{
			return EduS_News_Pro_Render_V4(Input, fileName, rutaName, zoneMinutes, alertMinutesBefore, showHighImpact, showMediumImpact, showLowImpact);
		}

		public EduS_Trader.EduS_News_Pro_Render_V4 EduS_News_Pro_Render_V4(ISeries<double> input, string fileName, string rutaName, int zoneMinutes, int alertMinutesBefore, bool showHighImpact, bool showMediumImpact, bool showLowImpact)
		{
			if (cacheEduS_News_Pro_Render_V4 != null)
				for (int idx = 0; idx < cacheEduS_News_Pro_Render_V4.Length; idx++)
					if (cacheEduS_News_Pro_Render_V4[idx] != null && cacheEduS_News_Pro_Render_V4[idx].FileName == fileName && cacheEduS_News_Pro_Render_V4[idx].RutaName == rutaName && cacheEduS_News_Pro_Render_V4[idx].ZoneMinutes == zoneMinutes && cacheEduS_News_Pro_Render_V4[idx].AlertMinutesBefore == alertMinutesBefore && cacheEduS_News_Pro_Render_V4[idx].ShowHighImpact == showHighImpact && cacheEduS_News_Pro_Render_V4[idx].ShowMediumImpact == showMediumImpact && cacheEduS_News_Pro_Render_V4[idx].ShowLowImpact == showLowImpact && cacheEduS_News_Pro_Render_V4[idx].EqualsInput(input))
						return cacheEduS_News_Pro_Render_V4[idx];
			return CacheIndicator<EduS_Trader.EduS_News_Pro_Render_V4>(new EduS_Trader.EduS_News_Pro_Render_V4(){ FileName = fileName, RutaName = rutaName, ZoneMinutes = zoneMinutes, AlertMinutesBefore = alertMinutesBefore, ShowHighImpact = showHighImpact, ShowMediumImpact = showMediumImpact, ShowLowImpact = showLowImpact }, input, ref cacheEduS_News_Pro_Render_V4);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.EduS_Trader.EduS_News_Pro_Render_V4 EduS_News_Pro_Render_V4(string fileName, string rutaName, int zoneMinutes, int alertMinutesBefore, bool showHighImpact, bool showMediumImpact, bool showLowImpact)
		{
			return indicator.EduS_News_Pro_Render_V4(Input, fileName, rutaName, zoneMinutes, alertMinutesBefore, showHighImpact, showMediumImpact, showLowImpact);
		}

		public Indicators.EduS_Trader.EduS_News_Pro_Render_V4 EduS_News_Pro_Render_V4(ISeries<double> input , string fileName, string rutaName, int zoneMinutes, int alertMinutesBefore, bool showHighImpact, bool showMediumImpact, bool showLowImpact)
		{
			return indicator.EduS_News_Pro_Render_V4(input, fileName, rutaName, zoneMinutes, alertMinutesBefore, showHighImpact, showMediumImpact, showLowImpact);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.EduS_Trader.EduS_News_Pro_Render_V4 EduS_News_Pro_Render_V4(string fileName, string rutaName, int zoneMinutes, int alertMinutesBefore, bool showHighImpact, bool showMediumImpact, bool showLowImpact)
		{
			return indicator.EduS_News_Pro_Render_V4(Input, fileName, rutaName, zoneMinutes, alertMinutesBefore, showHighImpact, showMediumImpact, showLowImpact);
		}

		public Indicators.EduS_Trader.EduS_News_Pro_Render_V4 EduS_News_Pro_Render_V4(ISeries<double> input , string fileName, string rutaName, int zoneMinutes, int alertMinutesBefore, bool showHighImpact, bool showMediumImpact, bool showLowImpact)
		{
			return indicator.EduS_News_Pro_Render_V4(input, fileName, rutaName, zoneMinutes, alertMinutesBefore, showHighImpact, showMediumImpact, showLowImpact);
		}
	}
}

#endregion
