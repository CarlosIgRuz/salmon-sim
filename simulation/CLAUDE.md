# simulation/ — Escena 3D en Unity

Objetivo: leer `trajectories.csv` y animar un salmón 3D por cada `id` dentro de
una jaula virtual, con el movimiento sincronizado al video original.

## Setup
- Unity 2022.3 LTS o más nuevo. La ruta del proyecto NO debe tener espacios.
- Conectado a Claude Code vía Unity MCP (ver README raíz).
- Copiar `../data/trajectories.csv` a `Assets/StreamingAssets/` (o usar el botón/ruta del script).

## Ya existe
- `Assets/Scripts/TrajectoryPlayer.cs`: lee el CSV, crea un pez por id,
  interpola posiciones entre frames y orienta cada pez según su dirección.

## Pendiente (en orden)
1. Escena base: jaula cilíndrica (red semitransparente), agua con niebla azul, luz desde arriba.
2. Prefab de salmón (modelo low-poly; si no hay, una cápsula estirada sirve para empezar).
3. Animación de nado (ondulación de la cola con shader o rotación simple).
4. UI: contador de peces visibles, velocidad media, play/pausa, línea de tiempo.
5. Cámara orbital para la demo.

## Reglas
- No leer `../vision/`. Solo importa el contrato de datos.
- Después de cada cambio de scripts, verificar que compile vía MCP antes de seguir.
