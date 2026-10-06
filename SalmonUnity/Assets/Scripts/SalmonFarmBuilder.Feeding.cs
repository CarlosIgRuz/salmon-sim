using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sistema de alimentación: tuberías flotantes desde el dosificador de los silos del pontón
/// hasta cada jaula, y un esparcidor rotatorio en superficie al centro de cada jaula.
/// Recorrido de cada tubería: cubierta del pontón → baja al agua → canal entre filas de
/// jaulas (paralela a las demás) → cruza el collar → flota hasta el esparcidor.
/// </summary>
public partial class SalmonFarmBuilder
{
    const float PipeRadius = 0.09f, PipeY = -0.04f;

    void BuildFeedLines(Transform parent)
    {
        float h = cageSize * 0.5f, pw = pontoonWidth, deckTop = 0.6f;
        // Cara frontal del dosificador (ver BuildPontoon) y tramo por la cubierta, entre caseta y silos.
        var doser = new Vector3(pontoonX + pw * 0.22f, 0.9f, pontoonLength * 0.08f + 2.4f - 3f);
        const float deckZ = 0.65f;
        var outside = new MeshBatch();

        // Grupos de tuberías por (canal, lado del pontón). Canal: el agua entre la fila y su vecina hacia el centro.
        float Channel(int r) => cageZ[r] < -0.01f ? cageZ[r] + h + spacing * 0.5f : cageZ[r] - h - spacing * 0.5f;
        var groups = new Dictionary<(int, int), List<FarmCage>>();
        foreach (var cage in cages)
        {
            int r = cage.index / cols, c = cage.index % cols;
            int side = c < cols / 2 ? -1 : 1;
            var key = (Mathf.RoundToInt(Channel(r) * 10f), side);
            if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<FarmCage>();
            list.Add(cage);
        }

        int bundle = 0;
        foreach (var kv in groups)
        {
            int side = kv.Key.Item2;
            // Por fila (lado del canal), la jaula más cercana al pontón va por fuera: así ninguna rama cruza otra tubería.
            foreach (int toward in new[] { -1, 1 })
            {
                var list = kv.Value.FindAll(cg => Mathf.Sign(cageZ[cg.index / cols] - Channel(cg.index / cols)) == toward);
                list.Sort((a, b) => Mathf.Abs(a.transform.localPosition.x - pontoonX).CompareTo(Mathf.Abs(b.transform.localPosition.x - pontoonX)));
                for (int rank = 0; rank < list.Count; rank++)
                {
                    var cage = list[rank];
                    int r = cage.index / cols;
                    float zc = Channel(r) + toward * (0.25f + (list.Count - 1 - rank) * 0.25f);
                    float cx = cage.transform.localPosition.x, cz = cageZ[r];
                    float edge = pontoonX + side * (pw * 0.5f + 0.2f);
                    float bx = cx - side * 2.2f; // rama junto al pasillo transversal, del lado del pontón
                    float dz = (bundle++ % 6 - 2.5f) * 0.11f;
                    var pts = new List<Vector3>
                    {
                        doser + new Vector3(side * 0.3f, 0f, 0f),
                        new(doser.x + side * 0.3f, deckTop + 0.1f, deckZ + dz),
                        new(edge, deckTop + 0.1f, deckZ + dz),
                        new(edge + side * 0.6f, PipeY, deckZ + dz),
                        new(edge + side * 1.8f, PipeY, zc),
                        new(bx, PipeY, zc),
                        new(bx, PipeY, cz - toward * (h + deckWidth + 0.15f)),
                        new(bx, DeckTop + 0.12f, cz - toward * (h + deckWidth)),
                    };
                    Pipe(outside, pts);
                    BuildCageFeed(cage, new Vector3(bx - cx, DeckTop + 0.12f, -toward * (h + deckWidth)), toward);
                }
            }
        }
        outside.Build(parent, "Tuberias", false);
    }

    /// Tramo dentro de la jaula (cruza el collar y flota hasta el centro) y el esparcidor.
    /// `from` es local a la jaula; `toward` = sentido +Z/-Z desde el canal hacia la jaula.
    void BuildCageFeed(FarmCage cage, Vector3 from, int toward)
    {
        float h = cageSize * 0.5f;
        var b = new MeshBatch();
        var inner = new Vector3(from.x, DeckTop + 0.12f, -toward * h);
        var water = new Vector3(from.x, 0.06f, -toward * (h - 0.8f));
        var hub = new Vector3(0f, 0.12f, 0f);
        var end = hub + (water - hub).normalized * 0.8f;
        Pipe(b, new List<Vector3> { from, inner, water, end });
        // Flotadores pequeños a lo largo del tramo en el agua
        int n = Mathf.Max(1, Mathf.FloorToInt(Vector3.Distance(water, end) / 2.2f));
        for (int i = 1; i < n; i++)
            b.Sphere(Mats.Buoy, Vector3.Lerp(water, end, i / (float)n) + Vector3.down * 0.05f, new Vector3(0.35f, 0.25f, 0.35f), Quaternion.identity);

        // Esparcidor: flotador, tubo vertical y cabezal rotatorio de 3 brazos.
        b.Cylinder(Mats.SpreaderFloat, new Vector3(0f, 0.05f, 0f), 0.8f, 0.4f, Quaternion.identity);
        b.Cylinder(Mats.Steel, new Vector3(0f, 0.55f, 0f), 0.12f, 0.6f, Quaternion.identity);
        b.Build(cage.transform, "Alimentacion", false);

        var rotor = new GameObject("Esparcidor (rotor)");
        rotor.transform.SetParent(cage.transform, false);
        rotor.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        var rb = new MeshBatch();
        rb.Cylinder(Mats.Rail, Vector3.zero, 0.26f, 0.28f, Quaternion.identity);
        for (int k = 0; k < 3; k++)
        {
            var q = Quaternion.Euler(0f, 120f * k, 0f);
            rb.Box(Mats.Steel, q * new Vector3(0.6f, 0f, 0f), new Vector3(1.0f, 0.08f, 0.12f), q);
            rb.Box(Mats.Rail, q * new Vector3(1.1f, -0.08f, 0f), new Vector3(0.12f, 0.24f, 0.16f), q);
        }
        rb.Build(rotor.transform, "Rotor", false);
        if (cage.school != null) cage.school.spreaderRotor = rotor.transform;
    }

    /// Tubería por una polilínea, con codos redondeados (esferas en las uniones).
    static void Pipe(MeshBatch b, List<Vector3> pts)
    {
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            Segment(b, Mats.Pipe, pts[i], pts[i + 1], PipeRadius);
            if (i > 0) b.Sphere(Mats.Pipe, pts[i], Vector3.one * PipeRadius * 2f, Quaternion.identity);
        }
    }
}
