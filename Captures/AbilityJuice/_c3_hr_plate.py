import numpy as np, json
from PIL import Image, ImageDraw

BOX = (400, 380, 860, 640)          # x0,y0,x1,y1 around the unit's whole travel
P = {'r1': 'Captures/AbilityJuice/shots/r1/hitreact/f%04d.jpg',
     'r2': 'Captures/AbilityJuice/shots/r2/hitreact/f%04d.jpg'}
PX_CELL = 287.0


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


def crop(tag, f):
    a = np.asarray(Image.open(P[tag] % f).convert('RGB')).astype(np.float32)
    return a[BOX[1]:BOX[3], BOX[0]:BOX[2]]


def plate(tag, frames):
    """Per-pixel brightest floor: the unit is darker than the board in every frame used."""
    stack = np.stack([lum(crop(tag, f)) for f in frames])
    return stack.max(0)


def hsv_sv(a):
    mx = a.max(2); mn = a.min(2)
    return np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0.0), mx/255.0


for tag, plate_frames in (('r2', [0, 16, 18, 20, 22]), ('r1', [0, 21, 24, 27, 30])):
    pl = plate(tag, plate_frames)
    print(f'\n===== {tag} =====  plate floor L: median {np.median(pl):.1f}  '
          f'p10 {np.percentile(pl,10):.1f}  p90 {np.percentile(pl,90):.1f}')
    print(f"{'f':>3} {'t':>7} | {'dark-60':>7} {'x/rest':>6} | {'hot+40':>7} {'clip':>6} | "
          f"{'|d|<20':>7} | {'sil':>6} {'x/rest':>6} {'w':>4} {'h':>4} {'wx/r':>5} {'cx':>6} {'dxcell':>7} | "
          f"{'S_hot':>5} {'S_body':>6} {'V_body':>6} {'L_body':>6}")
    rest = None
    for f in list(range(0, 32)) + [36, 40, 42, 43, 46, 50, 55]:
        a = crop(tag, f)
        L = lum(a)
        d = L - pl
        dark = d <= -60
        hot = d >= 40
        flat = np.abs(d) < 20
        sil = dark | hot | (np.abs(d) >= 20)
        sil = dark | (d >= 25) | (d <= -25)
        S, V = hsv_sv(a)
        ys, xs = np.where(sil)
        # body = the connected mass, approximated by the sil bbox interior
        clip = int((a.min(2) >= 250).sum())
        hotm = d >= 40
        bodym = dark | hotm
        row = dict(
            f=f, t=(f-12)/30.0, dark=int(dark.sum()), hot=int(hot.sum()), clip=clip,
            flat=int(flat.sum()), sil=int(sil.sum()),
            w=int(xs.max()-xs.min()+1) if len(xs) else 0,
            h=int(ys.max()-ys.min()+1) if len(ys) else 0,
            cx=float(xs.mean())+BOX[0] if len(xs) else 0,
            Shot=float(S[hotm].mean()) if hotm.sum() else 0.0,
            Sbody=float(S[bodym].mean()) if bodym.sum() else 0.0,
            Vbody=float(V[bodym].mean()) if bodym.sum() else 0.0,
            Lbody=float(L[bodym].mean()) if bodym.sum() else 0.0,
        )
        if rest is None:
            rest = row
        print(f"{f:3d} {row['t']:+7.3f} | {row['dark']:7d} {row['dark']/max(rest['dark'],1):6.2f} | "
              f"{row['hot']:7d} {clip:6d} | {row['flat']:7d} | {row['sil']:6d} "
              f"{row['sil']/max(rest['sil'],1):6.2f} {row['w']:4d} {row['h']:4d} {row['w']/max(rest['w'],1):5.2f} "
              f"{row['cx']:6.1f} {(row['cx']-rest['cx'])/PX_CELL:+7.3f} | {row['Shot']:5.3f} {row['Sbody']:6.3f} "
              f"{row['Vbody']:6.3f} {row['Lbody']:6.1f}")
