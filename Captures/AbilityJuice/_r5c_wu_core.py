"""Core round-5 wind-up measurements: bright+saturated audit and charge isolation."""
import numpy as np
from PIL import Image
import os

from _r5c_wu_measure import load, luma, hsv, panels, S

RUNS = [(0, 512), (520, 1032), (1040, 1552)]
LABEL = ["p1 t=0.35 (fire-0.75)", "p2 t=0.95 (fire-0.15)", "p3 t=1.40 (fire+0.30)"]


def bs_stats(pan, Lmin=200, Smin=0.45):
    L = luma(pan)
    h, s, v = hsv(pan)
    m = (L > Lmin) & (s >= Smin)
    n = pan.shape[0] * pan.shape[1]
    return 100.0 * m.sum() / n, m


def main():
    imgs = {}
    for tag in ("r3", "r4", "r5"):
        imgs[tag] = load(f"{S}/{tag}/windup.jpg")

    print("### bright(L>200) AND saturated(S>=0.45), % of panel")
    print(f"{'panel':28s} {'r3':>9s} {'r4':>9s} {'r5':>9s}")
    for i, (a, b) in enumerate(RUNS):
        row = []
        for tag in ("r3", "r4", "r5"):
            pct, _ = bs_stats(imgs[tag][:, a:b])
            row.append(pct)
        print(f"{LABEL[i]:28s} {row[0]:9.3f} {row[1]:9.3f} {row[2]:9.3f}")

    print("\n### relaxed thresholds on r5 (to find where the light died)")
    for Lmin in (160, 180, 190, 200, 210, 220, 240, 250):
        line = []
        for i, (a, b) in enumerate(RUNS):
            pct, _ = bs_stats(imgs["r5"][:, a:b], Lmin=Lmin)
            line.append(f"{pct:7.3f}")
        print(f"  L>{Lmin:3d} & S>=0.45 : " + " ".join(line))
    for Smin in (0.20, 0.30, 0.40, 0.45, 0.60):
        line = []
        for i, (a, b) in enumerate(RUNS):
            pct, _ = bs_stats(imgs["r5"][:, a:b], Smin=Smin)
            line.append(f"{pct:7.3f}")
        print(f"  L>200 & S>={Smin:4.2f} : " + " ".join(line))

    print("\n### r4 same sweep for comparison")
    for Lmin in (160, 180, 190, 200, 210, 220, 240, 250):
        line = []
        for i, (a, b) in enumerate(RUNS):
            pct, _ = bs_stats(imgs["r4"][:, a:b], Lmin=Lmin)
            line.append(f"{pct:7.3f}")
        print(f"  L>{Lmin:3d} & S>=0.45 : " + " ".join(line))

    # ---- charge isolation by differencing against r3 (same scene, no new charge) ----
    print("\n### charge mask via |r5 - r3| and |r4 - r3| (per-panel)")
    np.save("/tmp/_r5c_imgs.npy", np.stack([imgs["r3"], imgs["r4"], imgs["r5"]]))
    for tag in ("r4", "r5"):
        d = np.abs(imgs[tag] - imgs["r3"]).max(-1)
        for i, (a, b) in enumerate(RUNS):
            dp = d[:, a:b]
            for thr in (12, 20, 30):
                m = dp > thr
                print(f"  {tag} {LABEL[i]:26s} diff>{thr:2d}: {m.sum():6d}px  {100.0*m.mean():6.3f}%")
            print()


if __name__ == "__main__":
    main()
