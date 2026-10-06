# SalmonUnity — Guía para Claude Code

Proyecto Unity que arma una salmonera completa (lago, montañas, grilla de jaulas, pontón).
La "Jaula 1" anima salmones a partir de `data/trajectories.csv` (datos reales); las demás
tienen un cardumen simulado con boids (`FishSchool`).

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

Las trayectorias se guardan en la escala estimada del video (`videoWidth/Height/Depth`, 10×5×10 m) y **todas las métricas se calculan ahí** (velocidad, umbral `moveSpeedThreshold`, polarización y rotación con las velocidades del CSV, nunca con la orientación dibujada). `cageWidth/Height/Depth` solo es el volumen de dibujo. Bajo el umbral la velocidad se muestra "≈ 0 (en el lugar)"; si la mayoría está bajo el umbral, polarización y rotación son n/d.

**Inspector:** `csvFile`, `fps`, `videoWidth/Height/Depth`, `cageWidth/Height/Depth` (dibujo; `SalmonFarmBuilder` los sobrescribe para llenar la red menos `dataMargin`), `fishPrefab` (si es null usa `FishFactory`), `fishScale`, `turnSpeed`, `playing`, `speed`. Implementa `IFishSource` para el panel.

### `FishSchool.cs` + `BehaviorProfile.cs`
Cardumen simulado (boids) en coordenadas locales de la jaula (superficie y=0, red |x|,|z| ≤ `halfSize`, fondo −`netDepth`). Reglas: separación, alineación, cohesión, evitar la red (empuje suave + límite duro `hardMargin`: nunca la cruzan), profundidad preferida y giro en anillo (`clockwise`). Vecinos con grilla espacial (counting sort). Se dibuja con `Graphics.RenderMeshInstanced` (malla `FishFactory.SharedMesh()` + shader `SalmonSim/FishInstanced`, que hace el aleteo); sin GameObjects ni colliders por pez (el clic usa un test rayo-esfera).
- `profile` (`BehaviorProfile`, serializable): nº de peces, largo, velocidad media y variación, aceleración máxima, radios y pesos de cada regla, profundidad preferida y dispersión, peso y radio del anillo. Todo continuo: `BehaviorProfile.Lerp` y `FishSchool.BlendTo(perfil, segundos)` cambian de perfil suavemente. `BehaviorProfileAsset` (ScriptableObject) sirve para guardar presets.
- `wander` agrega deambular aleatorio (cardumen menos ordenado de noche).
- **Alimentación por comidas** (`MealStats Meal`): automáticas cada `mealInterval` (180 s) durante `mealSeconds` (40 s) si `autoFeed` (lo fija `FarmConditions`: de día y si la etapa se alimenta), o con `FeedNow()` ("Alimentar ahora"). La ración es `rationPerFish` (3) pellets por pez; el esparcidor los lanza en un anillo `spreadInner`–`spreadOuter` (1–4,5 m) y gira (`spreaderRotor`). Los peces con hambre (`hungerU < appetite`) suben bajo el esparcidor a `frenzyDepth` (frenesí: ×`frenzySpeed`, sin capa ni anillo), comen hasta `satiation` (3,5) y vuelven a su capa. Los pellets caen a `pelletSinkSpeed` 0,095 m/s ±`pelletSinkVariation` 10 % (NewDEPOMOD, SEPA), los arrastra `CurrentField` y, si salen por la red, cuentan como perdidos. `Meal.Unconsumed` = perdidos / lanzados; `LastMeal` guarda la última comida resuelta mientras corre la siguiente. Los pellets solo se simulan en la jaula abierta (`partial` si la comida empezó con la jaula cerrada); comer usa la grilla de vecinos.
- `fullQuality`: solo la jaula abierta simula todos los peces; en la vista general cada jaula simula `liteFishCount` (40).

### `FarmConditions.cs`
Etapa (Smolt / Engorda / Precosecha), estación (Verano / Invierno), hora (Día / Noche) y corriente (Débil / Media / Fuerte → `CurrentField.SetStrength`; botón "Ver corriente" activa `CurrentViz`). La barra achica su letra si no cabe en la pantalla. `BuildProfile(cage)` traduce la combinación a un `BehaviorProfile` (tamaño por peso, velocidad en BL/s limitada a 0,4–1,0, nº de peces de muestra, profundidad por termorregulación + desplazamiento día/noche según estación, estructura del cardumen, alimentación) y `Apply` lo aplica con `FishSchool.BlendTo` (`blendSeconds` = 3 s) y cambia la luz (`SalmonFarmBuilder.SetNight`; la Jaula 1 solo cambia la luz). Dibuja la barra superior de selectores (`FarmUi.TopInset` reserva su altura), la ventana "ⓘ Supuestos" (`Assumptions()` se arma con las mismas constantes del modelo; lo que no tiene referencia se marca "supuesto sin fuente") y el widget de perfil térmico.
**Pruebas por código:** `FindFirstObjectByType<FarmConditions>().Set(Stage.X, Season.Y, TimeOfDay.Z)` y `.SetCurrent(CurrentStrength.X)`; esperar ~10–15 s a que el cardumen cambie de capa (la velocidad vertical está limitada).

### `CurrentField.cs`
Campo de corriente sintético: `At(posMundo, t)` → velocidad horizontal (m/s). Marea semidiurna a lo largo de `tideAxisDeg` (elipse estrecha, `minorAxis`) + deriva residual (`residualDeg`), con ±`spatialNoise` de variación espacial, y perfil de potencia 1/7 sobre el fondo (`waterDepthAt` = `SalmonFarmBuilder.WaterDepth`). Por intensidad: rapidez media 0,06 / **0,13** / 0,20 m/s y residual 0,005 / 0,035 / 0,065 m/s (Media y rango residual: NewDEPOMOD, granja Muck, SEPA); la amplitud de la marea se calibra (`CalibrateAmplitude`) para que la media en un ciclo y en la columna sea esa. El ciclo real de 12,42 h dura `tidalPeriodSeconds` (240 s) en la demo. `Instance`/`Sample` para consultar desde cualquier script; `Describe`, `TideLabel`, `TideHours` para la interfaz.

### `CurrentViz.cs`
Visualización opcional de la corriente: partículas en suspensión con estela (jaula abierta), flechas sobre el agua fuera del módulo (vista general) y columnas de flechas por profundidad con su rapidez arriba/abajo (corte lateral; solo la componente en el plano del corte).

### `IFishSource.cs`
Contrato entre el panel y una jaula (`Columns`, `Notes`, `StatusLine`, `GetRows`, `GetStats`, `TryPick`, `SetSelection`). `SchoolStats` incluye polarización, orden de rotación (0–1, |promedio de la componente tangencial de la dirección|), validez de la dirección y concentración vertical (densidad a ±1 m de la mediana / densidad media). `FishMetrics` calcula esas métricas; `RendererHighlighter` resalta peces hechos de Renderers.

### `SalmonFarmBuilder.cs`
Componente que arma toda la escena al dar Play. Si no está en la escena, un `[RuntimeInitializeOnLoadMethod]` lo crea con valores por defecto (para cambiarlos, agrégalo a un objeto de la escena).
- **Inspector:** `rows`, `cols`, `cageSize`, `netDepth`, `spacing`, `deckWidth`, `walkwayWidth`, `meshSize`, `pontoonWidth`, y del entorno `seed`, `lakeRadius`, `mountainCount`, `treeCount`.
- Lago (shader `SalmonSim/Water`), orilla/fondo low-poly, montañas y árboles: decoración barata (sin sombras, mallas combinadas).
- Jaulas cuadradas (`FarmCage`): collar sobre flotadores, baranda, red (cintas + velo semitransparente), lastre, contorno de hover y `BoxCollider` para seleccionarla.
- Pasillos flotantes entre jaulas y hacia el pontón central (caseta, silos, dosificador, mástil con antenas).
- `SalmonFarmBuilder.Seabed.cs`: fondo bajo la salmonera (`seabedDepth` 35 m, `seabedSlope` hacia el centro del lago, `seabedRoughness`; `BedY(x,z)` / `WaterDepth(x,z)`), en anillos cosidos a la orilla, con textura de sedimento procedural; `rockCount` rocas low-poly; fondeo: 2 líneas por esquina del módulo (boya, cabo colgante en catenaria, cadena sobre el fondo, ancla de arrastre a `mooringScope`×profundidad); y la geometría del corte lateral (cara del corte, velo de agua, telón). El fondo y el fondeo quedan visibles bajo el agua (no son parte de `Isolate`).
- `SalmonFarmBuilder.Feeding.cs`: tuberías desde el dosificador por la cubierta, el canal entre filas y el collar hasta un esparcidor rotatorio flotante al centro de cada jaula (el rotor se asigna a `FishSchool.spreaderRotor`).
- Mueve el `TrajectoryPlayer` de la escena dentro de la "Jaula 1" (le agrega `SalmonHud`) y crea un `FishSchool` en cada jaula restante a partir de `simulatedProfile`, variado por jaula. Cada jaula lleva su etiqueta de origen (`dataLabel`: "Datos reales · video Katmai (río)" / "Simulación · basada en supuestos").
- Crea un único `SalmonPanel` en el objeto de la salmonera; el navegador le asigna la jaula abierta.
- `SetUnderwater(bool)` cambia el ambiente (niebla de distancia ↔ niebla submarina, sol, color de fondo); `Isolate(cage)` deja visible solo esa jaula; `SetSection(bool)` activa la geometría del corte y quita la niebla.
- Agrega `CurrentField` y `CurrentViz` a la salmonera.

### `FarmCage.cs`
Datos de una jaula (`displayName`, `description`, `player`, `FocusWorld`) y su resaltado (`SetHighlight`).

### `FarmKit.cs`
Utilidades de construcción: `FarmKit` (primitivas, cono, materiales URP Lit/transparentes), `MeshBatch` (combina primitivas por material) y `MeshBuilder` (mallas de triángulos con sombreado plano).

### `FishFactory.cs`
Construye un salmón procedural con cuerpo (esfera achatada), cola (pivot + cubo para aleteo) y aleta dorsal (cubo inclinado). Añade `FishWag` automáticamente.

### `Resources/SalmonWater.shader`, `Resources/SalmonUnlitTransparent.shader`
Shaders URP propios (en `Resources` para que entren en un build): agua con ondas por píxel y Fresnel; color transparente con niebla y desvanecido por profundidad (red, contorno).

### `FishWag.cs`
Anima la cola del salmón. En cada `Update()` aplica una rotación senoidal al `TailPivot` con frecuencia (~6.5 Hz) y amplitud (~28°) aleatorias por pez.

### `FarmCamera.cs`
Cámara con cuatro modos: `Overview` (órbita lenta sobre la salmonera), `Transition`, `Cage` (órbita bajo el agua frente a la red, sin salir de la superficie; si se sigue inclinando, la cámara se queda bajo la superficie y mira más hacia abajo, hasta `maxCageLookPitch`, para ver el fondo) y `Section` (corte lateral: ortográfica mirando al norte, el plano cercano es el corte; arrastrar mueve a los lados, la rueda acerca de "todo el fondeo" hasta exageración vertical ×1; `SectionExaggeration`). En los modos estables: arrastrar rota, la rueda hace zoom. `FlyToCage` / `FlyToOverview` vuelan ~1,5 s (`flightDuration`) por una curva con easing que termina en picado bajo el agua; avisan al cruzar la superficie. El input se lee con IMGUI (`Event.current`) porque el proyecto usa solo el Input System nuevo.

### `FarmNavigator.cs`
Navegación vista general ↔ jaula. En la vista general: etiqueta con el nombre de cada jaula, hover (raycast al collider) que la resalta y muestra un tooltip, clic para entrar. En la jaula: título, botón "← Volver" y tecla Esc; muestra `SalmonHud`/`SalmonPanel` solo si la jaula tiene `player`. Al cruzar la superficie llama a `SetUnderwater` e `Isolate`. En las jaulas simuladas, recuadro "Alimentación" (botón "Alimentar ahora", estado de la comida, peces con hambre, conteos y "Alimento no consumido (%)" con barra). En todas las vistas, la corriente en el lugar. Botón "Corte lateral ▸" (vista general): escala de profundidad y rótulos (red, fondo, anclas, líneas de fondeo).
**Pruebas por código:** `EnterCage(i)`, `ExitCage()`, `EnterSection()`, `ExitSection()`, `forcedHover = i` (hover sin mouse); `Current.school.FeedNow()`. Para capturar a mitad del vuelo, subir `FarmCamera.flightDuration` y congelar con `Time.timeScale = 0` (las llamadas `eval` tardan más que 1,5 s).

### `SalmonPanel.cs`
Panel derecho de la jaula abierta (cualquier `IFishSource`, asignado en `Source`): resumen en 6 recuadros (peces, velocidad media, profundidad media, polarización, orden de rotación, concentración) + línea de estado (temperatura, alimentación), tabla con scroll (solo dibuja las filas visibles) y selección (clic en una fila o en un pez, sin arrastrar). `PanelRect` permite a la cámara ignorar clics sobre el panel.

### `SalmonHud.cs`
HUD IMGUI (sin Canvas). Muestra en la esquina superior izquierda:
- `Salmones visibles: N` — peces activos en el frame actual.
- `Tiempo: T / D s` — tiempo de reproducción y duración total.

---

## Datos de entrada

El CSV debe estar en `Assets/StreamingAssets/trajectories.csv` (copia de `../data/trajectories.csv`) con las columnas:
`frame, t, id, cx, cy, w, h, conf, z`
(ver contrato completo en `../CLAUDE.md`).

`TrajectoryPlayer` usa dos escalas:
- **Métricas — escala estimada del video** (`videoWidth`, `videoHeight`, `videoDepth`; por defecto 10×5×10 m): `cx`, `cy` (0–1) y `z` (0–1) se convierten a metros con estos valores al cargar el CSV. Velocidades, el umbral `moveSpeedThreshold`, polarización, rotación y las columnas del panel se calculan aquí.
- **Dibujo** (`cageWidth`, `cageHeight`, `cageDepth`): solo deciden dónde se dibujan los peces dentro de la Jaula 1. `SalmonFarmBuilder` los fija para llenar la red con `dataMargin` (hoy 12×6×12 m). Cambiarlos no altera ninguna métrica.

## Flujo de prueba estándar

```bash
unity command recompile               # 1. confirmar sin errores
unity command editor_play             # 2. entrar en Play
# (en PowerShell) capturar screenshot
unity command console --result-only --json   # 3. leer consola
unity command editor_stop             # 4. salir de Play
```
