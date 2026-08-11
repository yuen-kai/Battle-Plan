import numpy as np
from PIL import Image
import os, json

BASE = os.path.dirname(os.path.abspath(__file__))
SH = os.path.join(BASE, 'shots', 'r3')
PITCH = {'death': 218.0, 'pogo': 158.0}
IMPACT = {'death': 13, 'pogo': 45}
NF = {'death': 74, 'pogo': None}


def load(shot, idx):
    p = os.path.join(SH, shot, 'f%04d.jpg' % idx)
    return np.asarray(Image.open(p).convert('RGB')).astype(np.float32)


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


def sat(a):
    mx = a.max(axis=-1); mn = a.min(axis=-1)
    return np.where(mx > 1e-3, (mx-mn)/np.maximum(mx, 1e-3), 0.0)


for shot in ('death', 'pogo'):
    files = sorted(f for f in os.listdir(os.path.join(SH, shot)) if f.endswith('.jpg'))
    n = len(files)
    clean = load(shot, 0)
    Lc = lum(clean)
    p = PITCH[shot]
    cell2 = p*p
    rows = []
    for i in range(n):
        a = load(shot, i)
        L = lum(a); S = sat(a)
        diff = np.abs(a - clean).max(axis=-1)
        changed = diff > 26
        dark = changed & (L < 110)
        darker = changed & (L < 70)
        clipped = (a[..., 0] >= 250) & (a[..., 1] >= 250) & (a[..., 2] >= 250)
        hotsat = (L > 150) & (S > 0.45)
        ys, xs = np.nonzero(dark)
        if len(xs) > 60:
            bb = (xs.min(), xs.max(), ys.min(), ys.max())
            cen = (xs.mean(), ys.mean())
            wcell = (bb[1]-bb[0]+1)/p; hcell = (bb[3]-bb[2]+1)/p
        else:
            bb = None; cen = (np.nan, np.nan); wcell = hcell = 0.0
        rows.append(dict(
            i=i, t=round((i - IMPACT[shot])/30.0, 3),
            dark_cells=round(dark.sum()/cell2, 3),
            v_dark_cells=round(darker.sum()/cell2, 3),
            w=round(wcell, 2), h=round(hcell, 2),
            cx=round(float(cen[0]), 1), cy=round(float(cen[1]), 1),
            clip=int(clipped.sum()),
            hotsat=int(hotsat.sum()),
            minL=round(float(L[changed].min()) if changed.sum() else 0, 1),
        ))
    json.dump(rows, open(os.path.join(BASE, '_ad_%s_track.json' % shot), 'w'), indent=0)
    print('===', shot, 'n=', n, 'pitch', p)
    print(' t      darkcells vdark  w    h     cx     cy   clip hotsat minL')
    for r in rows:
        print('%+0.3f  %8.3f %6.3f %5.2f %5.2f %6.1f %6.1f %5d %6d %6.1f' % (
            r['t'], r['dark_cells'], r['v_dark_cells'], r['w'], r['h'], r['cx'], r['cy'], r['clip'], r['hotsat'], r['minL']))
