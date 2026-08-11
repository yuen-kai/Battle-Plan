"""Step 11 - fair comparison: subtract each game's own board saturation baseline."""
import numpy as np
from PIL import Image

def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def stats(img):
    a = np.asarray(img.convert("RGB")).astype(np.float64)
    mx = a.max(-1); mn = a.min(-1)
    s = np.where(mx > 1e-6, (mx-mn)/np.maximum(mx,1), 0)
    L = lum(a)
    return dict(s45=float((s>0.45).mean()), medL=float(np.median(L)),
                p99L=float(np.percentile(L,99)), hot=float((L>240).mean()))

print("=== board baseline (LEFT panel) vs aftermath (RIGHT panel) ===")
print(f"{'strip':26s} {'base sat>.45':>13} {'after sat>.45':>14} {'DELTA':>8} "
      f"{'baseMedL':>9} {'afterP99L':>10} {'after hot%':>11}")
st = Image.open("Captures/AbilityJuice/strips/r2/aftermath.jpg"); w,h = st.size; pw = w//3
# our own clean board baseline = a clean frame
clean = Image.open("Captures/AbilityJuice/shots/r2/aftermath/f0000.jpg")
b = stats(clean)
for k, nm in ((0,"OURS p1 t=0.43"), (1,"OURS p2 t=0.57"), (2,"OURS p3 t=1.00")):
    a = stats(st.crop((k*pw,0,(k+1)*pw,h)))
    print(f"{nm:26s} {100*b['s45']:12.2f}% {100*a['s45']:13.2f}% "
          f"{100*(a['s45']-b['s45']):+7.2f}% {b['medL']:9.0f} {a['p99L']:10.0f} {100*a['hot']:10.2f}%")
for n in ("cm-8newabilities-13","cm-everyability-20","cm-everyability-23",
          "cm-8newabilities-07","cm-8newabilities-20","cm-everyability-30"):
    s = Image.open(f"Captures/AbilityJuice/strips/reference/{n}.jpg"); w2,h2 = s.size; p2 = w2//3
    bb = stats(s.crop((0,0,p2,h2)))          # left panel ~ board + pre-cast
    aa = stats(s.crop((2*p2,0,3*p2,h2)))
    print(f"{n:26s} {100*bb['s45']:12.2f}% {100*aa['s45']:13.2f}% "
          f"{100*(aa['s45']-bb['s45']):+7.2f}% {bb['medL']:9.0f} {aa['p99L']:10.0f} {100*aa['hot']:10.2f}%")
