using UnityEngine;

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

    /// Largo del modelo a escala 1 (m): de la punta del hocico al borde de la cola.
    public const float ModelLength = 0.83f;
    static Mesh sharedMesh;

    /// La misma forma que <see cref="Create"/> en una sola malla (sin jerarquía), para GPU instancing.
    /// El aleteo lo hace el shader SalmonSim/FishInstanced.
    public static Mesh SharedMesh()
    {
        if (sharedMesh != null) return sharedMesh;
        var parts = new[]
        {
            new CombineInstance { mesh = FarmKit.Sphere, transform = Matrix4x4.Scale(new Vector3(0.16f, 0.22f, 0.75f)) },
            new CombineInstance { mesh = FarmKit.Cube, transform = Matrix4x4.TRS(new Vector3(0f, 0f, -0.37f), Quaternion.identity, new Vector3(0.02f, 0.22f, 0.16f)) },
            new CombineInstance { mesh = FarmKit.Cube, transform = Matrix4x4.TRS(new Vector3(0f, 0.12f, 0.02f), Quaternion.Euler(-15f, 0f, 0f), new Vector3(0.015f, 0.10f, 0.20f)) },
        };
        sharedMesh = new Mesh { name = "SalmonInstanciado" };
        sharedMesh.CombineMeshes(parts, true, true);
        return sharedMesh;
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
