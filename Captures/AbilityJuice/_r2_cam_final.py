"""Parameter-ablation confidence test + debris-based hitstop measurement."""
import numpy as np
from PIL import Image
from scipy import ndimage, optimize
import os, json, sys

sys.path.insert(0, "/Users/cykai/Battle-Plan/Captures/AbilityJuice")
from _r2_cam_align import loader, build_mask, Aligner, CORNER_R, CY, CX

AJ = "/Users/cykai/Battle-Plan/Captures/AbilityJuice"
align = json.load(open(os.path.join(AJ, "_r2_cam_align.json")))


def refit(al, mov, x0, free):
    """Optimise only the parameters flagged free; the rest are pinned to 0."""
    x0 = np.asarray(x0, float).copy()
    for i in range(4):
        if not free[i]:
            x0[i] = 0.0
    idx = [i for i in range(4) if free[i]]
    if not idx:
        return al.cost(mov, x0), x0

    def f(v):
        p = x0.copy()
        for j, i in enumerate(idx):
            p[i] = v[j]
        return al.cost(mov, p)
    best = (f(x0[idx]), x0[idx].copy())
    for sc in (3.0, 0.8, 0.2, 0.05):
        step = np.array([sc, sc, sc * 0.12, sc * 0.002])[idx]
        simp = np.vstack([best[1]] + [best[1] + np.eye(len(idx))[k] * step[k]
                                      for k in range(len(idx))])
        r = optimize.minimize(f, best[1], method="Nelder-Mead",
                              options=dict(xatol=1e-5, fatol=1e-7, maxiter=1500,
                                           initial_simplex=simp))
        if r.fun < best[0]:
            best = (r.fun, r.x)
    p = x0.copy()
    for j, i in enumerate(idx):
        p[i] = best[1][j]
    return best[0], p


print("=" * 116)
print("ABLATION: pin one component to zero, re-optimise the rest. A large SAD penalty")
print("means that component is genuinely present in the pixels.")
print("=" * 116)
for rnd, fr in (("r2", [12, 14, 15, 16, 17]), ("r1", [12, 13, 15])):
    load = loader(rnd)
    ref = load(2)
    al = Aligner(ref, build_mask(ref))
    fit = {d["f"]: d for d in align[rnd]}
    print(f"\n[{rnd}]  {'f':>3} {'full SAD':>9} | {'no-roll SAD':>12} {'penalty':>8} | "
          f"{'no-zoom SAD':>12} {'penalty':>8} | {'no-trans SAD':>13} {'penalty':>8}")
    for i in fr:
        d = fit[i]
        x0 = [d["tx"], d["ty"], d["roll"], d["zoom"] / 100]
        mov = load(i)
        c_full, _ = refit(al, mov, x0, [1, 1, 1, 1])
        c_nr, _ = refit(al, mov, x0, [1, 1, 0, 1])
        c_nz, _ = refit(al, mov, x0, [1, 1, 1, 0])
        c_nt, _ = refit(al, mov, x0, [0, 0, 1, 1])
        print(f"      {i:>3} {c_full:9.4f} | {c_nr:12.4f} {c_nr/c_full:7.2f}x | "
              f"{c_nz:12.4f} {c_nz/c_full:7.2f}x | {c_nt:13.4f} {c_nt/c_full:7.2f}x")

# ------------------------------------------------------------------ HITSTOP
print()
print("=" * 116)
print("HITSTOP TEST — motion-compensate the camera away, then ask whether the DEBRIS moved.")
print("Written frames are 33.3ms of real time apart. If game time is frozen, debris is static.")
print("=" * 116)


def warp_to_ref(img, p):
    tx, ty, roll, zoom = p
    th = np.radians(roll); s = 1.0 + zoom
    Rm = s * np.array([[np.cos(th), -np.sin(th)], [np.sin(th), np.cos(th)]])
    off = np.array([CY, CX]) - Rm @ (np.array([CY, CX]))
    off = off + np.array([ty, tx])
    return ndimage.affine_transform(img, Rm, offset=off, order=1, mode="nearest")


load = loader("r2")
ref = load(2)
fit = {d["f"]: d for d in align["r2"]}
comp, dmask = {}, {}
for i in range(11, 24):
    d = fit.get(i)
    p = [d["tx"], d["ty"], d["roll"], d["zoom"] / 100] if d else [0, 0, 0, 0]
    w = warp_to_ref(load(i), p)
    comp[i] = w
    # debris = material darker than the clean board where the board used to be bright
    dm = (ref > 150) & (w < ref - 45)
    dm = ndimage.binary_opening(dm, np.ones((3, 3)))
    dmask[i] = dm

print(f"{'frame':>6} {'t(ms)':>7} | {'debris px':>10} {'centroid':>16} {'meanRadius':>11} | "
      f"{'Δcentroid':>10} {'ΔmeanR':>8} {'IoU vs prev':>12} {'silhouette Δpx':>15}")
prev = None
rows = []
for i in range(11, 24):
    dm = dmask[i]
    n = int(dm.sum())
    if n < 40:
        print(f"{i:>6} {(i-12)*33.333:>7.1f} | {n:>10} {'-':>16} {'-':>11} |")
        prev = None
        continue
    ys, xs = np.nonzero(dm)
    cen = np.array([xs.mean(), ys.mean()])
    mr = float(np.hypot(xs - cen[0], ys - cen[1]).mean())
    if prev is not None:
        dc = float(np.linalg.norm(cen - prev[0]))
        dr = mr - prev[1]
        inter = (dm & prev[2]).sum(); uni = (dm | prev[2]).sum()
        iou = inter / max(uni, 1)
        # how far the silhouette boundary moved: symmetric difference / perimeter
        sym = int((dm ^ prev[2]).sum())
        print(f"{i:>6} {(i-12)*33.333:>7.1f} | {n:>10} ({cen[0]:6.1f},{cen[1]:6.1f}) "
              f"{mr:11.2f} | {dc:10.3f} {dr:+8.3f} {iou:12.3f} {sym:15}")
        rows.append((i, dc, dr, iou, sym, n))
    else:
        print(f"{i:>6} {(i-12)*33.333:>7.1f} | {n:>10} ({cen[0]:6.1f},{cen[1]:6.1f}) "
              f"{mr:11.2f} |")
    prev = (cen, mr, dm)

print()
print("Per-pair change of the motion-compensated frame inside the debris region")
print(f"{'pair':>14} {'meanAbsΔ':>10} {'>20 px':>8}   {'read':>28}")
for i in range(12, 23):
    if i + 1 not in comp:
        break
    a, b = comp[i], comp[i + 1]
    reg = ndimage.binary_dilation(dmask[i] | dmask[i + 1], np.ones((9, 9)))
    if reg.sum() < 100:
        continue
    d = np.abs(b - a)[reg]
    print(f"  f{i:04d}->f{i+1:04d} {d.mean():10.3f} {int((d>20).sum()):8} ")
