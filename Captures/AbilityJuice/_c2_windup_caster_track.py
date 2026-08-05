import numpy as np
from PIL import Image
import os

ROOT = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(ROOT, 'shots', 'r2', 'windup')
frames = sorted(f for f in os.listdir(SH) if f.endswith('.jpg'))


def load(i):
    return np.asarray(Image.open(os.path.join(SH, frames[i])).convert('RGB')).astype(np.float32)


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


# caster region (left unit). generous box.
X0, X1, Y0, Y1 = 60, 340, 400, 600

f0 = load(0)
L0 = lum(f0)

print("== CASTER BODY TRACK (pink team blob, left unit) ==")
print("  i     t     pink_px   cx      cy     dx     dy   |  chevron_px  chev_cx  chev_lum chev_sat")
base = None
for i in range(0, 60):
    a = load(i)
    sub = a[Y0:Y1, X0:X1]
    R, G, B = sub[..., 0], sub[..., 1], sub[..., 2]
    # team pink: strong red, mid blue, low green
    pink = (R > 150) & (R - G > 60) & (B - G > 10)
    # charge chevrons: saturated orange/yellow  (R high, G mid, B low)
    chev = (R > 140) & (R - B > 90) & (G - B > 45)
    if pink.sum() > 50:
        ys, xs = np.nonzero(pink)
        cx, cy = xs.mean(), ys.mean()
    else:
        cx = cy = float('nan')
    if base is None and i == 0:
        base = (cx, cy)
    cs = ""
    if chev.sum() > 30:
        ys2, xs2 = np.nonzero(chev)
        cs = "%8d %8.1f %8.1f %8.3f" % (chev.sum(), xs2.mean(),
                                        lum(sub)[chev].mean(),
                                        ((sub.max(-1)-sub.min(-1))/np.maximum(sub.max(-1), 1))[chev].mean())
    else:
        cs = "%8d       -        -        -" % chev.sum()
    print("%3d %+6.2f  %7d %7.1f %7.1f %+6.1f %+6.1f | %s" % (
        i, i/30.0-0.40, pink.sum(), cx, cy, cx-base[0], cy-base[1], cs))

# total travel
print("\n== CASTER DISPLACEMENT SUMMARY ==")
xs_, ys_ = [], []
for i in range(0, 46):
    a = load(i); sub = a[Y0:Y1, X0:X1]
    R, G, B = sub[..., 0], sub[..., 1], sub[..., 2]
    pink = (R > 150) & (R - G > 60) & (B - G > 10)
    yy, xx = np.nonzero(pink)
    xs_.append(xx.mean()); ys_.append(yy.mean())
xs_ = np.array(xs_); ys_ = np.array(ys_)
print("  pre-fire (t=-0.40..+1.10): cx range %.1f px, cy range %.1f px" % (xs_.max()-xs_.min(), ys_.max()-ys_.min()))
print("  cx min/max %.1f / %.1f ; cy min/max %.1f / %.1f" % (xs_.min(), xs_.max(), ys_.min(), ys_.max()))
print("  pink area min/max over pre-fire window:")
areas = []
for i in range(0, 46):
    a = load(i); sub = a[Y0:Y1, X0:X1]
    R, G, B = sub[..., 0], sub[..., 1], sub[..., 2]
    areas.append(((R > 150) & (R-G > 60) & (B-G > 10)).sum())
areas = np.array(areas)
print("    %d .. %d  (%.1f%% swing)" % (areas.min(), areas.max(), 100*(areas.max()-areas.min())/areas.min()))
