import numpy as np, sys, os, itertools
from PIL import Image
sys.path.insert(0, os.getcwd())
import _r5_sw_sim as S
from _r5_sw_cfg import R4, frame
import _r5_ship as SH

V = {
 'A':[0.879,0.283,0.0836,0.0214,0.0055],
 'D':[0.325,0.543,0.194,0.0339,0.0058],
 'E':[0.170,0.423,0.275,0.0620,0.0107],
}
box=(110,250,320,400)
strips=[]
print('%-26s %6s %6s %6s %6s' % ('variant/share/relief','g_p90','g_p99','std','spread'))
for name, share, rel in itertools.product(['A','D','E'],[0.40,0.60],[0.55,1.0]):
    cfg = dict(SH.SHIP, slope_amp=V[name], lump_share=share, lump_relief=rel)
    r,p = S.report('', frame(0.1667, cfg), quiet=True)
    print('%-26s %6.2f %6.2f %6.1f %6.0f' % ('%s/%.2f/%.2f'%(name,share,rel),
          r.get('g90',0), r.get('g99',0), r.get('istd',0), r.get('ispread',0)))
    c = p.astype(np.uint8)[box[1]:box[3], box[0]:box[2]]
    c = np.asarray(Image.fromarray(c).resize(((box[2]-box[0])*2,(box[3]-box[1])*2), Image.NEAREST))
    strips.append(c)
row1=np.concatenate(strips[:6],axis=1); row2=np.concatenate(strips[6:],axis=1)
Image.fromarray(np.concatenate([row1,row2],axis=0)).save('_r5_lump_grid.png')
print('wrote _r5_lump_grid.png (row order = the table above, 6 per row)')
