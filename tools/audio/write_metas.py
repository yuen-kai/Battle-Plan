"""Writes the Unity .meta files for the generated audio, with import settings chosen per clip.

Getting these wrong is a real memory and CPU cost, so the policy is explicit:

  under 0.25 s   Decompress On Load, PCM        UI ticks and impacts. Uncompressed is a few KB
                                                each, decodes instantly, and avoids the pre-echo
                                                a codec puts on a 45 ms transient.
  0.25 - 1.0 s   Decompress On Load, Vorbis     Stingers and weapon tails. Held in memory as PCM
                                                so they fire with no decode latency.
  over 1.0 s     Compressed In Memory, Vorbis   Long loops and charges. Decoded on the fly.
  music          Streaming, Vorbis              45 s beds. Never resident.

GUIDs are derived from the asset path, so a regenerated set keeps the same GUIDs and nothing that
references a clip is broken by a rebuild. An existing .meta is never overwritten — Unity owns it
once it has imported the asset.

    python tools/audio/write_metas.py
"""

from __future__ import annotations

import hashlib
import sys
import wave
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
AUDIO = ROOT / "Assets" / "Audio"
MUSIC_DIR = AUDIO / "Resources" / "BattlePlanMusic"

FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

AUDIO_META = """fileFormatVersion: 2
guid: {guid}
AudioImporter:
  externalObjects: {{}}
  serializedVersion: 7
  defaultSettings:
    serializedVersion: 2
    loadType: {load_type}
    sampleRateSetting: 0
    sampleRateOverride: 48000
    compressionFormat: {compression}
    quality: {quality}
    conversionMode: 0
    preloadAudioData: {preload}
  platformSettingOverrides: {{}}
  forceToMono: 0
  normalize: 0
  loadInBackground: {background}
  ambisonic: 0
  3D: {spatial}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""


def guid_for(relative_path: str) -> str:
    """Deterministic, stable across regenerations, and namespaced so it cannot collide."""
    return hashlib.md5(f"battleplan-audio::{relative_path}".encode("utf-8")).hexdigest()


def duration(path: Path) -> float:
    with wave.open(str(path), "rb") as handle:
        return handle.getnframes() / handle.getframerate()


def settings_for(path: Path) -> dict:
    if MUSIC_DIR in path.parents:
        return {"load_type": 2, "compression": 1, "quality": 0.7, "preload": 0, "background": 1, "spatial": 0}

    seconds = duration(path)
    if seconds < 0.25:
        return {"load_type": 0, "compression": 0, "quality": 1, "preload": 1, "background": 0, "spatial": 1}
    if seconds <= 1.0:
        return {"load_type": 0, "compression": 1, "quality": 0.7, "preload": 1, "background": 0, "spatial": 1}
    return {"load_type": 1, "compression": 1, "quality": 0.7, "preload": 1, "background": 0, "spatial": 1}


def main() -> None:
    written, skipped = 0, 0

    folders = [AUDIO, AUDIO / "Resources", AUDIO / "Resources" / "BattlePlanSfx", MUSIC_DIR]
    for folder in folders:
        meta = folder.with_suffix(folder.suffix + ".meta")
        if meta.exists():
            skipped += 1
            continue
        meta.write_text(FOLDER_META.format(guid=guid_for(str(folder.relative_to(ROOT)))))
        written += 1

    summary = {}
    for wav in sorted(AUDIO.rglob("*.wav")):
        meta = wav.with_suffix(".wav.meta")
        config = settings_for(wav)
        label = {0: "DecompressOnLoad", 1: "CompressedInMemory", 2: "Streaming"}[config["load_type"]]
        codec = {0: "PCM", 1: "Vorbis"}[config["compression"]]
        summary[f"{label}/{codec}"] = summary.get(f"{label}/{codec}", 0) + 1

        if meta.exists():
            skipped += 1
            continue
        meta.write_text(
            AUDIO_META.format(guid=guid_for(str(wav.relative_to(ROOT))), **config)
        )
        written += 1

    total_bytes = sum(p.stat().st_size for p in AUDIO.rglob("*.wav"))
    print(f"wrote {written} meta file(s), skipped {skipped} existing")
    for key in sorted(summary):
        print(f"  {key:<32} {summary[key]}")
    print(f"  source WAV on disk               {total_bytes / 1_048_576:.1f} MB")


if __name__ == "__main__":
    main()
