import numpy as np
from PIL import Image
import os

ROOT = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(ROOT, 'shots', 'r2', 'windup')
frames = sorted(f for f in os.listdir(SH) if f.endswith('.jpg'))


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


def load(i):
    return np.asarray(Image.open(os.path.join(SH, frames[i])).convert('RGB')).astype(np.float32)


f0 = load(0); L0 = lum(f0)
floor = L0 > 150

print("== decompose: hard plate (d<-45) vs soft halo (-45<d<-12) ==")
print("  i     t    plate%  plate_bboxWxH  plate_lum  halo%   halo_bboxWxH  halo_lum")
for i in list(range(11, 50)) + [54, 60]:
    a = load(i); d = lum(a) - L0
    plate = (d < -45) & floor
    halo = (d < -12) & (d >= -45) & floor
    out = ["%3d %+6.2f" % (i, i/30.0-0.40)]
    for m in (plate, halo):
        if m.sum() > 200:
            ys, xs = np.nonzero(m)
            out.append("%6.2f %4dx%4d %6.1f" % (100*m.mean(), xs.max()-xs.min(), ys.max()-ys.min(), lum(a)[m].mean()))
        else:
            out.append("%6.2f    -  -      -  " % (100*m.mean()))
    print("  ".join(out))

# ---- edge hardness: horizontal scan through the plate at panel-1 and panel-2 times ----
print("\n== EDGE PROFILE (luminance per pixel across the tint boundary) ==")
for i in (22, 40):
    a = load(i); La = lum(a); d = La - L0
    plate = (d < -45) & floor
    ys, xs = np.nonzero(plate)
    cy = int(np.median(ys)); 
    row = d[cy]
    # find left and right crossings of -45 nearest the centroid
    cx = int(np.median(xs))
    prof = row
    def hardness(sign):
        # walk out from cx until we exit the plate
        x = cx
        while 0 < x < 1023 and prof[x] < -45:
            x += sign
        lo = max(0, x-25); hi = min(1023, x+25)
        seg = prof[lo:hi]
        # 10..90% transition width of the step
        a_, b_ = seg.min(), seg.max()
        thr_lo = a_ + 0.1*(b_-a_); thr_hi = a_ + 0.9*(b_-a_)
        idx = np.nonzero((seg <= thr_hi) & (seg >= thr_lo))[0]
        w = (idx.max()-idx.min()+1) if len(idx) else 0
        return x, abs(b_-a_), w, (abs(b_-a_)/max(w, 1))
    for s, nm in ((-1, 'left'), (1, 'right')):
        x, amp, w, slope = hardness(s)
        print("  frame %2d (t=%+.2f) %-5s edge at x=%4d : step %5.1f lum over %2dpx  = %5.1f lum/px"
              % (i, i/30.0-0.40, nm, x, amp, w, slope))

# ---- cell snapping: is the plate boundary axis-aligned to the tile grid? ----
print("\n== CELL SNAP: plate column heights (a cell-snapped plate gives flat runs) ==")
for i in (22, 40):
    a = load(i); d = lum(a) - L0
    plate = (d < -45) & floor
    cols = plate.sum(0)
    nz = np.nonzero(cols)[0]
    samp = cols[nz.min():nz.max()+1]
    # count distinct plateau runs
    runs = 1 + int((np.abs(np.diff(samp)) > 3).sum())
    print("  frame %2d: plate spans x=%d..%d, %d column-height changes >3px (a rectilinear tile union has few)"
          % (i, nz.min(), nz.max(), runs))
