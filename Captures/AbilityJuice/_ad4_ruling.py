import numpy as np
from PIL import Image

B = "Captures/AbilityJuice"


def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32)


def split(img):
    return [img[:, 0:512], img[:, 520:1032], img[:, 1040:1552]]


def lum(a):
    return 0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]


def sat(a):
    mx, mn = a.max(axis=2), a.min(axis=2)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


print("Board floor L (mode of panel-1 luminance) vs luminance of S>=0.5 material\n")
print(f"{'strip':24s} {'boardL':>7} {'satPix':>7} {'satL_med':>9} {'satL_p10':>9} {'satL_p90':>9} {'dL_med':>7} {'%satL<board':>11}")
for n, p in [
    ("OURS r4 debris p2", f"{B}/strips/r4/debris.jpg"),
    ("REF 8new-19", f"{B}/strips/reference/cm-8newabilities-19.jpg"),
    ("REF 8new-20", f"{B}/strips/reference/cm-8newabilities-20.jpg"),
    ("REF every-20", f"{B}/strips/reference/cm-everyability-20.jpg"),
    ("REF every-27", f"{B}/strips/reference/cm-everyability-27.jpg"),
    ("REF every-23", f"{B}/strips/reference/cm-everyability-23.jpg"),
    ("REF every-30", f"{B}/strips/reference/cm-everyability-30.jpg"),
    ("REF season2-05", f"{B}/strips/reference/cm-season2-05.jpg"),
]:
    ps = split(load(p))
    p1, p2, p3 = ps
    # board floor = modal luminance of the least-eventful panel
    Ls = [lum(x) for x in ps]
    quiet = int(np.argmin([sat(x).mean() for x in ps]))
    hist, edges = np.histogram(Ls[quiet], bins=64, range=(0, 256))
    board = float(edges[np.argmax(hist)] + 2)
    # material = saturated pixels in the busiest panel
    busy = int(np.argmax([((sat(x) >= 0.5)).sum() for x in ps]))
    S, L = sat(ps[busy]), Ls[busy]
    m = S >= 0.5
    if m.sum() < 100:
        print(f"{n:24s} {board:7.0f}  (too few)")
        continue
    sl = L[m]
    print(
        f"{n:24s} {board:7.0f} {m.sum():7d} {np.median(sl):9.1f} {np.percentile(sl,10):9.1f} "
        f"{np.percentile(sl,90):9.1f} {np.median(sl)-board:+7.1f} {100*(sl<board).mean():10.1f}%"
    )

print("\n--- OURS, money frame f15, material-side ratio (independent of any L floor) ---")
plate = load(f"{B}/shots/r4/debris/f0000.jpg")
a = load(f"{B}/shots/r4/debris/f0015.jpg")
pL, L, S = lum(plate), lum(a), sat(a)
CELL = 201.0 * 193.0
occl = (pL - L) > 60
eff = np.abs(a - plate).max(axis=2) > 18
print(f" occluding material (plate-L>60): {occl.sum()/CELL:.3f} cell^2")
print(f" all changed material:            {eff.sum()/CELL:.3f} cell^2")
for th in (0.35, 0.45, 0.50, 0.60):
    c = (S >= th) & eff
    print(
        f" S>={th:.2f} & changed: {c.sum()/CELL:6.3f} cell^2   "
        f"of which also occluding: {(c&occl).sum()/CELL:6.3f}   "
        f"grey:colour over changed = {(eff.sum()-c.sum())/max(c.sum(),1):.2f}:1"
    )
sl = L[(S >= 0.5) & eff]
print(f" our saturated material L: med={np.median(sl):.1f} p10={np.percentile(sl,10):.1f} p90={np.percentile(sl,90):.1f}; board=184")
print(f" fraction of our saturated material darker than board: {100*(sl<184).mean():.1f}%")

print("\n--- aftermath frame f28, same ---")
a = load(f"{B}/shots/r4/debris/f0028.jpg")
L, S = lum(a), sat(a)
eff = np.abs(a - plate).max(axis=2) > 18
occl = (pL - L) > 60
c = (S >= 0.5) & eff
print(f" changed={eff.sum()/CELL:.3f} cell^2  occluding={occl.sum()/CELL:.3f}  S>=.5={c.sum()/CELL:.3f}")
sl = L[c]
print(f" saturated L: med={np.median(sl):.1f} p10={np.percentile(sl,10):.1f} p90={np.percentile(sl,90):.1f}")
