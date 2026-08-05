import sys, glob, os
import numpy as np
sys.path.insert(0, "Captures/AbilityJuice")
from _ad5_lib import *

print("=== panel geometry ===")
for name in ["death", "shockwave", "numbers", "pogo", "grenade"]:
    img = load(f"{STRIPS}/r5/{name}.jpg")
    ps = panels(img)
    print(f"{name:10s} full={img.shape[1]}x{img.shape[0]}  panels=" +
          ", ".join(f"{p.shape[1]}x{p.shape[0]}" for p in ps))

print()
print("=== bright-and-saturated  L>199 & S>0.45  (% of panel) ===")
print(f"{'piece':12s} {'p1':>7s} {'p2':>7s} {'p3':>7s}   [user claim]")
claims = {"death": (1.00, 1.20, 0.00), "shockwave": (0.61, 0.99, 0.00),
          "numbers": (0.00, 0.95, 0.00)}
for name in ["death", "shockwave", "numbers"]:
    for rnd in ["r4", "r5"]:
        img = load(f"{STRIPS}/{rnd}/{name}.jpg")
        vals = [bright_sat_pct(p)[0] for p in panels(img)]
        tag = f"  claim {claims[name]}" if rnd == "r5" else ""
        print(f"{name+' '+rnd:12s} " + " ".join(f"{v:7.3f}" for v in vals) + tag)

for name in ["pogo", "grenade"]:
    img = load(f"{STRIPS}/r5/{name}.jpg")
    vals = [bright_sat_pct(p)[0] for p in panels(img)]
    print(f"{name+' r5':12s} " + " ".join(f"{v:7.3f}" for v in vals))

print()
print("=== whole-strip bright-and-saturated ===")
for name in ["death", "shockwave", "numbers"]:
    for rnd in ["r4", "r5"]:
        img = load(f"{STRIPS}/{rnd}/{name}.jpg")
        print(f"{name+' '+rnd:12s} {bright_sat_pct(img)[0]:7.3f}%")

print()
print("=== Clash Mini reference: bright-and-saturated per panel ===")
refs = sorted(glob.glob(f"{STRIPS}/reference/*.jpg"))
allp, impact = [], []
for f in refs:
    img = load(f)
    vals = [bright_sat_pct(p)[0] for p in panels(img)]
    allp.extend(vals)
    impact.append(vals[1])
    print(f"{os.path.basename(f):28s} " + " ".join(f"{v:7.3f}" for v in vals))
impact = np.array(impact)
allp = np.array(allp)
print()
print(f"CM impact panel (p2): median {np.median(impact):.3f}%  "
      f"min {impact.min():.3f}  max {impact.max():.3f}  "
      f"p25 {np.percentile(impact,25):.3f}  p75 {np.percentile(impact,75):.3f}")
print(f"CM all panels:        median {np.median(allp):.3f}%  min {allp.min():.3f}  max {allp.max():.3f}")
# aftermath panel specifically
after = np.array([bright_sat_pct(panels(load(f))[2])[0] for f in refs])
print(f"CM aftermath panel (p3): median {np.median(after):.3f}%  min {after.min():.3f}  max {after.max():.3f}")
