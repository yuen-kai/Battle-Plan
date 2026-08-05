import sys, glob, json
import numpy as np
from PIL import Image
sys.path.insert(0, "Captures/AbilityJuice")
from _ad5_lib import *
from scipy.ndimage import binary_opening, binary_closing, binary_erosion, binary_dilation, label, sobel


def frames(shot, rnd="r5"):
    return sorted(glob.glob(f"Captures/AbilityJuice/shots/{rnd}/{shot}/f*.jpg"))


def clean_plate(shot, rnd="r5"):
    return np.median(np.stack([load(f) for f in frames(shot, rnd)[-12:]]), axis=0)


# ---------------- shockwave visual ----------------
f = load(frames("shockwave")[17])
crop = f[300:760, 260:720]
Image.fromarray(np.asarray(Image.fromarray(crop.astype(np.uint8)).resize((720,720), Image.LANCZOS))).save(
    "Captures/AbilityJuice/_ad5_sw_zoom.jpg", quality=95)
f4 = load(frames("shockwave","r4")[17])
c4 = f4[300:760, 260:720]
side = np.concatenate([
    np.asarray(Image.fromarray(c4.astype(np.uint8)).resize((460,460), Image.LANCZOS)),
    np.asarray(Image.fromarray(crop.astype(np.uint8)).resize((460,460), Image.LANCZOS))], axis=1)
sq = lambda a: np.asarray(Image.fromarray(a.astype(np.uint8)).resize((36,36), Image.BILINEAR).resize((460,460), Image.NEAREST))
sqrow = np.concatenate([sq(c4), sq(crop)], axis=1)
Image.fromarray(np.concatenate([side, sqrow]).astype(np.uint8)).save(
    "Captures/AbilityJuice/_ad5_sw_cmp.jpg", quality=93)

print("=== shockwave core: hardness of the core edge ===")
L = lum(f)
hot = L > 240
# scan rays from core centroid outward, measure levels/px across the core boundary
hy, hx = np.where(hot)
cx, cy = hx.mean(), hy.mean()
steps = []
for a in np.linspace(0, 2*np.pi, 180, endpoint=False):
    prof = []
    for rr in np.arange(0, 260, 1.0):
        x, y = int(round(cx+rr*np.cos(a))), int(round(cy+rr*np.sin(a)))
        if 0 <= x < L.shape[1] and 0 <= y < L.shape[0]:
            prof.append(L[y, x])
    prof = np.array(prof)
    # find where it crosses 240 going out
    idx = np.where(prof < 240)[0]
    if idx.size:
        i = idx[0]
        seg = prof[max(0, i-14):i+14]
        if seg.size > 4:
            steps.append(np.abs(np.diff(seg)).max())
steps = np.array(steps)
print(f"  core boundary max gradient per ray: median {np.median(steps):.1f} levels/px  "
      f"p10 {np.percentile(steps,10):.1f}  p90 {np.percentile(steps,90):.1f}")
print("  (findings: Clash Mini dust silhouettes 49-91 levels/px; <10 = mush)")

# how far does the core take to fall from 240 to the board 182?
falls = []
for a in np.linspace(0, 2*np.pi, 180, endpoint=False):
    prof = []
    for rr in np.arange(0, 300, 1.0):
        x, y = int(round(cx+rr*np.cos(a))), int(round(cy+rr*np.sin(a)))
        if 0 <= x < L.shape[1] and 0 <= y < L.shape[0]:
            prof.append(L[y, x])
    prof = np.array(prof)
    a1 = np.where(prof < 240)[0]
    a2 = np.where(prof < 182)[0]
    if a1.size and a2.size and a2[0] > a1[0]:
        falls.append(a2[0]-a1[0])
print(f"  distance from L240 down to board L182: median {np.median(falls):.0f}px "
      f"= {np.median(falls)/218:.2f} cells  -> {58/max(1,np.median(falls)):.2f} levels/px average ramp")

print()
print("=== shockwave: does it EXPAND? (outer radius per frame) ===")
cp = clean_plate("shockwave")
for i in range(12, 26):
    ff = load(frames("shockwave")[i])
    d = np.abs(ff-cp).max(axis=-1)
    m = binary_opening(d > 18, np.ones((5,5)))
    if m.sum() < 200:
        print(f"  f{i:03d} t={(i-12)/30:+.3f}s  (nothing)"); continue
    ys, xs = np.where(m)
    rr = np.hypot(xs-cx, ys-cy)
    print(f"  f{i:03d} t={(i-12)/30:+.3f}s  area {100.0*m.sum()/m.size:5.2f}%  "
          f"p95 radius {np.percentile(rr,95):5.1f}px ({np.percentile(rr,95)/218:.2f} cells)  "
          f"bbox {xs.max()-xs.min()}x{ys.max()-ys.min()}")

# ---------------- numbers ----------------
print()
print("=== NUMBERS: verify the three tone claims (L 211/215/219 at S 0.77/0.86/0.92) ===")
picks = {q["shot"]: q for q in json.load(open(f"{STRIPS}/r5/picks.json"))}
for rnd in ["r4", "r5"]:
    pk = {q["shot"]: q for q in json.load(open(f"{STRIPS}/{rnd}/picks.json"))}
    fi = pk["numbers"]["impact_frame"]
    ff = load(frames("numbers", rnd)[fi])
    cp2 = clean_plate("numbers", rnd)
    d = np.abs(ff-cp2).max(axis=-1)
    m = binary_closing(binary_opening(d > 14, np.ones((3,3))), np.ones((3,3)))
    L, S, H = lum(ff), sat(ff), hue(ff)
    ys, xs = np.where(m)
    print(f"\n  {rnd} f{fi}: mark bbox {xs.max()-xs.min()+1}x{ys.max()-ys.min()+1}px "
          f"({(xs.max()-xs.min()+1)/218:.2f} x {(ys.max()-ys.min()+1)/218:.2f} cells), area {m.sum()}px")
    fill = m & (S > 0.5) & (L > 150)
    stroke = m & (L < 70)
    print(f"    yellow fill: n={fill.sum()}  meanL {L[fill].mean():.1f}  p50 {np.median(L[fill]):.1f} "
          f"p95 {np.percentile(L[fill],95):.1f}  meanS {S[fill].mean():.3f}  meanHue {np.median(H[fill]):.0f}")
    print(f"    dark stroke: n={stroke.sum()}  meanL {L[stroke].mean():.1f}  "
          f"fraction of mark that is stroke {100.0*stroke.sum()/m.sum():.1f}%")
    print(f"    fraction of mark that is fill  {100.0*fill.sum()/m.sum():.1f}%")
    print(f"    L>200 within mark {100.0*(L[m]>200).mean():.1f}%   bright&sat {100.0*((L[m]>199)&(S[m]>0.45)).mean():.1f}%")
