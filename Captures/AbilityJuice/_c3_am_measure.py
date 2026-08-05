"""Step 3 - full re-measurement of the r2 aftermath primitive against the six
prescriptions. Everything measured from the raw 1024x1024 frames."""
import numpy as np, glob, os, json
from PIL import Image
from scipy import ndimage

PC = 218.0
FPS = 30.0
T0 = -0.40
DIR = "Captures/AbilityJuice/shots/r2/aftermath"
FR = sorted(glob.glob(os.path.join(DIR, "f*.jpg")))
N = len(FR)

def load(i): return np.asarray(Image.open(FR[i]).convert("RGB")).astype(np.float64)
def lum(a):  return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]
def tt(i):   return T0 + i/FPS

def sat(a):
    mx = a.max(axis=-1); mn = a.min(axis=-1)
    return np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0.0)

def hue(a):
    r, g, b = a[...,0], a[...,1], a[...,2]
    mx = a.max(axis=-1); mn = a.min(axis=-1); d = mx-mn
    h = np.zeros_like(mx)
    m = (d > 1e-6)
    im = m & (mx == r); h[im] = ((g-b)[im]/d[im]) % 6
    im = m & (mx == g); h[im] = ((b-r)[im]/d[im]) + 2
    im = m & (mx == b); h[im] = ((r-g)[im]/d[im]) + 4
    return h*60.0

A0 = load(0); L0 = lum(A0)
FLOOR = float(np.percentile(L0, 50))
print(f"clean floor median luminance = {FLOOR:.1f}")

# ============================================================ 4. LIFE / MOTION
print("\n=== 4. MOTION: bounding box + centroid over time ===")
print(f"{'t':>7} {'bboxW':>6} {'bboxH':>6} {'cyCell':>7} {'cxCell':>7} {'darkArea':>9} {'anyArea':>8} {'minL':>6}")
rows = []
for i in range(N):
    A = load(i); L = lum(A); d = L - L0
    dark = d < -40           # genuinely occluding material
    anyc = np.abs(d) > 10
    r = {"i": i, "t": tt(i)}
    if dark.sum() > 300:
        ys, xs = np.nonzero(dark)
        wgt = np.clip(-d[dark], 0, None)
        r["w"] = (xs.max()-xs.min()+1)/PC
        r["h"] = (ys.max()-ys.min()+1)/PC
        r["cy"] = float((ys*wgt).sum()/wgt.sum())/PC
        r["cx"] = float((xs*wgt).sum()/wgt.sum())/PC
        r["darkA"] = float(dark.sum())/PC**2
        r["minL"] = float(L[dark].min())
    else:
        r.update(w=0, h=0, cy=np.nan, cx=np.nan, darkA=0, minL=np.nan)
    r["anyA"] = float(anyc.sum())/PC**2
    rows.append(r)
    if i % 3 == 0 or i in (25, 29, 42):
        print(f"{r['t']:+7.2f} {r['w']:6.2f} {r['h']:6.2f} {r['cy']:7.3f} {r['cx']:7.3f} "
              f"{r['darkA']:9.2f} {r['anyA']:8.2f} {r['minL']:6.1f}")
json.dump(rows, open("Captures/AbilityJuice/_c3_am_track.json", "w"), indent=1)

live = [r for r in rows if r["darkA"] > 0.05]
if live:
    ws = [r["w"] for r in live]; hs = [r["h"] for r in live]
    cys = [r["cy"] for r in live]; cxs = [r["cx"] for r in live]
    print(f"\ndark-mass present t={live[0]['t']:+.2f} .. {live[-1]['t']:+.2f}  "
          f"({live[-1]['t']-live[0]['t']:.2f}s)")
    print(f"bbox W range {min(ws):.2f}..{max(ws):.2f} cells   H range {min(hs):.2f}..{max(hs):.2f}")
    print(f"centroid Y travel {max(cys)-min(cys):.3f} cells   X travel {max(cxs)-min(cxs):.3f} cells")
    print(f"centroid Y: first {cys[0]:.3f} -> min {min(cys):.3f} (rise = {cys[0]-min(cys):+.3f} cells up)")

# any-change lifetime (includes ground mark + shadow)
alive = [r for r in rows if r["anyA"] > 0.10]
print(f"any-change present t={alive[0]['t']:+.2f} .. {alive[-1]['t']:+.2f}")
