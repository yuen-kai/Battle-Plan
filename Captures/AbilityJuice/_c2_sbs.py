import numpy as np
from PIL import Image, ImageDraw
import os

ROOT = os.path.dirname(os.path.abspath(__file__))
REF = os.path.join(ROOT, 'strips', 'reference')


def lum(a):
    return 0.2126*a[..., 0]+0.7152*a[..., 1]+0.0722*a[..., 2]


def panels(path):
    im = np.asarray(Image.open(path).convert('RGB')).astype(np.float32)
    H, W, _ = im.shape
    col = lum(im).mean(0); dark = col < 20
    runs = []; s = None
    for x in range(W):
        if dark[x] and s is None: s = x
        if not dark[x] and s is not None: runs.append((s, x)); s = None
    gut = [r for r in runs if 2 <= r[1]-r[0] <= 30 and 0.2*W < (r[0]+r[1])/2 < 0.8*W]
    if len(gut) >= 2:
        b = [0, gut[0][0], gut[0][1], gut[1][0], gut[1][1], W]
        return [im[:, b[0]:b[1]], im[:, b[2]:b[3]], im[:, b[4]:b[5]]]
    t = W//3
    return [im[:, 0:t], im[:, t:2*t], im[:, 2*t:]]


print("== PANEL-TO-PANEL CHANGE, all reference strips + ours ==")
print("%-26s %9s %9s %9s" % ("strip", "p1->p2", "p2->p3", "p1->p3"))
vals12 = []
for f in sorted(os.listdir(REF)):
    if not f.endswith('.jpg'): continue
    P = panels(os.path.join(REF, f))
    h = min(p.shape[0] for p in P); w = min(p.shape[1] for p in P)
    P = [p[:h, :w] for p in P]
    a = np.abs(lum(P[0])-lum(P[1])).mean()
    b = np.abs(lum(P[1])-lum(P[2])).mean()
    c = np.abs(lum(P[0])-lum(P[2])).mean()
    vals12.append(a)
    print("%-26s %9.1f %9.1f %9.1f" % (f[:-4], a, b, c))
print("  reference p1->p2 : min %.1f  median %.1f  max %.1f" % (min(vals12), float(np.median(vals12)), max(vals12)))
for nm in ('r1', 'r2'):
    P = panels(os.path.join(ROOT, 'strips', nm, 'windup.jpg'))
    h = min(p.shape[0] for p in P); w = min(p.shape[1] for p in P)
    P = [p[:h, :w] for p in P]
    print("%-26s %9.1f %9.1f %9.1f" % ("OURS " + nm + " windup",
                                       np.abs(lum(P[0])-lum(P[1])).mean(),
                                       np.abs(lum(P[1])-lum(P[2])).mean(),
                                       np.abs(lum(P[0])-lum(P[2])).mean()))

# ---- side by side sheet: ours on top, three references below, matched width ----
W = 1400
rows = []
labels = []
for path, lab in ((os.path.join(ROOT, 'strips', 'r2', 'windup.jpg'), 'OURS r2 windup'),
                  (os.path.join(REF, 'cm-everyability-22.jpg'), 'CM everyability-22'),
                  (os.path.join(REF, 'cm-everyability-27.jpg'), 'CM everyability-27'),
                  (os.path.join(REF, 'cm-8newabilities-13.jpg'), 'CM 8newabilities-13')):
    im = Image.open(path).convert('RGB')
    h = int(im.height * W / im.width)
    rows.append(im.resize((W, h), Image.LANCZOS)); labels.append(lab)
Ht = sum(r.height+22 for r in rows)
sheet = Image.new('RGB', (W, Ht), (10, 10, 10))
d = ImageDraw.Draw(sheet)
y = 0
for r, lab in zip(rows, labels):
    d.text((6, y+5), lab, fill=(255, 220, 0))
    sheet.paste(r, (0, y+20)); y += r.height+22
sheet.save(os.path.join(ROOT, '_c2_sbs.png'))

# squint version
sheet.resize((int(W*0.20), int(Ht*0.20)), Image.BOX).resize((int(W*0.42), int(Ht*0.42)), Image.NEAREST)\
     .save(os.path.join(ROOT, '_c2_sbs_squint.png'))
print("\nwrote _c2_sbs.png and _c2_sbs_squint.png")
