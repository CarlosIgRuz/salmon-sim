using System.Collections.Generic;
using UnityEngine;

/// <summary>Una fila de la tabla de salmones: ID y tres valores numéricos (ver <see cref="IFishSource.Columns"/>).</summary>
public struct FishRow
{
    public int id;
    public float c1, c2, c3;
    /// Si no es null, se muestra en lugar del número de la columna 1 (p. ej. "≈ 0 (en el lugar)").
    public string c1Text;
}

/// <summary>Resumen de una jaula para el panel.</summary>
public struct SchoolStats
{
    public int count;
    public float meanSpeed;     // m/s
    public float meanDepth;     // m bajo la superficie
    /// 0 = direcciones desordenadas, 1 = todos nadan hacia el mismo lado (|promedio de direcciones|).
    public float polarization;
    /// 0 = sin giro común, 1 = todos giran en el mismo sentido alrededor del eje de la jaula
    /// (|promedio de la componente tangencial de la dirección|).
    public float rotation;
    /// false si no hay suficientes peces en movimiento para hablar de dirección (polarización y rotación = n/d).
    public bool directionValid;
    /// Por qué la dirección no es válida (se muestra en el panel).
    public string directionNote;
    /// Densidad en la franja de ±1 m alrededor de la profundidad mediana, relativa a la densidad
    /// media en toda la profundidad de la red (1 = repartidos; 4 = muy concentrados). NaN si no aplica.
    public float concentration;
}

/// <summary>
/// Lo que el panel de salmones necesita de una jaula, venga de datos reales
/// (TrajectoryPlayer) o de la simulación (FishSchool).
/// </summary>
public interface IFishSource
{
    /// Encabezados: "ID" + 3 columnas (pueden llevar \n).
    string[] Columns { get; }
    /// Texto explicativo bajo la tabla.
    string Notes { get; }
    /// Llena `into` con los peces visibles, ordenados por ID.
    void GetRows(List<FishRow> into);
    SchoolStats GetStats();
    /// Línea extra bajo el resumen (temperatura, alimentación...), o null.
    string StatusLine { get; }
    /// Pez bajo el rayo (clic en 3D), o false.
    bool TryPick(Ray ray, out int id);
    /// Resalta `id` (o ninguno con -1) y atenúa al resto. Se llama en cada frame; debe ser barato.
    void SetSelection(int id, Color highlight, float emission, float dimFactor);
}

/// <summary>Métricas de dirección compartidas por las fuentes de peces.</summary>
public static class FishMetrics
{
    /// Polarización y orden de rotación a partir de posiciones horizontales relativas al eje
    /// de la jaula (x,z) y velocidades. Solo cuentan los vectores no nulos.
    public static void Direction(IReadOnlyList<Vector3> pos, IReadOnlyList<Vector3> vel, int n,
                                 out float polarization, out float rotation)
    {
        Vector3 dirSum = Vector3.zero;
        float rotSum = 0f;
        int nd = 0, nr = 0;
        for (int i = 0; i < n; i++)
        {
            float s = vel[i].magnitude;
            if (s < 1e-4f) continue;
            var d = vel[i] / s;
            dirSum += d;
            nd++;
            var r = new Vector2(pos[i].x, pos[i].z);
            if (r.sqrMagnitude < 0.09f) continue; // en el eje no hay sentido de giro
            r.Normalize();
            rotSum += r.x * d.z - r.y * d.x; // (r̂ × v̂)·ŷ con signo: +1 antihorario visto desde arriba
            nr++;
        }
        polarization = nd > 0 ? dirSum.magnitude / nd : 0f;
        rotation = nr > 0 ? Mathf.Abs(rotSum) / nr : 0f;
    }

    /// Concentración vertical: fracción de peces a ±1 m de la profundidad mediana,
    /// dividida por la fracción que habría con densidad uniforme en toda la red.
    public static float VerticalConcentration(List<float> depths, float netDepth)
    {
        int n = depths.Count;
        if (n == 0 || netDepth <= 2f) return float.NaN;
        depths.Sort();
        float median = depths[n / 2];
        int inBand = 0;
        foreach (float d in depths) if (Mathf.Abs(d - median) <= 1f) inBand++;
        float lo = Mathf.Max(0f, median - 1f), hi = Mathf.Min(netDepth, median + 1f);
        return (inBand / (float)n) / ((hi - lo) / netDepth);
    }
}

/// <summary>
/// Resaltado de peces hechos de Renderers (los de TrajectoryPlayer) sin tocar los
/// materiales compartidos: atenúa con MaterialPropertyBlock y usa una copia propia
/// del material para el resaltado (el emissive de URP necesita un keyword, que un
/// property block no puede activar).
/// </summary>
public class RendererHighlighter
{
    enum Look { Normal, Dim, Selected }
    readonly Dictionary<int, Look> applied = new();
    readonly Dictionary<Renderer, Material> originalMat = new();
    readonly Dictionary<Material, Material> highlightMats = new(); // original -> copia resaltada
    MaterialPropertyBlock mpb;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    public void Apply(IReadOnlyList<int> ids, System.Func<int, Transform> getFish, int selected,
                      Color highlight, float emission, float dimFactor)
    {
        foreach (int id in ids)
        {
            var want = id == selected ? Look.Selected : selected >= 0 ? Look.Dim : Look.Normal;
            if (applied.TryGetValue(id, out var have) ? have == want : want == Look.Normal) continue;
            // Se aplica también a peces ocultos: al reaparecer conservan su aspecto.
            SetLook(getFish(id), want, highlight, emission, dimFactor);
            applied[id] = want;
        }
    }

    void SetLook(Transform root, Look look, Color highlight, float emission, float dimFactor)
    {
        if (root == null) return;
        mpb ??= new MaterialPropertyBlock();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (!originalMat.TryGetValue(r, out var orig)) originalMat[r] = orig = r.sharedMaterial;
            if (orig == null) continue;
            switch (look)
            {
                case Look.Normal:
                    r.sharedMaterial = orig;
                    r.SetPropertyBlock(null);
                    break;
                case Look.Dim:
                    r.sharedMaterial = orig;
                    var c = orig.color * dimFactor;
                    c.a = orig.color.a;
                    mpb.Clear();
                    mpb.SetColor(BaseColorId, c);
                    mpb.SetColor(ColorId, c);
                    r.SetPropertyBlock(mpb);
                    break;
                case Look.Selected:
                    r.SetPropertyBlock(null);
                    r.sharedMaterial = HighlightFor(orig, highlight, emission);
                    break;
            }
        }
    }

    Material HighlightFor(Material orig, Color highlight, float emission)
    {
        if (highlightMats.TryGetValue(orig, out var m)) return m;
        m = new Material(orig) { name = orig.name + " (resaltado)", color = highlight };
        FarmKit.SetEmission(m, highlight * emission);
        return highlightMats[orig] = m;
    }

    public void Dispose()
    {
        foreach (var kv in originalMat)
            if (kv.Key != null) { kv.Key.sharedMaterial = kv.Value; kv.Key.SetPropertyBlock(null); }
        applied.Clear();
        foreach (var m in highlightMats.Values) Object.Destroy(m);
        highlightMats.Clear();
    }
}
