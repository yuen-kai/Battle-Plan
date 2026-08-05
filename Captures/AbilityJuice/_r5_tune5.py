import numpy as np, sys, os, itertools
sys.path.insert(0, os.getcwd())
from _r5_sw_sim import report
from _r5_sw_cfg import R4, frame
SL={'A':[0.879,0.283,0.0836,0.0214,0.0055]}
R5 = dict(R4, legacy=False, glint_amount=0.0, core_lin=[0,0,0], key_wrap=0.0,
          slope_amp=SL['A'], key=0.95, sky=0.02, ambient=0.012, wall_gamma=1.0)
print('%-42s %6s %6s %6s %6s %5s %5s' % ('in/out/share/scale/lumpR/keyG','g_p90','g_p99','std','spread','p5','p95'))
rows=[]
for fi, fo, sh, sc, lr, kg in itertools.product(
        [0.00,0.06],[0.92,1.00],[0.40,0.55],[26.0,34.0],[0.55,0.9],[1.0,1.5]):
    cfg = dict(R5, form_in=fi, form_out=fo, lump_share=sh, lump_scale=sc,
               lump_relief=lr, key_gamma=kg)
    r,_ = report('', frame(0.1667, cfg), quiet=True)
    tag='%.2f/%.2f/%.2f/%.0f/%.2f/%.1f'%(fi,fo,sh,sc,lr,kg)
    print('%-42s %6.2f %6.2f %6.1f %6.0f %5.0f %5.0f'
          % (tag, r.get('g90',0), r.get('g99',0), r.get('istd',0), r.get('ispread',0),
             r.get('ip5',0), r.get('ip95',0)))
    rows.append((r.get('g99',99), r.get('istd',0), tag))
rows.sort()
print('\nlowest p99 with std>=33:')
for x in [r for r in rows if r[1]>=33][:10]: print('  p99 %6.2f std %5.1f  %s' % x)
