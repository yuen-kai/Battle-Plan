# Battle Plan grid simulator

A fast, headless Python re-implementation of a Battle Plan combat round, for training a bot.
Elimination mode, the original five units (Commander, Pogo Rider, Ramrod, Sniper, Soldier),
with their abilities (Smoke Screen, Pogo, Ram Charge, Area Lock, Grenade) and the dodge phase. Unit stats are read from `Assets/UnitStats/*.asset` and maps from
`Assets/Scripts/Map/MapCatalog.cs`, so balance and map edits flow through automatically.

```bash
cd Training
python3 -m pytest -q
python3 bench.py --matches 200
```

## What is ported from the game

- Planning: 4-direction paths up to `moveDist`, no walls or revisits, units walk through
  each other, allies cannot share an end cell (`ApplyFriendlyEndCellSeparation`).
- Movement at `moveSpeed` cells/s, turning while walking at `rotationSpeed`.
- `Shooting.InitiateShooting` as a coroutine-for-coroutine port: fire starts when a unit's own
  path ends, nearest valid target, turn cost `(angle - 1°) / rotationSpeed`, Sniper lock and
  lock reveal, magazines and reloads, cease-fire one frame after the last mover stops (each unit
  finishes only its current magazine), ammo refilled each round, health kept.
- Abilities: legality (`SanitizeAbilityPlan`), cooldowns (N rounds unavailable after use), the
  dodge phase (alerts from `responseRange`, dives of up to `diveRange` at `diveSpeed` that replace
  the plan, 2 s recovery), Grenade (80 flat within 1.6 cells + body radius, walls and enemy bodies
  shield), Smoke (3×3 for this round and the next, blocks sight and new shots), Pogo (1 s
  untouchable jump, 1.5 s return-fire window), Area Lock (3 s beam, 130 to the first enemy across
  it), Ram Charge (3-cell dash, shoves and stuns enemies on its line, speeds up nearby allies).
  Every unit, gun, walk, ability, shove and stun runs as a coroutine on one clock, like Unity, and
  the Unity frame delays that decide close cease-fire races are modelled.
- Fog: Manhattan `visionRange` with the integer grid line-of-sight walk (`HasGridLineOfSight`).
- Targeting: projectile-radius sweep against octagonal walls (with corner plugs) and enemy
  bodies within `targetRange`; the game's corner-peek tests are reproduced in `tests/test_vision.py`.
- Shots: aimed at the target's current position with no lead, uniform spread over ±`bulletSpread`.
  Each shot's damage is split across every enemy by the share of the spread in which that enemy
  is the first thing the bullet would touch (walls absorb their share), using where each enemy
  will actually be when the bullet arrives, so stray bullets hit bystanders. `Config(sample_hits=True)` rolls each shot
  instead. Pogo backstab, enemy shoves onto held cells, simultaneous-wipe draws.

## Known gaps

- Stun interrupting an ability's first frame, Shield blocking (no original-five unit has one), and
  Close Ranks are not modelled.
- Frame-level timing (the game fires at most one shot per frame), the 0.3 s shove slide,
  one-frame corpses and small 3D height differences are approximated.
- The game has no round limit; the sim calls a draw after `Config.max_rounds` (40).

## Checking the sim against the real game

`Assets/Scripts/GameManager/DevParityLog.cs` (editor only) plays solo bot matches with random
original-five crews on every map, scripting both crews (half bot movement, half random walks, no
abilities), and writes each round's start state, executed paths and outcome to
`Training/parity/rounds-*.jsonl`. Run it from **Battle Plan ▸ Record Simulator Parity Rounds**, or
in Play mode with `DevParityLog.Run(matches, speed)`; it leaves Play mode when done. Then:

```bash
python3 parity.py
```

replays every recorded round from its exact start state and compares damage, deaths, shots, end
cells and round length. `--production-frames` simulates at 60 fps instead of the recording's frame
time (dev fast-forward makes each frame cover more game time, and units lose about a frame per cell).

| Recording | Rounds | Total damage sim/real | Team damage per round | Per-unit damage |
|---|---|---|---|---|
| Movement only | 163 | 0.98 | correlation 0.98 | MAE 4.6, correlation 0.87 |
| With abilities and dodges | 159 | 1.01 | correlation 0.98 | MAE 4.8, correlation 0.89 |

The luck-only floor (sim rolled shots vs sim average) is about MAE 3.0 / correlation 0.94. Rounds
containing each ability correlate 0.96–0.99 on team damage.

## Training

```bash
python3 train.py --steps 1200000
```

PPO against the scripted opponent in `battlesim/baselines.py`. The policy picks each unit's path a
step at a time or an ability target cell (155 masked actions), and dives during dodge windows.
Checkpoints and logs go to `Training/runs/` (gitignored).

Reward (`battlesim/reward.py`), per round:

- Match result: +1 win, −1 loss, −0.5 draw (including the round cap), minus 0.01 per round.
- Shaping that fades linearly to zero by `--shaping-fade` (80% of training by default): the change
  in a potential made of HP-share advantage and units-standing advantage, 0.5 each. It is
  potential-based (γΦ' − Φ, with Φ = 0 at the end), so it guides early learning without changing
  which strategy is best, and overkill never counts.
- Discounting (`--gamma` 0.98, `--lam` 0.95) applies per round, not per decision, so a win twenty
  rounds away still counts for about two thirds.

## Layout

- `battlesim/units.py`, `maps.py`, `spawns.py`: game data loaded from the Unity project.
- `battlesim/vision.py`: fog and line-of-sight geometry.
- `battlesim/sim.py`: one round of execution.
- `battlesim/env.py`: match driver and `PathBuilder`, where a policy picks each unit's path
  one step at a time (stay/up/right/down/left) with illegal steps masked.
- `battlesim/observe.py`: board-to-tensor encoding for a policy network.
- `battlesim/baselines.py`: a scripted opponent for testing.
- `parity.py`: replays recorded Unity rounds and reports agreement.
