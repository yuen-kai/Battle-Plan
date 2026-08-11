"""Round-3 critic: digit size, badge/board contrast, occlusion, motion."""

import numpy as np
from PIL import Image
from scipy import ndimage

FRAME, FOV, CELL, CAM_D, PITCH, LIFT = 1024, 60.0, 2.7, 9.0, np.radians(73.0), 2.2
ppu = lambda d: (FRAME / 2) / (d * np.tan(np.radians(FOV / 2)))
D_BADGE = CAM_D - LIFT * np.sin(PITCH)
PPU_B, PPC_DECK = ppu(D_BADGE), ppu(CAM_D) * CELL


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def load(run, i):
    return np.asarray(
        Image.open(f"shots/{run}/numbers/f{i:04d}.jpg").convert("RGB")
    ).astype(np.float32)


Lb2 = lum(load("r2", 0))


def badge_parts(i):
    f = load("r2", i)
    L = lum(f)
    dark = (L - Lb2) < -50
    filled = ndimage.binary_fill_holes(dark)
    filled = ndimage.binary_closing(filled, np.ones((5, 5)))
    lab, n = ndimage.label(filled)
    big = 1 + np.argmax(ndimage.sum(filled, lab, range(1, n + 1)))
    whole = lab == big
    rows = [(y, np.nonzero(whole[y])[0]) for y in range(whole.shape[0]) if whole[y].any()]
    wide = [(y, xr) for y, xr in rows if xr.max() - xr.min() > 60]
    py0, py1 = wide[0][0], wide[-1][0]
    px0 = min(xr.min() for _, xr in wide)
    px1 = max(xr.max() for _, xr in wide)
    return f, L, whole, (px0, px1, py0, py1)


print("=== DIGIT INK (isolated from the amber rim) ===")
for i in (14, 19, 24):
    f, L, whole, (px0, px1, py0, py1) = badge_parts(i)
    # interior only: erode the plate hard so the rim is excluded
    interior = ndimage.binary_erosion(whole, np.ones((3, 3)), iterations=9)
    ink = interior & (L > 105)
    ink = ndimage.binary_opening(ink, np.ones((3, 3)))
    ys, xs = np.nonzero(ink)
    cap, dw = ys.max() - ys.min() + 1, xs.max() - xs.min() + 1
    pw, ph = px1 - px0 + 1, py1 - py0 + 1
    print(
        f"f{i:02d} t={(i-12)/30:+.3f}  digits {dw}x{cap}px  "
        f"cap={cap/PPU_B:.3f}u = {cap/PPU_B/CELL:.3f} cells (world) | "
        f"apparent {cap/PPC_DECK:.3f} cells | strip px {cap/2:.0f}"
    )
    print(
        f"          plate {pw}x{ph}px = {pw/PPU_B:.2f}x{ph/PPU_B:.2f}u | "
        f"apparent {pw/PPC_DECK:.2f}x{ph/PPC_DECK:.2f} cells | "
        f"digit-to-plate height {cap/ph*100:.0f}%"
    )

print("\n=== BADGE vs BOARD: hard-edge contrast step across the outer contour ===")
for i in (13, 14, 19, 24, 27):
    f, L, whole, _ = badge_parts(i)
    outside = ndimage.binary_dilation(whole, np.ones((3, 3)), 3) & ~whole
    inside = whole & ~ndimage.binary_erosion(whole, np.ones((3, 3)), 3)
    li, lo = np.median(L[inside]), np.median(L[outside])
    # edge sharpness: 90->10 transition width along a horizontal cut at mid height
    ys, xs = np.nonzero(whole)
    ymid = int(np.median(ys))
    row = L[ymid]
    xr = np.nonzero(whole[ymid])[0]
    prof = row[xr.min() - 10 : xr.min() + 10]
    hi, lo2 = prof.max(), prof.min()
    steps = np.abs(np.diff(prof))
    print(
        f"f{i:02d}  inside L={li:6.1f}  outside L={lo:6.1f}  delta={li-lo:+7.1f} | "
        f"max edge step {steps.max():.0f} L/px  (findings: CM dust = 49-91 L/px)"
    )

print("\n=== OCCLUSION: unit body + health bar, before vs during ===")
base = load("r2", 0)
r, g, b = base[..., 0], base[..., 1], base[..., 2]
pink0 = (r > 170) & (g < 130) & (b > 90) & (b < 170) & (r - g > 60)
green0 = (g > 120) & (g - r > 40) & (g - b > 40)
print(f"clean frame: pink unit px={pink0.sum()}  health bar px={green0.sum()}")
for i in (13, 14, 19, 24):
    f = load("r2", i)
    r, g, b = f[..., 0], f[..., 1], f[..., 2]
    pink = (r > 170) & (g < 130) & (b > 90) & (b < 170) & (r - g > 60)
    green = (g > 120) & (g - r > 40) & (g - b > 40)
    print(
        f"f{i:02d}  pink {pink.sum():5d} ({pink.sum()/pink0.sum()*100:5.1f}% kept)  "
        f"health {green.sum():5d} ({green.sum()/green0.sum()*100:5.1f}% kept)"
    )
ys, xs = np.nonzero(pink0)
print(f"unit pink bbox y[{ys.min()},{ys.max()}] x[{xs.min()},{xs.max()}]")
ys, xs = np.nonzero(green0)
print(f"health bar bbox y[{ys.min()},{ys.max()}] x[{xs.min()},{xs.max()}]")
_, _, whole, bb = badge_parts(19)
ys, xs = np.nonzero(whole)
print(f"badge bbox      y[{ys.min()},{ys.max()}] x[{xs.min()},{xs.max()}]")

print("\n=== MOTION: scale + colour per frame ===")
prev_w = None
for i in range(12, 30):
    try:
        f, L, whole, (px0, px1, py0, py1) = badge_parts(i)
    except Exception:
        continue
    pw, ph = px1 - px0 + 1, py1 - py0 + 1
    interior = ndimage.binary_erosion(whole, np.ones((3, 3)), iterations=9)
    ink = interior & (L > 105)
    mean_rgb = f[ink].mean(0) if ink.sum() > 20 else np.zeros(3)
    inkL = L[ink].mean() if ink.sum() > 20 else 0
    mx = mean_rgb.max()
    mn = mean_rgb.min()
    sat = (mx - mn) / mx if mx > 0 else 0
    dw = f"{pw/175.0:.3f}"
    print(
        f"f{i:02d} t={(i-12)/30:+.3f}  plate {pw:3d}x{ph:3d}px  scale~{dw}  "
        f"digit RGB({mean_rgb[0]:5.1f},{mean_rgb[1]:5.1f},{mean_rgb[2]:5.1f}) "
        f"L={inkL:5.1f} sat={sat:.2f}"
    )
