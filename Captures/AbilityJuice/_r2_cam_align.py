"""Definitive camera-pose measurement by direct dense image alignment.

Phase correlation on 384px patches assumes pure translation *within* each patch. With
roll ~1.8deg and zoom ~4% that assumption breaks (12-15px of internal deformation),
which is exactly why the r2 8-patch residuals blew up to ~10px. This solves the
similarity (tx, ty, roll, zoom) directly by sampling the moving frame at warped
reference-pixel coordinates and minimising SAD, which handles rotation and scale exactly.
"""
import numpy as np
from PIL import Image
from scipy import ndimage, optimize
import os, json

AJ = "/Users/cykai/Battle-Plan/Captures/AbilityJuice"
CY = CX = 512.0
CORNER_R = np.hypot(512.0, 512.0)
RNG = np.random.default_rng(7)


def loader(rnd):
    D = os.path.join(AJ, "shots", rnd, "camera")
    return lambda i: np.asarray(Image.open(os.path.join(D, f"f{i:04d}.jpg")).convert("L"),
                                dtype=np.float64)


def build_mask(ref, exclude_boxes=True, cx=480, cy=430, rad=330):
    yy, xx = np.mgrid[0:1024, 0:1024]
    m = np.ones((1024, 1024), bool)
    m[:100, :] = False; m[-100:, :] = False; m[:, :100] = False; m[:, -100:] = False
    m &= ((xx - cx) ** 2 + (yy - cy) ** 2) > rad ** 2
    if exclude_boxes:
        dark = ndimage.binary_closing(ref < 110, np.ones((5, 5)))
        m &= ~ndimage.binary_dilation(dark, np.ones((21, 21)))
    return m


class Aligner:
    """Sample ~N masked reference points; cost = SAD between ref values and the
    moving frame sampled at the forward-warped locations."""

    def __init__(self, ref, mask, npts=60000):
        ys, xs = np.nonzero(mask)
        if len(ys) > npts:
            sel = RNG.choice(len(ys), npts, replace=False)
            ys, xs = ys[sel], xs[sel]
        self.py = ys.astype(np.float64) - CY
        self.px = xs.astype(np.float64) - CX
        self.rv = ref[ys, xs]
        self.grad = np.hypot(*np.gradient(ref))[ys, xs]

    def cost(self, mov, p):
        tx, ty, roll, zoom = p
        th = np.radians(roll); s = 1.0 + zoom
        c, si = np.cos(th) * s, np.sin(th) * s
        # forward: ref point -> where it landed in mov;  invert to sample mov
        # mov_pt = R*ref_pt + t  =>  we want mov value at that location
        mx = c * self.px - si * self.py + tx + CX
        my = si * self.px + c * self.py + ty + CY
        v = ndimage.map_coordinates(mov, [my, mx], order=1, mode="nearest")
        return float(np.abs(v - self.rv).mean())


def solve(al, mov, x0):
    f = lambda p: al.cost(mov, p)
    cands = [np.asarray(x0, float), np.zeros(4)]
    for _ in range(20):
        cands.append(np.asarray(x0, float) + RNG.normal(0, [8, 8, 0.6, 0.010]))
    best = min(((f(c), c) for c in cands), key=lambda t: t[0])
    for sc in ([5, 5, 0.5, 0.008], [1.5, 1.5, 0.15, 0.0025],
               [0.4, 0.4, 0.04, 0.0006], [0.1, 0.1, 0.01, 0.00015]):
        simplex = np.vstack([best[1]] + [best[1] + np.eye(4)[i] * sc[i] for i in range(4)])
        r = optimize.minimize(f, best[1], method="Nelder-Mead",
                              options=dict(xatol=1e-5, fatol=1e-7, maxiter=2000,
                                           initial_simplex=simplex))
        if r.fun < best[0]:
            best = (r.fun, r.x)
    return best[1], best[0]


def run(rnd, frames, seeds, label):
    load = loader(rnd)
    ref = load(2)
    mask = build_mask(ref)
    al = Aligner(ref, mask)
    floor = np.mean([al.cost(load(j), (0, 0, 0, 0)) for j in (3, 4, 5)])
    print(f"\n{'='*126}")
    print(f"{label} — DIRECT DENSE ALIGNMENT ({al.rv.size} board-plane samples; "
          f"static-frame SAD noise floor = {floor:.4f})")
    print('=' * 126)
    print(f"{'f':>3} {'t(ms)':>7} | {'tx px':>8} {'ty px':>8} {'|T|px':>7} {'%fr':>6} | "
          f"{'roll°':>7} {'zoom%':>7} | {'cornRoll':>8} {'zoomPx':>7} {'rotShr':>7} "
          f"{'cornTot':>8} | {'SAD':>6}")
    out = []
    for i in frames:
        p, c = solve(al, load(i), seeds.get(i, (0, 0, 0, 0)))
        tx, ty, roll, zoom = p
        mag = float(np.hypot(tx, ty))
        cr = abs(np.radians(roll)) * CORNER_R
        zp = abs(zoom) * CORNER_R
        share = cr / (cr + mag) if (cr + mag) > 1e-9 else 0.0
        out.append(dict(f=i, t=(i - 12) * 33.333, tx=tx, ty=ty, mag=mag, roll=roll,
                        zoom=zoom * 100, corner_roll=cr, zoom_px=zp, share=share,
                        corner_total=mag + cr + zp, sad=c))
        print(f"{i:>3} {(i-12)*33.333:>7.1f} | {tx:8.2f} {ty:8.2f} {mag:7.2f} "
              f"{100*mag/1024:5.2f}% | {roll:7.3f} {zoom*100:7.3f} | {cr:8.2f} {zp:7.2f} "
              f"{100*share:6.1f}% {mag+cr+zp:8.2f} | {c:6.3f}")
    return out


if __name__ == "__main__":
    seeds2 = {12: (12.6, -13.9, 2.54, 0.050), 13: (5.7, -17.3, 1.82, 0.039),
              14: (12.4, -18.1, 2.47, 0.053), 15: (3.2, -4.2, 0.61, 0.015),
              16: (-2.8, 2.4, -0.40, -0.009), 17: (-1.7, 2.3, -0.38, -0.008),
              18: (0.0, 0.4, -0.10, -0.0015), 19: (0.3, -0.5, 0.04, 0.001)}
    seeds1 = {12: (29.7, -69.5, 0.44, 0.017), 13: (11.7, 25.5, -0.33, -0.008),
              14: (2.4, 2.0, -0.04, -0.0004), 15: (-1.2, -8.6, 0.06, 0.002),
              16: (-1.9, 5.2, -0.03, -0.0014), 17: (-1.0, -1.2, 0.01, 0.00006)}
    res = {"r2": run("r2", range(8, 24), seeds2, "ROUND 2"),
           "r1": run("r1", range(8, 22), seeds1, "ROUND 1 (rejected)")}
    with open(os.path.join(AJ, "_r2_cam_align.json"), "w") as fh:
        json.dump(res, fh, indent=1)
    print("\nwrote _r2_cam_align.json")
