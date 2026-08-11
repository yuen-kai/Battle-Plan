from PIL import Image, ImageDraw, ImageFilter

sheet = Image.new('RGB', (1100, 700), (15,15,15))
d = ImageDraw.Draw(sheet)
for j,(rnd,y) in enumerate((('r3',0),('r4',340))):
    im = Image.open(f'Captures/AbilityJuice/strips/{rnd}/pogo.jpg').convert('RGB')
    pw = im.width//3
    pan = im.crop((2*pw, 0, 3*pw, im.height))
    # right half of panel 3, where the badge lives, at 2x
    c = pan.crop((300, 60, 517, 280)).resize((217*2, 220*2), Image.LANCZOS)
    sheet.paste(c, (0, y))
    d.text((450, y+8), f'{rnd} pogo panel 3 (right half)', fill=(255,255,255))
    # mark the panel's right edge
    d.line([(2*217-2, y),(2*217-2, y+2*220)], fill=(255,0,0), width=3)
    # squint version
    sq = c.resize((c.width//8, c.height//8), Image.LANCZOS).filter(ImageFilter.GaussianBlur(0.7))
    sheet.paste(sq.resize((c.width//3, c.height//3), Image.NEAREST), (460, y+30))
sheet.save('Captures/AbilityJuice/_ad4_pogo_view.png')
print('ok', sheet.size)
