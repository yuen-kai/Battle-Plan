"""Derive scene-stage backdrop colors that sit on the Tactical Toybox playmat ramp.

Works in OKLab so lightness/chroma steps stay perceptually even, then emits the
sRGB floats Unity wants in `m_BackGroundColor` (URP converts sRGB -> linear itself).
"""
import math

def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4

def linear_to_srgb(c):
    c = max(0.0, min(1.0, c))
    return c * 12.92 if c <= 0.0031308 else 1.055 * (c ** (1 / 2.4)) - 0.055

def hex_to_rgb(h):
    h = h.lstrip('#')
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))

def rgb_to_hex(rgb):
    return '#' + ''.join('%02X' % round(max(0.0, min(1.0, c)) * 255) for c in rgb)

def srgb_to_oklab(rgb):
    r, g, b = (srgb_to_linear(c) for c in rgb)
    l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b
    m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b
    s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b
    l_, m_, s_ = (math.copysign(abs(v) ** (1 / 3), v) for v in (l, m, s))
    return (
        0.2104542553 * l_ + 0.7936177850 * m_ - 0.0040720468 * s_,
        1.9779984951 * l_ - 2.4285922050 * m_ + 0.4505937099 * s_,
        0.0259040371 * l_ + 0.7827717662 * m_ - 0.8086757660 * s_,
    )

def oklab_to_srgb(lab):
    L, a, b = lab
    l_ = L + 0.3963377774 * a + 0.2158037573 * b
    m_ = L - 0.1055613458 * a - 0.0638541728 * b
    s_ = L - 0.0894841775 * a - 1.2914855480 * b
    l, m, s = l_ ** 3, m_ ** 3, s_ ** 3
    return (
        linear_to_srgb(+4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s),
        linear_to_srgb(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s),
        linear_to_srgb(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s),
    )

def to_lch(rgb):
    L, a, b = srgb_to_oklab(rgb)
    return L, math.hypot(a, b), math.degrees(math.atan2(b, a)) % 360

def from_lch(L, C, H):
    h = math.radians(H)
    return oklab_to_srgb((L, C * math.cos(h), C * math.sin(h)))

TOKENS = {
    'toy-ink':            '#1B1927',
    'toy-playmat':        '#2D2E43',
    'toy-playmat-raised': '#3A3B52',
    'toy-playmat-hover':  '#474862',
    'toy-muted-dark':     '#5B576B',
    'toy-muted':          '#BDB6C9',
    'toy-cream':          '#F5ECD8',
    'toy-orange':         '#F2A54A',
    'toy-teal':           '#55B29D',
    'toy-sky':            '#84D3E6',
    'toy-tomato':         '#E9685D',
}

CURRENT = {
    'Title  (renders as)': '#2E5082',
    'Roster (renders as)': '#674267',
    'Game   (renders as)': '#8CBDD1',
    'DESIGN title-periwinkle': '#7698BD',
    'DESIGN roster-mauve':     '#AA8BAA',
    'DESIGN join-clay':        '#B07F72',
}

print('== Tactical Toybox tokens (OKLCH) ==')
for name, hx in TOKENS.items():
    L, C, H = to_lch(hex_to_rgb(hx))
    print(f'  {name:20s} {hx}  L={L:.3f} C={C:.3f} H={H:6.1f}')

print('\n== What the stages look like today ==')
for name, hx in CURRENT.items():
    L, C, H = to_lch(hex_to_rgb(hx))
    print(f'  {name:24s} {hx}  L={L:.3f} C={C:.3f} H={H:6.1f}')

# Playmat ramp hue, held constant so the stages read as the same material.
RAMP_HUE = to_lch(hex_to_rgb(TOKENS['toy-playmat']))[2]
print(f'\nplaymat ramp hue = {RAMP_HUE:.1f}')

# Stage backdrops: one perceptual step-family above the panels so dark models keep
# their silhouette, chroma kept near the playmat so nothing competes with the accents.
STAGES = {
    'title':  (0.545, 0.045, RAMP_HUE - 12),   # cool periwinkle lean
    'roster': (0.545, 0.042, RAMP_HUE + 42),   # muted mauve lean
    'join':   (0.545, 0.042, RAMP_HUE + 118),  # warm clay lean
    'arena':  (0.600, 0.036, RAMP_HUE - 20),   # gameplay: lighter, near-neutral
}

print('\n== Proposed stage backdrops ==')
for name, (L, C, H) in STAGES.items():
    rgb = from_lch(L, C, H % 360)
    hx = rgb_to_hex(rgb)
    rl, rc, rh = to_lch(hex_to_rgb(hx))
    floats = ', '.join(f'{round(int(hx[1 + 2 * i:3 + 2 * i], 16) / 255, 7)}' for i in range(3))
    print(f'  {name:7s} {hx}  L={rl:.3f} C={rc:.3f} H={rh:6.1f}   unity sRGB floats: {{r: {floats}}}')
