using UnityEngine;

/// <summary>HUD simple (IMGUI) con el conteo de peces visibles y el tiempo.</summary>
public class SalmonHud : MonoBehaviour
{
    public TrajectoryPlayer player;
    GUIStyle style;

    void OnGUI()
    {
        if (player == null) return;
        if (style == null) style = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        style.fontSize = Mathf.RoundToInt(Screen.height / 28f);
        style.normal.textColor = Color.white;

        float pad = Screen.height * 0.02f;
        float line = style.fontSize * 1.4f;
        var r = new Rect(pad, pad, Screen.width * 0.7f, line);
        GUI.Label(r, $"Salmones visibles: {player.VisibleCount}", style);
        r.y += line;
        GUI.Label(r, $"Tiempo: {player.CurrentTime:F1} / {player.Duration:F1} s", style);
    }
}
