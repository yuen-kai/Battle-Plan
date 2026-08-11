import numpy as np
from PIL import Image
from scipy import ndimage
import os

ROOT = "/Users/cykai/Battle-Plan/Captures/AbilityJuice/shots/r2"
PXCELL = {"impactdip": 213.0, "impactcore": 232.0}
CENTRE = {"impactdip": (507.0, 483.0), "impactcore": (532.0, 478.0)}

def load(shot, i):
    return np.asarray(Image.open(os.path.join(ROOT, shot, f"f{i:04d}.jpg")).convert("RGB"),
                      dtype=np.float64)
def lum(a):
    return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]

def clean_set(shot, lo, hi):
    """Pixels that are board floor in pre-roll AND stay unwashed through the whole event."""
    med = np.median(np.stack([load(shot, i) for i in range(12)]), axis=0)
    L0 = lum(med); mx, mn = med.max(axis=2), med.min(axis=2)
    m = ndimage.binary_erosion((L0 > 150) & ((mx-mn)/np.maximum(mx,1e-6) < 0.16), np.ones((7,7)))
    cy, cx = CENTRE[shot]; pc = PXCELL[shot]
    yy, xx = np.mgrid[0:1024, 0:1024]
    rr = np.hypot(yy-cy, xx-cx)/pc
    m &= (rr >= lo) & (rr < hi)
    for i in range(11, 21):
        F = load(shot, i)
        mxf, mnf = F.max(axis=2), F.min(axis=2)
        m &= ((mxf-mnf)/np.maximum(mxf,1e-6) < 0.20)
    return m, float(L0[m].mean())

print("=" * 98)
print("R.  TIMING: when is the flash brightest, and when is the board darkest?")
print("    Fixed pixel set, matched world annulus 0.9-2.4 cells, clean in every frame,")
print("    measured identically in both shots so the dip can be isolated by subtraction.")
print("=" * 98)
LO, HI = 0.9, 2.4
md, bd = clean_set("impactdip", LO, HI)
mc, bc = clean_set("impactcore", LO, HI)
print(f"  dip shot: {md.sum()} px, baseline {bd:.1f}   |   control: {mc.sum()} px, baseline {bc:.1f}")
print()
print(f"  {'frame':>6} {'t(s)':>7} | {'flash clipped px':>16} {'flash area>235':>14} | "
      f"{'floor DIP':>9} {'floor CTRL':>10} | {'dip alone':>9}  profile")
best = None
for i in range(11, 22):
    Fd = load("impactdip", i); Fc = load("impactcore", i)
    Ld, Lc = lum(Fd), lum(Fc)
    clip = int(((Fc[...,0]>=254)&(Fc[...,1]>=254)&(Fc[...,2]>=254)).sum())
    area = float((Lc > 235).sum())/(PXCELL["impactcore"]**2)
    fd = float(Ld[md].mean()); fc = float(Lc[mc].mean())
    alone = (fd - bd) - (fc - bc)
    if best is None or alone < best[1]:
        best = (i, alone)
    bar = "#" * int(max(0, -alone*2))
    print(f"  f{i:04d} {(i-12)*0.03333:+7.3f} | {clip:16d} {area:13.3f}c2 | {fd:9.1f} {fc:10.1f} | "
          f"{alone:+9.1f}  {bar}")
print(f"\n  flash peak: f0012 (t=+0.000s).  dip's own peak: f{best[0]:04d} "
      f"(t={(best[0]-12)*0.03333:+.3f}s) at {best[1]:+.1f} luminance.")
print(f"  offset = {(best[0]-12)*33.33:.0f} ms LATE.")

print()
print("=" * 98)
print("S.  WHAT THE DIP IS WORTH AT THE FRAME THE STRIP CUTS")
print("    Contrast of the white core against the board, on each candidate impact frame.")
print("=" * 98)
print(f"  {'frame':>6} {'t(s)':>7} | {'core L':>7} {'board L (dip)':>13} {'board L (ctrl)':>14} | "
      f"{'headroom dip':>12} {'headroom ctrl':>13} {'gain':>7}")
for i in (12, 13, 14, 15):
    Ld = lum(load("impactdip", i)); Lc = lum(load("impactcore", i))
    fd = float(Ld[md].mean()); fc = float(Lc[mc].mean())
    coreL = 255.0
    print(f"  f{i:04d} {(i-12)*0.03333:+7.3f} | {coreL:7.1f} {fd:13.1f} {fc:14.1f} | "
          f"{coreL-fd:12.1f} {coreL-fc:13.1f} {(coreL-fd)/(coreL-fc):6.2f}x")

print()
print("=" * 98)
print("T.  IS THE DARKENING SHAPED, OR IS IT A FLAT WASH?  Angular and radial structure")
print("    of the dip's own contribution at its deepest frame (f0014).")
print("=" * 98)
Fd = load("impactdip", 14); Ld = lum(Fd)
medd = np.median(np.stack([load("impactdip", i) for i in range(12)]), axis=0)
L0d = lum(medd)
cy, cx = CENTRE["impactdip"]; pc = PXCELL["impactdip"]
yy, xx = np.mgrid[0:1024, 0:1024]
rr = np.hypot(yy-cy, xx-cx)/pc
th = np.degrees(np.arctan2(yy-cy, xx-cx)) % 360
print("  radial profile of drop (clean floor pixels, cells):")
for lo in np.arange(0.6, 3.2, 0.25):
    m = md & (rr >= lo) & (rr < lo+0.25)
    if m.sum() > 200:
        d = float((Ld[m]-L0d[m]).mean())
        print(f"    {lo:4.2f}-{lo+0.25:4.2f}c  drop={d:+6.1f}  {'#'*int(max(0,-d))}")
print("  angular profile at 1.0-2.0 cells (asymmetry check; a ring would be flat):")
drops = []
for a in range(0, 360, 30):
    m = md & (rr >= 1.0) & (rr < 2.0) & (th >= a) & (th < a+30)
    if m.sum() > 150:
        d = float((Ld[m]-L0d[m]).mean()); drops.append(d)
        print(f"    {a:3d}-{a+30:3d} deg  drop={d:+6.1f}  {'#'*int(max(0,-d))}")
if drops:
    print(f"    -> deepest {min(drops):+.1f}, shallowest {max(drops):+.1f}, "
          f"spread {max(drops)-min(drops):.1f} luminance "
          f"({100*(max(drops)-min(drops))/max(abs(min(drops)),1e-6):.0f}% of depth)")
