import numpy as np, sys, os, itertools
sys.path.insert(0, os.getcwd())
from _r5_sw_sim import report
from _r5_sw_cfg import R4, frame

R5 = dict(R4, legacy=False, glint_amount=0.0, core_lin=[0,0,0], key_wrap=0.0)
print('r4 real f17: grad p99 42.7, std 44.5, spread 117  |  r4 sim f17: 31.8 / 36.7 / 104')
print('target sim: p99 <= 7, std >= 34, spread >= 100\n')
print('%-38s %6s %6s %6s %6s %6s %6s' % ('scale/lumpR/share/wallG/keyG/key','g_p90','g_p99','std','spread','shadeCl','>L240'))
rows=[]
for scale, lumpR, share, wallG, keyG, key in itertools.product(
        [34.0,22.0,15.0],[0.10,0.20,0.34],[0.5,0.75],[0.9,1.5],[1.0,1.6],[0.62]):
    cfg = dict(R5, lump_scale=scale, lump_relief=lumpR, lump_share=share,
               wall_gamma=wallG, key_gamma=keyG, key=key)
    r,_ = report('', frame(0.1667, cfg), quiet=True)
    print('%-38s %6.2f %6.2f %6.1f %6.0f %6s %6.3f'
          % ('%.0f/%.2f/%.2f/%.1f/%.1f/%.2f'%(scale,lumpR,share,wallG,keyG,key),
             r.get('g90',0), r.get('g99',0), r.get('istd',0), r.get('ispread',0), '', r['hot']))
    rows.append((r.get('g99',99), r.get('istd',0), r.get('ispread',0), scale,lumpR,share,wallG,keyG,key))
rows.sort()
print('\nbest by gradient with std>=34:')
for x in [r for r in rows if r[1]>=34][:10]:
    print('  p99 %.2f std %.1f spread %.0f | scale %.0f lumpR %.2f share %.2f wallG %.1f keyG %.1f key %.2f' % x)
