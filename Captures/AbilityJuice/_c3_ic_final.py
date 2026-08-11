from PIL import Image, ImageDraw

picks = [('OURS r3 (impact core)', 'Captures/AbilityJuice/strips/r3/impactcore.jpg'),
         ('CM everyability-23', 'Captures/AbilityJuice/strips/reference/cm-everyability-23.jpg'),
         ('CM everyability-22', 'Captures/AbilityJuice/strips/reference/cm-everyability-22.jpg'),
         ('CM 8newabilities-14', 'Captures/AbilityJuice/strips/reference/cm-8newabilities-14.jpg')]

Wd = 1100
tiles = []
for name, path in picks:
    im = Image.open(path).convert('RGB')
    h = int(im.height * Wd / im.width)
    tiles.append((name, im.resize((Wd, h), Image.LANCZOS)))

H = sum(t[1].height + 26 for t in tiles)
out = Image.new('RGB', (Wd, H), (10, 10, 10))
d = ImageDraw.Draw(out)
y = 0
for name, im in tiles:
    d.text((6, y + 7), name, fill=(255, 235, 0))
    out.paste(im, (0, y + 26))
    y += im.height + 26
out.save('Captures/AbilityJuice/_c3_ic_stranger.png')
print('wrote _c3_ic_stranger.png', out.size)

# high-res look at our own impact panel
im = Image.open('Captures/AbilityJuice/strips/r3/impactcore.jpg').convert('RGB')
p = im.crop((520, 0, 1032, 512)).resize((760, 760), Image.LANCZOS)
p.save('Captures/AbilityJuice/_c3_ic_panel2.png')
print('wrote _c3_ic_panel2.png')
