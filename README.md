# Salmon Sim

Video submarino de salmones → detección y tracking → métricas → simulación 3D en Unity.

## Arranque rápido

```bash
# 1. Generar trayectorias de prueba (ya vienen generadas en data/)
python tools/generate_fake_trajectories.py --fish 40 --seconds 30

# 2. Copiarlas a Unity
cp data/trajectories.csv simulation/Assets/StreamingAssets/
```

## Unity

1. Crea un proyecto Unity 2022.3+ (3D, URP) **en una ruta sin espacios** y copia
   dentro la carpeta `simulation/Assets`.
2. Crea un GameObject vacío `Cage`, agrégale `TrajectoryPlayer` y dale Play.
   Sin prefab asignado usa cápsulas anaranjadas como salmones provisorios.

## Conectar Claude Code a Unity (MCP)

Opción recomendada para partir: Unity-MCP de Ivan Murzak.

1. En Unity: Package Manager → *Add package from git URL*:
   `https://github.com/IvanMurzak/Unity-MCP.git?path=/Unity-MCP-Plugin/Assets/root`
2. Abre la ventana del plugin, elige **Claude Code** y pulsa *Configure*.
3. Con Unity abierto, abre Claude Code en la carpeta del proyecto Unity y
   verifica con `/mcp` que aparezcan las herramientas.

Alternativa: el MCP oficial de Unity (paquete AI Assistant, *Project Settings → AI → Unity MCP*).

## Flujo con dos agentes

| Terminal | Carpeta | Tarea |
|----------|---------|-------|
| Agente 1 | `vision/` | YOLO + ByteTrack → `data/trajectories.csv` |
| Agente 2 | proyecto Unity | escena, jaula, prefab, UI (vía Unity MCP) |

Cada carpeta tiene su propio `CLAUDE.md`. Solo comparten `data/`.

## Experimento de tokens (opcional)

Instala un MCP de grafo de código (p. ej. `tokensave`) en **una** copia del repo,
haz la misma pregunta con y sin él, y compara con `/context`. Anota resultados en
`experimentos.md` — sirve como slide para la feria.
