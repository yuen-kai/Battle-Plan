import numpy as np, sys, os, itertools
sys.path.insert(0, os.getcwd())
from _r5_sw_sim import report
from _r5_sw_cfg import R4, frame
SL={'A':[0.879,0.283,0.0836,0.0214,0.0055],'C':[1.244,0.218,0.0331,0.0,0.0]}
R5 = dict(R4, legacy=False, glint_amount=0.0, core_lin=[0,0,0], key_wrap=0.0,
          slope_amp=SL['A'], key_gamma=1.0, key=0.95, wall_gamma=1.0)
print('r4 sim f17: p99 31.8  std 36.7  p5 39  p95 143')
print('%-40s %6s %6s %6s %6s %5s %5s' % ('in/out/share/scale/lumpR/sky/amb','g_p90','g_p99','std','spread','p5','p95'))
rows=[]
for fi, fo, sh, sc, lr, sky, amb in itertools.product(
        [0.06,0.20],[0.70,0.88],[0.45,0.70],[18.0,26.0],[0.8],[0.02,0.05],[0.012]):
    cfg = dict(R5, form_in=fi, form_out=fo, lump_share=sh, lump_scale=sc,
               lump_relief=lr, sky=sky, ambient=amb)
    r,_ = report('', frame(0.1667, cfg), quiet=True)
    tag='%.2f/%.2f/%.2f/%.0f/%.1f/%.2f/%.3f'%(fi,fo,sh,sc,lr,sky,amb)
    print('%-40s %6.2f %6.2f %6.1f %6.0f %5.0f %5.0f'
          % (tag, r.get('g90',0), r.get('g99',0), r.get('istd',0), r.get('ispread',0),
             r.get('ip5',0), r.get('ip95',0)))
    rows.append((r.get('istd',0), r.get('g99',99), tag))
rows.sort(key=lambda x:-x[0])
print('\nby std:')
for x in rows[:10]: print('  std %5.1f  p99 %6.2f  %s' % x)
