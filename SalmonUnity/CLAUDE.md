# SalmonUnity — Guía para Claude Code

Proyecto Unity que lee `data/trajectories.csv` y anima salmones 3D en una jaula virtual cilíndrica.

## Comandos esenciales con `unity command`

### Compilar y verificar errores
```bash
unity command recompile
```
Respuesta esperada: `up_to_date: No scripts needed recompilation.`
Si hay errores de compilación aparecen en la respuesta y en la consola.

### Leer la consola del Editor
```bash
unity command console
# Solo el resultado como JSON (para parsear con PowerShell/Python):
unity command console --result-only --json
```
Filtrar entradas relevantes (PowerShell):
```powershell
$result = unity command console --result-only --json | Out-String | ConvertFrom-Json
$result.entries | Where-Object { $_.message -notmatch "InvalidCastException|InputSystem" } |
    Select-Object seq, level, message | Format-Table -Wrap
```
Los errores de `InvalidCastException` en `InputSystem` son ruido interno de Unity al correr en CLI; no afectan la simulación.

### Entrar y salir de Play mode
```bash
unity command editor_play    # Entra en Play mode
unity command editor_stop    # Sale de Play mode (solo si está en Play)
```

### Tomar captura de pantalla
La forma más confiable es capturar en base64 y decodificar con PowerShell:
```powershell
$result = unity command capture_game_view --result-only --json | Out-String | ConvertFrom-Json
$b64 = $result.base64
[System.IO.File]::WriteAllBytes("C:\ruta\captura.png", [System.Convert]::FromBase64String($b64))
```
También existe `capture_scene_view` para capturar la vista de la escena en modo Editor.

### Listar todos los comandos disponibles
```bash
unity command          # lista completa
unity list             # ídem
unity command --tag editor   # filtrar por categoría
```

---

## Scripts en Assets/Scripts/

### `TrajectoryPlayer.cs`
Componente central. Lee `StreamingAssets/trajectories.csv` y crea un GameObject por cada `id` de pez encontrado en el CSV. En cada `Update()` interpola la posición del pez en el tiempo actual y lo mueve suavemente. Si hay un hueco > 1 s entre dos filas consecutivas del mismo pez, lo oculta (oclusión simulada). Expone `CurrentTime`, `VisibleCount` y `Duration` para el HUD.

**Inspector:** `csvFile`, `fps`, `cageWidth/Height/Depth` (metros de la jaula virtual), `fishPrefab` (si es null usa `FishFactory`), `turnSpeed`, `playing`, `speed`.

### `SalmonSceneBuilder.cs`
Clase estática con `[RuntimeInitializeOnLoadMethod]`; se ejecuta automáticamente al dar Play sin necesidad de colocarlo en la escena. Arma el entorno completo:
- Niebla exponencial azul-verdosa (efecto agua subacuática).
- Ajusta la luz direccional existente.
- Construye la red de la jaula cilíndrica con `LineRenderer`s (5 anillos + 28 líneas verticales).
- Añade `CameraOrbit` a la cámara principal y `SalmonHud` al objeto de la jaula.

También contiene `FishFactory`: construye un salmón procesal con cuerpo (esfera achatada), cola (pivot + cubo para aleteo) y aleta dorsal (cubo inclinado). Añade `FishWag` automáticamente.

### `FishWag.cs`
Anima la cola del salmón. En cada `Update()` aplica una rotación senoidal al `TailPivot` con frecuencia (~6.5 Hz) y amplitud (~28°) aleatorias por pez.

### `CameraOrbit.cs`
Orbita la cámara alrededor de `target` a velocidad constante (`degPerSec = 6`). No requiere input del usuario; funciona solo en modo de demo.

### `SalmonHud.cs`
HUD IMGUI (sin Canvas). Muestra en la esquina superior izquierda:
- `Salmones visibles: N` — peces activos en el frame actual.
- `Tiempo: T / D s` — tiempo de reproducción y duración total.

---

## Datos de entrada

El CSV debe estar en `Assets/StreamingAssets/trajectories.csv` con las columnas:
`frame, t, id, cx, cy, w, h, conf, z`
(ver contrato completo en `../CLAUDE.md`).

Las coordenadas `cx`, `cy` (0–1) y `z` (0–1) se mapean a metros de la jaula virtual según `cageWidth`, `cageHeight`, `cageDepth` del `TrajectoryPlayer`.

## Flujo de prueba estándar

```bash
unity command recompile               # 1. confirmar sin errores
unity command editor_play             # 2. entrar en Play
# (en PowerShell) capturar screenshot
unity command console --result-only --json   # 3. leer consola
unity command editor_stop             # 4. salir de Play
```
