import numpy as np
from PIL import Image, ImageDraw, ImageFilter

OURS2 = 'Captures/AbilityJuice/strips/r2/hitreact.jpg'
OURS1 = 'Captures/AbilityJuice/strips/r1/hitreact.jpg'
REFS = ['cm-everyability-05', 'cm-8newabilities-19', 'cm-everyability-27']

rows = [('R2 hitreact', OURS2), ('R1 hitreact', OURS1)] + \
       [(r, f'Captures/AbilityJuice/strips/reference/{r}.jpg') for r in REFS]

W = 620
out = []
for label, path in rows:
    im = Image.open(path).convert('RGB')
    im = im.resize((W, int(im.height*W/im.width)), Image.LANCZOS)
    out.append((label, im))

H = sum(im.height for _, im in out) + 22*len(out)
sheet = Image.new('RGB', (W*2+10, H), (12, 12, 14))
y = 0
d = ImageDraw.Draw(sheet)
for label, im in out:
    sheet.paste(im, (0, y+18))
    blur = im.filter(ImageFilter.GaussianBlur(3.2))
    g = blur.convert('L').convert('RGB')
    sheet.paste(g, (W+10, y+18))
    d.text((4, y+4), f'{label}   (left: as shot   right: squint = blur + greyscale)', fill=(240, 220, 120))
    y += im.height + 22
sheet.save('Captures/AbilityJuice/_c3_hr_squint.png')
print(sheet.size)

# tiny thumbnails: can you tell what happened?
th = Image.new('RGB', (200*len(out)+8*(len(out)-1), 70), (12, 12, 14))
for i, (label, im) in enumerate(out):
    t = im.resize((200, int(im.height*200/im.width)), Image.LANCZOS)
    th.paste(t, (i*208, (70-t.height)//2))
th.save('Captures/AbilityJuice/_c3_hr_thumbs.png')
print(th.size)
