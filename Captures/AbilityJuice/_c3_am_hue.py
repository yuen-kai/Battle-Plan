"""Step 12 - the decisive statistic: saturation as a function of brightness.
Round-1's trap was 'bright pixels colourless, coloured pixels dim'. Did r2 escape it?"""
import numpy as np, glob
from PIL import Image

def arr(p): return np.asarray(Image.open(p).convert("RGB")).astype(np.float64)
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def sat(a):
    mx = a.max(-1); mn = a.min(-1)
    return np.where(mx > 1e-6, (mx-mn)/np.maximum(mx,1), 0)

BINS = [(120,160),(160,190),(190,210),(210,230),(230,245),(245,256)]

def table(a, label):
    L = lum(a); S = sat(a)
    print(f"\n {label}")
    print(f"   {'L band':>10} {'px':>8} {'%frame':>7} {'medSat':>7} {'p90Sat':>7} {'sat>0.45 %ofband':>18}")
    for lo, hi in BINS:
        m = (L >= lo) & (L < hi)
        if m.sum() < 30:
            print(f"   {f'{lo}-{hi}':>10} {m.sum():8d}      -"); continue
        s = S[m]
        print(f"   {f'{lo}-{hi}':>10} {m.sum():8d} {100*m.mean():6.2f}% {np.median(s):7.2f} "
              f"{np.percentile(s,90):7.2f} {100*(s>0.45).mean():17.1f}%")

FR = sorted(glob.glob("Captures/AbilityJuice/shots/r2/aftermath/f*.jpg"))
for i, t in ((25, "+0.43"), (29, "+0.57"), (38, "+0.87")):
    table(arr(FR[i]), f"OURS aftermath t={t}s (full 1024 frame)")

for n in ("cm-8newabilities-13", "cm-everyability-23", "cm-8newabilities-07"):
    im = Image.open(f"Captures/AbilityJuice/strips/reference/{n}.jpg")
    w, h = im.size; p = w//3
    table(np.asarray(im.crop((2*p, 0, 3*p, h)).convert("RGB")).astype(np.float64),
          f"CLASH MINI {n} - aftermath panel")

print("\n=== headline ===")
for i, t in ((25,"+0.43"),(29,"+0.57")):
    a = arr(FR[i]); L = lum(a); S = sat(a)
    hot_col = (L > 200) & (S > 0.45)
    print(f" OURS t={t}: pixels that are BOTH bright(L>200) AND saturated(>0.45): "
          f"{hot_col.sum():6d}  = {100*hot_col.mean():.3f}% of frame  ({hot_col.sum()/218.0**2:.4f} cells^2)")
for n in ("cm-8newabilities-13","cm-everyability-23","cm-8newabilities-07"):
    im = Image.open(f"Captures/AbilityJuice/strips/reference/{n}.jpg"); w,h = im.size; p = w//3
    a = np.asarray(im.crop((2*p,0,3*p,h)).convert("RGB")).astype(np.float64)
    L = lum(a); S = sat(a); hc = (L > 200) & (S > 0.45)
    print(f" {n:22s}: bright AND saturated: {hc.sum():6d} = {100*hc.mean():.3f}% of panel")
