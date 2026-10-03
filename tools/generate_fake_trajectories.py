"""
Genera trayectorias simuladas con el MISMO formato que producirá vision/,
para poder desarrollar la escena de Unity antes de tener el detector listo.

Modelo: los salmones en jaula nadan en un anillo alrededor del centro (cardumen
circular). Una cámara lateral fija los ve; el tamaño aparente depende de la
distancia, y algunos peces "desaparecen" a ratos (oclusión / fuera de cuadro).

Uso:
    python tools/generate_fake_trajectories.py --fish 40 --seconds 30 --fps 25
"""
import argparse
import csv
import json
import math
from pathlib import Path

import numpy as np


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--fish", type=int, default=40)
    p.add_argument("--seconds", type=float, default=30)
    p.add_argument("--fps", type=int, default=25)
    p.add_argument("--seed", type=int, default=7)
    p.add_argument("--out", type=str, default=str(Path(__file__).resolve().parents[1] / "data"))
    a = p.parse_args()

    rng = np.random.default_rng(a.seed)
    n_frames = int(a.seconds * a.fps)

    # Jaula: radio 6 m, profundidad útil 4 m. Cámara en el borde mirando al centro.
    cage_r, depth_range = 6.0, 4.0
    cam = np.array([0.0, -cage_r - 1.0])  # posición de la cámara en el plano horizontal (x, z)
    fov = math.radians(70)

    # Cada pez: radio de su órbita, ángulo inicial, velocidad angular, altura, fase de ondulación
    radius = rng.uniform(2.0, 5.5, a.fish)
    theta0 = rng.uniform(0, 2 * math.pi, a.fish)
    omega = rng.normal(0.18, 0.03, a.fish)  # rad/s, casi todos en el mismo sentido (cardumen)
    height = rng.uniform(-depth_range / 2, depth_range / 2, a.fish)
    wobble = rng.uniform(0, 2 * math.pi, a.fish)
    body_len = rng.normal(0.65, 0.08, a.fish)  # metros

    rows = []
    for f in range(n_frames):
        t = f / a.fps
        th = theta0 + omega * t
        r = radius + 0.3 * np.sin(0.4 * t + wobble)
        x = r * np.cos(th)
        zc = r * np.sin(th)
        y = height + 0.25 * np.sin(0.6 * t + wobble)

        # Coordenadas relativas a la cámara
        dx, dz = x - cam[0], zc - cam[1]
        dist = np.hypot(dx, dz)
        ang = np.arctan2(dx, dz)  # ángulo horizontal respecto al eje óptico

        for i in range(a.fish):
            if abs(ang[i]) > fov / 2:
                continue  # fuera de cuadro
            if rng.random() < 0.06:
                continue  # oclusión aleatoria
            cx = 0.5 + ang[i] / fov
            cy = 0.5 - (y[i] / dist[i]) / math.tan(fov / 2) * 0.5
            if not (0 <= cy <= 1):
                continue
            scale = 1.2 / dist[i]
            w = float(np.clip(body_len[i] * scale, 0.01, 0.5))
            h = w * 0.35
            z = float(np.clip((dist[i] - 1.0) / (2 * cage_r), 0, 1))
            rows.append([f, round(t, 3), i + 1,
                         round(float(cx), 4), round(float(cy), 4),
                         round(w, 4), round(h, 4),
                         round(float(rng.uniform(0.6, 0.98)), 3), round(z, 4)])

    out = Path(a.out)
    out.mkdir(parents=True, exist_ok=True)
    with open(out / "trajectories.csv", "w", newline="") as fh:
        wr = csv.writer(fh)
        wr.writerow(["frame", "t", "id", "cx", "cy", "w", "h", "conf", "z"])
        wr.writerows(rows)
    with open(out / "meta.json", "w") as fh:
        json.dump({"fps": a.fps, "width": 1920, "height": 1080,
                   "frames": n_frames, "source": "synthetic"}, fh, indent=2)

    print(f"{len(rows)} filas, {n_frames} frames, {a.fish} peces -> {out}")


if __name__ == "__main__":
    main()
