"""Step 8 - visual crops: seam under smoke, embers magnified, ground-mark edge,
and a squint test of ours vs the Clash Mini bar."""
import numpy as np, glob, os
from PIL import Image, ImageDraw
from scipy import ndimage

PC = 218.0; T0 = -0.40
DIR = "Captures/AbilityJuice/shots/r2/aftermath"
FR = sorted(glob.glob(os.path.join(DIR, "f*.jpg")))
def load(i): return np.asarray(Image.open(FR[i]).convert("RGB")).astype(np.float64)
def lum(a):  return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
A0 = load(0); L0 = lum(A0)

def crop(i, y0, y1, x0, x1, scale=4):
    im = Image.open(FR[i]).convert("RGB").crop((x0, y0, x1, y1))
    return im.resize(((x1-x0)*scale, (y1-y0)*scale), Image.NEAREST)

# 1. seam under the thickest smoke (x=618 seam, deep rows)
A = load(29); L = lum(A)
deep = ndimage.binary_erosion(L < 45, np.ones((5,5)))
ys, xs = np.nonzero(deep)
print(f"deep smoke rows {ys.min()}..{ys.max()} cols {xs.min()}..{xs.max()}")
sel = [y for y in range(ys.min(), ys.max()) if deep[y, 600:640].all()]
print(f"seam x=618 fully deep on rows: {sel[:5]} ... {sel[-5:] if sel else ''} ({len(sel)})")
if sel:
    y0 = sel[len(sel)//2]-40; y1 = y0+80
    crop(29, max(0,y0), min(1024,y1), 560, 680, 5).save("Captures/AbilityJuice/_c3_seam_smoke.png")
    Image.fromarray(np.uint8(A0[max(0,y0):min(1024,y1), 560:680])).resize((600, 400), Image.NEAREST)\
        .save("Captures/AbilityJuice/_c3_seam_clean.png")
    row = L[y0+40, 560:680]
    print("  L along seam under smoke:", np.round(row[::6], 1).tolist())
    print("  L along same seam clean  :", np.round(L0[y0+40, 560:680][::6], 1).tolist())

# 2. embers magnified - find the warm cluster
for i in (25, 29, 38):
    A = load(i)
    RmB = A[...,0]-A[...,2]
    m = RmB > 45
    if m.sum() < 50: continue
    ys, xs = np.nonzero(m)
    cy, cx = int(ys.mean()), int(xs.mean())
    h = 160
    crop(i, max(0,cy-h), min(1024,cy+h), max(0,cx-h), min(1024,cx+h), 3)\
        .save(f"Captures/AbilityJuice/_c3_ember_{i}.png")
    print(f"ember crop f{i} centred ({cy},{cx})")

# 3. ground mark late, with edge
crop(52, 300, 780, 380, 860, 2).save("Captures/AbilityJuice/_c3_groundmark.png")

# 4. squint test: ours panel3 vs CM references, all reduced to 48px wide
outs = []
strip = Image.open("Captures/AbilityJuice/strips/r2/aftermath.jpg").convert("RGB")
w, h = strip.size; pw = w//3
ours = strip.crop((2*pw, 0, 3*pw, h))
outs.append(("OURS", ours))
for n in ("cm-8newabilities-13", "cm-everyability-20", "cm-everyability-23", "cm-8newabilities-07"):
    s = Image.open(f"Captures/AbilityJuice/strips/reference/{n}.jpg").convert("RGB")
    w2, h2 = s.size; p2 = w2//3
    outs.append((n, s.crop((2*p2, 0, 3*p2, h2))))

TH = 260
sq = Image.new("RGB", (TH*len(outs), TH*2+18), (16,16,18))
d = ImageDraw.Draw(sq)
for k, (nm, im) in enumerate(outs):
    full = im.resize((TH, TH), Image.LANCZOS)
    sq.paste(full, (k*TH, 0))
    tiny = im.resize((26, 26), Image.LANCZOS).resize((TH, TH), Image.NEAREST)
    sq.paste(tiny, (k*TH, TH+18))
    d.text((k*TH+6, TH+4), nm, fill=(230,230,230))
sq.save("Captures/AbilityJuice/_c3_squint.png")
print("wrote squint sheet")
