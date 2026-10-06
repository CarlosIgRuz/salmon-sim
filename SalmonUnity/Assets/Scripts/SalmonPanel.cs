using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Panel derecho (IMGUI) de la jaula abierta: resumen (nº de peces, velocidad y
/// profundidad medias, polarización, orden de rotación, concentración vertical),
/// tabla de salmones y selección (clic en una
/// fila o sobre un pez en 3D lo resalta en amarillo y atenúa al resto).
/// Funciona con cualquier <see cref="IFishSource"/>: datos reales (TrajectoryPlayer)
/// o simulación (FishSchool). Solo dibuja las filas visibles del scroll, así que
/// aguanta cientos de peces.
/// </summary>
public class SalmonPanel : MonoBehaviour
{
    [Tooltip("Segundos entre actualizaciones de la tabla y el resumen")]
    public float refreshInterval = 0.25f;

    [Header("Resaltado")]
    public Color highlightColor = new Color(1f, 0.85f, 0.05f);
    [Range(0f, 1f)] public float highlightEmission = 0.35f;
    [Tooltip("Multiplicador de color para los peces no seleccionados")]
    [Range(0f, 1f)] public float dimFactor = 0.55f;

    IFishSource source;
    /// Jaula que muestra el panel. Al cambiarla se borra la selección.
    public IFishSource Source
    {
        get => source;
        set
        {
            if (value == source) return;
            source?.SetSelection(-1, highlightColor, highlightEmission, dimFactor);
            source = value;
            SelectedId = -1;
            rows.Clear();
            nextRefresh = 0f;
            scroll = Vector2.zero;
        }
    }

    /// ID seleccionado, o -1. Se mantiene aunque el pez salga de cuadro.
    public int SelectedId { get; private set; } = -1;

    public void Select(int id) { SelectedId = id; scrollToSelected = true; }
    public void Toggle(int id) { if (SelectedId == id) SelectedId = -1; else Select(id); }
    public void ClearSelection() => SelectedId = -1;

    readonly List<FishRow> rows = new();
    SchoolStats stats;
    string status;
    float nextRefresh;
    Vector2 scroll;
    float viewHeight = 400f;
    bool scrollToSelected;
    Rect panelRect;
    Vector2? pressPos;
    const float ClickTolerance = 6f;

    /// Rectángulo del panel en coordenadas GUI (para que otros scripts ignoren clics sobre él).
    public Rect PanelRect => enabled ? panelRect : Rect.zero;

    void Update()
    {
        if (source == null) return;
        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + refreshInterval;
            source.GetRows(rows);
            stats = source.GetStats();
            status = source.StatusLine;
        }
        source.SetSelection(SelectedId, highlightColor, highlightEmission, dimFactor);
    }

    void OnDisable()
    {
        // Devuelve el aspecto normal a los peces si se cierra el panel.
        source?.SetSelection(-1, highlightColor, highlightEmission, dimFactor);
        SelectedId = -1;
    }

    void OnDestroy()
    {
        foreach (var t in new[] { texPanel, texRowSel, texRowHover, texTile, texBar, texBarBg })
            if (t != null) Destroy(t);
    }

    // ---------- Selección con clic en 3D ----------

    void TryPick(Vector2 guiPos)
    {
        var cam = Camera.main;
        if (cam == null) return;
        var ray = cam.ScreenPointToRay(new Vector3(guiPos.x, Screen.height - guiPos.y, 0f));
        if (source.TryPick(ray, out int id)) Toggle(id);
    }

    // ---------- Interfaz ----------

    GUIStyle cellText, cellTextSel;
    GUIStyle title, header, cell, cellSel, note, rowBg, rowBgSel, panelBg, tile, tileValue, tileLabel;
    Texture2D texPanel, texRowSel, texRowHover, texTile, texBar, texBarBg;
    static readonly float[] ColW = { 0.12f, 0.36f, 0.26f, 0.26f };

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
        texTile = Solid(new Color(1f, 1f, 1f, 0.07f));
        texBar = Solid(new Color(0.45f, 0.85f, 1f, 0.9f));
        texBarBg = Solid(new Color(1f, 1f, 1f, 0.12f));
        panelBg = new GUIStyle { normal = { background = texPanel } };
        title = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        header = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.LowerCenter,
                                                wordWrap = true, normal = { textColor = new Color(0.7f, 0.9f, 1f) } };
        cell = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, wordWrap = false, clipping = TextClipping.Clip,
                                              normal = { textColor = Color.white } };
        cellSel = new GUIStyle(cell) { fontStyle = FontStyle.Bold, normal = { textColor = new Color(1f, 0.95f, 0.6f) } };
        cellText = new GUIStyle(cell) { normal = { textColor = new Color(0.75f, 0.85f, 0.9f) } };
        cellTextSel = new GUIStyle(cellSel);
        note = new GUIStyle(GUI.skin.label) { wordWrap = true, normal = { textColor = new Color(0.75f, 0.85f, 0.9f) } };
        rowBg = new GUIStyle { hover = { background = texRowHover } };
        rowBgSel = new GUIStyle { normal = { background = texRowSel }, hover = { background = texRowSel } };
        tile = new GUIStyle { normal = { background = texTile } };
        tileValue = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft, wordWrap = false,
                                                   normal = { textColor = Color.white } };
        tileLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperLeft, wordWrap = true,
                                                   normal = { textColor = new Color(0.7f, 0.9f, 1f) } };
    }

    void OnGUI()
    {
        if (source == null) return;
        InitStyles();

        float fs = Mathf.Max(11f, Screen.height / 50f);
        title.fontSize = Mathf.RoundToInt(fs * 1.15f);
        header.fontSize = cell.fontSize = cellSel.fontSize = Mathf.RoundToInt(fs);
        note.fontSize = Mathf.RoundToInt(fs * 0.85f);
        tileLabel.fontSize = Mathf.RoundToInt(fs * 0.75f);
        tileValue.fontSize = Mathf.RoundToInt(fs * 1.1f);
        cellText.fontSize = Mathf.RoundToInt(fs * 0.8f);
        cellTextSel.fontSize = cellText.fontSize;
        float rowH = fs * 1.7f;

        float pad = Screen.height * 0.02f;
        float w = Mathf.Clamp(Screen.width * 0.30f, 280f, 560f);
        float top = pad + FarmUi.TopInset;
        panelRect = new Rect(Screen.width - w - pad, top, w, Screen.height - top - pad);

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

        GUILayout.Label("Resumen de la jaula", title);
        DrawSummary(areaW, fs);
        if (!string.IsNullOrEmpty(status)) GUILayout.Label(status, note);
        GUILayout.Space(fs * 0.3f);

        // Encabezado
        var hr = GUILayoutUtility.GetRect(areaW, fs * 4.2f); // hasta 3 líneas
        DrawCells(hr, source.Columns, header);

        // Filas con scroll: solo se dibujan las visibles.
        int selIdx = rows.FindIndex(f => f.id == SelectedId);
        if (scrollToSelected && selIdx >= 0 && e.type == EventType.Layout)
        {
            scroll.y = Mathf.Max(0f, (selIdx - 2) * rowH);
            scrollToSelected = false;
        }
        scroll = GUILayout.BeginScrollView(scroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar);
        float rowW = areaW - GUI.skin.verticalScrollbar.fixedWidth - 4f;
        var all = GUILayoutUtility.GetRect(rowW, rowH * rows.Count);
        int first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / rowH));
        int last = Mathf.Min(rows.Count, first + Mathf.CeilToInt(viewHeight / rowH) + 2);
        var vals = new string[4];
        for (int i = first; i < last; i++)
        {
            var f = rows[i];
            bool sel = f.id == SelectedId;
            var rr = new Rect(all.x, all.y + i * rowH, rowW, rowH);
            if (GUI.Button(rr, GUIContent.none, sel ? rowBgSel : rowBg)) Toggle(f.id);
            vals[0] = f.id.ToString();
            vals[1] = f.c1Text ?? f.c1.ToString("F2");
            vals[2] = f.c2.ToString("F1");
            vals[3] = f.c3.ToString("F1");
            DrawCells(rr, vals, sel ? cellSel : cell, f.c1Text != null ? (sel ? cellTextSel : cellText) : null);
        }
        GUILayout.EndScrollView();
        if (e.type == EventType.Repaint) viewHeight = GUILayoutUtility.GetLastRect().height;

        if (SelectedId >= 0 && selIdx < 0)
            GUILayout.Label($"Salmón {SelectedId} seleccionado: fuera de cuadro.", cellSel);

        GUILayout.Space(pad * 0.4f);
        GUILayout.Label(source.Notes, note);
        GUILayout.EndArea();
    }

    /// 6 recuadros (3×2) con los promedios de la jaula; polarización y rotación llevan barra 0–1.
    void DrawSummary(float areaW, float fs)
    {
        float gap = fs * 0.35f;
        float tw = (areaW - 2f * gap) / 3f, th = fs * 4.1f;
        var r = GUILayoutUtility.GetRect(areaW, th * 2f + gap);
        Rect Cell(int col, int row) => new(r.x + col * (tw + gap), r.y + row * (th + gap), tw, th);

        Tile(Cell(0, 0), stats.count.ToString(), "peces");
        Tile(Cell(1, 0), stats.meanSpeed.ToString("F2") + " m/s", "velocidad media");
        Tile(Cell(2, 0), stats.meanDepth.ToString("F1") + " m", "prof. media");
        string nd = "n/d", why = stats.directionNote ?? "sin datos";
        TileBar(Cell(0, 1), stats.directionValid ? stats.polarization.ToString("F2") : nd,
                stats.directionValid ? "polarización" : why, stats.directionValid ? stats.polarization : -1f, fs);
        TileBar(Cell(1, 1), stats.directionValid ? stats.rotation.ToString("F2") : nd,
                stats.directionValid ? "orden de rotación" : why, stats.directionValid ? stats.rotation : -1f, fs);
        bool hasConc = !float.IsNaN(stats.concentration);
        Tile(Cell(2, 1), hasConc ? "×" + stats.concentration.ToString("F1") : nd,
             hasConc ? "concentración" : "concentración");
    }

    void TileBar(Rect r, string value, string label, float bar01, float fs)
    {
        Tile(r, value, label);
        if (bar01 < 0f) return;
        float bw = r.width - fs * 1.0f;
        var bar = new Rect(r.x + fs * 0.5f, r.yMax - fs * 0.5f, bw, fs * 0.25f);
        GUI.DrawTexture(bar, texBarBg);
        GUI.DrawTexture(new Rect(bar.x, bar.y, bw * Mathf.Clamp01(bar01), bar.height), texBar);
    }

    void Tile(Rect r, string value, string label)
    {
        GUI.Box(r, GUIContent.none, tile);
        float p = tileValue.fontSize * 0.45f;
        GUI.Label(new Rect(r.x + p, r.y + p * 0.6f, r.width - 2f * p, tileValue.fontSize * 1.5f), value, tileValue);
        GUI.Label(new Rect(r.x + p, r.y + p * 0.6f + tileValue.fontSize * 1.4f, r.width - 2f * p, tileLabel.fontSize * 2.6f), label, tileLabel);
    }

    /// col1Style: estilo alternativo para la columna 1 cuando es texto en vez de número.
    static void DrawCells(Rect r, string[] texts, GUIStyle style, GUIStyle col1Style = null)
    {
        float x = r.x;
        for (int i = 0; i < texts.Length && i < ColW.Length; i++)
        {
            float cw = r.width * ColW[i];
            GUI.Label(new Rect(x, r.y, cw, r.height), texts[i], i == 1 && col1Style != null ? col1Style : style);
            x += cw;
        }
    }
}
