using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Arma la salmonera completa al dar Play, todo por código: lago con orilla y
/// montañas low-poly (decoración), grilla de jaulas cuadradas unidas por pasillos
/// flotantes, pontón central de operaciones, cámara y navegación.
/// El fondo, el fondeo y el corte lateral están en SalmonFarmBuilder.Seabed.cs; las
/// tuberías de alimento y los esparcidores, en SalmonFarmBuilder.Feeding.cs.
/// Si no hay uno en la escena se crea solo con los valores por defecto; para
/// cambiar la grilla, agrega este componente a un objeto de la escena.
/// La "Jaula 1" recibe el TrajectoryPlayer de la escena (datos reales).
/// </summary>
public partial class SalmonFarmBuilder : MonoBehaviour
{
    [Header("Grilla de jaulas")]
    [Min(1)] public int rows = 2;
    [Min(1)] public int cols = 4;
    [Tooltip("Lado interior de cada jaula (m)")] public float cageSize = 14f;
    [Tooltip("Profundidad de la red (m)")] public float netDepth = 8f;
    [Tooltip("Distancia entre las redes de jaulas vecinas (m)")] public float spacing = 6f;
    [Tooltip("Ancho del collar/pasarela de cada jaula (m)")] public float deckWidth = 1.2f;
    public float walkwayWidth = 2f;
    [Tooltip("Separación entre hilos de la red dibujada (m)")] public float meshSize = 1f;

    [Header("Peces")]
    [Tooltip("Margen (m) entre el volumen de datos reales y la red, a cada lado")]
    public float dataMargin = 1f;
    [Tooltip("Perfil base de las jaulas simuladas; cada jaula lo varía un poco")]
    public BehaviorProfile simulatedProfile = new();

    [Header("Pontón central")]
    public float pontoonWidth = 12f;

    [Header("Entorno (decorativo)")]
    public int seed = 7;
    public float lakeRadius = 420f;
    public int mountainCount = 13;
    public int treeCount = 260;

    public static readonly Color WaterColor = new(0.03f, 0.20f, 0.30f);
    public static readonly Color SkyColor = new(0.72f, 0.80f, 0.86f);
    // Desde la superficie la red se ve más marcada; dentro del agua, tenue para no tapar los peces.
    static readonly Color NetSurface = new(0.42f, 0.52f, 0.52f, 0.9f);
    static readonly Color NetUnderwater = new(0.55f, 0.72f, 0.75f, 0.32f);
    const float DeckY = 0.5f, DeckTop = 0.56f;

    public IReadOnlyList<FarmCage> Cages => cages;
    public bool Underwater { get; private set; }
    public bool InSection { get; private set; }

    readonly List<FarmCage> cages = new();
    readonly List<GameObject> scenery = new();
    float[] cageX, cageZ;
    float pontoonX, pontoonLength;
    /// Radio (m) del círculo que contiene el módulo (jaulas + pontón).
    float moduleRadius;
    Light sun;
    float murk;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindFirstObjectByType<SalmonFarmBuilder>() == null)
            new GameObject("Salmonera").AddComponent<SalmonFarmBuilder>();
    }

    void Awake() => Build();

    void Build()
    {
        var rnd = new System.Random(seed);
        Layout();

        var env = Child("Entorno");
        BuildLakeAndShore(env, rnd);
        BuildMountains(env, rnd);
        BuildTrees(env, rnd);
        scenery.Add(env.gameObject);

        var cageRoot = Child("Jaulas");
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++)
                cages.Add(BuildCage(cageRoot, r * cols + c, new Vector3(cageX[c], 0f, cageZ[r])));

        var walk = Child("Pasillos");
        BuildWalkways(walk);
        scenery.Add(walk.gameObject);

        var pontoon = Child("Ponton");
        pontoon.localPosition = new Vector3(pontoonX, 0f, 0f);
        BuildPontoon(pontoon);
        scenery.Add(pontoon.gameObject);

        AttachPlayer(cages[0]);
        for (int i = 1; i < cages.Count; i++) AttachSchool(cages[i], rnd);

        // Fondo, rocas y fondeo quedan visibles bajo el agua (no son parte de `scenery`).
        var bed = Child("Fondo");
        BuildSeabed(bed);
        BuildRocks(bed, new System.Random(seed + 11));
        BuildMooring(bed);
        var feed = Child("Alimentacion");
        BuildFeedLines(feed);
        scenery.Add(feed.gameObject);
        BuildSection(Child("Corte lateral"));

        var field = GetComponent<CurrentField>();
        if (field == null) field = gameObject.AddComponent<CurrentField>();
        field.waterDepthAt = WaterDepth;
        SetupCamera();
        foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
            if (l.type == LightType.Directional) { sun = l; break; }

        // Condiciones (etapa, estación, hora): perfiles iniciales al instante, antes del Start de los cardúmenes.
        var cond = GetComponent<FarmConditions>();
        if (cond == null) cond = gameObject.AddComponent<FarmConditions>();
        cond.farm = this;
        cond.nav = GetComponent<FarmNavigator>();
        cond.nav.conditions = cond;
        cond.field = field;
        var viz = GetComponent<CurrentViz>();
        if (viz == null) viz = gameObject.AddComponent<CurrentViz>();
        viz.farm = this;
        viz.nav = cond.nav;
        viz.field = field;
        cond.viz = viz;
        cond.Apply(immediate: true);
        ApplyNetLook();
        ApplyEnvironment();
    }

    Transform Child(string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(transform, false);
        return t;
    }

    // ------------------------------------------------------------------ Ambiente

    /// Iluminación de un momento del día, en superficie o bajo el agua.
    struct Lighting
    {
        public Color fog, ambient, light, waterDeep, waterSky;
        public float lightIntensity, fogDensity, fogEnd;
        public Vector3 lightEuler;

        public static Lighting Lerp(Lighting a, Lighting b, float t) => new()
        {
            fog = Color.Lerp(a.fog, b.fog, t), ambient = Color.Lerp(a.ambient, b.ambient, t),
            light = Color.Lerp(a.light, b.light, t), waterDeep = Color.Lerp(a.waterDeep, b.waterDeep, t),
            waterSky = Color.Lerp(a.waterSky, b.waterSky, t),
            lightIntensity = Mathf.Lerp(a.lightIntensity, b.lightIntensity, t),
            fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t), fogEnd = Mathf.Lerp(a.fogEnd, b.fogEnd, t),
            lightEuler = Vector3.Lerp(a.lightEuler, b.lightEuler, t),
        };
    }

    // Superficie: sol de tarde / luna. Bajo el agua: luz filtrada azul-verdosa / casi oscuro.
    static readonly Lighting SurfaceDay = new()
    {
        fog = SkyColor, ambient = new Color(0.56f, 0.62f, 0.68f), light = new Color(1f, 0.95f, 0.86f),
        lightIntensity = 1.25f, lightEuler = new Vector3(38f, -35f, 0f), fogEnd = 1100f,
        waterDeep = new Color(0.04f, 0.20f, 0.24f), waterSky = new Color(0.62f, 0.74f, 0.82f),
    };
    static readonly Lighting SurfaceNight = new()
    {
        fog = new Color(0.06f, 0.09f, 0.16f), ambient = new Color(0.12f, 0.15f, 0.24f), light = new Color(0.60f, 0.70f, 1f),
        lightIntensity = 0.3f, lightEuler = new Vector3(32f, 140f, 0f), fogEnd = 750f,
        waterDeep = new Color(0.01f, 0.04f, 0.07f), waterSky = new Color(0.10f, 0.14f, 0.24f),
    };
    static readonly Lighting UnderDay = new()
    {
        fog = WaterColor, ambient = new Color(0.30f, 0.50f, 0.60f), light = new Color(0.75f, 0.92f, 1f),
        lightIntensity = 1.3f, lightEuler = new Vector3(70f, 20f, 0f), fogDensity = 0.035f,
    };
    static readonly Lighting UnderNight = new()
    {
        fog = new Color(0.01f, 0.05f, 0.09f), ambient = new Color(0.11f, 0.18f, 0.26f), light = new Color(0.55f, 0.68f, 1f),
        lightIntensity = 0.35f, lightEuler = new Vector3(70f, 140f, 0f), fogDensity = 0.05f,
    };

    /// 0 = día, 1 = noche (se interpola en SetNight).
    public float Night { get; private set; }
    float nightTarget, nightSpeed;
    Material waterMat;

    /// Pasa a día o noche en `seconds` (0 = al instante).
    public void SetNight(bool night, float seconds)
    {
        nightTarget = night ? 1f : 0f;
        nightSpeed = seconds > 0f ? 1f / seconds : float.PositiveInfinity;
        if (seconds <= 0f) Night = nightTarget;
    }

    void Update()
    {
        Night = Mathf.MoveTowards(Night, nightTarget, nightSpeed * Time.deltaTime);
        // Al sumergirse el agua se ve turbia y se aclara en ~0,8 s.
        if (murk > 0f) murk = Mathf.Max(0f, murk - Time.deltaTime / 0.8f);
        ApplyEnvironment();
    }

    /// Cambia entre el ambiente de superficie (niebla de distancia, sol/luna) y el
    /// submarino (niebla azul-verdosa densa). murky: empieza turbia y se aclara.
    public void SetUnderwater(bool on, bool murky = false)
    {
        if (on == Underwater) return;
        Underwater = on;
        murk = on && murky ? 1f : 0f;
        ApplyNetLook();
        ApplyEnvironment();
    }

    /// Desde la superficie la red se desvanece con la profundidad; dentro del agua se ve entera y tenue.
    void ApplyNetLook()
    {
        Mats.Net.SetFloat("_DepthFade", Underwater ? 0f : 0.28f);
        Mats.NetFill.SetFloat("_DepthFade", Underwater ? 0f : 0.28f);
        Mats.Net.SetColor("_BaseColor", Underwater ? NetUnderwater : NetSurface);
    }

    void ApplyEnvironment()
    {
        float t = Mathf.SmoothStep(0f, 1f, Night);
        var L = Underwater ? Lighting.Lerp(UnderDay, UnderNight, t) : Lighting.Lerp(SurfaceDay, SurfaceNight, t);
        // En el corte lateral (cámara ortográfica) no hay niebla: el fondo de agua es un telón.
        RenderSettings.fog = !InSection;
        if (backdropMat != null) backdropMat.SetColor("_BaseColor", Color.Lerp(Color.white, new Color(0.25f, 0.3f, 0.4f), t));
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = L.ambient;
        RenderSettings.fogColor = L.fog;
        if (Underwater)
        {
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = Mathf.Lerp(L.fogDensity, 0.16f, murk * murk);
        }
        else
        {
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 150f;
            RenderSettings.fogEndDistance = L.fogEnd;
            if (waterMat == null)
            {
                var w = transform.Find("Entorno/Agua");
                if (w != null) waterMat = w.GetComponent<MeshRenderer>().sharedMaterial;
            }
            if (waterMat != null)
            {
                waterMat.SetColor("_DeepColor", L.waterDeep);
                waterMat.SetColor("_SkyColor", L.waterSky);
            }
        }
        if (sun != null)
        {
            sun.color = L.light;
            sun.intensity = L.lightIntensity;
            sun.transform.rotation = Quaternion.Euler(L.lightEuler);
        }
        var cam = Camera.main;
        if (cam != null) cam.backgroundColor = L.fog;
    }

    /// Activa o desactiva la vista "Corte lateral" (geometría del corte y ambiente sin niebla).
    public void SetSection(bool on)
    {
        InSection = on;
        if (sectionRoot != null) sectionRoot.SetActive(on);
        Isolate(null);
        ApplyEnvironment();
    }

    /// Deja visible solo `focus` (o todo, si es null).
    public void Isolate(FarmCage focus)
    {
        foreach (var s in scenery) s.SetActive(focus == null);
        foreach (var c in cages) c.gameObject.SetActive(focus == null || c == focus);
    }

    void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) cam = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 2500f;
        if (cam.GetComponent<FarmCamera>() == null) cam.gameObject.AddComponent<FarmCamera>();
        var nav = GetComponent<FarmNavigator>();
        if (nav == null) nav = gameObject.AddComponent<FarmNavigator>();
        nav.farm = this;
        nav.cam = cam.GetComponent<FarmCamera>();
        nav.panel = GetComponent<SalmonPanel>();
        if (nav.panel == null) nav.panel = gameObject.AddComponent<SalmonPanel>();
        nav.panel.enabled = false;
    }

    /// La Jaula 1 recibe el TrajectoryPlayer de la escena: el volumen del video
    /// (x ±w/2, y ±h/2, z 0..d) se escala para llenar la red dejando dataMargin.
    void AttachPlayer(FarmCage cage)
    {
        var player = FindFirstObjectByType<TrajectoryPlayer>();
        if (player == null) player = new GameObject("Datos Katmai").AddComponent<TrajectoryPlayer>();
        player.cageWidth = cageSize - 2f * dataMargin;
        player.cageDepth = cageSize - 2f * dataMargin;
        player.cageHeight = netDepth - 2f * dataMargin;
        player.transform.SetParent(cage.transform, false);
        player.transform.localPosition = cage.FocusLocal + new Vector3(0f, 0f, -player.cageDepth * 0.5f);
        player.transform.localRotation = Quaternion.identity;
        cage.player = player;
        cage.isRealData = true;
        cage.dataLabel = "Datos reales · video Katmai (río)";
        cage.description = "Peces detectados con YOLO y seguidos con ByteTrack";

        var hud = player.GetComponent<SalmonHud>();
        if (hud == null) hud = player.gameObject.AddComponent<SalmonHud>();
        hud.player = player;
        hud.enabled = false;
    }

    /// Jaulas simuladas: un cardumen boids. Cada jaula guarda una variación propia
    /// (cantidad, tamaño, velocidad, profundidad, giro) que FarmConditions aplica sobre el
    /// perfil de la etapa/estación/hora, para que no se vean todas iguales.
    void AttachSchool(FarmCage cage, System.Random rnd)
    {
        float R(float a, float b) => Rand(rnd, a, b);
        var go = new GameObject("Cardumen");
        go.transform.SetParent(cage.transform, false);
        var school = go.AddComponent<FishSchool>();
        school.halfSize = cageSize * 0.5f;
        school.netDepth = netDepth;
        school.seed = seed * 100 + cage.index;
        school.clockwise = cage.index % 2 == 0;
        cage.countFactor = R(0.75f, 1.25f);
        cage.lengthFactor = R(0.92f, 1.08f);
        cage.speedFactor = R(0.88f, 1.12f);
        cage.depthOffset = R(-0.6f, 0.6f);
        cage.circlingFactor = R(0.8f, 1.25f);
        cage.school = school;
        cage.isRealData = false;
        cage.dataLabel = "Simulación · basada en supuestos";
    }

    // ------------------------------------------------------------------ Grilla

    /// Posiciones de columnas/filas. El pontón va en un hueco entre las dos mitades de columnas.
    void Layout()
    {
        float pitch = cageSize + spacing;
        int split = cols / 2;
        float extra = pontoonWidth + spacing;
        cageX = new float[cols];
        for (int c = 0; c < cols; c++) cageX[c] = c * pitch + (c >= split ? extra : 0f);
        pontoonX = cageX[split] - cageSize * 0.5f - spacing - pontoonWidth * 0.5f;

        float minX = Mathf.Min(cageX[0] - cageSize * 0.5f, pontoonX - pontoonWidth * 0.5f);
        float maxX = Mathf.Max(cageX[cols - 1] + cageSize * 0.5f, pontoonX + pontoonWidth * 0.5f);
        float mid = (minX + maxX) * 0.5f;
        for (int c = 0; c < cols; c++) cageX[c] -= mid;
        pontoonX -= mid;

        cageZ = new float[rows];
        for (int r = 0; r < rows; r++) cageZ[r] = (r - (rows - 1) * 0.5f) * pitch;
        pontoonLength = rows * cageSize + (rows - 1) * spacing;

        float e = cageSize * 0.5f + deckWidth;
        ModuleBounds = Rect.MinMaxRect(cageX[0] - e, cageZ[0] - e, cageX[cols - 1] + e, cageZ[rows - 1] + e);
        moduleRadius = new Vector2(ModuleBounds.width, ModuleBounds.height).magnitude * 0.5f;
    }

    FarmCage BuildCage(Transform parent, int index, Vector3 pos)
    {
        var go = new GameObject($"Jaula {index + 1}");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        var cage = go.AddComponent<FarmCage>();
        cage.index = index;
        cage.displayName = $"Jaula {index + 1}";
        cage.size = cageSize;
        cage.netDepth = netDepth;

        // Materiales propios de cada jaula para poder resaltarla.
        var deck = FarmKit.Lit("Collar", new Color(0.55f, 0.57f, 0.58f), 0.3f, 0.4f);
        var rail = FarmKit.Lit("Baranda", new Color(0.95f, 0.72f, 0.10f), 0.35f);
        cage.tintMaterials = new[] { deck, rail };

        float h = cageSize * 0.5f, dw = deckWidth;
        var b = new MeshBatch();
        int posts = Mathf.Max(2, Mathf.RoundToInt(cageSize / 2f));
        for (int s = 0; s < 4; s++)
        {
            // Cada lado se arma en el lado +Z y se rota; el collar va "en molinete" para no solaparse.
            var q = Quaternion.Euler(0f, 90f * s, 0f);
            var along = q * Quaternion.Euler(0f, 0f, 90f); // eje Y del cilindro a lo largo de X
            b.Box(deck, q * new Vector3(dw * 0.5f, DeckY, h + dw * 0.5f), new Vector3(cageSize + dw, 0.12f, dw), q);
            // Flotadores de HDPE bajo el collar
            b.Cylinder(Mats.Hdpe, q * new Vector3(0f, 0.15f, h + 0.3f), 0.28f, cageSize + 2f * dw, along);
            b.Cylinder(Mats.Hdpe, q * new Vector3(0f, 0.15f, h + dw - 0.3f), 0.28f, cageSize + 2f * dw, along);
            // Baranda en el borde interior
            for (int i = 0; i < posts; i++)
            {
                float x = -h + i * cageSize / posts;
                b.Box(rail, q * new Vector3(x, DeckTop + 0.55f, h + 0.08f), new Vector3(0.07f, 1.1f, 0.07f), q);
            }
            b.Box(rail, q * new Vector3(0f, DeckTop + 0.55f, h + 0.08f), new Vector3(cageSize + 0.16f, 0.05f, 0.05f), q);
            b.Box(rail, q * new Vector3(0f, DeckTop + 1.08f, h + 0.08f), new Vector3(cageSize + 0.16f, 0.06f, 0.06f), q);
            // Lastre del fondo de la red
            b.Cylinder(Mats.Hdpe, q * new Vector3(0f, -netDepth, h), 0.12f, cageSize, along);
        }
        b.Build(go.transform, "Estructura");
        BuildNet(go.transform);

        // Contorno de resaltado (solo visible con hover)
        float o = h + dw + 0.25f;
        var lineGo = new GameObject("Contorno");
        lineGo.transform.SetParent(go.transform, false);
        var lr = lineGo.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = true;
        lr.positionCount = 4;
        lr.SetPositions(new[] { new Vector3(-o, 0.7f, -o), new Vector3(-o, 0.7f, o), new Vector3(o, 0.7f, o), new Vector3(o, 0.7f, -o) });
        lr.widthMultiplier = 0.35f;
        lr.sharedMaterial = Mats.Outline;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.enabled = false;
        cage.outline = lr;

        var col = go.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, (2f - netDepth) * 0.5f, 0f);
        col.size = new Vector3(cageSize + 2f * dw, netDepth + 2f, cageSize + 2f * dw);
        cage.pickCollider = col;
        return cage;
    }

    /// Red: hilos como cintas delgadas en las 4 caras y el fondo, más un velo casi transparente.
    void BuildNet(Transform parent)
    {
        float h = cageSize * 0.5f, top = DeckTop + 1.08f, bot = -netDepth;
        int n = Mathf.Max(2, Mathf.RoundToInt(cageSize / meshSize));
        var lines = new MeshBuilder();
        var fill = new MeshBuilder();
        const float hw = 0.022f;
        for (int s = 0; s < 4; s++)
        {
            var q = Quaternion.Euler(0f, 90f * s, 0f);
            for (int i = 0; i <= n; i++)
            {
                float x = -h + i * cageSize / n;
                lines.Strip(0, q * new Vector3(x, bot, h), q * new Vector3(x, top, h), q * new Vector3(hw, 0f, 0f));
            }
            for (float y = top; y > bot - 0.01f; y -= meshSize)
                lines.Strip(0, q * new Vector3(-h, y, h), q * new Vector3(h, y, h), new Vector3(0f, hw, 0f));
            fill.Quad(0, q * new Vector3(-h, bot, h), q * new Vector3(-h, top, h), q * new Vector3(h, top, h),
                      q * new Vector3(h, bot, h), q * Vector3.forward);
        }
        for (int i = 0; i <= n; i++)
        {
            float t = -h + i * cageSize / n;
            lines.Strip(0, new Vector3(t, bot, -h), new Vector3(t, bot, h), new Vector3(hw, 0f, 0f));
            lines.Strip(0, new Vector3(-h, bot, t), new Vector3(h, bot, t), new Vector3(0f, 0f, hw));
        }
        fill.Quad(0, new Vector3(-h, bot, -h), new Vector3(-h, bot, h), new Vector3(h, bot, h), new Vector3(h, bot, -h), Vector3.down);

        MeshBuilder.AddRenderer(parent, "Red", lines.ToMesh("Red"), Mats.Net, false);
        MeshBuilder.AddRenderer(parent, "Red (velo)", fill.ToMesh("Velo"), Mats.NetFill, false);
    }

    void BuildWalkways(Transform parent)
    {
        var b = new MeshBatch();
        float h = cageSize * 0.5f + deckWidth;
        int split = cols / 2;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c + 1 < cols; c++)
                if (c + 1 != split)
                    Walkway(b, new Vector3(cageX[c] + h, 0f, cageZ[r]), new Vector3(cageX[c + 1] - h, 0f, cageZ[r]));
            // Con el pontón, a ambos lados
            if (split > 0)
                Walkway(b, new Vector3(cageX[split - 1] + h, 0f, cageZ[r]), new Vector3(pontoonX - pontoonWidth * 0.5f, 0f, cageZ[r]));
            Walkway(b, new Vector3(pontoonX + pontoonWidth * 0.5f, 0f, cageZ[r]), new Vector3(cageX[split] - h, 0f, cageZ[r]));
        }
        for (int r = 0; r + 1 < rows; r++)
            for (int c = 0; c < cols; c++)
                Walkway(b, new Vector3(cageX[c], 0f, cageZ[r] + h), new Vector3(cageX[c], 0f, cageZ[r + 1] - h));
        b.Build(parent, "Pasillos");
    }

    /// Pasillo flotante de `from` a `to` (al nivel del agua), con flotadores y barandas bajas.
    void Walkway(MeshBatch b, Vector3 from, Vector3 to)
    {
        var dir = to - from;
        float len = dir.magnitude + 0.3f;
        if (len < 0.5f) return;
        var q = Quaternion.LookRotation(dir.normalized);
        var c = (from + to) * 0.5f;
        float hw = walkwayWidth * 0.5f;
        var alongZ = q * Quaternion.Euler(90f, 0f, 0f);
        b.Box(Mats.Walkway, c + Vector3.up * DeckY, new Vector3(walkwayWidth, 0.12f, len), q);
        foreach (float sx in new[] { -1f, 1f })
        {
            b.Cylinder(Mats.Hdpe, c + q * new Vector3(sx * (hw - 0.25f), 0.15f, 0f), 0.25f, len, alongZ);
            for (int i = 0; i <= 2; i++)
            {
                float z = Mathf.Lerp(-len * 0.5f + 0.2f, len * 0.5f - 0.2f, i / 2f);
                b.Box(Mats.Steel, c + q * new Vector3(sx * (hw - 0.05f), DeckTop + 0.5f, z), new Vector3(0.06f, 1f, 0.06f), q);
            }
            b.Box(Mats.Steel, c + q * new Vector3(sx * (hw - 0.05f), DeckTop + 1f, 0f), new Vector3(0.05f, 0.05f, len - 0.3f), q);
        }
    }

    // ------------------------------------------------------------------ Pontón

    void BuildPontoon(Transform parent)
    {
        var b = new MeshBatch();
        float pw = pontoonWidth, pl = pontoonLength, deckTop = 0.6f;
        b.Box(Mats.Hull, new Vector3(0f, -0.45f, 0f), new Vector3(pw, 1.9f, pl));
        b.Box(Mats.PontoonDeck, new Vector3(0f, 0.55f, 0f), new Vector3(pw - 0.1f, 0.1f, pl - 0.1f));
        foreach (float sx in new[] { -1f, 1f })
            b.Box(Mats.Hdpe, new Vector3(sx * (pw * 0.5f + 0.12f), 0.15f, 0f), new Vector3(0.25f, 0.35f, pl));

        // Caseta
        float houseW = pw * 0.62f, houseH = 3.2f, houseL = pl * 0.36f;
        var hc = new Vector3(0f, deckTop + houseH * 0.5f, -pl * 0.18f);
        float roofTop = deckTop + houseH + 0.3f;
        b.Box(Mats.House, hc, new Vector3(houseW, houseH, houseL));
        b.Box(Mats.Roof, new Vector3(hc.x, deckTop + houseH + 0.15f, hc.z), new Vector3(houseW + 0.6f, 0.3f, houseL + 0.6f));
        foreach (float sx in new[] { -1f, 1f })
            for (int i = 0; i < 4; i++)
            {
                float z = hc.z - houseL * 0.5f + (i + 0.5f) * houseL / 4f;
                b.Box(Mats.Glass, new Vector3(sx * (houseW * 0.5f + 0.02f), hc.y + 0.35f, z), new Vector3(0.06f, 1.1f, houseL / 4f - 0.8f));
            }
        float front = hc.z + houseL * 0.5f + 0.02f;
        b.Box(Mats.Glass, new Vector3(houseW * 0.15f, hc.y + 0.35f, front), new Vector3(houseW * 0.45f, 1.1f, 0.06f));
        b.Box(Mats.Door, new Vector3(-houseW * 0.3f, deckTop + 1.05f, front), new Vector3(1f, 2.1f, 0.06f));
        // Paneles solares en el techo
        for (int i = 0; i < 3; i++)
            b.Box(Mats.Solar, new Vector3(-houseW * 0.3f + i * houseW * 0.3f, roofTop + 0.4f, hc.z + houseL * 0.15f),
                  new Vector3(houseW * 0.26f, 0.06f, 1.6f), Quaternion.Euler(-25f, 0f, 0f));

        // Mástil con antenas, parabólica y baliza
        var mast = new Vector3(houseW * 0.32f, roofTop, hc.z - houseL * 0.3f);
        b.Cylinder(Mats.Steel, mast + Vector3.up * 4.5f, 0.09f, 9f, Quaternion.identity);
        b.Box(Mats.Steel, mast + Vector3.up * 7.8f, new Vector3(2.2f, 0.07f, 0.07f));
        b.Cylinder(Mats.Steel, mast + new Vector3(-1.05f, 8.6f, 0f), 0.03f, 1.6f, Quaternion.identity);
        b.Cylinder(Mats.Steel, mast + new Vector3(1.05f, 8.6f, 0f), 0.03f, 1.6f, Quaternion.identity);
        b.Sphere(Mats.Beacon, mast + Vector3.up * 9.1f, Vector3.one * 0.35f, Quaternion.identity);
        b.Sphere(Mats.House, mast + new Vector3(-0.9f, 5f, 0.2f), new Vector3(1.4f, 1.4f, 0.35f), Quaternion.Euler(-20f, 150f, 0f));
        b.Cylinder(Mats.Steel, new Vector3(-houseW * 0.4f, roofTop + 2f, hc.z + houseL * 0.4f), 0.03f, 4f, Quaternion.identity);
        b.Cylinder(Mats.Steel, new Vector3(-houseW * 0.4f, roofTop + 1.5f, hc.z - houseL * 0.4f), 0.03f, 3f, Quaternion.identity);

        // Silos de alimento (patas, tolva cónica, cuerpo, techo cónico) y dosificador
        const float sr = 1.5f, legH = 1.6f, hopper = 1.2f, bodyH = 5f;
        for (int i = 0; i < 3; i++)
        {
            var bse = new Vector3(-pw * 0.2f, deckTop, pl * 0.08f + i * (2f * sr + 0.9f));
            foreach (var lg in new[] { new Vector3(-1, 0, -1), new Vector3(1, 0, -1), new Vector3(-1, 0, 1), new Vector3(1, 0, 1) })
                b.Cylinder(Mats.Steel, bse + lg * (sr * 0.7f) + Vector3.up * (legH + hopper) * 0.5f, 0.07f, legH + hopper, Quaternion.identity);
            float hopTop = deckTop + legH + hopper;
            b.Cone(Mats.Silo, new Vector3(bse.x, hopTop, bse.z), sr, hopper, Quaternion.Euler(180f, 0f, 0f));
            b.Cylinder(Mats.Silo, new Vector3(bse.x, hopTop + bodyH * 0.5f, bse.z), sr, bodyH, Quaternion.identity);
            b.Cone(Mats.Silo, new Vector3(bse.x, hopTop + bodyH, bse.z), sr, 1.1f, Quaternion.identity);
            b.Cylinder(Mats.Hdpe, new Vector3(bse.x + 1.8f, deckTop + legH, bse.z), 0.12f, 3.6f, Quaternion.Euler(0f, 0f, 90f));
        }
        b.Box(Mats.Rail, new Vector3(pw * 0.22f, deckTop + 1.2f, pl * 0.08f + 2.4f), new Vector3(2.4f, 2.4f, 6f));

        // Baranda perimetral, con aberturas donde llegan los pasillos
        for (float z = -pl * 0.5f + 0.5f; z <= pl * 0.5f - 0.5f; z += 2f)
        {
            bool gap = false;
            foreach (float cz in cageZ) gap |= Mathf.Abs(z - cz) < walkwayWidth * 0.5f + 0.6f;
            if (gap) continue;
            foreach (float sx in new[] { -1f, 1f })
            {
                var p = new Vector3(sx * (pw * 0.5f - 0.15f), deckTop + 0.55f, z);
                b.Box(Mats.Rail, p, new Vector3(0.07f, 1.1f, 0.07f));
                b.Box(Mats.Rail, p + Vector3.up * 0.5f, new Vector3(0.05f, 0.05f, 2.05f));
            }
        }
        b.Build(parent, "Ponton");
    }

    // ------------------------------------------------------------------ Lago, orilla, montañas

    /// Altura del terreno de la orilla (m) en un punto del plano.
    float ShoreHeight(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float wobble = (Mathf.PerlinNoise(x * 0.004f + 31f, z * 0.004f + 17f) - 0.5f) * 110f;
        float rr = r + wobble;
        float r0 = lakeRadius * 0.62f, r1 = lakeRadius * 0.80f, r2 = lakeRadius * 0.88f, r3 = lakeRadius * 1.25f;
        float y;
        if (rr < r0) y = BedY(x, z);
        else if (rr < r1) y = Mathf.Lerp(BedY(x, z), -1f, Mathf.SmoothStep(0f, 1f, (rr - r0) / (r1 - r0)));
        else if (rr < r2) y = Mathf.Lerp(-1f, 2.5f, (rr - r1) / (r2 - r1));
        else y = Mathf.Lerp(2.5f, 32f, Mathf.Clamp01((rr - r2) / (r3 - r2)));
        float bumps = (Mathf.PerlinNoise(x * 0.03f + 5f, z * 0.03f + 9f) - 0.5f) * Mathf.Clamp(y, 0f, 30f) * 0.5f;
        return y + bumps;
    }

    void BuildLakeAndShore(Transform parent, System.Random rnd)
    {
        // Agua: disco con el shader propio (todas las ondas se calculan por píxel).
        var water = new MeshBuilder();
        const int seg = 96;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
            water.Tri(0, Vector3.zero, new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * lakeRadius,
                      new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * lakeRadius, Vector3.up);
        }
        MeshBuilder.AddRenderer(parent, "Agua", water.ToMesh("Agua"), FarmKit.Water(), false);

        // Fondo del lago + orilla en anillo, low-poly. Submallas: fango, arena, pasto, roca.
        float rIn = lakeRadius * 0.58f, rOut = lakeRadius * 2.2f;
        const int rings = 24, around = 120;
        var g = new Vector3[rings + 1, around];
        for (int k = 0; k <= rings; k++)
        {
            float r = Mathf.Lerp(rIn, rOut, k / (float)rings);
            for (int i = 0; i < around; i++)
            {
                float a = (i + (float)rnd.NextDouble() * 0.4f) * Mathf.PI * 2f / around;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                g[k, i] = new Vector3(x, ShoreHeight(x, z), z);
            }
        }
        var shore = new MeshBuilder(4);
        for (int k = 0; k < rings; k++)
            for (int i = 0; i < around; i++)
            {
                int j = (i + 1) % around;
                AddGroundTri(shore, g[k, i], g[k + 1, i], g[k + 1, j]);
                AddGroundTri(shore, g[k, i], g[k + 1, j], g[k, j]);
            }
        // El fondo dentro del anillo lo arma BuildSeabed, cosido a este primer anillo.
        shoreInnerRing = new Vector3[around];
        for (int i = 0; i < around; i++) shoreInnerRing[i] = g[0, i];
        MeshBuilder.AddRenderer(parent, "Orilla", shore.ToMesh("Orilla"),
            new[] { Mats.Mud, Mats.Sand, Mats.Grass, Mats.Rock }, false);
    }

    static void AddGroundTri(MeshBuilder mb, Vector3 a, Vector3 b, Vector3 c)
    {
        float y = (a.y + b.y + c.y) / 3f;
        int sub = y < -0.6f ? 0 : y < 1.6f ? 1 : y < 24f ? 2 : 3;
        mb.Tri(sub, a, b, c, Vector3.up);
    }

    void BuildMountains(Transform parent, System.Random rnd)
    {
        var mb = new MeshBuilder(3); // pasto, roca, nieve
        for (int m = 0; m < mountainCount; m++)
        {
            float ang = (m + Rand(rnd, -0.3f, 0.3f)) * Mathf.PI * 2f / mountainCount;
            float R = Rand(rnd, 95f, 165f), H = Rand(rnd, 120f, 260f);
            // La base (radio hasta 1,2·R) no debe meterse en el lago.
            float dist = lakeRadius * Rand(rnd, 1.0f, 1.25f) + R * 1.2f;
            var center = new Vector3(Mathf.Cos(ang) * dist, -6f, Mathf.Sin(ang) * dist);
            Mountain(mb, rnd, center, R, H);
        }
        MeshBuilder.AddRenderer(parent, "Montañas", mb.ToMesh("Montañas"),
            new[] { Mats.Forest, Mats.MountainRock, Mats.Snow }, false);
    }

    /// Montaña low-poly: anillos irregulares que se cierran hacia una cima.
    static void Mountain(MeshBuilder mb, System.Random rnd, Vector3 c, float R, float H)
    {
        const int rings = 5, sides = 11;
        var ring = new Vector3[rings, sides];
        float twist = Rand(rnd, 0f, 6.28f);
        for (int k = 0; k < rings; k++)
        {
            float f = k / (float)rings;
            float rr = R * Mathf.Pow(1f - f, 1.15f);
            float y = H * (1f - Mathf.Pow(1f - f, 1.7f));
            for (int i = 0; i < sides; i++)
            {
                float a = twist + (i + Rand(rnd, -0.3f, 0.3f)) * Mathf.PI * 2f / sides;
                float jr = rr * Rand(rnd, 0.78f, 1.2f);
                ring[k, i] = c + new Vector3(Mathf.Cos(a) * jr, y * Rand(rnd, 0.9f, 1.1f), Mathf.Sin(a) * jr);
            }
        }
        var apex = c + new Vector3(Rand(rnd, -0.1f, 0.1f) * R, H, Rand(rnd, -0.1f, 0.1f) * R);
        float snowLine = H * Rand(rnd, 0.58f, 0.72f);
        for (int k = 0; k < rings; k++)
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                if (k + 1 < rings)
                {
                    MountainTri(mb, c, snowLine, H, ring[k, i], ring[k + 1, i], ring[k + 1, j]);
                    MountainTri(mb, c, snowLine, H, ring[k, i], ring[k + 1, j], ring[k, j]);
                }
                else MountainTri(mb, c, snowLine, H, ring[k, i], apex, ring[k, j]);
            }
    }

    static void MountainTri(MeshBuilder mb, Vector3 c, float snowLine, float H, Vector3 a, Vector3 b, Vector3 d)
    {
        var mid = (a + b + d) / 3f;
        float y = mid.y - c.y;
        int sub = y > snowLine ? 2 : y < H * 0.16f ? 0 : 1;
        // Desde un punto bajo el centro, la normal de cada cara apunta hacia afuera y arriba.
        mb.Tri(sub, a, b, d, mid - (c + Vector3.down * H * 0.5f));
    }

    void BuildTrees(Transform parent, System.Random rnd)
    {
        var b = new MeshBatch();
        int placed = 0;
        for (int tries = 0; placed < treeCount && tries < treeCount * 20; tries++)
        {
            float a = Rand(rnd, 0f, Mathf.PI * 2f);
            float r = lakeRadius * Rand(rnd, 0.82f, 1.3f);
            float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
            float y = ShoreHeight(x, z);
            if (y < 2.5f || y > 24f) continue;
            if (Mathf.PerlinNoise(x * 0.02f + 3f, z * 0.02f + 7f) < 0.45f) continue; // bosquecillos
            float s = Rand(rnd, 0.8f, 1.4f);
            var p = new Vector3(x, y - 0.5f, z);
            b.Cylinder(Mats.Trunk, p + Vector3.up * s, 0.35f * s, 2f * s, Quaternion.identity);
            b.Cone(Mats.Pine, p + Vector3.up * 1.6f * s, 2.4f * s, 8f * s, Quaternion.Euler(0f, Rand(rnd, 0f, 360f), 0f));
            placed++;
        }
        b.Build(parent, "Arboles", false);
    }

    static float Rand(System.Random rnd, float a, float b) => a + (float)rnd.NextDouble() * (b - a);

    /// Materiales compartidos (los de cada jaula se crean aparte para resaltarlos).
    static class Mats
    {
        public static readonly Material Hdpe = FarmKit.Lit("HDPE", new Color(0.07f, 0.07f, 0.08f), 0.55f);
        public static readonly Material Steel = FarmKit.Lit("Acero", new Color(0.62f, 0.64f, 0.66f), 0.5f, 0.6f);
        public static readonly Material Walkway = FarmKit.Lit("Pasillo", new Color(0.50f, 0.52f, 0.53f), 0.3f, 0.4f);
        public static readonly Material Rail = FarmKit.Lit("Amarillo", new Color(0.95f, 0.72f, 0.10f), 0.35f);
        public static readonly Material Buoy = FarmKit.Lit("Boya", new Color(1f, 0.42f, 0.08f), 0.4f);
        public static readonly Material Hull = FarmKit.Lit("Casco", new Color(0.18f, 0.22f, 0.27f), 0.3f, 0.3f);
        public static readonly Material PontoonDeck = FarmKit.Lit("Cubierta", new Color(0.40f, 0.43f, 0.45f), 0.2f);
        public static readonly Material House = FarmKit.Lit("Caseta", new Color(0.90f, 0.91f, 0.92f), 0.25f);
        public static readonly Material Roof = FarmKit.Lit("Techo", new Color(0.22f, 0.33f, 0.48f), 0.3f);
        public static readonly Material Glass = FarmKit.Lit("Vidrio", new Color(0.07f, 0.12f, 0.18f), 0.95f, 0.2f);
        public static readonly Material Door = FarmKit.Lit("Puerta", new Color(0.55f, 0.15f, 0.12f), 0.3f);
        public static readonly Material Solar = FarmKit.Lit("Solar", new Color(0.08f, 0.12f, 0.28f), 0.9f, 0.3f);
        public static readonly Material Silo = FarmKit.Lit("Silo", new Color(0.80f, 0.82f, 0.84f), 0.55f, 0.6f);
        public static readonly Material Beacon = FarmKit.Emissive("Baliza", new Color(1f, 0.15f, 0.1f), 2f);
        public static readonly Material Mud = FarmKit.Lit("Fango", new Color(0.10f, 0.16f, 0.15f), 0.05f);
        public static readonly Material Sand = FarmKit.Lit("Arena", new Color(0.62f, 0.57f, 0.44f), 0.05f);
        public static readonly Material Grass = FarmKit.Lit("Pasto", new Color(0.27f, 0.40f, 0.20f), 0.05f);
        public static readonly Material Rock = FarmKit.Lit("RocaOrilla", new Color(0.40f, 0.40f, 0.38f), 0.1f);
        public static readonly Material Forest = FarmKit.Lit("Ladera", new Color(0.20f, 0.32f, 0.20f), 0.05f);
        public static readonly Material MountainRock = FarmKit.Lit("Roca", new Color(0.38f, 0.39f, 0.42f), 0.1f);
        public static readonly Material Snow = FarmKit.Lit("Nieve", new Color(0.93f, 0.95f, 0.98f), 0.3f);
        public static readonly Material Trunk = FarmKit.Lit("Tronco", new Color(0.30f, 0.20f, 0.12f), 0.05f);
        public static readonly Material Pine = FarmKit.Lit("Pino", new Color(0.10f, 0.25f, 0.15f), 0.05f);
        // La red se dibuja antes que el agua (cola 2950 < 3000) para verse a través de ella.
        public static readonly Material Net = FarmKit.Transparent("Red", NetSurface, 2950);
        public static readonly Material NetFill = FarmKit.Transparent("Velo", new Color(0.15f, 0.25f, 0.25f, 0.07f), 2940);
        public static readonly Material Outline = FarmKit.Transparent("Contorno", new Color(1f, 0.85f, 0.2f, 0.95f), 3010);
        // Fondo, fondeo y alimentación (fase 5)
        public static readonly Material SeabedRock = FarmKit.Lit("RocaFondo", new Color(0.30f, 0.29f, 0.26f), 0.1f);
        public static readonly Material Rope = FarmKit.Lit("Cabo", new Color(0.85f, 0.70f, 0.30f), 0.2f);
        public static readonly Material Chain = FarmKit.Lit("Cadena", new Color(0.22f, 0.22f, 0.24f), 0.5f, 0.7f);
        public static readonly Material AnchorSteel = FarmKit.Lit("Ancla", new Color(0.28f, 0.24f, 0.21f), 0.3f, 0.6f);
        public static readonly Material CutSediment = FarmKit.Lit("CorteSedimento", new Color(0.36f, 0.31f, 0.22f), 0.05f);
        public static readonly Material CutRock = FarmKit.Lit("CorteRoca", new Color(0.20f, 0.18f, 0.16f), 0.05f);
        public static readonly Material Pipe = FarmKit.Lit("Tuberia", new Color(0.86f, 0.86f, 0.82f), 0.4f);
        public static readonly Material SpreaderFloat = FarmKit.Lit("Esparcidor", new Color(0.95f, 0.55f, 0.10f), 0.35f);
    }
}
