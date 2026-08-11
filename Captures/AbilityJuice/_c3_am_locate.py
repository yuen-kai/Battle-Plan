"""Step 2 - cross-check px/cell against round-1's published numbers, and find which
raw frames the r2 strip panels were cut from."""
import numpy as np, glob, os
from PIL import Image

PC = 218.0  # px per cell, from grid autocorrelation

def frames(round_, shot="aftermath"):
    return sorted(glob.glob(f"Captures/AbilityJuice/shots/{round_}/{shot}/f*.jpg"))

def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float64)

def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]

# ---------- cross-check: reproduce round-1's "2.68 x 2.53 cells" -----------
F1 = frames("r1")
L1_0 = lum(load(F1[0]))
best = None
for i in range(len(F1)):
    L = lum(load(F1[i]))
    d = np.abs(L - L1_0)
    m = d > 12
    if m.sum() < 200:
        continue
    ys, xs = np.nonzero(m)
    h = (ys.max()-ys.min()+1)/PC; w = (xs.max()-xs.min()+1)/PC
    if best is None or w*h > best[1]*best[2]:
        best = (i, w, h)
print(f"[cross-check] r1 max plume bbox = {best[1]:.2f} x {best[2]:.2f} cells at frame {best[0]}"
      f"   (round-1 critic published 2.68 x 2.53)")

# ---------- which frames did the r2 strip use? ----------------------------
strip = np.asarray(Image.open("Captures/AbilityJuice/strips/r2/aftermath.jpg").convert("RGB")).astype(np.float64)
sh, sw, _ = strip.shape
pw = sw // 3
print(f"[strip] {sw}x{sh}, panel {pw}x{sh}")

F2 = frames("r2")
small = []
for p in F2:
    im = Image.open(p).convert("RGB").resize((pw, sh), Image.LANCZOS)
    small.append(np.asarray(im).astype(np.float64))

picks = []
for k in range(3):
    panel = strip[:, k*pw:(k+1)*pw, :]
    errs = [np.abs(s - panel).mean() for s in small]
    j = int(np.argmin(errs))
    picks.append(j)
    print(f"  panel {k+1} -> frame {j}  t={-0.40+j/30:+.3f}s   mae={errs[j]:.2f}"
          f"  (next best {sorted(errs)[1]:.2f})")
np.save("Captures/AbilityJuice/_c3_am_picks.npy", np.array(picks))
