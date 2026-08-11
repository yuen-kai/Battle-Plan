"""Round-3 aftermath: visual crops + refined colour/hue checks."""
import numpy as np, glob, os
from PIL import Image
from scipy import ndimage

PC = 218.0; FPS = 30.0; T0 = -0.40
DIR = "Captures/AbilityJuice/shots/r3/aftermath"
FR = sorted(glob.glob(os.path.join(DIR, "f*.jpg")))
def load(p): return np.asarray(Image.open(p).convert("RGB")).astype(np.float64)
def frame(i): return load(FR[i])
def lum(a): return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]
def sat(a):
    mx = a.max(axis=-1); mn = a.min(axis=-1)
    return np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0.0)
def hue(a):
    r, g, b = a[...,0], a[...,1], a[...,2]
    mx = a.max(axis=-1); mn = a.min(axis=-1); d = mx-mn
    h = np.zeros_like(mx); m = d > 1e-6
    im = m & (mx == r); h[im] = ((g-b)[im]/d[im]) % 6
    im = m & (mx == g); h[im] = ((b-r)[im]/d[im]) + 2
    im = m & (mx == b); h[im] = ((r-g)[im]/d[im]) + 4
    return h*60.0

A0 = frame(0); L0 = lum(A0)

print("=== luminance band breakdown of the saturated warm pixels (f29, +0.57) ===")
A = frame(29); L = lum(A); S = sat(A)
for lo, hi in [(200,235),(235,256),(160,200),(110,160),(60,110),(0,60)]:
    m = (L>=lo)&(L<hi)&(S>0.45)
    if m.sum():
        print(f"  L {lo:3d}-{hi:3d} sat>0.45: {int(m.sum()):7d} px = {m.sum()/PC**2:.3f} cells2  "
              f"mean rgb {A[m].mean(0).round(1)}  hue {np.median(hue(A[m])):.1f}")

print("\n=== hue histogram of our hot+sat pixels vs Clash Mini's ===")
m = (L>200)&(S>0.45)
h = hue(A[m])
print(f"  ours f29: n={m.sum()} hue p10 {np.percentile(h,10):.0f} med {np.median(h):.0f} "
      f"p90 {np.percentile(h,90):.0f}  B-channel median {np.median(A[m][:,2]):.0f}")
for p in ["cm-everyability-23","cm-everyability-20","cm-8newabilities-12","cm-8newabilities-13",
          "cm-season2-05","cm-everyability-30"]:
    im = load(f"Captures/AbilityJuice/strips/reference/{p}.jpg")[:, -512:, :]
    Lr = lum(im); Sr = sat(im); mr = (Lr>200)&(Sr>0.45)
    if mr.sum() > 50:
        hr = hue(im[mr])
        print(f"  {p:22s} n={int(mr.sum()):6d} hue p10 {np.percentile(hr,10):5.0f} "
              f"med {np.median(hr):5.0f} p90 {np.percentile(hr,90):5.0f}  "
              f"sat med {np.median(Sr[mr]):.2f}  rgb {im[mr].mean(0).round(0)}")

print("\n=== warm scorch on the aftermath panel f42 (+1.00) ===")
A42 = frame(42); L42 = lum(A42); S42 = sat(A42)
warm = (A42[...,0]-A42[...,2] > 30)
lab, n = ndimage.label(warm)
sizes = ndimage.sum(warm, lab, range(1, n+1))
big = lab == (np.argmax(sizes)+1)
mult = A42[big].mean(0)/A0[big].mean(0)
print(f"  largest warm blob {big.sum()/PC**2:.2f} cells2  rgb {A42[big].mean(0).round(1)} "
      f"mult {mult.round(3)}  sat {S42[big].mean():.3f} hue {np.median(hue(A42[big])):.0f} "
      f"medL {np.median(L42[big]):.1f}  maxL-in-frame {L42.max():.1f}")
ys,xs = np.nonzero(big)
print(f"  bbox {(xs.max()-xs.min()+1)/PC:.2f} x {(ys.max()-ys.min()+1)/PC:.2f} cells")

print("\n=== occlusion check: does the plume kill tile seams? (f29) ===")
# seam contrast = local std along a horizontal scan through clean vs covered floor
def seamcontrast(A, y0, y1, x0, x1):
    reg = lum(A)[y0:y1, x0:x1]
    return float(np.abs(np.diff(reg, axis=1)).max())
print(f"  clean floor seam step (f0): {seamcontrast(A0, 700, 760, 200, 900):.1f}")
print(f"  under plume (f29):          {seamcontrast(frame(29), 200, 260, 380, 700):.1f}")
print(f"  under scorch (f42):         {seamcontrast(frame(42), 470, 530, 420, 640):.1f}")

print("\n=== end-of-life pop check ===")
for i in [48, 58, 60, 61, 62, 63]:
    Ai = frame(i); d = lum(Ai)-L0
    print(f"  t={T0+i/FPS:+.2f} max|dL| {np.abs(d).max():6.1f}  px|d|>8 {int((np.abs(d)>8).sum()):7d}"
          f"  px|d|>20 {int((np.abs(d)>20).sum()):7d}")

# ---------------------------------------------------------------- crops
os.makedirs("Captures/AbilityJuice/_c4", exist_ok=True)
def savecrop(A, box, path, scale=2):
    y0,y1,x0,x1 = box
    im = Image.fromarray(A[y0:y1, x0:x1].astype(np.uint8))
    im = im.resize(((x1-x0)*scale, (y1-y0)*scale), Image.LANCZOS)
    im.save(path, quality=95)

savecrop(frame(29), (60, 620, 220, 780), "Captures/AbilityJuice/_c4/ours_impact_zoom.jpg", 1)
savecrop(frame(42), (280, 720, 300, 740), "Captures/AbilityJuice/_c4/ours_after_zoom.jpg", 1)

# reference right panels zoomed to the same relative scale
for p in ["cm-everyability-23", "cm-8newabilities-12"]:
    im = load(f"Captures/AbilityJuice/strips/reference/{p}.jpg")[:, -512:, :]
    Image.fromarray(im.astype(np.uint8)).resize((560,560), Image.LANCZOS).save(
        f"Captures/AbilityJuice/_c4/ref_{p}.jpg", quality=95)

# squint test: 3-panel strip at thumbnail size, blown back up
for tag, path in [("r3","Captures/AbilityJuice/strips/r3/aftermath.jpg"),
                  ("r2","Captures/AbilityJuice/strips/r2/aftermath.jpg"),
                  ("ref23","Captures/AbilityJuice/strips/reference/cm-everyability-23.jpg"),
                  ("ref12","Captures/AbilityJuice/strips/reference/cm-8newabilities-12.jpg")]:
    im = Image.open(path).convert("RGB")
    im.resize((194,64), Image.LANCZOS).resize((776,256), Image.NEAREST).save(
        f"Captures/AbilityJuice/_c4/squint_{tag}.jpg", quality=95)

# side-by-side: our aftermath panel next to two CM aftermath panels
ours = Image.open("Captures/AbilityJuice/strips/r3/aftermath.jpg").convert("RGB").crop((1040,0,1552,512))
a = Image.open("Captures/AbilityJuice/strips/reference/cm-everyability-23.jpg").convert("RGB").crop((1040,0,1552,512))
b = Image.open("Captures/AbilityJuice/strips/reference/cm-8newabilities-12.jpg").convert("RGB").crop((1040,0,1552,512))
sheet = Image.new("RGB", (512*3+16, 512), (0,0,0))
for k, im in enumerate([ours, a, b]):
    sheet.paste(im, (k*(512+8), 0))
sheet.save("Captures/AbilityJuice/_c4/panel3_vs_ref.jpg", quality=92)

# our impact panel vs CM's impact panels (middle)
ours2 = Image.open("Captures/AbilityJuice/strips/r3/aftermath.jpg").convert("RGB").crop((520,0,1032,512))
a2 = Image.open("Captures/AbilityJuice/strips/reference/cm-everyability-23.jpg").convert("RGB").crop((520,0,1032,512))
b2 = Image.open("Captures/AbilityJuice/strips/reference/cm-8newabilities-12.jpg").convert("RGB").crop((520,0,1032,512))
sheet = Image.new("RGB", (512*3+16, 512), (0,0,0))
for k, im in enumerate([ours2, a2, b2]):
    sheet.paste(im, (k*(512+8), 0))
sheet.save("Captures/AbilityJuice/_c4/panel2_vs_ref.jpg", quality=92)
print("\nwrote crops to Captures/AbilityJuice/_c4/")
