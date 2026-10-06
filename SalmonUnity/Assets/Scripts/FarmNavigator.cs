using UnityEngine;

/// <summary>Interfaz de la vista general: nombre de cada jaula sobre ella y ayuda de controles.</summary>
public class FarmNavigator : MonoBehaviour
{
    public SalmonFarmBuilder farm;
    public FarmCamera cam;

    GUIStyle label, hint;
    Texture2D texLabel;

    void InitStyles()
    {
        if (label != null) return;
        texLabel = SolidTex(new Color(0.05f, 0.10f, 0.14f, 0.72f));
        label = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = false,
            normal = { textColor = Color.white, background = texLabel },
            padding = new RectOffset(8, 8, 3, 3),
        };
        hint = new GUIStyle(label) { fontStyle = FontStyle.Normal };
    }

    static Texture2D SolidTex(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void OnDestroy()
    {
        if (texLabel != null) Destroy(texLabel);
    }

    void OnGUI()
    {
        if (farm == null || cam == null || cam.CurrentMode != FarmCamera.Mode.Overview) return;
        InitStyles();
        float fs = Mathf.Max(11f, Screen.height / 52f);
        label.fontSize = Mathf.RoundToInt(fs);
        hint.fontSize = Mathf.RoundToInt(fs * 0.9f);

        foreach (var c in farm.Cages)
            if (c.gameObject.activeInHierarchy) DrawWorldLabel(c.LabelWorld, c.displayName, label);

        var help = new GUIContent("Arrastrar: rotar   ·   Rueda: zoom");
        var hs = hint.CalcSize(help);
        GUI.Label(new Rect((Screen.width - hs.x) * 0.5f, Screen.height - hs.y - fs, hs.x, hs.y), help, hint);
    }

    void DrawWorldLabel(Vector3 world, string text, GUIStyle style)
    {
        var sp = cam.Cam.WorldToScreenPoint(world);
        if (sp.z <= 0f) return;
        var content = new GUIContent(text);
        var size = style.CalcSize(content);
        GUI.Label(new Rect(sp.x - size.x * 0.5f, Screen.height - sp.y - size.y, size.x, size.y), content, style);
    }
}
