"""Round-4 death builder: measure the floor, the clod, and find the corpse."""
import numpy as np
from PIL import Image

shot = "Captures/AbilityJuice/shots/r3/death"


def load(f):
    return np.asarray(Image.open(f"{shot}/f{f:04d}.jpg").convert("RGB"), dtype=np.float32)


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def sat(a):
    mx = a.max(-1)
    mn = a.min(-1)
    return np.where(mx > 1e-3, (mx - mn) / np.maximum(mx, 1e-3), 0.0)


def hue(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx, mn = a.max(-1), a.min(-1)
    d = np.maximum(mx - mn, 1e-6)
    h = np.zeros_like(mx)
    m = mx == r
    h[m] = (60 * ((g - b) / d))[m]
    m = mx == g
    h[m] = (60 * (2 + (b - r) / d))[m]
    m = mx == b
    h[m] = (60 * (4 + (r - g) / d))[m]
    return h % 360


clean = load(2)
print("clean frame floor: L mean %.1f  median %.1f  p5 %.1f p95 %.1f" % (
    lum(clean).mean(), np.median(lum(clean)), np.percentile(lum(clean), 5),
    np.percentile(lum(clean), 95)))

# central board region only, away from the props at the top
box = (slice(380, 720), slice(330, 760))
floor = clean[box]
print("centre floor patch: L median %.1f  sat %.3f  hue %.0f" % (
    np.median(lum(floor)), np.median(sat(floor)), np.median(hue(floor))))

base = lum(clean)
for f in (12, 14, 16, 18, 20, 22, 24, 26, 30, 34, 40, 46):
    a = load(f)
    L = lum(a)
    d = L - base
    dark = (d < -25)
    n = dark.sum()
    if n == 0:
        print(f"f{f:02d}: nothing")
        continue
    S = sat(a)[dark]
    H = hue(a)[dark]
    hot = (a.max(-1) >= 250).sum()
    brightsat = ((lum(a) > 140) & (sat(a) > 0.45)).sum()
    print(
        f"f{f:02d} t={-0.4 + f/30:+.3f}s  dark px {n:6d} ({n/ (218.0**2):.2f} cells2) "
        f"L med {np.median(L[dark]):5.1f}  sat med {np.median(S):.3f}  hue med {np.median(H):5.0f}"
        f"  clipped {hot:5d}  bright+sat {brightsat:5d}"
    )
