"""Is the r4 shockwave interior a LIT VOLUME or a GLOSSY MOULDED OBJECT?
Plus: adjudicate the builder's 'the mass mask ceilings the spread' argument.
"""
import numpy as np, glob, os
from PIL import Image, ImageFilter
from scipy.ndimage import gaussian_filter, uniform_filter


def lum(a): return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]
def sat(a):
    mx = a.max(axis=-1); mn = a.min(axis=-1)
    return np.where(mx > 1e-6, (mx - mn) / np.maximum(mx, 1e-6), 0.0)


def load_set(rd, f):
    D = 'Captures/AbilityJuice/shots/%s/shockwave/' % rd
    ld = lambda i: np.asarray(Image.open(D + 'f%04d.jpg' % i).convert('RGB')).astype(np.float32)
    plate = np.median(np.stack([ld(i) for i in range(0, 11)]), axis=0)
    return ld(f), plate


def erode(m, k):
    return np.asarray(Image.fromarray((m * 255).astype(np.uint8)).filter(ImageFilter.MinFilter(k))) > 127


print('=== 1. THE MASK-CEILING DISPUTE ===')
print('Builder: mass mask is L < plate-25, so nothing above ~157 can be in it; a 140 spread')
print('would require the shadow face at L=5, and pushing the key evicts lit faces from the mask.')
print()
for rd, f in (('r3', 17), ('r4', 13), ('r4', 17)):
    a, plate = load_set(rd, f)
    La, Sa, Lp = lum(a), sat(a), lum(plate)
    ceiling = float(np.median(Lp)) - 25
    changed = np.abs(a - plate).max(axis=2) > 20
    mass = changed & (La < Lp - 25)
    core = erode(mass, 17)
    # every changed pixel, regardless of whether it is darker than the plate
    fx = np.abs(a - plate).max(axis=2) > 18
    # dust vs additive crest: crest pixels are the saturated/hot ones
    crest = fx & ((La > 200) | (Sa > 0.45))
    dusty = fx & (~crest)
    dcore = erode(dusty, 17)
    print('%s f%-3d plate median L %.0f  -> mask ceiling %.0f' % (rd, f, np.median(Lp), ceiling))
    print('   core(dark-mask, eroded) n=%-7d std %5.1f  p5-p95 %3.0f-%3.0f (%3.0f)  max %3.0f'
          % (core.sum(), La[core].std(), np.percentile(La[core], 5), np.percentile(La[core], 95),
             np.percentile(La[core], 95) - np.percentile(La[core], 5), La[core].max()))
    if dcore.sum() > 100:
        v = La[dcore]
        print('   core(ALL non-crest changed px, eroded) n=%-7d std %5.1f  p5-p95 %3.0f-%3.0f (%3.0f)  max %3.0f'
              % (dcore.sum(), v.std(), np.percentile(v, 5), np.percentile(v, 95),
                 np.percentile(v, 95) - np.percentile(v, 5), v.max()))
    # how much dust actually sits at/above the ceiling? if the key were evicting mass, this is where it goes
    above = dusty & (La >= Lp - 25) & (La < 200)
    print('   non-crest changed px at/above the mask ceiling: %d (%.1f%% of non-crest footprint)'
          % (above.sum(), 100.0 * above.sum() / max(dusty.sum(), 1)))
    # headroom actually used inside the mask
    print('   core p95 %3.0f vs ceiling %3.0f  -> unused headroom below the ceiling: %.0f levels'
          % (np.percentile(La[core], 95), ceiling, ceiling - np.percentile(La[core], 95)))
    print('   core p5  %3.0f  -> unused headroom above black: %.0f levels'
          % (np.percentile(La[core], 5), np.percentile(La[core], 5)))
    print()

print('=== 2. LIT VOLUME vs GLOSSY OBJECT: where does the interior variance live? ===')
print('A lit volume carries its value change as a BROAD gradient across each lobe (low spatial')
print('frequency). A moulded/chrome surface carries it as tight specular banding (high frequency).')
print()


def freq_split(L, mask, label):
    """Fraction of masked luminance variance at coarse (>~24px) vs fine (<~6px) scale."""
    m = mask.astype(np.float64)
    Lm = np.where(mask, L, 0.0)

    def blur(x, s):
        num = gaussian_filter(x * m, s); den = gaussian_filter(m, s)
        return np.where(den > 1e-6, num / np.maximum(den, 1e-6), 0.0)
    coarse = blur(L, 12.0)          # >~24px structure
    mid = blur(L, 3.0)              # >~6px structure
    v_tot = L[mask].var()
    v_coarse = coarse[mask].var()
    v_mid_only = (mid - coarse)[mask].var()
    v_fine = (L - mid)[mask].var()
    print('   %-34s totalstd %5.1f | coarse(>24px) %4.0f%%  mid(6-24px) %4.0f%%  fine(<6px) %4.0f%%'
          % (label, np.sqrt(v_tot), 100 * v_coarse / v_tot, 100 * v_mid_only / v_tot, 100 * v_fine / v_tot))
    return np.sqrt(v_tot), v_coarse / v_tot


for rd, f in (('r3', 17), ('r4', 13), ('r4', 17)):
    a, plate = load_set(rd, f)
    La, Sa, Lp = lum(a), sat(a), lum(plate)
    mass = (np.abs(a - plate).max(axis=2) > 20) & (La < Lp - 25)
    core = erode(mass, 17)
    freq_split(La, core, 'OURS %s f%d dust core' % (rd, f))

print()
print('   --- Clash Mini dust/smoke masses, same split (plate-free: dark material on their board) ---')
for p, panel in (('cm-clashabilities-04.jpg', 2), ('cm-8newabilities-19.jpg', 1),
                 ('cm-everyability-22.jpg', 2), ('cm-8newabilities-12.jpg', 1),
                 ('cm-everyability-03.jpg', 1)):
    a = np.asarray(Image.open('Captures/AbilityJuice/strips/reference/' + p).convert('RGB')).astype(np.float32)
    L = lum(a); h, w, _ = a.shape; pw = w // 3
    sub = L[:, panel * pw:(panel + 1) * pw]
    subc = a[:, panel * pw:(panel + 1) * pw]
    # their "effect material": bright smoke/dust in CM is LIGHT on a mid board, so take the
    # brightest connected zone and erode it the same way
    thr = np.percentile(sub, 92)
    m = sub > thr
    mm = erode(m, 17)
    if mm.sum() > 300:
        freq_split(sub, mm, '%s p%d bright mass' % (p[:24], panel))

print()
print('=== 3. SPECULAR SIGNATURE: how narrow are the bright bands inside our dust? ===')
for rd, f in (('r3', 17), ('r4', 17)):
    a, plate = load_set(rd, f)
    La, Lp = lum(a), lum(plate)
    mass = (np.abs(a - plate).max(axis=2) > 20) & (La < Lp - 25)
    core = erode(mass, 17)
    v = La[core]
    hi = v > np.percentile(v, 90)
    # per-pixel gradient magnitude inside the core = how fast value changes across the material
    gy, gx = np.gradient(La)
    gm = np.hypot(gx, gy)
    print('   %s f%d  core |grad L| mean %.2f  p90 %.2f  p99 %.2f   (r1 law: silhouette edges 49-91/px;'
          % (rd, f, gm[core].mean(), np.percentile(gm[core], 90), np.percentile(gm[core], 99)))
    print('        interior of real dust should be well under that)')
    # bimodality: two-tone moulded look vs continuous gradient
    hist, _ = np.histogram(v, bins=48, range=(0, 200))
    hist = hist / hist.sum()
    print('        core L histogram peak share %.3f  entropy %.2f / %.2f max'
          % (hist.max(), -(hist[hist > 0] * np.log(hist[hist > 0])).sum(), np.log(48)))
