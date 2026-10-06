using System.Collections.Generic;
using UnityEngine;

/// <summary>Una fila de la tabla de salmones: ID y tres valores numéricos (ver <see cref="IFishSource.Columns"/>).</summary>
public struct FishRow
{
    public int id;
    public float c1, c2, c3;
}

/// <summary>Resumen de una jaula para el panel.</summary>
public struct SchoolStats
{
    public int count;
    public float meanSpeed;     // m/s
    public float meanDepth;     // m bajo la superficie
    /// 0 = direcciones desordenadas, 1 = todos nadan hacia el mismo lado (|promedio de direcciones|).
    public float polarization;
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
    /// Pez bajo el rayo (clic en 3D), o false.
    bool TryPick(Ray ray, out int id);
    /// Resalta `id` (o ninguno con -1) y atenúa al resto. Se llama en cada frame; debe ser barato.
    void SetSelection(int id, Color highlight, float emission, float dimFactor);
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
