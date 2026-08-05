"""Silhouette regularity: how close is the front to a perfect annulus, and is the rim a
constant-width hoop? Numbers the builder can aim at."""
import numpy as np
from PIL import Image

ROOT = "Captures/AbilityJuice/shots/r2/shockwave"
lum = lambda a: 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
load = lambda i: np.asarray(Image.open(f"{ROOT}/f{i:04d}.jpg")).astype(np.float64)
plate = np.median(np.stack([load(i) for i in range(11)]), axis=0)
Lp = lum(plate)
h, w = Lp.shape
cx, cy, axx, ayy = 511.5, 538.5, 302.5, 290.5
PXPU = 70.35
CELL = 189.9

for fr in [13, 15, 17, 19]:
    Lf = lum(load(fr))
    D = Lf - Lp
    outer, inner, thick, rimw, rimr = [], [], [], [], []
    for ang in np.linspace(0, 2 * np.pi, 360, endpoint=False):
        prof_d, prof_l, rr = [], [], []
        for t in np.arange(0.05, 2.0, 0.004):          # t in cells
            px = int(round(cx + np.cos(ang) * t * CELL * (axx / axx)))
            py = int(round(cy + np.sin(ang) * t * CELL * (ayy / axx)))
            if not (0 <= px < w and 0 <= py < h):
                break
            if Lp[py, px] < 150:                        # crate occludes the ray
                prof_d.append(np.nan); prof_l.append(np.nan); rr.append(t); continue
            prof_d.append(D[py, px]); prof_l.append(Lf[py, px]); rr.append(t)
        prof_d = np.array(prof_d); prof_l = np.array(prof_l); rr = np.array(rr)
        dark = prof_d < -30
        if np.nansum(dark) < 8:
            continue
        idx = np.where(dark)[0]
        outer.append(rr[idx[-1]]); inner.append(rr[idx[0]])
        thick.append(rr[idx[-1]] - rr[idx[0]])
        hot = np.where(prof_l > 230)[0]
        if len(hot) > 2:
            rimw.append((rr[hot[-1]] - rr[hot[0]]) * CELL)
            rimr.append(rr[hot].mean())
    outer = np.array(outer); thick = np.array(thick)
    print(f"f{fr:2d} t={-0.4+fr/30:+.3f}  n={len(outer)} rays")
    print(f"     outer radius  mean {outer.mean():.3f} cells   sd {outer.std():.4f} "
          f"({100*outer.std()/outer.mean():.1f}% of radius)   range {outer.min():.3f}-{outer.max():.3f}")
    print(f"     ring thickness mean {thick.mean():.3f} cells  sd {thick.std():.4f} "
          f"({100*thick.std()/thick.mean():.1f}%)")
    if rimw:
        rimw = np.array(rimw); rimr = np.array(rimr)
        print(f"     hot rim width mean {rimw.mean():.1f} px  sd {rimw.std():.1f} "
              f"({100*rimw.std()/rimw.mean():.1f}%)  |  rim radius sd "
              f"{100*np.std(rimr)/np.mean(rimr):.1f}% of radius, present on {len(rimw)}/360 rays")
    print()

print("=" * 78)
print("for scale: a perfect machine-drawn circle would be 0.0% sd.")
print("=" * 78)

# --- radial order: what is the outward stacking of light and matter? -------------------
print()
print("RADIAL STACKING at f17 (mean over 360 rays), r in cells:")
Lf = lum(load(17))
D = Lf - Lp
acc_l = np.zeros(200); acc_d = np.zeros(200); cnt = np.zeros(200)
for ang in np.linspace(0, 2 * np.pi, 720, endpoint=False):
    for k, t in enumerate(np.arange(0.02, 2.0, 0.01)):
        px = int(round(cx + np.cos(ang) * t * CELL))
        py = int(round(cy + np.sin(ang) * t * CELL * (ayy / axx)))
        if 0 <= px < w and 0 <= py < h and Lp[py, px] > 150 and k < 200:
            acc_l[k] += Lf[py, px]; acc_d[k] += D[py, px]; cnt[k] += 1
for k in range(0, 190, 8):
    if cnt[k] > 5:
        t = 0.02 + k * 0.01
        bar = "#" * int(max(0, (acc_l[k] / cnt[k] - 60) / 6))
        print(f"  r={t:.2f}c  L={acc_l[k]/cnt[k]:6.1f}  dL={acc_d[k]/cnt[k]:+7.1f}  {bar}")
