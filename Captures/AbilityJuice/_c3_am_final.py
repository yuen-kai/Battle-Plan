"""Step 10 - unbiased opacity, ground-mark identity (burn vs shadow),
and the exact colour of the 'fire'."""
import numpy as np, glob, os
from PIL import Image, ImageDraw
from scipy import ndimage

PC = 218.0
DIR = "Captures/AbilityJuice/shots/r2/aftermath"
FR = sorted(glob.glob(os.path.join(DIR, "f*.jpg")))
def load(i): return np.asarray(Image.open(FR[i]).convert("RGB")).astype(np.float64)
def lum(a):  return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def tt(i):   return -0.40+i/30.0
def hipass(X, s=9): return X - ndimage.uniform_filter(X, s)
A0 = load(0); L0 = lum(A0); FLOOR = float(np.percentile(L0, 50))

# ---- unbiased opacity: select by SMOOTHED delta, not by final luminance ----
print("=== OPACITY (unbiased: regions selected by smoothed density, not final L) ===")
hp0 = hipass(L0)
for i in (25, 29, 33):
    A = load(i); L = lum(A)
    dens = ndimage.uniform_filter(L0 - L, 21)      # smoothed darkening, floor-structure-free
    hp = hipass(L)
    for lo, hi, nm in ((150, 400, "densest  >150"), (110, 150, "dense 110-150"),
                       (70, 110, "mid    70-110"), (30, 70, "thin    30-70")):
        m = (dens >= lo) & (dens < hi) & (np.abs(hp0) > 5)
        m = ndimage.binary_erosion(m, np.ones((5,5)))
        if m.sum() < 300: 
            print(f" t={tt(i):+.2f} {nm}: n={m.sum()}"); continue
        a, b = hp0[m], hp[m]
        sl = float(np.polyfit(a, b, 1)[0])
        print(f" t={tt(i):+.2f} {nm}: n={m.sum():6d}  transmittance {sl:+.3f} "
              f"-> OPACITY {100*(1-max(sl,0)):4.0f}%   (mean L here {L[m].mean():.0f})")
    print()

# ---- ground mark: burn or shadow? -----------------------------------------
print("=== GROUND MARK identity ===")
for i in (46, 52, 58):
    A = load(i); L = lum(A); d = L - L0
    m = ndimage.binary_erosion(d < -40, np.ones((5,5)))
    if m.sum() < 300: continue
    src = A0[m]; dst = A[m]
    ratio = dst / np.maximum(src, 1e-6)
    print(f" t={tt(i):+.2f}: n={m.sum()}")
    print(f"   clean RGB under mark  {src.mean(0).round(1)}   hue-ish R-B {(src[:,0]-src[:,2]).mean():+.1f}")
    print(f"   marked RGB            {dst.mean(0).round(1)}   hue-ish R-B {(dst[:,0]-dst[:,2]).mean():+.1f}")
    print(f"   per-channel multiplier {ratio.mean(0).round(3)}  "
          f"(equal across RGB = neutral shadow; R>B = warm burn)")
    print(f"   mean luminance delta {d[m].mean():+.1f}   darkest {d[m].min():+.1f}")

# ---- exact colour of the 'fire' -------------------------------------------
print("\n=== THE FIRE: exact colours of the brightest ember body ===")
for i in (29, 34, 38):
    A = load(i); L = lum(A)
    RmB = A[...,0]-A[...,2]
    body = ndimage.binary_erosion((L > 195) & (RmB > 12) & ((L-L0) > 12), np.ones((5,5)))
    rim  = ndimage.binary_dilation(body, np.ones((9,9))) & ~body & (RmB > 30)
    if body.sum() < 50:
        print(f" t={tt(i):+.2f}: body {body.sum()} px"); continue
    b = A[body]; mx = b.max(1); mn = b.min(1)
    print(f" t={tt(i):+.2f}  BODY {body.sum():5d}px ({body.sum()/PC**2:.3f} cells^2)  "
          f"mean RGB {b.mean(0).round(0)}  sat {np.mean((mx-mn)/np.maximum(mx,1)):.2f}  "
          f"L {lum(b).mean():.0f} ({lum(b).mean()/FLOOR:.2f}x floor)")
    if rim.sum() > 50:
        r = A[rim]; mx2 = r.max(1); mn2 = r.min(1)
        print(f"          RIM  {rim.sum():5d}px  mean RGB {r.mean(0).round(0)}  "
              f"sat {np.mean((mx2-mn2)/np.maximum(mx2,1)):.2f}  L {lum(r).mean():.0f} "
              f"({lum(r).mean()/FLOOR:.2f}x floor)")

# ---- what fraction of the frame is saturated colour? vs Clash Mini --------
print("\n=== SATURATED-COLOUR COVERAGE: ours vs Clash Mini aftermath panels ===")
def cov(img):
    a = np.asarray(img.convert("RGB")).astype(np.float64)
    mx = a.max(-1); mn = a.min(-1)
    s = np.where(mx > 1e-6, (mx-mn)/np.maximum(mx,1), 0)
    return float((s > 0.45).mean()), float((s > 0.30).mean()), float(lum(a).max())
st = Image.open("Captures/AbilityJuice/strips/r2/aftermath.jpg"); w,h = st.size; pw = w//3
for k in range(3):
    c45, c30, mL = cov(st.crop((k*pw, 0, (k+1)*pw, h)))
    print(f" OURS panel {k+1}: sat>0.45 {100*c45:5.2f}%   sat>0.30 {100*c30:5.2f}%   maxL {mL:.0f}")
for n in ("cm-8newabilities-13","cm-everyability-20","cm-everyability-23","cm-8newabilities-07","cm-8newabilities-20"):
    s = Image.open(f"Captures/AbilityJuice/strips/reference/{n}.jpg"); w2,h2 = s.size; p2 = w2//3
    c45, c30, mL = cov(s.crop((2*p2, 0, 3*p2, h2)))
    print(f" {n:22s} R-panel: sat>0.45 {100*c45:5.2f}%   sat>0.30 {100*c30:5.2f}%   maxL {mL:.0f}")

# ---- squint sheet using OUR strongest frame too ---------------------------
outs = [("OURS p1 t=0.43", st.crop((0,0,pw,h))),
        ("OURS p2 t=0.57", st.crop((pw,0,2*pw,h))),
        ("OURS p3 t=1.00", st.crop((2*pw,0,3*pw,h)))]
for n in ("cm-8newabilities-13","cm-everyability-23","cm-8newabilities-07"):
    s = Image.open(f"Captures/AbilityJuice/strips/reference/{n}.jpg"); w2,h2=s.size; p2=w2//3
    outs.append((n[3:], s.crop((2*p2,0,3*p2,h2))))
TH=250
sq = Image.new("RGB",(TH*len(outs), TH*2+18),(14,14,16)); d = ImageDraw.Draw(sq)
for k,(nm,im) in enumerate(outs):
    sq.paste(im.resize((TH,TH), Image.LANCZOS),(k*TH,0))
    sq.paste(im.resize((22,22), Image.LANCZOS).resize((TH,TH), Image.NEAREST),(k*TH,TH+18))
    d.text((k*TH+5,TH+4), nm, fill=(235,235,235))
sq.save("Captures/AbilityJuice/_c3_squint2.png")
print("\nwrote _c3_squint2.png")
