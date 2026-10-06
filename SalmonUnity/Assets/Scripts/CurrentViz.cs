using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Visualización opcional de <see cref="CurrentField"/>:
/// - Jaula abierta: partículas finas en suspensión que derivan con la corriente (estelas cortas).
/// - Vista general: flechas sobre el agua alrededor del módulo (largo ∝ rapidez).
/// - Corte lateral: perfil de flechas por profundidad a cada lado del módulo (componente
///   en el plano del corte), con la rapidez arriba y abajo.
/// </summary>
public class CurrentViz : MonoBehaviour
{
    public SalmonFarmBuilder farm;
    public FarmNavigator nav;
    public CurrentField field;
    [Tooltip("Mostrar la corriente (lo cambia el botón de la barra superior)")]
    public bool show = true;
    public int particleCount = 700;
    [Tooltip("Largo de flecha (m) por cada m/s de corriente")]
    public float arrowScale = 60f;
    [Tooltip("Separación (m) de la grilla de flechas en la vista general")]
    public float arrowSpacing = 28f;

    Vector3[] particles;
    float[] particleSink;
    Matrix4x4[] particleMatrices;
    FarmCage particleCage;
    Mesh arrowMesh, speckMesh;
    Material arrowMat, speckMat;
    GUIStyle label;
    Texture2D texLabel;
    readonly System.Random rnd = new(5);

    const float ParticleHalf = 13f;

    void OnDestroy()
    {
        if (texLabel != null) Destroy(texLabel);
    }

    void LateUpdate()
    {
        if (!show || field == null || nav == null || nav.cam == null) return;
        Init();
        switch (nav.cam.CurrentMode)
        {
            case FarmCamera.Mode.Cage:
                if (nav.Current != null) Particles(nav.Current);
                break;
            case FarmCamera.Mode.Overview:
                SurfaceArrows();
                break;
            case FarmCamera.Mode.Section:
                ProfileArrows();
                break;
        }
    }

    void Init()
    {
        if (arrowMesh != null) return;
        arrowMesh = BuildArrow();
        speckMesh = BuildSpeck();
        arrowMat = FarmKit.Transparent("Flecha de corriente", new Color(1f, 0.82f, 0.25f, 0.9f), 3060);
        speckMat = new Material(Resources.Load<Shader>("SalmonFishInstanced")) { name = "Partículas", enableInstancing = true };
        speckMat.SetColor("_BaseColor", new Color(0.75f, 0.85f, 0.85f));
        speckMat.SetColor("_BellyColor", new Color(0.75f, 0.85f, 0.85f));
        speckMat.SetColor("_Emission", new Color(0.35f, 0.42f, 0.42f));
        speckMat.SetFloat("_WagAmp", 0f);
        particles = new Vector3[particleCount];
        particleSink = new float[particleCount];
        particleMatrices = new Matrix4x4[particleCount];
    }

    float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

    // ------------------------------------------------------------------ Jaula: partículas

    /// Partículas en una caja alrededor de la jaula; las que salen por un lado entran por el opuesto.
    void Particles(FarmCage cage)
    {
        var c = cage.transform.position;
        float bottom = cage.netDepth + 6f;
        if (particleCage != cage)
        {
            particleCage = cage;
            for (int i = 0; i < particles.Length; i++)
            {
                particles[i] = c + new Vector3(R(-ParticleHalf, ParticleHalf), R(-bottom, -0.3f), R(-ParticleHalf, ParticleHalf));
                particleSink[i] = R(0.002f, 0.015f);
            }
        }
        float dt = Time.deltaTime, t = Time.time;
        for (int i = 0; i < particles.Length; i++)
        {
            var p = particles[i];
            var u = field.At(p, t);
            p += (u + Vector3.down * particleSink[i]) * dt;
            var d = p - c;
            d.x = Mathf.Repeat(d.x + ParticleHalf, 2f * ParticleHalf) - ParticleHalf;
            d.z = Mathf.Repeat(d.z + ParticleHalf, 2f * ParticleHalf) - ParticleHalf;
            if (d.y < -bottom) d.y = -0.4f;
            p = c + d;
            particles[i] = p;
            // Estela corta en la dirección de la corriente: su largo muestra la rapidez.
            float len = 0.035f + u.magnitude * 1.4f;
            var rot = u.sqrMagnitude > 1e-8f ? Quaternion.LookRotation(u) : Quaternion.identity;
            particleMatrices[i] = Matrix4x4.TRS(p, rot, new Vector3(0.035f, 0.035f, len));
        }
        var rp = new RenderParams(speckMat)
        {
            worldBounds = new Bounds(c + Vector3.down * bottom * 0.5f, new Vector3(2f * ParticleHalf + 2f, bottom + 2f, 2f * ParticleHalf + 2f)),
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
        };
        Graphics.RenderMeshInstanced(rp, speckMesh, 0, particleMatrices, particles.Length);
    }

    // ------------------------------------------------------------------ Vista general: flechas

    void SurfaceArrows()
    {
        var m = farm.ModuleBounds;
        float ext = 70f, t = Time.time;
        for (float x = m.xMin - ext; x <= m.xMax + ext + 0.1f; x += arrowSpacing)
            for (float z = m.yMin - ext; z <= m.yMax + ext + 0.1f; z += arrowSpacing)
            {
                // Fuera del módulo, para no tapar las jaulas.
                if (x > m.xMin - 8f && x < m.xMax + 8f && z > m.yMin - 8f && z < m.yMax + 8f) continue;
                var p = new Vector3(x, 0.35f, z);
                var u = field.At(new Vector3(x, -0.5f, z), t);
                float len = u.magnitude * arrowScale;
                if (len < 0.3f) continue;
                // Centrada en el punto de la grilla.
                var q = Quaternion.LookRotation(u);
                DrawArrow(p - u.normalized * len * 0.5f, q, Mathf.Clamp(len * 0.35f, 1.2f, 3.5f), len);
            }
    }

    // ------------------------------------------------------------------ Corte lateral: perfil

    /// Columnas de flechas a ambos lados del módulo, cada 3 m de profundidad hasta el fondo.
    void ProfileArrows()
    {
        float t = Time.time, z = farm.SectionCutZ + 0.6f;
        foreach (float x in ProfileColumns())
        {
            float bed = -farm.BedY(x, z);
            for (float d = 0.8f; d < bed - 0.5f; d += 3f)
            {
                var u = field.At(new Vector3(x, -d, z), t);
                float len = Mathf.Abs(u.x) * arrowScale;
                if (len < 0.3f) continue;
                var dir = new Vector3(Mathf.Sign(u.x), 0f, 0f);
                // La cara de la flecha mira a la cámara (-Z); su ancho queda vertical.
                var q = Quaternion.LookRotation(dir, Vector3.back);
                DrawArrow(new Vector3(x, -d, z) - dir * len * 0.5f, q, 1.4f, len);
            }
        }
    }

    float[] ProfileColumns()
    {
        var m = farm.ModuleBounds;
        return new[] { m.xMin - 28f, m.xMax + 28f };
    }

    void DrawArrow(Vector3 tail, Quaternion q, float width, float len)
    {
        var rp = new RenderParams(arrowMat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
        Graphics.RenderMesh(rp, arrowMesh, 0, Matrix4x4.TRS(tail, q, new Vector3(width, 1f, len)));
    }

    void OnGUI()
    {
        if (!show || field == null || nav == null || nav.cam == null || nav.cam.CurrentMode != FarmCamera.Mode.Section) return;
        if (label == null)
        {
            texLabel = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texLabel.SetPixel(0, 0, new Color(0.05f, 0.08f, 0.10f, 0.75f));
            texLabel.Apply();
            label = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter, wordWrap = false, padding = new RectOffset(5, 5, 1, 2),
                normal = { textColor = new Color(1f, 0.88f, 0.45f), background = texLabel },
            };
        }
        label.fontSize = Mathf.RoundToInt(Mathf.Max(10f, Screen.height / 62f));
        float t = Time.time, z = farm.SectionCutZ + 0.6f;
        foreach (float x in ProfileColumns())
        {
            float bed = -farm.BedY(x, z);
            float top = Mathf.Abs(field.At(new Vector3(x, -0.8f, z), t).x);
            float low = Mathf.Abs(field.At(new Vector3(x, -(bed - 2f), z), t).x);
            Label(new Vector3(x, 2.5f, z), $"Corriente (en el corte)\n{top:F3} m/s arriba");
            Label(new Vector3(x, -bed - 0.5f, z), $"{low:F3} m/s cerca del fondo", below: true);
        }
    }

    void Label(Vector3 world, string text, bool below = false)
    {
        var cam = nav.cam.Cam;
        var sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0f || sp.x < 0f || sp.x > Screen.width) return;
        var c = new GUIContent(text);
        var s = label.CalcSize(c);
        float y = Screen.height - sp.y + (below ? 4f : -s.y);
        GUI.Label(new Rect(sp.x - s.x * 0.5f, y, s.x, s.y), c, label);
    }

    // ------------------------------------------------------------------ Mallas

    /// Flecha plana en el plano XZ que apunta a +Z, de z=0 a z=1 y ancho 1 (cabeza).
    static Mesh BuildArrow()
    {
        const float shaft = 0.16f, headStart = 0.62f;
        var m = new Mesh { name = "Flecha" };
        m.SetVertices(new[]
        {
            new Vector3(-shaft, 0f, 0f), new Vector3(shaft, 0f, 0f), new Vector3(shaft, 0f, headStart), new Vector3(-shaft, 0f, headStart),
            new Vector3(-0.5f, 0f, headStart), new Vector3(0.5f, 0f, headStart), new Vector3(0f, 0f, 1f),
        });
        m.SetTriangles(new[] { 0, 3, 2, 0, 2, 1, 4, 6, 5 }, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// Octaedro alargado en Z (centrado), para las estelas de las partículas.
    static Mesh BuildSpeck()
    {
        var mb = new MeshBuilder();
        var v = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        for (int sx = 0; sx < 2; sx++)
            for (int sy = 2; sy < 4; sy++)
                for (int sz = 4; sz < 6; sz++)
                    mb.Tri(0, v[sx] * 0.5f, v[sy] * 0.5f, v[sz] * 0.5f, v[sx] + v[sy] + v[sz]);
        return mb.ToMesh("Partícula");
    }
}
