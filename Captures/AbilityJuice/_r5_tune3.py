import numpy as np, sys, os, itertools
sys.path.insert(0, os.getcwd())
from _r5_sw_sim import report
from _r5_sw_cfg import R4, frame

SLOPE = {
 'flat': None,
 'A': [0.879,0.283,0.0836,0.0214,0.0055],
 'B': [1.083,0.253,0.0535,0.00922,0.0017],
 'C': [1.244,0.218,0.0331,0.0,0.0],
}
R5 = dict(R4, legacy=False, glint_amount=0.0, core_lin=[0,0,0], key_wrap=0.0,
          wall_gamma=1.1, lump_share=0.6, key_gamma=1.0)
print('goal: p99 <= 8, std >= 36 (r4 sim was 31.8 / 36.7)\n')
print('%-40s %6s %6s %6s %6s %5s %5s' % ('slope/scale/lumpR/share/key/sky/wG','g_p90','g_p99','std','spread','p5','p95'))
rows=[]
for sname, scale, lumpR, share, key, sky, wg in itertools.product(
        ['A','B','C'],[22.0,15.0],[0.35,0.7,1.2],[0.55,0.8],[0.80,0.95],[0.07],[1.1,1.7]):
    cfg = dict(R5, slope_amp=SLOPE[sname], lump_scale=scale, lump_relief=lumpR,
               lump_share=share, key=key, sky=sky, wall_gamma=wg, ambient=0.030)
    r,_ = report('', frame(0.1667, cfg), quiet=True)
    tag='%s/%.0f/%.2f/%.2f/%.2f/%.2f/%.1f'%(sname,scale,lumpR,share,key,sky,wg)
    print('%-40s %6.2f %6.2f %6.1f %6.0f %5.0f %5.0f'
          % (tag, r.get('g90',0), r.get('g99',0), r.get('istd',0), r.get('ispread',0),
             r.get('ip5',0), r.get('ip95',0)))
    rows.append((r.get('g99',99), r.get('istd',0), tag))
rows.sort(key=lambda x: (-(x[1]>=36), x[0]))
print('\nranked (std>=36 first, then lowest p99):')
for x in rows[:12]: print('  p99 %6.2f std %5.1f  %s' % x)
