"""Edge ramp: for each row, how many pixels between a clean-floor pixel (L>165) and a
body pixel (L<90)?  Round 2 was rejected because this went from 1 px to 26 px."""
import numpy as np
from PIL import Image

P3 = 'Captures/AbilityJuice/shots/r3/hitreact/f%04d.jpg'
P2 = 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


def measure(p, f, label, y0=430, y1=560, x0=380, x1=900):
    L = lum(np.asarray(Image.open(p % f).convert('RGB')).astype(np.float32))
    widths, steps, nbody, nrow = [], [], 0, 0
    for y in range(y0, y1, 3):
        row = L[y, x0:x1]
        nrow += 1
        body = np.where(row < 90)[0]
        if not len(body):
            continue
        nbody += 1
        for edge, direction in ((body.min(), -1), (body.max(), +1)):
            k = edge
            n = 0
            while 0 <= k + direction < len(row) and row[k] <= 165 and n < 60:
                k += direction
                n += 1
            if row[k] > 165:
                widths.append(n)
                seg = row[min(edge, k):max(edge, k)+1]
                if len(seg) > 1:
                    steps.append(np.abs(np.diff(seg)).max())
    if not widths:
        print(f'{label:28s}  NO BODY PIXEL BELOW L90 ANYWHERE  ({nbody}/{nrow} rows had body)')
        return
    print(f'{label:28s}  rows with body {nbody:2d}/{nrow}   ramp px: med {np.median(widths):5.1f} '
          f'p90 {np.percentile(widths,90):5.1f} max {max(widths):3d}   '
          f'steepest step within ramp: med {np.median(steps):5.0f} L/px')


print('=' * 104)
print('FLOOR -> BODY RAMP  (rows through the unit, both sides)')
print('=' * 104)
measure(P3, 8, 'rest (pink unit)')
print()
for f in (12, 13, 14):
    measure(P2, f, f'R2 f{f} t={(f-12)/30:+.3f}')
print()
for f in (12, 13, 14, 15, 16):
    measure(P3, f, f'R3 f{f} t={(f-12)/30:+.3f}')

print()
print('=' * 104)
print('DARKEST PIXEL ANYWHERE ON THE UNIT (excluding board obstacles: rows 380-620)')
print('=' * 104)
for tag, p, frames in (('r2', P2, [8, 12, 13, 14]), ('r3', P3, [8, 12, 13, 14, 15, 16])):
    for f in frames:
        L = lum(np.asarray(Image.open(p % f).convert('RGB')).astype(np.float32))
        sub = L[380:620, 380:900]
        print(f'{tag} f{f} t={(f-12)/30:+.3f}: min L = {sub.min():5.1f}   '
              f'n(L<30) = {int((sub<30).sum()):6d}   n(L<60) = {int((sub<60).sum()):6d}   '
              f'n(L>240) = {int((sub>240).sum()):6d}')
    print()
