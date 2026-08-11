"""The decisive test: is the unit's silhouette edge a hard step or a bloom ramp?"""
import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

P = {'r2': 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg',
     'r3': 'Captures/AbilityJuice/shots/r3/hitreact/f%04d.jpg'}


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


def frame(tag, f):
    return np.asarray(Image.open(P[tag] % f).convert('RGB')).astype(np.float32)


# ---- locate the unit's paint centroid in each frame (red-dominant, non-amber) ----
def paint_mask(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    green = (g - r > 34) & (g - b > 34)
    m = (r - b > 50) & (g - b <= 25) & ~green
    m = ndimage.binary_opening(m, np.ones((3, 3)))
    lab, n = ndimage.label(ndimage.binary_closing(m, np.ones((15, 15))))
    if n == 0:
        return m
    sizes = ndimage.sum(m, lab, range(1, n+1))
    return m & (lab == int(np.argmax(sizes)) + 1)


print('=' * 104)
print('EDGE PROFILE — radial scan out of the unit body, 360 rays.')
print('  ramp = pixels between "clearly unit" and "clearly floor" (L crosses 90 -> 165)')
print('=' * 104)
print(f"{'':>16} {'cx':>6} {'cy':>6} | {'rays w/ dark rim':>16} {'rim thick px':>13} "
      f"{'ramp px (med)':>14} {'ramp px (p90)':>14} | {'L/px slope':>10}")


def edge_report(tag, f, quadrant=None):
    a = frame(tag, f)
    L = lum(a)
    pm = paint_mask(a)
    if pm.sum() < 200:
        print(f"{tag+' f'+str(f):>16}   -- no paint left in frame (unit bleached) --")
        return
    ys, xs = np.where(pm)
    cy, cx = ys.mean(), xs.mean()
    core = ndimage.binary_fill_holes(ndimage.binary_closing(pm, np.ones((25, 25))))
    nrim, thick, ramps = 0, [], []
    used = 0
    for k in range(360):
        th = k * np.pi / 180.0
        if quadrant is not None and not (quadrant[0] <= (k % 360) < quadrant[1]):
            continue
        dx, dy = np.cos(th), np.sin(th)
        # find last pixel of the filled body along this ray
        last = None
        for rr in range(4, 260):
            x, y = int(round(cx + dx*rr)), int(round(cy + dy*rr))
            if not (0 <= x < a.shape[1] and 0 <= y < a.shape[0]):
                break
            if core[y, x]:
                last = rr
        if last is None:
            continue
        used += 1
        prof = []
        for rr in range(last, last + 60):
            x, y = int(round(cx + dx*rr)), int(round(cy + dy*rr))
            if not (0 <= x < a.shape[1] and 0 <= y < a.shape[0]):
                break
            prof.append(L[y, x])
        prof = np.array(prof)
        if len(prof) < 20:
            continue
        d = (prof < 60)
        run = 0
        i = 0
        while i < len(d) and d[i]:
            run += 1
            i += 1
        if run == 0:  # allow a 2px anti-aliased lead-in
            j = 0
            while j < 3 and j < len(d) and not d[j]:
                j += 1
            while j < len(d) and d[j]:
                run += 1
                j += 1
        if run > 0:
            nrim += 1
            thick.append(run)
        lo = np.where(prof < 90)[0]
        hi = np.where(prof > 165)[0]
        if len(lo) and len(hi):
            h = hi[hi > lo[0]]
            if len(h):
                ramps.append(h[0] - lo[0])
    if not ramps:
        ramps = [999]
    if not thick:
        thick = [0]
    slope = (165 - 90) / max(np.median(ramps), 1e-6)
    print(f"{tag+' f'+str(f)+f' t={(f-12)/30.0:+.3f}':>16} {cx:6.1f} {cy:6.1f} | "
          f"{nrim:6d}/{used:<9d} {np.median(thick):13.1f} {np.median(ramps):14.1f} "
          f"{np.percentile(ramps,90):14.1f} | {slope:10.1f}")


for tag, frames in (('r2', [8, 12, 13, 14]), ('r3', [8, 12, 13, 14, 15, 16])):
    for f in frames:
        edge_report(tag, f)
    print()

print('right-hand side only (away from the stamp, rays -60deg..+60deg):')
for tag, frames in (('r2', [8, 12, 13]), ('r3', [8, 12, 13, 14])):
    for f in frames:
        edge_report(tag, f, quadrant=(0, 60))
        edge_report(tag, f, quadrant=(300, 360))

# ---- bloom halo: does the clean floor lift near the unit? ----
print()
print('=' * 104)
print('BLOOM HALO — luminance lift on clean floor rings well away from the unit')
print('=' * 104)
for tag in ('r2', 'r3'):
    base = frame(tag, 8)
    Lb = lum(base)
    for f in (12, 13, 14, 15, 16):
        a = frame(tag, f)
        d = lum(a) - Lb
        ring = np.zeros(d.shape, bool)
        ring[250:330, 400:900] = True
        ring[640:720, 400:900] = True
        print(f'{tag} f{f} t={(f-12)/30.0:+.3f}: floor delta mean {d[ring].mean():+6.2f}  '
              f'p95 {np.percentile(d[ring],95):+6.2f}  max {d[ring].max():+6.1f}  '
              f'n(>8) {int((d[ring]>8).sum()):5d}/{int(ring.sum())}')
    print()
