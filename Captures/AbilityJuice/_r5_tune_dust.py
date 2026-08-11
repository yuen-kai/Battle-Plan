import numpy as np, sys, os, itertools
sys.path.insert(0, os.getcwd())
from _r5_sw_sim import report
from _r5_sw_cfg import R4, frame

print('r4 sim baseline at f17 -> grad p99 31.8, std 36.7, spread 104')
print('target: grad p99 <= 7 (sim) ~ <=10 (real, x1.34), std >= 36, spread >= 100\n')
print('%-46s %6s %6s %6s %6s %6s' % ('wall / lump / wrap / gamma / scale','g_p99','g_p90','std','spread','>L240'))
best=[]
for wall, lump, wrap, gamma, scale in itertools.product(
        [0.26,0.14,0.07],[0.07,0.16,0.26],[0.0,0.35,0.6],[1.25,0.85],[34.0,24.0]):
    cfg = dict(R4, legacy=False, wall_relief=wall, lump_relief=lump,
               key_wrap=wrap, key_gamma=gamma, lump_scale=scale)
    r,_ = report('', frame(0.1667, cfg), quiet=True)
    print('%-46s %6.2f %6.2f %6.1f %6.0f %6.3f'
          % ('%.2f / %.2f / %.2f / %.2f / %.0f'%(wall,lump,wrap,gamma,scale),
             r.get('g99',0), r.get('g90',0), r.get('istd',0), r.get('ispread',0), r['hot']))
    best.append((r.get('g99',99), wall, lump, wrap, gamma, scale, r.get('istd',0), r.get('ispread',0)))
best.sort()
print('\nlowest gradients:')
for b in best[:8]:
    print('  p99 %.2f  wall %.2f lump %.2f wrap %.2f gamma %.2f scale %.0f  std %.1f spread %.0f' % b)
