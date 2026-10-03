# vision/ — Detección y tracking

Objetivo: convertir un video submarino en `../data/trajectories.csv` y `../data/meta.json`
siguiendo exactamente el contrato del CLAUDE.md raíz.

## Stack sugerido
- Python 3.11, `ultralytics` (YOLO + ByteTrack integrado con `model.track(..., tracker="bytetrack.yaml")`)
- `opencv-python` para leer video, `pandas` para el CSV
- Entrenamiento/fine-tuning en Google Colab (GPU). Aquí solo inferencia.

## Pasos
1. `track.py`: video → CSV con cx, cy, w, h normalizados + id + conf.
2. Profundidad `z`: primera versión por tamaño aparente
   (`z = 1 - normalizar(h)` usando la mediana por id para suavizar).
   Mejora opcional: Depth Anything.
3. `metrics.py`: desde el CSV calcula por frame:
   conteo visible, velocidad media, polarización del cardumen, mapa de calor.

## Reglas
- No leer `../simulation/`. Solo importa el contrato de datos.
- Probar siempre con un clip corto (10–20 s) antes de procesar videos largos.
- Validar el CSV de salida contra el esquema antes de darlo por terminado.
