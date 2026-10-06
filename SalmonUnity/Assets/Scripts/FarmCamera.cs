using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Cámara de la salmonera. En la vista general orbita lentamente alrededor de la
/// grilla; dentro de una jaula orbita bajo el agua alrededor de su red. En ambos
/// modos arrastrar con el mouse rota y la rueda acerca/aleja. Entre uno y otro
/// vuela por una curva con easing (FlyToCage / FlyToOverview).
/// Modo Section ("Corte lateral"): cámara ortográfica mirando al norte desde el plano de
/// corte, con exageración vertical; arrastrar mueve a los lados y la rueda acerca (hasta ×1).
/// El input se lee con IMGUI (Event.current) porque el proyecto usa solo el Input System nuevo.
/// </summary>
[RequireComponent(typeof(Camera))]
public class FarmCamera : MonoBehaviour
{
    public enum Mode { Overview, Transition, Cage, Section }

    [Header("Vista general")]
    public Vector3 overviewTarget = new(0f, -2f, 0f);
    public float overviewDistance = 100f;
    public float minOverviewDistance = 40f, maxOverviewDistance = 330f;
    public float overviewPitch = 21f;
    public float minOverviewPitch = 6f, maxOverviewPitch = 80f;
    public float overviewYaw = 25f;
    [Tooltip("Grados por segundo de la órbita automática")]
    public float overviewAutoSpeed = 2.5f;

    [Header("Dentro de una jaula")]
    public float cageDistance = 17f;
    public float minCageDistance = 9f, maxCageDistance = 30f;
    public float cagePitch = 5f;
    public float minCagePitch = -35f;
    [Tooltip("Inclinación máxima de la mirada en la jaula: sobre el límite de la superficie la cámara ya no sube, solo mira más hacia abajo")]
    public float maxCageLookPitch = 70f;
    public float cageAutoSpeed = 4f;
    [Tooltip("Altura máxima (m) de la cámara en la jaula: siempre bajo la superficie")]
    public float maxCageCameraY = -0.5f;

    [Header("Transición")]
    public float flightDuration = 1.5f;
    [Tooltip("Altura (m) del punto de control de la curva sobre la jaula: define el picado")]
    public float flightArcHeight = 20f;

    [Header("Corte lateral")]
    [Tooltip("Metros que se ven detrás del plano de corte")]
    public float sectionViewDepth = 90f;

    [Header("Control con el mouse")]
    public float degreesPerPixel = 0.25f;
    [Tooltip("Cambio relativo de distancia por unidad de rueda")]
    public float zoomStep = 0.04f;
    [Tooltip("Segundos sin input antes de retomar la órbita automática")]
    public float idleBeforeAuto = 4f;
    [Tooltip("Píxeles de arrastre a partir de los cuales un clic pasa a ser rotación")]
    public float clickTolerance = 6f;

    public Mode CurrentMode { get; private set; } = Mode.Overview;
    public Camera Cam { get; private set; }

    /// Clic sin arrastre (posición en coordenadas GUI).
    public event Action<Vector2> Clicked;
    /// Si devuelve true para una posición GUI, la cámara ignora ese clic/arrastre (paneles, botones).
    public Func<Vector2, bool> IsOverUi;

    Vector3 cageTarget;
    float cageYaw, cagePitchNow, cageDist;
    float lastInput = -100f;
    float secX, secWidth, secMaxWidth, secLimitX, secBottom, secTop, secCutZ, savedNear, savedFar;
    bool dragging;
    float dragDist;

    void Awake() => Cam = GetComponent<Camera>();

    void LateUpdate()
    {
        if (CurrentMode == Mode.Transition) return;
        if (CurrentMode == Mode.Section) { SectionPose(); return; }
        bool idle = Time.unscaledTime - lastInput > idleBeforeAuto;
        Vector3 p; Quaternion r;
        if (CurrentMode == Mode.Overview)
        {
            if (idle) overviewYaw += overviewAutoSpeed * Time.deltaTime;
            OverviewPose(out p, out r);
        }
        else
        {
            if (idle) cageYaw += cageAutoSpeed * Time.deltaTime;
            CagePose(out p, out r);
        }
        transform.SetPositionAndRotation(p, r);
    }

    // ------------------------------------------------------------------ Poses

    void OverviewPose(out Vector3 p, out Quaternion r) =>
        OrbitPose(overviewTarget, overviewYaw, overviewPitch, overviewDistance, out p, out r);

    void CagePose(out Vector3 p, out Quaternion r)
    {
        float maxPitch = MaxCagePitch();
        // Más allá del límite la cámara se queda bajo la superficie e inclina la mirada (ver el fondo).
        cagePitchNow = Mathf.Clamp(cagePitchNow, minCagePitch, Mathf.Max(maxPitch, maxCageLookPitch));
        OrbitPose(cageTarget, cageYaw, Mathf.Min(cagePitchNow, maxPitch), cageDist, out p, out _);
        r = Quaternion.Euler(cagePitchNow, cageYaw, 0f);
    }

    /// Pitch de órbita más alto con la cámara aún bajo la superficie.
    float MaxCagePitch()
    {
        float maxSin = (maxCageCameraY - cageTarget.y) / cageDist;
        return maxSin >= 1f ? 89f : Mathf.Asin(Mathf.Clamp(maxSin, -1f, 1f)) * Mathf.Rad2Deg;
    }

    /// Pose de órbita: mira a target desde dist, con yaw/pitch en grados (pitch > 0 = desde arriba).
    public static void OrbitPose(Vector3 target, float yaw, float pitch, float dist, out Vector3 pos, out Quaternion rot)
    {
        rot = Quaternion.Euler(pitch, yaw, 0f);
        pos = target - rot * Vector3.forward * dist;
    }

    // ------------------------------------------------------------------ Vuelo

    /// Vuela desde la vista general hasta quedar bajo el agua frente a `focus`.
    /// onSurface(true/false) se llama al cruzar la superficie hacia abajo/arriba.
    /// yaw/pitch/dist opcionales: por defecto de frente a la red (yaw 0, mirando hacia +Z como
    /// la cámara del video). Un pitch alto se limita para no salir del agua.
    public void FlyToCage(Vector3 focus, Action<bool> onSurface, Action onArrive,
                          float yaw = 0f, float? pitch = null, float? dist = null)
    {
        if (CurrentMode != Mode.Overview) return;
        cageTarget = focus;
        cageYaw = yaw;
        cageDist = dist ?? cageDistance;
        cagePitchNow = Mathf.Min(pitch ?? cagePitch, MaxCagePitch()); // al llegar, mirando al foco
        StartCoroutine(Fly(true, onSurface, onArrive));
    }

    /// Vuelo inverso: desde la pose actual en la jaula a la vista general tal como estaba.
    public void FlyToOverview(Action<bool> onSurface, Action onArrive)
    {
        if (CurrentMode != Mode.Cage) return;
        StartCoroutine(Fly(false, onSurface, onArrive));
    }

    IEnumerator Fly(bool entering, Action<bool> onSurface, Action onArrive)
    {
        CurrentMode = Mode.Transition;
        dragging = false;
        OverviewPose(out var pA, out _);
        CagePose(out var pB, out _);
        // Curva cuadrática: el punto de control está sobre la jaula, así la cámara
        // se acerca por arriba y termina en picado bajo el agua.
        var away = pA - pB;
        away.y = 0f;
        away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.back;
        var ctrl = pB + Vector3.up * flightArcHeight + away * flightArcHeight * 0.75f;

        bool under = transform.position.y < 0f;
        for (float t = 0f; ; t += Time.deltaTime / Mathf.Max(flightDuration, 0.01f))
        {
            float u = Mathf.Clamp01(t);
            float e = u < 0.5f ? 4f * u * u * u : 1f - Mathf.Pow(-2f * u + 2f, 3f) * 0.5f; // easeInOutCubic
            float s = entering ? e : 1f - e;
            float k = 1f - s;
            var pos = k * k * pA + 2f * k * s * ctrl + s * s * pB;
            var look = Vector3.Lerp(overviewTarget, cageTarget, s);
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));

            bool nowUnder = pos.y < 0f;
            if (nowUnder != under) { under = nowUnder; onSurface?.Invoke(under); }
            if (u >= 1f) break;
            yield return null;
        }
        CurrentMode = entering ? Mode.Cage : Mode.Overview;
        lastInput = Time.unscaledTime; // pausa breve antes de retomar la órbita automática
        onArrive?.Invoke();
    }

    // ------------------------------------------------------------------ Corte lateral

    /// Exageración vertical actual del corte (1 = escala real).
    public float SectionExaggeration => secWidth / Mathf.Max(Cam.aspect, 0.1f) / Mathf.Max(secTop - secBottom, 0.1f);

    /// Pasa al corte lateral: plano en z = cutZ (todo lo que está delante se recorta), x en
    /// ±halfWidth y profundidades de bottom a top. Empieza mostrando todo el ancho.
    public void EnterSection(float cutZ, float halfWidth, float bottom, float top)
    {
        if (CurrentMode != Mode.Overview) return;
        secCutZ = cutZ; secLimitX = halfWidth; secBottom = bottom; secTop = top;
        secMaxWidth = 2f * halfWidth;
        secWidth = secMaxWidth;
        secX = 0f;
        savedNear = Cam.nearClipPlane;
        savedFar = Cam.farClipPlane;
        Cam.orthographic = true;
        Cam.nearClipPlane = 0.5f;
        Cam.farClipPlane = sectionViewDepth;
        dragging = false;
        CurrentMode = Mode.Section;
        SectionPose();
    }

    public void ExitSection()
    {
        if (CurrentMode != Mode.Section) return;
        Cam.ResetProjectionMatrix();
        Cam.orthographic = false;
        Cam.nearClipPlane = savedNear;
        Cam.farClipPlane = savedFar;
        CurrentMode = Mode.Overview;
        lastInput = Time.unscaledTime;
    }

    void SectionPose()
    {
        float h = secTop - secBottom;
        float minWidth = h * Cam.aspect; // exageración ×1
        secWidth = Mathf.Clamp(secWidth, minWidth, Mathf.Max(secMaxWidth, minWidth));
        float half = secWidth * 0.5f;
        secX = half >= secLimitX ? 0f : Mathf.Clamp(secX, -secLimitX + half, secLimitX - half);
        float midY = (secTop + secBottom) * 0.5f;
        // La cámara queda medio metro antes del corte: el plano cercano (0,5 m) es el corte.
        transform.SetPositionAndRotation(new Vector3(secX, midY, secCutZ - 0.5f), Quaternion.identity);
        Cam.orthographicSize = h * 0.5f;
        Cam.projectionMatrix = Matrix4x4.Ortho(-half, half, -h * 0.5f, h * 0.5f, Cam.nearClipPlane, Cam.farClipPlane);
    }

    // ------------------------------------------------------------------ Input

    public void Orbit(Vector2 pixelDelta)
    {
        lastInput = Time.unscaledTime;
        float dy = pixelDelta.x * degreesPerPixel, dp = pixelDelta.y * degreesPerPixel;
        if (CurrentMode == Mode.Overview)
        {
            overviewYaw += dy;
            overviewPitch = Mathf.Clamp(overviewPitch + dp, minOverviewPitch, maxOverviewPitch);
        }
        else if (CurrentMode == Mode.Cage)
        {
            cageYaw += dy;
            cagePitchNow += dp; // CagePose lo limita
        }
        else if (CurrentMode == Mode.Section)
            secX -= pixelDelta.x * secWidth / Mathf.Max(Screen.width, 1); // SectionPose lo limita
    }

    public void Zoom(float wheel)
    {
        lastInput = Time.unscaledTime;
        float f = Mathf.Pow(1f + zoomStep, wheel);
        if (CurrentMode == Mode.Overview)
            overviewDistance = Mathf.Clamp(overviewDistance * f, minOverviewDistance, maxOverviewDistance);
        else if (CurrentMode == Mode.Cage)
            cageDist = Mathf.Clamp(cageDist * f, minCageDistance, maxCageDistance);
        else if (CurrentMode == Mode.Section)
            secWidth *= f; // SectionPose lo limita
    }

    void OnGUI()
    {
        var e = Event.current;
        if (CurrentMode == Mode.Transition) return;
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
}
