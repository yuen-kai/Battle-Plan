#!/usr/bin/env python3
"""Mine ability-moment frames from gameplay video.

VFX flashes create sharp, short-lived spikes in average luma. We find those
spikes, reject any that straddle an editing cut, and pull an aligned
windup / impact / aftermath triplet around each one.
"""
import json
import os
import subprocess
import sys

import numpy as np
from PIL import Image

STAGING = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(STAGING)

WINDUP_OFF = -0.28
AFTERMATH_OFF = 0.38
CUT_GUARD = 25.0        # scdet score above this inside the triplet window = editing cut
MIN_PEAK_GAP = 1.6      # seconds of non-max suppression between kept peaks

# Sources with a fixed presentation layout the generic detector can't infer,
# as ffmpeg crop geometry (x, y, w, h).
CROP_OVERRIDE = {
    "cm-everyability": (755, 0, 1165, 1080),   # card panel pinned to the left third
}


def run(cmd):
    return subprocess.run(cmd, shell=True, capture_output=True, text=True)


def profile(video):
    """Per-frame (time, scene-change score, avg luma, avg saturation)."""
    cache = os.path.join(STAGING, "prof", os.path.basename(video) + ".json")
    os.makedirs(os.path.dirname(cache), exist_ok=True)
    if os.path.exists(cache):
        return np.load(cache + ".npy")
    cmd = (
        f'ffprobe -f lavfi -i "movie={video},scdet=s=0:t=10,signalstats" '
        f'-show_entries frame=pts_time:frame_tags=lavfi.signalstats.YAVG,'
        f'lavfi.signalstats.SATAVG,lavfi.scd.score -of json=c=1 -v quiet'
    )
    out = run(cmd).stdout
    frames = json.loads(out)["frames"]
    rows = []
    for f in frames:
        tags = f.get("tags", {})
        try:
            rows.append((
                float(f["pts_time"]),
                float(tags.get("lavfi.scd.score", 0.0)),
                float(tags["lavfi.signalstats.YAVG"]),
                float(tags.get("lavfi.signalstats.SATAVG", 0.0)),
            ))
        except (KeyError, ValueError):
            continue
    arr = np.array(rows, dtype=np.float64)
    np.save(cache + ".npy", arr)
    return arr


def highlights(video):
    """Per-frame fraction of near-white pixels: the white-hot core of a VFX flash.

    Far more selective than mean luma, which barely moves for a localized
    explosion in a 1080p frame.
    """
    cache = os.path.join(STAGING, "prof", os.path.basename(video) + ".hi.npy")
    os.makedirs(os.path.dirname(cache), exist_ok=True)
    if os.path.exists(cache):
        return np.load(cache)
    cmd = (
        f'ffmpeg -i "{video}" -vf '
        f'"lutyuv=y=\'if(gt(val,215),255,0)\',signalstats,metadata=print:file=-" '
        f'-f null - -loglevel error'
    )
    out = run(cmd).stdout
    times, vals, cur = [], [], None
    for line in out.splitlines():
        line = line.strip()
        if line.startswith("frame:"):
            for part in line.split():
                if part.startswith("pts_time:"):
                    cur = float(part.split(":", 1)[1])
        elif line.startswith("lavfi.signalstats.YAVG=") and cur is not None:
            times.append(cur)
            vals.append(float(line.split("=", 1)[1]))
            cur = None
    arr = np.array([times, vals], dtype=np.float64).T
    np.save(cache, arr)
    return arr


def detect_crop(video, duration):
    """Find the sharp central region, to strip blurred pillarbox backdrops."""
    times = np.linspace(duration * 0.15, duration * 0.85, 6)
    cols_acc, rows_acc, shape = None, None, None
    for t in times:
        tmp = os.path.join(STAGING, "_crop.png")
        run(f'ffmpeg -ss {t:.2f} -i "{video}" -frames:v 1 -y "{tmp}" -loglevel error')
        if not os.path.exists(tmp):
            continue
        g = np.asarray(Image.open(tmp).convert("L"), dtype=np.float32)
        shape = g.shape
        # high-frequency energy: blurred regions have almost none
        ex = np.abs(np.diff(g, axis=1))
        ey = np.abs(np.diff(g, axis=0))
        c = ex.mean(axis=0)
        r = ey.mean(axis=1)
        cols_acc = c if cols_acc is None else cols_acc + c
        rows_acc = r if rows_acc is None else rows_acc + r
    if cols_acc is None:
        return None
    h, w = shape

    def bounds(prof, n):
        thr = 0.35 * np.percentile(prof, 97)
        idx = np.where(prof > thr)[0]
        if len(idx) == 0:
            return 0, n
        return int(idx[0]), int(idx[-1]) + 1

    x0, x1 = bounds(cols_acc, w)
    y0, y1 = bounds(rows_acc, h)
    # only crop if a meaningful chunk is junk
    if (x1 - x0) > 0.92 * w and (y1 - y0) > 0.92 * h:
        return None
    x0, x1 = max(0, x0), min(w, x1)
    y0, y1 = max(0, y0), min(h, y1)
    if (x1 - x0) < 320 or (y1 - y0) < 320:
        return None
    return (x0, y0, x1 - x0, y1 - y0)


def find_peaks(arr, hi, max_peaks):
    t, scd, y, sat = arr[:, 0], arr[:, 1], arr[:, 2], arr[:, 3]
    n = len(t)
    if n < 60:
        return []
    # resample the highlight signal onto the profile timeline
    h = np.interp(t, hi[:, 0], hi[:, 1]) if len(hi) else np.zeros(n)
    fps = (n - 1) / max(t[-1] - t[0], 1e-6)
    half = max(int(fps * 1.2), 5)
    pad = np.pad(h, (half, half), mode="edge")
    base = np.array([np.median(pad[i:i + 2 * half + 1]) for i in range(n)])
    delta = h - base
    thr = max(0.6, float(np.percentile(delta, 99.0)) * 0.28)

    cands = []
    for i in range(2, n - 2):
        if delta[i] < thr:
            continue
        if not (delta[i] >= delta[i - 1] and delta[i] >= delta[i + 1]):
            continue
        cands.append((delta[i], i))
    cands.sort(reverse=True)

    def cut_free(ti):
        lo, hi = ti + WINDUP_OFF - 0.10, ti + AFTERMATH_OFF + 0.10
        m = (t >= lo) & (t <= hi)
        if not m.any():
            return False
        return float(scd[m].max()) < CUT_GUARD

    kept = []
    for d, i in cands:
        ti = float(t[i])
        if ti + WINDUP_OFF < t[0] + 0.2 or ti + AFTERMATH_OFF > t[-1] - 0.2:
            continue
        if any(abs(ti - k[0]) < MIN_PEAK_GAP for k in kept):
            continue
        if not cut_free(ti):
            continue
        kept.append((ti, d, float(sat[i])))
        if len(kept) >= max_peaks:
            break
    kept.sort()
    return kept


def grab(video, t, dest, crop):
    vf = f"crop={crop[2]}:{crop[3]}:{crop[0]}:{crop[1]}" if crop else None
    vfarg = f'-vf "{vf}"' if vf else ""
    run(f'ffmpeg -ss {t:.3f} -i "{video}" -frames:v 1 {vfarg} -q:v 2 -y "{dest}" -loglevel error')
    return os.path.exists(dest)


def ahash(path):
    im = Image.open(path).convert("L").resize((16, 16), Image.LANCZOS)
    a = np.asarray(im, dtype=np.float32)
    return (a > a.mean()).flatten()


def main():
    video = sys.argv[1]
    tag = sys.argv[2]
    max_peaks = int(sys.argv[3]) if len(sys.argv) > 3 else 14

    dur = float(run(
        f'ffprobe -v error -show_entries format=duration -of csv=p=0 "{video}"'
    ).stdout.strip() or 0)
    arr = profile(video)
    if len(arr) == 0:
        print(f"{tag}: no profile")
        return
    hi = highlights(video)
    crop = CROP_OVERRIDE.get(tag) or detect_crop(video, dur)
    peaks = find_peaks(arr, hi, max_peaks)
    print(f"{tag}: dur={dur:.0f}s frames={len(arr)} crop={crop} peaks={len(peaks)}")

    out = {}
    for k, (ti, d, s) in enumerate(peaks, 1):
        for cat, off in (("windup", WINDUP_OFF), ("impact", 0.0), ("aftermath", AFTERMATH_OFF)):
            dest = os.path.join(STAGING, "cand", cat, f"{tag}-{k:02d}-{cat}.jpg")
            os.makedirs(os.path.dirname(dest), exist_ok=True)
            if grab(video, ti + off, dest, crop):
                out.setdefault(cat, []).append((dest, ti + off, d))

    # general: evenly spaced, cut-free, well-saturated frames
    t, scd, y, sat = arr[:, 0], arr[:, 1], arr[:, 2], arr[:, 3]
    for k, ti in enumerate(np.linspace(dur * 0.12, dur * 0.9, 8), 1):
        m = (t >= ti - 0.3) & (t <= ti + 0.3)
        if not m.any() or scd[m].max() >= CUT_GUARD:
            continue
        dest = os.path.join(STAGING, "cand", "general", f"{tag}-g{k:02d}.jpg")
        os.makedirs(os.path.dirname(dest), exist_ok=True)
        grab(video, float(ti), dest, crop)

    for cat, items in sorted(out.items()):
        print(f"   {cat}: {len(items)}")


if __name__ == "__main__":
    main()
