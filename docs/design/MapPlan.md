# More Maps — Design Plan

> **Status: proposed.** No code or assets changed. Layouts below are validated offline against a
> port of `GridSystem.HasGridLineOfSight`; they still need the edit-mode gate in §4.

**Prepared:** 2026-07-29
**Scope:** `docs/Update.md` → "More maps". Target Unity 6000.3.1f1.

## 1. Prerequisite: there is no map system

`GameLoop.wallLayout`, `GameLoop.KingOfTheHillCells`, and `CreateSpawnPositions` are static;
`MatchOptions` carries mode/opponent/fog but no map id. Adding a map is a code change today.

Minimum viable system: a per-map record of `walls` + `hill` + `spawns`, a map id on `MatchOptions`,
and a lobby selector. **Constrain v1 to 15×10.** Changing board dimensions also drags in the
`ArenaBuilder` deck, the two fixed camera poses, `gridBounds`, the 150-tile fog overlay pool, and
the map preview's aspect ratio. At fixed size a new map is a pure data swap.

## 2. The map list

The shipped 15×10 arena is unnamed in code and in docs; this plan calls it **Concourse**. All names
are infrastructure nouns from one family so the lobby list reads as a set.

All entries are 15×10, 180°-point-symmetric, fully connected, hill traversable, no spawn inside a
wall. A map record is `walls` + `hill` + `spawns`, so two entries may share a blockout and differ
only in deployment. The set is chosen to span one dial — how exposed the objective is — with
Concourse at the open end and Bastion at the closed end, so the two can be read against each other.

| Name | Identity | Blockout | Deployment |
|---|---|---|---|
| **Concourse** | shipped default; wide and open, long crossing lanes | 18 walls | back ranks, evenly spread |
| **Bastion** | fortified hill with four offset doorways | new, 34 walls | back ranks, evenly spread |
| **Foundry** | dense close quarters, no long lanes | new, 34 walls | back ranks, evenly spread |
| **Causeway** | spines, exposed centre court, protected side corridors | new, 26 walls | back ranks, evenly spread |
| **Concourse Oblique** | diagonal contest on familiar ground | shares Concourse | corner-weighted |

**Build order: Oblique → Bastion → Foundry → Causeway.** Oblique goes first because it needs zero
blockout work and exercises the spawn half of the map record, which is the part of the system most
likely to be wrong. Bastion follows because it sits furthest from Concourse on objective exposure,
so it is the entry most likely to produce a legible comparison early.

### Concourse Oblique

Concourse's blockout unchanged, but each crew masses on one side — blue on `(0,0) (2,0) (4,0)
(6,0) (8,0)`, red on `(6,9) (8,9) (10,9) (12,9) (14,9)` — so the contested axis runs corner to
corner instead of straight up the board.

```
  9  ......r.r.r.r.r
  8  ....#.....#....
  7  .......#.......
  6  ..#..#HHH#..#..
  5  #.....HHH.....#
  4  #.....HHH.....#
  3  ..#..#HHH#..#..
  2  .......#.......
  1  ....#.....#....
  0  b.b.b.b.b......
```

**Intent.** Same geometry, different problem. Every slot now has a distinct job: spawn-to-hill goes
from a flat `3,2,2,2,3` rounds to `3,3,2,1,1`, so a crew has two units that can touch the hill on
round one and two committed to the long way round. Roster order becomes a real deployment decision
rather than a cosmetic left/right choice (see EXP-B03 in
`docs/design/GameplayExperiments-Catalog.md`).

**Cheapest entry in the list.** Zero new art, zero blockout, no preview regeneration beyond spawn
markers, and it reuses a layout already covered by existing tests. Its whole cost is making spawns
per-map data — which the map record needs regardless — so it is the natural first consumer of the
system in §1.

**Key risk, and why it is worth taking.** Round-one hill access is exactly the pressure EXP-D06
flags for Pogo, and Oblique hands it to two slots of any roster. That could make round one
deterministic in KOTH. It is also the only lever in this list that is reversible by editing five
coordinates, so it is the cheapest possible place to learn whether fast hill access is a problem or
a feature. Ship it behind the map selector, not as the default.

### Bastion

The hill becomes a compound with four one-cell doorways — `(8,7)` north, `(6,2)` south, `(5,5)`
west, `(9,4)` east — offset so no straight shaft runs through the objective.

```
  9  ..r.r..r..r.r..
  8  ..#.#.....#.#..
  7  .#...###.#...#.
  6  ..#..#HHH#..#..
  5  ...#..HHH#.#...
  4  ...#.#HHH..#...
  3  ..#..#HHH#..#..
  2  .#...#.###...#.
  1  ..#.#.....#.#..
  0  ..b.b..b..b.b..
```

**Intent.** The opposite pole from Concourse on objective exposure — 11.0 against 48.0 — which makes
the pair a controlled comparison rather than two unrelated boards. Approach collapses from "many
angles" to four doorways, and every ability gets a job at one: Grenade clears a doorway, Smoke blinds
one, Shield Rush breaches one, Pogo vaults the wall entirely.

**Limits.** Contesting needs one body on one cell, so this makes the hill *survivable*, not
*turtle-able* — it turns mutual wipes into a fight for the doorways. And it is not a fix for KOTH's
failure modes: nothing has traced those to geometry, so the claim is only that if exposure drives
them, this is the board where that shows up — at the cost of a wall list rather than a scoring
change.

### Foundry

Dense close quarters — the far end of the openness dial from Concourse.

```
  9  ..r.r..r..r.r..
  8  ..#...#.#...#..
  7  #...#..#..#...#
  6  .#...#HHH#...#.
  5  ...##.HHH.##...
  4  ...##.HHH.##...
  3  .#...#HHH#...#.
  2  #...#..#..#...#
  1  ..#...#.#...#..
  0  ..b.b..b..b.b..
```

**Intent.** Compress the spread between the roster's range bands. No straight line exceeds six cells
anywhere on the board, which is inside Soldier acquisition (4) and Commander/Sniper acquisition (5),
so the Sniper's reach advantage has less room to express and short-range kits spend fewer rounds
crossing open ground. Cover density is the variable EXP-D03 names as the strongest driver of
Shotgunner value, and Foundry is the arm that moves it. Secondary effects to watch, all unmeasured:
more blind corners for fog, and more corridor stacking for Grenade.

**Cheapest new blockout.** The only new layout that also satisfies the stricter mirror symmetry
Concourse happens to have, so existing symmetry assertions and `CoverVariant`'s coordinate folding
keep working untouched.

### Causeway

Two vertical spines split the board into a fast exposed centre court and two protected side
corridors, each with a near and a far doorway.

```
  9  ..r#r..r..r#r..
  8  ...#..#....#...
  7  .......##......
  6  ...#.#HHH#.#...
  5  ...#..HHH..#...
  4  ...#..HHH..#...
  3  ...#.#HHH#.#...
  2  ......##.......
  1  ...#....#..#...
  0  ..b#b..b..b#b..
```

**Intent.** Put a price on route choice. The spines make committing to a side expensive to reverse,
and fog hides which side you took until execution, so the hidden-orders mechanic decides something
structural rather than only the last few cells of a path. The open centre is the priced trade for
speed, not an oversight — it is the reason taking the safe route costs you tempo.

## 3. Measured comparison

Offline, using the exact supercover/permissive-corner LoS model and Manhattan vision diamonds.
*Hill exposure* = mean count of off-hill open cells with line of sight to a hill cell.

| | walls | density | lane through hill | mean cells seen, Sniper (7) | hill exposure | within sniper range 5 | spawn→hill, rounds |
|---|---:|---:|---:|---:|---:|---:|---|
| Concourse | 18 | 12% | 13 | 40.9 | 48.0 | 28.0 | 3,2,2,2,3 |
| Bastion | 34 | 23% | 7 | 22.7 | **11.0** | **8.5** | 3,2,2,3,3 |
| Foundry | 34 | 23% | **6** | 25.4 | 20.0 | 15.7 | 3,2,2,2,3 |
| Causeway | 26 | 17% | 7 | 32.7 | 18.5 | 14.8 | 3,2,2,2,3 |
| Concourse Oblique | 18 | 12% | 13 | 40.9 | 48.0 | 28.0 | **3,3,2,1,1** |

Oblique shares every sightline column with Concourse by construction; deployment is its only
variable, which is what makes it a clean read on spawn geometry.

## 4. Acceptance gate for any new map

Pure checks, no Play mode:

1. 180° point symmetry: `(x,y) ∈ walls ⟺ (14−x, 9−y) ∈ walls`.
2. Every open cell reachable from every other (single 4-connected region).
3. No spawn cell and no hill cell is a wall.
4. Longest straight open run crossing a hill cell ≤ 8.
5. Spawn→hill BFS ≤ 4 rounds at `moveDist` 3 for every slot, and the per-slot profile must be
   identical for both teams. A map with any slot at 1 round — currently only Oblique — is not
   rejected, but must declare it, because round-one hill access is a live balance question
   (EXP-D06).
6. Zero round-0 spawn-to-spawn sightlines at vision 7.

## 5. Touch points and risks

| Item | Impact |
|---|---|
| `Assets/Editor/ArenaBuilder.cs` | `ExpectedWalls` asserts exactly 18 walls and 12 hill cells. Must become per-map. |
| `ExpandedBoardLayout_IsSymmetricConnectedAndUsesFullWidth` | Bastion and Causeway are point-symmetric but **not** mirror-symmetric. The test must accept 180° rotation — which is the correct fairness criterion anyway, since the two team cameras sit exactly 180° apart. Foundry passes either way. |
| `MapPreview.png` + `UIToolkitAssetSmokeTests` | Freshness check; needs one preview per map. |
| `CoverVariant.VariantForCell` | Folds coordinates for mirror-symmetric variant picking. Cosmetic only, but degrades on point-symmetric layouts. |
| `BotPlayer` | Reads `wallLayout` and `KingOfTheHillCells` generically. Every layout stays connected with the hill reachable, so bots should degrade rather than break. Oblique is the exception worth watching: bots send every KOTH unit at the hill (§1.6 of the catalog), which a round-one hill touch may reward far too well. Unverified. |
| Balance evidence | Per `GameplayExperiments-Catalog.md` §2.2, map is a pinned control. Cover density strongly changes Shotgunner value (EXP-D03) and Pogo reach (EXP-D06); stratify by layout instead of pooling maps. |

## 6. Not verified

Every number here comes from an offline port of the LoS code, not from Unity — the editor was held
by another agent. Nothing has been run against the real `GameLoop`, the edit-mode suite, or a live
match. Treat §3 as design evidence, not as test results.
