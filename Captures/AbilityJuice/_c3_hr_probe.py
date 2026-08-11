import numpy as np
from PIL import Image

P = 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'
a = np.asarray(Image.open(P % 8).convert('RGB')).astype(np.float32)
r, g, b = a[..., 0], a[..., 1], a[..., 2]
L = 0.2126*r + 0.7152*g + 0.0722*b


def stat(name, y0, y1, x0, x1):
    sub = a[y0:y1, x0:x1]
    ll = L[y0:y1, x0:x1]
    print(f'{name:18s} RGB={sub.reshape(-1,3).mean(0).round(1)} L={ll.mean():6.1f} '
          f'Lmin={ll.min():5.1f} Lmax={ll.max():5.1f} R-B={(sub[...,0]-sub[...,2]).mean():+6.1f}')


stat('floor left', 250, 300, 100, 200)
stat('floor below unit', 650, 720, 450, 600)
stat('floor right', 250, 300, 800, 900)
stat('obstacle TL', 30, 80, 180, 300)
stat('obstacle TR', 60, 110, 720, 850)
stat('unit body', 470, 500, 510, 560)
stat('unit rim top', 415, 425, 520, 560)
stat('gun barrel', 500, 512, 600, 640)

# whole-frame colour classes
print()
print('--- rest frame class counts (1024x1024) ---')
rb = r - b
print('R-B > 10 :', int((rb > 10).sum()))
print('L < 100 & |R-B|<20 :', int(((L < 100) & (np.abs(rb) < 20)).sum()))
print('L < 100 & R-B<=-20 :', int(((L < 100) & (rb <= -20)).sum()))
print('L<100 total:', int((L < 100).sum()))
# histogram of L in the strip crop region (the square the strip actually cuts)
side = min(a.shape[:2])
print('strip crop = full square', side)
