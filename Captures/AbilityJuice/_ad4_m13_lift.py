import numpy as np
from PIL import Image, ImageDraw

R4='Captures/AbilityJuice/shots/r4/death'
def IM(i): return Image.open('%s/f%04d.jpg'%(R4,i)).convert('RGB')
def A(i): return np.asarray(IM(i)).astype(np.float32)

x0,y0,W=400,380,300
th=330
def lift(im, g=0.32, mul=2.6):
    a=np.asarray(im).astype(np.float32)/255.0
    a=np.clip((a**g)*1.0,0,1)
    return Image.fromarray((a*255).astype(np.uint8))

frames=[12,13,14,15,16,18,20,22,24,26]
cols=5
s=Image.new('RGB',(th*cols,(th+24)*2),(12,12,12)); dr=ImageDraw.Draw(s)
for k,i in enumerate(frames):
    r,c=k//cols,k%cols
    c_im=IM(i).crop((x0,y0,x0+W,y0+W)).resize((th,th),Image.LANCZOS)
    s.paste(lift(c_im),(c*th,r*(th+24)+24))
    dr.text((c*th+8,r*(th+24)+7),'f%d %+.3fs  (gamma-lifted)'%(i,(i-12)/30.0),fill=(255,255,90))
s.save('Captures/AbilityJuice/_ad4_void_lifted.jpg',quality=94)
print('wrote lifted void')

# what IS inside the void? classify interior pixels
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
print()
print('=== INTERIOR STRUCTURE of the dark mass: local 9x9 contrast (flat cut-out vs lit form) ===')
print('   round-3 bar: ours 1.6-1.9, Clash Mini 5.8-16.9')
from scipy import ndimage
post=np.median(np.stack([A(i) for i in range(66,73)]),axis=0)
for i in [16,18,20]:
    a=A(i); L=lum(a)
    m=(L<60)&(lum(post)>100)
    m=ndimage.binary_erosion(m,np.ones((9,9)))
    if m.sum()<500: print('  f%d: eroded core too small'%i); continue
    mean=ndimage.uniform_filter(L,9)
    sq=ndimage.uniform_filter(L*L,9)
    std=np.sqrt(np.maximum(sq-mean*mean,0))
    print('  f%02d  eroded-core px=%6d  local 9x9 std mean=%.2f  core L std=%.2f  core L range=%.0f..%.0f' %
          (i,m.sum(),std[m].mean(), L[m].std(), L[m].min(), L[m].max()))
