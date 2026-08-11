import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage
from _c4_wu_setup import load, amber, t_of

CX, CY = 512.6, 549.0
base = load(0)
Lb = 0.2126 * base[..., 0] + 0.7152 * base[..., 1] + 0.0722 * base[..., 2]
H, W = 1024, 1024
yy, xx = np.mgrid[0:H, 0:W]
R = np.sqrt((xx - CX) ** 2 + (yy - CY) ** 2)
ANG = (((np.arctan2(yy - CY, xx - CX) + np.pi) / (2 * np.pi)) * 120).astype(np.int16) % 120

# the ring is a dark line ON the tan plate; find it by looking for a radial luminance
# trough restricted to plate pixels, using a proper 1-D profile at 1px resolution
print('  i     t     r_px  cells   trough  angcov%  ringL  fwhm_px  hardest_step')
res = []
for i in range(20, 47):
    a = load(i)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    prof = np.full(430, np.nan)
    for r0 in range(6, 430):
        sel = (np.abs(R - r0) < 0.6) & (Lb > 120)
        if sel.sum() > 25:
            prof[r0] = np.percentile(L[sel], 12)
    sm = ndimage.uniform_filter1d(np.nan_to_num(prof, nan=120.0), 31)
    tr = prof - sm
    tr[:8] = 0
    tr[420:] = 0
    r0 = int(np.nanargmin(tr))
    ann = (np.abs(R - r0) <= 3) & (L < sm[r0] - 15) & (Lb > 120)
    cov = len(np.unique(ANG[ann])) / 120 * 100 if ann.any() else 0
    half = sm[r0] + tr[r0] / 2
    lo = hi = r0
    while lo > 9 and prof[lo - 1] < half:
        lo -= 1
    while hi < 419 and prof[hi + 1] < half:
        hi += 1
    step = np.nanmax(np.abs(np.diff(prof[max(0, r0 - 10):r0 + 11])))
    print(f'{i:3d} {t_of(i):+.3f}  {r0:5d} {r0/218:5.2f}  {tr[r0]:+7.1f}  {cov:5.1f}  '
          f'{np.median(L[ann]) if ann.any() else float("nan"):6.1f}  {hi-lo+1:5d}   {step:5.1f}/px')
    res.append((t_of(i), r0, tr[r0], cov))

res = np.array(res)
np.save('_c4_wu_collapse.npy', res)

# super-zoom of the last 10 frames
def z(i):
    im = Image.open(f'shots/r3/windup/f{i:04d}.jpg').convert('RGB')
    c = im.crop((392, 429, 632, 669)).resize((260, 260), Image.NEAREST)
    d = ImageDraw.Draw(c)
    d.rectangle([0, 0, 259, 15], fill=(0, 0, 0))
    d.text((3, 3), f'{t_of(i):+.3f}s', fill=(255, 255, 0))
    return np.asarray(c)

rows = [[33, 35, 37, 39], [40, 41, 42, 43], [44, 45, 46, 47]]
Image.fromarray(np.vstack([np.hstack([z(i) for i in r]) for r in rows])).save('_c4_wu_collapse.png')
print('saved collapse zoom')
