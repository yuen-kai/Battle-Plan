import numpy as np
from PIL import Image
from scipy import ndimage
import os

ROOT = "/Users/cykai/Battle-Plan/Captures/AbilityJuice/shots/r2"
OUT = "/Users/cykai/Battle-Plan/Captures/AbilityJuice"

def load(shot, i):
    return np.asarray(Image.open(os.path.join(ROOT, shot, f"f{i:04d}.jpg")).convert("RGB"),
                      dtype=np.float64)

def lum(rgb):
    return 0.2126 * rgb[..., 0] + 0.7152 * rgb[..., 1] + 0.0722 * rgb[..., 2]

# camera distance 12 (dip) vs 11 (core): dip is 11/12 the on-screen size.
PXCELL = {"impactdip": 213.0, "impactcore": 232.0}
CENTRE = {"impactdip": (507.0, 483.0), "impactcore": (532.0, 478.0)}

def base(shot):
    return np.median(np.stack([load(shot, i) for i in range(0, 12)]), axis=0)

def floormask(shot):
    med = base(shot)
    L = lum(med)
    mx, mn = med.max(axis=2), med.min(axis=2)
    sat = (mx - mn) / np.maximum(mx, 1e-6)
    m = (L > 150) & (sat < 0.16)
    return ndimage.binary_erosion(m, np.ones((5, 5)))

print("=" * 82)
print("E.  RATIO FIELD IN CELL UNITS  (frame / pre-roll, floor pixels only)")
print("    ratio<1 = pixel got darker.  Compared at MATCHED WORLD RADIUS.")
print("=" * 82)

BANDS = [(0.0, 0.3), (0.3, 0.55), (0.55, 0.8), (0.8, 1.1), (1.1, 1.45),
         (1.45, 1.9), (1.9, 2.4), (2.4, 3.0), (3.0, 9.9)]

data = {}
for shot in ("impactdip", "impactcore"):
    med = base(shot)
    fm = floormask(shot)
    cy, cx = CENTRE[shot]
    pc = PXCELL[shot]
    H, W = fm.shape
    yy, xx = np.mgrid[0:H, 0:W]
    rr = np.hypot(yy - cy, xx - cx) / pc
    data[shot] = (med, fm, rr)

for shot in ("impactdip", "impactcore"):
    med, fm, rr = data[shot]
    b = lum(med)
    print(f"\n  --- {shot} --- (radius in CELLS; dip reach is claimed 2.74 cells)")
    print("  frame  t(s) " + "".join(f"{lo:.2f}-{hi:<4.2f}" for lo, hi in BANDS))
    for i in [11] + list(range(12, 21)) + [24, 30, 42]:
        L = lum(load(shot, i))
        row = []
        for lo, hi in BANDS:
            m = fm & (rr >= lo) & (rr < hi)
            row.append(f"{(L[m]/b[m]).mean():9.3f}" if m.sum() > 300 else "      ---")
        print(f"  f{i:04d} {(i-12)*0.03333:+5.2f}" + "".join(row))

print()
print("=" * 82)
print("F.  ISOLATING THE DIP:  ratio_dip / ratio_core at matched world radius")
print("    ~1.00 = the dip added nothing the flash was not already doing.")
print("=" * 82)
print("  frame  t(s) " + "".join(f"{lo:.2f}-{hi:<4.2f}" for lo, hi in BANDS))
for i in list(range(12, 20)) + [22, 30, 42]:
    row = []
    for lo, hi in BANDS:
        vals = {}
        for shot in ("impactdip", "impactcore"):
            med, fm, rr = data[shot]
            m = fm & (rr >= lo) & (rr < hi)
            if m.sum() > 300:
                b = lum(med)[m]
                vals[shot] = float((lum(load(shot, i))[m] / b).mean())
        if len(vals) == 2:
            row.append(f"{vals['impactdip']/vals['impactcore']:9.3f}")
        else:
            row.append("      ---")
    print(f"  f{i:04d} {(i-12)*0.03333:+5.2f}" + "".join(row))

print()
print("=" * 82)
print("G.  IS IT A DIP OR IS IT BLUE PAINT?  per-channel ratio, dip shot")
print("    A multiply dip: all three ratios < 1 together (tint 0.84/1.03/1.21 normalised).")
print("    Alpha blue over the floor: B ratio stays high while R collapses.")
print("=" * 82)
med, fm, rr = data["impactdip"]
for i in (12, 13, 14, 15, 16, 17, 18):
    print(f"  f{i:04d} t={(i-12)*0.03333:+.2f}s")
    F = load("impactdip", i)
    for lo, hi in BANDS[2:]:
        m = fm & (rr >= lo) & (rr < hi)
        if m.sum() < 300:
            continue
        r = [float((F[..., c][m] / np.maximum(med[..., c][m], 1)).mean()) for c in range(3)]
        print(f"      {lo:.2f}-{hi:.2f} cells   R={r[0]:.3f} G={r[1]:.3f} B={r[2]:.3f}   "
              f"B/R={r[2]/max(r[0],1e-6):.2f}")

print()
print("=" * 82)
print("H.  FULL-SCREEN FADE TEST  (dip shot). If the far corners darken as much as")
print("    the ring, it is a fade. Reach is claimed 2.74 cells = 583 px.")
print("=" * 82)
med, fm, rr = data["impactdip"]
b = lum(med)
H = W = 1024
corner = np.zeros((H, W), bool)
corner[:150, :150] = corner[:150, -150:] = corner[-150:, :150] = corner[-150:, -150:] = True
edge = np.zeros((H, W), bool)
edge[:70, :] = edge[-70:, :] = edge[:, :70] = edge[:, -70:] = True
ringm = fm & (rr >= 0.9) & (rr < 1.8)
print(f"  {'frame':>6} {'t(s)':>6} | {'ring 0.9-1.8c':>14} {'frame corners':>14} "
      f"{'frame edges':>13} {'deepest/corner':>15}")
for i in range(11, 20):
    L = lum(load("impactdip", i))
    rg = float((L[ringm] / b[ringm]).mean())
    cm = fm & corner
    em = fm & edge
    co = float((L[cm] / b[cm]).mean())
    ed = float((L[em] / b[em]).mean())
    print(f"  f{i:04d} {(i-12)*0.03333:+6.2f} | {rg:14.3f} {co:14.3f} {ed:13.3f} "
          f"{(1-rg)/max(1-co,1e-4):15.1f}x")

print()
print("=" * 82)
print("I.  RETURN TO NORMAL  (floor only, dip shot: tail frames vs pre-roll)")
print("=" * 82)
med, fm, rr = data["impactdip"]
b = lum(med)
for i in (17, 18, 19, 20, 21, 25, 30, 36, 42):
    L = lum(load("impactdip", i))
    d = L[fm] - b[fm]
    print(f"  f{i:04d} t={(i-12)*0.03333:+.2f}s  floor deltaL mean={d.mean():+7.3f} "
          f"p1={np.percentile(d,1):+7.2f} p99={np.percentile(d,99):+6.2f} "
          f"maxAbs={np.abs(d).max():6.2f}")

print()
print("=" * 82)
print("J.  THE ONLY QUESTION: does the flash read harder?")
print("    Michelson contrast of the white core against the floor it sits on,")
print("    and the peak core luminance, in each shot.")
print("=" * 82)
for shot in ("impactdip", "impactcore"):
    med, fm, rr = data[shot]
    pc = PXCELL[shot]
    cy, cx = CENTRE[shot]
    print(f"\n  --- {shot} ---")
    print(f"  {'frame':>6} {'t(s)':>6} | {'coreL(top1%)':>12} {'localfloorL':>12} "
          f"{'michelson':>10} {'ratio':>7} | {'clipped px':>10} {'coreArea(cells^2)':>17}")
    for i in range(12, 19):
        F = load(shot, i)
        L = lum(F)
        yy, xx = np.mgrid[0:1024, 0:1024]
        r = np.hypot(yy - cy, xx - cx) / pc
        inner = r < 0.5
        coreL = float(np.percentile(L[inner], 99))
        # nearest genuinely-floor annulus outside the effect body
        ring = fm & (r >= 1.0) & (r < 1.6)
        fl = float(np.median(L[ring]))
        mich = (coreL - fl) / max(coreL + fl, 1e-6)
        clipped = int(((F[..., 0] >= 254) & (F[..., 1] >= 254) & (F[..., 2] >= 254)).sum())
        area = float((L > 235).sum()) / (pc * pc)
        print(f"  f{i:04d} {(i-12)*0.03333:+6.2f} | {coreL:12.1f} {fl:12.1f} {mich:10.3f} "
              f"{coreL/max(fl,1):7.2f} | {clipped:10d} {area:17.3f}")
