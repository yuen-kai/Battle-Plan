from PIL import Image

a = Image.open('Captures/AbilityJuice/strips/r4/windup.jpg').convert('RGB')
b = Image.open('Captures/AbilityJuice/strips/r3/windup.jpg').convert('RGB')
W,H = a.size
pw = W//3
box1 = (10, 120, 210, 280)
box2 = (pw+0, 120, pw+200, 280)

tiles = {}
for img,tag in ((b,'r3'),(a,'r4')):
    for k,bx in (('p1',box1),('p2',box2)):
        c = img.crop(bx)
        tiles[f'{tag}-{k}'] = c.resize((c.width*4, c.height*4), Image.LANCZOS)

cw, ch = tiles['r3-p1'].size
sheet = Image.new('RGB', (cw*2+20, ch*2+20), (255,255,255))
for name,cx,cy in [('r3-p1',0,0),('r4-p1',1,0),('r3-p2',0,1),('r4-p2',1,1)]:
    sheet.paste(tiles[name], (cx*(cw+20), cy*(ch+20)))
sheet.save('Captures/AbilityJuice/_ad4_wu_zoom2.png')
print(sheet.size)
