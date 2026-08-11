"""Round-3 critic: aftermath primitive. Step 1 - geometry, floor baseline, grid pitch."""
import numpy as np, glob, os
from PIL import Image

DIR = "Captures/AbilityJuice/shots/r2/aftermath"
FR = sorted(glob.glob(os.path.join(DIR, "f*.jpg")))
print(f"frames: {len(FR)}")

def load(i):
    return np.asarray(Image.open(FR[i]).convert("RGB")).astype(np.float64)

def lum(a):  # Rec.709 on sRGB bytes, matching prior rounds' "luminance out of 255"
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]

a0 = load(0)
H, W, _ = a0.shape
print(f"size: {W}x{H}")
FPS = 30.0
T0 = -0.40
def t(i): return T0 + i/FPS
print(f"t[0]={t(0):+.3f}  t[{len(FR)-1}]={t(len(FR)-1):+.3f}")

L0 = lum(a0)

# --- locate the tile grid pitch from a clean frame ------------------------
# Seams are darker lines. Use a horizontal band in the lower half (clean floor,
# no props) and autocorrelate the column-mean profile.
band = L0[int(H*0.72):int(H*0.80), :]
prof = band.mean(axis=0)
prof = prof - prof.mean()
ac = np.correlate(prof, prof, mode="full")[len(prof)-1:]
ac /= ac[0]
# first strong peak after lag 40
cand = [k for k in range(40, min(500, len(ac)-1)) if ac[k] > ac[k-1] and ac[k] >= ac[k+1]]
cand.sort(key=lambda k: -ac[k])
print("top autocorr lags:", [(k, round(float(ac[k]), 3)) for k in cand[:6]])

# --- floor statistics on clean frame -------------------------------------
# sample flat floor regions away from props
print(f"\nframe0 global luminance: mean {L0.mean():.1f}  p50 {np.percentile(L0,50):.1f}  "
      f"p90 {np.percentile(L0,90):.1f}  max {L0.max():.1f}")
np.save("Captures/AbilityJuice/_c3_am_L0.npy", L0)
np.save("Captures/AbilityJuice/_c3_am_a0.npy", a0)
