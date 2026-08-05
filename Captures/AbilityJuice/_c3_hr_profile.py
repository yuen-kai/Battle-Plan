import numpy as np
from PIL import Image, ImageDraw

P2 = 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'
P1 = 'Captures/AbilityJuice/shots/r1/hitreact/f%04d.jpg'


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


def rowprofile(path, f, y, x0, x1):
    a = np.asarray(Image.open(path % f).convert('RGB')).astype(np.float32)
    return lum(a)[y, x0:x1], a[y, x0:x1]


def summarise(path, f, y, x0, x1, tag):
    L, rgb = rowprofile(path, f, y, x0, x1)
    floor = np.median(np.concatenate([L[:12], L[-12:]]))
    print(f'\n### {tag} f{f} row y={y}  floor L={floor:.1f}')
    # print a compact run-length encoding at 4-level granularity
    runs = []
    for i, v in enumerate(L):
        q = int(round(v/8.0))
        if runs and runs[-1][0] == q:
            runs[-1][2] = i
        else:
            runs.append([q, i, i])
    for q, i0, i1 in runs:
        if i1-i0 < 1:
            continue
        px = rgb[(i0+i1)//2]
        mx, mn = px.max(), px.min()
        sat = (mx-mn)/max(mx, 1e-6)
        print(f'  x {x0+i0:4d}-{x0+i1:4d} ({i1-i0+1:3d}px) L~{q*8:4d}  RGB=({px[0]:3.0f},{px[1]:3.0f},{px[2]:3.0f}) S={sat:.2f}  d_floor={q*8-floor:+6.1f}')
    print(f'  darkest L={L.min():.1f}  brightest L={L.max():.1f}  n(L<100)={int((L<100).sum())} n(L>240)={int((L>240).sum())}')


# rest: unit centre row (through the disc, below the health bar)
summarise(P2, 8, 505, 440, 680, 'R2 REST')
summarise(P2, 13, 505, 600, 840, 'R2 FLASH (strip panel 2)')
summarise(P2, 12, 500, 560, 800, 'R2 FLASH first frame')
summarise(P2, 26, 505, 440, 680, 'R2 RECOVERY (strip panel 3)')
summarise(P1, 13, 505, 560, 800, 'R1 FLASH (last round)')
