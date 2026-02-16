# EduS-Traiding-Tools-NT8-enUSO
Repositorio de indicadores avanzados y estrategias automáticas para NT8


# 📊 NinjaTrader 8 - EduS Trading Ecosystem
### *Sistemas Avanzados para el Análisis de Subasta y Flujo de Órdenes*

Bienvenido al repositorio oficial de herramientas de **EduS Day Trading**. Este ecosistema está diseñado para traders profesionales que operan derivados del CME (ES, NQ, MES, MNQ) utilizando metodologías de **Market Profile, Wyckoff y Contexto Institucional**.

---

## 🛠️ Estructura del Ecosistema

El proyecto se divide en tres pilares fundamentales que integran el análisis macro y la ejecución técnica:

### 1. 📂 Indicadores de Contexto de Mercado (`/Indicators/Market-Context`)
* **EduS Unified Market Context:** Centraliza niveles clave de la sesión RTH y Globex. Incluye visualización de aperturas, rangos iniciales (ORB) y máximos/mínimos históricos.
* **EduS Institutional Gap Pro:** Analiza los desequilibrios (Gaps) de apertura, filtrando por volatilidad (VIX) para determinar probabilidades de "Gap & Go" o reversión.
* **EduS News Pro Render V4:** El motor visual que proyecta eventos macroeconómicos directamente en el precio. Utiliza `SharpDX` para un renderizado de alto rendimiento.

### 2. 📂 Nodos de Volumen y Subasta (`/Indicators/Order-Flow-Nodes`)
* **EduS Trader Nodes V8 (Institutional):** Identifica nodos de alto/bajo volumen (HVN/LVN) y zonas de "Anti-POC" para detectar puntos de absorción institucional.
* **EduS Dynamic Swing AVWAP:** Implementación avanzada del VWAP anclado a pivots técnicos (Swings), permitiendo identificar el precio justo de la subasta en tendencias en desarrollo.
* **EduS Institutional Naked POCs:** Traza niveles de control de volumen no testeados, actuando como imanes magnéticos para el precio en sesiones de alta convicción.

### 3. 📂 Señales y Gestión de Riesgo (`/Indicators/Signals-and-Patterns`)
* **EduS Master Indicator:** Un panel de control integral que combina canales de Keltner adaptativos, MACD con Bandas de Bollinger y métricas de volatilidad ATR.
* **EduS MTF Doji Zones:** Detector de patrones de indecisión en múltiples marcos temporales (Multi-Timeframe), señalando posibles giros estructurales.
* **EduS Keltner StopTarget:** Herramienta matemática de gestión que propone niveles de Stop Loss y Take Profit basados en la desviación estándar de la volatilidad actual.

---

## 🐍 Módulo de Sincronización de Noticias (Python)
Ubicado en `/Data-Fetchers/News-Sync/`

Este sistema automatiza la extracción de datos fundamentales para que el trader nunca sea sorprendido por la volatilidad macro.
- **Script:** `EduS_News_Sync.py`
- **Fuente:** ForexFactory (via CloudScraper).
- **Funcionalidad:** Consolida noticias concurrentes para evitar el ruido visual y genera un archivo `Noticias.csv` que NinjaTrader lee en tiempo real mediante un `FileSystemWatcher`.

---

## 🚀 Instalación y Uso

### NinjaTrader 8:
1. Descargue los archivos `.cs` de las carpetas de indicadores.
2. Cópielos en su ruta local: `Documentos\NinjaTrader 8\bin\Custom\Indicators`.
3. Abra NinjaTrader y compile (F5 dentro del editor de scripts).

### Python News Sync:
1. Instale las dependencias necesarias:
   ```bash
   pip install cloudscraper beautifulsoup4



2. Ejecute el script antes de su sesión de trading:
   ```bash
   python EduS_News_Sync.py


----

## ⚖️ Descargo de Responsabilidad (Disclaimer)

El contenido de este repositorio tiene fines puramente educativos y de herramientas de análisis. El trading de futuros y derivados conlleva un riesgo significativo. **El rendimiento pasado no garantiza resultados futuros.** No constituye asesoramiento financiero.

---

**Desarrollado por EduS Day Trading** *Analista de Mercados con +20 años de experiencia.*

---

### ¿Qué sigue ahora?
Una vez que pegue y guarde el archivo:
1.  Abra **GitHub Desktop**.
2.  Verá el cambio en el `README.md`.
3.  Escriba en el Summary: **"Finalización de documentación profesional del repositorio"**.
4.  Haga clic en **Commit to main** y luego en **Push origin**.
