# Salmon Sim — Feria de Ciencia de Datos

Proyecto: a partir de video submarino de salmones, detectar y seguir cada pez,
calcular métricas y recrear el movimiento en una salmonera 3D en Unity, donde
una jaula muestra los datos reales y las demás una simulación de cardumen.

## Estructura
- `SalmonUnity/`   → proyecto Unity 6 (URP). Escena procedural: salmonera completa
                     (ver `SalmonUnity/CLAUDE.md`)
- `vision/`        → pipeline de visión: video → detección → tracking → CSV.
                     Hoy vive en un notebook de Colab (`vision/salmon_vision.ipynb`)
- `data/`          → el contrato entre ambas partes (`trajectories.csv` + `meta.json`)
- `tools/`         → utilidades (generador de trayectorias simuladas)
- `docs/capturas/` → capturas de cada fase, para la feria

## Regla principal
Las dos partes se comunican SOLO a través de `data/`. Un agente trabajando en
`vision/` no necesita leer `SalmonUnity/` y viceversa. No lo hagas: gasta contexto.
Unity lee una copia del CSV en `SalmonUnity/Assets/StreamingAssets/trajectories.csv`:
al regenerar `data/trajectories.csv`, cópialo ahí.

## Contrato de datos (no cambiar sin actualizar ambos lados)

`data/trajectories.csv` — una fila por pez por frame:

| columna | tipo  | significado |
|---------|-------|-------------|
| frame   | int   | número de frame (desde 0) |
| t       | float | tiempo en segundos |
| id      | int   | ID del pez asignado por el tracker |
| cx, cy  | float | centro de la caja, normalizado 0–1 (0,0 = arriba-izquierda de la imagen) |
| w, h    | float | ancho y alto de la caja, normalizados 0–1 |
| conf    | float | confianza de la detección 0–1 |
| z       | float | profundidad estimada normalizada 0–1 (0 = pegado a la cámara, 1 = fondo visible) |

`data/meta.json` — `fps`, `width`, `height` (px del video), `frames`, `source` ("synthetic" o nombre del video).

Si un pez no se detecta en un frame (oclusión), simplemente no hay fila: el
consumidor debe interpolar.

## Pipeline de visión usado (datos actuales de `data/`)
- **Video:** tramo 1:30–2:00 de "Underwater Salmon Cam – Katmai 2022" (cámara fija
  en un río; 30 s, 1280×720, 29,97 fps → `source: katmai_30s.mp4`).
- **Detección:** YOLOE `yoloe-11l-seg` con prompt de texto `"fish"`, `imgsz=1280`, `conf=0.25`.
- **Tracking:** ByteTrack con `track_buffer=60`.
- **Limpieza:** se descartan IDs que duran menos de 1 s.
- **Profundidad `z`:** por tamaño aparente, usando la mediana por ID.
- **Suavizado:** media móvil de 7 frames.
- **Resultado:** 56 IDs, ~9 peces visibles por frame.

## Escena en Unity (estado actual)
- Salmonera de 2×4 jaulas cuadradas unidas por pasillos, con pontón central, en un
  lago con montañas. Navegación: clic en una jaula → la cámara vuela bajo el agua;
  "← Volver" / Esc regresa.
- **Jaula 1 = datos reales** (el CSV del video). Las métricas se calculan en la escala
  estimada del video, no en la del dibujo.
- **Jaulas 2–8 = simulación** (boids): cardumen en anillo, con selectores de etapa
  (smolt / engorda / precosecha), estación (verano / invierno) y hora (día / noche).
  Botón "ⓘ Supuestos" con cada supuesto, su valor y su fuente (o "supuesto sin fuente").
- Panel de salmones con resumen (velocidad, profundidad, polarización, orden de
  rotación, concentración) y selección de peces.

## Estado

**Hecho**
- Pipeline de visión sobre el video de Katmai → `data/trajectories.csv`.
- Fases 1–4 en Unity: escena de la salmonera, navegación vista general ↔ jaula,
  cardumen simulado, métricas corregidas, selectores y ventana de supuestos.
  Capturas en `docs/capturas/`.

**Pendiente**
- Fase 5: alimentación (más allá de los pellets actuales).
- Probar el pipeline con video de una salmonera real (no de río).
- Métricas en Python (`vision/`): conteo, velocidad, polarización, mapa de calor.
- Demo grabada como plan B.
- Presentación para la feria.

## Utilidades
- `tools/generate_fake_trajectories.py --fish 40 --seconds 30 --out <carpeta>`: genera
  trayectorias sintéticas con el mismo contrato. **Sin `--out` escribe en `data/` y
  pisa los datos reales**; usa por ejemplo `--out data/synthetic`.
