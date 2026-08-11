#!/usr/bin/env python3
"""Copy the approved frames into the reference folders and write MANIFEST.md."""
import json
import os
import shutil

from PIL import Image

import mine

STAGING = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(STAGING)
SOURCES = json.load(open(os.path.join(STAGING, "sources.json")))

# Per-moment notes, written from visual review of each frame.
M = {
    "cm-everyability-03": dict(
        ability="Wizard-family channelled beam on the violet board",
        windup="Caster braced, board still clean — the calm read before the beam lands.",
        impact="Two hard white vertical energy columns punch down; core is pure white, edges stay violet-tinted.",
        aftermath="Columns thin into pale vertical smoke that keeps the silhouette of the hit a beat longer."),
    "cm-everyability-05": dict(
        ability="Melee crit on the sand board",
        windup="Attacker leans in, faint cyan gather at the weapon before contact.",
        impact="White core with cyan fringe, hit sparks thrown radially, damage number popping on the same frame.",
        aftermath="Cyan wash dissipates upward while the unit knockback still reads."),
    "cm-everyability-10": dict(
        ability="Spear Goblin 'Spear Toss' clash ability",
        windup="Thrower winds back, target tile clean, no effect yet.",
        impact="Orange-red spear impact with a hot white centre and scorch spreading along the tile.",
        aftermath="Scorch stain persists on the tile with thin smoke curling off it."),
    "cm-everyability-11": dict(
        ability="Archer volley — multi-target arrow impacts",
        windup="Arrows in flight, board otherwise clean.",
        impact="Four separate gold-white hit bursts land on the same frame — good reference for multi-hit staging.",
        aftermath="Bursts collapse to small gold sparks; damage numbers rise."),
    "cm-everyability-12": dict(
        ability="Ranged volley, second wave",
        windup="Units advancing, telegraph not yet lit.",
        impact="Paired gold bursts with white cores, each with its own small ground contact flare.",
        aftermath="Sparks fall and fade, dust remains at the contact tiles."),
    "cm-everyability-14": dict(
        ability="Slam-style ground ability on the sand board",
        windup="Attacker raised over the target tile, gold charge on the weapon.",
        impact="White core plus a crisp expanding white ground ring — the clearest ring-on-floor read in the set.",
        aftermath="Ring has expanded and thinned to a faint disc while smoke lifts from the centre."),
    "cm-everyability-18": dict(
        ability="Ice/freeze area ability",
        windup="Board pre-freeze, units mid-move.",
        impact="Cyan-white frost blast covering most of the board; pale blue rim, near-white centre.",
        aftermath="Frost residue sits on the tiles as a flat pale-blue decal with white crystal wisps."),
    "cm-everyability-20": dict(
        ability="Giant Skeleton death bomb (K.O. ability)",
        windup="Pink AoE tile telegraph lights the exact blast footprint before anything detonates.",
        impact="Huge white-hot sphere with a saturated magenta rim, blowing out the board underneath.",
        aftermath="Orange-red scorch disc burned onto the tiles with white smoke puffs rising off it."),
    "cm-everyability-22": dict(
        ability="Giant Skeleton death bomb — widest framing",
        windup="Pink tile telegraph fully lit across the affected tiles, victims still alive on them.",
        impact="White-hot core with magenta rim at maximum radius, board geometry still readable through it.",
        aftermath="Wide orange scorch disc, smoke columns, and damage numbers all in one frame."),
    "cm-everyability-23": dict(
        ability="Giant Skeleton death bomb — off-centre variant",
        windup="Telegraph tiles lit pink with a white charge already forming at the centre.",
        impact="Blast sphere clipped by the board edge, showing how the effect is allowed to overflow the play area.",
        aftermath="Scorch disc plus a tall white smoke plume — the strongest smoke read in the set."),
    "cm-everyability-26": dict(
        ability="Magenta area-denial ability on a tile grid",
        windup="Grid tiles begin to tint magenta, marking the AoE footprint.",
        impact="Magenta-violet energy fills whole tiles rather than a circle — grid-native AoE shape.",
        aftermath="Tiles hold a soft magenta glow that decays per-tile, with white wisps above."),
    "cm-everyability-27": dict(
        ability="Magenta area-denial, denser cast",
        windup="Tile telegraph lit, caster mid-animation.",
        impact="Bright white core inside the magenta tile field — core/rim separation is very legible here.",
        aftermath="Magenta field fades unevenly while smoke lingers over the centre tiles."),
    "cm-everyability-30": dict(
        ability="Violet burst with star sparks",
        windup="Charge glow gathering at the caster, tiles beginning to tint.",
        impact="Violet-white burst throwing four-point star sparks — Supercell's signature spark shape.",
        aftermath="Star sparks drift outward and fade against the residual violet tile glow."),
    "cm-8newabilities-07": dict(
        ability="Hero super on the crimson/violet arena",
        windup="Cyan-white star charge forming at the hero with gold sparks already shedding.",
        impact="Gold-white burst with the smoke column starting behind it on the same frame.",
        aftermath="Smoke drifts up off the tile while gold sparks settle and damage numbers rise."),
    "cm-8newabilities-08": dict(
        ability="Hero super — full star burst",
        windup="Gold energy gathering low along the ground before release.",
        impact="Textbook frame: white-hot centre, saturated gold rim, radial star spikes and a ground contact flare together.",
        aftermath="Burst collapses into a white smoke veil with drifting gold motes."),
    "cm-8newabilities-12": dict(
        ability="Explosive ability on the sand board",
        windup="Orange glow spreading across the target tiles as a ground telegraph.",
        impact="Orange explosion with a white core punching up off the tile.",
        aftermath="Orange scorch left on the tiles with a white smoke column and tan debris chips."),
    "cm-8newabilities-13": dict(
        ability="Area slam on the sand board",
        windup="White-hot charge with the tiles under the target already glowing orange.",
        impact="White-gold burst with debris thrown radially outward.",
        aftermath="Best aftermath in the set: orange scorch ring on the ground, tall white smoke plume, cyan residual glow and green debris confetti in one frame."),
    "cm-8newabilities-14": dict(
        ability="Ring-shaped knockback ability",
        windup="Gold charge with a white sphere forming at the caster.",
        impact="White-cyan core inside a gold ring sphere — clean separation of core and rim layers.",
        aftermath="Cyan-green residual glow sits on the tile after the ring has passed."),
    "cm-8newabilities-18": dict(
        ability="Explosive ability on the dark industrial board",
        windup="Cyan energy streaks converging with a gold gather at the caster.",
        impact="Orange fire sitting directly beside cold blue-white energy — warm/cool contrast inside one effect.",
        aftermath="Magenta and white smoke with blue residual glow over the dark board."),
    "cm-8newabilities-19": dict(
        ability="Electric/energy strike on the dark board",
        windup="Cyan charge streak drawn toward the target before the hit.",
        impact="Orange fire core with a cyan energy trail — reads hot and electric at once.",
        aftermath="Blue residual glow on the struck units with light white smoke."),
    "cm-8newabilities-20": dict(
        ability="Heavy melee finisher on the dark board",
        windup="Orange-gold charge built up on the weapon at full wind-back.",
        impact="Orange-magenta burst with a white core against the dark board; the strongest value contrast in the set.",
        aftermath="Cyan residue clings to the struck unit while smoke lifts off the tile."),
    "cm-clashabilities-02": dict(
        ability="Clash ability — opening sword strike",
        windup="Attacker mid-swing, gold sparks starting at the blade.",
        impact="Warm gold burst hugging the ground with white core and four-point sparks, on the bright green board.",
        aftermath="Gold star sparks hang in the air after the burst has gone, with a small white smoke puff."),
    "cm-clashabilities-03": dict(
        ability="Clash ability — second strike",
        windup="Wind-up pose with the board otherwise clean and readable.",
        impact="Compact gold burst with star sparks; small effect that still reads clearly at board scale.",
        aftermath="Sparks and a low white dust puff remain at the contact tile."),
    "cm-clashabilities-04": dict(
        ability="Ranged clash ability",
        windup="White energy charge forming in front of the caster.",
        impact="White-cyan cloud burst with a bright core, unit silhouettes still readable through it.",
        aftermath="Cloud thins into white smoke drifting across two tiles."),
    "cm-season2-05": dict(
        ability="Water-themed super on the underwater board",
        windup="Cyan water column gathering upward — one of the clearest 'energy gathering' frames captured.",
        impact="White-cyan water column at full height with foam detail at the base.",
        aftermath="Column collapses into a wide white foam cloud that lingers over the tiles."),
    "cm-season2-10": dict(
        ability="Board-scale hit on the underwater stage",
        windup="Units closing, effect not yet triggered.",
        impact="White-teal burst with star sparks against the teal board.",
        aftermath="Teal splash residue and foam sit on the tiles after the burst."),
}

SELECT = {
    "impact": [
        "cm-everyability-03", "cm-everyability-05", "cm-everyability-10",
        "cm-everyability-11", "cm-everyability-12", "cm-everyability-14",
        "cm-everyability-18", "cm-everyability-20", "cm-everyability-22",
        "cm-everyability-23", "cm-everyability-26", "cm-everyability-27",
        "cm-everyability-30",
        "cm-8newabilities-07", "cm-8newabilities-08", "cm-8newabilities-12",
        "cm-8newabilities-13", "cm-8newabilities-14", "cm-8newabilities-18",
        "cm-8newabilities-19", "cm-8newabilities-20",
        "cm-clashabilities-02", "cm-clashabilities-03", "cm-clashabilities-04",
        "cm-season2-05", "cm-season2-10",
    ],
    "windup": [
        "cm-everyability-03", "cm-everyability-05", "cm-everyability-10",
        "cm-everyability-14", "cm-everyability-20", "cm-everyability-22",
        "cm-everyability-23", "cm-everyability-27", "cm-everyability-30",
        "cm-8newabilities-07", "cm-8newabilities-08", "cm-8newabilities-12",
        "cm-8newabilities-13", "cm-8newabilities-14", "cm-8newabilities-18",
        "cm-8newabilities-19", "cm-8newabilities-20",
        "cm-clashabilities-04", "cm-season2-05",
    ],
    "aftermath": [
        "cm-everyability-03", "cm-everyability-05", "cm-everyability-18",
        "cm-everyability-20", "cm-everyability-22", "cm-everyability-23",
        "cm-everyability-26", "cm-everyability-27", "cm-everyability-30",
        "cm-8newabilities-07", "cm-8newabilities-08", "cm-8newabilities-12",
        "cm-8newabilities-13", "cm-8newabilities-14", "cm-8newabilities-18",
        "cm-8newabilities-19", "cm-8newabilities-20",
        "cm-clashabilities-02", "cm-clashabilities-03", "cm-clashabilities-04",
        "cm-season2-05", "cm-season2-10",
    ],
}

GENERAL = {
    "cm-8newabilities-g02": "Green board mid-exchange: gold hit burst, floating damage number, health bars — shows how VFX shares space with UI.",
    "cm-8newabilities-g03": "Violet arena with simultaneous white and gold effects; good read on effect density during a busy round.",
    "cm-8newabilities-g04": "Cyan-white beam crossing the board — shows how a linear effect is kept narrow so the grid stays readable.",
    "cm-8newabilities-g05": "Sand board with orange scorch, cyan residue and several units — the full warm/cool palette in one shot.",
    "cm-8newabilities-g06": "Crowded green board with layered small effects; reference for keeping many simultaneous FX legible.",
    "cm-8newabilities-g07": "Dark industrial board with orange, white and magenta effects — strongest value contrast of the general set.",
    "cm-clashabilities-g02": "Clean top-down green board with units and light FX; good neutral baseline for the art style.",
    "cm-clashabilities-g03": "Green board with damage numbers and star sparks mid-exchange.",
    "cm-clashabilities-g04": "Green board with a white burst plus dust — typical mid-combat frame.",
    "cm-everyability-g01": "Violet board with white-pink glow across the tiles; shows tile-level lighting response to an effect.",
}


def main():
    manifest = []
    counts = {}
    for cat in ("windup", "impact", "aftermath", "general"):
        os.makedirs(os.path.join(ROOT, cat), exist_ok=True)
        items = ([(f"{k}-{cat}", k) for k in SELECT[cat]] if cat != "general"
                 else [(k, k) for k in GENERAL])
        for fname, key in items:
            src = os.path.join(STAGING, "cand", cat, fname + ".jpg")
            if not os.path.exists(src):
                print("MISSING", src)
                continue
            dst = os.path.join(ROOT, cat, fname + ".jpg")
            shutil.copy2(src, dst)
            w, h = Image.open(dst).size
            tag = key.rsplit("-", 1)[0] if cat != "general" else key.split("-g")[0]
            meta = SOURCES[tag]
            secs = TIMES.get(fname)
            url = f"https://www.youtube.com/watch?v={meta['id']}"
            if secs is not None:
                url += f"&t={int(secs)}s"
            if cat == "general":
                desc = GENERAL[key]
                ability = "General gameplay"
            else:
                desc = M[key][cat]
                ability = M[key]["ability"]
            manifest.append(dict(cat=cat, file=fname + ".jpg", w=w, h=h, url=url,
                                 game=meta["game"], title=meta["title"],
                                 ability=ability, desc=desc))
            counts[cat] = counts.get(cat, 0) + 1
    json.dump(manifest, open(os.path.join(STAGING, "manifest.json"), "w"), indent=1)
    print(counts, "total", len(manifest))


TIMES = {}
if os.path.exists(os.path.join(STAGING, "ranked.json")):
    for r in json.load(open(os.path.join(STAGING, "ranked.json"))):
        if r.get("t") is not None:
            TIMES[r["stem"]] = r["t"]

if __name__ == "__main__":
    main()
