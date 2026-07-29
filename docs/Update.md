**~ Post-match battle report with plan reveal** — *on hold, built but hidden*

- The game's whole conceit is hidden simultaneous orders, but players never see what the opponent actually planned — replaying both committed plans round by round turns the core mechanic into a payoff and teaches the game better than a tutorial.
- A static per-round summary shipped and was judged the weaker half of the idea; it should be a **replay**, not a table. The UI is hidden (`report-reveal` carries `hidden`) and nothing on the match-end path feeds it. Server-side recording in `BattleReport.cs` + `GameLoop` is kept and still tested, so the data any replay needs is already there: both crews' committed orders, ability targets, dodges, deaths, and hill state per round.
- **Blocked on determinism.** The match cannot be re-simulated: movement steps on `Time.deltaTime` inside `yield return null` loops (`Movement.cs:195`), `Time.timeScale` scales those steps, bullets are PhysX rigidbodies resolved by collision callbacks, shot cadence quantizes to frames via `WaitForSeconds`, target ties break on `FindGameObjectsWithTag` order (`Shooting.cs:447`), and `UnityEngine.Random` is a global stream shared with VFX. Seeding the spread alone changes nothing.
- Two ways forward: (a) record combat events — shooter, target, damage, timestamp — and render them on a timeline beside the plans, cheap and needs no determinism; or (b) convert combat/movement to a fixed-timestep seeded simulation, which is a rewrite but is also the prerequisite for the seeded self-play the experiments catalog wants (EXP-A02/A03).

- X **Last-known-position fog ghosts**
  - Leaving a translucent marker where an enemy was last seen is client-local and already listed as the top Phase 2 fog item in `FogOfWar-and-DamageRebalance.md` §1.8, which calls it a big usability win.

- [ ] **Character info with real stats**
  - The roster screen shows portrait, name, description, and ability name only, so players pick a crew without seeing the range bands, HP, damage, or cooldowns that the entire skill ceiling is built on.
  - [ ] Create an all characters screen, linked from title
    - [ ] carosel with the character model on it
    - [ ] stats
    - [ ] ability info
  - [ ] Sort characters by classes
- [x] **Audio and settings persistence**
  - `AudioManager` holds a music source, an SFX source, and a click clip but has empty `Start`/`Update` with no volume UI, and the quality dropdown resets every launch because nothing is saved.
- [x] **Reconnect grace**
  - Any drop currently ends the match instantly via `MatchResultReason.DisconnectForfeit`, so one wifi hiccup kills a session with a friend.
- [ ] **Guided first match**
  - Simultaneous hidden orders plus a dodge window can't be taught by the static text modal in `TitleScreen.uxml`, but build it as contextual prompts inside a bot match rather than a scripted lesson so pacing changes don't force a rewrite.
- [ ] **More maps**
- [ ] **Protect the President**
  - Genuinely suits the game because it creates attack/defend asymmetry and a comeback vector, but it's the most expensive item here and King of the Hill's own failure modes (too-fast three-streak, endless contested resets) should be resolved first.
- [ ] **Sixth character**
  - Mechanically straightforward (data asset, prefab variant, catalog entry, network prefab, ability class), but it needs a model, portrait, animations, and bot logic while widening a balance space you currently can't measure.

