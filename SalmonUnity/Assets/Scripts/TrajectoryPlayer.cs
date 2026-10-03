using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// Lee data/trajectories.csv (ver CLAUDE.md raíz) y anima un pez por cada id.
/// Coloca este componente en un GameObject vacío y asigna un prefab de salmón.
/// </summary>
public class TrajectoryPlayer : MonoBehaviour
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
    public float turnSpeed = 6f;

    [Header("Reproducción")]
    public bool playing = true;
    public float speed = 1f;

    public float CurrentTime { get; private set; }
    public int VisibleCount { get; private set; }
    public float Duration { get; private set; }

    // id -> lista ordenada de (tiempo, posición)
    readonly Dictionary<int, List<(float t, Vector3 p)>> tracks = new();
    readonly Dictionary<int, Transform> fish = new();

    void Start()
    {
        Load(Path.Combine(Application.streamingAssetsPath, csvFile));
        foreach (var id in tracks.Keys)
        {
            var go = fishPrefab != null ? Instantiate(fishPrefab, transform)
                                        : CreatePlaceholder();
            go.name = $"Salmon_{id}";
            go.transform.localPosition = tracks[id][0].p;
            fish[id] = go.transform;
        }
        Debug.Log($"TrajectoryPlayer: {tracks.Count} peces, {Duration:F1} s");
    }

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

            Vector3 dir = pos - tr.localPosition;
            tr.localPosition = pos;
            if (dir.sqrMagnitude > 1e-6f)
            {
                var target = Quaternion.LookRotation(transform.TransformDirection(dir));
                tr.rotation = Quaternion.Slerp(tr.rotation, target, Time.deltaTime * turnSpeed);
            }
        }
        VisibleCount = visible;
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
