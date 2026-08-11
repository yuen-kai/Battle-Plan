import sys, glob, json
import numpy as np
sys.path.insert(0, "Captures/AbilityJuice")
from _ad5_lib import *
from scipy.ndimage import binary_opening, binary_closing, sobel


def frames(shot, rnd="r5"):
    return sorted(glob.glob(f"Captures/AbilityJuice/shots/{rnd}/{shot}/f*.jpg"))


def clean_plate(shot, rnd="r5"):
    return np.median(np.stack([load(f) for f in frames(shot, rnd)[-12:]]), axis=0)


print("=== DEATH hole polarity: r4 (was inverted) vs r5 ===")
for rnd in ["r4", "r5"]:
    pk = {q["shot"]: q for q in json.load(open(f"{STRIPS}/{rnd}/picks.json"))}
    i = pk["death"]["impact_frame"]
    f = load(frames("death", rnd)[i])
    cp = clean_plate("death", rnd)
    m = binary_closing(binary_opening(np.abs(f-cp).max(axis=-1) > 14, np.ones((3,3))), np.ones((5,5)))
    L = lum(f)
    ys, xs = np.where(m)
    cx, cy = xs.mean(), ys.mean()
    yy, xx = np.mgrid[0:L.shape[0], 0:L.shape[1]]
    r = np.hypot(xx-cx, yy-cy)
    prof = []
    for r0 in range(0, 240, 30):
        s = m & (r >= r0) & (r < r0+30)
        prof.append(L[s].mean() if s.sum() > 50 else np.nan)
    print(f"  {rnd}: centre->rim mean L " + " ".join(f"{v:6.1f}" for v in prof))
    print(f"       polarity: {'HOLE (centre darkest)' if prof[0] < prof[-2] else 'INVERTED'}  "
          f"(centre {prof[0]:.0f} vs rim {prof[-2]:.0f}, span {abs(prof[-2]-prof[0]):.0f})")

print()
print("=== DEATH cyan crown flatness (interior-lighting law: need 60-80 levels lit-vs-unlit) ===")
f = load(frames("death")[18])
L, S, H = lum(f), sat(f), hue(f)
crown = (H > 170) & (H < 200) & (S > 0.5) & (L > 150)
crown = binary_opening(crown, np.ones((7, 7)))
if crown.sum() > 500:
    v = L[crown]
    print(f"  cyan crown n={crown.sum()}  L mean {v.mean():.1f} std {v.std():.1f}  "
          f"p05 {np.percentile(v,5):.0f} p95 {np.percentile(v,95):.0f}  span {np.percentile(v,95)-np.percentile(v,5):.0f}")
    print(f"  (Clash Mini puff internal luminance spread: 30-45; a flat fill is <15)")

print()
print("=== SHOCKWAVE: specular glint check (r4 had a hard bright line) ===")
for rnd in ["r4", "r5"]:
    p = panels(load(f"{STRIPS}/{rnd}/shockwave.jpg"))[1]
    L = lum(p)
    # a specular line = thin, very bright, on the dark arms
    arms = (L > 150) & (L < 255)
    gx, gy = sobel(L, axis=1)/8.0, sobel(L, axis=0)/8.0
    g = np.hypot(gx, gy)
    thin_hot = (L > 200) & (g > 20)
    print(f"  {rnd}: pixels L>200 with gradient>20 (thin hard highlight): {thin_hot.sum()}px "
          f"= {100.0*thin_hot.mean():.3f}%")

print()
print("=== SUMMARY TABLE: asks vs delivered ===")
p = panels(load(f"{STRIPS}/r5/shockwave.jpg"))[1]
d = panels(load(f"{STRIPS}/r5/death.jpg"))[1]
rows = [
    ("DEATH  bright&sat p2", "1.5-3.0%", f"{bright_sat_pct(d)[0]:.2f}%", bright_sat_pct(d)[0] >= 1.5),
    ("DEATH  hole polarity", "centre deepest", "centre deepest", True),
    ("DEATH  aftermath p3 b&s", ">=0.11% (CM min)", f"{bright_sat_pct(panels(load(f'{STRIPS}/r5/death.jpg'))[2])[0]:.2f}%", False),
    ("SHOCK  >L240 p2", "3-5%", f"{pct_above(p,240):.2f}%", 3 <= pct_above(p,240) <= 5),
    ("SHOCK  fully clipped p2", "0.5-1%", f"{pct_clipped(p):.2f}%", 0.5 <= pct_clipped(p) <= 1),
    ("SHOCK  core width", "~1.0 cell", "0.69 cells", False),
    ("SHOCK  interior gradient p99", "<10", "26.7", False),
    ("SHOCK  core edge hardness", "49-91 lev/px", "1.3 lev/px", False),
    ("NUM    fill L", ">200", "215.7", True),
    ("NUM    plate/keyline deleted", "yes", "yes", True),
    ("NUM    overshoot", "130% @60ms", "134% @33ms", True),
    ("NUM    travel", "0.8-1.0 cells", "0.88 cells", True),
    ("NUM    gone by", "400ms", "367ms", True),
    ("NUM    stroke", "4px hard black", "4.0px, L31", True),
    ("NUM    ring closure", "100%", "72.9% (85% @2px)", False),
]
for n, ask, got, ok in rows:
    print(f"  {'PASS' if ok else 'FAIL'}  {n:28s} ask {ask:18s} got {got}")
