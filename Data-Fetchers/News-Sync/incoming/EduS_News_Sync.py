import cloudscraper
from bs4 import BeautifulSoup
from datetime import datetime, timedelta
import os

# ==============================================================================
# CONFIGURACIÓN DE USUARIO
# ==============================================================================

# RUTA AUTOMÁTICA
documents_path = os.path.join(os.path.expanduser('~'), 'Documents')
if os.path.exists(os.path.join(documents_path, 'NinjaTrader 8')):
    OUTPUT_PATH = os.path.join(documents_path, 'NinjaTrader 8', 'Incoming', 'Noticias.csv')
else:
    onedrive_path = os.path.join(os.path.expanduser('~'), 'OneDrive - Desarrollo Personal', 'Documents') 
    OUTPUT_PATH = os.path.join(onedrive_path, 'NinjaTrader 8', 'Incoming', 'Noticias.csv')

# URL Y AÑO
TARGET_URL = 'https://www.forexfactory.com/calendar?month=this'
SIMULATION_YEAR = "2026"
AJUSTE_HORARIO = -2  # Cambiar a -5 si ve las horas adelantadas (tipo Londres)

# ==============================================================================
# LOGICA V6: CONSOLIDACIÓN Y LIMPIEZA
# ==============================================================================

def get_ff_data_consolidated(url):
    print(f"--- Iniciando V6 (Consolidación de Eventos) ---")
    
    scraper = cloudscraper.create_scraper()
    try:
        response = scraper.get(url)
        if response.status_code != 200:
            print(f"Error {response.status_code}")
            return None
    except Exception as e:
        print(f"Error crítico: {e}")
        return None

    soup = BeautifulSoup(response.content, 'html.parser')
    table = soup.find('table', class_='calendar__table')
    if not table: return None

    # Diccionario para agrupar eventos: Key = "YYYY-MM-DD HH:MM"
    # Value = { 'impact_score': 3, 'impact_str': 'Rojo', 'titles': [] }
    grouped_events = {}
    
    # Lista para mantener el orden cronológico de las claves
    ordered_keys = []

    last_known_date = ""
    last_known_time_str = "" 

    rows = table.find_all('tr')
    print("Analizando y agrupando noticias...")

    for row in rows:
        if not row.get('class') or 'calendar__row' not in row.get('class'): continue
        
        # 1. FECHA
        date_cell = row.find('td', class_='calendar__date')
        if date_cell:
            text_date = date_cell.get_text().strip()
            if text_date:
                last_known_date = f"{text_date} {SIMULATION_YEAR}"
                last_known_time_str = "" 
        if not last_known_date: continue

        # 2. HORA (Memoria)
        time_cell = row.find('td', class_='calendar__time')
        current_time_str = time_cell.get_text().strip() if time_cell else ""
        
        if current_time_str and "Day" not in current_time_str:
            last_known_time_str = current_time_str
        
        if not last_known_time_str: continue # Si no hay hora, saltar

        # 3. FILTRO USD
        currency_cell = row.find('td', class_='calendar__currency')
        currency = currency_cell.get_text().strip() if currency_cell else ""
        if currency != 'USD': continue 

        # 4. DETERMINAR IMPACTO (Numérico para jerarquía)
        impact_cell = row.find('td', class_='calendar__impact')
        impact_val = 0
        impact_name = "Amarilla"
        
        if impact_cell:
            span = impact_cell.find('span')
            if span:
                className = span.get('class', [])
                if 'icon--ff-impact-red' in className: 
                    impact_val = 3
                    impact_name = "Rojo"
                elif 'icon--ff-impact-ora' in className: 
                    impact_val = 2
                    impact_name = "Naranja"
                elif 'icon--ff-impact-yel' in className: 
                    impact_val = 1
                    impact_name = "Amarilla"
                else: continue 

        # 5. TITULO
        event_cell = row.find('td', class_='calendar__event')
        title = event_cell.get_text().strip() if event_cell else "News"

        # 6. CALCULO DE TIMESTAMP PARA AGRUPAR
        try:
            full_date_str = f"{last_known_date} {last_known_time_str}"
            try:
                dt_obj = datetime.strptime(full_date_str, "%a %b %d %Y %I:%M%p")
            except ValueError:
                dt_obj = datetime.strptime(full_date_str, "%b %d %Y %I:%M%p")

            # Ajuste horario
            dt_adjusted = dt_obj + timedelta(hours=AJUSTE_HORARIO)
            
            # CLAVE ÚNICA DE AGRUPACIÓN (Fecha + Hora)
            fmt_date = dt_adjusted.strftime("%Y-%m-%d")
            fmt_time = dt_adjusted.strftime("%H:%M")
            unique_key = f"{fmt_date};{fmt_time}"

            # --- LOGICA DE CONSOLIDACIÓN ---
            if unique_key in grouped_events:
                # Ya existe una noticia a esta hora, agregamos datos
                entry = grouped_events[unique_key]
                entry['titles'].append(f"{title} ({impact_name})") # Agregamos título + impacto individual
                
                # Si la nueva noticia es más grave, actualizamos el color dominante
                if impact_val > entry['max_impact_val']:
                    entry['max_impact_val'] = impact_val
                    entry['dominant_impact'] = impact_name
            else:
                # Nueva hora, crear entrada
                grouped_events[unique_key] = {
                    'max_impact_val': impact_val,
                    'dominant_impact': impact_name,
                    'titles': [f"{title} ({impact_name})"]
                }
                ordered_keys.append(unique_key)

        except Exception as e:
            continue

    # 7. GENERAR LISTA FINAL DE CSV
    final_csv_lines = []
    for key in ordered_keys:
        item = grouped_events[key]
        
        # Concatenamos los títulos con un " + "
        combined_titles = " + ".join(item['titles'])
        
        # Construimos la línea: Fecha;Hora;ImpactoDominante;TitulosUnidos
        line = f"{key};{item['dominant_impact']};{combined_titles}"
        final_csv_lines.append(line)
        
        print(f" -> {key} | {item['dominant_impact']} | {combined_titles}")

    return final_csv_lines

def save_csv(data_list, path):
    if not data_list:
        print("\n[ALERTA] No hay datos.")
        return
    try:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, 'w', encoding='utf-8') as f:
            f.write("# Auto-Generated by EduS V6 (Consolidated)\n")
            for line in data_list:
                f.write(line + "\n")
        print(f"\n[EXITO] Noticias consolidadas guardadas en:\n{path}")
    except Exception as e:
        print(f"Error guardando: {e}")

if __name__ == "__main__":
    data = get_ff_data_consolidated(TARGET_URL)
    save_csv(data, OUTPUT_PATH)