import numpy as np, os, json
from _c3_db_setup import ROOT, FPS, T0, load, lum, sat, plate_of

CELL = 199.0

def run(rnd):
    d = os.path.join(ROOT, 'shots', rnd, 'debris')
    plate, fr = plate_of(d)
    pl = lum(plate)
    rows = []
    for i in range(9, 34):
        im = load(os.path.join(d, fr[i]))
        L = lum(im); S = sat(im)
        m = np.abs(im - plate).max(-1) > 12
        # --- exactly the r2 critic's two metrics
        clip_all = int(((im >= 250).all(-1) & m).sum())
        dark = float((m & (pl - L > 60)).sum()) / CELL**2
        # --- extra, more generous readings
        clip_any = int(((im >= 250).any(-1) & m).sum())
        clip_hot = int(((im.max(-1) >= 250) & m).sum())
        # saturation of the HOT region (the thing the money frame needs to be coloured)
        hot = m & (L > 150)
        sat_hot = float(np.percentile(S[hot], 50)) if hot.sum() > 50 else 0.0
        sat_hot90 = float(np.percentile(S[hot], 90)) if hot.sum() > 50 else 0.0
        # saturation of the whole effect
        sat_all = float(np.percentile(S[m], 50)) if m.sum() > 50 else 0.0
        # bright AND saturated (the Clash Mini 1-5% metric)
        bs = m & (L > 150) & (S > 0.45)
        # opaque at the stricter luminance law band (40-110)
        deep = float((m & (L < 110)).sum()) / CELL**2
        rows.append(dict(t=(i-T0)/FPS, i=i, clip=clip_all, clip_any=clip_any, clip_hot=clip_hot,
                         dark=dark, deep=deep, sat_hot=sat_hot, sat_hot90=sat_hot90,
                         sat_all=sat_all, bs=int(bs.sum()), mpx=int(m.sum())))
    return rows

for rnd in ('r2', 'r3'):
    rows = run(rnd)
    print('=' * 108)
    print(rnd.upper(), '  DEBRIS  — money-frame test  (dark = pl-L>60, clip = all 3 ch >=250, sat measured on L>150 effect px)')
    print('    t     dark cell^2   deep(L<110)   clip255   clipAny   satHot50  satHot90   bright&sat px   all-effect px')
    for r in rows:
        star = ''
        if r['dark'] >= 1.5 and r['clip'] >= 300 and r['sat_hot'] >= 0.60:
            star = '   <<< MONEY FRAME'
        print('  %+0.3f    %6.3f       %6.3f      %6d    %6d     %.3f     %.3f      %6d        %7d%s'
              % (r['t'], r['dark'], r['deep'], r['clip'], r['clip_any'], r['sat_hot'], r['sat_hot90'],
                 r['bs'], r['mpx'], star))
    print()
    json.dump(rows, open(os.path.join(ROOT, '_c3_db_money_%s.json' % rnd), 'w'))
