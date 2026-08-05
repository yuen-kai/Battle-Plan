import sys, glob, json
import numpy as np
from PIL import Image
sys.path.insert(0, "Captures/AbilityJuice")
from _ad5_lib import *
from scipy.ndimage import binary_opening, binary_closing, gaussian_filter


def frames(shot, rnd="r5"):
    return sorted(glob.glob(f"Captures/AbilityJuice/shots/{rnd}/{shot}/f*.jpg"))


def clean_plate(shot, rnd="r5"):
    return np.median(np.stack([load(f) for f in frames(shot, rnd)[-12:]]), axis=0)


def emask(f, cp, thresh=14):
    d = np.abs(f - cp).max(axis=-1)
    return binary_closing(binary_opening(d > 14, np.ones((3, 3))), np.ones((5, 5)))


def stats(shot, rnd="r5"):
    picks = {p["shot"]: p for p in json.load(open(f"{STRIPS}/{rnd}/picks.json"))}
    fi = picks[shot]["impact_frame"]
    cp = clean_plate(shot, rnd)
    f = load(frames(shot, rnd)[fi])
    m = emask(f, cp)
    L, S, H = lum(f), sat(f), hue(f)
    ys, xs = np.where(m)
    return dict(f=f, cp=cp, m=m, L=L, S=S, H=H, fi=fi,
                bbox=(xs.min(), ys.min(), xs.max(), ys.max()))


print("=== DEATH vs POGO: greyscale separation at the impact frame ===")
D = stats("death")
P = stats("pogo")
for tag, Z in [("DEATH r5", D), ("POGO  r5", P)]:
    L, m, S, H = Z["L"], Z["m"], Z["S"], Z["H"]
    x0, y0, x1, y1 = Z["bbox"]
    v = L[m]
    hh = H[m][S[m] > 0.30]
    print(f"\n  {tag}  frame f{Z['fi']:03d}")
    print(f"    footprint {(x1-x0+1)/218:.2f} x {(y1-y0+1)/218:.2f} cells, area {100.0*m.sum()/m.size:.2f}% of frame")
    print(f"    L: mean {v.mean():6.1f}  median {np.median(v):6.1f}  p05 {np.percentile(v,5):6.1f}  p95 {np.percentile(v,95):6.1f}")
    print(f"    L<40 {100*(v<40).mean():5.1f}%   L<80 {100*(v<80).mean():5.1f}%   L>200 {100*(v>200).mean():5.1f}%")
    print(f"    S mean {S[m].mean():.3f}   bright&sat {100.0*((L>199)&(S>0.45)&m).sum()/m.size:.3f}% of frame")
    if hh.size > 50:
        # circular median hue
        print(f"    dominant hue (S>0.30): median {np.median(hh):.0f} deg  "
              f"p25 {np.percentile(hh,25):.0f}  p75 {np.percentile(hh,75):.0f}")
        for lo, hi, nm in [(0,60,'red/orange'),(60,150,'yellow/green'),(150,210,'cyan'),(210,270,'blue'),(270,360,'magenta')]:
            frac = ((hh>=lo)&(hh<hi)).mean()
            if frac > 0.03:
                print(f"      {nm:12s} {100*frac:5.1f}%")

print()
print("=== GREYSCALE-ONLY discrimination (throw the colour away) ===")
# Build a squint-greyscale of each effect region and compare shape + value histogram
def squint_grey(Z, size=64):
    x0, y0, x1, y1 = Z["bbox"]
    L = Z["L"]
    crop = L[y0:y1+1, x0:x1+1]
    im = Image.fromarray(crop.astype(np.uint8)).resize((size, size), Image.BILINEAR)
    return np.asarray(im).astype(np.float64)

gd, gp = squint_grey(D), squint_grey(P)
print(f"  squinted 64x64 grey: death mean {gd.mean():.1f} std {gd.std():.1f}   "
      f"pogo mean {gp.mean():.1f} std {gp.std():.1f}")
print(f"  mean-value gap: {abs(gd.mean()-gp.mean()):.1f} levels")
hd = np.histogram(gd, bins=16, range=(0,256))[0] / gd.size
hp = np.histogram(gp, bins=16, range=(0,256))[0] / gp.size
inter = np.minimum(hd, hp).sum()
print(f"  greyscale histogram intersection (1.0 = identical): {inter:.3f}")

# shape/aspect
for tag, Z in [("death", D), ("pogo", P)]:
    x0,y0,x1,y1 = Z["bbox"]
    m = Z["m"][y0:y1+1, x0:x1+1]
    print(f"  {tag}: bbox fill ratio {m.mean():.3f}, aspect {(x1-x0+1)/(y1-y0+1):.2f}")

print()
print("=== r4 death vs r5 pogo, same test (the separation that must not regress) ===")
D4 = stats("death", "r4")
g4 = squint_grey(D4)
print(f"  r4 death squint mean {g4.mean():.1f} std {g4.std():.1f}")
print(f"  r4-death vs r5-pogo mean gap {abs(g4.mean()-gp.mean()):.1f} levels")
h4 = np.histogram(g4, bins=16, range=(0,256))[0] / g4.size
print(f"  r4-death vs r5-pogo hist intersection: {np.minimum(h4,hp).sum():.3f}")
print(f"  r5-death vs r5-pogo hist intersection: {inter:.3f}")

# ---------- write comparison images ----------
OUT = "Captures/AbilityJuice"


def crop_sq(Z, pad=40):
    x0, y0, x1, y1 = Z["bbox"]
    cx, cy = (x0+x1)//2, (y0+y1)//2
    half = max(x1-x0, y1-y0)//2 + pad
    f = Z["f"]
    x0, x1 = max(0, cx-half), min(f.shape[1], cx+half)
    y0, y1 = max(0, cy-half), min(f.shape[0], cy+half)
    return f[y0:y1, x0:x1]

cd, cpg = crop_sq(D), crop_sq(P)
S = 400
def rs(a, s=S):
    return np.asarray(Image.fromarray(a.astype(np.uint8)).resize((s, s), Image.LANCZOS)).astype(np.float64)
cd, cpg = rs(cd), rs(cpg)
cd4 = rs(crop_sq(D4))
row1 = np.concatenate([cd4, cd, cpg], axis=1)
g = lambda a: np.stack([lum(a)]*3, axis=-1)
row2 = np.concatenate([g(cd4), g(cd), g(cpg)], axis=1)
sq = lambda a: np.asarray(Image.fromarray(a.astype(np.uint8)).resize((32,32), Image.BILINEAR).resize((S,S), Image.NEAREST)).astype(np.float64)
row3 = np.concatenate([sq(g(cd4)), sq(g(cd)), sq(g(cpg))], axis=1)
Image.fromarray(np.concatenate([row1, row2, row3]).astype(np.uint8)).save(f"{OUT}/_ad5_sep.jpg", quality=92)
print(f"\n  wrote _ad5_sep.jpg  (cols: r4 death | r5 death | r5 pogo; rows: colour | grey | squint)")
