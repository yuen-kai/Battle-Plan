"""Round-4 death builder: calibrate linear-authored tones against what landed in frame.

The DeathMark/DeathShard shaders are unlit and fully opaque where they draw, so the values they
were authored with map straight through the post stack. Measuring the extremes of that region
gives the transfer curve every new tone has to be authored against.
"""
import numpy as np
from PIL import Image

death = "Captures/AbilityJuice/shots/r3/death"
pogo = "Captures/AbilityJuice/shots/r3/pogo"


def load(shot, f):
    return np.asarray(Image.open(f"{shot}/f{f:04d}.jpg").convert("RGB"), dtype=np.float32)


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def sat(a):
    mx, mn = a.max(-1), a.min(-1)
    return np.where(mx > 1e-3, (mx - mn) / np.maximum(mx, 1e-3), 0.0)


base = lum(load(death, 2))
a = load(death, 30)
mask = (lum(a) - base) < -25
L = lum(a)[mask]
print("mark region f30: n=%d  min %.0f  p1 %.0f  p5 %.0f  p25 %.0f  med %.0f  p75 %.0f  p95 %.0f  max %.0f"
      % (mask.sum(), L.min(), *[np.percentile(L, p) for p in (1, 5, 25, 50, 75, 95)], L.max()))
print("  authored linear extremes: core 0.020 (both) .. shard lit 0.115, mark edge 0.086")
print("  grain multiplies the mark by 0.62..1.38 -> 0.0124..0.0276 core, 0.053..0.119 edge")

# how many pixels are fully clipped anywhere in either shot
for name, shot, n in (("death", death, 73), ("pogo", pogo, 96)):
    full = 0
    anyc = 0
    peak = 0
    for f in range(0, n):
        try:
            im = load(shot, f)
        except FileNotFoundError:
            break
        full += int((im.min(-1) >= 254).sum())
        anyc += int((im.max(-1) >= 254).sum())
        peak = max(peak, float(lum(im).max()))
    print(f"{name}: fully-clipped {full}  any-channel-clipped {anyc}  peak L {peak:.0f}")

# pogo landing peak frame, for the numbers the critic quoted
pbase = lum(load(pogo, 2))
for f in (44, 45, 46, 47, 48, 49, 50, 52):
    im = load(pogo, f)
    m = (lum(im) - pbase) < -25
    if m.sum() == 0:
        continue
    print("pogo f%d: dark %5d (%.2f cells2) Lmed %5.1f satmed %.3f  fullclip %4d"
          % (f, m.sum(), m.sum() / 218.0 ** 2, np.median(lum(im)[m]), np.median(sat(im)[m]),
             int((im.min(-1) >= 254).sum())))
