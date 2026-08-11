import numpy as np, glob
from PIL import Image
from scipy import ndimage

def lum(x): return 0.2126*x[...,0]+0.7152*x[...,1]+0.0722*x[...,2]
def sat(x):
    mx=x.max(-1); mn=x.min(-1); return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0)
def hue(x):
    mx=x.max(-1); mn=x.min(-1); d=mx-mn
    r,g,b=x[...,0],x[...,1],x[...,2]
    h=np.zeros_like(mx)
    m=(d>1e-6)&(mx==r); h[m]=(60*((g-b)[m]/d[m]))%360
    m=(d>1e-6)&(mx==g); h[m]=60*((b-r)[m]/d[m])+120
    m=(d>1e-6)&(mx==b); h[m]=60*((r-g)[m]/d[m])+240
    return h

def analyse(path, box, label):
    A = np.asarray(Image.open(path).convert('RGB')).astype(np.float32)
    P = A[box[1]:box[3], box[0]:box[2]]
    L,S,Hh = lum(P), sat(P), hue(P)
    glyph = (S>0.40)&(Hh>18)&(Hh<70)&(L>110)
    if glyph.sum() < 50:
        print(label, 'no glyph'); return
    white = (S<0.12)&(L>200)
    print('%-22s glyph px %5d | S p5 %.2f p50 %.2f p95 %.2f mean %.2f | L p50 %3.0f max %3.0f | hue med %3.0f | near-white px in box: %d'
          % (label, glyph.sum(), np.percentile(S[glyph],5), np.percentile(S[glyph],50),
             np.percentile(S[glyph],95), S[glyph].mean(), np.percentile(L[glyph],50),
             L[glyph].max(), np.median(Hh[glyph]), int(white.sum())))
    return P, glyph

print('=== claim: "the 12 runs saturation 0.59 -> 0.92 (mean 0.76), no white, most saturated of the three" ===')
analyse('Captures/AbilityJuice/shots/r4/pogo/f0062.jpg', (845,335,945,435), 'r4 pogo "12"')
analyse('Captures/AbilityJuice/shots/r4/numbers/f0013.jpg', (480,120,760,360), 'r4 numbers "80"')
analyse('Captures/AbilityJuice/shots/r4/death/f0016.jpg', (470,260,730,380), 'r4 death "160"')
print()
analyse('Captures/AbilityJuice/shots/r3/pogo/f0062.jpg', (700,300,1024,470), 'r3 pogo (small hit)')
analyse('Captures/AbilityJuice/shots/r3/numbers/f0013.jpg', (480,150,760,400), 'r3 numbers "80"')

# --- broken rim: sample the keyline of the "80" badge
print('\n=== claim: broken rim (median 4.2px, p5 = 0, 34% of columns open) ===')
for rnd, fr in (('r3', 13), ('r4', 13)):
    A = np.asarray(Image.open(f'Captures/AbilityJuice/shots/{rnd}/numbers/f{fr:04d}.jpg').convert('RGB')).astype(np.float32)
    L,S,Hh = lum(A), sat(A), hue(A)
    m = (S>0.45)&(Hh>35)&(Hh<62)&(L>110)
    lab,n = ndimage.label(ndimage.binary_dilation(m, np.ones((15,15))), np.ones((3,3)))
    sizes = [( (lab==k)&m ).sum() for k in range(1,n+1)]
    k = int(np.argmax(sizes))+1
    badge = (lab==k)&m
    ys,xs = np.nonzero(badge)
    y0,y1,x0,x1 = ys.min(), ys.max(), xs.min(), xs.max()
    # top rim thickness per column, measured over the top 25% of the badge
    thick=[]
    for x in range(x0, x1+1):
        col = badge[y0:y0+max(4,(y1-y0)//4), x]
        run = 0
        for v in col:
            if v: run += 1
            elif run: break
        thick.append(run)
    thick = np.array(thick)
    print('%s "80": badge %dx%d px | top-rim thickness  median %.1f  p5 %.1f  p95 %.1f  |  open columns (0px) %.0f%%'
          % (rnd, x1-x0+1, y1-y0+1, np.median(thick), np.percentile(thick,5), np.percentile(thick,95),
             100.0*(thick==0).mean()))

# --- gap between the badge and the victim
print('\n=== claim: gap to victim 51px (was 23px) ===')
def gap(path, rnd):
    A = np.asarray(Image.open(path).convert('RGB')).astype(np.float32)
    L,S,Hh = lum(A), sat(A), hue(A)
    gold = (S>0.45)&(Hh>35)&(Hh<62)&(L>110)
    lab,n = ndimage.label(ndimage.binary_dilation(gold, np.ones((15,15))), np.ones((3,3)))
    sizes=[((lab==k)&gold).sum() for k in range(1,n+1)]
    b = (lab==int(np.argmax(sizes))+1)&gold
    ys,xs = np.nonzero(b)
    plate_bottom = ys.max()
    # victim: the pink unit under it
    pink = (S>0.35)&((Hh>320)|(Hh<8))&(L>60)&(L<215)
    py,px = np.nonzero(pink)
    sel = (px > xs.min()-90)&(px < xs.max()+90)
    if sel.sum()==0: print(rnd,'no victim'); return
    victim_top = py[sel].min()
    print('%s: badge bottom y=%d  victim top y=%d  ->  gap %d px' % (rnd, plate_bottom, victim_top, victim_top-plate_bottom))
gap('Captures/AbilityJuice/shots/r3/numbers/f0013.jpg','r3')
gap('Captures/AbilityJuice/shots/r4/numbers/f0013.jpg','r4')
