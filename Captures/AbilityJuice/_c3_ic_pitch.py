import numpy as np
from PIL import Image

# Establish cell pitch on the impactcore camera by finding tile seams in a clean preroll frame.
plate = np.asarray(Image.open('Captures/AbilityJuice/shots/r3/impactcore/f0000.jpg')).astype(np.float32)
L = plate @ np.array([0.2126, 0.7152, 0.0722], np.float32)
print('floor luminance: median %.1f  p05 %.1f  p95 %.1f' % (np.median(L), np.percentile(L, 5), np.percentile(L, 95)))

# horizontal seams: column-wise darkness in a band that is pure floor
band = L[430:600, :]
col = band.mean(axis=0)
sm = np.convolve(col, np.ones(3) / 3, mode='same')
mins = []
for x in range(4, 1020):
    w = sm[x - 4:x + 5]
    if sm[x] == w.min() and sm[x] < np.median(sm) - 4:
        mins.append(x)
# collapse runs
groups = []
for x in mins:
    if groups and x - groups[-1][-1] <= 3:
        groups[-1].append(x)
    else:
        groups.append([x])
seams = [int(np.mean(g)) for g in groups]
print('seam x:', seams)
if len(seams) > 1:
    d = np.diff(seams)
    print('spacings:', d, 'median', np.median(d))
