# Salmon Sim — Feria de Ciencia de Datos

Proyecto: a partir de video submarino de una jaula de salmones, detectar y seguir
cada pez, calcular métricas y recrear el movimiento en una escena 3D en Unity.

## Estructura
- `vision/`      → Python: video → detección (YOLO) → tracking (ByteTrack) → CSV
- `simulation/`  → Unity: lee el CSV y anima salmones 3D en una jaula virtual
- `data/`        → el contrato entre ambas partes (`trajectories.csv` + `meta.json`)
- `tools/`       → utilidades (generador de trayectorias simuladas)

## Regla principal
Las dos partes se comunican SOLO a través de `data/`. Un agente trabajando en
`vision/` no necesita leer `simulation/` y viceversa. No lo hagas: gasta contexto.

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

`data/meta.json` — `fps`, `width`, `height` (px del video), `source` ("synthetic" o nombre del video).

Si un pez no se detecta en un frame (oclusión), simplemente no hay fila: el
consumidor debe interpolar.

## Plan (2 semanas)
1. Semana 1: `vision/` produce el CSV real y las métricas.
2. Semana 2: `simulation/` en Unity + dashboard + demo grabada como plan B.
