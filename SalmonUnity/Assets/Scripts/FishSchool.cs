using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Cardumen simulado (boids) dentro de una jaula cuadrada. Reglas: separación,
/// alineación, cohesión, evitar la red (fuerza suave + límite duro: nunca la cruzan),
/// profundidad preferida y giro en anillo alrededor del centro.
/// Alimentación por comidas (automáticas o "Alimentar ahora"): el esparcidor del centro
/// lanza la ración en un anillo; los peces con hambre (fracción `appetite` del perfil) suben
/// bajo el esparcidor en frenesí, comen hasta saciarse y vuelven a su capa. Los pellets caen
/// a pelletSinkSpeed ±10 %, arrastrados por CurrentField; los que nadie come salen por la red
/// y cuentan como alimento no consumido (MealStats).
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

    [Header("Alimentación")]
    [Tooltip("Velocidad media de caída del pellet (m/s). Fuente: NewDEPOMOD, granja Muck (SEPA).")]
    public float pelletSinkSpeed = 0.095f;
    [Tooltip("Variación de la caída entre pellets (± fracción). Fuente: NewDEPOMOD, granja Muck (SEPA).")]
    public float pelletSinkVariation = 0.1f;
    [Tooltip("Radios (m) del anillo donde el esparcidor rotatorio lanza los pellets")]
    public float spreadInner = 1f, spreadOuter = 4.5f;
    public int maxPellets = 2500;
    [Tooltip("Pellets por pez en cada comida (cada pellet dibujado representa muchos)")]
    public float rationPerFish = 3f;
    [Tooltip("Pellets que sacian a un pez")]
    public float satiation = 3.5f;
    [Tooltip("Duración (s) de una comida")]
    public float mealSeconds = 40f;
    [Tooltip("Tiempo (s) entre el inicio de dos comidas automáticas")]
    public float mealInterval = 180f;
    [Tooltip("Profundidad (m) bajo el esparcidor a la que suben los peces con hambre")]
    public float frenzyDepth = 1.2f;
    [Tooltip("Velocidad en el frenesí (× la de crucero)")]
    public float frenzySpeed = 1.6f;
    [Tooltip("Segundos que se sigue dibujando un pellet después de salir por la red")]
    public float lostFadeSeconds = 15f;
    [Tooltip("Cabezal del esparcidor (lo crea SalmonFarmBuilder); gira durante las comidas")]
    public Transform spreaderRotor;

    [Header("Condiciones (las fija FarmConditions)")]
    [Tooltip("Comidas automáticas cada mealInterval (de día y si la etapa se alimenta)")]
    public bool autoFeed;
    [Tooltip("Texto de alimentación para el panel")]
    public string feedingNote = "";
    /// Temperatura del agua (°C) según la profundidad (m); null = sin perfil térmico.
    public System.Func<float, float> temperatureAt;

    /// Perfil en uso (el objetivo, o una mezcla durante BlendTo).
    public BehaviorProfile Current { get; private set; }
    public int ActiveCount { get; private set; }

    Vector3[] pos, vel;
    Quaternion[] rot;
    float[] speedU, depthU, sizeMul; // valores por pez en [-1,1] / factor de tamaño
    float[] hungerU, gut;            // con hambre si hungerU < appetite; gut = pellets comidos en la comida
    int[] foodTarget;
    Matrix4x4[] matrices;
    int[] cellOf, sorted, cellStart, cursor;
    int gx, gy, gz;
    float cellSize;
    int selected = -1;
    BehaviorProfile blendFrom;
    float blendT = 1f, blendDuration;
    struct Pellet
    {
        public Vector3 p;
        public float sink;   // m/s
        public int meal;     // id de la comida que lo lanzó
        public float lostAt; // < 0: dentro de la red; si no, Time.time al salir
    }
    Pellet[] pellets;
    Matrix4x4[] pelletMatrices;
    int pelletCount;
    float pelletAccum, nextMeal, rotorSpeed;
    MealStats meal;
    System.Random pelletRnd;
    readonly List<float> depthScratch = new();
    SchoolStats cachedStats;
    int cachedFrame = -1;

    static Material pelletMat;

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
        hungerU = new float[n]; gut = new float[n]; foodTarget = new int[n];
        pellets = new Pellet[maxPellets];
        pelletMatrices = new Matrix4x4[maxPellets];
        pelletRnd = new System.Random(seed + 7);

        var rnd = new System.Random(seed);
        float R() => (float)rnd.NextDouble();
        for (int i = 0; i < n; i++)
        {
            speedU[i] = R() + R() - 1f; // triangular en [-1,1]
            depthU[i] = R() + R() - 1f;
            sizeMul[i] = 0.88f + 0.24f * R();
            hungerU[i] = R();
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
        if (dt > 0f)
        {
            UpdateMeal(dt);
            UpdatePellets(dt, Current);
            Step(ActiveCount, dt, Current);
        }
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
        bool feeding = FeedingTime;

        for (int i = 0; i < n; i++)
        {
            var p = pos[i];
            var v = vel[i];
            bool hungry = feeding && IsHungry(i, P);
            float desired = P.meanSpeed * (1f + speedU[i] * P.speedVariation) * (hungry ? frenzySpeed : 1f);

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
            if (hungry)
            {
                // Frenesí: deja su capa y el anillo y va hacia el alimento (o bajo el esparcidor).
                var toFood = FoodTarget(i, p) - p;
                float dist = toFood.magnitude;
                if (dist > 0.15f) acc += (toFood / dist * desired - v) * 2f;
            }
            else
            {
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
            }
            if (P.wander > 0f)
            {
                float tt = Time.time * 0.35f;
                acc += new Vector3(Mathf.PerlinNoise(tt, i * 1.37f) - 0.5f, 0f,
                                   Mathf.PerlinNoise(i * 1.37f, tt + 50f) - 0.5f) * (2f * P.wander);
            }
            float sp = v.magnitude;
            if (sp > 1e-3f) acc += v / sp * ((desired - sp) * 2f);
            acc = Vector3.ClampMagnitude(acc, P.maxAccel * (hungry ? 2f : 1f));

            // --- Red, fondo y superficie: empuje suave que crece al acercarse (no se limita)
            float m = Mathf.Max(P.wallMargin, 0.05f), w = P.wallWeight * 1.5f;
            acc.x += (Push(p.x + h, m) - Push(h - p.x, m)) * w;
            acc.z += (Push(p.z + h, m) - Push(h - p.z, m)) * w;
            acc.y += (Push(p.y + netDepth, m) - Push(-p.y, m)) * w;

            v += acc * dt;
            // Los salmones nadan casi horizontales; velocidad acotada.
            float hs = new Vector2(v.x, v.z).magnitude;
            v.y = Mathf.Clamp(v.y, -0.6f * hs - 0.08f, 0.6f * hs + 0.08f);
            sp = v.magnitude;
            float lo = desired * 0.6f, hi = desired * 1.4f;
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

    // ------------------------------------------------------------------ Alimentación

    /// Una comida: ración lanzada, pellets comidos y perdidos (salieron por la red).
    public struct MealStats
    {
        public int id, ration, delivered, eaten, lost;
        public bool active, manual;
        /// Parte de la comida ocurrió con la jaula cerrada (sin pellets simulados): cifras incompletas.
        public bool partial;
        public float elapsed;
        public int InWater => delivered - eaten - lost;
        /// Fracción del alimento entregado que nadie comió (perdido / entregado).
        public float Unconsumed => delivered > 0 ? lost / (float)delivered : 0f;
    }

    /// Comida en curso, o la última (id = 0: todavía ninguna).
    public MealStats Meal => meal;
    /// Última comida terminada y sin pellets suyos en la red (id = 0: ninguna). Se mantiene
    /// mientras corre la siguiente, para comparar.
    public MealStats LastMeal => lastMeal;
    MealStats lastMeal;
    /// Hay comida en curso o quedan pellets suyos dentro de la red.
    public bool FeedingTime => meal.active || meal.InWater > 0;
    /// Segundos hasta la próxima comida automática (< 0 si no hay comidas automáticas).
    public float NextMealIn => autoFeed && !meal.active ? Mathf.Max(0f, nextMeal - Time.time) : -1f;

    /// Peces con hambre en esta comida (fracción `appetite` del perfil).
    public int HungryCount
    {
        get
        {
            if (pos == null || Current == null) return 0;
            int k = 0;
            for (int i = 0; i < ActiveCount; i++) if (hungerU[i] < Current.appetite) k++;
            return k;
        }
    }

    /// "Alimentar ahora": empieza una comida si no hay otra en curso.
    public bool FeedNow() => StartMeal(manual: true);

    bool StartMeal(bool manual)
    {
        if (meal.active || pos == null) return false;
        // Si quedaban pellets de la anterior ya no se cuentan: su resultado queda incompleto.
        if (meal.id > 0 && meal.delivered > 0 && lastMeal.id != meal.id)
        {
            lastMeal = meal;
            lastMeal.partial |= meal.InWater > 0;
        }
        meal = new MealStats
        {
            id = meal.id + 1, active = true, manual = manual, partial = !fullQuality,
            ration = Mathf.RoundToInt(rationPerFish * Mathf.Round(Current.fishCount)),
        };
        pelletAccum = 0f;
        System.Array.Clear(gut, 0, gut.Length);
        nextMeal = Time.time + mealInterval;
        return true;
    }

    void UpdateMeal(float dt)
    {
        // Sin comidas automáticas el reloj se reinicia; al activarlas, la primera llega
        // tras una espera distinta en cada jaula (no comen todas a la vez).
        if (!autoFeed && !meal.active) nextMeal = Time.time + 8f + (seed % 7) * 5f;
        if (autoFeed && !meal.active && Time.time >= nextMeal) StartMeal(manual: false);
        if (meal.active)
        {
            meal.elapsed += dt;
            if (!fullQuality) meal.partial = true;
            if (meal.elapsed >= mealSeconds) meal.active = false;
        }
        if (!meal.active && meal.InWater == 0 && meal.delivered > 0) lastMeal = meal;
        if (spreaderRotor != null)
        {
            rotorSpeed = Mathf.MoveTowards(rotorSpeed, meal.active ? 240f : 0f, 300f * dt);
            spreaderRotor.Rotate(0f, rotorSpeed * dt, 0f, Space.Self);
        }
    }

    bool IsHungry(int i, BehaviorProfile P) => hungerU[i] < P.appetite && gut[i] < satiation;

    /// Hacia dónde nada un pez con hambre: "su" pellet si está cerca (se reelige cada ~0,5 s
    /// entre unas muestras al azar), si no, un punto propio del anillo bajo el esparcidor.
    Vector3 FoodTarget(int i, Vector3 p)
    {
        if (pelletCount > 0)
        {
            int k = foodTarget[i];
            if (k >= pelletCount || pellets[k].lostAt >= 0f || (Time.frameCount + i) % 30 == 0)
            {
                float best = float.MaxValue;
                k = -1;
                for (int s = 0; s < 6; s++)
                {
                    int c = (int)((uint)(i * 7919 + Time.frameCount * 31 + s * 104729) % (uint)pelletCount);
                    if (pellets[c].lostAt >= 0f) continue;
                    float d2 = (pellets[c].p - p).sqrMagnitude;
                    if (d2 < best) { best = d2; k = c; }
                }
                foodTarget[i] = k < 0 ? int.MaxValue : k;
            }
            k = foodTarget[i];
            if (k < pelletCount && (pellets[k].p - p).sqrMagnitude < 16f) return pellets[k].p;
        }
        float a = i * 2.39996f;
        float rr = Mathf.Lerp(spreadInner, spreadOuter, Mathf.Repeat(i * 0.618034f, 1f));
        return new Vector3(Mathf.Cos(a) * rr, -frenzyDepth - (i % 5) * 0.2f, Mathf.Sin(a) * rr);
    }

    /// Pellets (solo en la jaula abierta): el esparcidor lanza la ración durante la comida;
    /// caen a su velocidad, la corriente los arrastra y un pez con hambre a menos de 1,5 largos
    /// de cuerpo se lo come. Si salen por la red (lados o fondo) se cuentan como no consumidos.
    void UpdatePellets(float dt, BehaviorProfile P)
    {
        if (!fullQuality) { pelletCount = 0; pelletAccum = 0f; return; }
        if (meal.active)
        {
            pelletAccum += meal.ration / Mathf.Max(mealSeconds, 0.1f) * dt;
            while (pelletAccum >= 1f && meal.delivered < meal.ration && pelletCount < pellets.Length)
            {
                pelletAccum -= 1f;
                float a = (float)pelletRnd.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(spreadInner, spreadOuter, Mathf.Sqrt((float)pelletRnd.NextDouble()));
                float sink = pelletSinkSpeed * (1f + pelletSinkVariation * (2f * (float)pelletRnd.NextDouble() - 1f));
                pellets[pelletCount++] = new Pellet
                {
                    p = new Vector3(Mathf.Cos(a) * r, -0.1f, Mathf.Sin(a) * r), sink = sink, meal = meal.id, lostAt = -1f,
                };
                meal.delivered++;
            }
            pelletAccum = Mathf.Min(pelletAccum, 1f);
        }
        if (pelletCount == 0) return;

        float eat2 = Mathf.Pow(Mathf.Max(0.3f, P.fishLength * 1.5f), 2f);
        var field = CurrentField.Instance;
        float now = Time.time;
        for (int k = 0; k < pelletCount; k++)
        {
            ref var pe = ref pellets[k];
            var drift = field != null ? transform.InverseTransformVector(field.At(transform.TransformPoint(pe.p), now)) : Vector3.zero;
            pe.p += new Vector3(drift.x, -pe.sink, drift.z) * dt;
            bool gone;
            if (pe.lostAt < 0f)
            {
                if (Mathf.Abs(pe.p.x) > halfSize || Mathf.Abs(pe.p.z) > halfSize || pe.p.y < -netDepth)
                {
                    pe.lostAt = now;
                    if (pe.meal == meal.id) meal.lost++;
                    gone = false;
                }
                else
                {
                    gone = TryEat(pe.p, eat2, P);
                    if (gone && pe.meal == meal.id) meal.eaten++;
                }
            }
            else gone = now - pe.lostAt > lostFadeSeconds;
            if (gone) { pellets[k] = pellets[--pelletCount]; k--; }
        }
    }

    /// ¿Hay un pez con hambre a menos de √r2 del pellet? Busca en la grilla de vecinos del último paso.
    bool TryEat(Vector3 q, float r2, BehaviorProfile P)
    {
        if (cellStart == null || cellSize <= 0f) return false;
        Cell(q, out int cx, out int cy, out int cz);
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
                        if (j >= ActiveCount || !IsHungry(j, P)) continue;
                        if ((pos[j] - q).sqrMagnitude < r2) { gut[j] += 1f; return true; }
                    }
                }
            }
        }
        return false;
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
        pelletMat = new Material(fishMat) { name = "Pellet" };
        pelletMat.SetColor(BaseColorId, new Color(0.45f, 0.28f, 0.12f));
        pelletMat.SetColor(BellyColorId, new Color(0.55f, 0.36f, 0.16f));
        pelletMat.SetFloat("_WagAmp", 0f);
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
        if (pelletCount > 0)
        {
            // Pellets exagerados (8 cm) para que se vean desde la cámara.
            for (int q = 0; q < pelletCount; q++)
                pelletMatrices[q] = l2w * Matrix4x4.TRS(pellets[q].p, Quaternion.identity, Vector3.one * 0.08f);
            var prp = rp;
            prp.material = pelletMat;
            // Los pellets perdidos siguen cayendo fuera de la red.
            prp.worldBounds = new Bounds(bounds.center, bounds.size + new Vector3(12f, 6f, 12f));
            Graphics.RenderMeshInstanced(prp, FarmKit.Sphere, 0, pelletMatrices, pelletCount);
        }
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
        "al eje de la jaula.\nPolarización: todos hacia el mismo lado. Orden de rotación: todos giran en el " +
        "mismo sentido (alto en un anillo). Concentración: densidad a ±1 m de la profundidad mediana vs. la media.\n" +
        "Clic en una fila o en un pez para resaltarlo; otro clic lo deselecciona.";

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

    /// Resumen (se calcula una vez por frame aunque lo pidan varios).
    public SchoolStats GetStats()
    {
        if (cachedFrame == Time.frameCount) return cachedStats;
        cachedFrame = Time.frameCount;
        var st = new SchoolStats { count = ActiveCount, directionValid = true };
        depthScratch.Clear();
        for (int i = 0; i < ActiveCount; i++)
        {
            st.meanSpeed += vel[i].magnitude;
            st.meanDepth += -pos[i].y;
            depthScratch.Add(-pos[i].y);
        }
        if (ActiveCount > 0)
        {
            st.meanSpeed /= ActiveCount;
            st.meanDepth /= ActiveCount;
            FishMetrics.Direction(pos, vel, ActiveCount, out st.polarization, out st.rotation);
            st.concentration = FishMetrics.VerticalConcentration(depthScratch, netDepth);
        }
        return cachedStats = st;
    }

    public string StatusLine
    {
        get
        {
            var st = GetStats();
            string temp = temperatureAt != null
                ? $"Agua a {st.meanDepth:F1} m: {temperatureAt(st.meanDepth):F1} °C (cómodo 8–20 °C)"
                : "Sin perfil térmico";
            string s = string.IsNullOrEmpty(feedingNote) ? temp : temp + "\n" + feedingNote;
            if (lastMeal.id > 0)
                s += $"\nÚltima comida: alimento no consumido {lastMeal.Unconsumed * 100f:F0} %{(lastMeal.partial ? " (parcial)" : "")}";
            return s;
        }
    }

    /// Pellets en el agua (para pruebas).
    public int PelletCount => pelletCount;

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
