import numpy as np
from PIL import Image
import os, json

ROOT = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(ROOT, 'shots', 'r2', 'windup')
frames = sorted(f for f in os.listdir(SH) if f.endswith('.jpg'))
N = len(frames)
print("frames:", N)


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


def sat(a):
    mx = a.max(-1); mn = a.min(-1)
    return np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0.0)


def load(i):
    return np.asarray(Image.open(os.path.join(SH, frames[i])).convert('RGB')).astype(np.float32)


f0 = load(0)
H, W, _ = f0.shape
print("frame size:", W, "x", H)

# ---- timing model ----
def t_of(i):
    return i/30.0 - 0.40

print("t range: %.3f .. %.3f" % (t_of(0), t_of(N-1)))

# ---- clean floor reference: frame 0 (t=-0.40, nothing on screen) ----
L0 = lum(f0)
print("\n== FLOOR (frame 0, whole image) ==")
print("mean lum %.1f  median %.1f  p10 %.1f p90 %.1f" % (L0.mean(), np.median(L0), np.percentile(L0,10), np.percentile(L0,90)))

# The board floor = light blue-grey tiles. Isolate by luminance band (exclude dark crates).
floor_mask0 = L0 > 150
print("floor(>150) mean lum %.1f  sat %.3f  coverage %.1f%%" % (
    L0[floor_mask0].mean(), sat(f0/255.0)[floor_mask0].mean(), 100*floor_mask0.mean()))

# ---- per-frame difference against frame 0 ----
rows = []
for i in range(N):
    a = load(i)
    La = lum(a)
    d = La - L0                       # luminance delta vs clean plate
    # "mark" = pixels darkened by the multiply tint, on floor only
    darker = (d < -12) & floor_mask0
    brighter = (d > 12)
    S = sat(a/255.0)
    row = dict(i=i, t=t_of(i),
               dark_cov=100*darker.mean(),
               bright_cov=100*brighter.mean(),
               dark_lum=float(La[darker].mean()) if darker.sum() > 50 else float('nan'),
               dark_sat=float(S[darker].mean()) if darker.sum() > 50 else float('nan'),
               dark_delta=float(d[darker].mean()) if darker.sum() > 50 else float('nan'),
               maxlum=float(La.max()),
               clipped=int((( a >= 254).all(-1)).sum()))
    if darker.sum() > 50:
        ys, xs = np.nonzero(darker)
        row['bbox'] = (int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max()))
        row['bw'] = int(xs.max()-xs.min()); row['bh'] = int(ys.max()-ys.min())
        row['cx'] = float(xs.mean()); row['cy'] = float(ys.mean())
    else:
        row['bbox'] = None; row['bw'] = 0; row['bh'] = 0; row['cx'] = float('nan'); row['cy'] = float('nan')
    rows.append(row)

print("\n== PER-FRAME (tint = pixels >12 darker than clean plate) ==")
print(" i    t      dark%   lum    sat    delta   bbox w x h    cx     cy    maxL  clip")
for r in rows:
    print("%3d %+6.2f  %6.2f %6.1f %6.3f %7.1f  %4d x %4d  %6.1f %6.1f %5.0f %5d" % (
        r['i'], r['t'], r['dark_cov'], r['dark_lum'], r['dark_sat'], r['dark_delta'],
        r['bw'], r['bh'], r['cx'], r['cy'], r['maxlum'], r['clipped']))

json.dump([{k: (list(v) if isinstance(v, tuple) else v) for k, v in r.items()} for r in rows],
          open(os.path.join(ROOT, '_c2_windup_rows.json'), 'w'), indent=1)
