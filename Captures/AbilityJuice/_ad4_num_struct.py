import numpy as np
from PIL import Image
from scipy import ndimage

def lum(x): return 0.2126*x[...,0]+0.7152*x[...,1]+0.0722*x[...,2]
def sat(x):
    mx=x.max(-1); mn=x.min(-1); return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0)

# ---- A. reference damage numbers, plateless? ----
crops = [
    ('cm-8newabilities-07.jpg', (95, 55, 235, 205), 'CM  x4 / x4'),
    ('cm-everyability-22.jpg',  (1160, 60, 1340, 190), 'CM  gold 5s'),
    ('cm-8newabilities-13.jpg', (1340, 220, 1470, 300), 'CM  glyphs'),
]
tiles=[]
for f,bx,lab in crops:
    im = Image.open('Captures/AbilityJuice/strips/reference/'+f).convert('RGB')
    c = im.crop(bx); k = max(1,int(420/c.width))
    tiles.append((lab, c.resize((c.width*k, c.height*k), Image.LANCZOS)))
# ours for comparison
im = Image.open('Captures/AbilityJuice/strips/r4/numbers.jpg').convert('RGB')
c = im.crop((251+517, 72, 372+517, 172)); k=max(1,int(420/c.width))
tiles.append(('OURS 80', c.resize((c.width*k, c.height*k), Image.LANCZOS)))

W = sum(t[1].width for t in tiles)+20*len(tiles); H = max(t[1].height for t in tiles)
sheet = Image.new('RGB',(W,H),(15,15,15)); x=0
for lab,t in tiles:
    sheet.paste(t,(x,0)); x+=t.width+20
sheet.save('Captures/AbilityJuice/_ad4_num_vs_ref.png')
print('vs-ref sheet', sheet.size, [t[0] for t in tiles])

# ---- B. desaturation test on the badge region ----
print('\n=== desaturation test (does the number lose anything without colour?) ===')
for name, path, bx in [
    ('ours 80  (f13)', 'Captures/AbilityJuice/shots/r4/numbers/f0013.jpg', (480,120,760,360)),
    ('ours 12  (f62)', 'Captures/AbilityJuice/shots/r4/pogo/f0062.jpg',    (845,335,945,435)),
    ('ours 160 (f16)', 'Captures/AbilityJuice/shots/r4/death/f0016.jpg',   (470,255,730,380)),
]:
    A = np.asarray(Image.open(path).convert('RGB')).astype(np.float32)[bx[1]:bx[3], bx[0]:bx[2]]
    L = lum(A); S = sat(A)
    board = 182.0
    print('%-16s  L: p2 %3.0f  p50 %3.0f  p98 %3.0f  |  vs board 182: darkest %-+4.0f brightest %-+4.0f  |  frac |L-182|>40: %.1f%%  |  S mean %.2f'
          % (name, np.percentile(L,2), np.percentile(L,50), np.percentile(L,98),
             np.percentile(L,2)-board, np.percentile(L,98)-board,
             100.0*(np.abs(L-board)>40).mean(), S.mean()))

# ---- C. plate vs glyph: how much of the badge is container? ----
print('\n=== badge anatomy: how much is plate, how much is number? ===')
for name, path, bx in [
    ('ours 80  (f13)', 'Captures/AbilityJuice/shots/r4/numbers/f0013.jpg', (480,120,760,360)),
    ('ours 12  (f62)', 'Captures/AbilityJuice/shots/r4/pogo/f0062.jpg',    (845,335,945,435)),
    ('ours 160 (f16)', 'Captures/AbilityJuice/shots/r4/death/f0016.jpg',   (470,255,730,380)),
]:
    A = np.asarray(Image.open(path).convert('RGB')).astype(np.float32)[bx[1]:bx[3], bx[0]:bx[2]]
    L,S = lum(A), sat(A)
    dark = L < 110                                   # the plate interior + outline
    glyph = (S>0.40)&(L>110)                         # the digits + keyline
    body = ndimage.binary_fill_holes(ndimage.binary_closing(dark|glyph, np.ones((7,7))))
    lab,n = ndimage.label(body)
    if n:
        sizes = ndimage.sum(body, lab, range(1,n+1)); body = lab==(int(np.argmax(sizes))+1)
    tot = max(body.sum(),1)
    print('%-16s  badge footprint %5d px  |  plate/dark %4.0f%%  glyph+keyline %4.0f%%' % (
        name, tot, 100.0*(dark&body).sum()/tot, 100.0*(glyph&body).sum()/tot))
