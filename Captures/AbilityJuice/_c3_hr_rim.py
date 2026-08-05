import numpy as np
from PIL import Image

P2 = 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'
a8 = np.asarray(Image.open(P2 % 8).convert('RGB')).astype(np.float32)
a13 = np.asarray(Image.open(P2 % 13).convert('RGB')).astype(np.float32)


def scan(a, y, x0, x1, label):
    print(f'--- {label}  row y={y} ---')
    prev = None
    for x in range(x0, x1):
        px = a[y, x]
        L = 0.2126*px[0]+0.7152*px[1]+0.0722*px[2]
        s = f'x={x:4d} RGB=({px[0]:5.1f},{px[1]:5.1f},{px[2]:5.1f}) L={L:6.1f} R-B={px[0]-px[2]:+6.1f}'
        print(s)


# horizontal cut through the middle of the body at rest (body centre ~ y 480, x 470..650)
scan(a8, 505, 452, 480, 'rest: left edge of unit')
print()
scan(a13, 505, 620, 660, 'flash f13: left edge of unit')
