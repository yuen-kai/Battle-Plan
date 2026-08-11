"""Hitstop measured on the debris: how much game time passes between written frames?"""
import numpy as np
from PIL import Image
from scipy import ndimage
import os, json, sys

sys.path.insert(0, "/Users/cykai/Battle-Plan/Captures/AbilityJuice")
from _r2_cam_align import loader, CY, CX

AJ = "/Users/cykai/Battle-Plan/Captures/AbilityJuice"
align = json.load(open(os.path.join(AJ, "_r2_cam_align.json")))
load = loader("r2")
D = os.path.join(AJ, "shots/r2/camera")


def rgb(i):
    return np.asarray(Image.open(os.path.join(D, f"f{i:04d}.jpg")).convert("RGB"),
                      dtype=np.float64)


def warp_to_ref(img, p):
    tx, ty, roll, zoom = p
    th = np.radians(roll); s = 1.0 + zoom
    Rm = s * np.array([[np.cos(th), -np.sin(th)], [np.sin(th), np.cos(th)]])
    off = np.array([CY, CX]) - Rm @ np.array([CY, CX]) + np.array([ty, tx])
    if img.ndim == 3:
        return np.dstack([ndimage.affine_transform(img[..., c], Rm, offset=off,
                                                   order=1, mode="nearest")
                          for c in range(3)])
    return ndimage.affine_transform(img, Rm, offset=off, order=1, mode="nearest")


ref = load(2)
refc = rgb(2)
fit = {d["f"]: d for d in align["r2"]}
# board geometry that is legitimately dark in the clean plate (props, shadows)
static_dark = ndimage.binary_dilation(
    ndimage.binary_closing(ref < 140, np.ones((5, 5))), np.ones((9, 9)))

comp, chunk = {}, {}
for i in range(11, 30):
    d = fit.get(i)
    p = [d["tx"], d["ty"], d["roll"], d["zoom"] / 100] if d else [0, 0, 0, 0]
    w = warp_to_ref(rgb(i), p)
    comp[i] = w
    lum = w @ [0.299, 0.587, 0.114]
    # debris = opaque material sitting on what is normally bright clean board
    m = (lum < 140) & (~static_dark)
    m = ndimage.binary_opening(m, np.ones((5, 5)))
    m = ndimage.binary_closing(m, np.ones((5, 5)))
    lab, n = ndimage.label(m)
    if n:
        sizes = np.bincount(lab.ravel()); sizes[0] = 0
        keep = np.isin(lab, np.nonzero(sizes >= 60)[0])
        m = keep
    chunk[i] = m

print("=" * 118)
print("DEBRIS SILHOUETTE, camera motion removed  (opaque material on clean board, blobs >=60px)")
print("=" * 118)
print(f"{'f':>3} {'t(ms)':>7} | {'area px':>8} {'blobs':>6} {'centroid':>16} {'rms radius':>11} "
      f"| {'Δcentroid':>10} {'Δrms':>7} {'IoU':>6} {'areaΔ%':>7}")
prev = None
seq = []
for i in range(11, 30):
    m = chunk[i]
    n = int(m.sum())
    lab, nb = ndimage.label(m)
    if n < 60:
        print(f"{i:>3} {(i-12)*33.333:>7.1f} | {n:>8} {nb:>6}")
        prev = None
        continue
    ys, xs = np.nonzero(m)
    cen = np.array([xs.mean(), ys.mean()])
    rms = float(np.sqrt(((xs - cen[0]) ** 2 + (ys - cen[1]) ** 2).mean()))
    if prev is not None:
        dc = float(np.linalg.norm(cen - prev[0]))
        dr = rms - prev[1]
        iou = (m & prev[2]).sum() / max((m | prev[2]).sum(), 1)
        da = 100 * (n - prev[3]) / max(prev[3], 1)
        print(f"{i:>3} {(i-12)*33.333:>7.1f} | {n:>8} {nb:>6} ({cen[0]:6.1f},{cen[1]:6.1f}) "
              f"{rms:11.2f} | {dc:10.3f} {dr:+7.3f} {iou:6.3f} {da:+7.1f}")
        seq.append((i, dc, dr, iou, n))
    else:
        print(f"{i:>3} {(i-12)*33.333:>7.1f} | {n:>8} {nb:>6} ({cen[0]:6.1f},{cen[1]:6.1f}) "
              f"{rms:11.2f} |")
    prev = (cen, rms, m, n)

print()
print("=" * 118)
print("PER-CHUNK TRACKING — displacement of matched debris blobs between written frames")
print("=" * 118)


def blobs(m, minsz=120):
    lab, n = ndimage.label(m)
    out = []
    for k in range(1, n + 1):
        ys, xs = np.nonzero(lab == k)
        if len(ys) < minsz:
            continue
        out.append((np.array([xs.mean(), ys.mean()]), len(ys)))
    return out


print(f"{'pair':>16} {'matched':>8} {'median |Δ| px':>14} {'mean |Δ| px':>12} {'max':>7}  "
      f"{'implied game ms':>16}")
rates = {}
for i in range(12, 26):
    if i + 1 not in chunk:
        break
    A, B = blobs(chunk[i]), blobs(chunk[i + 1])
    if not A or not B:
        continue
    ds = []
    for ca, sa in A:
        best = min(B, key=lambda t: np.linalg.norm(t[0] - ca))
        if abs(np.log(best[1] / sa)) < 1.0:
            ds.append(float(np.linalg.norm(best[0] - ca)))
    if not ds:
        continue
    ds = np.array(ds)
    rates[i] = float(np.median(ds))
    print(f"  f{i:04d}->f{i+1:04d} {len(ds):>8} {np.median(ds):14.3f} {ds.mean():12.3f} "
          f"{ds.max():7.2f}")

# normal-rate baseline from the frames right after release, decay-corrected
base = [rates[k] for k in (17, 18, 19, 20) if k in rates]
if base:
    nb = float(np.median(base))
    print(f"\nbaseline debris advance in a NON-frozen 33.3ms frame pair "
          f"(f17..f21) = {nb:.3f} px")
    for k in (12, 13, 14, 15):
        if k in rates:
            gm = 33.333 * rates[k] / nb
            print(f"  f{k:04d}->f{k+1:04d}: {rates[k]:7.3f} px  "
                  f"=> {100*rates[k]/nb:6.1f}% of a normal frame  "
                  f"=> ~{gm:6.2f} ms of game time elapsed")

print()
print("=" * 118)
print("PIXEL-LEVEL FREEZE TEST inside the debris bounding box (camera removed)")
print("=" * 118)
print(f"{'pair':>16} {'meanAbsΔ':>10} {'px>25':>8} {'frac of region':>15}")
for i in range(12, 26):
    if i + 1 not in chunk:
        break
    reg = ndimage.binary_dilation(chunk[i] | chunk[i + 1], np.ones((15, 15)))
    if reg.sum() < 200:
        continue
    a = comp[i] @ [0.299, 0.587, 0.114]
    b = comp[i + 1] @ [0.299, 0.587, 0.114]
    d = np.abs(b - a)[reg]
    print(f"  f{i:04d}->f{i+1:04d} {d.mean():10.3f} {int((d>25).sum()):8} "
          f"{(d>25).mean():15.3f}")
