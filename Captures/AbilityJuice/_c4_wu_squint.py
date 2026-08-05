import numpy as np
from PIL import Image, ImageDraw, ImageFilter

W = 300


def panel(path, idx=0, label=''):
    im = Image.open(path).convert('RGB')
    pw = im.width // 3 if im.width % 3 == 0 else 512
    if 'strips' in path and im.width == 1552:
        box = (idx * 520, 0, idx * 520 + 512, 512)
    else:
        box = (idx * (im.width // 3), 0, (idx + 1) * (im.width // 3), im.height)
    c = im.crop(box).resize((W, W), Image.LANCZOS)
    return c


items = [
    ('OURS r3 t=0.33', panel('strips/r3/windup.jpg', 0)),
    ('OURS r2 t=0.33', panel('strips/r2/windup.jpg', 0)),
    ('CM ev-22', panel('strips/reference/cm-everyability-22.jpg', 0)),
    ('CM ev-20', panel('strips/reference/cm-everyability-20.jpg', 0)),
    ('CM ev-03', panel('strips/reference/cm-everyability-03.jpg', 0)),
]

rows = []
for scale, blur in [(1.0, 0), (0.33, 0), (0.20, 2)]:
    row = []
    for name, im in items:
        s = im.resize((int(W * scale), int(W * scale)), Image.LANCZOS)
        if blur:
            s = s.filter(ImageFilter.GaussianBlur(blur))
        s = s.resize((W, W), Image.NEAREST)
        d = ImageDraw.Draw(s)
        d.rectangle([0, 0, W - 1, 15], fill=(0, 0, 0))
        d.text((3, 3), f'{name} @{scale:.2f}', fill=(255, 255, 0))
        row.append(np.asarray(s))
    rows.append(np.hstack(row))
Image.fromarray(np.vstack(rows)).save('_c4_wu_squint.png')

# greyscale-only row
g = []
for name, im in items:
    a = np.asarray(im, float)
    L = (0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]).astype(np.uint8)
    s = Image.fromarray(np.dstack([L] * 3)).resize((90, 90), Image.LANCZOS).resize((W, W), Image.NEAREST)
    d = ImageDraw.Draw(s)
    d.rectangle([0, 0, W - 1, 15], fill=(0, 0, 0))
    d.text((3, 3), name + ' GREY', fill=(255, 255, 0))
    g.append(np.asarray(s))
Image.fromarray(np.hstack(g)).save('_c4_wu_squint_grey.png')
print('ok')
