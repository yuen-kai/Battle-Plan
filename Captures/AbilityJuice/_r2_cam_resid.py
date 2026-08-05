"""Is the r2 similarity fit trustworthy? Per-patch detail + affine/homography residual."""
import numpy as np
from PIL import Image
import os, sys

sys.path.insert(0, "/Users/cykai/Battle-Plan/Captures/AbilityJuice")
from _r2_cam_fit import phase_corr, ring, P, CY, CX, loader, H, W, CORNER_R

load2 = loader("r2")
load1 = loader("r1")


def patch_flow(ref, mov):
    rows = []
    for (y, x) in ring:
        dy, dx, conf = phase_corr(ref[y:y + P, x:x + P], mov[y:y + P, x:x + P])
        rows.append((y + P / 2 - CY, x + P / 2 - CX, dx, dy, conf))
    return np.array(rows)


def fit_model(rows, model, keep=None):
    """model: 'sim' (4dof), 'affine' (6dof). Returns params, residual vector."""
    if keep is None:
        keep = np.ones(len(rows), bool)
    cy, cx, dx, dy = rows[keep, 0], rows[keep, 1], rows[keep, 2], rows[keep, 3]
    n = keep.sum()
    if model == "sim":
        A = np.zeros((2 * n, 4)); rhs = np.zeros(2 * n)
        A[:n, 0] = 1; A[:n, 2] = cx; A[:n, 3] = -cy; rhs[:n] = dx
        A[n:, 1] = 1; A[n:, 2] = cy; A[n:, 3] = cx;  rhs[n:] = dy
        sol, *_ = np.linalg.lstsq(A, rhs, rcond=None)
        pdx = sol[0] + sol[2] * rows[:, 1] - sol[3] * rows[:, 0]
        pdy = sol[1] + sol[2] * rows[:, 0] + sol[3] * rows[:, 1]
    else:  # full affine: dx = a0 + a1*x + a2*y ; dy = b0 + b1*x + b2*y
        A = np.zeros((2 * n, 6)); rhs = np.zeros(2 * n)
        A[:n, 0] = 1; A[:n, 1] = cx; A[:n, 2] = cy; rhs[:n] = dx
        A[n:, 3] = 1; A[n:, 4] = cx; A[n:, 5] = cy; rhs[n:] = dy
        sol, *_ = np.linalg.lstsq(A, rhs, rcond=None)
        pdx = sol[0] + sol[1] * rows[:, 1] + sol[2] * rows[:, 0]
        pdy = sol[3] + sol[4] * rows[:, 1] + sol[5] * rows[:, 0]
    res = np.hypot(rows[:, 2] - pdx, rows[:, 3] - pdy)
    return sol, res


print("=" * 100)
print("PER-PATCH FLOW, ROUND 2 — is the 4-DOF similarity model adequate?")
print("=" * 100)
ref2 = load2(2)
for i in [12, 13, 14, 15, 16, 17]:
    rows = patch_flow(ref2, load2(i))
    ssol, sres = fit_model(rows, "sim")
    asol, ares = fit_model(rows, "affine")
    tx, ty, a, b = ssol
    roll = np.degrees(np.arctan2(b, 1 + a))
    print(f"\nf{i:04d} t={(i-12)*33.333:+6.1f}ms   sim: T=({tx:+7.2f},{ty:+7.2f}) "
          f"|T|={np.hypot(tx,ty):6.2f}  roll={roll:+.3f}deg  zoom={a*100:+.3f}%")
    for r, se, ae in zip(rows, sres, ares):
        loc = f"({r[1]:+5.0f},{r[0]:+5.0f})"
        print(f"    patch {loc:>14} obs=({r[2]:+7.2f},{r[3]:+7.2f}) conf={r[4]:6.1f}  "
              f"simResid={se:6.2f}  affResid={ae:6.2f}")
    print(f"    medResid sim={np.median(sres):.2f}  affine={np.median(ares):.2f}   "
          f"maxResid sim={sres.max():.2f} affine={ares.max():.2f}")

print()
print("=" * 100)
print("Same diagnostic for ROUND 1 peak (baseline for how clean a good fit looked)")
print("=" * 100)
ref1 = load1(2)
for i in [12, 13]:
    rows = patch_flow(ref1, load1(i))
    ssol, sres = fit_model(rows, "sim")
    asol, ares = fit_model(rows, "affine")
    tx, ty, a, b = ssol
    roll = np.degrees(np.arctan2(b, 1 + a))
    print(f"\nf{i:04d}  sim: T=({tx:+7.2f},{ty:+7.2f}) |T|={np.hypot(tx,ty):6.2f} "
          f"roll={roll:+.3f}deg zoom={a*100:+.3f}%")
    for r, se, ae in zip(rows, sres, ares):
        print(f"    patch ({r[1]:+5.0f},{r[0]:+5.0f}) obs=({r[2]:+7.2f},{r[3]:+7.2f}) "
              f"conf={r[4]:6.1f}  simResid={se:6.2f} affResid={ae:6.2f}")
    print(f"    medResid sim={np.median(sres):.2f}  affine={np.median(ares):.2f}")
