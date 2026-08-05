"""Dense-grid flow with a floor-only mask.

Hypothesis: the big residuals in the 8-patch fit come from PARALLAX on the tall box
props (real 3-D camera motion), not from a bad measurement. Test by splitting patches
into floor-only and box-containing sets and comparing residuals.
"""
import numpy as np
from PIL import Image
from scipy import ndimage
import os, sys, json

sys.path.insert(0, "/Users/cykai/Battle-Plan/Captures/AbilityJuice")
from _r2_cam_fit import phase_corr

AJ = "/Users/cykai/Battle-Plan/Captures/AbilityJuice"
CY = CX = 512.0
CORNER_R = np.hypot(512.0, 512.0)


def loader(rnd):
    D = os.path.join(AJ, "shots", rnd, "camera")
    return lambda i: np.asarray(Image.open(os.path.join(D, f"f{i:04d}.jpg")).convert("L"),
                                dtype=np.float64)


def build_masks(ref, colref):
    """floor = bright board surface; box = dark tall geometry."""
    dark = ref < 110
    dark = ndimage.binary_closing(dark, np.ones((5, 5)))
    box = ndimage.binary_dilation(dark, np.ones((11, 11)))
    # unit + debris live near frame centre; exclude a generous disc
    yy, xx = np.mgrid[0:1024, 0:1024]
    central = (xx - 480) ** 2 + (yy - 430) ** 2 < 300 ** 2
    return (~box) & (~central), box


def grid_flow(ref, mov, mask, ps=96, step=48, cov_min=0.97):
    rows = []
    for y in range(0, 1024 - ps + 1, step):
        for x in range(0, 1024 - ps + 1, step):
            sub = mask[y:y + ps, x:x + ps]
            cov = sub.mean()
            if cov < cov_min:
                continue
            r = ref[y:y + ps, x:x + ps]
            if r.std() < 4:            # featureless, phase corr is meaningless
                continue
            dy, dx, conf = phase_corr(r, mov[y:y + ps, x:x + ps])
            if abs(dy) > 60 or abs(dx) > 60:
                continue
            rows.append((y + ps / 2 - CY, x + ps / 2 - CX, dx, dy, conf))
    return np.array(rows)


def fit_sim(rows, iters=4):
    keep = np.ones(len(rows), bool)
    sol = None
    for _ in range(iters):
        cy, cx, dx, dy = rows[keep, 0], rows[keep, 1], rows[keep, 2], rows[keep, 3]
        n = int(keep.sum())
        A = np.zeros((2 * n, 4)); rhs = np.zeros(2 * n)
        A[:n, 0] = 1; A[:n, 2] = cx; A[:n, 3] = -cy; rhs[:n] = dx
        A[n:, 1] = 1; A[n:, 2] = cy; A[n:, 3] = cx;  rhs[n:] = dy
        sol, *_ = np.linalg.lstsq(A, rhs, rcond=None)
        pdx = sol[0] + sol[2] * rows[:, 1] - sol[3] * rows[:, 0]
        pdy = sol[1] + sol[2] * rows[:, 0] + sol[3] * rows[:, 1]
        err = np.hypot(rows[:, 2] - pdx, rows[:, 3] - pdy)
        med = np.median(err[keep])
        nk = err < max(1.0, 2.5 * med)
        if nk.sum() < 8 or (nk == keep).all():
            break
        keep = nk
    tx, ty, a, b = sol
    return dict(tx=tx, ty=ty, mag=float(np.hypot(tx, ty)),
                roll=float(np.degrees(np.arctan2(b, 1 + a))), zoom=float(a * 100),
                n=int(keep.sum()), ntot=len(rows),
                med=float(np.median(err[keep])), mx=float(err[keep].max()))


for rnd, rng in (("r2", range(6, 24)), ("r1", range(6, 22))):
    load = loader(rnd)
    ref = load(2)
    floor_mask, box_mask = build_masks(ref, None)
    print(f"\n{'='*118}")
    print(f"{rnd.upper()} — dense 128px grid, FLOOR-ONLY patches "
          f"(floor mask covers {100*floor_mask.mean():.1f}% of frame)")
    print('=' * 118)
    print(f"{'f':>3} {'t(ms)':>7} | {'tx':>7} {'ty':>7} {'|T|px':>7} {'%fr':>6} | "
          f"{'roll°':>7} {'zoom%':>7} | {'cornerRoll':>10} {'zoomPx':>7} {'rotShare':>8} | "
          f"{'n/tot':>7} {'med':>5} {'max':>5}")
    out = []
    for i in rng:
        rows = grid_flow(ref, load(i), floor_mask)
        if len(rows) < 10:
            print(f"{i:>3}  too few patches ({len(rows)})")
            continue
        r = fit_sim(rows)
        cr = abs(np.radians(r['roll'])) * CORNER_R
        zp = abs(r['zoom'] / 100) * CORNER_R
        share = cr / (cr + r['mag']) if (cr + r['mag']) > 1e-9 else 0
        r.update(f=i, t=(i - 12) * 33.333, corner_roll=cr, zoom_px=zp, share=share)
        out.append(r)
        print(f"{i:>3} {(i-12)*33.333:>7.1f} | {r['tx']:7.2f} {r['ty']:7.2f} {r['mag']:7.2f} "
              f"{100*r['mag']/1024:5.2f}% | {r['roll']:7.3f} {r['zoom']:7.3f} | "
              f"{cr:10.2f} {zp:7.2f} {100*share:7.1f}% | {r['n']:>3}/{r['ntot']:<3} "
              f"{r['med']:5.2f} {r['mx']:5.2f}")
    with open(os.path.join(AJ, f"_r2_cam_dense_{rnd}.json"), "w") as fh:
        json.dump(out, fh, indent=1)

# ---- parallax test: floor-only fit vs box-region residual, r2 peak ----
print(f"\n{'='*118}")
print("PARALLAX TEST (r2): fit similarity on FLOOR only, then measure how badly the "
      "BOX patches disagree.")
print("Large, systematic box disagreement = genuine 3-D camera motion (depth parallax).")
print('=' * 118)
load = loader("r2")
ref = load(2)
floor_mask, box_mask = build_masks(ref, None)
for i in [12, 14, 15, 16]:
    mov = load(i)
    fr = grid_flow(ref, mov, floor_mask)
    r = fit_sim(fr)
    br = grid_flow(ref, mov, box_mask, cov_min=0.45)
    if len(br) == 0:
        print(f"f{i:04d}: no box patches"); continue
    pdx = r['tx'] + (r['zoom'] / 100) * br[:, 1] - np.tan(np.radians(r['roll'])) * br[:, 0]
    pdy = r['ty'] + (r['zoom'] / 100) * br[:, 0] + np.tan(np.radians(r['roll'])) * br[:, 1]
    err = np.hypot(br[:, 2] - pdx, br[:, 3] - pdy)
    print(f"f{i:04d} t={(i-12)*33.333:+6.1f}ms  floorFit |T|={r['mag']:.2f}px roll={r['roll']:+.3f}° "
          f"zoom={r['zoom']:+.3f}%  medFloorResid={r['med']:.2f}  ||  "
          f"box patches n={len(br)} medResid={np.median(err):.2f} max={err.max():.2f}")

# ---- also compare r1 the same way ----
load = loader("r1")
ref = load(2)
fm, bm = build_masks(ref, None)
for i in [12, 13]:
    mov = load(i)
    fr = grid_flow(ref, mov, fm)
    r = fit_sim(fr)
    br = grid_flow(ref, mov, bm, cov_min=0.45)
    pdx = r['tx'] + (r['zoom'] / 100) * br[:, 1] - np.tan(np.radians(r['roll'])) * br[:, 0]
    pdy = r['ty'] + (r['zoom'] / 100) * br[:, 0] + np.tan(np.radians(r['roll'])) * br[:, 1]
    err = np.hypot(br[:, 2] - pdx, br[:, 3] - pdy)
    print(f"[r1] f{i:04d} floorFit |T|={r['mag']:.2f}px roll={r['roll']:+.3f}° "
          f"zoom={r['zoom']:+.3f}%  medFloorResid={r['med']:.2f}  ||  "
          f"box n={len(br)} medResid={np.median(err):.2f} max={err.max():.2f}")
