from PIL import Image, ImageDraw

P3 = 'Captures/AbilityJuice/shots/r3/hitreact/f%04d.jpg'
P2 = 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'
BOX = (380, 360, 900, 660)
W = BOX[2]-BOX[0]
H = BOX[3]-BOX[1]
SC = 2

rows = [('r3', P3, [8, 12, 13, 14]), ('r2', P2, [8, 12, 13, 14])]
out = Image.new('RGB', (W*SC*4, H*SC*2 + 40), (12, 12, 12))
d = ImageDraw.Draw(out)
for ri, (tag, path, frames) in enumerate(rows):
    for ci, f in enumerate(frames):
        im = Image.open(path % f).convert('RGB').crop(BOX).resize((W*SC, H*SC), Image.LANCZOS)
        out.paste(im, (ci*W*SC, ri*H*SC + 20 + ri*20))
        d.text((ci*W*SC + 6, ri*H*SC + 4 + ri*20),
               f'{tag}  f{f}  t={(f-12)/30.0:+.3f}s', fill=(255, 255, 0))
out.save('Captures/AbilityJuice/_ad3_hr_zoom.png')
print(out.size)
