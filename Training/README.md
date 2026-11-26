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
python3 train.py --init models/bot.pt --fog-start 0.5 --fog-end 0.5 --entropy 0.003 --benchmark models/bot.pt
```

Self-play PPO: the learner only ever fights saved copies of itself and never copies another player's
moves. `--init` continues from a trained checkpoint; without it training starts from random weights.
The policy picks each unit's path a step at a time or an ability target cell (155 masked actions),
and dives during dodge windows. Checkpoints and logs go to `Training/runs/` (gitignored):
`policy.pt` is the latest weights, `best.pt` the best evaluation, `log.jsonl` one record per update,
`pool/` the saved copies.

- Opponents: a copy of the learner joins the pool every `--snapshot-every` updates and the last
  `--pool-size` are kept, starting with the `--init` weights. The newest copy plays `--latest-share`
  of games and older copies split the rest, so the learner keeps pace with itself without forgetting
  how to beat earlier styles. Copies that leave the pool are deleted except every 100th update, kept
  for tournaments.
- Anchors: `--anchors` keeps checkpoints such as every past champion in the pool for the whole run
  (`--anchor-share` of games, 20% by default), so the learner can't drift into a style that only
  beats its recent selves.
- Fog of war: `Config(fog_of_war=False)` matches the game's fog-off option, where every enemy is
  observable (`CanTeamObserveUnit` is always true) but smoke and walls still block shots. One policy
  plays both modes; a scalar tells it which. Training starts fog-off and ramps to 60% fog games
  between 20% and 60% of the run (`--fog-start`, `--fog-end`, `--fog-ramp-begin`, `--fog-ramp-end`).
- Sides: the learner plays a random side each match. Team 1 sees the board rotated 180° (maps and
  spawns are rotationally symmetric), so both sides share one point of view.
- Abilities: target-cell scores are offset by the log of the number of legal cells, so the urge to
  cast is not inflated by how many cells happen to be legal; an untrained policy casts about as often
  as it takes any one move.
- Moves are scored from the network's features at the four neighbouring cells, so a lesson learned
  in one place applies everywhere.
- The value network sees true enemy positions even under fog (it is only used during training), so
  the learning signal stays clean when fog is on.
- Simulation and the opponents' moves run in `--workers` processes; the learner's moves and the
  update run on the Mac GPU (`--device mps`) when available. Each update logs policy entropy
  (1 = uniform), value explained variance, KL, clip fraction, results against the pool and how long
  the rollout and update took. Every `--eval-every` updates it plays the scripted bot in
  `battlesim/baselines.py` and any `--benchmark` checkpoints with fog on and off; `best.pt` is the
  checkpoint with the best mean win − loss. These are yardsticks only, never training opponents.
- The scripted bot dodges away from the threat, then away from known enemies, then at random. An
  earlier version broke dodge ties toward the top of the board, which sent team 0 toward the enemy
  and team 1 away from it, and gave team 1 a 16-point edge in scripted mirror matches.

Evaluate a checkpoint over many games, both sides and both fog modes, with 95% intervals:

```bash
python3 evaluate.py runs/<run>/policy.pt --opponents scripted teacher runs/<other>/policy.pt
```

Evaluation plays every seed twice with sides swapped, so a mirror match is exactly even and map,
roster and side luck cancel out of comparisons.

Promoting a new bot goes through a gate that guards against circular progress, where each new bot
beats the last one by exploiting it while getting worse against older styles:

```bash
python3 evaluate.py runs/<run>/pool/u*.pt --champion models/bot.pt --opponents scripted teacher runs/hall/*.pt
```

A candidate passes only if its win − loss margin against the champion is above zero by more than its
95% interval (both sides and fog modes pooled), and it is not clearly worse than the champion against
any other opponent: the scripted bots and every earlier champion (`runs/hall/`).

### Current bot

`models/bot.pt` plays both fog modes. Each version had to pass the gate above.

| Version | How it was made | vs previous champion, both fog modes |
|---|---|---|
| v0 | Earlier pipeline, since removed: copied the scripted bot, PPO against it, then self-play | — |
| v1 | Round 2 below, update 200 | +26% ±6% margin |

| v1 against (400 paired games each) | Fog: win / loss | Fog off: win / loss |
|---|---|---|
| Scripted bot | 70% / 19% | 61% / 30% |
| Scripted bot, no random moves | 66% / 23% | 62% / 31% |
| v0 | 56% / 28% | 58% / 35% |

Self-play rounds from v0:

1. Plain self-play (`--lr 3e-4`, shaping on, no anchors): drifted in circles. It beat its recent
   selves 57% / 35% while sliding to 37% / 49% against v0, so it was stopped at 1.7M steps.
2. `--anchors runs/hall/bot-v0.pt --anchor-share 0.3 --lr 1e-4 --lr-end 0 --rollout 128
   --hp-weight 0 --count-weight 0 --critic-warmup 5 --fog-start 0.5 --fog-end 0.5 --entropy 0.003`,
   4M steps: improved against every opponent and levelled off after about 2.3M steps.

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
- `train.py`: self-play PPO trainer; `tests/test_training.py` covers fog-off mode, side rotation,
  the policy heads and the opponent pool.
- `battlesim/sim.py`: one round of execution.
- `battlesim/env.py`: match driver and `PathBuilder`, where a policy picks each unit's path
  one step at a time (stay/up/right/down/left) with illegal steps masked.
- `battlesim/observe.py`: board-to-tensor encoding for a policy network.
- `battlesim/baselines.py`: a scripted opponent for testing.
- `parity.py`: replays recorded Unity rounds and reports agreement.
