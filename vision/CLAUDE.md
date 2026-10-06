# vision/ — Detección y tracking

Objetivo: convertir un video submarino en `../data/trajectories.csv` y `../data/meta.json`
siguiendo exactamente el contrato del CLAUDE.md raíz.

## Dónde vive
El pipeline corre en Google Colab (GPU). El notebook se guardará en este repo como
`vision/salmon_vision.ipynb` (Colab: Archivo → Descargar → Descargar .ipynb).
Videos y datasets no se suben a git (`vision/videos/`, `vision/datasets/` están en `.gitignore`).

## Pipeline usado (datos actuales)
1. **Video:** tramo 1:30–2:00 de "Underwater Salmon Cam – Katmai 2022" (cámara fija en
   un río), recortado a `katmai_30s.mp4` (30 s, 1280×720, 29,97 fps).
2. **Detección:** YOLOE `yoloe-11l-seg` (ultralytics) con prompt de texto `"fish"`,
   `imgsz=1280`, `conf=0.25`.
3. **Tracking:** ByteTrack con `track_buffer=60` (aguanta oclusiones de ~2 s).
4. **Filtro:** se descartan IDs que duran menos de 1 s (detecciones espurias).
5. **Profundidad `z`:** por tamaño aparente de la caja, con la mediana por ID
   (pez más grande = más cerca de la cámara; un valor fijo por pez).
6. **Suavizado:** media móvil de 7 frames.
7. **Salida:** CSV con el contrato + `meta.json`.

**Resultado:** 56 IDs, ~9 peces visibles por frame.

## Pendiente
- Métricas en Python a partir del CSV: conteo visible, velocidad media,
  polarización, mapa de calor.
- Probar con video de una salmonera real (el actual es un río: los peces se
  mantienen quietos contra la corriente).
- Profundidad mejor que el tamaño aparente (p. ej. Depth Anything).

## Reglas
- No leer `../SalmonUnity/`. Solo importa el contrato de datos.
- Probar siempre con un clip corto (10–20 s) antes de procesar videos largos.
- Validar el CSV de salida contra el esquema antes de darlo por terminado.
- Al regenerar `data/trajectories.csv`, copiarlo también a
  `SalmonUnity/Assets/StreamingAssets/trajectories.csv` (Unity lee esa copia).
