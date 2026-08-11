import sys, glob, json
import numpy as np
from PIL import Image
sys.path.insert(0, "Captures/AbilityJuice")
from _ad5_lib import *
from scipy.ndimage import (binary_opening, binary_closing, binary_erosion, binary_dilation,
                           binary_fill_holes, label, distance_transform_edt)


def frames(shot, rnd="r5"):
    return sorted(glob.glob(f"Captures/AbilityJuice/shots/{rnd}/{shot}/f*.jpg"))


def clean_plate(shot, rnd="r5"):
    return np.median(np.stack([load(f) for f in frames(shot, rnd)[-12:]]), axis=0)


cp = clean_plate("numbers")
fs = frames("numbers")

print("=== NUMBERS timing: 0 -> 130% in 60ms, settle by 120ms, travel 0.8-1.0 cells, gone by 400ms ===")
prev = None
rows = []
for i in range(8, 30):
    f = load(fs[i])
    d = np.abs(f - cp).max(axis=-1)
    m = binary_closing(binary_opening(d > 14, np.ones((3, 3))), np.ones((3, 3)))
    t = (i - 12) / 30.0
    if m.sum() < 60:
        rows.append((i, t, 0, None, None, 0))
        print(f"  f{i:03d} t={t:+.3f}s   (absent)")
        continue
    ys, xs = np.where(m)
    h = ys.max() - ys.min() + 1
    w = xs.max() - xs.min() + 1
    rows.append((i, t, m.sum(), xs.mean(), ys.mean(), h))
    print(f"  f{i:03d} t={t:+.3f}s  area {m.sum():6d}px  bbox {w:3d}x{h:3d}  "
          f"centroid ({xs.mean():6.1f},{ys.mean():6.1f})  height {h/218:.3f} cells")

live = [r for r in rows if r[2] > 60]
if live:
    hs = [r[5] for r in live]
    print(f"\n  peak bbox height {max(hs)}px at t={live[int(np.argmax(hs))][1]:+.3f}s; "
          f"settled height {hs[-1]}px -> overshoot {100.0*max(hs)/np.median(hs[-4:]) if len(hs)>4 else float('nan'):.0f}% of settle")
    y0 = live[0][4]; y1 = min(r[4] for r in live)
    print(f"  vertical travel: {y0-y1:.0f}px = {(y0-y1)/218:.2f} cells   (ask 0.8-1.0 cells)")
    print(f"  first visible t={live[0][1]:+.3f}s, last visible t={live[-1][1]:+.3f}s "
          f"-> lifetime {live[-1][1]-live[0][1]+0.033:.3f}s   (ask: gone by 0.400s)")

print()
print("=== stroke width + drop shadow ===")
f = load(fs[13])
d = np.abs(f - cp).max(axis=-1)
m = binary_closing(binary_opening(d > 14, np.ones((3, 3))), np.ones((3, 3)))
L, S = lum(f), sat(f)
fill = m & (S > 0.5) & (L > 150)
fill = binary_fill_holes(binary_closing(fill, np.ones((3, 3))))
stroke = m & (L < 70)
# distance transform inside the stroke band gives half-width
dt = distance_transform_edt(stroke)
print(f"  black stroke: max half-width {dt.max():.1f}px -> full width ~{2*dt.max():.1f}px")
sk = dt[dt > 0.9]
print(f"  stroke half-width p50 {np.percentile(sk,50):.1f} p90 {np.percentile(sk,90):.1f} "
      f"-> typical full width {2*np.percentile(sk,50):.1f}px  (ask: 4px hard black stroke)")
print(f"  raw frame is 1024px; the shipped strip panel is 512 -> stroke reads at "
      f"{2*np.percentile(sk,50)/2:.1f}px on the panel")
# shadow: dark pixels NOT adjacent to fill, offset direction
sh = m & (L < 120) & (L >= 70)
if sh.sum() > 30:
    fy, fx = np.where(fill); sy, sx = np.where(sh)
    print(f"  mid-dark 'shadow' band: {sh.sum()}px, offset from fill centroid "
          f"dx {sx.mean()-fx.mean():+.1f} dy {sy.mean()-fy.mean():+.1f}px")
else:
    print(f"  mid-dark shadow band: {sh.sum()}px  (no separable drop shadow found)")

print()
print("=== ring test: is the dark outline closed around the glyph contour? ===")
fill_d = binary_dilation(fill, np.ones((3, 3)))
edge = fill_d & ~fill
edge_dark = edge & (L < 90)
print(f"  glyph contour px {edge.sum()}, of which dark(L<90) {edge_dark.sum()} "
      f"= {100.0*edge_dark.sum()/max(1,edge.sum()):.1f}%  (builder claims 100% by construction)")
for w in (2, 4, 6):
    ring = binary_dilation(fill, np.ones((2*w+1, 2*w+1))) & ~fill
    print(f"    at {w}px out: dark fraction {100.0*(ring & (L<90)).sum()/max(1,ring.sum()):.1f}%")

print()
print("=== GREYSCALE legibility: number vs board (the r1 failure was 7 levels) ===")
for rnd in ["r4", "r5"]:
    pk = {q["shot"]: q for q in json.load(open(f"{STRIPS}/{rnd}/picks.json"))}
    fi = pk["numbers"]["impact_frame"]
    ff = load(frames("numbers", rnd)[fi])
    cpx = clean_plate("numbers", rnd)
    dd = np.abs(ff - cpx).max(axis=-1)
    mm = binary_closing(binary_opening(dd > 14, np.ones((3, 3))), np.ones((3, 3)))
    Lf, Sf = lum(ff), sat(ff)
    ys, xs = np.where(mm)
    # local board around the mark
    pad = 40
    box = np.zeros_like(mm)
    box[max(0,ys.min()-pad):ys.max()+pad, max(0,xs.min()-pad):xs.max()+pad] = True
    board = box & ~binary_dilation(mm, np.ones((9, 9)))
    fl = mm & (Sf > 0.5) & (Lf > 150)
    st = mm & (Lf < 70)
    print(f"  {rnd}: local board L {Lf[board].mean():.1f} | fill L {Lf[fl].mean():.1f} "
          f"(delta {Lf[fl].mean()-Lf[board].mean():+.1f}) | stroke L {Lf[st].mean():.1f} "
          f"(delta {Lf[st].mean()-Lf[board].mean():+.1f})")
    print(f"       mark area {mm.sum()}px = {100.0*mm.sum()/mm.size:.2f}% of frame; "
          f"cap height {(ys.max()-ys.min()+1)/218:.2f} cells")
