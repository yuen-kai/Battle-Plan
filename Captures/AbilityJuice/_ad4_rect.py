import numpy as np
from PIL import Image
from scipy import ndimage

B = "Captures/AbilityJuice"
plate = np.asarray(Image.open(f"{B}/shots/r4/debris/f0000.jpg").convert("RGB")).astype(np.float32)
a = np.asarray(Image.open(f"{B}/shots/r4/debris/f0033.jpg").convert("RGB")).astype(np.float32)
ratio = a / np.maximum(plate, 1)

print("horizontal scan at row 500, cols 400..430 and 595..625  (ratio R,G,B)")
for x in list(range(408, 428)) + [None] + list(range(600, 620)):
    if x is None:
        print("   ...")
        continue
    print(f"  x={x}: {ratio[500, x].round(3).tolist()}   rgb={a[500,x].astype(int).tolist()}")

print("\nvertical scan at col 500, rows 412..436")
for y in range(412, 436):
    print(f"  y={y}: {ratio[y, 500].round(3).tolist()}   rgb={a[y,500].astype(int).tolist()}")

# ---------- threshold reconciliation ----------
print("\n\n===== THRESHOLD RECONCILIATION =====")


def split(p):
    im = np.asarray(Image.open(p).convert("RGB")).astype(np.float32)
    return [im[:, 0:512], im[:, 520:1032], im[:, 1040:1552]]


def lum(x):
    return 0.299 * x[..., 0] + 0.587 * x[..., 1] + 0.114 * x[..., 2]


def sat(x):
    mx, mn = x.max(axis=2), x.min(axis=2)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0)


print(f"{'strip':22s} {'playfieldL':>10} {'satMat_medL':>11} {'delta':>6} {'%satMat_L>=120':>14} {'%satMat_|dL|>=40':>16}")
rows = [
    ("OURS r4 debris", f"{B}/strips/r4/debris.jpg"),
    ("REF 8new-19", f"{B}/strips/reference/cm-8newabilities-19.jpg"),
    ("REF 8new-20", f"{B}/strips/reference/cm-8newabilities-20.jpg"),
    ("REF every-20", f"{B}/strips/reference/cm-everyability-20.jpg"),
    ("REF every-27", f"{B}/strips/reference/cm-everyability-27.jpg"),
    ("REF every-30", f"{B}/strips/reference/cm-everyability-30.jpg"),
    ("REF every-23", f"{B}/strips/reference/cm-everyability-23.jpg"),
]
for n, p in rows:
    ps = split(p)
    st = np.stack(ps)
    spread = (st.max(axis=0) - st.min(axis=0)).max(axis=2)
    static = spread < 14
    # play-field board level: modal luminance of static pixels
    Lq = lum(ps[0])[static]
    h, e = np.histogram(Lq, bins=64, range=(0, 256))
    board = float(e[np.argmax(h)] + 2)
    # saturated effect material across the two event panels
    sl = []
    for pn in ps[1:]:
        L, S = lum(pn), sat(pn)
        m = (S >= 0.50) & (~static)
        sl.append(L[m])
    sl = np.concatenate([x for x in sl if x.size])
    if sl.size < 200:
        print(f"{n:22s} {board:10.0f}  (too few)")
        continue
    print(
        f"{n:22s} {board:10.0f} {np.median(sl):11.1f} {np.median(sl)-board:+6.0f} "
        f"{100*(sl>=120).mean():13.1f}% {100*(np.abs(sl-board)>=40).mean():15.1f}%"
    )
