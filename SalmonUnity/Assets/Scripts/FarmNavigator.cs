using UnityEngine;

/// <summary>
/// Navegación entre la vista general y una jaula.
/// - Vista general: nombre sobre cada jaula; hover la resalta; clic vuela hacia ella.
/// - Jaula: botón "← Volver" (o Esc) hace el vuelo inverso; se muestra el panel de
///   salmones (datos reales o simulación) y, con datos reales, el HUD del video.
/// Solo la jaula abierta simula su cardumen completo (FishSchool.fullQuality).
/// Al cruzar la superficie cambia el ambiente y se ocultan/restauran el resto de la escena.
/// Para probar por código: EnterCage(i), ExitCage(), forcedHover.
/// </summary>
public class FarmNavigator : MonoBehaviour
{
    public SalmonFarmBuilder farm;
    public FarmCamera cam;
    public SalmonPanel panel;
    [Tooltip("Si es >= 0, fuerza el hover sobre esa jaula (pruebas sin mouse)")]
    public int forcedHover = -1;

    public FarmCage Hovered { get; private set; }
    /// Jaula abierta (o hacia la que se vuela); null en la vista general.
    public FarmCage Current { get; private set; }
    public bool Busy => cam != null && cam.CurrentMode == FarmCamera.Mode.Transition;

    GUIStyle label, labelHover, hint, title, sub, button, empty, badgeReal, badgeSim;
    Texture2D texLabel, texHover, texButton, texButtonHover, texReal, texSim;
    Rect backRect;

    void Start()
    {
        cam.Clicked += OnClick;
        cam.IsOverUi = IsOverUi;
    }

    void OnDestroy()
    {
        if (cam != null) cam.Clicked -= OnClick;
        foreach (var t in new[] { texLabel, texHover, texButton, texButtonHover, texReal, texSim })
            if (t != null) Destroy(t);
    }

    // ------------------------------------------------------------------ Navegación

    public void EnterCage(int index) => EnterCage(farm.Cages[index]);

    public void EnterCage(FarmCage cage)
    {
        if (cage == null || Busy || Current != null) return;
        SetHover(null);
        Current = cage;
        SetPickColliders(false); // si no, el collider de la jaula tapa los clics sobre los peces
        cam.FlyToCage(cage.FocusWorld, OnSurface, () =>
        {
            farm.Isolate(cage);
            farm.SetUnderwater(true);
            if (cage.school != null) cage.school.fullQuality = true;
            ShowCageUi(true);
        });
    }

    public void ExitCage()
    {
        if (Current == null || Busy) return;
        ShowCageUi(false);
        cam.FlyToOverview(OnSurface, () =>
        {
            farm.Isolate(null);
            farm.SetUnderwater(false);
            if (Current.school != null) Current.school.fullQuality = false;
            SetPickColliders(true);
            Current = null;
        });
    }

    void OnSurface(bool under)
    {
        if (under)
        {
            farm.Isolate(Current);
            farm.SetUnderwater(true, murky: true);
        }
        else
        {
            farm.Isolate(null);
            farm.SetUnderwater(false);
        }
        if (Current != null && Current.school != null) Current.school.fullQuality = under;
    }

    void ShowCageUi(bool on)
    {
        if (Current == null) return;
        if (Current.player != null)
        {
            var hud = Current.player.GetComponent<SalmonHud>();
            if (hud != null) hud.enabled = on;
        }
        if (panel != null)
        {
            if (on) panel.Source = Current.Source;
            panel.enabled = on && Current.Source != null;
        }
    }

    void SetPickColliders(bool on)
    {
        foreach (var c in farm.Cages)
            if (c.pickCollider != null) c.pickCollider.enabled = on;
    }

    // ------------------------------------------------------------------ Hover / clic

    void SetHover(FarmCage cage)
    {
        if (cage == Hovered) return;
        if (Hovered != null) Hovered.SetHighlight(false);
        Hovered = cage;
        if (Hovered != null) Hovered.SetHighlight(true);
    }

    FarmCage CageUnder(Vector2 guiPos)
    {
        var ray = cam.Cam.ScreenPointToRay(new Vector3(guiPos.x, Screen.height - guiPos.y, 0f));
        return Physics.Raycast(ray, out var hit, 3000f) ? hit.collider.GetComponentInParent<FarmCage>() : null;
    }

    void OnClick(Vector2 guiPos)
    {
        if (Current == null && !Busy && Hovered != null) EnterCage(Hovered);
    }

    bool IsOverUi(Vector2 guiPos)
    {
        if (Current != null && backRect.Contains(guiPos)) return true;
        return Current != null && panel != null && panel.PanelRect.Contains(guiPos);
    }

    // ------------------------------------------------------------------ Interfaz

    void OnGUI()
    {
        if (farm == null || cam == null) return;
        InitStyles();
        var e = Event.current;

        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && Current != null)
        {
            ExitCage();
            e.Use();
        }

        float fs = Mathf.Max(11f, Screen.height / 52f);
        label.fontSize = labelHover.fontSize = button.fontSize = Mathf.RoundToInt(fs);
        badgeReal.fontSize = badgeSim.fontSize = Mathf.RoundToInt(fs * 0.78f);
        hint.fontSize = sub.fontSize = Mathf.RoundToInt(fs * 0.9f);
        title.fontSize = Mathf.RoundToInt(fs * 1.4f);
        empty.fontSize = Mathf.RoundToInt(fs * 1.1f);

        switch (cam.CurrentMode)
        {
            case FarmCamera.Mode.Overview: DrawOverview(e, fs); break;
            case FarmCamera.Mode.Cage: DrawCage(fs); break;
        }
    }

    void DrawOverview(Event e, float fs)
    {
        backRect = Rect.zero;
        if (e.type == EventType.Repaint)
            SetHover(forcedHover >= 0 && forcedHover < farm.Cages.Count ? farm.Cages[forcedHover] : CageUnder(e.mousePosition));

        foreach (var c in farm.Cages)
            if (c.gameObject.activeInHierarchy && c != Hovered)
                DrawCageLabel(c, label);
        if (Hovered != null)
        {
            DrawCageLabel(Hovered, labelHover);
            // Tooltip junto al cursor (o bajo la etiqueta si el hover es forzado)
            var tip = new GUIContent($"{Hovered.displayName}  ·  clic para entrar\n{Hovered.description}");
            var ts = hint.CalcSize(tip);
            Vector2 at = forcedHover >= 0 ? WorldToGui(Hovered.LabelWorld) + new Vector2(-ts.x * 0.5f, fs * 1.6f)
                                          : e.mousePosition + new Vector2(18f, 18f);
            at.x = Mathf.Clamp(at.x, 4f, Screen.width - ts.x - 4f);
            at.y = Mathf.Clamp(at.y, 4f, Screen.height - ts.y - 4f);
            GUI.Label(new Rect(at, ts), tip, hint);
        }

        var help = new GUIContent("Clic en una jaula: entrar   ·   Arrastrar: rotar   ·   Rueda: zoom");
        var hs = hint.CalcSize(help);
        GUI.Label(new Rect((Screen.width - hs.x) * 0.5f, Screen.height - hs.y - fs, hs.x, hs.y), help, hint);
    }

    void DrawCage(float fs)
    {
        if (Current == null) return;
        float pad = Screen.height * 0.02f;
        var bc = new GUIContent("← Volver   (Esc)");
        var bs = button.CalcSize(bc);
        backRect = new Rect(pad, Screen.height - pad - bs.y * 1.3f, bs.x + fs * 1.5f, bs.y * 1.3f);
        if (GUI.Button(backRect, bc, button)) ExitCage();

        // Título centrado arriba (el HUD va a la izquierda y el panel a la derecha)
        var tc = new GUIContent(Current.displayName);
        var sc = new GUIContent(Current.description);
        var tsz = title.CalcSize(tc);
        var ssz = sub.CalcSize(sc);
        float w = Mathf.Max(tsz.x, ssz.x);
        float x = (Screen.width - w) * 0.5f;
        GUI.Label(new Rect(x, pad, w, tsz.y), tc, title);
        GUI.Label(new Rect(x, pad + tsz.y, w, ssz.y), sc, sub);
        if (!string.IsNullOrEmpty(Current.dataLabel))
        {
            var style = Current.isRealData ? badgeReal : badgeSim;
            var bcont = new GUIContent(Current.dataLabel);
            var bsz = style.CalcSize(bcont);
            GUI.Label(new Rect((Screen.width - bsz.x) * 0.5f, pad + tsz.y + ssz.y + fs * 0.3f, bsz.x, bsz.y), bcont, style);
        }

        if (Current.Source == null)
        {
            var ec = new GUIContent("Jaula sin datos todavía:\nse poblará con salmones simulados.");
            var es = empty.CalcSize(ec);
            GUI.Label(new Rect((Screen.width - es.x) * 0.5f, Screen.height * 0.45f, es.x, es.y), ec, empty);
        }

        var help = new GUIContent("Arrastrar: rotar   ·   Rueda: zoom");
        var hs = hint.CalcSize(help);
        GUI.Label(new Rect((Screen.width - hs.x) * 0.5f, Screen.height - hs.y - fs, hs.x, hs.y), help, hint);
    }

    Vector2 WorldToGui(Vector3 world)
    {
        var sp = cam.Cam.WorldToScreenPoint(world);
        return new Vector2(sp.x, Screen.height - sp.y);
    }

    /// Nombre de la jaula y, debajo, la etiqueta de origen de los datos (verde: real, naranja: simulación).
    void DrawCageLabel(FarmCage c, GUIStyle nameStyle)
    {
        var sp = cam.Cam.WorldToScreenPoint(c.LabelWorld);
        if (sp.z <= 0f) return;
        var nc = new GUIContent(c.displayName);
        var ns = nameStyle.CalcSize(nc);
        float y = Screen.height - sp.y - ns.y;
        GUI.Label(new Rect(sp.x - ns.x * 0.5f, y, ns.x, ns.y), nc, nameStyle);
        if (string.IsNullOrEmpty(c.dataLabel)) return;
        var style = c.isRealData ? badgeReal : badgeSim;
        var bc = new GUIContent(c.dataLabel);
        var bs = style.CalcSize(bc);
        GUI.Label(new Rect(sp.x - bs.x * 0.5f, y + ns.y, bs.x, bs.y), bc, style);
    }

    void DrawWorldLabel(Vector3 world, string text, GUIStyle style)
    {
        var sp = cam.Cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) return;
        var content = new GUIContent(text);
        var size = style.CalcSize(content);
        GUI.Label(new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y, size.x, size.y), content, style);
    }

    void InitStyles()
    {
        if (label != null) return;
        texLabel = SolidTex(new Color(0.05f, 0.10f, 0.14f, 0.72f));
        texHover = SolidTex(new Color(0.95f, 0.75f, 0.10f, 0.95f));
        texButton = SolidTex(new Color(0.05f, 0.10f, 0.14f, 0.85f));
        texButtonHover = SolidTex(new Color(0.12f, 0.24f, 0.32f, 0.95f));
        label = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false,
            normal = { textColor = Color.white, background = texLabel },
            padding = new RectOffset(8, 8, 3, 3),
        };
        labelHover = new GUIStyle(label) { normal = { textColor = new Color(0.08f, 0.08f, 0.08f), background = texHover } };
        hint = new GUIStyle(label) { fontStyle = FontStyle.Normal, alignment = TextAnchor.MiddleLeft };
        texReal = SolidTex(new Color(0.12f, 0.55f, 0.30f, 0.92f));
        texSim = SolidTex(new Color(0.85f, 0.45f, 0.10f, 0.92f));
        badgeReal = new GUIStyle(label)
        {
            fontStyle = FontStyle.Bold, padding = new RectOffset(6, 6, 1, 2),
            normal = { textColor = Color.white, background = texReal },
        };
        badgeSim = new GUIStyle(badgeReal) { normal = { textColor = Color.white, background = texSim } };
        title = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false,
            normal = { textColor = Color.white },
        };
        sub = new GUIStyle(title) { fontStyle = FontStyle.Normal, normal = { textColor = new Color(0.8f, 0.9f, 0.95f) } };
        empty = new GUIStyle(title) { fontStyle = FontStyle.Italic, normal = { textColor = new Color(0.85f, 0.92f, 0.95f) } };
        button = new GUIStyle(GUI.skin.button)
        {
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white, background = texButton },
            hover = { textColor = Color.white, background = texButtonHover },
            active = { textColor = Color.white, background = texButtonHover },
            padding = new RectOffset(12, 12, 6, 6),
        };
    }

    static Texture2D SolidTex(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }
}
