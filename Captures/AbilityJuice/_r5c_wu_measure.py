"""Round-5 wind-up critic measurements. Read-only analysis of captured strips."""
import numpy as np
from PIL import Image
import os, json

BASE = os.path.dirname(os.path.abspath(__file__))
S = os.path.join(BASE, "strips")


def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float64)


def luma(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def hsv(a):
    mx = a.max(-1)
    mn = a.min(-1)
    d = mx - mn
    s = np.where(mx > 0, d / np.maximum(mx, 1e-9), 0.0)
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    h = np.zeros_like(mx)
    m = (d > 0) & (mx == r)
    h[m] = (60 * ((g[m] - b[m]) / d[m])) % 360
    m = (d > 0) & (mx == g)
    h[m] = (60 * ((b[m] - r[m]) / d[m]) + 120) % 360
    m = (d > 0) & (mx == b)
    h[m] = (60 * ((r[m] - g[m]) / d[m]) + 240) % 360
    return h, s, mx


def panels(img, n=3):
    """Split a strip into n panels, trimming the black separator gutters."""
    L = luma(img)
    colmean = L.mean(0)
    dark = colmean < 30
    # find contiguous non-dark runs
    runs, start = [], None
    for i, d in enumerate(dark):
        if not d and start is None:
            start = i
        elif d and start is not None:
            runs.append((start, i))
            start = None
    if start is not None:
        runs.append((start, len(dark)))
    runs = [r for r in runs if r[1] - r[0] > 50]
    return runs


def report(tag, path):
    img = load(path)
    print(f"\n=== {tag}  {os.path.basename(path)}  shape={img.shape}")
    rr = panels(img)
    print("   panel x-runs:", rr)
    return img, rr


if __name__ == "__main__":
    for tag, p in [("r5", f"{S}/r5/windup.jpg"), ("r4", f"{S}/r4/windup.jpg"), ("r3", f"{S}/r3/windup.jpg")]:
        report(tag, p)
