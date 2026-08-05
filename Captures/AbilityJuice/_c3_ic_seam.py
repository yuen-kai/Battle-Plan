import numpy as np
from PIL import Image

W = np.array([0.2126, 0.7152, 0.0722], np.float32)
P3 = 'Captures/AbilityJuice/shots/r3/impactcore/'
PX = 218.0


def load(p, i):
    return np.asarray(Image.open(p + 'f%04d.jpg' % i)).astype(np.float32)


def sat(a):
    mx = a.max(axis=2)
    mn = a.min(axis=2)
    return np.where(mx > 1, (mx - mn) / np.maximum(mx, 1), 0.0)


plate = load(P3, 0)
Lp = plate @ W

f15 = load(P3, 15)
mn = f15.min(axis=2)
white = (mn >= 235) & (sat(f15) < 0.10)
ys, xs = np.nonzero(white)
cy, cx = int(ys.mean()), int(xs.mean())
rWhite = np.sqrt(white.sum() / np.pi)
print('core centre (%d,%d)  white radius %.0f px (%.2f cells)' % (cy, cx, rWhite, 2 * rWhite / PX))

# locate real vertical seams in the plate on a row band that the RIM covers (outside the white core)
row0, row1 = cy + int(rWhite) + 15, cy + int(rWhite) + 55
prof = Lp[row0:row1].mean(axis=0)
sm = np.convolve(prof, np.ones(3) / 3, 'same')
cand = [x for x in range(60, 980) if sm[x] == sm[x - 6:x + 7].min() and sm[x] < np.median(sm) - 6]
groups = []
for x in cand:
    if groups and x - groups[-1][-1] <= 4:
        groups[-1].append(x)
    else:
        groups.append([x])
seams = [int(np.mean(g)) for g in groups]
print('rim-band rows %d-%d  plate seams at x=%s' % (row0, row1, seams))

for sx in seams:
    if abs(sx - cx) > 330:
        continue
    print('\n seam x=%d  (%.2f cells from centre, under the rim)' % (sx, abs(sx - cx) / PX))
    for i in [0, 12, 13, 14, 15, 16, 17]:
        f = load(P3, i)
        L = f @ W
        w = L[row0:row1, sx - 22:sx + 23].mean(axis=0)
        print('   f%02d t%+.3f  seam contrast %5.1f   local median L %5.1f' % (
            i, (i - 12) / 30.0, w.max() - w.min(), np.median(w)))

# --- layer differentiation: does anything move on its own clock? ---
print('\n--- layer clocks: outer reach of each layer, in cells ---')
print('%-4s %-8s %8s %8s %8s %8s' % ('f', 't', 'whiteR', 'rimR', 'spikeR', 'glowR'))
for i in range(12, 19):
    f = load(P3, i)
    L = f @ W
    S = sat(f)
    mnn = f.min(axis=2)
    d = np.abs(f - plate).max(axis=2)
    wh = (mnn >= 235) & (S < 0.10)
    rim = (d > 20) & (L < Lp - 35)
    glow = d > 8
    yy, xx = np.mgrid[0:1024, 0:1024]
    r = np.sqrt((yy - cy) ** 2 + (xx - cx) ** 2)
    def reach(m, q=99.5):
        return (np.percentile(r[m], q) / PX) if m.sum() > 50 else 0.0
    # spikes = white pixels beyond the white core's own equivalent radius
    rw = np.sqrt(max(wh.sum(), 1) / np.pi)
    spike = wh & (r > rw * 1.02)
    print('%-4d %+8.3f %8.2f %8.2f %8.2f %8.2f' % (
        i, (i - 12) / 30.0, reach(wh, 99.0), reach(rim, 99.0),
        (r[spike].max() / PX) if spike.sum() > 30 else 0.0, reach(glow, 99.0)))
