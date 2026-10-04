using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Arma sola la escena al dar Play: agua (niebla azul), luz, red de la jaula,
/// camara orbital y HUD. Si no hay un TrajectoryPlayer en la escena, lo crea.
/// </summary>
public static class SalmonSceneBuilder
{
    static readonly Color WaterColor = new Color(0.03f, 0.20f, 0.30f);
    public static readonly Vector3 CageCenter = new Vector3(0f, 0f, 5f);
    const float CageRadius = 7.5f;
    const float CageHalfHeight = 3.2f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Build()
    {
        var player = Object.FindFirstObjectByType<TrajectoryPlayer>();
        if (player == null)
            player = new GameObject("Cage").AddComponent<TrajectoryPlayer>();

        SetupWater();
        SetupLight();
        BuildNet(player.transform);
        SetupCamera();

        if (player.GetComponent<SalmonHud>() == null)
            player.gameObject.AddComponent<SalmonHud>().player = player;
        if (player.GetComponent<SalmonPanel>() == null)
            player.gameObject.AddComponent<SalmonPanel>().player = player;
    }

    static void SetupWater()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = WaterColor;
        RenderSettings.fogDensity = 0.035f;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.30f, 0.50f, 0.60f);
    }

    static void SetupLight()
    {
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            l.color = new Color(0.75f, 0.92f, 1f);
            l.intensity = 1.3f;
            l.transform.rotation = Quaternion.Euler(70f, 20f, 0f);
        }
    }

    static void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = WaterColor;
        cam.fieldOfView = 60f;
        var orbit = cam.GetComponent<CameraOrbit>();
        if (orbit == null) orbit = cam.gameObject.AddComponent<CameraOrbit>();
        orbit.target = CageCenter;
    }

    static void BuildNet(Transform parent)
    {
        var root = new GameObject("Net");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = CageCenter;

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        var mat = new Material(shader);
        var netColor = new Color(0.6f, 0.85f, 0.95f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", netColor);
        mat.color = netColor;

        // Anillos horizontales
        const int rings = 5, seg = 64;
        for (int i = 0; i < rings; i++)
        {
            float y = Mathf.Lerp(-CageHalfHeight, CageHalfHeight, i / (rings - 1f));
            var pts = new Vector3[seg];
            for (int s = 0; s < seg; s++)
            {
                float a = s / (float)seg * Mathf.PI * 2f;
                pts[s] = new Vector3(Mathf.Cos(a) * CageRadius, y, Mathf.Sin(a) * CageRadius);
            }
            MakeLine(root.transform, mat, pts, true);
        }

        // Lineas verticales
        const int verts = 28;
        for (int v = 0; v < verts; v++)
        {
            float a = v / (float)verts * Mathf.PI * 2f;
            float x = Mathf.Cos(a) * CageRadius, z = Mathf.Sin(a) * CageRadius;
            MakeLine(root.transform, mat,
                new[] { new Vector3(x, -CageHalfHeight, z), new Vector3(x, CageHalfHeight, z) }, false);
        }
    }

    static void MakeLine(Transform parent, Material mat, Vector3[] pts, bool loop)
    {
        var go = new GameObject("NetLine");
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.loop = loop;
        lr.positionCount = pts.Length;
        lr.SetPositions(pts);
        lr.widthMultiplier = 0.03f;
        lr.sharedMaterial = mat;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
    }
}

/// <summary>Construye un salmon procedural (cuerpo, cola con aleteo, aleta dorsal).</summary>
public static class FishFactory
{
    static Material bodyMat, finMat;

    public static GameObject Create(Transform parent)
    {
        var root = new GameObject("Salmon");
        root.transform.SetParent(parent, false);
        root.transform.localScale = Vector3.one * Random.Range(0.85f, 1.15f);

        // El pez mira hacia +Z; la cola queda en -Z.
        Part(PrimitiveType.Sphere, root.transform, new Vector3(0f, 0f, 0f),
             new Vector3(0.16f, 0.22f, 0.75f), true);

        var tailPivot = new GameObject("TailPivot").transform;
        tailPivot.SetParent(root.transform, false);
        tailPivot.localPosition = new Vector3(0f, 0f, -0.30f);
        Part(PrimitiveType.Cube, tailPivot, new Vector3(0f, 0f, -0.07f),
             new Vector3(0.02f, 0.22f, 0.16f), false);

        var dorsal = Part(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.12f, 0.02f),
             new Vector3(0.015f, 0.10f, 0.20f), false);
        dorsal.localRotation = Quaternion.Euler(-15f, 0f, 0f);

        var wag = root.AddComponent<FishWag>();
        wag.tail = tailPivot;
        wag.phase = Random.Range(0f, Mathf.PI * 2f);
        wag.freq = Random.Range(5.5f, 7.5f);
        return root;
    }

    static Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, bool body)
    {
        var go = GameObject.CreatePrimitive(type);
        Object.Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = body ? GetMat(ref bodyMat, r.sharedMaterial, new Color(0.62f, 0.70f, 0.75f), 0.7f)
                                : GetMat(ref finMat, r.sharedMaterial, new Color(0.38f, 0.45f, 0.52f), 0.3f);
        return go.transform;
    }

    static Material GetMat(ref Material cache, Material source, Color c, float smooth)
    {
        if (cache != null) return cache;
        cache = new Material(source) { color = c };
        if (cache.HasProperty("_Smoothness")) cache.SetFloat("_Smoothness", smooth);
        if (cache.HasProperty("_Metallic")) cache.SetFloat("_Metallic", 0.25f);
        return cache;
    }
}
