import sys, glob, json
import numpy as np
from PIL import Image
sys.path.insert(0, "Captures/AbilityJuice")
from _ad5_lib import *

OUT = "Captures/AbilityJuice"


def frames(shot, rnd="r5"):
    return sorted(glob.glob(f"Captures/AbilityJuice/shots/{rnd}/{shot}/f*.jpg"))


# death filmstrip f12..f23, cropped on the event
fs = frames("death")
row = []
for i in range(12, 24):
    f = load(fs[i])[330:790, 280:740]
    row.append(np.asarray(Image.fromarray(f.astype(np.uint8)).resize((170, 170), Image.LANCZOS)))
Image.fromarray(np.concatenate([np.concatenate(row[:6], axis=1),
                                np.concatenate(row[6:], axis=1)]).astype(np.uint8)).save(
    f"{OUT}/_ad5_death_film.jpg", quality=93)

# r4 same
fs4 = frames("death", "r4")
row4 = []
for i in range(10, 22):
    f = load(fs4[i])[330:790, 280:740]
    row4.append(np.asarray(Image.fromarray(f.astype(np.uint8)).resize((170, 170), Image.LANCZOS)))
Image.fromarray(np.concatenate([np.concatenate(row4[:6], axis=1),
                                np.concatenate(row4[6:], axis=1)]).astype(np.uint8)).save(
    f"{OUT}/_ad5_death_film_r4.jpg", quality=93)

# strip-scale squint of death vs pogo vs a CM reference
def strip_squint(path, k=48):
    im = load(path)
    g = np.stack([lum(im)] * 3, axis=-1)
    small = Image.fromarray(g.astype(np.uint8)).resize((k * 3, k), Image.BILINEAR)
    return np.asarray(small.resize((1024, 341), Image.NEAREST))

rows = [strip_squint(f"{STRIPS}/r5/death.jpg"),
        strip_squint(f"{STRIPS}/r5/pogo.jpg"),
        strip_squint(f"{STRIPS}/r5/shockwave.jpg"),
        strip_squint(f"{STRIPS}/reference/cm-everyability-20.jpg")]
Image.fromarray(np.concatenate(rows).astype(np.uint8)).save(f"{OUT}/_ad5_squint4.jpg", quality=92)
print("wrote _ad5_death_film.jpg, _ad5_death_film_r4.jpg, _ad5_squint4.jpg")

print()
print("=== cyan-area overlap: death vs pogo (a player reads colour first) ===")
for name in ["death", "pogo"]:
    p = panels(load(f"{STRIPS}/r5/{name}.jpg"))[1]
    H, S, L = hue(p), sat(p), lum(p)
    cyan = (H > 165) & (H < 215) & (S > 0.35)
    warm = (H < 45) & (S > 0.35)
    dark = L < 60
    print(f"  {name:6s}: cyan {100.0*cyan.mean():5.2f}%   warm {100.0*warm.mean():5.2f}%   "
          f"dark(L<60) {100.0*dark.mean():5.2f}%")
p4 = panels(load(f"{STRIPS}/r4/death.jpg"))[1]
H, S, L = hue(p4), sat(p4), lum(p4)
print(f"  death r4: cyan {100.0*((H>165)&(H<215)&(S>0.35)).mean():5.2f}%   "
      f"warm {100.0*((H<45)&(S>0.35)).mean():5.2f}%   dark(L<60) {100.0*(L<60).mean():5.2f}%")

print()
print("=== does anything on the r5 death panel say 'a unit died here'? ===")
# panel 3 residue vs CM aftermath
for name in ["death", "shockwave"]:
    p3 = panels(load(f"{STRIPS}/r5/{name}.jpg"))[2]
    L3, S3 = lum(p3), sat(p3)
    print(f"  {name} p3: pixels L<110 {100.0*(L3<110).mean():5.2f}%  S>0.45 {100.0*(S3>0.45).mean():5.2f}%  "
          f"L>199&S>0.45 {bright_sat_pct(p3)[0]:.3f}%")
refs = sorted(glob.glob(f"{STRIPS}/reference/*.jpg"))
v1 = [100.0*(sat(panels(load(r))[2]) > 0.45).mean() for r in refs]
print(f"  Clash Mini p3 saturated(S>0.45) area: median {np.median(v1):.2f}%  min {min(v1):.2f}%")
