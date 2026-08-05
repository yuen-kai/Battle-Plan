import sys, glob, json
import numpy as np
from PIL import Image
sys.path.insert(0, "Captures/AbilityJuice")
from _ad5_lib import *


def frames(shot, rnd="r5"):
    return sorted(glob.glob(f"Captures/AbilityJuice/shots/{rnd}/{shot}/f*.jpg"))


f = load(frames("death")[18])
# death mass sits around (510,540) per earlier centroid
crop = f[380:760, 300:680]
big = np.asarray(Image.fromarray(crop.astype(np.uint8)).resize((760, 760), Image.NEAREST))
Image.fromarray(big).save("Captures/AbilityJuice/_ad5_death_zoom.jpg", quality=95)

# tight crop on the flat interior to expose any tiling
tight = f[470:610, 380:520]
big2 = np.asarray(Image.fromarray(tight.astype(np.uint8)).resize((700, 700), Image.NEAREST))
Image.fromarray(big2).save("Captures/AbilityJuice/_ad5_death_tex.jpg", quality=95)

# FFT of the interior luminance to detect a periodic lattice
L = lum(f[470:610, 380:520])
L = L - L.mean()
F = np.abs(np.fft.fftshift(np.fft.fft2(L * np.hanning(L.shape[0])[:, None] * np.hanning(L.shape[1])[None, :])))
c = np.array(F.shape) // 2
F[c[0]-2:c[0]+3, c[1]-2:c[1]+3] = 0
pk = np.unravel_index(F.argmax(), F.shape)
fy, fx = pk[0] - c[0], pk[1] - c[1]
period = L.shape[0] / max(1e-6, np.hypot(fy, fx))
print(f"death interior FFT: strongest non-DC peak at ({fy},{fx}) -> period {period:.1f}px "
      f"({period/218:.3f} cells);  peak/mean energy ratio {F.max()/F.mean():.1f}")
print("  (ratio >40 with a clean single peak indicates a regular repeating lattice)")

# same test on r4 for comparison
f4 = load(frames("death", "r4")[16])
L4 = lum(f4[470:610, 380:520]); L4 = L4 - L4.mean()
F4 = np.abs(np.fft.fftshift(np.fft.fft2(L4 * np.hanning(L4.shape[0])[:,None] * np.hanning(L4.shape[1])[None,:])))
F4[c[0]-2:c[0]+3, c[1]-2:c[1]+3] = 0
print(f"r4 same window: peak/mean energy ratio {F4.max()/F4.mean():.1f}")
print("wrote _ad5_death_zoom.jpg, _ad5_death_tex.jpg")
