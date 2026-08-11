import numpy as np
from PIL import Image
from scipy import ndimage

B = "Captures/AbilityJuice"
D = f"{B}/shots/r4/debris"


def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32)


def hsl(a):
    mx, mn = a.max(axis=2), a.min(axis=2)
    d = mx - mn
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    H = np.zeros_like(mx)
    m = (d > 1e-6) & (mx == r); H[m] = (60 * ((g - b) / np.maximum(d, 1e-6)))[m] % 360
    m = (d > 1e-6) & (mx == g); H[m] = (60 * ((b - r) / np.maximum(d, 1e-6)) + 120)[m]
    m = (d > 1e-6) & (mx == b); H[m] = (60 * ((r - g) / np.maximum(d, 1e-6)) + 240)[m]
    S = np.where(mx > 1e-6, d / np.maximum(mx, 1e-6), 0)
    L = 0.299 * r + 0.587 * g + 0.114 * b
    return H, S, L


plate = load(f"{D}/f0000.jpg")
pH, pS, pL = hsl(plate)

for fi in (28, 33):
    a = load(f"{D}/f{fi:04d}.jpg")
    H, S, L = hsl(a)
    diff = np.abs(a - plate).max(axis=2)
    # the ground scorch = changed, NOT dark chunk, sitting on the floor
    scorch = (diff > 18) & (L > 90) & (pL > 150)
    scorch = ndimage.binary_opening(scorch, np.ones((3, 3), bool))
    scorch = ndimage.binary_closing(scorch, np.ones((7, 7), bool))
    lab, n = ndimage.label(scorch)
    sz = ndimage.sum(scorch, lab, range(1, n + 1))
    dom = lab == (int(np.argmax(sz)) + 1)
    sel = dom & (S >= 0.30)
    hs = H[sel]
    print(f"\n=== f{fi:04d} (t={(fi-12)/30:+.3f}) ground scorch, {int(dom.sum())} px ===")
    print(f" saturated (S>=.30) inside scorch: {int(sel.sum())}")
    for lo, hi, nm in [(0, 20, "red-ember"), (20, 45, "orange"), (45, 65, "yellow"), (65, 170, "GREEN"), (170, 260, "cyan/blue")]:
        c = ((hs >= lo) & (hs < hi)).sum()
        print(f"   hue {lo:3d}-{hi:3d} {nm:10s}: {c:6d}  {100*c/max(len(hs),1):5.1f}%")
    print(f" median L inside scorch: {np.median(L[dom]):.0f}  vs plate under it {np.median(pL[dom]):.0f}"
          f"  -> delta {np.median(L[dom])-np.median(pL[dom]):+.0f}")
    print(f" scorch pixels BRIGHTER than the plate beneath: {100*((L>pL)&dom).mean()/max(dom.mean(),1e-9):.1f}%")

    # straight-edge test: how much of the scorch boundary is axis-aligned straight?
    ys, xs = np.nonzero(dom)
    x0, x1, y0, y1 = xs.min(), xs.max(), ys.min(), ys.max()
    print(f" bbox {x1-x0}x{y1-y0} px  fill ratio {dom.sum()/((x1-x0)*(y1-y0)):.2f}")
    # count columns whose top boundary lands on the same few rows
    tops = np.array([np.nonzero(dom[:, x])[0].min() if dom[:, x].any() else -1 for x in range(x0, x1 + 1)])
    v = tops[tops >= 0]
    mode_top = np.bincount(v).argmax()
    onrow = ((np.abs(v - mode_top) <= 2).sum())
    print(f" top boundary: {onrow}/{len(v)} columns ({100*onrow/len(v):.0f}%) land within 2px of row {mode_top}"
          f"  -> straight horizontal edge of {onrow}px")
    lefts = np.array([np.nonzero(dom[y, :])[0].min() if dom[y, :].any() else -1 for y in range(y0, y1 + 1)])
    w = lefts[lefts >= 0]
    mode_l = np.bincount(w).argmax()
    oncol = (np.abs(w - mode_l) <= 2).sum()
    print(f" left boundary: {oncol}/{len(w)} rows ({100*oncol/len(w):.0f}%) within 2px of col {mode_l}")
    rights = np.array([np.nonzero(dom[y, :])[0].max() if dom[y, :].any() else -1 for y in range(y0, y1 + 1)])
    e = rights[rights >= 0]
    mode_r = np.bincount(e).argmax()
    oncr = (np.abs(e - mode_r) <= 2).sum()
    print(f" right boundary: {oncr}/{len(e)} rows ({100*oncr/len(e):.0f}%) within 2px of col {mode_r}")
