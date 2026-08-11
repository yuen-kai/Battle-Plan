import numpy as np
from PIL import Image

PX = 218.0
W = np.array([0.2126, 0.7152, 0.0722], np.float32)


def load(p, i):
    return np.asarray(Image.open(p + 'f%04d.jpg' % i)).astype(np.float32)


def sat(a):
    mx = a.max(axis=2)
    mn = a.min(axis=2)
    return np.where(mx > 1, (mx - mn) / np.maximum(mx, 1), 0.0)


def run(path, frames, label):
    plate = load(path, 0)
    Lp = plate @ W
    print('\n===== %s =====' % label)
    print('%-4s %-8s %7s %7s %7s %7s %7s %7s %7s %7s %7s' % (
        'f', 't(s)', 'whiteC', 'clip%', 'opaqC', 'w/opaq', 'ctrWht', 'rimMinL', 'rimSat', 'rimPx', 'reachC'))
    for i in frames:
        f = load(path, i)
        L = f @ W
        S = sat(f)
        mn = f.min(axis=2)
        d = np.abs(f - plate).max(axis=2)
        changed = d > 20
        white = (mn >= 235) & (S < 0.10)
        # opaque = changed AND materially darker than the plate: real occluding material
        opaque = changed & (L < Lp - 35)
        rim = opaque & ~white
        if changed.sum() < 200:
            print('%-4d %+8.4f     -- nothing --' % (i, (i - 12) / 30.0))
            continue
        ys, xs = np.nonzero(white if white.sum() > 500 else (opaque if opaque.sum() > 200 else changed))
        if len(ys) == 0:
            print('%-4d %+8.4f     -- nothing --' % (i, (i - 12) / 30.0))
            continue
        cy, cx = ys.mean(), xs.mean()
        yy, xx = np.mgrid[0:f.shape[0], 0:f.shape[1]]
        r = np.sqrt((yy - cy) ** 2 + (xx - cx) ** 2)
        solid = white | opaque
        wD = 2 * np.sqrt(white.sum() / np.pi) / PX
        oD = 2 * np.sqrt(solid.sum() / np.pi) / PX
        cyi, cxi = int(cy), int(cx)
        ctr = white[cyi - 8:cyi + 9, cxi - 8:cxi + 9].mean()
        rimL = np.percentile(L[rim], 2) if rim.sum() > 200 else float('nan')
        rimS = np.percentile(S[rim], 90) if rim.sum() > 200 else float('nan')
        reach = (r[solid].max() * 2 / PX) if solid.sum() else 0
        print('%-4d %+8.4f %7.3f %7.2f %7.3f %7.3f %7.3f %7.1f %7.3f %7d %7.2f' % (
            i, (i - 12) / 30.0, wD, 100.0 * ((f[:, :, 0] >= 250) & (f[:, :, 1] >= 250) & (f[:, :, 2] >= 250)).mean(),
            oD, wD / max(oD, 1e-6), ctr, rimL, rimS, int(rim.sum()), reach))


run('Captures/AbilityJuice/shots/r3/impactcore/', range(11, 21), 'ROUND 3')
run('Captures/AbilityJuice/shots/r2/impactcore/', range(11, 21), 'ROUND 2')

# --- tile-seam occlusion under the core, round 3 peak and hold frames ---
print('\n--- seam contrast (clean floor seam contrast is ~39; opaque needs <8) ---')
p3 = 'Captures/AbilityJuice/shots/r3/impactcore/'
plate = load(p3, 0)
Lp = plate @ W
# find a seam column inside the core footprint
f15 = load(p3, 15)
mn = f15.min(axis=2)
S = sat(f15)
white = (mn >= 235) & (S < 0.10)
ys, xs = np.nonzero(white)
cy, cx = int(ys.mean()), int(xs.mean())
print('core centroid (y,x) =', cy, cx)
band = Lp[cy - 90:cy + 90, cx - 90:cx + 90]
prof = band.mean(axis=0)
seamx = int(np.argmin(prof)) + cx - 90
print('darkest plate seam near core at x=%d  plate contrast=%.1f' % (
    seamx, prof.max() - prof.min()))
for i in [0, 12, 13, 14, 15, 16, 17, 18]:
    f = load(p3, i)
    L = f @ W
    b = L[cy - 90:cy + 90, cx - 90:cx + 90].mean(axis=0)
    print('  f%02d t%+.3f  local seam contrast = %6.1f' % (i, (i - 12) / 30.0, b.max() - b.min()))
