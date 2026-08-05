import numpy as np
from PIL import Image
import os

ROOT = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(ROOT, 'shots', 'r2', 'windup')
frames = sorted(f for f in os.listdir(SH) if f.endswith('.jpg'))


def load(i):
    return np.asarray(Image.open(os.path.join(SH, frames[i])).convert('RGB')).astype(np.float32)


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


f0 = load(0); L0 = lum(f0)

# ---- cell pitch from tile seams on a clean row of floor ----
row = L0[760]           # a clean floor row near the bottom
dv = np.abs(np.diff(row))
seams = np.nonzero(dv > 6)[0]
groups = []
for s in seams:
    if groups and s - groups[-1][-1] <= 3:
        groups[-1].append(s)
    else:
        groups.append([s])
cent = [int(np.mean(g)) for g in groups]
gaps = np.diff(cent)
gaps = gaps[gaps > 40]
print("== TILE PITCH (screen px per cell, bottom row y=760) ==")
print("  seam x:", cent)
print("  pitch samples:", list(gaps), " median %.0f px/cell" % np.median(gaps))
PITCH = float(np.median(gaps))

# same near the plate row
row2 = L0[520]
dv2 = np.abs(np.diff(row2)); s2 = np.nonzero(dv2 > 6)[0]
g2 = []
for s in s2:
    if g2 and s-g2[-1][-1] <= 3: g2[-1].append(s)
    else: g2.append([s])
c2 = [int(np.mean(g)) for g in g2]
gg = np.diff(c2); gg = gg[gg > 40]
print("  at plate row y=520: median %.0f px/cell" % (np.median(gg) if len(gg) else -1))
PITCH_MID = float(np.median(gg)) if len(gg) else PITCH

# ---- plate size in cells ----
for i, nm in ((22, 'panel1 t=+0.33'), (40, 'panel2 t=+0.93')):
    a = load(i); d = lum(a)-L0
    plate = (d < -45) & (L0 > 150)
    ys, xs = np.nonzero(plate)
    print("  %s plate bbox %.2f x %.2f cells (%d x %d px @ %.0f px/cell), area %.2f cells^2" % (
        nm, (xs.max()-xs.min())/PITCH_MID, (ys.max()-ys.min())/PITCH_MID,
        xs.max()-xs.min(), ys.max()-ys.min(), PITCH_MID, plate.sum()/(PITCH_MID**2)))

# ---- is the SAME cell set tinted in panel 1 and panel 2? ----
a1 = load(22); a2 = load(40)
p1 = (lum(a1)-L0 < -45) & (L0 > 150)
p2 = (lum(a2)-L0 < -45) & (L0 > 150)
inter = (p1 & p2).sum(); union = (p1 | p2).sum()
print("\n== PANEL1 vs PANEL2 TINTED FOOTPRINT ==")
print("  p1 %d px, p2 %d px, IoU = %.3f  (1.000 = literally the same shape)" % (p1.sum(), p2.sum(), inter/union))
print("  cells gained by p2: %.2f cells^2, cells lost: %.2f cells^2" % (
    (p2 & ~p1).sum()/PITCH_MID**2, (p1 & ~p2).sum()/PITCH_MID**2))

# ---- chroma-based plate edge (isolates amber tint from the grey halo) ----
print("\n== PLATE EDGE via chroma (R-B), immune to the grey halo ==")
for i in (22, 40):
    a = load(i)
    chroma = (a[..., 0]-a[..., 2]) - (f0[..., 0]-f0[..., 2])
    r = chroma[520]
    m = (lum(a)-L0 < -45)[520]
    xs = np.nonzero(m)[0]
    if len(xs) == 0:
        continue
    for edge, nm in ((xs.min(), 'left'), (xs.max(), 'right')):
        lo, hi = max(0, edge-24), min(1023, edge+24)
        seg = r[lo:hi]
        a_, b_ = seg.min(), seg.max(); amp = b_-a_
        tl, th = a_+0.1*amp, a_+0.9*amp
        idx = np.nonzero((seg >= tl) & (seg <= th))[0]
        w = (idx.max()-idx.min()+1) if len(idx) else 1
        print("  f%d %-5s x=%4d : chroma step %5.1f over %2d px = %5.1f /px" % (i, nm, edge, amp, w, amp/w))

# ---- RING: radial profile of grey (achromatic) darkening around plate centre ----
print("\n== RING (achromatic darkening), radial profile about plate centre ==")
ys, xs = np.nonzero(p1)
CX, CY = xs.mean(), ys.mean()
Y, X = np.mgrid[0:1024, 0:1024]
Rr = np.hypot(X-CX, (Y-CY)/0.62)      # de-squash for camera tilt
edges = np.arange(0, 620, 20)
print("   frame   t      " + " ".join("%4d" % e for e in edges[:-1]))
for i in (12, 16, 20, 22, 26, 30, 34, 40, 44, 45):
    a = load(i)
    dgrey = lum(a)-L0
    chroma = np.abs((a[..., 0]-a[..., 2]) - (f0[..., 0]-f0[..., 2]))
    grey_only = (chroma < 12) & (L0 > 150)
    prof = []
    for k in range(len(edges)-1):
        m = (Rr >= edges[k]) & (Rr < edges[k+1]) & grey_only
        prof.append(dgrey[m].mean() if m.sum() > 200 else np.nan)
    print("   f%-3d %+6.2f  " % (i, i/30.0-0.40) + " ".join(("%4.0f" % v if np.isfinite(v) else "   .") for v in prof))
print("  (values are luminance delta vs clean plate; a 'ring' = a localised trough)")
