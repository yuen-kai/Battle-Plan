"""High-res crops of the caster region for r3/r4/r5 wind-up panels 1 and 2."""
import numpy as np
from PIL import Image
import os
from _r5c_wu_measure import load, S

OUT = os.path.dirname(os.path.abspath(__file__))
RUNS = [(0, 512), (520, 1032), (1040, 1552)]

# caster sits left-of-centre; crop generously
CROPS = {0: (0, 130, 355, 165), 1: (0, 150, 355, 175)}  # (x0,y0) size w,h -> below


def crop(img, pi, box):
    a, b = RUNS[pi]
    x0, y0, w, h = box
    return img[y0:y0 + h, a + x0:a + x0 + w]


def main():
    imgs = {t: load(f"{S}/{t}/windup.jpg") for t in ("r3", "r4", "r5")}
    boxes = {0: (0, 120, 220, 190), 1: (0, 140, 260, 190)}
    for pi in (0, 1):
        tiles = []
        for t in ("r3", "r4", "r5"):
            c = crop(imgs[t], pi, boxes[pi])
            tiles.append(c)
        stack = np.concatenate(tiles, axis=0).astype(np.uint8)
        im = Image.fromarray(stack)
        im = im.resize((im.width * 3, im.height * 3), Image.NEAREST)
        im.save(f"{OUT}/_r5c_zoom_p{pi+1}.png")
        print(f"wrote _r5c_zoom_p{pi+1}.png  (rows: r3 / r4 / r5)  tile={tiles[0].shape}")


if __name__ == "__main__":
    main()
