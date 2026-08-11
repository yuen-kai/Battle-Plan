import numpy as np
from PIL import Image

B = "Captures/AbilityJuice"


def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32)


def lum(a):
    return 0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]


def sat(a):
    mx, mn = a.max(axis=2), a.min(axis=2)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0)


# aftermath panel, board-relative test, on the 512 strip panel
img = load(f"{B}/strips/r4/debris.jpg")
p1, p2, p3 = img[:, 0:512], img[:, 520:1032], img[:, 1040:1552]
for nm, p in [("impact p2", p2), ("aftermath p3", p3)]:
    L, S = lum(p), sat(p)
    ch = np.abs(p - p1).max(axis=2) > 14
    for tn, m in [
        ("S>=.50 & L>=120", (S >= 0.50) & (L >= 120)),
        ("S>=.50 & |L-182|>=40 & changed", (S >= 0.50) & (np.abs(L - 182) >= 40) & ch),
    ]:
        print(f"{nm:14s} {tn:34s} {100*m.sum()/(512*512):6.3f}% of panel   {m.sum()/9700:.3f} cell^2")

# grey : colour ratio over changed material, both panels
print()
for nm, p in [("impact p2", p2), ("aftermath p3", p3)]:
    S = sat(p)
    ch = np.abs(p - p1).max(axis=2) > 14
    c = (S >= 0.50) & ch
    print(f"{nm}: changed={ch.sum()/9700:.3f} cell^2  saturated={c.sum()/9700:.3f} cell^2  grey:colour={(ch.sum()-c.sum())/max(c.sum(),1):.2f}:1")

# same for the two grey-board references
for rn in ["cm-8newabilities-19", "cm-8newabilities-20", "cm-everyability-20"]:
    im = load(f"{B}/strips/reference/{rn}.jpg")
    q1, q2, q3 = im[:, 0:512], im[:, 520:1032], im[:, 1040:1552]
    st = np.stack([q1, q2, q3])
    static = (st.max(axis=0) - st.min(axis=0)).max(axis=2) < 14
    for i, q in [(2, q2), (3, q3)]:
        S = sat(q)
        ch = ~static
        c = (S >= 0.50) & ch
        print(f"{rn} p{i}: changed={ch.sum()/9700:.3f} sat={c.sum()/9700:.3f} grey:colour={(ch.sum()-c.sum())/max(c.sum(),1):.2f}:1")
