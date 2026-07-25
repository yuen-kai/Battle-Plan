"""Chroma ladder for the stage backdrops, holding lightness and hue fixed."""
from stage_palette import from_lch, to_lch, hex_to_rgb, rgb_to_hex, TOKENS

TITLE_HUE = 270.9
ROSTER_HUE = 324.7
L = 0.545

print('reference chroma:')
for name in ('toy-playmat-hover', 'toy-sky', 'toy-teal', 'toy-orange'):
    l, c, h = to_lch(hex_to_rgb(TOKENS[name]))
    print(f'  {name:20s} C={c:.3f}')
print('  current title        C=0.045')
print('  original title       C=0.092  (what it looked like before)')

for label, hue in (('title', TITLE_HUE), ('roster', ROSTER_HUE)):
    print(f'\n{label} ladder (L={L}, H={hue}):')
    for c in (0.045, 0.058, 0.071, 0.084):
        hx = rgb_to_hex(from_lch(L, c, hue))
        rl, rc, rh = to_lch(hex_to_rgb(hx))
        print(f'  C={c:.3f} -> {hx}   (actual L={rl:.3f} C={rc:.3f})')
