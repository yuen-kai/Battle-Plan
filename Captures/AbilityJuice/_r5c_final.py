"""Cool-band crowding check across r5 pieces, and r4-vs-r5 charge onset from contact sheets."""
import numpy as np
from scipy import ndimage
from _r5c_wu_measure import load, luma, hsv, S
from _r5c_ref import panel_runs
from _r5c_time import sheet_cells


def piece_hue(piece):
    img = load(f"{S}/r5/{piece}.jpg")
    out = []
    for pi, (a, b) in enumerate(panel_runs(img)):
        p = img[:, a:b]
        L = luma(p); h, s, v = hsv(p)
        # effect-ish: saturated non-team-pink colour of any brightness
        m = (s >= 0.40) & (L > 90) & ~((h > 320) | (h < 15))
        if m.sum() < 200:
            out.append(None); continue
        hv, sv, Lv = h[m], s[m], L[m]
        hm = np.degrees(np.arctan2(np.sin(np.radians(hv)).mean(), np.cos(np.radians(hv)).mean())) % 360
        cool = (hv > 140) & (hv < 220)
        out.append((int(m.sum()), hm, float(np.median(hv)), float(Lv.mean()),
                    float(cool.mean()), float(np.median(hv[cool])) if cool.sum() > 50 else float("nan")))
    return out


def onset(tag):
    img = load(f"{S}/{tag}/windup_contact.jpg")
    rows = sheet_cells(img)
    ncol = 10
    W = img.shape[1] / ncol
    seq = []
    for ri, (y0, y1) in enumerate(rows):
        for ci in range(ncol):
            t = img[y0:y1, int(ci * W):int((ci + 1) * W)]
            L = luma(t); h, s, v = hsv(t)
            if tag == "r4":
                m = (h > 30) & (h < 75) & (s > 0.55) & (L > 150)
            else:
                m = (h > 140) & (h < 210) & (s > 0.18) & (L > 190)
            seq.append(int(m.sum()))
    return seq


def main():
    print("### cool-band crowding in the r5 set (saturated effect material, any brightness)")
    for piece in ("windup", "death", "pogo", "shockwave", "grenade", "aftermath"):
        r = piece_hue(piece)
        for pi, v in enumerate(r):
            if v is None:
                continue
            n, hm, hmed, Lm, coolfrac, coolmed = v
            print(f"  {piece:10s} p{pi+1}: {n:6d}px  hue circ-mean {hm:5.0f}  median {hmed:5.0f}  "
                  f"L {Lm:5.0f}  cool-frac {coolfrac:4.2f}  cool median hue "
                  f"{coolmed if coolmed==coolmed else -1:5.0f}")

    print("\n### charge onset & growth from the contact sheets (frame index, 1/30s, t = -0.40 + k/30)")
    for tag in ("r4", "r5"):
        seq = onset(tag)
        nz = [i for i, x in enumerate(seq) if x > 0]
        pk = int(np.argmax(seq))
        first = nz[0] if nz else -1
        last = nz[-1] if nz else -1
        print(f"  {tag}: first frame k={first} (t={-0.40+first/30:.2f}s)  "
              f"peak k={pk} (t={-0.40+pk/30:.2f}s, {seq[pk]}px)  "
              f"last k={last} (t={-0.40+last/30:.2f}s)")
        # frames within 80% of peak = hold length
        hold = [i for i, x in enumerate(seq) if x >= 0.8 * seq[pk]]
        print(f"      frames >=80%% of peak: {hold}  -> hold {len(hold)*33:d}ms")
        print(f"      series: {seq}")
        for t_abs in (0.35, 0.95, 1.40):
            k = int(round((t_abs + 0.40) * 30))
            k = min(k, len(seq) - 1)
            print(f"      t={t_abs:.2f}s -> k={k}: {seq[k]}px  ({100*seq[k]/max(seq[pk],1):.0f}% of peak)")


if __name__ == "__main__":
    main()
