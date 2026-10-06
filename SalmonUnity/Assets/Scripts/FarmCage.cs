using UnityEngine;

/// <summary>
/// Una jaula de la salmonera: nombre, collider para hover/clic, resaltado
/// y la fuente de sus peces: datos reales (TrajectoryPlayer) o simulación (FishSchool).
/// </summary>
public class FarmCage : MonoBehaviour
{
    public int index;
    public string displayName;
    [TextArea] public string description;
    public float size = 14f;
    public float netDepth = 8f;
    [Tooltip("Etiqueta del origen de los datos, visible en la vista general y dentro de la jaula")]
    public string dataLabel;
    public bool isRealData;
    public TrajectoryPlayer player;
    public FishSchool school;

    /// Peces de la jaula para el panel (datos reales o simulación), o null si está vacía.
    public IFishSource Source => player != null ? player : school != null ? (IFishSource)school : null;

    [Header("Resaltado (hover)")]
    public Color highlightColor = new(1f, 0.9f, 0.35f);
    public BoxCollider pickCollider;
    public LineRenderer outline;
    public Material[] tintMaterials;

    Color[] baseColors;
    public bool Highlighted { get; private set; }

    /// Centro de interés bajo el agua (local): la mitad de la red.
    public Vector3 FocusLocal => new(0f, -netDepth * 0.5f, 0f);
    public Vector3 FocusWorld => transform.TransformPoint(FocusLocal);
    /// Punto sobre la jaula donde se dibuja su nombre.
    public Vector3 LabelWorld => transform.TransformPoint(new Vector3(0f, 3.5f, 0f));

    public void SetHighlight(bool on)
    {
        if (on == Highlighted) return;
        Highlighted = on;
        if (outline != null) outline.enabled = on;
        if (tintMaterials == null) return;
        if (baseColors == null)
        {
            baseColors = new Color[tintMaterials.Length];
            for (int i = 0; i < tintMaterials.Length; i++) baseColors[i] = tintMaterials[i].color;
        }
        for (int i = 0; i < tintMaterials.Length; i++)
        {
            var m = tintMaterials[i];
            m.color = on ? Color.Lerp(baseColors[i], highlightColor, 0.6f) : baseColors[i];
            FarmKit.SetEmission(m, on ? highlightColor * 0.45f : Color.black);
        }
    }
}
