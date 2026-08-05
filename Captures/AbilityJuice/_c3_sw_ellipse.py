import numpy as np, json
from PIL import Image

def loader(round_, shot='shockwave'):
    D = 'Captures/AbilityJuice/shots/%s/%s/' % (round_, shot)
    return lambda i: np.asarray(Image.open(D + 'f%04d.jpg' % i).convert('RGB')).astype(np.float32)

def lum(a): return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]
def sat(a):
    mx = a.max(axis=-1); mn = a.min(axis=-1)
    return np.where(mx > 1e-6, (mx-mn)/np.maximum(mx,1e-6), 0.0)

def bilinear(img, y, x):
    h, w = img.shape[:2]
    y = np.clip(y, 0, h-1.001); x = np.clip(x, 0, w-1.001)
    y0 = np.floor(y).astype(int); x0 = np.floor(x).astype(int)
    fy = y-y0; fx = x-x0
    a = img[y0,x0]; b = img[y0,x0+1]; c = img[y0+1,x0]; d = img[y0+1,x0+1]
    return a*(1-fx)*(1-fy) + b*fx*(1-fy) + c*(1-fx)*fy + d*fx*fy

def fit_ellipse(x, y):
    """Direct least-squares conic fit; returns callable r(theta) about the given origin."""
    D = np.column_stack([x*x, x*y, y*y, x, y, np.ones_like(x)])
    _,_,V = np.linalg.svd(D, full_matrices=False)
    return V[-1]

def conic_radius(c, th):
    """Radius along direction th from origin (0,0) for conic c."""
    A,B,C,Dd,E,F = c
    ct, st = np.cos(th), np.sin(th)
    a = A*ct*ct + B*ct*st + C*st*st
    b = Dd*ct + E*st
    disc = b*b - 4*a*F
    out = np.full_like(th, np.nan)
    ok = (disc >= 0) & (np.abs(a) > 1e-12)
    sq = np.sqrt(np.maximum(disc,0))
    r1 = (-b + sq)/(2*a); r2 = (-b - sq)/(2*a)
    r = np.where(r1 > 0, r1, r2)
    out[ok] = r[ok]
    return out

def measure(round_, frame, label, NR=360, RMAX=440, plate_frames=range(0,11)):
    load = loader(round_)
    plate = np.median(np.stack([load(i) for i in plate_frames]), axis=0)
    a = load(frame)
    Lp, La, Sa = lum(plate), lum(a), sat(a)
    diff = np.abs(a-plate).max(axis=2)
    matter = (diff > 20) & (La < Lp - 25)
    anyfx = diff > 18
    ys, xs = np.nonzero(anyfx)
    oy, ox = ys.mean(), xs.mean()

    th = np.linspace(0, 2*np.pi, NR, endpoint=False)
    rr = np.arange(0, RMAX, 1.0)
    R, T = np.meshgrid(rr, th)
    Mray = bilinear(matter.astype(np.float32), oy + R*np.sin(T), ox + R*np.cos(T)) > 0.5

    outer = np.full(NR, np.nan)
    for i in range(NR):
        run = 0; last = -1
        for j in range(len(rr)):
            if Mray[i,j]:
                run += 1
                if run >= 6: last = j
            else: run = 0
        if last >= 0: outer[i] = rr[last]

    ok = ~np.isnan(outer)
    # robust ellipse fit: iterate, rejecting rays >1.5 MAD from the model (vents/notches)
    keep = ok.copy()
    for _ in range(6):
        px = outer[keep]*np.cos(th[keep]); py = outer[keep]*np.sin(th[keep])
        c = fit_ellipse(px, py)
        pred = conic_radius(c, th)
        res = outer - pred
        mad = np.nanmedian(np.abs(res[ok] - np.nanmedian(res[ok])))
        keep = ok & (np.abs(res - np.nanmedian(res[ok])) < max(4.0*mad, 6.0))
        if keep.sum() < 60: break
    pred = conic_radius(c, th)
    res = outer - pred
    Rm = np.nanmean(pred)

    out = dict(label=label, frame=frame, origin=[round(float(oy),1), round(float(ox),1)],
               ellipse_mean_r=round(float(Rm),1),
               inliers=int(keep.sum()),
               resid_std_all_pct=round(float(np.nanstd(res[ok])/Rm*100),2),
               resid_std_inlier_pct=round(float(np.nanstd(res[keep])/Rm*100),2),
               resid_ptp_px=round(float(np.nanmax(res[ok])-np.nanmin(res[ok])),1),
               resid_p95_px=round(float(np.nanpercentile(np.abs(res[ok]),95)),1))
    # vents = rays where the front is >30% short of the fitted ellipse (or absent)
    short = ok & (outer < 0.70*pred)
    vent = (~ok) | short
    out['vent_rays'] = int(vent.sum()); out['vent_pct'] = round(100.0*vent.sum()/NR,1)
    # contiguous vent groups
    v = vent.astype(int); grp = 0; runs = []
    i = 0
    while i < NR:
        if v[i]:
            j = i
            while j < NR and v[j]: j += 1
            runs.append(int(j-i)); i = j
        else: i += 1
    if len(runs) > 1 and vent[0] and vent[NR-1]:
        runs[0] += runs[-1]; runs.pop()
    out['vent_groups'] = len([r for r in runs if r >= 3]); out['vent_group_sizes'] = sorted(runs, reverse=True)[:8]
    out['raw_std_pct'] = round(float(np.nanstd(outer[ok])/np.nanmean(outer[ok])*100),2)
    return out, dict(outer=outer, pred=pred, th=th, origin=(oy,ox), matter=matter, plate=plate, a=a)

if __name__ == '__main__':
    res = []
    for rd, f, lab in [('r2',15,'r2 f15'), ('r2',17,'r2 f17 (SHIPPED PANEL)'), ('r2',19,'r2 f19'),
                       ('r3',15,'r3 f15'), ('r3',17,'r3 f17 (SHIPPED PANEL)'), ('r3',19,'r3 f19')]:
        s,_ = measure(rd, f, lab)
        print(json.dumps(s)); res.append(s)
    json.dump(res, open('Captures/AbilityJuice/_c3_sw_ellipse.json','w'), indent=1)
