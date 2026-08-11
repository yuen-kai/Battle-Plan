import numpy as np
from PIL import Image

DIR = 'Captures/AbilityJuice/shots/r3/impactcore/'
PX = 218.0


def load(p, i):
    return np.asarray(Image.open(p + 'f%04d.jpg' % i)).astype(np.float32)


def lum(a):
    return a @ np.array([0.2126, 0.7152, 0.0722], np.float32)


def sat(a):
    mx = a.max(axis=2)
    mn = a.min(axis=2)
    return np.where(mx > 1, (mx - mn) / np.maximum(mx, 1), 0.0)


def analyse(path, frames, label):
    plate = load(path, 0)
    print('\n===== %s =====' % label)
    hdr = '%-4s %-8s %7s %7s %7s %7s %7s %7s %7s %7s'
    print(hdr % ('f', 't(s)', 'whiteD', 'clipD', 'bodyD', 'w/body', 'ctrLum', 'ringW', 'ringSat', 'ringLum'))
    for i in frames:
        f = load(path, i)
        S = sat(f)
        mn = f.min(axis=2)
        L = lum(f)
        white = (mn >= 235) & (S < 0.10)
        clip = (f[:, :, 0] >= 250) & (f[:, :, 1] >= 250) & (f[:, :, 2] >= 250)
        # opaque/coloured body: the blue disc. Distinguish from floor by blue dominance or darkness.
        blue = (f[:, :, 2] - f[:, :, 0] > 25) & (S > 0.25)
        core = white | blue
        if core.sum() < 100:
            print('%-4d %+8.4f      -- gone --' % (i, (i - 12) / 30.0))
            continue
        ys, xs = np.nonzero(core)
        cy, cx = ys.mean(), xs.mean()
        wA, cA, bA = white.sum(), clip.sum(), core.sum()
        wD = 2 * np.sqrt(wA / np.pi)
        cD = 2 * np.sqrt(cA / np.pi)
        bD = 2 * np.sqrt(bA / np.pi)
        # centre luminance: 11px box at the core centroid
        cyi, cxi = int(round(cy)), int(round(cx))
        ctr = L[cyi - 5:cyi + 6, cxi - 5:cxi + 6].mean()
        ringW = (bD - wD) / 2.0
        rs = S[blue]
        rl = L[blue]
        print('%-4d %+8.4f %7.1f %7.1f %7.1f %7.3f %7.1f %7.1f %7.3f %7.1f' % (
            i, (i - 12) / 30.0, wD, cD, bD, wD / bD, ctr, ringW,
            np.percentile(rs, 90), np.median(rl)))
        # radial whiteness profile: is the middle white or hollow?
        yy, xx = np.mgrid[0:f.shape[0], 0:f.shape[1]]
        r = np.sqrt((yy - cy) ** 2 + (xx - cx) ** 2)
        prof = []
        for r0 in range(0, int(bD / 2) + 40, 20):
            m = (r >= r0) & (r < r0 + 20)
            if m.sum():
                prof.append('%d:%.2f' % (r0, white[m].mean()))
        print('     white fraction by radius(px): ' + ' '.join(prof))


analyse(DIR, [12, 13, 14, 15, 16, 17, 18], 'ROUND 3')
analyse('Captures/AbilityJuice/shots/r2/impactcore/', [12, 13, 14, 15, 16, 17, 18], 'ROUND 2')
