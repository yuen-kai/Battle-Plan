"""Round-3 aftermath: internal structure of the new gold ember + mark edge + panel deltas."""
import numpy as np, glob, os
from PIL import Image
from scipy import ndimage

PC = 218.0; FPS = 30.0; T0 = -0.40
FR = sorted(glob.glob("Captures/AbilityJuice/shots/r3/aftermath/f*.jpg"))
def load(p): return np.asarray(Image.open(p).convert("RGB")).astype(np.float64)
def frame(i): return load(FR[i])
def lum(a): return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]
def sat(a):
    mx = a.max(axis=-1); mn = a.min(axis=-1)
    return np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0.0)
A0 = frame(0); L0 = lum(A0)

print("=== gold ember mass: internal luminance spread (uniformity law: CM lobes 30-45) ===")
for i in (25, 29):
    A = frame(i); L = lum(A); S = sat(A)
    m = (S > 0.45) & (L > 130) & (A[...,0]-A[...,2] > 60)
    lab, n = ndimage.label(m)
    sizes = ndimage.sum(m, lab, range(1, n+1))
    big = lab == (np.argmax(sizes)+1)
    v = L[big]
    print(f"  f{i} t={T0+i/FPS:+.2f}  area {big.sum()/PC**2:.2f} cells2  "
          f"L mean {v.mean():.1f} std {v.std():.1f}  p5 {np.percentile(v,5):.0f} "
          f"p95 {np.percentile(v,95):.0f}  spread(p95-p5) {np.percentile(v,95)-np.percentile(v,5):.1f}")
    ys, xs = np.nonzero(big)
    print(f"      bbox {(xs.max()-xs.min()+1)/PC:.2f} x {(ys.max()-ys.min()+1)/PC:.2f} cells   "
          f"fill {big.sum()/((xs.max()-xs.min()+1)*(ys.max()-ys.min()+1)):.2f}")

print("\n=== edge hardness: luminance step per pixel across silhouettes (law: 2-4px step) ===")
def edgeramp(A, name, mask):
    L = lum(A)
    gy, gx = np.gradient(L)
    g = np.hypot(gx, gy)
    er = ndimage.binary_dilation(mask, iterations=2) & ~ndimage.binary_erosion(mask, iterations=2)
    if er.sum() < 50: return
    print(f"  {name:26s} edge |dL/dpx| median {np.median(g[er]):5.1f}  p90 {np.percentile(g[er],90):5.1f}")
A29 = frame(29); L29 = lum(A29)
edgeramp(A29, "smoke plume (f29)", (L29-L0) < -60)
edgeramp(A29, "gold ember (f29)", (sat(A29)>0.45)&(L29>150)&(A29[...,0]-A29[...,2]>60))
A42 = frame(42)
edgeramp(A42, "ground mark (f42)", (A42[...,0]-A42[...,2]) > 30)
print("  reference for scale:")
for p in ["cm-everyability-23", "cm-8newabilities-12"]:
    im = load(f"Captures/AbilityJuice/strips/reference/{p}.jpg")[:, -512:, :]
    Lr = lum(im)
    hot = (Lr>150)&(sat(im)>0.45)
    edgeramp(np.asarray(Image.fromarray(im.astype(np.uint8)).resize((1024,1024), Image.LANCZOS)).astype(float),
             p, np.asarray(Image.fromarray((hot*255).astype(np.uint8)).resize((1024,1024), Image.NEAREST))>127)

print("\n=== ground mark radial profile at f42: how many px does the edge take? ===")
warm = (A42[...,0]-A42[...,2]) > 30
lab, n = ndimage.label(warm); sizes = ndimage.sum(warm, lab, range(1, n+1))
big = lab == (np.argmax(sizes)+1)
ys, xs = np.nonzero(big); cy, cx = int(ys.mean()), int(xs.mean())
row = lum(A42)[cy, :]
base = np.median(L0[cy, :])
inside = np.nonzero(big[cy, :])[0]
if len(inside):
    x1 = inside.max()
    prof = row[x1-6:x1+22]
    print(f"  scan y={cy}, mark right edge x={x1}, floor L={base:.0f}")
    print("  L: " + " ".join(f"{v:.0f}" for v in prof))
    d = np.abs(np.diff(prof))
    print(f"  max single-px step {d.max():.1f}; px to travel 80% of the "
          f"{abs(prof[0]-prof[-1]):.0f}-level range: "
          f"{int(np.sum(np.cumsum(d) < 0.8*d.sum()))}")

print("\n=== panel-to-panel change (brief: 'nothing changed') ===")
st = load("Captures/AbilityJuice/strips/r3/aftermath.jpg")
p1, p2, p3 = st[:, 0:512], st[:, 520:1032], st[:, 1040:1552]
for a, b, nm in [(p1,p2,"panel1 vs panel2"), (p1,p3,"panel1 vs panel3"), (p2,p3,"panel2 vs panel3")]:
    print(f"  {nm}: mean|dRGB| {np.abs(a-b).mean():6.2f}  "
          f"px>20 {100*(np.abs(lum(a)-lum(b))>20).mean():5.2f}%")
for tag, f in [("ref23","cm-everyability-23"), ("ref12","cm-8newabilities-12")]:
    st = load(f"Captures/AbilityJuice/strips/reference/{f}.jpg")
    a, b = st[:, 0:512], st[:, 1040:1552]
    print(f"  {tag} panel1 vs panel3: mean|dRGB| {np.abs(a-b).mean():6.2f}  "
          f"px>20 {100*(np.abs(lum(a)-lum(b))>20).mean():5.2f}%")

print("\n=== r2 -> r3 headline deltas ===")
for tag, d in [("r2","Captures/AbilityJuice/shots/r2/aftermath"),
               ("r3","Captures/AbilityJuice/shots/r3/aftermath")]:
    fs = sorted(glob.glob(os.path.join(d, "f*.jpg")))
    best = (0, 0, 0)
    for i, f in enumerate(fs):
        A = load(f); L = lum(A); S = sat(A)
        n = int(((L>200)&(S>0.45)).sum())
        if n > best[1]: best = (i, n, T0+i/FPS)
    print(f"  {tag}: peak hot+sat {best[1]} px = {best[1]/PC**2:.3f} cells2 "
          f"({100*best[1]/1048576:.2f}% frame) at t={best[2]:+.2f}")

# stacked squint sheet: ours over the two strongest references
rows = ["Captures/AbilityJuice/strips/r3/aftermath.jpg",
        "Captures/AbilityJuice/strips/reference/cm-everyability-23.jpg",
        "Captures/AbilityJuice/strips/reference/cm-8newabilities-12.jpg",
        "Captures/AbilityJuice/strips/reference/cm-everyability-20.jpg"]
sh = Image.new("RGB", (776, 256*4+12), (20,20,20))
for k, p in enumerate(rows):
    im = Image.open(p).convert("RGB").resize((194,64), Image.LANCZOS).resize((776,256), Image.NEAREST)
    sh.paste(im, (0, k*(256+4)))
sh.save("Captures/AbilityJuice/_c4/squint_stack.jpg", quality=93)
print("\nwrote Captures/AbilityJuice/_c4/squint_stack.jpg")
