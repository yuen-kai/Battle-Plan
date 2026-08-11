"""Round-3 critic measurements for the damage-number badge."""

import glob
import json

import numpy as np
from PIL import Image

FOV = 60.0
FRAME = 1024
CELL = 2.7
CAM_D = 9.0
PITCH = np.radians(73.0)
LIFT = 2.2

PX_PER_UNIT_AT = lambda d: (FRAME / 2) / (d * np.tan(np.radians(FOV / 2)))
D_BADGE = CAM_D - LIFT * np.sin(PITCH)

print("=== CAMERA ARITHMETIC ===")
print(f"fov 60 perspective, pitch 73, cam distance to deck lookAt = {CAM_D}")
print(f"px/world-unit at deck   d={CAM_D:.3f} -> {PX_PER_UNIT_AT(CAM_D):.1f}")
print(f"  => px per cell at deck = {PX_PER_UNIT_AT(CAM_D)*CELL:.1f}  (full-res 1024)")
print(f"  => px per cell at deck = {PX_PER_UNIT_AT(CAM_D)*CELL/2:.1f}  (512 strip panel)")
print(f"popup lifted {LIFT} world-up -> closer along cam axis by {LIFT*np.sin(PITCH):.3f}")
print(f"px/world-unit at badge  d={D_BADGE:.3f} -> {PX_PER_UNIT_AT(D_BADGE):.1f}")
print(f"  MAGNIFICATION from the 2.2 lift = {CAM_D/D_BADGE:.4f}x")
PPU_BADGE = PX_PER_UNIT_AT(D_BADGE)
PPC_DECK = PX_PER_UNIT_AT(CAM_D) * CELL


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def load(run, i):
    return np.asarray(
        Image.open(f"shots/{run}/numbers/f{i:04d}.jpg").convert("RGB")
    ).astype(np.float32)


base2 = load("r2", 0)
Lb2 = lum(base2)
base1 = load("r1", 0)
Lb1 = lum(base1)

print("\n=== R2 BADGE GEOMETRY (settled frame 19, t=+0.233) ===")
f = load("r2", 19)
L = lum(f)
d = L - Lb2
plate = d < -50
ys, xs = np.nonzero(plate)
print(f"badge+tab bbox  w={xs.max()-xs.min()+1}px h={ys.max()-ys.min()+1}px")

# Plate body only: rows where the dark run is wide (tab rows are narrow).
rows = {y: np.nonzero(plate[y])[0] for y in range(ys.min(), ys.max() + 1)}
wide = [y for y, xr in rows.items() if len(xr) > 0 and xr.max() - xr.min() > 60]
py0, py1 = min(wide), max(wide)
pxs = np.concatenate([rows[y] for y in wide])
px0, px1 = pxs.min(), pxs.max()
pw, ph = px1 - px0 + 1, py1 - py0 + 1
print(f"plate only      w={pw}px h={ph}px")
print(
    f"   -> world {pw/PPU_BADGE:.3f} x {ph/PPU_BADGE:.3f} units"
    f"   (builder claims 1.35 x 1.09)"
)
print(f"   -> on screen  {pw/PPC_DECK:.3f} x {ph/PPC_DECK:.3f} CELLS as the eye compares it")

# Digits: bright amber inside the plate.
sub = f[py0:py1 + 1, px0:px1 + 1]
subL = lum(sub)
dig = subL > 110
ys3, xs3 = np.nonzero(dig)
cap_px = ys3.max() - ys3.min() + 1
dw_px = xs3.max() - xs3.min() + 1
print(f"digit ink       w={dw_px}px cap={cap_px}px")
print(
    f"   -> cap world {cap_px/PPU_BADGE:.3f} units = {cap_px/PPU_BADGE/CELL:.3f} cells"
    f"   (builder claims 0.66 u / 0.245 cells)"
)
print(f"   -> cap on screen = {cap_px/PPC_DECK:.3f} cells; {cap_px/2:.0f}px on the 512 strip")

print("\n=== R1 GLYPH GEOMETRY (settled frame 19; peak frame 12) ===")
for i in (12, 14, 19):
    f1 = load("r1", i)
    d1 = np.abs(lum(f1) - Lb1)
    m = d1 > 40
    y1, x1 = np.nonzero(m)
    w, h = x1.max() - x1.min() + 1, y1.max() - y1.min() + 1
    print(
        f"f{i:02d} t={(i-12)/30:+.3f}  glyph {w}x{h}px | "
        f"world {w/PPU_BADGE:.2f} x {h/PPU_BADGE:.2f} u | "
        f"apparent {w/PPC_DECK:.2f} x {h/PPC_DECK:.2f} cells"
    )

# ---------------------------------------------------------------- keyline test
print("\n=== ACCEPTANCE TEST: 3px ring below luminance 60 along >=85% of contour ===")


def ring_test(mask, L, name, band=3):
    from scipy import ndimage

    inner = mask & ~ndimage.binary_erosion(mask, np.ones((3, 3)), 1)
    cy, cx = np.nonzero(inner)
    vals = []
    dil = ndimage.binary_dilation(mask, np.ones((3, 3)), band)
    ring = dil & ~mask
    for y, x in zip(cy, cx):
        # sample the ring band radially outward via nearest ring pixel
        y0, y1_, x0, x1_ = max(0, y - band - 1), y + band + 2, max(0, x - band - 1), x + band + 2
        patch = ring[y0:y1_, x0:x1_]
        pl = L[y0:y1_, x0:x1_]
        if patch.sum() == 0:
            continue
        vals.append(pl[patch].min())
    vals = np.array(vals)
    frac = (vals < 60).mean()
    print(
        f"{name}: contour n={len(vals)} | frac ring < L60 = {frac*100:.1f}% | "
        f"median ring L = {np.median(vals):.1f} | PASS" if frac >= 0.85
        else f"{name}: contour n={len(vals)} | frac ring < L60 = {frac*100:.1f}% | "
        f"median ring L = {np.median(vals):.1f} | FAIL"
    )
    return frac, np.median(vals)


try:
    from scipy import ndimage

    for i in (13, 14, 19, 24):
        f = load("r2", i)
        L = lum(f)
        d = L - Lb2
        plate = d < -50
        ys, xs = np.nonzero(plate)
        y0, y1, x0, x1 = ys.min() - 12, ys.max() + 12, xs.min() - 12, xs.max() + 12
        sub, subL = f[y0:y1, x0:x1], L[y0:y1, x0:x1]
        # glyph mask (amber ink)
        glyph = (subL > 105) & (sub[..., 0] > 130) & (sub[..., 2] < 190)
        glyph = ndimage.binary_opening(glyph, np.ones((3, 3)))
        print(f"\n-- frame {i} (t={(i-12)/30:+.3f}s)")
        ring_test(glyph, subL, "  GLYPH contour (literal test)")
        badge = ndimage.binary_fill_holes((subL < 120) | glyph)
        badge = ndimage.binary_opening(badge, np.ones((3, 3)))
        lab, n = ndimage.label(badge)
        if n:
            big = 1 + np.argmax(ndimage.sum(badge, lab, range(1, n + 1)))
            badge = lab == big
        ring_test(badge, subL, "  BADGE contour vs board (spirit of test)")
except ImportError:
    print("scipy missing")
