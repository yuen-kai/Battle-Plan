import numpy as np
from PIL import Image

B = "Captures/AbilityJuice"
PANEL = 512 * 512
CELL = 9700.0


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


def fair(name, path):
    ps = split(load(path))
    st = np.stack([p for p in ps])
    # static = pixels essentially unchanged across all three panels (board, UI, idle units)
    spread = st.max(axis=0) - st.min(axis=0)
    static = spread.max(axis=2) < 14
    out = [name]
    for i, p in enumerate(ps):
        L, S = lum(p), sat(p)
        m = (S >= 0.50) & (L >= 120)
        tot = m.sum()
        dyn = (m & ~static).sum()
        out.append((100 * tot / PANEL, 100 * dyn / PANEL, dyn / CELL))
    print(
        f"{name:30s} static={100*static.mean():5.1f}%  "
        + "  ".join(f"p{i+1} all={a:6.3f}% dyn={b:6.3f}% ({c:5.3f}cell2)" for i, (a, b, c) in enumerate(out[1:]))
    )
    return out


rows = [
    ("OURS r4 debris", f"{B}/strips/r4/debris.jpg"),
    ("OURS r3 debris", f"{B}/strips/r3/debris.jpg"),
    ("OURS r4 grenade", f"{B}/strips/r4/grenade.jpg"),
    ("REF 8new-19 grey", f"{B}/strips/reference/cm-8newabilities-19.jpg"),
    ("REF 8new-20 grey", f"{B}/strips/reference/cm-8newabilities-20.jpg"),
    ("REF every-20 bomb", f"{B}/strips/reference/cm-everyability-20.jpg"),
    ("REF every-03", f"{B}/strips/reference/cm-everyability-03.jpg"),
    ("REF 8new-12", f"{B}/strips/reference/cm-8newabilities-12.jpg"),
    ("REF every-27", f"{B}/strips/reference/cm-everyability-27.jpg"),
    ("REF every-23", f"{B}/strips/reference/cm-everyability-23.jpg"),
    ("REF every-05", f"{B}/strips/reference/cm-everyability-05.jpg"),
    ("REF every-30", f"{B}/strips/reference/cm-everyability-30.jpg"),
    ("REF season2-05", f"{B}/strips/reference/cm-season2-05.jpg"),
    ("REF clashab-04", f"{B}/strips/reference/cm-clashabilities-04.jpg"),
]
res = [fair(n, p) for n, p in rows]

refs = [r for r in res if r[0].startswith("REF")]
for idx, lbl in [(1, "impact p2"), (2, "aftermath p3")]:
    v = sorted(r[1 + idx][1] for r in refs)
    print(f"\nREF {lbl} dynamic saturated %: min={v[0]:.3f} med={np.median(v):.3f} max={v[-1]:.3f}  n={len(v)}")
    print("  all:", [round(x, 2) for x in v])
