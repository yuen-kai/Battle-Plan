import numpy as np, os
from PIL import Image
from scipy import ndimage
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)),'_ad3_num_measure.py')).read().split("if __name__")[0])
ROOT = os.path.dirname(os.path.abspath(__file__))

# --- POGO badge: hand-bounded from the gridded crop (frame 58) ---
fr = load(fp('r3','pogo',58)); L,S,H = lum(fr),sat(fr),hue(fr)
box = (958, 375, 1024, 428)
x0,y0,x1,y1 = box
sub = fr[y0:y1, x0:x1]; Ls, Ss = lum(sub), sat(sub)
print('POGO f058 badge box', box)
print('  L: min=%.0f p5=%.0f p50=%.0f p95=%.0f max=%.0f' % (Ls.min(),np.percentile(Ls,5),np.percentile(Ls,50),np.percentile(Ls,95),Ls.max()))
print('  S: p50=%.3f p95=%.3f max=%.3f' % (np.percentile(Ss,50),np.percentile(Ss,95),Ss.max()))
print('  fraction of badge px with S>0.45 : %.3f' % (Ss>0.45).mean())
print('  right-most column reached by dark keyline: ', end='')
dark = Ls<60
cols = np.where(dark.any(0))[0]
print(x0+cols.max(), '(frame width 1024)  -> clipped' if x0+cols.max()>=1022 else '')

# how much of the digit run is cut: find the digit ink columns
ink = (Ls>170)
icols = np.where(ink.any(0))[0]
print('  digit ink spans x %d..%d ; frame edge at 1023' % (x0+icols.min(), x0+icols.max()))

# --- compare with the amber "80" and red "160" badges ---
for tag, rnd, shot, idx, bx in (('80 amber','r3','numbers',20,(536,204,699,334)),
                                ('160 red','r3','death',13,(455,285,880,420))):
    f2 = load(fp(rnd,shot,idx)); a,b,c,d = bx
    s2 = f2[b:d, a:c]; L2,S2 = lum(s2), sat(s2)
    print('%s  L p5=%.0f p50=%.0f p95=%.0f | S p50=%.2f p95=%.2f | frac S>0.45 = %.2f'
          % (tag, np.percentile(L2,5),np.percentile(L2,50),np.percentile(L2,95),
             np.percentile(S2,50),np.percentile(S2,95),(S2>0.45).mean()))

# --- when does the pogo badge appear relative to landing? ---
print()
print('POGO badge presence (dark keyline blob in x>930, y 340..440):')
for i in range(40, 76):
    f3 = load(fp('r3','pogo',i)); L3 = lum(f3)
    reg = L3[340:440, 930:1024]
    n = int((reg<55).sum())
    print('  f%03d dark px=%4d' % (i, n), '  <-- badge' if n>300 else '')
