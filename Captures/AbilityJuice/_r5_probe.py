import numpy as np, sys, os
sys.path.insert(0, os.getcwd())
from _b4_sw_pipe import s2l, screen, LSH, grade, l2s

def show(tag, lin):
    px = screen(np.array([lin], dtype=np.float64))
    L,S,H = LSH(px)
    print('%-40s rgb(%3.0f,%3.0f,%3.0f)  L=%5.1f  S=%.3f  H=%3.0f  clip3=%s'
          % (tag, px[0,0],px[0,1],px[0,2], L[0], S[0], H[0], 'YES' if (px[0]>=250).all() else '-'))

print('=== white plateau candidates ===')
for v in (2.5,3.0,3.5,4.0,5.0,6.0,8.0):
    show('white (%.1f,%.1f,%.1f)'%(v,v,v), [v]*3)

print('\n=== hot stop: two channels clip, red held (189 deg family) ===')
for r,g,b in [(1.0,2.2,3.0),(1.2,2.4,3.2),(1.4,2.6,3.4),(0.8,2.0,2.8),(1.6,3.0,3.8),
              (0.9,2.6,3.6),(0.6,1.8,2.6),(1.8,2.8,3.4)]:
    show('hot (%.1f,%.1f,%.1f)'%(r,g,b), [r,g,b])

print('\n=== rim hue stop: saturated 189, as bright as it goes ===')
for k in (1.0,1.4,1.8,2.2,2.6,3.2,4.0):
    show('hue 189 x%.1f'%k, list(np.array([0.075,0.475,1.0])*k))
for k in (1.4,1.8,2.2,2.6):
    show('hue 189-core x%.1f'%k, list(np.array([0.105,0.600,1.0])*k))

print('\n=== deck / dust reference ===')
show('deck', [s2l(0.714)]*3)
show('DustLit as linear', [s2l(0.660),s2l(0.632),s2l(0.590)])
show('DustShadow as linear', [s2l(0.112),s2l(0.101),s2l(0.090)])
show('DustKeyLit as linear', [s2l(0.5054),s2l(0.6635),s2l(0.7327)])
