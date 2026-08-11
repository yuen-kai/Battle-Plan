import numpy as np, sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath('.')))
sys.path.insert(0, os.getcwd())
from _r5_sw_sim import report, CELL_PANEL_PX, PX_PER_WORLD
from _r5_sw_cfg import R4, frame
print('px/world %.2f   cell on panel %.1f px' % (PX_PER_WORLD, CELL_PANEL_PX))
print('\n=== SIMULATED reproduction of the SHIPPED r4 shockwave ===')
for t,name in ((0.0333,'f13'),(0.1667,'f17')):
    report('r4 %s (+%.4f)'%(name,t), frame(t, R4))
print("""
real measured (panel, from _r5_sw_base.py):
  f13  >L240 0.042%  clip 0.000%  Lmax 247
  f17  >L240 0.080%  clip 0.000%  Lmax 249  interior std 44.5 spread 117  grad p99 42.7
""")
