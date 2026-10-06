using System.Collections.Generic;
using UnityEngine;

public enum Stage { Smolt, Engorda, Precosecha }
public enum Season { Verano, Invierno }
public enum TimeOfDay { Dia, Noche }

/// <summary>Altura reservada arriba por la barra de selectores (la usan HUD, panel y títulos).</summary>
public static class FarmUi
{
    public static float TopInset;
}

/// <summary>
/// Condiciones de la salmonera: etapa, estación, hora y corriente. Traduce cada combinación a
/// un BehaviorProfile para las jaulas simuladas (cambio suave con FishSchool.BlendTo), a las
/// comidas automáticas, a la luz de la escena (todas las jaulas, incluida la de datos reales)
/// y a la intensidad de CurrentField.
/// Dibuja la barra superior de selectores y la ventana "ⓘ Supuestos", que se arma con las
/// mismas constantes que usa el modelo, así la tabla nunca queda desactualizada.
/// </summary>
public class FarmConditions : MonoBehaviour
{
    public SalmonFarmBuilder farm;
    public FarmNavigator nav;
    public Stage stage = Stage.Engorda;
    public Season season = Season.Verano;
    public TimeOfDay time = TimeOfDay.Dia;
    public CurrentStrength current = CurrentStrength.Media;
    public CurrentField field;
    public CurrentViz viz;
    [Tooltip("Duración (s) del cambio suave entre perfiles")]
    public float blendSeconds = 3f;

    // ------------------------------------------------------------------ Constantes del modelo

    struct StageData
    {
        public string label;
        public float weightKg;     // peso típico del modelo
        public float bodyLengths;  // velocidad de crucero en largos de cuerpo por segundo
        public int sampleFish;     // peces mostrados (muestra, no densidad real)
        public bool feeding;
    }

    static readonly StageData[] Stages =
    {
        new() { label = "Smolt (40–120 g)", weightKg = 0.08f, bodyLengths = 1.0f, sampleFish = 300, feeding = true },
        new() { label = "Engorda", weightKg = 1.5f, bodyLengths = 0.7f, sampleFish = 220, feeding = true },
        new() { label = "Precosecha (>2 kg, ayuno)", weightKg = 4.0f, bodyLengths = 0.5f, sampleFish = 160, feeding = false },
    };

    const float LengthWeightK = 0.011f;            // W(g) = K · L(cm)³
    const float ComfortMin = 8f, ComfortMax = 20f;  // °C
    const float PreferredTemp = 14f;                // °C, centro del rango cómodo
    const float SummerSurfaceT = 19f, SummerGradient = -1.0f;  // °C, °C/m
    const float WinterSurfaceT = 4f, WinterGradient = 0.6f;
    const float WinterSpeed = 0.7f, WinterAppetite = 0.4f;
    const float NightSpeed = 0.75f, NightStructure = 0.45f, NightSpread = 1.6f;
    const float DayWander = 0.1f, NightWander = 1.5f;   // deambular = factor × velocidad (m/s²)
    const float BaseDepthSpread = 1.0f;
    const float SummerDayShift = 0.8f, SummerNightShift = -1.5f;  // m (+ = más hondo)
    const float WinterDayShift = 0f, WinterNightShift = -0.5f;
    const float MinBodyLengths = 0.4f, MaxBodyLengths = 1.0f;  // BL/s, rango con fuente

    static readonly string[] StageLabels = { Stages[0].label, Stages[1].label, Stages[2].label };
    static readonly string[] SeasonLabels = { "Verano", "Invierno" };
    static readonly string[] TimeLabels = { "Día", "Noche" };

    public static float LengthFromWeight(float kg) => Mathf.Pow(kg * 1000f / LengthWeightK, 1f / 3f) / 100f;

    /// Temperatura (°C) a una profundidad (m) en la estación elegida.
    public float TemperatureAt(float depth) => season == Season.Verano
        ? SummerSurfaceT + SummerGradient * depth
        : WinterSurfaceT + WinterGradient * depth;

    /// Profundidad de la red cuya temperatura está más cerca de la preferida.
    public float ThermalDepth(float netDepth)
    {
        float best = 0f, bestErr = float.MaxValue;
        for (float z = 0f; z <= netDepth + 1e-3f; z += 0.1f)
        {
            float err = Mathf.Abs(TemperatureAt(z) - PreferredTemp);
            if (err < bestErr) { bestErr = err; best = z; }
        }
        return best;
    }

    float DayNightShift => season == Season.Verano
        ? (time == TimeOfDay.Dia ? SummerDayShift : SummerNightShift)
        : (time == TimeOfDay.Dia ? WinterDayShift : WinterNightShift);

    /// Perfil para una jaula simulada según las condiciones actuales y la variación propia de la jaula.
    public BehaviorProfile BuildProfile(FarmCage cage)
    {
        var sd = Stages[(int)stage];
        bool night = time == TimeOfDay.Noche, winter = season == Season.Invierno;
        float L = LengthFromWeight(sd.weightKg) * cage.lengthFactor;
        // Los factores de invierno y noche son supuestos; el resultado se mantiene dentro del rango con fuente.
        float bl = Mathf.Clamp(sd.bodyLengths * (winter ? WinterSpeed : 1f) * (night ? NightSpeed : 1f), MinBodyLengths, MaxBodyLengths);
        float speed = bl * L * cage.speedFactor;
        float appetite = sd.feeding && !night ? (winter ? WinterAppetite : 1f) : 0f;
        float structure = night ? NightStructure : 1f;

        var p = farm.simulatedProfile.Clone();
        p.fishLength = L;
        p.fishCount = Mathf.Min(300f, Mathf.Round(sd.sampleFish * cage.countFactor));
        p.meanSpeed = speed;
        p.maxAccel = 0.3f + 2f * speed;
        p.separationRadius = Mathf.Max(0.25f, 1.2f * L);
        p.neighborRadius = Mathf.Max(0.8f, 3.5f * L);
        p.alignmentWeight *= structure;
        p.cohesionWeight *= structure;
        p.circlingWeight *= structure * cage.circlingFactor;
        p.speedVariation += night ? 0.1f : 0f;
        p.wander = (night ? NightWander : DayWander) * speed;
        p.depthSpread = BaseDepthSpread * (night ? NightSpread : 1f);
        p.preferredDepth = Mathf.Clamp(ThermalDepth(farm.netDepth) + DayNightShift + cage.depthOffset,
                                       1.2f, farm.netDepth - 1.2f);
        p.appetite = appetite;
        return p;
    }

    bool AutoFeed => Stages[(int)stage].feeding && time == TimeOfDay.Dia;

    string FeedingNote()
    {
        if (!Stages[(int)stage].feeding) return "Comidas automáticas: no (ayuno antes de la cosecha)";
        if (time == TimeOfDay.Noche) return "Comidas automáticas: no (de noche no se alimenta)";
        return $"Comidas automáticas: sí · apetito {(season == Season.Invierno ? WinterAppetite : 1f) * 100f:F0} % de los peces";
    }

    /// Aplica las condiciones a todas las jaulas (suave, o al instante al iniciar).
    public void Apply(bool immediate)
    {
        var sd = Stages[(int)stage];
        foreach (var cage in farm.Cages)
        {
            if (cage.school == null) continue;
            var p = BuildProfile(cage);
            if (immediate) cage.school.profile = p;
            else cage.school.BlendTo(p, blendSeconds);
            cage.school.temperatureAt = TemperatureAt;
            cage.school.feedingNote = FeedingNote();
            cage.school.autoFeed = AutoFeed;
            cage.description = $"{sd.label} · {p.fishCount:F0} peces (muestra) · {p.fishLength * 100f:F0} cm";
        }
        farm.SetNight(time == TimeOfDay.Noche, immediate ? 0f : blendSeconds);
        if (field != null) field.SetStrength(current, immediate);
        thermalTexDirty = true;
    }

    public void SetCurrent(CurrentStrength c)
    {
        if (c == current) return;
        current = c;
        if (field != null) field.SetStrength(c);
    }

    public void Set(Stage s, Season se, TimeOfDay t)
    {
        if (s == stage && se == season && t == time) return;
        stage = s; season = se; time = t;
        Apply(false);
    }

    // ------------------------------------------------------------------ Supuestos y fuentes

    public const string NoSource = "supuesto sin fuente";

    public struct Assumption { public string what, value, source; }

    public List<Assumption> Assumptions()
    {
        string F(float v, string fmt = "F1") => v.ToString(fmt);
        float l0 = LengthFromWeight(Stages[0].weightKg) * 100f, l1 = LengthFromWeight(Stages[1].weightKg) * 100f,
              l2 = LengthFromWeight(Stages[2].weightKg) * 100f;
        const string frontiersPhys = "Frontiers in Physiology 2021 (doi 10.3389/fphys.2021.719594)";
        const string frontiersRob = "Frontiers in Robotics and AI 2025 (doi 10.3389/frobt.2025.1574161)";
        const string fao = "FAO, Programa de información de especies acuáticas";
        const string depomod = "Informe NewDEPOMOD, granja Muck (SEPA)";
        return new List<Assumption>
        {
            new() { what = "Velocidad de crucero", value = "0,4–1,0 largos de cuerpo/s (BL/s); el modelo nunca sale de este rango", source = frontiersPhys },
            new() { what = "Velocidad por etapa", value = $"Smolt {F(Stages[0].bodyLengths)} · Engorda {F(Stages[1].bodyLengths)} · Precosecha {F(Stages[2].bodyLengths)} BL/s", source = NoSource },
            new() { what = "Nado circular siguiendo la red", value = "cardumen en anillo que gira en un sentido", source = "Juell & Westerberg 1993; Oppedal et al. 2011" },
            new() { what = "Agrupación en profundidad", value = "1,5–5× la densidad media (panel: concentración)", source = "Oppedal et al. 2011 (revisión)" },
            new() { what = "Termorregulación", value = $"rango cómodo {ComfortMin:F0}–{ComfortMax:F0} °C; buscan su capa preferida", source = "Johansson et al. 2009" },
            new() { what = "Temperatura preferida", value = $"{PreferredTemp:F0} °C (centro del rango cómodo)", source = NoSource },
            new() { what = "Perfil térmico de verano", value = $"{SummerSurfaceT:F0} °C en superficie, {SummerGradient:+0.0;-0.0} °C/m ({SummerSurfaceT + SummerGradient * 8f:F0} °C a 8 m)", source = NoSource },
            new() { what = "Invierno: capa inferior más tibia", value = "agua estratificada; los peces bajan a la capa más tibia", source = frontiersRob },
            new() { what = "Perfil térmico de invierno", value = $"{WinterSurfaceT:F0} °C en superficie, {WinterGradient:+0.0;-0.0} °C/m ({WinterSurfaceT + WinterGradient * 8f:F1} °C a 8 m)", source = NoSource },
            new() { what = "Día/noche según la estación", value = "el cambio de profundidad entre día y noche depende de la estación", source = "Johansson et al. 2009" },
            new() { what = "Desplazamiento día/noche", value = $"verano {SummerDayShift:+0.0;-0.0} m de día, {SummerNightShift:+0.0;-0.0} m de noche; invierno {WinterDayShift:+0.0;-0.0} / {WinterNightShift:+0.0;-0.0} m (+ = más hondo)", source = NoSource },
            new() { what = "Días largos: suben y comen más", value = "en verano el apetito es mayor", source = frontiersRob },
            new() { what = "Factores de invierno", value = $"velocidad ×{F(WinterSpeed, "F2")}, apetito {WinterAppetite * 100f:F0} %", source = NoSource },
            new() { what = "Noche", value = $"velocidad ×{F(NightSpeed, "F2")}; alineación, cohesión y giro ×{F(NightStructure, "F2")}; deambular ×{NightWander / DayWander:F0}; dispersión vertical ×{F(NightSpread)}; sin alimentación", source = NoSource },
            new() { what = "Smolt", value = $"40–120 g (modelo: {Stages[0].weightKg * 1000f:F0} g)", source = fao },
            new() { what = "Engorda", value = $"~2 años en el mar (modelo: {F(Stages[1].weightKg)} kg)", source = fao },
            new() { what = "Precosecha", value = $"cosecha >2 kg con ayuno de hasta 3 días (modelo: {F(Stages[2].weightKg)} kg, sin alimentar)", source = fao },
            new() { what = "Densidad máxima", value = "20 kg/m³", source = fao },
            new() { what = "Peces mostrados", value = $"muestra representativa: {Stages[0].sampleFish} / {Stages[1].sampleFish} / {Stages[2].sampleFish} (no la densidad real)", source = NoSource },
            new() { what = "Largo según el peso", value = $"W = {LengthWeightK} · L³ (g, cm): {l0:F0} / {l1:F0} / {l2:F0} cm", source = NoSource },
            new() { what = "Caída del pellet", value = "0,095 m/s ±10 % (cada pellet su velocidad)", source = depomod },
            new() { what = "Comidas", value = FeedingValue(), source = NoSource },
            new() { what = "Ración y saciedad", value = RationValue(), source = NoSource },
            new() { what = "Frenesí", value = FrenzyValue(), source = NoSource },
            new() { what = "Esparcidor rotatorio", value = SpreadValue(), source = NoSource },
            new() { what = "Corriente media", value = $"Media {CurrentField.MeanSpeeds[1]:F2} m/s (rapidez media en un ciclo y en la columna de agua)", source = depomod },
            new() { what = "Corriente débil / fuerte", value = $"{CurrentField.MeanSpeeds[0]:F2} / {CurrentField.MeanSpeeds[2]:F2} m/s", source = NoSource },
            new() { what = "Deriva residual", value = $"rango 0,005–0,065 m/s: débil {CurrentField.ResidualSpeeds[0]:F3} · media {CurrentField.ResidualSpeeds[1]:F3} · fuerte {CurrentField.ResidualSpeeds[2]:F3} m/s", source = depomod },
            new() { what = "Marea semidiurna", value = $"período {CurrentField.RealTidalPeriodHours:F2} h (componente M2)", source = "Dato astronómico (constituyente M2)" },
            new() { what = "Tiempo de la marea en la demo", value = TideDemoValue(), source = NoSource },
            new() { what = "Forma de la marea", value = TideShapeValue(), source = NoSource },
            new() { what = "Corriente según la profundidad", value = "perfil de potencia 1/7 sobre el fondo (más lenta cerca del fondo)", source = "Soulsby 1997, Dynamics of Marine Sands" },
            new() { what = "Fondo", value = BedValue(), source = NoSource },
            new() { what = "Fondeo", value = MooringValue(), source = NoSource },
            new() { what = "Pesos de las reglas boids", value = "separación, alineación, cohesión, red, profundidad y giro", source = NoSource },
        };
    }

    FishSchool AnySchool()
    {
        foreach (var c in farm.Cages) if (c.school != null) return c.school;
        return null;
    }

    string FeedingValue()
    {
        var s = AnySchool();
        return s == null ? "—" : $"{s.mealSeconds:F0} s cada {s.mealInterval:F0} s de día (tiempo comprimido), o con \"Alimentar ahora\"";
    }

    string RationValue()
    {
        var s = AnySchool();
        return s == null ? "—" : $"{s.rationPerFish:F0} pellets por pez y comida; un pez se sacia con {s.satiation:F1}";
    }

    string FrenzyValue()
    {
        var s = AnySchool();
        return s == null ? "—" : $"los peces con hambre suben a ~{s.frenzyDepth:F1} m bajo el esparcidor a {s.frenzySpeed:F1}× su velocidad";
    }

    string SpreadValue()
    {
        var s = AnySchool();
        return s == null ? "—" : $"lanza la ración en un anillo de {s.spreadInner:F1}–{s.spreadOuter:F1} m de radio";
    }

    string TideDemoValue() => field == null ? "—" : $"un ciclo de marea dura {field.tidalPeriodSeconds:F0} s";

    string TideShapeValue() => field == null ? "—"
        : $"eje a {field.tideAxisDeg:F0}° del este, elipse estrecha (eje menor {field.minorAxis * 100f:F0} %), variación espacial ±{field.spatialNoise * 100f:F0} %; deriva hacia {field.residualDeg:F0}°";

    string BedValue() => $"~{farm.seabedDepth:F0} m bajo la salmonera, pendiente {farm.seabedSlope * 100f:F1} % hacia el centro del lago";

    string MooringValue() => $"2 líneas por esquina, alcance {farm.mooringScope:F0}:1 (cabo + cadena hasta un ancla de arrastre)";

    // ------------------------------------------------------------------ Interfaz

    public bool ShowAssumptions { get; set; }
    Rect barRect, windowRect;
    Vector2 windowScroll;
    GUIStyle bar, barLabel, toggle, infoButton, winBg, winTitle, th, td, tdSource, tdNoSource, closeButton, small;
    Texture2D texBar, texOn, texOff, texWin, texRow, texThermal;
    bool thermalTexDirty = true;

    /// Posición GUI sobre la barra o la ventana de supuestos (la cámara la ignora).
    public bool IsOverUi(Vector2 guiPos) => barRect.Contains(guiPos) || (ShowAssumptions && windowRect.Contains(guiPos));

    void OnDestroy()
    {
        foreach (var t in new[] { texBar, texOn, texOff, texWin, texRow, texThermal })
            if (t != null) Destroy(t);
    }

    void OnGUI()
    {
        if (farm == null) return;
        if (nav != null && nav.Busy) { FarmUi.TopInset = 0f; return; }
        InitStyles();
        GUI.depth = -10; // por encima del resto de la interfaz
        float fs = Mathf.Max(11f, Screen.height / 52f);
        bar.fontSize = barLabel.fontSize = toggle.fontSize = infoButton.fontSize = Mathf.RoundToInt(fs * 0.95f);
        // Si la barra no cabe en el ancho de la pantalla, achica su letra.
        float need = BarWidth(), avail = Screen.width - fs * 1.2f;
        if (need > avail)
            bar.fontSize = barLabel.fontSize = toggle.fontSize = infoButton.fontSize =
                Mathf.Max(8, Mathf.FloorToInt(toggle.fontSize * avail / need));
        winTitle.fontSize = Mathf.RoundToInt(fs * 1.3f);
        th.fontSize = td.fontSize = tdSource.fontSize = tdNoSource.fontSize = closeButton.fontSize = Mathf.RoundToInt(fs * 0.9f);
        small.fontSize = Mathf.RoundToInt(fs * 0.8f);

        float h = fs * 2.3f;
        barRect = new Rect(0f, 0f, Screen.width, h);
        FarmUi.TopInset = h;
        GUI.Box(barRect, GUIContent.none, bar);
        GUILayout.BeginArea(new Rect(fs * 0.6f, fs * 0.35f, Screen.width - fs * 1.2f, h - fs * 0.5f));
        GUILayout.BeginHorizontal();
        int st = Selector("Etapa", (int)stage, StageLabels);
        int se = Selector("Estación", (int)season, SeasonLabels);
        int ti = Selector("Hora", (int)time, TimeLabels);
        int cu = Selector("Corriente", (int)current, CurrentField.Labels);
        if (viz != null)
            viz.show = GUILayout.Toggle(viz.show, viz.show ? "Ver corriente ✓" : "Ver corriente", toggle,
                                        GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(true));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("ⓘ Supuestos", infoButton, GUILayout.ExpandHeight(true))) ShowAssumptions = !ShowAssumptions;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
        Set((Stage)st, (Season)se, (TimeOfDay)ti);
        SetCurrent((CurrentStrength)cu);

        if (ShowAssumptions) DrawAssumptions(fs);
    }

    /// Ancho (px) que ocupa la barra con la letra actual.
    float BarWidth()
    {
        float w = 0f;
        void Sel(string label, string[] options)
        {
            w += barLabel.CalcSize(new GUIContent(label)).x + barLabel.margin.horizontal + barLabel.fontSize * 1.2f;
            foreach (var o in options) w += toggle.CalcSize(new GUIContent(o)).x + toggle.margin.horizontal;
        }
        Sel("Etapa", StageLabels);
        Sel("Estación", SeasonLabels);
        Sel("Hora", TimeLabels);
        Sel("Corriente", CurrentField.Labels);
        w += toggle.CalcSize(new GUIContent("Ver corriente ✓")).x + infoButton.CalcSize(new GUIContent("ⓘ Supuestos")).x + 2f * toggle.margin.horizontal;
        return w * 1.12f; // holgura: márgenes y espacios de GUILayout que no se miden aquí
    }

    int Selector(string label, int value, string[] options)
    {
        GUILayout.Label(label, barLabel, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(true));
        int r = GUILayout.Toolbar(value, options, toggle, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(true));
        GUILayout.Space(barLabel.fontSize * 1.2f);
        return r;
    }

    void DrawAssumptions(float fs)
    {
        float w = Mathf.Min(Screen.width * 0.86f, 1100f), h = (Screen.height - FarmUi.TopInset) * 0.86f;
        windowRect = new Rect((Screen.width - w) * 0.5f, FarmUi.TopInset + (Screen.height - FarmUi.TopInset - h) * 0.5f, w, h);
        GUI.Box(windowRect, GUIContent.none, winBg);
        float pad = fs * 0.8f;
        GUILayout.BeginArea(new Rect(windowRect.x + pad, windowRect.y + pad, w - 2f * pad, h - 2f * pad));
        GUILayout.BeginHorizontal();
        GUILayout.Label("Supuestos del modelo y sus fuentes", winTitle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Cerrar ✕", closeButton)) ShowAssumptions = false;
        GUILayout.EndHorizontal();
        GUILayout.Label("La simulación de las jaulas 2–8, la corriente y el alimento usan estos valores. Lo marcado " +
                        "en naranja es una elección nuestra sin referencia que la respalde.", small);
        GUILayout.Space(fs * 0.4f);
        float cw = w - 2f * pad - GUI.skin.verticalScrollbar.fixedWidth - 6f;
        float[] cols = { 0.26f, 0.42f, 0.32f };
        Row(new[] { "Supuesto", "Valor en el modelo", "Fuente" }, cols, cw, th, th);
        windowScroll = GUILayout.BeginScrollView(windowScroll);
        foreach (var a in Assumptions())
            Row(new[] { a.what, a.value, a.source }, cols, cw, td, a.source == NoSource ? tdNoSource : tdSource);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    static void Row(string[] cells, float[] cols, float width, GUIStyle style, GUIStyle lastStyle)
    {
        GUILayout.BeginHorizontal();
        for (int i = 0; i < cells.Length; i++)
            GUILayout.Label(cells[i], i == cells.Length - 1 ? lastStyle : style, GUILayout.Width(width * cols[i]));
        GUILayout.EndHorizontal();
    }

    /// Barra vertical con el perfil térmico (superficie arriba), el rango cómodo y la profundidad del cardumen.
    public void DrawThermalWidget(Rect r, float netDepth, float schoolDepth, float fs)
    {
        InitStyles();
        if (thermalTexDirty || texThermal == null) BuildThermalTexture(netDepth);
        small.fontSize = Mathf.RoundToInt(fs * 0.8f);
        var bg = new Rect(r.x - fs * 0.4f, r.y - fs * 2.4f, r.width + fs * 9.5f, r.height + fs * 3.6f);
        GUI.Box(bg, GUIContent.none, winBg);
        GUI.Label(new Rect(bg.x + fs * 0.4f, bg.y + fs * 0.3f, bg.width, fs * 1.6f),
                  $"Temperatura · {SeasonLabels[(int)season].ToLowerInvariant()}", small);
        GUI.DrawTexture(r, texThermal, ScaleMode.StretchToFill);
        // Escala: superficie, mitad y fondo
        foreach (float z in new[] { 0f, netDepth * 0.5f, netDepth })
        {
            float y = r.y + r.height * (z / netDepth);
            GUI.Label(new Rect(r.xMax + fs * 0.3f, y - fs * 0.7f, fs * 8f, fs * 1.4f), $"{z:F0} m · {TemperatureAt(z):F1} °C", small);
        }
        // Profundidad del cardumen
        float sy = r.y + r.height * Mathf.Clamp01(schoolDepth / netDepth);
        GUI.DrawTexture(new Rect(r.x - fs * 0.3f, sy - 1.5f, r.width + fs * 0.6f, 3f), Texture2D.whiteTexture);
        GUI.Label(new Rect(r.xMax + fs * 0.3f, sy - fs * 0.7f + (Mathf.Abs(schoolDepth - netDepth * 0.5f) < 0.8f ? fs * 1.2f : 0f),
                           fs * 8f, fs * 1.4f), $"◄ cardumen {schoolDepth:F1} m", small);
    }

    void BuildThermalTexture(float netDepth)
    {
        const int n = 64;
        if (texThermal == null) texThermal = new Texture2D(1, n) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
        for (int i = 0; i < n; i++)
        {
            float z = netDepth * (1f - i / (n - 1f)); // fila 0 = abajo
            float t = TemperatureAt(z);
            var c = Color.Lerp(new Color(0.15f, 0.35f, 0.95f), new Color(0.95f, 0.30f, 0.15f), Mathf.InverseLerp(4f, 20f, t));
            if (t < ComfortMin || t > ComfortMax) c = Color.Lerp(c, Color.gray, 0.55f); // fuera del rango cómodo
            texThermal.SetPixel(0, i, c);
        }
        texThermal.Apply();
        thermalTexDirty = false;
    }

    static Texture2D Solid(Color c)
    {
        var t = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    void InitStyles()
    {
        if (bar != null) return;
        texBar = Solid(new Color(0.03f, 0.08f, 0.12f, 0.88f));
        texOn = Solid(new Color(0.15f, 0.55f, 0.80f, 1f));
        texOff = Solid(new Color(1f, 1f, 1f, 0.10f));
        texWin = Solid(new Color(0.02f, 0.08f, 0.13f, 0.95f));
        bar = new GUIStyle { normal = { background = texBar } };
        barLabel = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, wordWrap = false,
                                                  normal = { textColor = new Color(0.7f, 0.9f, 1f) } };
        toggle = new GUIStyle(GUI.skin.button)
        {
            normal = { background = texOff, textColor = new Color(0.85f, 0.9f, 0.95f) },
            hover = { background = texOff, textColor = Color.white },
            active = { background = texOn, textColor = Color.white },
            onNormal = { background = texOn, textColor = Color.white },
            onHover = { background = texOn, textColor = Color.white },
            onActive = { background = texOn, textColor = Color.white },
            padding = new RectOffset(10, 10, 3, 3),
            margin = new RectOffset(1, 1, 0, 0),
        };
        infoButton = new GUIStyle(toggle) { fontStyle = FontStyle.Bold };
        closeButton = new GUIStyle(toggle);
        winBg = new GUIStyle { normal = { background = texWin } };
        winTitle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        small = new GUIStyle(GUI.skin.label) { wordWrap = true, normal = { textColor = new Color(0.8f, 0.88f, 0.93f) } };
        th = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, wordWrap = true, normal = { textColor = new Color(0.7f, 0.9f, 1f) } };
        td = new GUIStyle(GUI.skin.label) { wordWrap = true, normal = { textColor = Color.white } };
        tdSource = new GUIStyle(td) { normal = { textColor = new Color(0.6f, 0.95f, 0.7f) } };
        tdNoSource = new GUIStyle(td) { fontStyle = FontStyle.Italic, normal = { textColor = new Color(1f, 0.65f, 0.25f) } };
    }
}
