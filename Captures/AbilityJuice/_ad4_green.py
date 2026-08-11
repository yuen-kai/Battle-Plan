import numpy as np
from PIL import Image
from scipy import ndimage

B = "Captures/AbilityJuice"
a = np.asarray(Image.open(f"{B}/shots/r4/debris/f0033.jpg").convert("RGB")).astype(np.float32)
sub = a[400:620, 390:640]
r, g, b = sub[..., 0], sub[..., 1], sub[..., 2]
mx, mn = sub.max(axis=2), sub.min(axis=2)
S = np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0)
green = (g > r + 12) & (g > b + 12) & (S > 0.25)
lab, n = ndimage.label(green)
sz = ndimage.sum(green, lab, range(1, n + 1))
print(f"green pixels in scorch crop: {int(green.sum())} in {n} blobs")
print(f"blob sizes: max={sz.max():.0f} median={np.median(sz):.0f} n>=20px={(sz>=20).sum()} n>=50px={(sz>=50).sum()}")
big = sz >= 50
print(f"pixels in blobs >=50px: {int(sz[big].sum())} ({100*sz[big].sum()/max(green.sum(),1):.0f}% of green)")
ys, xs = np.nonzero(lab == (int(np.argmax(sz)) + 1))
print("sample RGB of the largest green blob:")
for k in range(0, min(len(ys), 60), 12):
    print("   ", sub[ys[k], xs[k]].astype(int).tolist())
print("mean RGB of all green px:", sub[green].mean(axis=0).round(0).tolist())
print("mean RGB of orange px  :", sub[(r > g + 25) & (S > 0.3)].mean(axis=0).round(0).tolist())

# is the same green present in the ORIGINAL 1024 capture of an earlier frame (i.e. not a late artefact)?
for fi in (16, 20, 24, 28, 33, 37):
    im = np.asarray(Image.open(f"{B}/shots/r4/debris/f{fi:04d}.jpg").convert("RGB")).astype(np.float32)
    s = im[400:620, 390:640]
    rr, gg, bb = s[..., 0], s[..., 1], s[..., 2]
    m2 = (gg > rr + 12) & (gg > bb + 12)
    print(f" f{fi:04d}: green px = {int(m2.sum())}")
