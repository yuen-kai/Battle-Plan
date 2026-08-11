from PIL import Image

# Clash Mini damage / stack numbers, zoomed
crops = [
    ('cm-8newabilities-07.jpg', (60, 45, 150, 145), 'x4 green'),
    ('cm-everyability-22.jpg',  (780, 40, 880, 120), 'gold 5 (p3)'),
    ('cm-8newabilities-19.jpg', (0, 0, 340, 341), 'p1 full'),
    ('cm-8newabilities-13.jpg', (0, 0, 340, 341), 'p1 full'),
]
tiles=[]
for f,(x0,y0,x1,y1),lab in crops:
    im = Image.open('Captures/AbilityJuice/strips/reference/'+f).convert('RGB')
    c = im.crop((x0,y0,min(x1,im.width),min(y1,im.height)))
    k = max(1, int(340/max(c.width,1)))
    tiles.append(c.resize((c.width*k, c.height*k), Image.LANCZOS))
    print(f, lab, c.size, '->', tiles[-1].size)

W = sum(t.width for t in tiles) + 20*len(tiles)
H = max(t.height for t in tiles)
sheet = Image.new('RGB', (W, H), (20,20,20))
x=0
for t in tiles:
    sheet.paste(t, (x, 0)); x += t.width + 20
sheet.save('Captures/AbilityJuice/_ad4_ref_nums.png')
print(sheet.size)
