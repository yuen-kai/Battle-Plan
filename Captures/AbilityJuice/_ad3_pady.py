import numpy as np, os
from PIL import Image
from scipy import ndimage
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])
ROOT = os.path.dirname(os.path.abspath(__file__))

def stack(rnd, idx, col):
    pl = plate(rnd,'numbers'); fr = load(fp(rnd,'numbers',idx))
    L,S,H = lum(fr), sat(fr), hue(fr)
    rows=[]
    for y in range(150, 420):
        rows.append((y, L[y,col], S[y,col], H[y,col], tuple(int(v) for v in fr[y,col])))
    return rows

def label_px(Lv,Sv,Hv):
    if Lv < 55: return 'KEY/BODY-dark'
    if Sv>0.45 and 25<Hv<70 and Lv>=100: return 'AMBER'
    if Lv>=100 and Sv<=0.45: return 'CREAM'
    if 55<=Lv<100: return 'BODY-mid'
    return '?'

for rnd, idx, cols in (('r3',20,[590, 650]), ('r2',20,[592, 645])):
    for col in cols:
        rows = stack(rnd, idx, col)
        seq=[]
        for y,Lv,Sv,Hv,rgb in rows:
            lb = label_px(Lv,Sv,Hv)
            if seq and seq[-1][0]==lb: seq[-1][2]=y
            else: seq.append([lb,y,y])
        print('==',rnd,'f%03d col=%d'%(idx,col))
        for lb,a,b in seq:
            if b-a+1 >= 1:
                print('    %-14s y %3d..%3d  (%d px)' % (lb,a,b,b-a+1))
        print()
