import numpy as np, json, sys
from PIL import Image

def loader(round_, shot='shockwave'):
    D = 'Captures/AbilityJuice/shots/%s/%s/' % (round_, shot)
    def load(i):
        return np.asarray(Image.open(D + 'f%04d.jpg' % i).convert('RGB')).astype(np.float32)
    return load

def lum(a):
    return 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]

def sat(a):
    mx = a.max(axis=-1); mn = a.min(axis=-1)
    return np.where(mx > 1e-6, (mx-mn)/np.maximum(mx,1e-6), 0.0)

def bilinear(img, y, x):
    h, w = img.shape[:2]
    y = np.clip(y, 0, h-1.001); x = np.clip(x, 0, w-1.001)
    y0 = np.floor(y).astype(int); x0 = np.floor(x).astype(int)
    fy = y - y0; fx = x - x0
    a = img[y0, x0]; b = img[y0, x0+1]; c = img[y0+1, x0]; d = img[y0+1, x0+1]
    if img.ndim == 3:
        fy = fy[..., None]; fx = fx[..., None]
    return a*(1-fx)*(1-fy) + b*fx*(1-fy) + c*(1-fx)*fy + d*fx*fy

def analyse(round_, frame, plate_frames, label, origin=None, NR=360, RMAX=430):
    load = loader(round_)
    plate = np.median(np.stack([load(i) for i in plate_frames]), axis=0)
    a = load(frame)
    Lp = lum(plate); La = lum(a); Sa = sat(a)
    diff = np.abs(a - plate).max(axis=2)
    # occluding matter: differs from plate AND is darker than the floor
    matter = (diff > 20) & (La < Lp - 25)
    # any change at all
    any_fx = diff > 18
    if origin is None:
        ys, xs = np.nonzero(any_fx)
        origin = (ys.mean(), xs.mean())
    oy, ox = origin

    th = np.linspace(0, 2*np.pi, NR, endpoint=False)
    rr = np.arange(0, RMAX, 1.0)
    R, T = np.meshgrid(rr, th)
    Y = oy + R*np.sin(T); X = ox + R*np.cos(T)

    Mray = bilinear(matter.astype(np.float32), Y, X) > 0.5
    Fray = bilinear(any_fx.astype(np.float32), Y, X) > 0.5
    Lray = bilinear(La, Y, X)
    Sray = bilinear(Sa, Y, X)

    # outer boundary per ray: outermost r that ends a run of >=6 consecutive matter samples
    outer = np.full(NR, np.nan)
    for i in range(NR):
        m = Mray[i]
        run = 0; last = -1
        for j in range(len(rr)):
            if m[j]:
                run += 1
                if run >= 6: last = j
            else:
                run = 0
        if last >= 0: outer[i] = rr[last]
    found = ~np.isnan(outer)
    o = outer[found]
    stats = dict(label=label, frame=frame, origin=[round(float(oy),1), round(float(ox),1)],
                 rays=NR, rays_with_front=int(found.sum()),
                 vent_rays=int((~found).sum()), vent_pct=round(100.0*(~found).sum()/NR,1))
    if len(o):
        stats.update(mean_r=round(float(o.mean()),1), std_r=round(float(o.std()),1),
                     std_pct=round(100.0*o.std()/o.mean(),2),
                     min_r=round(float(o.min()),1), max_r=round(float(o.max()),1),
                     p10_r=round(float(np.percentile(o,10)),1), p90_r=round(float(np.percentile(o,90)),1),
                     range_pct=round(100.0*(o.max()-o.min())/o.mean(),1))
        # including vents as radius 0
        full = np.nan_to_num(outer, nan=0.0)
        stats.update(std_pct_with_vents=round(100.0*full.std()/max(full.mean(),1e-6),1))

    # HOT RIM: bright band. floor p95 measured for reference
    stats['floor_p95'] = round(float(np.percentile(Lp, 95)),1)
    for thr in (200, 210, 220):
        cov = 0; widths = []
        for i in range(NR):
            hot = Lray[i] > thr
            if hot.any():
                # only count hot inside/near the effect (within 1.15x its own outer radius)
                lim = (outer[i] if found[i] else np.nanmean(outer))*1.18
                hot = hot & (rr < lim)
            if hot.any():
                cov += 1
                widths.append(float(hot.sum()))
        stats['rim_cov_L%d' % thr] = cov
        stats['rim_cov_pct_L%d' % thr] = round(100.0*cov/NR,1)
        if widths:
            w = np.array(widths)
            stats['rim_w_L%d' % thr] = [round(float(w.mean()),1), round(float(w.min()),1), round(float(w.max()),1),
                                        round(float(w.max()/max(w.min(),1e-6)),2)]

    # arcs: contiguous runs of rays that have a hot band at L>210
    hotray = np.array([bool(((Lray[i] > 210) & (rr < (outer[i] if found[i] else np.nanmean(outer))*1.18)).any()) for i in range(NR)])
    arcs = []
    if hotray.any():
        idx = np.nonzero(hotray)[0]
        start = idx[0]; prev = idx[0]
        for k in idx[1:]:
            if k != prev+1:
                arcs.append((start, prev)); start = k
            prev = k
        arcs.append((start, prev))
        if len(arcs) > 1 and arcs[0][0] == 0 and arcs[-1][1] == NR-1:
            arcs[0] = (arcs[-1][0]-NR, arcs[0][1]); arcs.pop()
    stats['hot_arcs'] = len(arcs)
    stats['hot_arc_lengths'] = sorted([int(b-a+1) for a,b in arcs], reverse=True)[:10]

    # SATURATION above L200
    hi = La > 200
    stats['px_L200'] = int(hi.sum())
    if hi.any():
        s = Sa[hi]
        stats['sat_at_L200'] = dict(max=round(float(s.max()),3), p99=round(float(np.percentile(s,99)),3),
                                    p95=round(float(np.percentile(s,95)),3), mean=round(float(s.mean()),3),
                                    n_over_045=int((s>0.45).sum()), pct_over_045=round(100.0*(s>0.45).sum()/hi.sum(),2))
    # saturation over the effect region overall
    reg = any_fx
    if reg.any():
        s = Sa[reg]
        stats['sat_effect'] = dict(mean=round(float(s.mean()),3), p95=round(float(np.percentile(s,95)),3),
                                   max=round(float(s.max()),3))
        stats['plate_sat_same_region'] = round(float(sat(plate)[reg].mean()),3)
    return stats, dict(outer=outer, hotray=hotray, matter=matter, a=a, plate=plate, origin=origin)

if __name__ == '__main__':
    out = []
    s2, _ = analyse('r2', 17, range(0,11), 'r2 impact f17')
    print(json.dumps(s2, indent=1)); out.append(s2)
    for f in (15, 17, 19):
        s3, _ = analyse('r3', f, range(0,11), 'r3 f%d' % f)
        print(json.dumps(s3, indent=1)); out.append(s3)
    json.dump(out, open('Captures/AbilityJuice/_c3_sw_rays.json','w'), indent=1)
