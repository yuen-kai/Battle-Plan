import numpy as np
from PIL import Image
import colorsys

B = "Captures/AbilityJuice"
PANEL = 512 * 512
CELL = 9700.0  # px^2 per projected cell on a 512 strip panel (101 x ~96, sheared)


def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32)


def split(img):
    return [img[:, 0:512], img[:, 520:1032], img[:, 1040:1552]]


def lum(a):
    return 0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]


def sat(a):
    mx = a.max(axis=2)
    mn = a.min(axis=2)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


def hue(a):
    mx = a.max(axis=2)
    mn = a.min(axis=2)
    d = mx - mn
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    h = np.zeros_like(mx)
    m = (d > 1e-6) & (mx == r)
    h[m] = (60 * ((g - b) / np.maximum(d, 1e-6)))[m] % 360
    m = (d > 1e-6) & (mx == g)
    h[m] = (60 * ((b - r) / np.maximum(d, 1e-6)) + 120)[m]
    m = (d > 1e-6) & (mx == b)
    h[m] = (60 * ((r - g) / np.maximum(d, 1e-6)) + 240)[m]
    return h


def report(name, path):
    img = load(path)
    ps = split(img)
    print(f"\n=== {name} ===")
    for i, p in enumerate(ps):
        L = lum(p)
        S = sat(p)
        a = ((S >= 0.50) & (L >= 120)).sum()
        b = ((S > 0.45) & (L > 199)).sum()
        c = ((S >= 0.50) & (L >= 60)).sum()  # colour at ANY value
        d = ((S >= 0.35) & (L >= 120)).sum()
        print(
            f" p{i+1}: A[S>=.50,L>=120]={a:7d} ({100*a/PANEL:5.3f}%, {a/CELL:5.3f} cell^2)"
            f" | B[S>.45,L>199]={b:6d} ({100*b/PANEL:5.3f}%)"
            f" | S>=.50 any L={c:7d} ({100*c/PANEL:5.3f}%)"
            f" | S>=.35,L>=120={d:7d} ({100*d/PANEL:5.3f}%)"
        )


for n, p in [
    ("R4 debris", f"{B}/strips/r4/debris.jpg"),
    ("R3 debris", f"{B}/strips/r3/debris.jpg"),
    ("R4 grenade (context)", f"{B}/strips/r4/grenade.jpg"),
    ("REF cm-8new-19 (grey board)", f"{B}/strips/reference/cm-8newabilities-19.jpg"),
    ("REF cm-8new-20 (grey board)", f"{B}/strips/reference/cm-8newabilities-20.jpg"),
    ("REF cm-every-20 (bomb)", f"{B}/strips/reference/cm-everyability-20.jpg"),
    ("REF cm-every-03", f"{B}/strips/reference/cm-everyability-03.jpg"),
    ("REF cm-8new-12", f"{B}/strips/reference/cm-8newabilities-12.jpg"),
    ("REF cm-every-27", f"{B}/strips/reference/cm-everyability-27.jpg"),
    ("REF cm-every-23", f"{B}/strips/reference/cm-everyability-23.jpg"),
]:
    report(n, p)
