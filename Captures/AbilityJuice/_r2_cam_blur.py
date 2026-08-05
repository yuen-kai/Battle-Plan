"""Is Clash Mini's impact frame smeared relative to its own wind-up frame? Is ours?

Compared WITHIN each strip so source compression cancels out. Blur is measured on the
static board furniture (not the effect) so the effect's own softness does not pollute it.
"""
import numpy as np
from PIL import Image
from scipy import ndimage
import os, glob

AJ = "/Users/cykai/Battle-Plan/Captures/AbilityJuice"


def panels(path):
    im = Image.open(path).convert("L")
    W, H = im.size
    a = np.asarray(im, dtype=np.float64)
    gut = (W - 3 * H) // 2
    return [a[:, 0:H], a[:, H + gut:2 * H + gut], a[:, W - H:W]], H


def sharpness(p):
    """Board-furniture edge acuity: 99th-pct gradient over local contrast.
    Effect pixels (very bright or very saturated blowout) are excluded."""
    m = np.ones_like(p, bool)
    b = int(p.shape[0] * 0.04)
    m[:b, :] = False; m[-b:, :] = False; m[:, :b] = False; m[:, -b:] = False
    m &= p < 225                       # drop the blowout
    m &= ~ndimage.binary_dilation(p > 240, np.ones((25, 25)))
    if m.mean() < 0.15:
        return None
    s = ndimage.gaussian_filter(p, 0.6)
    gy, gx = np.gradient(s)
    g = np.hypot(gx, gy)[m]
    if g.size < 5000:
        return None
    # acuity = how concentrated the gradient energy is in a few strong edges.
    # A smeared frame spreads the same contrast over more pixels -> lower p99.
    return float(np.percentile(g, 99.5)) / max(float(np.percentile(g, 60)), 1e-6)


print("=" * 104)
print("EDGE ACUITY per panel (higher = crisper). Ratio panel2/panel1 < 1 means the")
print("impact frame is smeared relative to the wind-up frame from the same source.")
print("=" * 104)
print(f"{'strip':>30} {'p1':>8} {'p2':>8} {'p3':>8} | {'p2/p1':>7} {'p3/p1':>7}")
ratios = []
for p in sorted(glob.glob(os.path.join(AJ, "strips/reference/*.jpg"))):
    ps, S = panels(p)
    v = [sharpness(x) for x in ps]
    if v[0] is None or v[1] is None:
        print(f"{os.path.basename(p)[:-4]:>30}  (skipped, blowout dominates)")
        continue
    r2 = v[1] / v[0]
    r3 = v[2] / v[0] if v[2] else float("nan")
    ratios.append(r2)
    print(f"{os.path.basename(p)[:-4]:>30} {v[0]:8.3f} {v[1]:8.3f} "
          f"{v[2] if v[2] else float('nan'):8.3f} | {r2:7.3f} {r3:7.3f}")

for lab, path in (("OURS r2 camera", "strips/r2/camera.jpg"),
                  ("OURS r1 camera", "strips/r1/camera.jpg"),
                  ("OURS r2 impactcore", "strips/r2/impactcore.jpg"),
                  ("OURS r2 debris", "strips/r2/debris.jpg")):
    ps, S = panels(os.path.join(AJ, path))
    v = [sharpness(x) for x in ps]
    if v[0] and v[1]:
        print(f"{lab:>30} {v[0]:8.3f} {v[1]:8.3f} "
              f"{v[2] if v[2] else float('nan'):8.3f} | {v[1]/v[0]:7.3f} "
              f"{(v[2]/v[0]) if v[2] else float('nan'):7.3f}")

r = np.array(ratios)
print(f"\nClash Mini impact-frame acuity ratio: median {np.median(r):.3f}, "
      f"mean {r.mean():.3f}, {int((r<0.95).sum())}/{len(r)} strips measurably softer at impact")
