import numpy as np
from PIL import Image
import os

B = "Captures/AbilityJuice"


def load(p):
    return np.asarray(Image.open(p).convert("RGB")).astype(np.float32)


def panels(img):
    """Split a 3-panel strip on its black dividers. Returns list of (x0,x1)."""
    h, w, _ = img.shape
    col = img.mean(axis=(0, 2))
    dark = col < 30
    runs = []
    i = 0
    while i < w:
        if dark[i]:
            j = i
            while j < w and dark[j]:
                j += 1
            if j - i >= 2:
                runs.append((i, j))
            i = j
        else:
            i += 1
    return runs, w, h


for name, p in [
    ("r4 debris", f"{B}/strips/r4/debris.jpg"),
    ("r3 debris", f"{B}/strips/r3/debris.jpg"),
    ("ref 19", f"{B}/strips/reference/cm-8newabilities-19.jpg"),
]:
    img = load(p)
    runs, w, h = panels(img)
    print(f"{name}: {w}x{h} dividers={runs}")

# Cell pitch on the debris strip panel 1 (clean board) via column autocorrelation
img = load(f"{B}/strips/r4/debris.jpg")
h, w, _ = img.shape
p1 = img[:, 0:340].mean(axis=2)
# use vertical seams: gradient along x, summed over y
g = np.abs(np.diff(p1, axis=1)).sum(axis=0)
g = g - g.mean()
ac = np.correlate(g, g, mode="full")[len(g) - 1 :]
ac /= ac[0]
best = np.argsort(ac[40:200])[::-1][:6] + 40
print("panel1 autocorr peaks (px):", sorted(best.tolist()), [round(float(ac[b]), 3) for b in sorted(best)])

# Full-res frame cell pitch
f = load(f"{B}/shots/r4/debris/f0000.jpg")
print("full frame:", f.shape)
gm = f.mean(axis=2)
g2 = np.abs(np.diff(gm, axis=1)).sum(axis=0)
g2 = g2 - g2.mean()
ac2 = np.correlate(g2, g2, mode="full")[len(g2) - 1 :]
ac2 /= ac2[0]
best2 = np.argsort(ac2[80:400])[::-1][:8] + 80
print("fullres autocorr peaks (px):", sorted(best2.tolist()))
print("board floor luma stats:", float(gm.mean()), float(np.median(gm)))
