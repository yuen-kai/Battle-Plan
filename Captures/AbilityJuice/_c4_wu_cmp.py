import numpy as np
from PIL import Image
import glob

def stats(im):
    a = np.asarray(im.convert('RGB')).astype(np.float32)
    L = 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
    mx, mn = a.max(2), a.min(2)
    sat = (mx - mn) / np.maximum(mx, 1)
    med = np.median(L)
    return dict(
        Lmed=med,
        p99=np.percentile(L, 99),
        p01=np.percentile(L, 1),
        bright_sat=np.mean((L > 200) & (sat > 0.45)) * 100,
        clipped=np.mean(mn >= 250) * 100,
        far_from_med=np.mean(np.abs(L - med) > 60) * 100,
        satmean=sat.mean(),
    )


def p1(path):
    im = Image.open(path)
    w = im.width // 3 if im.width % 3 == 0 else 512
    return im.crop((0, 0, w if im.width % 3 == 0 else 512, im.height))


print(f'{"strip":34s} {"Lmed":>6} {"p01":>6} {"p99":>6} {"bright&sat%":>11} '
      f'{"clip%":>7} {"|L-med|>60 %":>13} {"satmean":>8}')
rows = [('OURS r3 windup p1', 'strips/r3/windup.jpg'), ('OURS r2 windup p1', 'strips/r2/windup.jpg')]
rows += [(f'CM {p.split("/")[-1][:-4]}', p) for p in sorted(glob.glob('strips/reference/*.jpg'))]
cm = []
for name, path in rows:
    s = stats(p1(path))
    print(f'{name:34s} {s["Lmed"]:6.0f} {s["p01"]:6.0f} {s["p99"]:6.0f} {s["bright_sat"]:11.2f} '
          f'{s["clipped"]:7.2f} {s["far_from_med"]:13.1f} {s["satmean"]:8.3f}')
    if name.startswith('CM'):
        cm.append(s)

print('\nClash Mini wind-up panel medians across 17 strips:')
for k in ['bright_sat', 'clipped', 'far_from_med', 'satmean']:
    v = [c[k] for c in cm]
    print(f'  {k:14s} median {np.median(v):7.2f}   min {min(v):7.2f}  max {max(v):7.2f}')
