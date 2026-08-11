import numpy as np
from PIL import Image, ImageDraw, ImageFilter

P3 = 'Captures/AbilityJuice/shots/r3/hitreact/f%04d.jpg'
P2 = 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'

# ---------- 1. tight zoom on the unit alone: rest / r2 flash / r3 flash ----------
tiles = [(P3, 8, (440, 400, 640, 580), 'rest'),
         (P2, 12, (600, 400, 800, 580), 'R2 t=0  REJECTED'),
         (P2, 13, (620, 410, 820, 590), 'R2 t=+33ms'),
         (P3, 12, (590, 400, 790, 580), 'R3 t=0'),
         (P3, 13, (630, 410, 830, 590), 'R3 t=+33ms')]
SC = 3
out = Image.new('RGB', (200*SC*len(tiles), 200*SC + 26), (10, 10, 10))
d = ImageDraw.Draw(out)
for i, (p, f, box, lab) in enumerate(tiles):
    im = Image.open(p % f).convert('RGB').crop(box).resize((200*SC, 200*SC), Image.NEAREST)
    out.paste(im, (i*200*SC, 26))
    d.text((i*200*SC+6, 7), lab, fill=(255, 255, 0))
out.save('Captures/AbilityJuice/_ad3_hr_unitzoom.png')
print('unitzoom', out.size)

# ---------- 2. squint: our strip vs the four strongest references ----------
refs = ['cm-everyability-20', 'cm-everyability-23', 'cm-8newabilities-12', 'cm-clashabilities-04']
strips = [('OURS r3', 'Captures/AbilityJuice/strips/r3/hitreact.jpg'),
          ('OURS r2', 'Captures/AbilityJuice/strips/r2/hitreact.jpg')] + \
    [(r, f'Captures/AbilityJuice/strips/reference/{r}.jpg') for r in refs]
W = 660
rows = []
for lab, path in strips:
    im = Image.open(path).convert('RGB')
    im = im.resize((W, int(W*im.height/im.width)), Image.LANCZOS)
    rows.append((lab, im))
H = sum(r[1].height + 18 for r in rows)
sq = Image.new('RGB', (W, H), (10, 10, 10))
d = ImageDraw.Draw(sq)
y = 0
for lab, im in rows:
    d.text((4, y+4), lab, fill=(255, 255, 0))
    sq.paste(im, (0, y+18))
    y += im.height + 18
sq.save('Captures/AbilityJuice/_ad3_hr_vsref.png')
sq.resize((W//4, H//4), Image.LANCZOS).resize((W//2, H//2), Image.NEAREST).save(
    'Captures/AbilityJuice/_ad3_hr_vsref_squint.png')
print('vsref', sq.size)

# ---------- 3. what the strip WOULD look like if it cut the real impact ----------
im = Image.open('Captures/AbilityJuice/strips/r3/hitreact.jpg').convert('RGB')
p = im.height
gut = (im.width - 3*p)//2
alt = Image.new('RGB', (p*3 + gut*2, p), (0, 0, 0))
for i, f in enumerate((9, 13, 40)):
    a = Image.open(P3 % f).convert('RGB').resize((p, p), Image.LANCZOS)
    alt.paste(a, (i*(p+gut), 0))
alt.save('Captures/AbilityJuice/_ad3_hr_altstrip.jpg', quality=92)

both = Image.new('RGB', (im.width, im.height*2 + 26), (10, 10, 10))
d = ImageDraw.Draw(both)
d.text((6, 4), 'SHIPPED PICK   f16 / f20 / f33   (t=+0.133 / +0.267 / +0.700)', fill=(255, 80, 80))
both.paste(im, (0, 20))
d.text((6, im.height+24), 'IF CUT AT THE REAL IMPACT   f9 / f13 / f40', fill=(120, 255, 120))
both.paste(alt, (0, im.height+40))
both.save('Captures/AbilityJuice/_ad3_hr_stripfix.jpg', quality=90)
print('stripfix', both.size)

# ---------- 4. which frames does the shipped strip actually contain? ----------
panel = np.asarray(im.crop((0, 0, p, p)).resize((128, 128), Image.LANCZOS)).astype(np.float32)
best = None
for f in range(61):
    a = np.asarray(Image.open(P3 % f).convert('RGB').resize((128, 128), Image.LANCZOS)).astype(np.float32)
    e = np.abs(a - panel).mean()
    if best is None or e < best[1]:
        best = (f, e)
print('strip panel 1 matches source frame', best)
for pi in (1, 2):
    pan = np.asarray(im.crop((pi*(p+gut), 0, pi*(p+gut)+p, p)).resize((128, 128), Image.LANCZOS)).astype(np.float32)
    b = None
    for f in range(61):
        a = np.asarray(Image.open(P3 % f).convert('RGB').resize((128, 128), Image.LANCZOS)).astype(np.float32)
        e = np.abs(a - pan).mean()
        if b is None or e < b[1]:
            b = (f, e)
    print(f'strip panel {pi+1} matches source frame', b)
