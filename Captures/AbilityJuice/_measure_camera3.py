import numpy as np
from PIL import Image
from scipy import ndimage
import os

D = "/Users/cykai/Battle-Plan/Captures/AbilityJuice/shots/r1/camera"

def load(i):
    return np.asarray(Image.open(os.path.join(D, f"f{i:04d}.jpg")).convert("L"), dtype=np.float64)

H = W = 1024
CY = CX = 512.0

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
# eight big patches in a ring, avoiding the middle third where the unit sits
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

def warp_like(img, tx, ty, rot_deg, scale_minus1):
    """Apply the fitted similarity so img aligns onto the reference frame."""
    th = np.radians(rot_deg); s = 1 + scale_minus1
    # forward: p' = c + s*R*(p-c) + t  -> inverse map for ndimage
    Rm = s * np.array([[np.cos(th), -np.sin(th)], [np.sin(th), np.cos(th)]])
    Minv = np.linalg.inv(Rm)                       # rows/cols = (y,x)
    off = np.array([CY, CX]) - Minv @ (np.array([CY, CX]) + np.array([ty, tx]))
    return ndimage.affine_transform(img, Minv, offset=off, order=3, mode="nearest")

ref = load(2)
print("=== SIMILARITY FIT vs pre-roll f0002 (8 large ring patches, robust) ===")
print(f"{'f':>3} {'t(ms)':>7} | {'tx px':>8} {'ty px':>8} {'|T| px':>8} {'%frame':>7} | "
      f"{'roll°':>7} {'zoom%':>7} | {'n':>2} {'medErr':>7}")
fits = {}
for i in range(9, 24):
    tx, ty, roll, sc, n, err = fit_similarity(ref, load(i))
    fits[i] = (tx, ty, roll, sc)
    mag = np.hypot(tx, ty)
    print(f"{i:>3} {(i-12)*33.33:>7.1f} | {tx:8.2f} {ty:8.2f} {mag:8.2f} {100*mag/1024:6.2f}% | "
          f"{roll:7.3f} {sc*100:7.3f} | {n:>2} {err:7.2f}")

print()
print("--- f0012 patch detail (the frame the build claims 74px on) ---")
fit_similarity(ref, load(12), verbose=True)

print()
print("=== HITSTOP TEST: remove camera motion, is the WORLD frozen? ===")
print("If Hitstop() froze the sim, motion-compensated consecutive frames match.")
print(f"{'pair':>15} | {'rawΔ':>8} {'compΔ':>8} | {'interpretation'}")
for i in range(10, 20):
    a, b = load(i), load(i + 1)
    ta = fits.get(i); tb = fits.get(i + 1)
    if ta is None or tb is None:
        continue
    wa = warp_like(a, *ta)
    wb = warp_like(b, *tb)
    m = np.zeros((H, W), bool); m[80:H - 80, 80:W - 80] = True   # ignore warp edges
    raw = float(np.abs(b - a)[m].mean())
    comp = float(np.abs(wb - wa)[m].mean())
    tag = "WORLD FROZEN" if comp < 1.0 else ("world moving" if comp > 3 else "partial")
    print(f"  f{i:04d}->f{i+1:04d} | {raw:8.3f} {comp:8.3f} | {tag}")

print()
print("=== Is the pre-roll world static at all (i.e. could a freeze even show)? ===")
for i in range(3, 12):
    a, b = load(i), load(i + 1)
    print(f"  f{i:04d}->f{i+1:04d}: meanΔ={float(np.abs(b-a).mean()):.4f}")

print()
print("=== VELOCITY per rendered-frame pair (screen px between written frames) ===")
prev = None
for i in range(11, 22):
    t = fits.get(i)
    if t is None:
        continue
    p = np.array([t[0], t[1]])
    if prev is not None:
        print(f"  f{i-1:04d}->f{i:04d}: step={np.linalg.norm(p-prev):7.2f} px over 33.3ms "
              f"= {np.linalg.norm(p-prev)/0.0333:7.0f} px/s")
    prev = p
