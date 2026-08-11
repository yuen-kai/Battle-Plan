"""Round-2 camera-response measurement: same 8-patch robust similarity fit as round 1."""
import numpy as np
from PIL import Image
from scipy import ndimage
import os, sys, json

BASE = "/Users/cykai/Battle-Plan/Captures/AbilityJuice/shots"

H = W = 1024
CY = CX = 512.0
CORNER_R = np.hypot(512.0, 512.0)   # 724.08 px, distance centre -> frame corner


def loader(rnd):
    D = os.path.join(BASE, rnd, "camera")
    def load(i):
        return np.asarray(Image.open(os.path.join(D, f"f{i:04d}.jpg")).convert("L"),
                          dtype=np.float64)
    return load


def hann2d(h, w):
    return np.outer(np.hanning(h), np.hanning(w))


def phase_corr(ref, mov):
    h, w = ref.shape
    win = hann2d(h, w)
    R = np.fft.fft2((ref - ref.mean()) * win)
    M = np.fft.fft2((mov - mov.mean()) * win)
    X = R * np.conj(M)
    X /= np.maximum(np.abs(X), 1e-12)
    c = np.fft.ifft2(X).real
    py, px = np.unravel_index(np.argmax(c), c.shape)
    iy = py - h if py > h // 2 else py
    ix = px - w if px > w // 2 else px

    def parab(cm, c0, cp):
        den = 2 * (cm - 2 * c0 + cp)
        return (cm - cp) / den if abs(den) > 1e-12 else 0.0
    c0 = c[py, px]
    sy = parab(c[(py - 1) % h, px], c0, c[(py + 1) % h, px])
    sx = parab(c[py, (px - 1) % w], c0, c[py, (px + 1) % w])
    return (iy + sy), (ix + sx), float(c0 / (np.abs(c).mean() + 1e-12))


P = 384
ring = [(8, 8), (8, 320), (8, W - P - 8),
        (320, 8), (320, W - P - 8),
        (H - P - 8, 8), (H - P - 8, 320), (H - P - 8, W - P - 8)]


def fit_similarity(ref, mov, verbose=False):
    rows = []
    for (y, x) in ring:
        dy, dx, conf = phase_corr(ref[y:y + P, x:x + P], mov[y:y + P, x:x + P])
        rows.append((y + P / 2 - CY, x + P / 2 - CX, dx, dy, conf))
    rows = np.array(rows)
    keep = np.ones(len(rows), bool)
    for _ in range(3):
        cy, cx, dx, dy = rows[keep, 0], rows[keep, 1], rows[keep, 2], rows[keep, 3]
        n = keep.sum()
        A = np.zeros((2 * n, 4)); rhs = np.zeros(2 * n)
        A[:n, 0] = 1; A[:n, 2] = cx; A[:n, 3] = -cy; rhs[:n] = dx
        A[n:, 1] = 1; A[n:, 2] = cy; A[n:, 3] = cx;  rhs[n:] = dy
        sol, *_ = np.linalg.lstsq(A, rhs, rcond=None)
        pred_dx = sol[0] + sol[2] * rows[:, 1] - sol[3] * rows[:, 0]
        pred_dy = sol[1] + sol[2] * rows[:, 0] + sol[3] * rows[:, 1]
        err = np.hypot(rows[:, 2] - pred_dx, rows[:, 3] - pred_dy)
        med = np.median(err[keep])
        newkeep = err < max(2.0, 3 * med)
        if newkeep.sum() < 5 or (newkeep == keep).all():
            keep = newkeep if newkeep.sum() >= 5 else keep
            break
        keep = newkeep
    tx, ty, a, b = sol
    if verbose:
        for r, k in zip(rows, keep):
            print(f"      patch c=({r[1]:+6.0f},{r[0]:+6.0f}) d=({r[2]:+7.2f},{r[3]:+7.2f}) "
                  f"conf={r[4]:6.1f} {'' if k else 'REJECTED'}")
    return tx, ty, np.degrees(np.arctan2(b, 1 + a)), a, keep.sum(), float(np.median(err[keep]))


def run(rnd, ref_idx, lo, hi, label):
    load = loader(rnd)
    ref = load(ref_idx)
    print(f"\n{'='*104}")
    print(f"{label}  —  8-patch robust similarity fit vs pre-roll f{ref_idx:04d}")
    print('=' * 104)
    print(f"{'f':>3} {'t(ms)':>8} | {'tx px':>8} {'ty px':>8} {'|T| px':>8} {'%frame':>7} | "
          f"{'roll deg':>9} {'zoom%':>7} | {'cornerRoll':>10} {'rotShare':>8} | {'n':>2} {'medErr':>7}")
    fits = {}
    recs = []
    for i in range(lo, hi):
        tx, ty, roll, sc, n, err = fit_similarity(ref, load(i))
        fits[i] = (tx, ty, roll, sc)
        mag = np.hypot(tx, ty)
        corner_roll = abs(np.radians(roll)) * CORNER_R
        share = corner_roll / (corner_roll + mag) if (corner_roll + mag) > 1e-9 else 0.0
        recs.append(dict(f=i, t=(i - 12) * 33.333, tx=tx, ty=ty, mag=mag, roll=roll,
                         zoom=sc * 100, corner_roll=corner_roll, share=share,
                         n=int(n), err=err))
        print(f"{i:>3} {(i-12)*33.333:>8.1f} | {tx:8.2f} {ty:8.2f} {mag:8.2f} {100*mag/1024:6.2f}% | "
              f"{roll:9.3f} {sc*100:7.3f} | {corner_roll:10.2f} {100*share:7.1f}% | {n:>2} {err:7.2f}")
    return fits, recs


if __name__ == "__main__":
    which = sys.argv[1] if len(sys.argv) > 1 else "both"
    out = {}
    if which in ("r1", "both"):
        f1, r1 = run("r1", 2, 6, 30, "ROUND 1 (rejected)")
        out["r1"] = r1
    if which in ("r2", "both"):
        f2, r2 = run("r2", 2, 6, 40, "ROUND 2")
        out["r2"] = r2
    with open("/Users/cykai/Battle-Plan/Captures/AbilityJuice/_r2_cam_fit.json", "w") as fh:
        json.dump(out, fh, indent=1)
    print("\nwrote _r2_cam_fit.json")
