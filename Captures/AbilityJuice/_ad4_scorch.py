import numpy as np
from PIL import Image
from scipy import ndimage

B = "Captures/AbilityJuice"
D = f"{B}/shots/r4/debris"
CELLW, CELLH = 201.0, 193.0


def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32)


def lum(a):
    return 0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]


def sat(a):
    mx, mn = a.max(axis=2), a.min(axis=2)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


def hue(a):
    mx, mn = a.max(axis=2), a.min(axis=2)
    d = mx - mn
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    h = np.zeros_like(mx)
    m = (d > 1e-6) & (mx == r); h[m] = (60 * ((g - b) / np.maximum(d, 1e-6)))[m] % 360
    m = (d > 1e-6) & (mx == g); h[m] = (60 * ((b - r) / np.maximum(d, 1e-6)) + 120)[m]
    m = (d > 1e-6) & (mx == b); h[m] = (60 * ((r - g) / np.maximum(d, 1e-6)) + 240)[m]
    return h


plate = load(f"{D}/f0000.jpg")
pL = lum(plate)

# --- locate tile seams on the clean plate: local minima ridges of luminance ---
sm = ndimage.uniform_filter(pL, 9)
seamness = sm - pL  # positive where a dark seam sits below its neighbourhood
seam = seamness > 8
print("clean-plate seam pixels:", int(seam.sum()))


def seam_contrast(L, region):
    """Mean (neighbourhood - seam) luminance across seam pixels inside region."""
    s = seam & region
    if s.sum() < 200:
        return None, int(s.sum())
    nb = ndimage.uniform_filter(L, 9)
    return float((nb - L)[s].mean()), int(s.sum())


# clean reference contrast in an untouched area
far = np.zeros_like(seam)
far[100:900, 60:260] = True
c0, n0 = seam_contrast(pL, far)
print(f"clean floor seam contrast (control window): {c0:.1f} over {n0} seam px")

print(f"\n{'f':>3} {'t':>7} {'scorchCell2':>11} {'width_c':>7} {'hgt_c':>6} {'seamC':>6} {'satArea%':>8} {'hue_med':>7} {'S_med':>6} {'L_med':>6} {'cx':>5} {'cy':>5}")
prev = None
for fi in range(12, 46):
    a = load(f"{D}/f{fi:04d}.jpg")
    L, S, H = lum(a), sat(a), hue(a)
    diff = np.abs(a - plate).max(axis=2)
    m = ndimage.binary_opening(diff > 18, np.ones((3, 3), bool))
    if m.sum() < 200:
        continue
    lab, n = ndimage.label(ndimage.binary_closing(m, np.ones((9, 9), bool)))
    sz = ndimage.sum(m, lab, range(1, n + 1))
    dom = lab == (int(np.argmax(sz)) + 1)
    ys, xs = np.nonzero(dom)
    cx, cy = xs.mean(), ys.mean()
    w = (xs.max() - xs.min()) / CELLW
    h = (ys.max() - ys.min()) / CELLH
    sc, _ = seam_contrast(L, dom)
    satm = (S >= 0.50) & dom
    hm = H[satm]
    print(
        f"{fi:3d} {(fi-12)/30:+7.3f} {dom.sum()/(CELLW*CELLH):11.3f} {w:7.2f} {h:6.2f} "
        f"{(sc if sc else -1):6.1f} {100*satm.sum()/(1024*1024):8.3f} "
        f"{(np.median(hm) if hm.size else -1):7.1f} {(np.median(S[satm]) if hm.size else -1):6.2f} "
        f"{(np.median(L[satm]) if hm.size else -1):6.0f} {cx:5.0f} {cy:5.0f}"
    )

# --- frozen-tail check over the plateau ---
print("\n--- tail stability +0.60 .. +0.87 ---")
prev = None
for fi in range(30, 39):
    a = load(f"{D}/f{fi:04d}.jpg")
    if prev is not None:
        d = np.abs(a - prev)
        print(f" f{fi:04d} vs f{fi-1:04d}: maxdiff={d.max():5.1f} meandiff={d.mean():.4f} changed>4={int((d.max(axis=2)>4).sum())}")
    prev = a
