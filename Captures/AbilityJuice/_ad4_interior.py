import numpy as np
from PIL import Image
from scipy import ndimage

B = "Captures/AbilityJuice"
CELL = 201.0 * 193.0


def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32)


def lum(a):
    return 0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]


def sat(a):
    mx, mn = a.max(axis=2), a.min(axis=2)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


plate = load(f"{B}/shots/r4/debris/f0000.jpg")
pL = lum(plate)


def local_sigma(L, mask, k=9):
    """std of L in a k x k window, averaged over the eroded mask interior."""
    m = mask.astype(np.float32)
    mu = ndimage.uniform_filter(L * m, k) / np.maximum(ndimage.uniform_filter(m, k), 1e-6)
    mu2 = ndimage.uniform_filter(L * L * m, k) / np.maximum(ndimage.uniform_filter(m, k), 1e-6)
    var = np.maximum(mu2 - mu * mu, 0)
    core = ndimage.binary_erosion(mask, np.ones((k, k), bool))
    if core.sum() < 50:
        core = mask
    return float(np.sqrt(var)[core].mean()), int(core.sum())


for fi, label in [(15, "money frame +0.100s"), (13, "+0.033s"), (18, "+0.200s"), (28, "aftermath +0.533s")]:
    a = load(f"{B}/shots/r4/debris/f{fi:04d}.jpg")
    L = lum(a)
    occl = (pL - L) > 60
    occl = ndimage.binary_opening(occl, np.ones((3, 3), bool))
    lab, n = ndimage.label(occl)
    if n == 0:
        print(f"{label}: no occluding mass")
        continue
    sizes = ndimage.sum(occl, lab, range(1, n + 1))
    big = int(np.argmax(sizes)) + 1
    dom = lab == big
    s9, npx = local_sigma(L, dom, 9)
    s15, _ = local_sigma(L, dom, 15)
    glob = float(L[dom].std())
    print(
        f"{label}: dominant mass {sizes.max()/CELL:.3f} cell^2, {int(sizes.max())}px | "
        f"local sigma 9x9 = {s9:.2f} (core {npx}px), 15x15 = {s15:.2f} | global std in mass = {glob:.1f}"
    )

# Same test on the strongest reference dust masses
print("\n--- reference dust interiors (512 panel; ours measured on 1024, so also give ours at 512) ---")
img = load(f"{B}/strips/r4/debris.jpg")
p2 = img[:, 520:1032]
board = 182.0
L2 = lum(p2)
occl2 = ndimage.binary_opening((board - L2) > 60, np.ones((3, 3), bool))
lab, n = ndimage.label(occl2)
sizes = ndimage.sum(occl2, lab, range(1, n + 1))
dom = lab == (int(np.argmax(sizes)) + 1)
print(f"OURS strip p2 dominant: {sizes.max():.0f}px  local sigma 9x9 = {local_sigma(L2, dom, 9)[0]:.2f}")

for nm in ["cm-8newabilities-19", "cm-8newabilities-20", "cm-everyability-20", "cm-everyability-27"]:
    im = load(f"{B}/strips/reference/{nm}.jpg")
    ps = [im[:, 0:512], im[:, 520:1032], im[:, 1040:1552]]
    S0 = sat(ps[0])
    for i, p in enumerate(ps[1:], start=2):
        L = lum(p)
        d = np.abs(p - ps[0]).max(axis=2) > 25
        m = ndimage.binary_opening(d & (L > 60), np.ones((5, 5), bool))
        lab, k = ndimage.label(m)
        if k == 0:
            continue
        sz = ndimage.sum(m, lab, range(1, k + 1))
        if sz.max() < 2000:
            continue
        dm = lab == (int(np.argmax(sz)) + 1)
        s9, npx = local_sigma(L, dm, 9)
        print(f"{nm} p{i}: dominant changed mass {sz.max():.0f}px  local sigma 9x9 = {s9:.2f}")
