import numpy as np
from PIL import Image
from scipy import ndimage
import os

ROOT = "/Users/cykai/Battle-Plan/Captures/AbilityJuice/shots/r2"
OUT = "/Users/cykai/Battle-Plan/Captures/AbilityJuice"

def load(shot, i):
    return np.asarray(Image.open(os.path.join(ROOT, shot, f"f{i:04d}.jpg")).convert("RGB"),
                      dtype=np.float64)

PXCELL = {"impactdip": 213.0, "impactcore": 232.0}
CENTRE = {"impactdip": (507.0, 483.0), "impactcore": (532.0, 478.0)}

def world_crop(shot, i, half_cells=2.0, size=460):
    """Crop the same world-space window from either shot, at the same output size,
    so camera distance 11 vs 12 stops being a confound."""
    img = load(shot, i)
    cy, cx = CENTRE[shot]
    hp = half_cells * PXCELL[shot]
    ys = np.linspace(cy - hp, cy + hp, size)
    xs = np.linspace(cx - hp, cx + hp, size)
    gy, gx = np.meshgrid(ys, xs, indexing="ij")
    out = np.stack([ndimage.map_coordinates(img[..., c], [gy, gx], order=1, mode="nearest")
                    for c in range(3)], axis=-1)
    return np.clip(out, 0, 255)

def label(a, text, h=26):
    from PIL import ImageDraw
    im = Image.fromarray(a.astype(np.uint8))
    strip = Image.new("RGB", (im.width, h), (12, 12, 14))
    d = ImageDraw.Draw(strip)
    d.text((6, 7), text, fill=(235, 235, 235))
    canvas = Image.new("RGB", (im.width, im.height + h))
    canvas.paste(strip, (0, 0)); canvas.paste(im, (0, h))
    return np.asarray(canvas, dtype=np.float64)

def hstack(items, gap=8, bg=18):
    h = max(a.shape[0] for a in items)
    parts = []
    for k, a in enumerate(items):
        if a.shape[0] < h:
            a = np.vstack([a, np.full((h - a.shape[0], a.shape[1], 3), bg, float)])
        parts.append(a)
        if k != len(items) - 1:
            parts.append(np.full((h, gap, 3), bg, float))
    return np.hstack(parts)

def vstack(items, gap=8, bg=18):
    w = max(a.shape[1] for a in items)
    parts = []
    for k, a in enumerate(items):
        if a.shape[1] < w:
            a = np.hstack([a, np.full((a.shape[0], w - a.shape[1], 3), bg, float)])
        parts.append(a)
        if k != len(items) - 1:
            parts.append(np.full((gap, w, 3), bg, float))
    return np.vstack(parts)

def save(a, name):
    Image.fromarray(np.clip(a, 0, 255).astype(np.uint8)).save(os.path.join(OUT, name), quality=95)
    print("wrote", name)

# ---- 1. A/B at matched world scale, the three frames the strip could cut -------------
rows = []
for i in (12, 13, 14, 15):
    t = (i - 12) * 0.03333
    a = label(world_crop("impactdip", i), f"WITH DIP   f{i:04d}  t={t:+.2f}s")
    b = label(world_crop("impactcore", i), f"NO DIP     f{i:04d}  t={t:+.2f}s")
    rows.append(hstack([a, b]))
save(vstack(rows), "_r2_dip_ab.jpg")

# ---- 2. squint: same A/B at thumbnail size --------------------------------------------
sq = []
for i in (12, 13, 14):
    a = world_crop("impactdip", i, size=110)
    b = world_crop("impactcore", i, size=110)
    sq.append(hstack([label(a, "DIP"), label(b, "NONE")]))
save(hstack(sq, gap=14), "_r2_dip_squint.jpg")

# ---- 3. greyscale A/B: strips value contrast out of hue -------------------------------
rows = []
for i in (13, 14):
    a = world_crop("impactdip", i); b = world_crop("impactcore", i)
    ga = np.repeat((0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2])[..., None], 3, axis=2)
    gb = np.repeat((0.2126*b[...,0]+0.7152*b[...,1]+0.0722*b[...,2])[..., None], 3, axis=2)
    rows.append(hstack([label(ga, f"GREY  dip f{i:04d}"), label(gb, f"GREY  none f{i:04d}")]))
save(vstack(rows), "_r2_dip_grey.jpg")

# ---- 4. the dip field alone: ratio of frame to pre-roll, remapped -----------------
med = np.median(np.stack([load("impactdip", i) for i in range(12)]), axis=0)
medc = np.median(np.stack([load("impactcore", i) for i in range(12)]), axis=0)
tiles = []
for i in (12, 13, 14, 15, 16, 17):
    r = load("impactdip", i) / np.maximum(med, 1.0)
    g = r.mean(axis=2)
    # 1.0 -> black, 0.4 -> white, so the dip's own footprint is visible as a shape
    v = np.clip((1.0 - g) / 0.6, 0, 1) * 255
    v = np.repeat(v[..., None], 3, axis=2)
    small = np.asarray(Image.fromarray(v.astype(np.uint8)).resize((300, 300)), dtype=np.float64)
    tiles.append(label(small, f"darkening f{i:04d} t={(i-12)*0.03333:+.2f}"))
save(hstack(tiles), "_r2_dip_field.jpg")

# ---- 5. does the unit/cover stay bright while the floor drops? ------------------------
print()
print("=" * 78)
print("K.  WHAT ACTUALLY DARKENS  (dip shot): floor vs unit vs cover-block tops")
print("=" * 78)
L0 = 0.2126*med[...,0]+0.7152*med[...,1]+0.0722*med[...,2]
mx, mn = med.max(axis=2), med.min(axis=2)
sat = (mx - mn)/np.maximum(mx, 1e-6)
floor = ndimage.binary_erosion((L0 > 150) & (sat < 0.16), np.ones((5,5)))
dark_cover = ndimage.binary_erosion((L0 < 90) & (sat < 0.35), np.ones((5,5)))   # block sides/tops
unit = ndimage.binary_erosion((sat > 0.45) & (med[...,0] > med[...,2] + 30), np.ones((3,3)))
H = W = 1024
yy, xx = np.mgrid[0:H, 0:W]
rr = np.hypot(yy - CENTRE["impactdip"][0], xx - CENTRE["impactdip"][1]) / PXCELL["impactdip"]
near = rr < 2.2
print(f"  masks: floor={floor.sum()} darkcover={dark_cover.sum()} unit(red)={unit.sum()} "
      f"| within 2.2 cells: floor={int((floor&near).sum())} cover={int((dark_cover&near).sum())} "
      f"unit={int((unit&near).sum())}")
print(f"  {'frame':>6} {'t(s)':>6} | {'floor ratio':>12} {'coverTop ratio':>15} {'unit ratio':>11}")
for i in range(11, 19):
    F = load("impactdip", i)
    L = 0.2126*F[...,0]+0.7152*F[...,1]+0.0722*F[...,2]
    def rat(m):
        m = m & near
        return float((L[m]/np.maximum(L0[m],1)).mean()) if m.sum() > 200 else float("nan")
    print(f"  f{i:04d} {(i-12)*0.03333:+6.2f} | {rat(floor):12.3f} {rat(dark_cover):15.3f} "
          f"{rat(unit):11.3f}")

# ---- 6. hard rectangular boundary check (quad edge visible?) --------------------------
print()
print("=" * 78)
print("L.  QUAD-EDGE / BANDING CHECK: largest single-pixel step in the darkening field")
print("=" * 78)
for i in (12, 13, 14, 15, 16):
    r = (load("impactdip", i) / np.maximum(med, 1.0)).mean(axis=2)
    rs = ndimage.median_filter(r, 3)
    gy, gx = np.gradient(rs)
    g = np.hypot(gy, gx)
    inner = np.zeros_like(g, bool); inner[40:-40, 40:-40] = True
    m = floor & inner
    print(f"  f{i:04d}  max|d(ratio)/dpx| on floor = {g[m].max():.4f}  "
          f"p99.9={np.percentile(g[m],99.9):.4f}  p99={np.percentile(g[m],99):.4f}")
