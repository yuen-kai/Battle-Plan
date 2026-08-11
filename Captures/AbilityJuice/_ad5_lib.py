"""Round-5 critic measurement library. Independent of any builder code."""
import numpy as np
from PIL import Image

STRIPS = "Captures/AbilityJuice/strips"


def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float64)


def panels(img):
    """Split a 3-panel strip on its black separator columns."""
    h, w, _ = img.shape
    col = img.mean(axis=(0, 2))
    dark = col < 30
    # find runs of dark columns that are not at the very edge
    runs, start = [], None
    for x in range(w):
        if dark[x] and start is None:
            start = x
        elif not dark[x] and start is not None:
            runs.append((start, x))
            start = None
    if start is not None:
        runs.append((start, w))
    runs = [r for r in runs if r[0] > w * 0.15 and r[1] < w * 0.85]
    if len(runs) != 2:
        # fall back to even thirds
        t = w // 3
        return [img[:, 0:t], img[:, t:2 * t], img[:, 2 * t:]]
    a, b = runs
    return [img[:, 0:a[0]], img[:, a[1]:b[0]], img[:, b[1]:w]]


def lum(p):
    """Rec.601 luma, the convention used by every prior round."""
    return 0.299 * p[..., 0] + 0.587 * p[..., 1] + 0.114 * p[..., 2]


def sat(p):
    mx = p.max(axis=-1)
    mn = p.min(axis=-1)
    return np.where(mx > 0, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


def hue(p):
    r, g, b = p[..., 0] / 255, p[..., 1] / 255, p[..., 2] / 255
    mx = np.max(p, axis=-1) / 255
    mn = np.min(p, axis=-1) / 255
    d = mx - mn
    h = np.zeros_like(mx)
    m = (d > 1e-6) & (mx == r)
    h[m] = (60 * ((g - b)[m] / d[m])) % 360
    m = (d > 1e-6) & (mx == g)
    h[m] = 60 * ((b - r)[m] / d[m]) + 120
    m = (d > 1e-6) & (mx == b)
    h[m] = 60 * ((r - g)[m] / d[m]) + 240
    return h


def bright_sat_pct(p, lth=199, sth=0.45):
    m = (lum(p) > lth) & (sat(p) > sth)
    return 100.0 * m.sum() / m.size, m


def pct_above(p, lth):
    L = lum(p)
    return 100.0 * (L > lth).sum() / L.size


def pct_clipped(p, ch=250):
    m = (p[..., 0] >= ch) & (p[..., 1] >= ch) & (p[..., 2] >= ch)
    return 100.0 * m.sum() / m.size


def local_contrast(p, k=9):
    """Mean of (max-min) in a k x k window, via separable rank filters."""
    from scipy.ndimage import maximum_filter, minimum_filter
    L = lum(p)
    return float((maximum_filter(L, k) - minimum_filter(L, k)).mean())
