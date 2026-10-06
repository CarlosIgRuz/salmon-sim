using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Lee data/trajectories.csv (ver CLAUDE.md raíz) y anima un pez por cada id.
/// Coloca este componente en un GameObject vacío y asigna un prefab de salmón.
/// </summary>
public class TrajectoryPlayer : MonoBehaviour, IFishSource
{
    [Header("Datos")]
    [Tooltip("Nombre del archivo dentro de Assets/StreamingAssets")]
    public string csvFile = "trajectories.csv";
    public float fps = 25f;

    [Header("Mapeo a la jaula virtual (metros)")]
    public float cageWidth = 10f;   // ancho visible (cx 0..1)
    public float cageHeight = 5f;   // alto visible (cy 0..1)
    public float cageDepth = 10f;   // profundidad (z 0..1)

    [Header("Peces")]
    public GameObject fishPrefab;
    [Tooltip("Escala de cada pez (1 = salmón de ~0,8 m)")]
    public float fishScale = 1.3f;
    public float turnSpeed = 6f;

    [Header("Orientación")]
    [Tooltip("Dirección 'aguas arriba' en el espacio local de la jaula. Los peces casi quietos miran hacia aquí.")]
    public Vector3 upstreamDirection = Vector3.back; // (0,0,-1): hacia la cámara
    [Tooltip("Velocidad (m/s) a partir de la cual el pez se orienta según su desplazamiento.")]
    public float moveSpeedThreshold = 0.6f;
    [Tooltip("Fracción del umbral bajo la cual vuelve a mirar aguas arriba (histéresis, evita parpadeo).")]
    [Range(0f, 1f)] public float releaseFraction = 0.7f;
    [Tooltip("Semiventana (s) para estimar la velocidad; filtra el temblor del tracker.")]
    public float velocityWindow = 0.5f;

    [Header("Reproducción")]
    public bool playing = true;
    public float speed = 1f;

    public float CurrentTime { get; private set; }
    public int VisibleCount { get; private set; }
    public float Duration { get; private set; }

    // id -> lista ordenada de (tiempo, posición)
    readonly Dictionary<int, List<(float t, Vector3 p)>> tracks = new();
    readonly Dictionary<int, Transform> fish = new();
    readonly HashSet<int> moving = new(); // peces que ahora siguen su dirección de movimiento

    void Start()
    {
        Load(Path.Combine(Application.streamingAssetsPath, csvFile));
        foreach (var id in tracks.Keys)
        {
            var go = fishPrefab != null ? Instantiate(fishPrefab, transform)
                                        : CreatePlaceholder();
            go.name = $"Salmon_{id}";
            go.transform.localScale *= fishScale;
            go.transform.localPosition = tracks[id][0].p;
            go.transform.rotation = UpstreamRotation();
            // Collider simple para poder seleccionar el pez con un clic (raycast).
            if (go.GetComponent<Collider>() == null)
            {
                var col = go.AddComponent<CapsuleCollider>();
                col.direction = 2; // eje Z, a lo largo del cuerpo
                col.radius = 0.25f;
                col.height = 1.0f;
            }
            fish[id] = go.transform;
            fishIds.Add(id);
            fishByTransform[go.transform] = id;
        }
        fishIds.Sort();
        Debug.Log($"TrajectoryPlayer: {tracks.Count} peces, {Duration:F1} s");
    }

    // ---------- Consulta para el panel / selección ----------

    readonly List<int> fishIds = new();
    readonly Dictionary<Transform, int> fishByTransform = new();

    /// IDs de todos los peces del CSV, ordenados.
    public IReadOnlyList<int> FishIds => fishIds;

    public Transform GetFish(int id) => fish.TryGetValue(id, out var t) ? t : null;

    public bool IsVisible(int id) => fish.TryGetValue(id, out var t) && t.gameObject.activeSelf;

    /// Devuelve el ID del pez al que pertenece un transform (o uno de sus hijos).
    public bool TryGetId(Transform t, out int id)
    {
        for (; t != null; t = t.parent)
            if (fishByTransform.TryGetValue(t, out id)) return true;
        id = -1;
        return false;
    }

    // ---------- IFishSource (panel) ----------

    static readonly string[] ColumnNames = { "ID", "Vel. aparente\n(m/s)", "Dist. a cámara\n(m)", "Altura\n(m)" };
    public string[] Columns => ColumnNames;
    public string Notes =>
        "Vel. aparente: desplazamiento respecto a la cámara (promedio en ±0,5 s), no el " +
        "esfuerzo de nado; ≈0 si el pez se mantiene quieto contra la corriente.\n" +
        "Dist. a cámara: estimada por el tamaño aparente del pez (fija para cada pez).\n" +
        "Altura: posición vertical en la imagen, medida desde el borde inferior.\n" +
        "Clic en una fila o en un pez para resaltarlo; otro clic lo deselecciona.";

    /// Filas: velocidad aparente (m/s), distancia a la cámara (m, de z), altura en la imagen (m, de cy).
    public void GetRows(List<FishRow> into)
    {
        into.Clear();
        foreach (int id in fishIds)
        {
            if (!IsVisible(id)) continue;
            var p = fish[id].localPosition;
            into.Add(new FishRow
            {
                id = id,
                c1 = Velocity(tracks[id], CurrentTime).magnitude,
                c2 = p.z,
                c3 = p.y + cageHeight * 0.5f,
            });
        }
    }

    /// Resumen de los peces visibles. La profundidad sale de la altura en la imagen
    /// mapeada a la jaula, y la polarización de la velocidad aparente.
    public SchoolStats GetStats()
    {
        var st = new SchoolStats();
        Vector3 dirSum = Vector3.zero;
        int nMoving = 0;
        foreach (int id in fishIds)
        {
            if (!IsVisible(id)) continue;
            var v = Velocity(tracks[id], CurrentTime);
            st.count++;
            st.meanSpeed += v.magnitude;
            st.meanDepth += -fish[id].position.y;
            if (v.sqrMagnitude > 0.0025f) { dirSum += v.normalized; nMoving++; }
        }
        if (st.count > 0) { st.meanSpeed /= st.count; st.meanDepth /= st.count; }
        st.polarization = nMoving > 0 ? dirSum.magnitude / nMoving : 0f;
        return st;
    }

    public bool TryPick(Ray ray, out int id)
    {
        Physics.SyncTransforms(); // los peces se mueven por transform, sin Rigidbody
        id = -1;
        return Physics.Raycast(ray, out var hit, 500f) && TryGetId(hit.transform, out id);
    }

    readonly RendererHighlighter highlighter = new();

    public void SetSelection(int id, Color highlight, float emission, float dimFactor) =>
        highlighter.Apply(fishIds, GetFish, id, highlight, emission, dimFactor);

    void OnDestroy() => highlighter.Dispose();

    void Load(string path)
    {
        if (!File.Exists(path)) { Debug.LogError($"No existe {path}"); return; }
        var ci = CultureInfo.InvariantCulture;
        string[] lines = File.ReadAllLines(path);
        // columnas: frame,t,id,cx,cy,w,h,conf,z
        for (int i = 1; i < lines.Length; i++)
        {
            var c = lines[i].Split(',');
            if (c.Length < 9) continue;
            float t = float.Parse(c[1], ci);
            int id = int.Parse(c[2], ci);
            float cx = float.Parse(c[3], ci);
            float cy = float.Parse(c[4], ci);
            float z = float.Parse(c[8], ci);

            var pos = new Vector3((cx - 0.5f) * cageWidth,
                                  (0.5f - cy) * cageHeight,
                                  z * cageDepth);
            if (!tracks.TryGetValue(id, out var list))
                tracks[id] = list = new List<(float, Vector3)>();
            list.Add((t, pos));
            if (t > Duration) Duration = t;
        }
        foreach (var l in tracks.Values) l.Sort((a, b) => a.t.CompareTo(b.t));
    }

    void Update()
    {
        if (Duration <= 0) return;
        if (playing) CurrentTime = (CurrentTime + Time.deltaTime * speed) % Duration;

        int visible = 0;
        foreach (var kv in tracks)
        {
            var tr = fish[kv.Key];
            if (!Sample(kv.Value, CurrentTime, out var pos))
            {
                tr.gameObject.SetActive(false);
                continue;
            }
            tr.gameObject.SetActive(true);
            visible++;

            tr.localPosition = pos;

            // Velocidad suavizada en una ventana centrada: el desplazamiento
            // frame a frame es casi todo ruido del tracker.
            Vector3 vel = Velocity(kv.Value, CurrentTime);
            float spd = vel.magnitude;
            bool isMoving = moving.Contains(kv.Key)
                ? spd > moveSpeedThreshold * releaseFraction
                : spd > moveSpeedThreshold;
            if (isMoving) moving.Add(kv.Key); else moving.Remove(kv.Key);

            var target = isMoving
                ? Quaternion.LookRotation(transform.TransformDirection(vel))
                : UpstreamRotation();
            float k = 1f - Mathf.Exp(-turnSpeed * Time.deltaTime); // independiente del framerate
            tr.rotation = Quaternion.Slerp(tr.rotation, target, k);
        }
        VisibleCount = visible;
    }

    Quaternion UpstreamRotation()
    {
        var dir = upstreamDirection.sqrMagnitude > 1e-6f ? upstreamDirection : Vector3.back;
        return Quaternion.LookRotation(transform.TransformDirection(dir));
    }

    /// Velocidad (m/s, espacio local) por diferencia centrada en [t-w, t+w],
    /// recortada a los tramos donde el pez está en cuadro.
    Vector3 Velocity(List<(float t, Vector3 p)> l, float t)
    {
        float w = Mathf.Max(velocityWindow, 0.01f);
        float t0 = Mathf.Max(t - w, l[0].t), t1 = Mathf.Min(t + w, l[^1].t);
        if (t1 - t0 < 1e-3f) return Vector3.zero;
        if (!Sample(l, t0, out var p0) || !Sample(l, t1, out var p1)) return Vector3.zero;
        return (p1 - p0) / (t1 - t0);
    }

    /// Interpola la posición en el tiempo t. Devuelve false si el pez no está
    /// en cuadro en ese momento (antes de su primera / después de su última fila,
    /// o en un hueco largo).
    static bool Sample(List<(float t, Vector3 p)> l, float t, out Vector3 pos)
    {
        pos = default;
        if (t < l[0].t || t > l[^1].t) return false;
        int lo = 0, hi = l.Count - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) / 2;
            if (l[mid].t <= t) lo = mid; else hi = mid;
        }
        var a = l[lo]; var b = l[hi];
        if (b.t - a.t > 1.0f) return false; // hueco > 1 s: el pez salió de cuadro
        float k = b.t > a.t ? (t - a.t) / (b.t - a.t) : 0f;
        pos = Vector3.Lerp(a.p, b.p, k);
        return true;
    }

    GameObject CreatePlaceholder()
    {
        return FishFactory.Create(transform);
    }
}
