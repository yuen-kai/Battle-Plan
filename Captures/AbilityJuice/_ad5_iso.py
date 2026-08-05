import sys, glob, os, json
import numpy as np
sys.path.insert(0, "Captures/AbilityJuice")
from _ad5_lib import *
from scipy.ndimage import label, binary_opening, binary_closing, maximum_filter, minimum_filter

SH = "Captures/AbilityJuice/shots/r5"


def frames(shot, rnd="r5"):
    return sorted(glob.glob(f"Captures/AbilityJuice/shots/{rnd}/{shot}/f*.jpg"))


def clean_plate(shot, rnd="r5"):
    """Median of the last 12 frames -> board with no effect."""
    fs = frames(shot, rnd)[-12:]
    return np.median(np.stack([load(f) for f in fs]), axis=0)


print("=== board floor luminance, rec601 (findings say 181-184) ===")
for shot in ["death", "shockwave", "numbers"]:
    cp = clean_plate(shot)
    L = lum(cp)
    floor = L[(L > 150) & (L < 215)]
    print(f"  {shot:10s} clean-plate floor band: mean {floor.mean():.1f} "
          f"median {np.median(floor):.1f}  p10 {np.percentile(floor,10):.1f} p90 {np.percentile(floor,90):.1f}")

print()
print("=== effect isolation via clean plate ===")
picks = {p["shot"]: p for p in json.load(open(f"{STRIPS}/r5/picks.json"))}


def effect(shot, fidx, thresh=14, rnd="r5"):
    cp = clean_plate(shot, rnd)
    f = load(frames(shot, rnd)[fidx])
    d = np.abs(f - cp).max(axis=-1)
    m = d > thresh
    m = binary_closing(binary_opening(m, np.ones((3, 3))), np.ones((5, 5)))
    return f, cp, m


for shot in ["death", "shockwave", "numbers"]:
    p = picks[shot]
    print(f"\n--- {shot} ---")
    for key in ["windup_frame", "impact_frame", "aftermath_frame"]:
        fi = p[key]
        f, cp, m = effect(shot, fi)
        area = 100.0 * m.sum() / m.size
        if m.sum() < 50:
            print(f"  {key:16s} f{fi:03d} t={p[key.replace('frame','time')]:+.3f}s "
                  f"changed-area {area:5.2f}%  (nothing)")
            continue
        ys, xs = np.where(m)
        L = lum(f)[m]
        S = sat(f)[m]
        px_cell = f.shape[1] / (f.shape[1] / 218.0)  # placeholder
        print(f"  {key:16s} f{fi:03d} t={p[key.replace('frame','time')]:+.3f}s "
              f"changed-area {area:5.2f}%  bbox {xs.max()-xs.min()+1}x{ys.max()-ys.min()+1}px "
              f"meanL {L.mean():6.1f}  meanS {S.mean():.3f}  "
              f"L<40 {100.0*(L<40).sum()/L.size:5.1f}%  L>240 {100.0*(L>240).sum()/L.size:5.1f}%")

print()
print("=== full-frame size reference ===")
f0 = load(frames("death")[0])
print(f"  raw capture is {f0.shape[1]}x{f0.shape[0]}; findings say 1 cell ~= 218px at 1024")
print(f"  -> 1 cell ~= {218.0 * f0.shape[1] / 1024.0:.0f}px here")
