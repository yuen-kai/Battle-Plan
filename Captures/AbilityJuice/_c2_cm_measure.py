import numpy as np
from PIL import Image
import os

ROOT = os.path.dirname(os.path.abspath(__file__))
REF = os.path.join(ROOT, 'strips', 'reference')


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


def sat(a):
    mx = a.max(-1); mn = a.min(-1)
    return np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0)


def panels(path):
    im = np.asarray(Image.open(path).convert('RGB')).astype(np.float32)
    H, W, _ = im.shape
    # strips are 3 panels with thin black gutters
    col = lum(im).mean(0)
    dark = col < 20
    # find gutter centres
    runs = []
    s = None
    for x in range(W):
        if dark[x] and s is None: s = x
        if not dark[x] and s is not None:
            runs.append((s, x)); s = None
    gut = [r for r in runs if 2 <= r[1]-r[0] <= 30 and 0.2*W < (r[0]+r[1])/2 < 0.8*W]
    if len(gut) >= 2:
        b = [0, gut[0][0], gut[0][1], gut[1][0], gut[1][1], W]
        return [im[:, b[0]:b[1]], im[:, b[2]:b[3]], im[:, b[4]:b[5]]]
    third = W//3
    return [im[:, 0:third], im[:, third:2*third], im[:, 2*third:]]


print("== CLASH MINI: panel-1 telegraph vs its own board floor ==")
print("%-26s  %-34s  %-34s" % ("strip", "panel1", "panel3"))
for f in ['cm-everyability-22.jpg', 'cm-everyability-20.jpg', 'cm-everyability-27.jpg',
          'cm-everyability-30.jpg', 'cm-8newabilities-12.jpg', 'cm-clashabilities-04.jpg']:
    P = panels(os.path.join(REF, f))
    p1, p2, p3 = P
    out = [f[:-4]]
    for p in (p1, p3):
        L = lum(p); S = sat(p)
        # the ability zone = the most saturated 8% of the panel
        thr = np.percentile(S, 92)
        hot = S >= thr
        out.append("floorL %3.0f | hotS %.2f hotL %3.0f cov %2.0f%%" % (
            np.median(L), S[hot].mean(), L[hot].mean(), 100*hot.mean()))
    print("%-26s  %-34s  %-34s" % tuple(out))

print("\n== PANEL1 -> PANEL3 CHANGE (how much the frame is transformed) ==")
print("%-26s  %8s %8s %8s" % ("strip", "meanAbsD", "satDelta", "lumDelta"))
for f in sorted(os.listdir(REF)):
    if not f.endswith('.jpg'):
        continue
    P = panels(os.path.join(REF, f))
    p1, p3 = P[0], P[2]
    h = min(p1.shape[0], p3.shape[0]); w = min(p1.shape[1], p3.shape[1])
    a, b = p1[:h, :w], p3[:h, :w]
    print("%-26s  %8.1f %8.3f %8.1f" % (f[:-4], np.abs(lum(a)-lum(b)).mean(),
                                        sat(b).mean()-sat(a).mean(), lum(b).mean()-lum(a).mean()))

# ---- ours, same metric ----
print("\n== OURS r2 windup, same metric ==")
for name in ('r2', 'r1'):
    P = panels(os.path.join(ROOT, 'strips', name, 'windup.jpg'))
    p1, p2, p3 = P
    h = min(p1.shape[0], p3.shape[0]); w = min(p1.shape[1], p3.shape[1])
    print("  %s  p1->p3 meanAbsD %.1f   p1->p2 meanAbsD %.1f" % (
        name, np.abs(lum(p1[:h, :w])-lum(p3[:h, :w])).mean(),
        np.abs(lum(p1[:h, :w])-lum(p2[:h, :w])).mean()))
    for k, p in enumerate(P):
        L = lum(p); S = sat(p)
        thr = np.percentile(S, 92); hot = S >= thr
        print("     panel%d  floorL %3.0f | hotS %.2f hotL %3.0f | maxL %3.0f | clipped %d" % (
            k+1, np.median(L), S[hot].mean(), L[hot].mean(), L.max(), int((p >= 254).all(-1).sum())))
