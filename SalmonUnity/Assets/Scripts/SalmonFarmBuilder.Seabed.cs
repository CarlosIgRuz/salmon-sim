using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Fondo del lago bajo la salmonera (batimetría suave, sedimento, rocas), sistema de
/// fondeo (cabo + cadena desde las esquinas del módulo hasta anclas en el fondo) y la
/// geometría de la vista "Corte lateral" (cara del corte, velo de agua y telón de fondo).
/// </summary>
public partial class SalmonFarmBuilder
{
    [Header("Fondo y fondeo")]
    [Tooltip("Profundidad (m) del fondo bajo la salmonera")]
    public float seabedDepth = 35f;
    [Tooltip("Pendiente del fondo hacia el centro del lago (m de profundidad por m horizontal)")]
    public float seabedSlope = 0.035f;
    [Tooltip("Amplitud (m) de las irregularidades del fondo")]
    public float seabedRoughness = 1.5f;
    public int rockCount = 70;
    [Tooltip("Alcance de cada línea de fondeo: distancia horizontal al ancla / profundidad")]
    public float mooringScope = 3f;

    /// Límites del módulo (bordes exteriores de los collares): x = X del mundo, y = Z del mundo.
    public Rect ModuleBounds { get; private set; }
    /// |x| máximo de las anclas (para encuadrar el corte lateral).
    public float MooringReach { get; private set; }
    public IReadOnlyList<Vector3> Anchors => anchors;
    /// Plano del corte lateral: justo delante (sur) del módulo.
    public float SectionCutZ => ModuleBounds.yMin - 1f;
    public float SectionHalfWidth => MooringReach + 15f;
    /// Borde inferior del corte (m, y negativa): algo bajo el punto más hondo del perfil.
    public float SectionBottom { get; private set; }

    readonly List<Vector3> anchors = new();
    Vector3[] shoreInnerRing;
    GameObject sectionRoot;
    Material backdropMat;

    /// Altura (y, negativa) del fondo en (x, z): ~seabedDepth bajo la salmonera, más hondo
    /// hacia el centro del lago, con irregularidades suaves (dos escalas de ruido).
    public float BedY(float x, float z)
    {
        float r = Mathf.Sqrt(x * x + z * z);
        float depth = seabedDepth + seabedSlope * (moduleRadius * 0.6f - r);
        depth += (Mathf.PerlinNoise(x * 0.018f + 11f, z * 0.018f + 23f) - 0.5f) * 3.2f * seabedRoughness;
        depth += (Mathf.PerlinNoise(x * 0.07f + 3f, z * 0.07f + 41f) - 0.5f) * 1.2f * seabedRoughness;
        return -Mathf.Max(depth, 2f);
    }

    /// Profundidad del agua (m) en (x, z); la usa CurrentField.
    public float WaterDepth(float x, float z) => -BedY(x, z);

    // ------------------------------------------------------------------ Fondo

    /// Fondo en anillos concéntricos (más finos cerca de la salmonera), cosido al primer
    /// anillo de la orilla. Lleva UV para la textura de sedimento.
    void BuildSeabed(Transform parent)
    {
        int around = shoreInnerRing.Length;
        float rIn = lakeRadius * 0.58f;
        const int rings = 48;
        var g = new Vector3[rings + 1, around];
        for (int k = 1; k <= rings; k++)
            for (int i = 0; i < around; i++)
            {
                if (k == rings) { g[k, i] = shoreInnerRing[i]; continue; }
                float r = rIn * Mathf.Pow(k / (float)rings, 1.5f);
                float a = i * Mathf.PI * 2f / around;
                float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
                g[k, i] = new Vector3(x, BedY(x, z), z);
            }
        var center = new Vector3(0f, BedY(0f, 0f), 0f);

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            if (Vector3.Cross(b - a, c - a).y < 0f) (b, c) = (c, b);
            foreach (var v in new[] { a, b, c })
            {
                tris.Add(verts.Count);
                verts.Add(v);
                uvs.Add(new Vector2(v.x, v.z) / 6f);
            }
        }
        for (int i = 0; i < around; i++)
        {
            int j = (i + 1) % around;
            Tri(center, g[1, i], g[1, j]);
            for (int k = 1; k < rings; k++)
            {
                Tri(g[k, i], g[k + 1, i], g[k + 1, j]);
                Tri(g[k, i], g[k + 1, j], g[k, j]);
            }
        }
        var mesh = new Mesh { name = "Fondo", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var mat = FarmKit.Lit("Sedimento", new Color(0.92f, 0.90f, 0.84f), 0.05f);
        var tex = SedimentTexture(seed);
        mat.mainTexture = tex;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        MeshBuilder.AddRenderer(parent, "Fondo (sedimento)", mesh, mat, false);

        // Borde inferior del corte lateral
        SectionBottom = 0f;
        for (float x = -SectionHalfWidthEstimate(); x <= SectionHalfWidthEstimate(); x += 2f)
            SectionBottom = Mathf.Min(SectionBottom, BedY(x, SectionCutZ));
        SectionBottom -= 6f;
    }

    float SectionHalfWidthEstimate() => ModuleBounds.width * 0.5f + 5f + mooringScope * seabedDepth + 15f;

    /// Textura de fango/sedimento repetible: ruido fBm (cosido en los bordes) + piedrecitas.
    static Texture2D SedimentTexture(int seed)
    {
        const int n = 256;
        var tex = new Texture2D(n, n, TextureFormat.RGB24, true)
        {
            name = "Sedimento", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear,
            anisoLevel = 4, hideFlags = HideFlags.HideAndDontSave,
        };
        var dark = new Color(0.17f, 0.17f, 0.13f);
        var light = new Color(0.40f, 0.37f, 0.27f);
        // Ruido con período 1 en (u, v): mezcla de 4 muestras desplazadas en un período.
        float Tile(float u, float v, float f, float o)
        {
            float N(float a, float b) => Mathf.PerlinNoise(a * f + o, b * f + o * 1.7f);
            return N(u, v) * (1 - u) * (1 - v) + N(u - 1, v) * u * (1 - v)
                 + N(u, v - 1) * (1 - u) * v + N(u - 1, v - 1) * u * v;
        }
        var px = new Color[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)n, v = y / (float)n;
                float h = 0.55f * Tile(u, v, 4f, 3f) + 0.30f * Tile(u, v, 11f, 17f) + 0.15f * Tile(u, v, 29f, 41f);
                h = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.7f, h));
                px[y * n + x] = Color.Lerp(dark, light, h);
            }
        var rnd = new System.Random(seed + 3);
        for (int s = 0; s < 220; s++)
        {
            int cx = rnd.Next(n), cy = rnd.Next(n), r = 1 + rnd.Next(3);
            float shade = (float)rnd.NextDouble() < 0.5f ? 0.6f : 1.35f;
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > r * r) continue;
                    int i = ((cy + dy + n) % n) * n + (cx + dx + n) % n;
                    px[i] *= shade;
                }
        }
        tex.SetPixels(px);
        tex.Apply(true);
        return tex;
    }

    /// Rocas low-poly sobre el fondo, más densas cerca de la salmonera.
    void BuildRocks(Transform parent, System.Random rnd)
    {
        var mb = new MeshBuilder();
        for (int i = 0; i < rockCount; i++)
        {
            float a = Rand(rnd, 0f, Mathf.PI * 2f);
            float r = Mathf.Lerp(6f, 170f, Mathf.Pow((float)rnd.NextDouble(), 1.3f));
            float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
            float size = 0.5f + 2.5f * Mathf.Pow((float)rnd.NextDouble(), 3f);
            Rock(mb, rnd, new Vector3(x, BedY(x, z) + size * 0.1f, z), size);
        }
        MeshBuilder.AddRenderer(parent, "Rocas", mb.ToMesh("Rocas"), Mats.SeabedRock, false);
    }

    static void Rock(MeshBuilder mb, System.Random rnd, Vector3 c, float size)
    {
        const int lat = 3, lon = 7;
        var ring = new Vector3[lat, lon];
        float yaw = Rand(rnd, 0f, Mathf.PI * 2f), squash = Rand(rnd, 0.45f, 0.7f);
        for (int k = 0; k < lat; k++)
        {
            float phi = Mathf.PI * (k + 1) / (lat + 1);
            for (int i = 0; i < lon; i++)
            {
                float th = yaw + (i + Rand(rnd, -0.25f, 0.25f)) * Mathf.PI * 2f / lon;
                float rr = size * Rand(rnd, 0.75f, 1.2f);
                ring[k, i] = c + new Vector3(Mathf.Sin(phi) * Mathf.Cos(th) * rr, Mathf.Cos(phi) * rr * squash,
                                             Mathf.Sin(phi) * Mathf.Sin(th) * rr);
            }
        }
        var top = c + Vector3.up * size * squash * Rand(rnd, 0.8f, 1.1f);
        var bottom = c + Vector3.down * size * squash * 0.8f;
        for (int i = 0; i < lon; i++)
        {
            int j = (i + 1) % lon;
            mb.Tri(0, top, ring[0, i], ring[0, j], (top + ring[0, i] + ring[0, j]) / 3f - c);
            for (int k = 0; k + 1 < lat; k++)
            {
                mb.Tri(0, ring[k, i], ring[k + 1, i], ring[k + 1, j], (ring[k, i] + ring[k + 1, j]) * 0.5f - c);
                mb.Tri(0, ring[k, i], ring[k + 1, j], ring[k, j], (ring[k, i] + ring[k + 1, j]) * 0.5f - c);
            }
            mb.Tri(0, bottom, ring[lat - 1, j], ring[lat - 1, i], (bottom + ring[lat - 1, i] + ring[lat - 1, j]) / 3f - c);
        }
    }

    // ------------------------------------------------------------------ Fondeo

    /// Dos líneas por esquina del módulo (hacia afuera en X y en Z): brida hasta una boya,
    /// cabo que baja colgando, cadena que se apoya en el fondo y ancla de arrastre.
    void BuildMooring(Transform parent)
    {
        var b = new MeshBatch();
        anchors.Clear();
        MooringReach = 0f;
        var m = ModuleBounds;
        foreach (float sx in new[] { -1f, 1f })
            foreach (float sz in new[] { -1f, 1f })
            {
                var corner = new Vector3(sx > 0 ? m.xMax : m.xMin, 0.25f, sz > 0 ? m.yMax : m.yMin);
                MooringLine(b, corner, new Vector3(sx, 0f, 0f));
                MooringLine(b, corner, new Vector3(0f, 0f, sz));
            }
        b.Build(parent, "Fondeo", false);
    }

    void MooringLine(MeshBatch b, Vector3 corner, Vector3 dir)
    {
        var buoy = corner + dir * 5f;
        buoy.y = 0.2f;
        b.Sphere(Mats.Buoy, buoy, Vector3.one * 1.3f, Quaternion.identity);
        Segment(b, Mats.Rope, corner, buoy - dir * 0.6f, 0.06f);

        float L = mooringScope * seabedDepth;
        var anchor = buoy + dir * L;
        anchor.y = BedY(anchor.x, anchor.z);
        var touch = buoy + dir * (L * 0.62f);
        float yTop = buoy.y - 0.6f, yTouch = BedY(touch.x, touch.z) + 0.15f;
        // Tramo colgante: forma de catenaria (horizontal al tocar el fondo); cabo arriba, cadena abajo.
        var prev = new Vector3(buoy.x, yTop, buoy.z);
        const int hang = 18;
        for (int i = 1; i <= hang; i++)
        {
            float s = i / (float)hang;
            var p = Vector3.Lerp(buoy, touch, s);
            p.y = yTouch + (yTop - yTouch) * (1f - s) * (1f - s);
            bool chain = s > 0.7f;
            Segment(b, chain ? Mats.Chain : Mats.Rope, prev, p, chain ? 0.14f : 0.08f);
            prev = p;
        }
        // Cadena apoyada en el fondo hasta el ancla
        for (int i = 1; i <= 8; i++)
        {
            var p = Vector3.Lerp(touch, anchor, i / 8f);
            p.y = BedY(p.x, p.z) + 0.15f;
            Segment(b, Mats.Chain, prev, p, 0.14f);
            prev = p;
        }
        Anchor(b, anchor, dir);
        anchors.Add(anchor);
        MooringReach = Mathf.Max(MooringReach, Mathf.Abs(anchor.x));
    }

    /// Ancla de arrastre (agrandada para que se vea): uña hacia afuera, caña hacia la salmonera, cepo.
    static void Anchor(MeshBatch b, Vector3 p, Vector3 dir)
    {
        var q = Quaternion.LookRotation(dir);
        b.Box(Mats.AnchorSteel, p + q * new Vector3(0f, 0.45f, -1.2f), new Vector3(0.35f, 0.35f, 3.6f), q * Quaternion.Euler(-8f, 0f, 0f));
        b.Box(Mats.AnchorSteel, p + q * new Vector3(0f, 0.35f, 1.0f), new Vector3(3f, 0.25f, 1.8f), q * Quaternion.Euler(30f, 0f, 0f));
        b.Box(Mats.AnchorSteel, p + q * new Vector3(0f, 0.7f, -2.8f), new Vector3(2.2f, 0.2f, 0.2f), q);
    }

    /// Cilindro de `a` a `c` (cabos, cadenas, tuberías).
    static void Segment(MeshBatch b, Material mat, Vector3 a, Vector3 c, float radius)
    {
        var d = c - a;
        if (d.sqrMagnitude < 1e-6f) return;
        b.Cylinder(mat, (a + c) * 0.5f, radius, d.magnitude, Quaternion.FromToRotation(Vector3.up, d));
    }

    // ------------------------------------------------------------------ Corte lateral

    /// Geometría que solo se ve en el corte: la cara del corte (sedimento y roca bajo el
    /// perfil del fondo), un velo azul sobre lo sumergido y un telón de agua al fondo.
    void BuildSection(Transform parent)
    {
        sectionRoot = parent.gameObject;
        // La cámara corta en SectionCutZ (plano cercano): la cara va apenas detrás.
        float z = SectionCutZ + 0.1f, W = SectionHalfWidth + 300f;

        var face = new MeshBuilder(2);
        const float sediment = 2.5f, bottom = -150f, step = 2f;
        for (float x = -W; x < W; x += step)
        {
            float x1 = x + step, y0 = BedY(x, z), y1 = BedY(x1, z);
            face.Quad(0, new Vector3(x, y0, z), new Vector3(x1, y1, z), new Vector3(x1, y1 - sediment, z),
                      new Vector3(x, y0 - sediment, z), Vector3.back);
            face.Quad(1, new Vector3(x, y0 - sediment, z), new Vector3(x1, y1 - sediment, z), new Vector3(x1, bottom, z),
                      new Vector3(x, bottom, z), Vector3.back);
        }
        MeshBuilder.AddRenderer(parent, "Cara del corte", face.ToMesh("Corte"), new[] { Mats.CutSediment, Mats.CutRock }, false);

        // Velo de agua delante de todo lo sumergido (se dibuja al final)
        var veil = FarmKit.Transparent("Velo de agua", new Color(0.10f, 0.45f, 0.62f, 0.14f), 3050);
        MeshBuilder.AddRenderer(parent, "Velo de agua", Quad(new Vector3(-W, bottom, z - 0.05f), new Vector3(W, 0f, z - 0.05f),
                                Color.white, Color.white), veil, false);

        // Telón de agua al fondo de la vista (antes que todo, sin escribir profundidad)
        backdropMat = FarmKit.Transparent("Telón de agua", Color.white, 1000);
        float zb = SectionCutZ + 86f;
        MeshBuilder.AddRenderer(parent, "Telón", Quad(new Vector3(-W, bottom, zb), new Vector3(W, 0f, zb),
                                new Color(0.01f, 0.06f, 0.10f), new Color(0.12f, 0.42f, 0.52f)), backdropMat, false);
        sectionRoot.SetActive(false);
    }

    /// Rectángulo vertical (plano XY) de `min` a `max`, con color abajo/arriba.
    static Mesh Quad(Vector3 min, Vector3 max, Color bottom, Color top)
    {
        var m = new Mesh { name = "Quad" };
        m.SetVertices(new[] { new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                              new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z) });
        m.SetColors(new[] { bottom, bottom, top, top });
        m.SetTriangles(new[] { 0, 2, 1, 0, 3, 2 }, 0);
        m.RecalculateBounds();
        return m;
    }
}
