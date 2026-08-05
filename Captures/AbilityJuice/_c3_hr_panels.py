import numpy as np
from PIL import Image

STRIPS = {
    'OURS r2': 'Captures/AbilityJuice/strips/r2/hitreact.jpg',
    'OURS r1': 'Captures/AbilityJuice/strips/r1/hitreact.jpg',
    'cm-everyability-05': 'Captures/AbilityJuice/strips/reference/cm-everyability-05.jpg',
    'cm-8newabilities-19': 'Captures/AbilityJuice/strips/reference/cm-8newabilities-19.jpg',
    'cm-everyability-27': 'Captures/AbilityJuice/strips/reference/cm-everyability-27.jpg',
    'cm-8newabilities-07': 'Captures/AbilityJuice/strips/reference/cm-8newabilities-07.jpg',
    'cm-everyability-20': 'Captures/AbilityJuice/strips/reference/cm-everyability-20.jpg',
}


def panels(path):
    im = Image.open(path).convert('RGB')
    w, h = im.size
    p = h                       # square panels
    gut = (w - 3*p)//2
    return [np.asarray(im.crop((i*(p+gut), 0, i*(p+gut)+p, p)), dtype=np.float32) for i in range(3)]


def lum(a):
    return 0.2126*a[..., 0] + 0.7152*a[..., 1] + 0.0722*a[..., 2]


print(f"{'strip':22s} {'panel':6s} {'medL':>6} {'p1':>5} {'p99':>5} {'span':>5} "
      f"{'%>+40':>6} {'%<-40':>6} {'clip%':>6} {'satP90':>6} | {'thumb dev':>9} {'thumb area%':>11}")
for name, path in STRIPS.items():
    ps = panels(path)
    base = np.median(lum(ps[0]))
    for i, a in enumerate(ps):
        L = lum(a)
        med = float(np.median(L))
        mx, mn = a.max(2), a.min(2)
        S = np.where(mx > 0, (mx-mn)/np.maximum(mx, 1e-6), 0)
        d = L - med
        # squint: 40px thumbnail, greyscale
        t = np.asarray(Image.fromarray(L.clip(0, 255).astype(np.uint8)).resize((40, 40), Image.LANCZOS),
                       dtype=np.float32)
        td = t - np.median(t)
        print(f"{name:22s} p{i+1:<5d} {med:6.1f} {np.percentile(L,1):5.0f} {np.percentile(L,99):5.0f} "
              f"{np.percentile(L,99)-np.percentile(L,1):5.0f} {100*(d>40).mean():6.2f} {100*(d<-40).mean():6.2f} "
              f"{100*(mn>=250).mean():6.2f} {np.percentile(S,90):6.2f} | {np.abs(td).max():9.1f} "
              f"{100*(np.abs(td)>25).mean():11.2f}")
    print()
