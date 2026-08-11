import numpy as np
from PIL import Image, ImageDraw

P3 = 'Captures/AbilityJuice/shots/r3/impactcore/'
P2 = 'Captures/AbilityJuice/shots/r2/impactcore/'


def crop(path, i, box=(506 - 300, 495 - 300, 506 + 300, 495 + 300), size=256):
    im = Image.open(path + 'f%04d.jpg' % i).convert('RGB').crop(box)
    return im.resize((size, size), Image.LANCZOS)


# timeline: r3 on top, r2 below, same frames
frames = list(range(11, 20))
S = 256
sheet = Image.new('RGB', (S * len(frames), S * 2 + 44), (12, 12, 12))
d = ImageDraw.Draw(sheet)
for k, i in enumerate(frames):
    sheet.paste(crop(P3, i), (k * S, 22))
    sheet.paste(crop(P2, i), (k * S, 22 + S))
    d.text((k * S + 6, 6), 't%+.3fs  f%d' % ((i - 12) / 30.0, i), fill=(255, 255, 0))
d.text((6, 22 + 2 * S + 8), 'TOP = round 3      BOTTOM = round 2', fill=(255, 255, 255))
sheet.save('Captures/AbilityJuice/_c3_ic_timeline.png')
print('wrote _c3_ic_timeline.png', sheet.size)

# squint: shipped impact panel of ours vs the four strongest references, tiny
def panel2(path):
    a = np.asarray(Image.open(path).convert('RGB'))
    dark = (a.max(axis=2) < 24).mean(axis=0) > 0.85
    cuts, run = [], []
    for x, v in enumerate(dark):
        if v:
            run.append(x)
        elif run:
            if len(run) >= 2:
                cuts.append((run[0], run[-1]))
            run = []
    cuts = [c for c in cuts if 0.2 * a.shape[1] < c[0] < 0.8 * a.shape[1]]
    if len(cuts) < 2:
        w = a.shape[1] // 3
        return Image.fromarray(a[:, w:2 * w])
    return Image.fromarray(a[:, cuts[0][1] + 1:cuts[-1][0]])


picks = [('OURS r3', 'Captures/AbilityJuice/strips/r3/impactcore.jpg'),
         ('OURS r2', 'Captures/AbilityJuice/strips/r2/impactcore.jpg'),
         ('cm-ea-30', 'Captures/AbilityJuice/strips/reference/cm-everyability-30.jpg'),
         ('cm-ea-23', 'Captures/AbilityJuice/strips/reference/cm-everyability-23.jpg'),
         ('cm-ea-22', 'Captures/AbilityJuice/strips/reference/cm-everyability-22.jpg'),
         ('cm-ea-20', 'Captures/AbilityJuice/strips/reference/cm-everyability-20.jpg'),
         ('cm-8na-14', 'Captures/AbilityJuice/strips/reference/cm-8newabilities-14.jpg')]

for tag, size in [('big', 300), ('squint', 64)]:
    out = Image.new('RGB', (310 * len(picks), 340), (12, 12, 12))
    dd = ImageDraw.Draw(out)
    for k, (name, path) in enumerate(picks):
        p = panel2(path)
        small = p.resize((size, size), Image.LANCZOS)
        out.paste(small.resize((300, 300), Image.NEAREST), (k * 310 + 5, 26))
        dd.text((k * 310 + 8, 8), name, fill=(255, 255, 0))
    out.save('Captures/AbilityJuice/_c3_ic_%s.png' % tag)
    print('wrote _c3_ic_%s.png' % tag, out.size)
