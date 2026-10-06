using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Panel derecho (IMGUI) con una tabla de los salmones visibles y selección:
/// clic en una fila o sobre un pez en 3D lo resalta en amarillo y atenúa al resto.
/// No modifica los materiales compartidos de FishFactory: atenúa con
/// MaterialPropertyBlock y usa una copia propia del material para el resaltado
/// (el emissive de URP necesita un keyword, que un property block no puede activar).
/// </summary>
public class SalmonPanel : MonoBehaviour
{
    public TrajectoryPlayer player;
    [Tooltip("Segundos entre actualizaciones de la tabla")]
    public float refreshInterval = 0.25f;

    [Header("Resaltado")]
    public Color highlightColor = new Color(1f, 0.85f, 0.05f);
    [Range(0f, 1f)] public float highlightEmission = 0.35f;
    [Tooltip("Multiplicador de color para los peces no seleccionados")]
    [Range(0f, 1f)] public float dimFactor = 0.55f;

    /// ID seleccionado, o -1. Se mantiene aunque el pez salga de cuadro.
    public int SelectedId { get; private set; } = -1;

    public void Select(int id) { SelectedId = id; scrollToSelected = true; }
    public void Toggle(int id) { if (SelectedId == id) SelectedId = -1; else Select(id); }
    public void ClearSelection() => SelectedId = -1;

    readonly List<TrajectoryPlayer.FishInfo> rows = new();
    float nextRefresh;
    Vector2 scroll;
    bool scrollToSelected;
    Rect panelRect;
    Vector2? pressPos;
    const float ClickTolerance = 6f;

    /// Rectángulo del panel en coordenadas GUI (para que otros scripts ignoren clics sobre él).
    public Rect PanelRect => enabled ? panelRect : Rect.zero;

    void Update()
    {
        if (player == null) return;
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + refreshInterval;
            player.GetVisibleFish(rows);
        }
        ApplyLooks();
    }

    // ---------- Resaltado ----------

    enum Look { Normal, Dim, Selected }
    readonly Dictionary<int, Look> applied = new();
    readonly Dictionary<Renderer, Material> originalMat = new();
    readonly Dictionary<Material, Material> highlightMats = new(); // original -> copia resaltada
    MaterialPropertyBlock mpb;
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int ColorId = Shader.PropertyToID("_Color");

    void ApplyLooks()
    {
        foreach (int id in player.FishIds)
        {
            var want = id == SelectedId ? Look.Selected
                     : SelectedId >= 0 ? Look.Dim : Look.Normal;
            if (applied.TryGetValue(id, out var have) ? have == want : want == Look.Normal) continue;
            // Se aplica también a peces ocultos: al reaparecer conservan su aspecto.
            SetLook(player.GetFish(id), want);
            applied[id] = want;
        }
    }

    void SetLook(Transform root, Look look)
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
                    r.sharedMaterial = HighlightFor(orig);
                    break;
            }
        }
    }

    Material HighlightFor(Material orig)
    {
        if (highlightMats.TryGetValue(orig, out var m)) return m;
        m = new Material(orig) { name = orig.name + " (resaltado)", color = highlightColor };
        if (m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", highlightColor * highlightEmission);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        return highlightMats[orig] = m;
    }

    void OnDestroy()
    {
        foreach (var m in highlightMats.Values) Destroy(m);
    }

    // ---------- Selección con clic en 3D ----------

    void TryPick(Vector2 guiPos)
    {
        var cam = Camera.main;
        if (cam == null) return;
        Physics.SyncTransforms(); // los peces se mueven por transform, sin Rigidbody
        var ray = cam.ScreenPointToRay(new Vector3(guiPos.x, Screen.height - guiPos.y, 0f));
        if (Physics.Raycast(ray, out var hit, 500f) && player.TryGetId(hit.transform, out int id))
            Toggle(id);
    }

    // ---------- Interfaz ----------

    GUIStyle title, header, cell, cellSel, note, rowBg, rowBgSel, panelBg;
    Texture2D texPanel, texRowSel, texRowHover;
    static readonly float[] ColW = { 0.16f, 0.28f, 0.28f, 0.28f };
    static readonly string[] ColNames = { "ID", "Vel. aparente\n(m/s)", "Dist. a cámara\n(m)", "Altura\n(m)" };

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void InitStyles()
    {
        if (panelBg != null) return;
        texPanel = Solid(new Color(0.02f, 0.10f, 0.16f, 0.82f));
        texRowSel = Solid(new Color(1f, 0.85f, 0.05f, 0.35f));
        texRowHover = Solid(new Color(1f, 1f, 1f, 0.08f));
        panelBg = new GUIStyle { normal = { background = texPanel } };
        title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        header = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter,
                                                wordWrap = true, normal = { textColor = new Color(0.7f, 0.9f, 1f) } };
        cell = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        cellSel = new GUIStyle(cell) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.95f, 0.6f) } };
        note = new GUIStyle(GUI.skin.label) { wordWrap = true, normal = { textColor = new Color(0.75f, 0.85f, 0.9f) } };
        rowBg = new GUIStyle { hover = { background = texRowHover } };
        rowBgSel = new GUIStyle { normal = { background = texRowSel }, hover = { background = texRowSel } };
    }

    void OnGUI()
    {
        if (player == null) return;
        InitStyles();

        float fs = Mathf.Max(11f, Screen.height / 50f);
        title.fontSize = Mathf.RoundToInt(fs * 1.15f);
        header.fontSize = cell.fontSize = cellSel.fontSize = Mathf.RoundToInt(fs);
        note.fontSize = Mathf.RoundToInt(fs * 0.85f);
        float rowH = fs * 1.7f;

        float pad = Screen.height * 0.02f;
        float w = Mathf.Clamp(Screen.width * 0.30f, 280f, 560f);
        panelRect = new Rect(Screen.width - w - pad, pad, w, Screen.height - 2f * pad);

        // Clic fuera del panel (sin arrastrar, que rota la cámara): seleccionar un pez en 3D.
        var e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0)
            pressPos = panelRect.Contains(e.mousePosition) ? null : e.mousePosition;
        else if (e.type == EventType.MouseUp && e.button == 0 && pressPos.HasValue)
        {
            if ((e.mousePosition - pressPos.Value).magnitude <= ClickTolerance) TryPick(e.mousePosition);
            pressPos = null;
        }

        GUI.Box(panelRect, GUIContent.none, panelBg);
        float inner = pad * 0.6f;
        GUILayout.BeginArea(new Rect(panelRect.x + inner, panelRect.y + inner,
                                     panelRect.width - 2f * inner, panelRect.height - 2f * inner));
        float areaW = panelRect.width - 2f * inner;

        GUILayout.Label($"Salmones visibles: {rows.Count}", title);

        // Encabezado
        var hr = GUILayoutUtility.GetRect(areaW, fs * 4.2f); // hasta 3 líneas
        DrawCells(hr, ColNames, header);

        // Filas con scroll
        int selIdx = rows.FindIndex(f => f.id == SelectedId);
        if (scrollToSelected && selIdx >= 0 && e.type == EventType.Layout)
        {
            scroll.y = Mathf.Max(0f, (selIdx - 2) * rowH);
            scrollToSelected = false;
        }
        scroll = GUILayout.BeginScrollView(scroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar);
        float rowW = areaW - GUI.skin.verticalScrollbar.fixedWidth - 4f;
        var vals = new string[4];
        foreach (var f in rows)
        {
            bool sel = f.id == SelectedId;
            var rr = GUILayoutUtility.GetRect(rowW, rowH);
            if (GUI.Button(rr, GUIContent.none, sel ? rowBgSel : rowBg)) Toggle(f.id);
            vals[0] = f.id.ToString();
            vals[1] = f.apparentSpeed.ToString("F2");
            vals[2] = f.cameraDistance.ToString("F1");
            vals[3] = f.height.ToString("F1");
            DrawCells(rr, vals, sel ? cellSel : cell);
        }
        GUILayout.EndScrollView();

        if (SelectedId >= 0 && selIdx < 0)
            GUILayout.Label($"Salmón {SelectedId} seleccionado: fuera de cuadro.", cellSel);

        GUILayout.Space(pad * 0.4f);
        GUILayout.Label(
            "Vel. aparente: desplazamiento respecto a la cámara (promedio en ±0,5 s), no el " +
            "esfuerzo de nado; ≈0 si el pez se mantiene quieto contra la corriente.\n" +
            "Dist. a cámara: estimada por el tamaño aparente del pez (fija para cada pez).\n" +
            "Altura: posición vertical en la imagen, medida desde el borde inferior.\n" +
            "Clic en una fila o en un pez para resaltarlo; otro clic lo deselecciona.", note);
        GUILayout.EndArea();
    }

    static void DrawCells(Rect r, string[] texts, GUIStyle style)
    {
        float x = r.x;
        for (int i = 0; i < texts.Length; i++)
        {
            float cw = r.width * ColW[i];
            GUI.Label(new Rect(x, r.y, cw, r.height), texts[i], style);
            x += cw;
        }
    }

    void OnDisable()
    {
        // Devuelve los materiales originales si se desactiva el panel.
        foreach (var kv in originalMat)
            if (kv.Key != null) { kv.Key.sharedMaterial = kv.Value; kv.Key.SetPropertyBlock(null); }
        applied.Clear();
    }
}
