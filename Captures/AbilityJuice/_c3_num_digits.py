"""Round-3 critic: precise digit cap height, overshoot, and Clash Mini numeral scale."""

import numpy as np
from PIL import Image
from scipy import ndimage

FRAME, FOV, CELL, CAM_D, PITCH, LIFT = 1024, 60.0, 2.7, 9.0, np.radians(73.0), 2.2
ppu = lambda d: (FRAME / 2) / (d * np.tan(np.radians(FOV / 2)))
PPU_B = ppu(CAM_D - LIFT * np.sin(PITCH))
PPC_DECK = ppu(CAM_D) * CELL


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def load(run, i):
    return np.asarray(
        Image.open(f"shots/{run}/numbers/f{i:04d}.jpg").convert("RGB")
    ).astype(np.float32)


Lb2 = lum(load("r2", 0))

print("=== DIGIT CAP HEIGHT (largest connected bright blob = a numeral) ===")
for i in (12, 14, 17, 19, 24, 27):
    f = load("r2", i)
    L = lum(f)
    dark = ndimage.binary_fill_holes((L - Lb2) < -50)
    lab, n = ndimage.label(ndimage.binary_closing(dark, np.ones((5, 5))))
    whole = lab == 1 + np.argmax(ndimage.sum(dark, lab, range(1, n + 1)))
    ys, xs = np.nonzero(whole)
    # keyline+rim together are ~14% of cap; erode generously then take blobs
    core = ndimage.binary_erosion(whole, np.ones((3, 3)), iterations=16)
    ink = ndimage.binary_opening(core & (L > 110), np.ones((3, 3)))
    lab2, n2 = ndimage.label(ink)
    if n2 == 0:
        print(f"f{i:02d}: no ink")
        continue
    sizes = ndimage.sum(ink, lab2, range(1, n2 + 1))
    blobs = [k + 1 for k in range(n2) if sizes[k] > 0.25 * sizes.max()]
    hs, ws, allx = [], [], []
    for k in blobs:
        by, bx = np.nonzero(lab2 == k)
        hs.append(by.max() - by.min() + 1)
        ws.append(bx.max() - bx.min() + 1)
        allx += [bx.min(), bx.max()]
    cap = max(hs)
    span = max(allx) - min(allx) + 1
    # keyline-outer plate box
    rows = [(y, np.nonzero(whole[y])[0]) for y in range(whole.shape[0]) if whole[y].any()]
    wide = [(y, xr) for y, xr in rows if xr.max() - xr.min() > 60]
    pw = max(xr.max() for _, xr in wide) - min(xr.min() for _, xr in wide) + 1
    ph = wide[-1][0] - wide[0][0] + 1
    print(
        f"f{i:02d} t={(i-12)/30:+.3f}  {len(blobs)} numerals, cap={cap}px width/glyph={np.mean(ws):.0f}px "
        f"span={span}px | cap world {cap/PPU_B:.3f}u = {cap/PPU_B/CELL:.3f} cells | "
        f"apparent {cap/PPC_DECK:.3f} cells | strip {cap/2:.0f}px | plate {pw}x{ph}"
    )
    if i == 19:
        print(
            f"      glyph aspect (w/h) = {np.mean(ws)/cap:.3f}  "
            f"(Arial Bold '0' is ~0.72; a condensed display numeral ~0.55-0.62)"
        )

print("\n=== OVERSHOOT: outer keyline extent, threshold-free (uses colour distance) ===")
base = load("r2", 0)
for i in range(12, 29):
    f = load("r2", i)
    diff = np.abs(f - base).sum(2)
    m = ndimage.binary_fill_holes(diff > 90)
    m = ndimage.binary_closing(m, np.ones((5, 5)))
    lab, n = ndimage.label(m)
    if n == 0:
        continue
    big = lab == 1 + np.argmax(ndimage.sum(m, lab, range(1, n + 1)))
    rows = [(y, np.nonzero(big[y])[0]) for y in range(big.shape[0]) if big[y].any()]
    wide = [(y, xr) for y, xr in rows if xr.max() - xr.min() > 50]
    if not wide:
        continue
    w = max(xr.max() for _, xr in wide) - min(xr.min() for _, xr in wide) + 1
    h = wide[-1][0] - wide[0][0] + 1
    print(f"f{i:02d} t={(i-12)/30:+.3f}  plate {w:3d}x{h:3d}px")

print("\n=== CLASH MINI NUMERALS: how big are Supercell's damage numbers? ===")
for name in ("cm-everyability-23", "cm-everyability-22", "cm-everyability-20"):
    im = np.asarray(Image.open(f"strips/reference/{name}.jpg").convert("RGB")).astype(np.float32)
    panel = im[:, 690:1024]  # third panel
    r, g, b = panel[..., 0], panel[..., 1], panel[..., 2]
    yellow = (r > 200) & (g > 150) & (b < 110) & (r - b > 110)
    yellow = ndimage.binary_opening(yellow, np.ones((2, 2)))
    lab, n = ndimage.label(yellow)
    sizes = ndimage.sum(yellow, lab, range(1, n + 1))
    hs = []
    for k in range(n):
        if not (12 < sizes[k] < 400):
            continue
        by, bx = np.nonzero(lab == k + 1)
        h, w = by.max() - by.min() + 1, bx.max() - bx.min() + 1
        if 4 < h < 30 and 3 < w < 26 and h >= w * 0.8:
            hs.append((h, w))
    hs.sort(reverse=True)
    print(f"{name}: candidate numeral blobs (h,w) = {hs[:6]}  [panel is 334px wide]")
