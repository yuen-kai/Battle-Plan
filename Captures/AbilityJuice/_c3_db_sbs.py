import os
from PIL import Image, ImageDraw, ImageFilter

ROOT = os.path.dirname(os.path.abspath(__file__))
rows = [('OURS r3 debris', 'strips/r3/debris.jpg'),
        ('CM everyability-20', 'strips/reference/cm-everyability-20.jpg'),
        ('CM clashabilities-04', 'strips/reference/cm-clashabilities-04.jpg'),
        ('CM 8newabilities-13', 'strips/reference/cm-8newabilities-13.jpg'),
        ('OURS r2 debris', 'strips/r2/debris.jpg')]

W = 780
tiles = []
for nm, p in rows:
    im = Image.open(os.path.join(ROOT, p)).convert('RGB')
    im = im.resize((W, int(W*im.size[1]/im.size[0])), Image.LANCZOS)
    tiles.append((nm, im))
h = tiles[0][1].size[1]
sheet = Image.new('RGB', (W, h*len(tiles)+22*len(tiles)), (18, 18, 20))
y = 0
for nm, im in tiles:
    ImageDraw.Draw(sheet).text((6, y+5), nm, fill=(255, 220, 90))
    sheet.paste(im, (0, y+20)); y += h+22
sheet.save(os.path.join(ROOT, '_c3_db_sbs.jpg'), quality=93)

# squint version
sq = sheet.resize((sheet.size[0]//5, sheet.size[1]//5), Image.LANCZOS).filter(ImageFilter.GaussianBlur(0.6))
sq = sq.resize(sheet.size, Image.NEAREST)
sq.save(os.path.join(ROOT, '_c3_db_sbs_squint.jpg'), quality=90)
print('written', sheet.size)
