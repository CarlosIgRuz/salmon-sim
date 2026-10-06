using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Mallas y materiales compartidos para construir la salmonera por código:
/// primitivas de Unity, un cono low-poly y materiales URP Lit / transparentes.
/// </summary>
public static class FarmKit
{
    static Mesh cube, cylinder, sphere, cone;
    static Material litTemplate;

    public static Mesh Cube { get { Init(); return cube; } }
    public static Mesh Cylinder { get { Init(); return cylinder; } }
    public static Mesh Sphere { get { Init(); return sphere; } }
    public static Mesh Cone { get { Init(); return cone; } }

    static void Init()
    {
        if (cube != null) return;
        cube = Grab(PrimitiveType.Cube);
        cylinder = Grab(PrimitiveType.Cylinder);
        sphere = Grab(PrimitiveType.Sphere);
        cone = BuildCone(8);
    }

    static Mesh Grab(PrimitiveType type)
    {
        var go = GameObject.CreatePrimitive(type);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        if (litTemplate == null) litTemplate = go.GetComponent<Renderer>().sharedMaterial;
        Object.DestroyImmediate(go);
        return mesh;
    }

    /// Cono de radio 0,5 con la base en y=0 y la punta en y=1 (sombreado plano).
    static Mesh BuildCone(int sides)
    {
        var mb = new MeshBuilder();
        var apex = Vector3.up;
        for (int i = 0; i < sides; i++)
        {
            float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
            var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * 0.5f;
            var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * 0.5f;
            mb.Tri(0, p0, p1, apex, (p0 + p1).normalized + Vector3.up * 0.5f);
        }
        return mb.ToMesh("Cono");
    }

    /// Material URP Lit opaco.
    public static Material Lit(string name, Color c, float smooth = 0.2f, float metal = 0f)
    {
        Init();
        var m = new Material(litTemplate) { name = name, color = c };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
        return m;
    }

    public static Material Emissive(string name, Color c, float intensity)
    {
        var m = Lit(name, c);
        SetEmission(m, c * intensity);
        return m;
    }

    public static void SetEmission(Material m, Color e)
    {
        if (!m.HasProperty("_EmissionColor")) return;
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetColor("_EmissionColor", e);
    }

    /// Color plano semitransparente con niebla (shader propio en Resources).
    public static Material Transparent(string name, Color c, int queue)
    {
        var sh = Resources.Load<Shader>("SalmonUnlitTransparent");
        var m = new Material(sh) { name = name, renderQueue = queue };
        m.SetColor("_BaseColor", c);
        return m;
    }

    public static Material Water()
    {
        var sh = Resources.Load<Shader>("SalmonWater");
        return new Material(sh) { name = "Agua", renderQueue = 3000 };
    }
}

/// <summary>
/// Junta muchas primitivas en una malla por material: cientos de piezas
/// (barandas, postes, flotadores) quedan en pocas llamadas de dibujo.
/// Las posiciones son locales al objeto padre que recibe <see cref="Build"/>.
/// </summary>
public class MeshBatch
{
    readonly Dictionary<Material, List<CombineInstance>> parts = new();

    public void Add(Mesh mesh, Material mat, Matrix4x4 m)
    {
        if (!parts.TryGetValue(mat, out var list)) parts[mat] = list = new List<CombineInstance>();
        list.Add(new CombineInstance { mesh = mesh, transform = m });
    }

    public void Box(Material mat, Vector3 c, Vector3 size) => Box(mat, c, size, Quaternion.identity);
    public void Box(Material mat, Vector3 c, Vector3 size, Quaternion r) =>
        Add(FarmKit.Cube, mat, Matrix4x4.TRS(c, r, size));

    /// Cilindro centrado en c, con su eje a lo largo del eje Y de r.
    public void Cylinder(Material mat, Vector3 c, float radius, float length, Quaternion r) =>
        Add(FarmKit.Cylinder, mat, Matrix4x4.TRS(c, r, new Vector3(2f * radius, 0.5f * length, 2f * radius)));

    public void Sphere(Material mat, Vector3 c, Vector3 size, Quaternion r) =>
        Add(FarmKit.Sphere, mat, Matrix4x4.TRS(c, r, size));

    /// Cono con la base centrada en baseCenter, apuntando según el eje Y de r.
    public void Cone(Material mat, Vector3 baseCenter, float radius, float height, Quaternion r) =>
        Add(FarmKit.Cone, mat, Matrix4x4.TRS(baseCenter, r, new Vector3(2f * radius, height, 2f * radius)));

    public GameObject Build(Transform parent, string name, bool castShadows = true)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        foreach (var kv in parts)
        {
            var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(kv.Value.ToArray(), true, true);
            MeshBuilder.AddRenderer(root.transform, kv.Key.name, mesh, kv.Key, castShadows);
        }
        parts.Clear();
        return root;
    }
}

/// <summary>Construye mallas de triángulos sueltos (sombreado plano), con submallas.</summary>
public class MeshBuilder
{
    readonly List<Vector3> verts = new();
    readonly List<int>[] subs;

    public MeshBuilder(int subMeshes = 1)
    {
        subs = new List<int>[subMeshes];
        for (int i = 0; i < subMeshes; i++) subs[i] = new List<int>();
    }

    /// Triángulo cuya cara frontal apunta hacia `outward`.
    public void Tri(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) (b, c) = (c, b);
        int i = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        subs[sub].Add(i); subs[sub].Add(i + 1); subs[sub].Add(i + 2);
    }

    public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
    {
        Tri(sub, a, b, c, outward);
        Tri(sub, a, c, d, outward);
    }

    /// Cinta delgada de a a b, de ancho 2·halfWidth (para hilos de red).
    public void Strip(int sub, Vector3 a, Vector3 b, Vector3 halfWidth)
    {
        var n = Vector3.Cross(b - a, halfWidth);
        Quad(sub, a - halfWidth, b - halfWidth, b + halfWidth, a + halfWidth, n);
    }

    public Mesh ToMesh(string name)
    {
        var m = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
        m.SetVertices(verts);
        m.subMeshCount = subs.Length;
        for (int i = 0; i < subs.Length; i++) m.SetTriangles(subs[i], i);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    public static GameObject AddRenderer(Transform parent, string name, Mesh mesh, Material mat, bool castShadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
        return go;
    }

    public static GameObject AddRenderer(Transform parent, string name, Mesh mesh, Material[] mats, bool castShadows)
    {
        var go = AddRenderer(parent, name, mesh, mats[0], castShadows);
        go.GetComponent<MeshRenderer>().sharedMaterials = mats;
        return go;
    }
}
