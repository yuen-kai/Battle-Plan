import json, numpy as np
from PIL import Image

ROOT = '/Users/cykai/Battle-Plan/Captures/AbilityJuice'
DIR = f'{ROOT}/shots/r3/windup'
N = 71
T0 = -0.40
FPS = 30.0


def t_of(i):
    return T0 + i / FPS


def load(i, d=DIR):
    return np.asarray(Image.open(f'{d}/f{i:04d}.jpg').convert('RGB')).astype(np.float32)


def lum(a):
    return 0.2126 * a[..., 0] + 0.7152 * a[..., 1] + 0.0722 * a[..., 2]


def amber(a):
    """warm-minus-cool signal; board is blue-grey so R-B is negative on clean floor"""
    return a[..., 0] - a[..., 2]


if __name__ == '__main__':
    base = load(0)
    print('baseline lum mean', lum(base).mean(), 'amber mean', amber(base).mean(),
          'amber p99', np.percentile(amber(base), 99))
    for i in (22, 40, 44, 45, 46, 54):
        a = load(i)
        d = amber(a) - amber(base)
        print(f'f{i:04d} t={t_of(i):+.3f}  amberdelta p50={np.percentile(d,50):.1f} '
              f'p99={np.percentile(d,99):.1f} max={d.max():.1f} '
              f'frac>15={np.mean(d>15)*100:.2f}%')
