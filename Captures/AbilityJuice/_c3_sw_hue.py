import numpy as np, json, colorsys
from PIL import Image, ImageDraw

def loader(rd, shot='shockwave'):
    D = 'Captures/AbilityJuice/shots/%s/%s/' % (rd, shot)
    return lambda i: np.asarray(Image.open(D+'f%04d.jpg'%i).convert('RGB')).astype(np.float32)
def lum(a): return 0.2126*a[...,0]+0.7152*a[...,1]+0.0722*a[...,2]
def sat(a):
    mx=a.max(axis=-1); mn=a.min(axis=-1)
    return np.where(mx>1e-6,(mx-mn)/np.maximum(mx,1e-6),0.0)
def hue(a):
    r,g,b = a[...,0]/255., a[...,1]/255., a[...,2]/255.
    mx=np.max(a,axis=-1)/255.; mn=np.min(a,axis=-1)/255.; d=mx-mn
    h=np.zeros_like(mx)
    m=(d>1e-6)
    ir=(mx==r)&m; ig=(mx==g)&m; ib=(mx==b)&m
    h[ir]=((g-b)[ir]/d[ir])%6; h[ig]=((b-r)[ig]/d[ig])+2; h[ib]=((r-g)[ib]/d[ib])+4
    return h*60

for rd, f in [('r2',17), ('r3',15), ('r3',17), ('r3',19)]:
    load = loader(rd)
    plate = np.median(np.stack([load(i) for i in range(0,11)]),axis=0)
    a = load(f)
    La, Sa, Ha = lum(a), sat(a), hue(a)
    Lp = lum(plate)
    diff = np.abs(a-plate).max(axis=2)
    anyfx = diff > 18

    # CONTROL: does the clean plate itself carry bright saturated pixels?
    Sp = sat(plate); hip = Lp > 200
    ctrl = dict(plate_px_L200=int(hip.sum()),
                plate_sat_max=round(float(Sp[hip].max()),3) if hip.any() else None,
                plate_n_sat045_L200=int((Sp[hip]>0.45).sum()) if hip.any() else 0)

    hi = (La > 200)
    hi_fx = hi & anyfx           # bright AND part of the effect
    hi_new = hi & anyfx & (Lp <= 200)   # bright, part of effect, and NOT bright in the plate
    rows = dict(round=rd, frame=f, **ctrl)
    for name, m in [('L200_all', hi), ('L200_in_effect', hi_fx), ('L200_new', hi_new)]:
        if m.sum() == 0: rows[name] = None; continue
        s = Sa[m]
        sel = s > 0.45
        rows[name] = dict(n=int(m.sum()), sat_max=round(float(s.max()),3),
                          sat_p95=round(float(np.percentile(s,95)),3),
                          n_sat045=int(sel.sum()), pct_sat045=round(100.0*sel.sum()/m.sum(),1))
        if sel.sum() > 20:
            hh = Ha[m][sel]
            rows[name]['hue_deg_p10_p50_p90'] = [round(float(np.percentile(hh,10)),0),
                                                 round(float(np.percentile(hh,50)),0),
                                                 round(float(np.percentile(hh,90)),0)]
            # warm (0-60 or >330) vs blue (180-270)
            warm = ((hh<60)|(hh>330)).sum(); blue = ((hh>=180)&(hh<=280)).sum()
            rows[name]['warm_px'] = int(warm); rows[name]['blue_px'] = int(blue)
    # blue team colour presence anywhere in effect (not just L>200)
    bl = anyfx & (Ha>=190)&(Ha<=270) & (Sa>0.45)
    rows['blue_sat045_anyL'] = int(bl.sum())
    if bl.any():
        rows['blue_sat045_lum'] = [round(float(La[bl].min()),0), round(float(np.median(La[bl])),0), round(float(La[bl].max()),0)]
        rows['blue_sat_max'] = round(float(Sa[bl].max()),3)
    wm = anyfx & ((Ha<60)|(Ha>330)) & (Sa>0.45)
    rows['warm_sat045_anyL'] = int(wm.sum())
    if wm.any():
        rows['warm_sat045_lum'] = [round(float(La[wm].min()),0), round(float(np.median(La[wm])),0), round(float(La[wm].max()),0)]
    # total effect footprint in px for context
    rows['effect_px'] = int(anyfx.sum())
    print(json.dumps(rows))
