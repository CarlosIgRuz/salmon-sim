using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Cardumen simulado (boids) dentro de una jaula cuadrada. Reglas: separación,
/// alineación, cohesión, evitar la red (fuerza suave + límite duro: nunca la cruzan),
/// profundidad preferida y giro en anillo alrededor del centro.
/// Coordenadas locales de la jaula: superficie en y=0, red en |x|,|z| ≤ halfSize,
/// fondo en y = -netDepth.
/// Rendimiento: vecinos con grilla espacial; todos los peces se dibujan con una
/// malla y material compartidos (GPU instancing), sin GameObjects ni colliders por pez.
/// Fuera de la jaula abierta (fullQuality = false) solo se simulan liteFishCount peces.
/// </summary>
public class FishSchool : MonoBehaviour, IFishSource
{
    [Tooltip("Perfil objetivo. Editarlo en el Inspector aplica al instante; BlendTo lo cambia suavemente.")]
    public BehaviorProfile profile = new();
    [Tooltip("Sentido de giro del anillo, visto desde arriba")]
    public bool clockwise = true;
    [Min(1)] public int maxFish = 300;
    [Tooltip("Peces simulados y dibujados cuando la jaula no está abierta")]
    public int liteFishCount = 40;
    [Tooltip("Solo la jaula abierta simula todos los peces")]
    public bool fullQuality;
    public int seed = 1;

    [Header("Jaula (la fija SalmonFarmBuilder)")]
    public float halfSize = 7f;
    public float netDepth = 8f;
    [Tooltip("Distancia mínima (m) a la red, al fondo y a la superficie: nunca se cruza")]
    public float hardMargin = 0.35f;

    /// Perfil en uso (el objetivo, o una mezcla durante BlendTo).
    public BehaviorProfile Current { get; private set; }
    public int ActiveCount { get; private set; }

    Vector3[] pos, vel;
    Quaternion[] rot;
    float[] speedU, depthU, sizeMul; // valores por pez en [-1,1] / factor de tamaño
    Matrix4x4[] matrices;
    int[] cellOf, sorted, cellStart, cursor;
    int gx, gy, gz;
    float cellSize;
    int selected = -1;
    BehaviorProfile blendFrom;
    float blendT = 1f, blendDuration;

    static Material fishMat, dimMat, highlightMat;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int BellyColorId = Shader.PropertyToID("_BellyColor");
    static readonly int EmissionId = Shader.PropertyToID("_Emission");

    // ------------------------------------------------------------------ Perfil

    /// Cambia al perfil `target` interpolando durante `seconds` (suavizado).
    public void BlendTo(BehaviorProfile target, float seconds)
    {
        blendFrom = (Current ?? profile).Clone();
        profile = target;
        blendDuration = Mathf.Max(seconds, 0.01f);
        blendT = 0f;
    }

    void UpdateProfile(float dt)
    {
        if (blendT < 1f && blendFrom != null)
        {
            blendT = Mathf.Min(1f, blendT + dt / blendDuration);
            Current = BehaviorProfile.Lerp(blendFrom, profile, Mathf.SmoothStep(0f, 1f, blendT));
        }
        else Current = profile;
    }

    // ------------------------------------------------------------------ Ciclo

    void Start()
    {
        Current = profile;
        int n = maxFish;
        pos = new Vector3[n]; vel = new Vector3[n]; rot = new Quaternion[n];
        speedU = new float[n]; depthU = new float[n]; sizeMul = new float[n];
        matrices = new Matrix4x4[n];
        cellOf = new int[n]; sorted = new int[n];

        var rnd = new System.Random(seed);
        float R() => (float)rnd.NextDouble();
        for (int i = 0; i < n; i++)
        {
            speedU[i] = R() + R() - 1f; // triangular en [-1,1]
            depthU[i] = R() + R() - 1f;
            sizeMul[i] = 0.88f + 0.24f * R();
            float a = R() * Mathf.PI * 2f;
            float r = profile.ringRadius * halfSize * (0.6f + 0.8f * R());
            float y = -Mathf.Clamp(profile.preferredDepth + depthU[i] * profile.depthSpread,
                                   hardMargin + 0.5f, netDepth - hardMargin - 0.5f);
            pos[i] = new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
            vel[i] = Tangent(pos[i]) * profile.meanSpeed;
            rot[i] = Quaternion.LookRotation(vel[i]);
        }
    }

    void Update()
    {
        if (pos == null) return;
        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        UpdateProfile(dt);
        int want = Mathf.Clamp(Mathf.RoundToInt(Current.fishCount), 0, pos.Length);
        ActiveCount = fullQuality ? want : Mathf.Min(want, liteFishCount);
        if (dt > 0f) Step(ActiveCount, dt, Current);
        Render(ActiveCount);
    }

    // ------------------------------------------------------------------ Simulación

    Vector3 Tangent(Vector3 p)
    {
        var r = new Vector3(p.x, 0f, p.z);
        if (r.sqrMagnitude < 1e-6f) return Vector3.forward;
        r.Normalize();
        // Horario visto desde arriba: en +X se mueve hacia -Z.
        return clockwise ? new Vector3(r.z, 0f, -r.x) : new Vector3(-r.z, 0f, r.x);
    }

    void Step(int n, float dt, BehaviorProfile P)
    {
        if (n == 0) return;
        BuildGrid(n, Mathf.Max(P.neighborRadius, 0.3f));
        float h = halfSize;
        float R2 = P.neighborRadius * P.neighborRadius;
        float S2 = P.separationRadius * P.separationRadius;
        float ringR = P.ringRadius * (h - hardMargin);
        float minY = -netDepth + hardMargin, maxY = -hardMargin;
        float turn = 1f - Mathf.Exp(-6f * dt);

        for (int i = 0; i < n; i++)
        {
            var p = pos[i];
            var v = vel[i];
            float desired = P.meanSpeed * (1f + speedU[i] * P.speedVariation);

            // --- Vecinos (grilla de 3×3×3 celdas)
            Vector3 sep = Vector3.zero, ali = Vector3.zero, coh = Vector3.zero;
            int nb = 0;
            Cell(p, out int cx, out int cy, out int cz);
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx; if (x < 0 || x >= gx) continue;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int y = cy + dy; if (y < 0 || y >= gy) continue;
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        int z = cz + dz; if (z < 0 || z >= gz) continue;
                        int c = (x * gy + y) * gz + z;
                        for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
                        {
                            int j = sorted[k];
                            if (j == i) continue;
                            var d = pos[j] - p;
                            float d2 = d.sqrMagnitude;
                            if (d2 > R2) continue;
                            nb++;
                            ali += vel[j];
                            coh += pos[j];
                            if (d2 < S2 && d2 > 1e-6f) sep -= d / d2;
                        }
                    }
                }
            }

            // --- Reglas "sociales" y de preferencia (su suma se limita a maxAccel)
            var acc = Vector3.zero;
            if (nb > 0)
            {
                acc += (ali / nb - v) * P.alignmentWeight;
                acc += (coh / nb - p) * (P.cohesionWeight * 0.5f);
                acc += sep * (P.separationWeight * 0.4f);
            }
            float targetY = Mathf.Clamp(-(P.preferredDepth + depthU[i] * P.depthSpread), minY + 0.3f, maxY - 0.3f);
            acc.y += ((targetY - p.y) * 0.5f - v.y) * P.depthWeight;

            var radial = new Vector3(p.x, 0f, p.z);
            float r = radial.magnitude;
            if (r > 1e-3f)
            {
                var flat = new Vector3(v.x, 0f, v.z);
                var want = Tangent(p) * desired + radial / r * ((ringR - r) * 0.4f);
                acc += (want - flat) * (P.circlingWeight * 0.5f);
            }
            float sp = v.magnitude;
            if (sp > 1e-3f) acc += v / sp * (desired - sp);
            acc = Vector3.ClampMagnitude(acc, P.maxAccel);

            // --- Red, fondo y superficie: empuje suave que crece al acercarse (no se limita)
            float m = Mathf.Max(P.wallMargin, 0.05f), w = P.wallWeight * 1.5f;
            acc.x += (Push(p.x + h, m) - Push(h - p.x, m)) * w;
            acc.z += (Push(p.z + h, m) - Push(h - p.z, m)) * w;
            acc.y += (Push(p.y + netDepth, m) - Push(-p.y, m)) * w;

            v += acc * dt;
            // Los salmones nadan casi horizontales; velocidad acotada.
            float hs = new Vector2(v.x, v.z).magnitude;
            v.y = Mathf.Clamp(v.y, -0.35f * hs - 0.05f, 0.35f * hs + 0.05f);
            sp = v.magnitude;
            float lo = desired * 0.35f, hi = desired * 2f + 0.2f;
            if (sp < lo) v = (sp > 1e-4f ? v / sp : Tangent(p)) * lo;
            else if (sp > hi) v *= hi / sp;

            p += v * dt;
            // Límite duro: nunca atraviesan la red (se anula la velocidad hacia afuera).
            float lim = h - hardMargin;
            if (p.x > lim) { p.x = lim; if (v.x > 0f) v.x = 0f; }
            if (p.x < -lim) { p.x = -lim; if (v.x < 0f) v.x = 0f; }
            if (p.z > lim) { p.z = lim; if (v.z > 0f) v.z = 0f; }
            if (p.z < -lim) { p.z = -lim; if (v.z < 0f) v.z = 0f; }
            if (p.y > maxY) { p.y = maxY; if (v.y > 0f) v.y = 0f; }
            if (p.y < minY) { p.y = minY; if (v.y < 0f) v.y = 0f; }

            pos[i] = p;
            vel[i] = v;
            if (v.sqrMagnitude > 1e-6f) rot[i] = Quaternion.Slerp(rot[i], Quaternion.LookRotation(v), turn);
        }
    }

    /// 0 lejos de la pared; crece cuadráticamente hasta 1 al tocarla (d = distancia).
    static float Push(float d, float margin)
    {
        if (d >= margin) return 0f;
        float k = 1f - Mathf.Max(d, 0f) / margin;
        return k * k;
    }

    void Cell(Vector3 p, out int x, out int y, out int z)
    {
        x = Mathf.Clamp((int)((p.x + halfSize) / cellSize), 0, gx - 1);
        y = Mathf.Clamp((int)(-p.y / cellSize), 0, gy - 1);
        z = Mathf.Clamp((int)((p.z + halfSize) / cellSize), 0, gz - 1);
    }

    /// Ordena los peces por celda (counting sort): cellStart[c]..cellStart[c+1] son los de la celda c.
    void BuildGrid(int n, float size)
    {
        cellSize = size;
        gx = gz = Mathf.Max(1, Mathf.CeilToInt(2f * halfSize / size));
        gy = Mathf.Max(1, Mathf.CeilToInt(netDepth / size));
        int cells = gx * gy * gz;
        if (cellStart == null || cellStart.Length < cells + 1)
        {
            cellStart = new int[cells + 1];
            cursor = new int[cells + 1];
        }
        System.Array.Clear(cellStart, 0, cells + 1);
        for (int i = 0; i < n; i++)
        {
            Cell(pos[i], out int x, out int y, out int z);
            int c = (x * gy + y) * gz + z;
            cellOf[i] = c;
            cellStart[c + 1]++;
        }
        for (int c = 0; c < cells; c++) cellStart[c + 1] += cellStart[c];
        System.Array.Copy(cellStart, cursor, cells + 1);
        for (int i = 0; i < n; i++) sorted[cursor[cellOf[i]]++] = i;
    }

    // ------------------------------------------------------------------ Dibujo

    static void InitMaterials()
    {
        if (fishMat != null) return;
        var sh = Resources.Load<Shader>("SalmonFishInstanced");
        fishMat = new Material(sh) { name = "Salmon (instanciado)", enableInstancing = true };
        dimMat = new Material(fishMat) { name = "Salmon (atenuado)" };
        highlightMat = new Material(fishMat) { name = "Salmon (resaltado)" };
    }

    void Render(int n)
    {
        if (n == 0) return;
        InitMaterials();
        var mesh = FishFactory.SharedMesh();
        float baseScale = Current.fishLength / FishFactory.ModelLength;
        var l2w = transform.localToWorldMatrix;
        int k = 0;
        for (int i = 0; i < n; i++)
        {
            if (i == selected) continue;
            matrices[k++] = l2w * Matrix4x4.TRS(pos[i], rot[i], Vector3.one * (baseScale * sizeMul[i]));
        }
        var bounds = new Bounds(transform.TransformPoint(new Vector3(0f, -netDepth * 0.5f, 0f)),
                                new Vector3(halfSize * 2f + 2f, netDepth + 2f, halfSize * 2f + 2f));
        var rp = new RenderParams(selected >= 0 && selected < n ? dimMat : fishMat)
        {
            worldBounds = bounds,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false,
            layer = gameObject.layer,
        };
        if (k > 0) Graphics.RenderMeshInstanced(rp, mesh, 0, matrices, k);
        if (selected >= 0 && selected < n)
        {
            rp.material = highlightMat;
            Graphics.RenderMesh(rp, mesh, 0, l2w * Matrix4x4.TRS(pos[selected], rot[selected], Vector3.one * (baseScale * sizeMul[selected] * 1.15f)));
        }
    }

    // ------------------------------------------------------------------ IFishSource (panel)

    static readonly string[] ColumnNames = { "ID", "Velocidad\n(m/s)", "Prof.\n(m)", "Dist. al centro\n(m)" };
    public string[] Columns => ColumnNames;
    public string Notes =>
        "Simulación (boids): separación, alineación, cohesión, evitar la red, profundidad " +
        "preferida y giro en anillo. Los parámetros son supuestos, no mediciones.\n" +
        "Velocidad: rapidez de nado. Profundidad: bajo la superficie. Dist. al centro: horizontal, " +
        "al eje de la jaula.\nClic en una fila o en un pez para resaltarlo; otro clic lo deselecciona.";

    public void GetRows(List<FishRow> into)
    {
        into.Clear();
        for (int i = 0; i < ActiveCount; i++)
            into.Add(new FishRow
            {
                id = i + 1,
                c1 = vel[i].magnitude,
                c2 = -pos[i].y,
                c3 = new Vector2(pos[i].x, pos[i].z).magnitude,
            });
    }

    public SchoolStats GetStats()
    {
        var st = new SchoolStats { count = ActiveCount };
        if (ActiveCount == 0) return st;
        var dirSum = Vector3.zero;
        for (int i = 0; i < ActiveCount; i++)
        {
            float s = vel[i].magnitude;
            st.meanSpeed += s;
            st.meanDepth += -pos[i].y;
            if (s > 1e-4f) dirSum += vel[i] / s;
        }
        st.meanSpeed /= ActiveCount;
        st.meanDepth /= ActiveCount;
        st.polarization = dirSum.magnitude / ActiveCount;
        return st;
    }

    public bool TryPick(Ray ray, out int id)
    {
        id = -1;
        if (pos == null) return false;
        float best = float.MaxValue;
        float radius = Current.fishLength * 0.45f;
        for (int i = 0; i < ActiveCount; i++)
        {
            var c = transform.TransformPoint(pos[i]);
            float t = Vector3.Dot(c - ray.origin, ray.direction);
            if (t < 0f || t >= best) continue;
            if ((ray.origin + ray.direction * t - c).sqrMagnitude <= radius * radius * sizeMul[i] * sizeMul[i])
            {
                best = t;
                id = i + 1;
            }
        }
        return id > 0;
    }

    public void SetSelection(int id, Color highlight, float emission, float dimFactor)
    {
        selected = id - 1;
        if (selected < 0) return;
        InitMaterials();
        highlightMat.SetColor(BaseColorId, highlight);
        highlightMat.SetColor(BellyColorId, Color.Lerp(highlight, Color.white, 0.4f));
        highlightMat.SetColor(EmissionId, highlight * emission);
        dimMat.SetColor(BaseColorId, fishMat.GetColor(BaseColorId) * dimFactor);
        dimMat.SetColor(BellyColorId, fishMat.GetColor(BellyColorId) * dimFactor);
    }
}
