"""Look at what is actually happening in r2 f10..f18 — diff maps + tile pitch."""
import numpy as np
from PIL import Image
import os

AJ = "/Users/cykai/Battle-Plan/Captures/AbilityJuice"
D2 = os.path.join(AJ, "shots/r2/camera")


def L(i):
    return np.asarray(Image.open(os.path.join(D2, f"f{i:04d}.jpg")).convert("L"),
                      dtype=np.float32)


ref = L(2)

# ---- tile pitch, so we know the phase-correlation aliasing distance ----
row = ref[300, :]
f = np.abs(np.fft.rfft(row - row.mean()))
k = np.argmax(f[3:120]) + 3
print(f"dominant horizontal period in a clean row: {1024/k:.1f} px  (bin {k})")
col = ref[:, 700]
f2 = np.abs(np.fft.rfft(col - col.mean()))
k2 = np.argmax(f2[3:120]) + 3
print(f"dominant vertical   period in a clean col: {1024/k2:.1f} px  (bin {k2})")

# ---- where does the frame actually change? ----
print("\nabs-diff vs pre-roll f0002, by region (mean |Δ| luminance):")
print(f"{'f':>4} {'t(ms)':>7} | {'whole':>7} {'centre':>7} {'ring':>7} | "
      f"{'>25 px count':>12} {'centroid of change':>22}")
for i in list(range(10, 22)) + [27]:
    d = np.abs(L(i) - ref)
    m_c = np.zeros_like(d, bool); m_c[300:724, 300:724] = True
    m_r = ~m_c
    big = d > 25
    if big.sum() > 50:
        ys, xs = np.nonzero(big)
        cen = f"({xs.mean():.0f},{ys.mean():.0f}) sd=({xs.std():.0f},{ys.std():.0f})"
    else:
        cen = "-"
    print(f"{i:>4} {(i-12)*33.333:>7.1f} | {d.mean():7.3f} {d[m_c].mean():7.3f} "
          f"{d[m_r].mean():7.3f} | {big.sum():>12} {cen:>22}")

# ---- montage: f10..f18 with diff underneath ----
sel = list(range(10, 19))
tiles = []
for i in sel:
    a = L(i)
    d = np.clip(np.abs(a - ref) * 3, 0, 255)
    tiles.append(np.vstack([a, d]))
mont = np.hstack([np.asarray(Image.fromarray(t.astype(np.uint8)).resize((256, 512)))
                  for t in tiles])
Image.fromarray(mont).save(os.path.join(AJ, "_r2_cam_diff.png"))
print(f"\nwrote _r2_cam_diff.png  (top row f{sel[0]}..f{sel[-1]}, bottom = 3x abs diff vs f0002)")
