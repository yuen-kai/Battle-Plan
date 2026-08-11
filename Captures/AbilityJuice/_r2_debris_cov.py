import numpy as np
from PIL import Image
import os
ROOT=os.path.dirname(os.path.abspath(__file__))
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
print("Effect coverage of the IMPACT panel, using that strip's own panel-1 as the plate")
print("(reference boards are static-camera so panel1 is a fair plate; units = % of panel area)")
for nm,p in [('OURS r2','strips/r2/debris.jpg'),
             ('OURS r1','strips/r1/debris.jpg'),
             ('CM clashabilities-04','strips/reference/cm-clashabilities-04.jpg'),
             ('CM 8newabilities-13','strips/reference/cm-8newabilities-13.jpg'),
             ('CM everyability-05','strips/reference/cm-everyability-05.jpg'),
             ('CM everyability-23','strips/reference/cm-everyability-23.jpg'),
             ('CM 8newabilities-18','strips/reference/cm-8newabilities-18.jpg'),
             ('CM everyability-30','strips/reference/cm-everyability-30.jpg')]:
    im=np.asarray(Image.open(os.path.join(ROOT,p)).convert('RGB')).astype(np.float32)
    p1=im[:,0:512]; p2=im[:,520:1032]; p3=im[:,1040:1552]
    d2=np.abs(p2-p1).max(-1)>25
    d3=np.abs(p3-p1).max(-1)>25
    L2=lum(p2)
    print("  %-22s IMPACT changed %5.1f%%   AFTERMATH changed %5.1f%%   impact panel lum sd %5.1f  p99-p1 %5.0f"%(
        nm, 100*d2.mean(), 100*d3.mean(), L2.std(), np.percentile(L2,99)-np.percentile(L2,1)))
