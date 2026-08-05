"""Drop the simulated effect onto the real captured deck, so the look can be judged in context.

The dust is opaque, so where its mask is solid the simulated pixel stands. The crest is additive,
so elsewhere the simulated frame's departure from its flat stand-in deck is added to the real plate.
"""
import numpy as np, sys, os
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from _b4_sw_final import FINAL, frame, LEG
from _b4_sw_sim import render, N, PX_PER_WORLD

D = 'Captures/AbilityJuice/shots/r3/shockwave/'
load = lambda i: np.asarray(Image.open(D + 'f%04d.jpg' % i).convert('RGB')).astype(np.float64)
plate = np.median(np.stack([load(i) for i in range(0, 11)]), axis=0)

# the r3 mass centroid, so the simulated ring lands where the real one did
CX, CY = 442, 541


def onto_board(cfg):
    px, flat = render(cfg)
    board = plate.copy()
    x0, y0 = CX - N // 2, CY - N // 2
    xs, xe = max(x0, 0), min(x0 + N, board.shape[1])
    ys, ye = max(y0, 0), min(y0 + N, board.shape[0])
    sub = board[ys:ye, xs:xe]
    cut = px[ys - y0:ye - y0, xs - x0:xe - x0]
    ref = flat[ys - y0:ye - y0, xs - x0:xe - x0]
    # opaque where the simulated pixel is far below its own stand-in deck, additive elsewhere
    solid = (cut.mean(-1) < ref.mean(-1) - 12)[..., None]
    board[ys:ye, xs:xe] = np.where(solid, cut, np.clip(sub + (cut - ref), 0, 255))
    return np.clip(board, 0, 255).astype(np.uint8)


panels = []
for t, real in ((0.0333, 13), (0.1667, 17), (0.6000, 30)):
    if t > 0.4:
        panels.append(load(real).astype(np.uint8))
    else:
        panels.append(onto_board(frame(t, FINAL)))
new = np.concatenate(panels, axis=1)
old = np.concatenate([load(i).astype(np.uint8) for i in (13, 17, 30)], axis=1)
both = np.concatenate([old, new], axis=0)
im = Image.fromarray(both).resize((both.shape[1] // 3, both.shape[0] // 3), Image.LANCZOS)
im.save('Captures/AbilityJuice/_b4_sw_onboard.png')
im.resize((im.size[0] // 3, im.size[1] // 3), Image.LANCZOS).resize(im.size, Image.NEAREST) \
    .save('Captures/AbilityJuice/_b4_sw_onboard_squint.png')
print('wrote _b4_sw_onboard.png  [top: shipped r3 f13/f17/f30 | bottom: round 4]')
