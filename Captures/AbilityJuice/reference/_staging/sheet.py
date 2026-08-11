#!/usr/bin/env python3
"""Build labelled contact sheets so batches of candidate frames can be reviewed at once."""
import os
import sys

from PIL import Image, ImageDraw

CELL_W, COLS = 470, 4


def build(paths, out, cols=COLS):
    if not paths:
        return 0
    rows = (len(paths) + cols - 1) // cols
    cell_h = int(CELL_W * 9 / 16)
    sheet = Image.new("RGB", (cols * CELL_W, rows * (cell_h + 20)), (18, 18, 22))
    d = ImageDraw.Draw(sheet)
    for i, p in enumerate(paths):
        try:
            im = Image.open(p).convert("RGB")
        except Exception:
            continue
        im.thumbnail((CELL_W, cell_h), Image.LANCZOS)
        x = (i % cols) * CELL_W + (CELL_W - im.width) // 2
        y = (i // cols) * (cell_h + 20)
        sheet.paste(im, (x, y))
        d.text((x + 4, y + cell_h + 4), f"[{i}] {os.path.basename(p)[:52]}",
               fill=(240, 240, 120))
    sheet.save(out, quality=88)
    return len(paths)


if __name__ == "__main__":
    src, out = sys.argv[1], sys.argv[2]
    start = int(sys.argv[3]) if len(sys.argv) > 3 else 0
    count = int(sys.argv[4]) if len(sys.argv) > 4 else 12
    files = sorted(
        os.path.join(src, f) for f in os.listdir(src) if f.lower().endswith(".jpg")
    )
    chunk = files[start:start + count]
    n = build(chunk, out)
    for i, p in enumerate(chunk):
        print(f"[{i}] {p}")
    print(f"-> {out} ({n} of {len(files)} total)")
