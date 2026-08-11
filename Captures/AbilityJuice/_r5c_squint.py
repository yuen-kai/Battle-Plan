"""Thumbnail/squint test + caster silhouette scale + r4 onset comparison."""
import numpy as np
from PIL import Image
from scipy import ndimage
import os
from _r5c_wu_measure import load, luma, hsv, S
from _r5c_ref import panel_runs

OUT = os.path.dirname(os.path.abspath(__file__))
RUNS = [(0, 512), (520, 1032), (1040, 1552)]
PPC = 109.0


def squint(tag, imgs, pi):
    a, b = RUNS[pi]
    cur = imgs[tag][:, a:b]
    ref = imgs["r3"][:, a:b]
    out = []
    for k in (8, 4, 2):
        w = 512 // k
        c = np.asarray(Image.fromarray(cur.astype(np.uint8)).resize((w, w), Image.LANCZOS)).astype(float)
        r = np.asarray(Image.fromarray(ref.astype(np.uint8)).resize((w, w), Image.LANCZOS)).astype(float)
        d = np.abs(luma(c) - luma(r))
        hs, ss, vs = hsv(c)
        hr, sr, vr = hsv(r)
        out.append((w, float(d.max()), int((d > 12).sum()), float(np.abs(ss - sr).max())))
    return out


def caster_area(imgs, pi):
    """Pink team body silhouette area in the panel."""
    a, b = RUNS[pi]
    p = imgs["r3"][:, a:b]
    h, s, v = hsv(p)
    pink = ((h > 330) | (h < 12)) & (s > 0.35) & (v > 90)
    pink = ndimage.binary_closing(pink, np.ones((3, 3)))
    lab, n = ndimage.label(pink, np.ones((3, 3)))
    sizes = ndimage.sum(pink, lab, range(1, n + 1))
    if len(sizes) == 0:
        return 0
    big = int(sizes.max())
    j = int(np.argmax(sizes)) + 1
    ys, xs = np.nonzero(lab == j)
    return big, (xs.max() - xs.min() + 1), (ys.max() - ys.min() + 1)


def main():
    imgs = {t: load(f"{S}/{t}/windup.jpg") for t in ("r3", "r4", "r5")}
    print("### squint test: does the charge survive downsampling? (|dL| vs the no-charge r3 plate)")
    for pi in (0, 1):
        for tag in ("r4", "r5"):
            rows = squint(tag, imgs, pi)
            s = "  ".join(f"{w}px: dLmax {dm:5.1f} px>12 {n:5d}" for w, dm, n, ds in rows)
            print(f"  p{pi+1} {tag}: {s}")

    print("\n### caster silhouette")
    for pi in (0, 1):
        ca, cw, ch = caster_area(imgs, pi)
        print(f"  p{pi+1}: pink body {ca}px  {cw}x{ch}px = {cw/PPC:.2f}x{ch/PPC:.2f} cells")
        for tag, area in (("r4", (2111, 3168)[pi]), ("r5", (1562, 2425)[pi])):
            print(f"      {tag} charge area / caster body = {area/ca:.2f}x")

    print("\n### r4 vs r5 side-by-side headline numbers")
    hdr = f"{'':32s}{'r4 p1':>9s}{'r4 p2':>9s}{'r5 p1':>9s}{'r5 p2':>9s}"
    print(hdr)
    rows = [
        ("bright+saturated % of panel", 0.556, 0.865, 0.000, 0.000),
        ("charge area (cell^2)", 0.178, 0.267, 0.131, 0.204),
        ("charge px above L200", 1457, 2271, 347, 1344),
        ("  ...their max saturation", 1.000, 1.000, 0.344, 0.378),
        ("charge px S>=0.45", 1985, 3153, 347, 288),
        ("  ...their max luminance", 223, 229, 140, 121),
        ("charge mean saturation", 0.895, 0.943, 0.311, 0.286),
        ("interior L std (eroded)", 22.1, 28.4, 50.9, 14.0),
    ]
    for nm, a1, a2, b1, b2 in rows:
        print(f"{nm:32s}{a1:9.3f}{a2:9.3f}{b1:9.3f}{b2:9.3f}")

    # squint jpgs for eyeballing
    for tag in ("r3", "r4", "r5"):
        im = Image.fromarray(imgs[tag].astype(np.uint8)).resize((388, 128), Image.LANCZOS)
        im.save(f"{OUT}/_r5c_squint_{tag}.png")
    st = np.concatenate([np.asarray(Image.open(f"{OUT}/_r5c_squint_{t}.png")) for t in ("r3", "r4", "r5")], 0)
    Image.fromarray(st).resize((st.shape[1] * 2, st.shape[0] * 2), Image.NEAREST).save(f"{OUT}/_r5c_squint_all.png")
    print("\nwrote _r5c_squint_all.png (r3 / r4 / r5 at 1/4 scale)")


if __name__ == "__main__":
    main()
