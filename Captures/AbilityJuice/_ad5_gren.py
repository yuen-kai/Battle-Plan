import sys, glob, json
import numpy as np
from PIL import Image
sys.path.insert(0, "Captures/AbilityJuice")
from _ad5_lib import *
from scipy.ndimage import binary_opening, binary_closing, binary_dilation, label

OUT = "Captures/AbilityJuice"


def frames(shot, rnd="r5"):
    return sorted(glob.glob(f"Captures/AbilityJuice/shots/{rnd}/{shot}/f*.jpg"))


picks = {q["shot"]: q for q in json.load(open(f"{STRIPS}/r5/picks.json"))}
fi = picks["grenade"]["impact_frame"]
f = load(frames("grenade")[fi])
L, S, H = lum(f), sat(f), hue(f)

print(f"=== GRENADE impact frame f{fi} (t={picks['grenade']['impact_time']:+.3f}s) ===")
# find yellow numeral pixels: hue 45-70, high sat, high L
num = (H > 40) & (H < 72) & (S > 0.55) & (L > 170)
num = binary_opening(num, np.ones((3, 3)))
lab, n = label(binary_closing(num, np.ones((9, 9))))
sizes = np.bincount(lab.ravel()); sizes[0] = 0
keep = [i for i in range(1, n+1) if sizes[i] > 250]
print(f"  yellow-numeral blobs >=250px: {len(keep)}  sizes {[int(sizes[i]) for i in keep]}")
for i in keep:
    ys, xs = np.where(lab == i)
    # local background ring
    bl = np.zeros_like(num); bl[ys, xs] = True
    ring = binary_dilation(bl, np.ones((25, 25))) & ~binary_dilation(bl, np.ones((9, 9)))
    print(f"    blob at ({xs.mean():.0f},{ys.mean():.0f}) {xs.max()-xs.min()+1}x{ys.max()-ys.min()+1}px "
          f"fillL {L[bl].mean():.0f} S {S[bl].mean():.2f} | surround L {L[ring].mean():.0f} "
          f"(delta {L[bl].mean()-L[ring].mean():+.0f}) darkfrac(L<90) {100.0*(L[ring]<90).mean():.0f}%")

print()
print("=== how much of the grenade impact panel is already yellow/warm+bright? ===")
warm = (H > 15) & (H < 75) & (S > 0.45) & (L > 170)
print(f"  warm bright pixels in frame: {100.0*warm.sum()/warm.size:.2f}%")
print(f"  of those, numeral blobs account for {100.0*sum(sizes[i] for i in keep)/max(1,warm.sum()):.1f}%")
print("  -> the rest is explosion fire in the SAME hue band as the damage numbers")

# crops
p = panels(load(f"{STRIPS}/r5/grenade.jpg"))[1]
Image.fromarray(np.asarray(Image.fromarray(p.astype(np.uint8)).resize((760,760), Image.LANCZOS))).save(
    f"{OUT}/_ad5_gren_zoom.jpg", quality=94)
g = np.stack([lum(p)]*3, axis=-1)
sq = lambda a, k=40: np.asarray(Image.fromarray(a.astype(np.uint8)).resize((k,k), Image.BILINEAR).resize((380,380), Image.NEAREST))
rs = lambda a: np.asarray(Image.fromarray(a.astype(np.uint8)).resize((380,380), Image.LANCZOS))
Image.fromarray(np.concatenate([
    np.concatenate([rs(p), rs(g)], axis=1),
    np.concatenate([sq(p), sq(g)], axis=1)]).astype(np.uint8)).save(f"{OUT}/_ad5_gren_cmp.jpg", quality=93)

# numbers strip: colour / grey / squint
pn5 = panels(load(f"{STRIPS}/r5/numbers.jpg"))[1]
pn4 = panels(load(f"{STRIPS}/r4/numbers.jpg"))[1]
def tri(a):
    a = a[60:300, 130:400]
    return np.concatenate([rs(a), rs(np.stack([lum(a)]*3, -1)), sq(np.stack([lum(a)]*3, -1), 28)], axis=1)
Image.fromarray(np.concatenate([tri(pn4), tri(pn5)]).astype(np.uint8)).save(f"{OUT}/_ad5_num_cmp.jpg", quality=93)
print(f"\n  wrote _ad5_gren_zoom.jpg, _ad5_gren_cmp.jpg, _ad5_num_cmp.jpg")

print()
print("=== all three r5 pieces: panel 3 vs panel 1 ('nothing changed' test) ===")
for name in ["death", "shockwave", "numbers"]:
    ps = panels(load(f"{STRIPS}/r5/{name}.jpg"))
    d13 = np.abs(ps[2] - ps[0]).max(axis=-1)
    d12 = np.abs(ps[1] - ps[0]).max(axis=-1)
    print(f"  {name:10s} p3-vs-p1 changed>20: {100.0*(d13>20).mean():5.2f}%   "
          f"p2-vs-p1 changed>20: {100.0*(d12>20).mean():5.2f}%")
refs = sorted(glob.glob(f"{STRIPS}/reference/*.jpg"))
vals = []
for r in refs:
    ps = panels(load(r))
    vals.append(100.0*(np.abs(ps[2]-ps[0]).max(axis=-1) > 20).mean())
print(f"  Clash Mini p3-vs-p1 changed>20: median {np.median(vals):.2f}%  min {min(vals):.2f}%  max {max(vals):.2f}%")
