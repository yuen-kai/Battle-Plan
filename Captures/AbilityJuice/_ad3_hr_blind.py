"""Blind test: shipped cut vs a correct cut, both against the strongest reference, squinted."""
from PIL import Image, ImageDraw

P3 = 'Captures/AbilityJuice/shots/r3/hitreact/f%04d.jpg'
SIDE = 340
GUT = 8


def strip_from(frames):
    s = Image.new('RGB', (SIDE*3 + GUT*2, SIDE), (0, 0, 0))
    for i, f in enumerate(frames):
        s.paste(Image.open(P3 % f).convert('RGB').resize((SIDE, SIDE), Image.LANCZOS),
                (i*(SIDE+GUT), 0))
    return s


def ref_strip(name):
    im = Image.open(f'Captures/AbilityJuice/strips/reference/{name}.jpg').convert('RGB')
    w = SIDE*3 + GUT*2
    return im.resize((w, int(im.height*w/im.width)), Image.LANCZOS)


rows = [
    ('A  shipped cut          f16 / f20 / f33', strip_from([16, 20, 33])),
    ('B  cut at the event     f9  / f13 / f26', strip_from([9, 13, 26])),
    ('C  Clash Mini  everyability-20', ref_strip('cm-everyability-20')),
    ('D  Clash Mini  clashabilities-04', ref_strip('cm-clashabilities-04')),
]
W = SIDE*3 + GUT*2
H = sum(r[1].height + 20 for r in rows)
out = Image.new('RGB', (W, H), (8, 8, 8))
d = ImageDraw.Draw(out)
y = 0
for lab, im in rows:
    d.text((4, y+5), lab, fill=(255, 230, 0))
    out.paste(im, (0, y+20))
    y += im.height + 20
out.save('Captures/AbilityJuice/_ad3_hr_blind.png')
sm = out.resize((W//5, H//5), Image.LANCZOS)
sm.resize((W//2, H//2), Image.NEAREST).save('Captures/AbilityJuice/_ad3_hr_blind_squint.png')
print(out.size, sm.size)
