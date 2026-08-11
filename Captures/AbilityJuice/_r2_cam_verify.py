"""Independent, model-free checks of ROLL and ZOOM, plus the debris hitstop test."""
import numpy as np
from PIL import Image
from scipy import ndimage
import os, json

AJ = "/Users/cykai/Battle-Plan/Captures/AbilityJuice"
CY = CX = 512.0
CORNER_R = np.hypot(512.0, 512.0)


def loader(rnd):
    D = os.path.join(AJ, "shots", rnd, "camera")
    return lambda i: np.asarray(Image.open(os.path.join(D, f"f{i:04d}.jpg")).convert("L"),
                                dtype=np.float64)


# ---------------------------------------------------------------- ROLL, model free
def orient_hist(img, mask, nb=1440):
    """Gradient-orientation histogram of strong edges, weighted by |grad|.
    A pure camera roll shifts this histogram rigidly. Immune to translation."""
    gy, gx = np.gradient(ndimage.gaussian_filter(img, 1.2))
    mag = np.hypot(gx, gy)
    sel = mask & (mag > np.percentile(mag[mask], 88))
    ang = (np.degrees(np.arctan2(gy[sel], gx[sel])) % 180.0)
    h, _ = np.histogram(ang, bins=nb, range=(0, 180), weights=mag[sel])
    return h / (h.sum() + 1e-12)


def hist_shift(h0, h1, maxdeg=8.0, nb=1440):
    """Circular cross-correlation peak with parabolic sub-bin refinement."""
    per = nb / 180.0
    k = int(maxdeg * per)
    best, bs = -1e18, 0
    scores = {}
    for s in range(-k, k + 1):
        v = float(np.dot(h0, np.roll(h1, s)))
        scores[s] = v
        if v > best:
            best, bs = v, s
    a, b, c = scores.get(bs - 1, best), best, scores.get(bs + 1, best)
    den = 2 * (a - 2 * b + c)
    sub = (a - c) / den if abs(den) > 1e-15 else 0.0
    return (bs + sub) / per


# ---------------------------------------------------------------- ZOOM, model free
def tile_pitch(img, mask):
    """Radial power spectrum peak of the tile lattice. Scales exactly with zoom,
    immune to translation. Sub-bin refined."""
    m = img * mask
    m = m - m[mask].mean()
    win = np.outer(np.hanning(1024), np.hanning(1024))
    F = np.abs(np.fft.fftshift(np.fft.fft2(m * win)))
    yy, xx = np.mgrid[0:1024, 0:1024]
    r = np.hypot(yy - 512, xx - 512)
    rb = r.astype(int)
    prof = np.bincount(rb.ravel(), F.ravel(), minlength=520)[:520]
    cnt = np.bincount(rb.ravel(), minlength=520)[:520]
    prof = prof / np.maximum(cnt, 1)
    lo, hi = 4, 60
    k = lo + int(np.argmax(prof[lo:hi]))
    a, b, c = prof[k - 1], prof[k], prof[k + 1]
    den = 2 * (a - 2 * b + c)
    return k + ((a - c) / den if abs(den) > 1e-12 else 0.0)


def build_mask(ref, cx=480, cy=430, rad=330):
    yy, xx = np.mgrid[0:1024, 0:1024]
    m = np.ones((1024, 1024), bool)
    m[:110, :] = False; m[-110:, :] = False; m[:, :110] = False; m[:, -110:] = False
    m &= ((xx - cx) ** 2 + (yy - cy) ** 2) > rad ** 2
    dark = ndimage.binary_closing(ref < 110, np.ones((5, 5)))
    m &= ~ndimage.binary_dilation(dark, np.ones((21, 21)))
    return m


align = json.load(open(os.path.join(AJ, "_r2_cam_align.json")))

for rnd, frames in (("r2", [11, 12, 13, 14, 15, 16, 17, 18]),
                    ("r1", [11, 12, 13, 14, 15, 16])):
    load = loader(rnd)
    ref = load(2)
    mask = build_mask(ref)
    h0 = orient_hist(ref, mask)
    p0 = tile_pitch(ref, mask)
    fit = {d["f"]: d for d in align[rnd]}
    print(f"\n{'='*112}")
    print(f"{rnd.upper()} — INDEPENDENT CHECKS   (edge-orientation roll, lattice-pitch zoom)")
    print(f"    reference lattice peak = bin {p0:.4f}")
    print('=' * 112)
    print(f"{'f':>3} | {'roll: solver':>12} {'orientHist':>11} {'Δ':>7} | "
          f"{'zoom%: solver':>13} {'lattice%':>9} {'Δ':>7}")
    for i in frames:
        h1 = orient_hist(load(i), mask)
        d_roll = hist_shift(h0, h1)
        pi = tile_pitch(load(i), mask)
        # bin index is proportional to spatial FREQUENCY -> zoom = p0/pi - 1
        z_lat = (p0 / pi - 1.0) * 100
        s = fit.get(i, {})
        sr, sz = s.get("roll", float("nan")), s.get("zoom", float("nan"))
        print(f"{i:>3} | {sr:12.3f} {d_roll:11.3f} {abs(abs(sr)-abs(d_roll)):7.3f} | "
              f"{sz:13.3f} {z_lat:9.3f} {abs(abs(sz)-abs(z_lat)):7.3f}")
