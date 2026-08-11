import numpy as np
from PIL import Image
from scipy import ndimage

B = "Captures/AbilityJuice"
D = f"{B}/shots/r4/debris"
CW, CH = 201.0, 193.0


def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32)


def lum(a):
    return 0.299 * a[..., 0] + 0.587 * a[..., 1] + 0.114 * a[..., 2]


plate = load(f"{D}/f0000.jpg")
pL = lum(plate)

# --- seam contrast, peak-to-trough method (the one that yields ~39 on clean floor) ---
# find seam troughs along scanlines: local minima with a shoulder either side
def seam_ptt(L, region, halfw=7):
    vals = []
    ys, xs = np.nonzero(region)
    if len(ys) == 0:
        return None, 0
    y0, y1, x0, x1 = ys.min(), ys.max(), xs.min(), xs.max()
    for y in range(y0, y1):
        row = L[y]
        for x in range(max(x0, halfw), min(x1, L.shape[1] - halfw)):
            if not region[y, x]:
                continue
            c = row[x]
            l = row[x - halfw : x].max()
            r = row[x + 1 : x + 1 + halfw].max()
            if c < row[x - 1] and c <= row[x + 1]:
                vals.append(min(l, r) - c)
    return (float(np.mean(vals)) if vals else None), len(vals)


ctrl = np.zeros(pL.shape, bool)
ctrl[150:850, 80:280] = True
c, n = seam_ptt(pL, ctrl)
print(f"clean floor seam peak-to-trough: {c:.1f} over {n} minima  (round-1 quoted ~39)")

for fi in (28, 33):
    a = load(f"{D}/f{fi:04d}.jpg")
    L = lum(a)
    diff = np.abs(a - plate).max(axis=2)
    mark = ndimage.binary_opening((diff > 18) & (L > 90) & (pL > 150), np.ones((3, 3), bool))
    mark = ndimage.binary_closing(mark, np.ones((7, 7), bool))
    lab, k = ndimage.label(mark)
    sz = ndimage.sum(mark, lab, range(1, k + 1))
    dom = ndimage.binary_erosion(lab == (int(np.argmax(sz)) + 1), np.ones((5, 5), bool))
    v, nn = seam_ptt(L, dom)
    print(f" f{fi:04d} t={(fi-12)/30:+.3f}: seam peak-to-trough under mark = {v:.1f} over {nn}"
          f"   ratio vs clean = {v/c:.2f}   (builder claim 19-23 on a 39-scale => ratio 0.49-0.59)")

# --- scorch extent with a loose threshold (include the soft outer wash) ---
print("\nscorch extent, loose mask (diff>8, on floor, not a dark chunk):")
for fi in (20, 28, 33, 37):
    a = load(f"{D}/f{fi:04d}.jpg")
    L = lum(a)
    diff = np.abs(a - plate).max(axis=2)
    m = (diff > 8) & (L > 100) & (pL > 150)
    m = ndimage.binary_closing(ndimage.binary_opening(m, np.ones((3, 3), bool)), np.ones((11, 11), bool))
    lab, k = ndimage.label(m)
    sz = ndimage.sum(m, lab, range(1, k + 1))
    dom = lab == (int(np.argmax(sz)) + 1)
    ys, xs = np.nonzero(dom)
    print(f" f{fi:04d} t={(fi-12)/30:+.3f}: {(xs.max()-xs.min())/CW:.2f} x {(ys.max()-ys.min())/CH:.2f} cells,"
          f" area {dom.sum()/(CW*CH):.2f} cell^2  (ask: 1.5-2 cells wide)")

# --- final headline numbers, restated ---
print("\n=== HEADLINE, money frame f0015 (+0.100s), 1024px capture ===")
a = load(f"{D}/f0015.jpg")
L = lum(a)
mx, mn = a.max(axis=2), a.min(axis=2)
S = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0)
ch = np.abs(a - plate).max(axis=2) > 18
for nm, m in [
    ("S>=.50 & L>=120  (round-3 test)", (S >= 0.50) & (L >= 120)),
    ("S>.45 & L>199    (owner test)", (S > 0.45) & (L > 199)),
    ("S>=.50 & |L-182|>=40 & changed  (board-relative)", (S >= 0.50) & (np.abs(L - 182) >= 40) & ch),
    ("S>=.50 & changed (no L gate)", (S >= 0.50) & ch),
    ("occluding (plate-L>60)", (pL - L) > 60),
]:
    print(f" {nm:52s} {m.sum()/(CW*CH):6.3f} cell^2   {100*m.sum()/(1024*1024):6.3f}% of frame")
