import numpy as np
from PIL import Image, ImageDraw

P2 = 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'
box = (200, 330, 900, 700)   # 700 x 370
W, H = box[2]-box[0], box[3]-box[1]
S = 1.4
frames = [8, 12, 13, 14, 15, 16, 18, 26]
img = Image.new('RGB', (int(W*S), int(H*S)*len(frames)+6*(len(frames)-1)), (10, 11, 15))
d = ImageDraw.Draw(img)
for i, f in enumerate(frames):
    t = Image.open(P2 % f).convert('RGB').crop(box).resize((int(W*S), int(H*S)), Image.LANCZOS)
    dd = ImageDraw.Draw(t)
    for x in range(box[0], box[2], 100):
        px = (x-box[0])*S
        dd.line([(px, 0), (px, 12)], fill=(255, 60, 60), width=2)
        dd.text((px+3, 2), str(x), fill=(255, 60, 60))
    img.paste(t, (0, i*(int(H*S)+6)))
    d.text((6, i*(int(H*S)+6)+int(H*S)-14), f'f{f}  t={(f-12)/30.0:+.3f}s', fill=(255, 240, 120))
img.save('Captures/AbilityJuice/_c3_hr_wide.png')
print(img.size)
