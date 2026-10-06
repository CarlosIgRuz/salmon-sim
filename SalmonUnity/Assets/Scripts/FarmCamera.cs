using System;
using UnityEngine;

/// <summary>
/// Cámara de la salmonera. En la vista general orbita lentamente alrededor de la
/// grilla; arrastrar con el mouse rota y la rueda acerca/aleja. El input se lee
/// con IMGUI (Event.current) porque el proyecto usa solo el Input System nuevo.
/// </summary>
[RequireComponent(typeof(Camera))]
public class FarmCamera : MonoBehaviour
{
    public enum Mode { Overview, Transition, Cage }

    [Header("Vista general")]
    public Vector3 overviewTarget = new(0f, -2f, 0f);
    public float overviewDistance = 100f;
    public float minOverviewDistance = 40f, maxOverviewDistance = 330f;
    public float overviewPitch = 21f;
    public float minOverviewPitch = 6f, maxOverviewPitch = 80f;
    public float overviewYaw = 25f;
    [Tooltip("Grados por segundo de la órbita automática")]
    public float overviewAutoSpeed = 2.5f;

    [Header("Control con el mouse")]
    public float degreesPerPixel = 0.25f;
    [Tooltip("Cambio relativo de distancia por unidad de rueda")]
    public float zoomStep = 0.04f;
    [Tooltip("Segundos sin input antes de retomar la órbita automática")]
    public float idleBeforeAuto = 4f;
    [Tooltip("Píxeles de arrastre a partir de los cuales un clic pasa a ser rotación")]
    public float clickTolerance = 6f;

    public Mode CurrentMode { get; protected set; } = Mode.Overview;
    public Camera Cam { get; private set; }

    /// Clic sin arrastre (posición en coordenadas GUI).
    public event Action<Vector2> Clicked;
    /// Si devuelve true para una posición GUI, la cámara ignora ese clic/arrastre (paneles, botones).
    public Func<Vector2, bool> IsOverUi;

    float lastInput = -100f;
    bool dragging;
    float dragDist;

    void Awake() => Cam = GetComponent<Camera>();

    void LateUpdate()
    {
        if (CurrentMode != Mode.Overview) return;
        if (Time.unscaledTime - lastInput > idleBeforeAuto)
            overviewYaw += overviewAutoSpeed * Time.deltaTime;
        OrbitPose(overviewTarget, overviewYaw, overviewPitch, overviewDistance, out var p, out var r);
        transform.SetPositionAndRotation(p, r);
    }

    public void Orbit(Vector2 pixelDelta)
    {
        lastInput = Time.unscaledTime;
        if (CurrentMode != Mode.Overview) return;
        overviewYaw += pixelDelta.x * degreesPerPixel;
        overviewPitch = Mathf.Clamp(overviewPitch + pixelDelta.y * degreesPerPixel, minOverviewPitch, maxOverviewPitch);
    }

    public void Zoom(float wheel)
    {
        lastInput = Time.unscaledTime;
        if (CurrentMode != Mode.Overview) return;
        overviewDistance = Mathf.Clamp(overviewDistance * Mathf.Pow(1f + zoomStep, wheel),
                                       minOverviewDistance, maxOverviewDistance);
    }

    void OnGUI()
    {
        var e = Event.current;
        if (CurrentMode == Mode.Transition) { dragging = false; return; }
        switch (e.type)
        {
            case EventType.MouseDown:
                if (IsOverUi != null && IsOverUi(e.mousePosition)) break;
                dragging = true;
                dragDist = 0f;
                break;
            case EventType.MouseDrag:
                if (!dragging) break;
                dragDist += e.delta.magnitude;
                if (dragDist > clickTolerance) Orbit(e.delta);
                break;
            case EventType.MouseUp:
                if (dragging && dragDist <= clickTolerance && e.button == 0) Clicked?.Invoke(e.mousePosition);
                dragging = false;
                break;
            case EventType.ScrollWheel:
                if (IsOverUi != null && IsOverUi(e.mousePosition)) break;
                Zoom(e.delta.y);
                e.Use();
                break;
        }
    }

    /// Pose de órbita: mira a target desde dist, con yaw/pitch en grados (pitch > 0 = desde arriba).
    public static void OrbitPose(Vector3 target, float yaw, float pitch, float dist, out Vector3 pos, out Quaternion rot)
    {
        rot = Quaternion.Euler(pitch, yaw, 0f);
        pos = target - rot * Vector3.forward * dist;
    }
}
