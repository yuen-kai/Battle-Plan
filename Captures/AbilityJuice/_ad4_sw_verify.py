"""Round-4 shockwave verification. Reproduces the r3 critic's exact metric definitions
(_c3_sw_strip.py, _c3_sw_form.py) against the real r4 captures and the shipped strip."""
import numpy as np, glob, os, json
from PIL import Image, ImageFilter


def lum(a): return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def sat(a):
    mx = a.max(axis=-1); mn = a.min(axis=-1)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


def hue(a):
    r, g, b = a[..., 0], a[..., 1], a[..., 2]
    mx = a.max(axis=-1); mn = a.min(axis=-1); d = mx - mn
    h = np.zeros_like(mx); m = d > 1e-6
    ir = (mx == r) & m; ig = (mx == g) & m; ib = (mx == b) & m
    h[ir] = (((g - b)[ir] / d[ir]) % 6); h[ig] = ((b - r)[ig] / d[ig]) + 2; h[ib] = ((r - g)[ib] / d[ib]) + 4
    return h * 60


# ---------------------------------------------------------------- strip level
def strip_stats(p):
    a = np.asarray(Image.open(p).convert('RGB')).astype(np.float32)
    h, w, _ = a.shape
    S, L, H = sat(a), lum(a), hue(a)
    keep = L > 8
    both = keep & (L > 200) & (S > 0.45)
    out = dict(file=os.path.basename(os.path.dirname(p)) + '/' + os.path.basename(p), size=[w, h],
               sat_mean=round(float(S[keep].mean()), 3),
               pct_bright_sat=round(100.0 * both.sum() / keep.sum(), 3),
               bs_px=int(both.sum()))
    if both.sum() > 20:
        out['bs_hue_med'] = round(float(np.median(H[both])), 0)
        out['bs_sat_med'] = round(float(np.median(S[both])), 2)
    pw = w // 3
    per = []
    for i in range(3):
        sl = slice(i * pw, (i + 1) * pw)
        k = keep[:, sl]
        b = k & (L[:, sl] > 200) & (S[:, sl] > 0.45)
        per.append(round(100.0 * b.sum() / max(k.sum(), 1), 3))
    out['panels_pct'] = per
    return out


print('=== A. SHIPPED STRIP: bright-and-saturated (L>200 & sat>0.45) ===')
refs = sorted(glob.glob('Captures/AbilityJuice/strips/reference/*.jpg'))
rs = [strip_stats(p) for p in refs]
bs = [r['pct_bright_sat'] for r in rs]
sm = [r['sat_mean'] for r in rs]
print('REF n=%d  pct_bright_sat  min %.3f  median %.3f  max %.3f' % (len(bs), min(bs), float(np.median(bs)), max(bs)))
print('REF sat_mean  min %.3f  median %.3f  max %.3f' % (min(sm), float(np.median(sm)), max(sm)))
for r in sorted(rs, key=lambda x: -x['pct_bright_sat'])[:6]:
    print('   %-42s %6.3f%%  satmean %.3f' % (r['file'], r['pct_bright_sat'], r['sat_mean']))
print()
for p in ['Captures/AbilityJuice/strips/r3/shockwave.jpg',
          'Captures/AbilityJuice/strips/r4/shockwave.jpg',
          'Captures/AbilityJuice/strips/r4/death.jpg',
          'Captures/AbilityJuice/strips/r4/pogo.jpg']:
    print(json.dumps(strip_stats(p)))

# also: a looser bar, in case the builder measured something else
print()
print('--- r4 strip under looser thresholds (probing what the 1.1%% claim could have meant) ---')
a = np.asarray(Image.open('Captures/AbilityJuice/strips/r4/shockwave.jpg').convert('RGB')).astype(np.float32)
L, S, H = lum(a), sat(a), hue(a)
tot = a.shape[0] * a.shape[1]
for nm, m in [('L>200 & S>0.45', (L > 200) & (S > 0.45)),
              ('L>180 & S>0.45', (L > 180) & (S > 0.45)),
              ('L>150 & S>0.45', (L > 150) & (S > 0.45)),
              ('L>200 & S>0.30', (L > 200) & (S > 0.30)),
              ('L>200 & S>0.20', (L > 200) & (S > 0.20)),
              ('L>200 (any sat)', (L > 200)),
              ('S>0.45 (any L)', (S > 0.45))]:
    print('   %-18s %7d px = %.3f%%' % (nm, m.sum(), 100.0 * m.sum() / tot))


# ------------------------------------------------------- full-res frame level
def frame_metrics(rd, f, label):
    D = 'Captures/AbilityJuice/shots/%s/shockwave/' % rd
    load = lambda i: np.asarray(Image.open(D + 'f%04d.jpg' % i).convert('RGB')).astype(np.float32)
    plate = np.median(np.stack([load(i) for i in range(0, 11)]), axis=0)
    a = load(f)
    La, Sa, Ha = lum(a), sat(a), hue(a)
    Lp = lum(plate)
    m = (np.abs(a - plate).max(axis=2) > 20) & (La < Lp - 25)
    core = np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(17))) > 127
    row = dict(tag=label, mass_px=int(m.sum()), core_px=int(core.sum()))
    if core.sum() > 100:
        vc = La[core]
        row.update(core_std=round(float(vc.std()), 1), core_mean=round(float(vc.mean()), 0),
                   core_p5=round(float(np.percentile(vc, 5)), 0), core_p95=round(float(np.percentile(vc, 95)), 0),
                   core_spread=round(float(np.percentile(vc, 95) - np.percentile(vc, 5)), 0),
                   core_min=round(float(vc.min()), 0), core_max=round(float(vc.max()), 0))
    fx = np.abs(a - plate).max(axis=2) > 18
    bsm = fx & (La > 200) & (Sa > 0.45)
    row['bs_px'] = int(bsm.sum())
    row['bs_pct_frame'] = round(100.0 * bsm.sum() / (1024 ** 2), 3)
    if bsm.sum() > 50:
        row['bs_hue_med'] = round(float(np.median(Ha[bsm])), 0)
        row['bs_sat_med'] = round(float(np.median(Sa[bsm])), 2)
        row['bs_Lmax'] = round(float(La[bsm].max()), 0)
    hot = fx & (La > 210)
    hd = np.asarray(Image.fromarray((hot * 255).astype(np.uint8)).filter(ImageFilter.MaxFilter(41))) > 127
    ring = hd & (~m) & (~hot)
    row['hot_px'] = int(hot.sum())
    row['ring_px'] = int(ring.sum())
    if ring.sum() > 50:
        row['spill_dL'] = round(float(La[ring].mean() - Lp[ring].mean()), 1)
        row['spill_p95_dL'] = round(float(np.percentile(La[ring] - Lp[ring], 95)), 1)
    # saturation of the whole effect footprint / of the mass
    row['fx_sat'] = round(float(Sa[fx].mean()), 3)
    row['mass_sat'] = round(float(Sa[m].mean()), 3)
    # occlusion check: how dark is the mass
    if m.sum() > 100:
        row['mass_L_mean'] = round(float(La[m].mean()), 0)
    return row


print()
print('=== B. FULL-RES FRAMES: eroded-core interior lighting + spill ===')
rows = []
for rd, f in (('r3', 15), ('r3', 17), ('r3', 19), ('r4', 13), ('r4', 17), ('r4', 30)):
    r = frame_metrics(rd, f, '%s f%d' % (rd, f))
    rows.append(r)
    print(json.dumps(r))

print()
print('=== C. CM reference bright-zone interior std (the 30-45 comparison) ===')
for p in ['cm-clashabilities-04.jpg', 'cm-8newabilities-12.jpg', 'cm-8newabilities-19.jpg',
          'cm-everyability-22.jpg', 'cm-everyability-03.jpg', 'cm-everyability-30.jpg']:
    a = np.asarray(Image.open('Captures/AbilityJuice/strips/reference/' + p).convert('RGB')).astype(np.float32)
    L = lum(a); h, w, _ = a.shape; pw = w // 3
    mid = L[:, pw:2 * pw]
    br = mid > 200
    print('  %-28s impact panel L mean %3.0f std %4.1f | bright(>200) %.3f  bright-zone std %4.1f'
          % (p, mid.mean(), mid.std(), br.mean(), mid[br].std() if br.any() else -1))
