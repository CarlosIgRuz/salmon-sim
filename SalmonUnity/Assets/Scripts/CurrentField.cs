using UnityEngine;

public enum CurrentStrength { Debil, Media, Fuerte }

/// <summary>
/// Campo de corriente sintético: se consulta por posición (mundo) y tiempo con <see cref="At"/>.
/// - Marea semidiurna que va y viene a lo largo de un eje (elipse de marea estrecha).
/// - Deriva residual constante (lo que queda al promediar un ciclo).
/// - Decrece con la profundidad (perfil de potencia 1/7 sobre el fondo).
/// - Pequeña variación espacial para que no sea uniforme.
/// La amplitud de la marea se calibra para que la rapidez media (promedio en un ciclo y en
/// la columna de agua) sea la de la intensidad elegida. El ciclo se comprime para la demo.
/// </summary>
public class CurrentField : MonoBehaviour
{
    public static CurrentField Instance { get; private set; }

    public CurrentStrength strength = CurrentStrength.Media;
    [Tooltip("Duración (s) de un ciclo de marea en la demo. El real dura 12,42 h (M2).")]
    public float tidalPeriodSeconds = 240f;
    [Tooltip("Eje de la marea: grados desde +X (este) hacia +Z (norte)")]
    public float tideAxisDeg = 15f;
    [Tooltip("Eje menor de la elipse de marea, como fracción del mayor")]
    public float minorAxis = 0.15f;
    [Tooltip("Dirección de la deriva residual: grados desde +X hacia +Z")]
    public float residualDeg = 70f;
    [Tooltip("Variación espacial de la marea (± fracción)")]
    public float spatialNoise = 0.12f;
    [Tooltip("Segundos del cambio suave al elegir otra intensidad")]
    public float blendSeconds = 2f;

    /// Rapidez media (m/s) y deriva residual (m/s) por intensidad: Débil, Media, Fuerte.
    public static readonly float[] MeanSpeeds = { 0.06f, 0.13f, 0.20f };
    public static readonly float[] ResidualSpeeds = { 0.005f, 0.035f, 0.065f };
    public static readonly string[] Labels = { "Débil", "Media", "Fuerte" };
    public const float RealTidalPeriodHours = 12.42f;
    public const float ProfileExponent = 1f / 7f;

    /// Profundidad del agua (m) en (x, z) del mundo; la fija SalmonFarmBuilder.
    public System.Func<float, float, float> waterDepthAt;

    float amp, residual, ampTarget, residualTarget;
    bool initialized;

    void Awake()
    {
        Instance = this;
        SetStrength(strength, immediate: true);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void SetStrength(CurrentStrength s, bool immediate = false)
    {
        strength = s;
        residualTarget = ResidualSpeeds[(int)s];
        ampTarget = CalibrateAmplitude(MeanSpeeds[(int)s], residualTarget);
        if (immediate || !initialized) { amp = ampTarget; residual = residualTarget; initialized = true; }
    }

    void Update()
    {
        float k = Time.deltaTime / Mathf.Max(blendSeconds, 0.01f);
        amp = Mathf.MoveTowards(amp, ampTarget, Mathf.Max(ampTarget, amp) * k);
        residual = Mathf.MoveTowards(residual, residualTarget, Mathf.Max(residualTarget, residual) * k);
    }

    static Vector2 Dir(float deg) => new(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad));

    /// Velocidad (m/s, horizontal) de la corriente en `world` en el tiempo `t` (s).
    public Vector3 At(Vector3 world, float t)
    {
        float depth = Mathf.Max(0f, -world.y);
        float H = waterDepthAt != null ? waterDepthAt(world.x, world.z) : 35f;
        float f = DepthFactor(depth, H);
        if (f <= 0f) return Vector3.zero;
        float ph = Phase(t);
        float n = (Mathf.PerlinNoise(world.x * 0.012f + 3f, world.z * 0.012f + 7f) - 0.5f) * 2f * spatialNoise;
        var axis = Dir(tideAxisDeg);
        var perp = new Vector2(-axis.y, axis.x);
        var u = axis * (amp * Mathf.Cos(ph) * (1f + n)) + perp * (amp * minorAxis * Mathf.Sin(ph))
              + Dir(residualDeg) * residual;
        return new Vector3(u.x, 0f, u.y) * f;
    }

    /// Corriente ahora (Time.time).
    public Vector3 At(Vector3 world) => At(world, Time.time);

    /// Consulta sin instancia (cero si no hay campo en la escena).
    public static Vector3 Sample(Vector3 world) => Instance != null ? Instance.At(world) : Vector3.zero;

    /// Fracción de la velocidad de superficie que queda a `depth` m con el fondo a `H` m (ley 1/7).
    public static float DepthFactor(float depth, float H)
    {
        if (H <= 0f || depth >= H) return 0f;
        return Mathf.Pow((H - depth) / H, ProfileExponent);
    }

    public float Phase(float t) => 2f * Mathf.PI * t / Mathf.Max(tidalPeriodSeconds, 1f);

    /// Hora dentro del ciclo de marea real (0–12,42 h) que corresponde al tiempo t.
    public float TideHours(float t) => Mathf.Repeat(t / Mathf.Max(tidalPeriodSeconds, 1f), 1f) * RealTidalPeriodHours;

    /// "llenante" / "vaciante" / "estoa" según el momento del ciclo.
    public string TideLabel(float t)
    {
        float c = Mathf.Cos(Phase(t));
        return c > 0.3f ? "marea llenante" : c < -0.3f ? "marea vaciante" : "estoa (cambio de marea)";
    }

    /// Amplitud de la marea tal que la rapidez media en un ciclo, promediada en la columna
    /// (factor 1/(1+1/7) = 7/8 de la superficie), sea `mean`. Bisección: es monótona en A.
    public float CalibrateAmplitude(float mean, float res)
    {
        var axis = Dir(tideAxisDeg);
        var perp = new Vector2(-axis.y, axis.x);
        var r = Dir(residualDeg) * res;
        float colAvg = 1f / (1f + ProfileExponent);
        float MeanFor(float A)
        {
            float s = 0f;
            const int N = 96;
            for (int i = 0; i < N; i++)
            {
                float ph = 2f * Mathf.PI * i / N;
                s += (axis * (A * Mathf.Cos(ph)) + perp * (A * minorAxis * Mathf.Sin(ph)) + r).magnitude;
            }
            return s / N * colAvg;
        }
        float lo = 0f, hi = 2f;
        for (int it = 0; it < 40; it++)
        {
            float mid = (lo + hi) * 0.5f;
            if (MeanFor(mid) < mean) lo = mid; else hi = mid;
        }
        return (lo + hi) * 0.5f;
    }

    /// Rumbo hacia donde va la corriente (+Z = norte, +X = este).
    public static string Compass(Vector3 u)
    {
        if (new Vector2(u.x, u.z).sqrMagnitude < 1e-8f) return "—";
        float a = Mathf.Repeat(Mathf.Atan2(u.x, u.z) * Mathf.Rad2Deg, 360f);
        string[] names = { "N", "NE", "E", "SE", "S", "SO", "O", "NO" };
        return names[Mathf.RoundToInt(a / 45f) % 8];
    }

    /// Texto corto: "0,12 m/s hacia el E · marea llenante".
    public string Describe(Vector3 world)
    {
        var u = At(world);
        return $"{u.magnitude:F2} m/s hacia el {Compass(u)} · {TideLabel(Time.time)}";
    }
}
