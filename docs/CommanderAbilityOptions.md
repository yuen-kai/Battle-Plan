# Commander Ability Options

Status: selected for implementation. The Commander uses a radial **Smoke Screen**: a 3×3, nine-cell area centered on its committed target. It is public, symmetric, movement-permeable, and clears before the next planning phase.

## Baseline and decision rules

The Commander has no live `Ability` component and is currently roster-ineligible. The old **Reroute** concept is not a viable baseline: it paused execution and reopened planning, which conflicts with Battle Plan's plan → dodge → execute cadence.

The Commander should earn a roster slot by making one committed, high-information support play. Every candidate below is evaluated against these non-negotiables:

- Resolve from the committed plan; never pause combat or reopen planning.
- Be public at execution start and server-authoritative.
- Preserve meaningful spatial counterplay and avoid an invisible math-only advantage.
- Respect fog-bounded bot knowledge and deterministic tie-breaking.
- Fit a one-use-per-match baseline without requiring an economy rework.

Scores are directional (1 poor, 5 strong), not proof that an option is fun:

| Option | Distinctive | Commander fantasy | Cadence fit | Clarity/counterplay | Feasibility | Bot fit | Balance risk |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Directional Smoke Wall | 5 | 4 | 5 | 5 | 4 | 3 | Medium |
| Radial Smoke Screen | 5 | 4 | 5 | 4 | 4 | 3 | Medium |
| Rally Beacon | 3 | 5 | 3 | 3 | 2 | 2 | High |
| Cover Drop | 4 | 3 | 5 | 4 | 2 | 3 | High |
| Overwatch Order | 3 | 4 | 4 | 3 | 3 | 3 | Medium-high |
| Evac Corridor | 4 | 5 | 4 | 4 | 3 | 3 | Medium-high |
| Signal Jam | 4 | 4 | 5 | 3 | 3 | 2 | High |
| Intel Flare | 3 | 4 | 5 | 4 | 4 | 4 | Medium |
| Guard Formation | 2 | 4 | 5 | 2 | 4 | 4 | High |

## 1. Directional Smoke Wall

**Pitch:** The Commander commits a direction during planning. At execution, a short smoke segment appears across that lane and remains active only for that used round; it clears before the following planning phase. Shots crossing it fail for either team; units can walk through it.

**Why it fits:** It expresses command as terrain control rather than damage. A wall turns “I protect the team” into a readable spatial decision: draw a line between a threatened ally and a sniper lane, then invite the opponent to route around, push through, or use a different angle. It uses the existing directional ability path, so the input language already exists in the game.

**Turn-to-turn use:** Rescue a teammate caught in a long sightline, break an enemy firing lane before a push, or create a short-lived rotation lane. The Commander pays for protection by potentially cutting off friendly fire too.

**Counterplay and information:** The wall is telegraphed to both sides, is symmetric, does not stop movement, and disappears after the round. Enemies can avoid the lane, cross it, or punish the Commander’s cast position next round.

**Implementation path:** Add a transient, grid-aligned line-of-sight blocker used by the shared shot-LoS check while leaving `wallLayout` and pathfinding unchanged. It must deliberately define whether grenade/Area Lock LoS uses the same blocker. The bot needs to identify a threatened ally/enemy line from visible or last-known information.

**Balance levers:** segment length, cast range, Commander HP/vision, and one-use economy. Duration is fixed: the wall exists for the used round only, then clears.

**Self-critique:** A wall can be overly exact: it may feel like a rules exception rather than smoke if line intersections are visually ambiguous. It also risks becoming a universal answer to sniper lanes if the segment is too long. The support bot work is real; without it, the Commander is underrepresented in automated matches.

## 2. Radial Smoke Screen

**Pitch:** The Commander targets a cell and fills a compact circular/cell-quantized area with smoke that blocks shots passing through it for the combat window.

**Why it fits:** This is the most immediately legible version of “cover the retreat.” It protects a cluster of allies or disrupts a contested center without pretending the Commander can rewrite another unit’s move.

**Turn-to-turn use:** Place smoke on a choke, around a vulnerable ally’s likely destination, or over a central firefight to force a close-range contest. It is broader and more forgiving than a wall.

**Counterplay and information:** The placement is public, blocks friendly shots too, permits movement, and clears next round. Opponents can push into the cloud, reposition to an edge, or exploit the fact that the Commander has spent its only utility.

**Implementation path:** Reuse existing square-targeting and ability-radius data, backed by the same transient LoS mechanism as the wall. The bot evaluates candidate cells that interrupt an observed or remembered threatening lane while not suppressing its own winning shot.

**Balance levers:** cast range, radius, duration, cloud-cell LoS rule, and cast self-exposure.

**Self-critique:** A cloud is less precise, which can create confusing “why did that shot fail?” moments around its edges. It can also dominate a central objective more than intended and may be visually similar to fog unless the VFX and cell boundary are exceptionally clear.

## 3. Rally Beacon

**Pitch:** The Commander places a public beacon that grants nearby allies a small movement or dodge benefit for that round.

**Why it fits:** This is the literal leadership fantasy: the Commander coordinates a push rather than merely blocking fire. It gives the team a proactive rhythm and can make a planned assault feel organized.

**Turn-to-turn use:** Enable a joint advance, improve an ally’s retreat odds, or help a team reach a KOTH point. It rewards planning a formation before execution.

**Counterplay and information:** The beacon must be public and its affected cells obvious. Enemies can avoid the zone, break the formation, or take advantage of units clustering around a predictable benefit.

**Implementation path:** Requires new movement-budget or dodge-budget plumbing across committed plans, UI explanation, and bot valuation. The ability cannot silently alter a player’s already committed movement in a way that feels like lost agency.

**Balance levers:** radius, bonus distance, affected allies, duration, and whether it applies to movement or dodge—not both.

**Self-critique:** This is dangerously close to a hidden numeric buff. It adds new edge cases across movement, collisions, dodge windows, and path validation, while being harder to read than smoke. It should not be selected as a first Commander rework unless smoke testing shows LoS denial is fundamentally unfun.

## 4. Cover Drop

**Pitch:** The Commander deploys a temporary hard barrier on a target cell.

**Why it fits:** A Commander establishing cover is intuitive and delivers clear protection. It has a strong tactical-board-game identity.

**Turn-to-turn use:** Close a lane, hold a corner, or make a temporary safe staging area for a teammate.

**Counterplay and information:** A public barrier is very readable, but the counterplay is weaker than smoke because it can physically deny routes. Enemies need an alternate path or a way to remove it.

**Implementation path:** Unlike smoke, a hard blocker must change pathfinding and collision consistently, handle units whose committed path becomes invalid, and synchronize that state. It cannot safely be treated as a visual-only wall.

**Balance levers:** width, lifetime, placement restrictions, destructibility, and whether it blocks abilities.

**Self-critique:** This risks breaking the game’s core promise that plans resolve predictably. It turns one support cast into mid-round map mutation and can hard-lock tight maps. Reject for v1.

## 5. Overwatch Order

**Pitch:** The Commander marks a public lane; an ally who enters or fires within it gains a constrained reaction shot or accuracy benefit.

**Why it fits:** It makes the Commander a tactical coordinator and can reward a well-read enemy route.

**Turn-to-turn use:** Deter a push through a corridor, protect a retreat lane, or amplify a planned hold.

**Counterplay and information:** The marked lane is public, so opponents can reroute, bait it, or force it to expire. Its counterplay depends on precise timing and clear feedback.

**Implementation path:** Requires a reaction system or altered firing priority, neither of which is a current live ability pattern. The bot must predict enemy routing without cheating under fog.

**Balance levers:** lane length, reaction range, trigger count, damage/accuracy modifier, and expiry.

**Self-critique:** It adds timing exceptions and can make firefights feel arbitrary: “why did that unit fire twice?” It overlaps the Sniper’s area-denial role while increasing rule complexity. Defer unless the game later adds a clean reaction framework.

## 6. Evac Corridor

**Pitch:** The Commander marks a directional corridor. Allies moving through it receive protection from ranged fire only while inside the corridor; enemies can enter it normally.

**Why it fits:** It is a more explicitly supportive version of smoke: “move here now.” It makes the Commander a formation leader and preserves movement-based counterplay.

**Turn-to-turn use:** Cover a retreat, guide a push through exposed ground, or create a safe rotation toward the hill.

**Counterplay and information:** Public telegraph, symmetric rules, and a narrow footprint make it readable. Enemies can contest the corridor, flank its endpoint, or deny its exit.

**Implementation path:** Could be implemented as a directional smoke wall with an ally-only protection rule, but that introduces team-asymmetry. A symmetric version simply collapses back into Directional Smoke Wall.

**Balance levers:** length, width, duration, cast range, and whether units inside versus shots crossing are protected.

**Self-critique:** Its unique wording conceals a key problem: if allies alone gain protection, it violates the strongest fairness principle; if both teams do, it is just smoke with extra terminology. Reject as a separate ability; retain its “evacuation route” fantasy as an intended Smoke Wall use case.

## 7. Signal Jam

**Pitch:** The Commander targets an area where enemy abilities cannot activate, or have reduced range, for one round.

**Why it fits:** Electronic warfare reads as command-and-control and can counter explosive ability turns without adding damage.

**Turn-to-turn use:** Preempt an enemy grenade or Area Lock lane, force an opponent to use a normal move, or protect a key objective timing.

**Counterplay and information:** A public jam zone lets opponents move their caster, choose a normal action, or spend their ability before the jam. It needs unmistakable UI because the effect changes intent rather than physical space.

**Implementation path:** Requires ordering rules for simultaneous committed abilities: does the jam cancel an already committed action, prevent consumption, or only affect future turns? It also requires non-omniscient bot prediction of enemy ability intent.

**Balance levers:** radius, range, target team, affected ability types, and duration.

**Self-critique:** This is a direct denial of player agency and is difficult to make fair when plans are simultaneous. It risks creating feel-bad “my button did nothing” moments and demands too much prediction from bots. Reject for the initial rework.

## 8. Intel Flare

**Pitch:** The Commander reveals a target zone or enemy for the round and publicly marks it for both teams.

**Why it fits:** Reconnaissance is a credible Commander role and works naturally with fog-of-war. It enables coordinated targeting without directly changing damage.

**Turn-to-turn use:** Check a suspected sniper perch, validate a route before committing a follow-up round, or contest an objective hidden by fog.

**Counterplay and information:** A flare is public, so the opponent knows it has been scanned and can reposition on the next round. It has no immediate physical protection, which keeps it fair.

**Implementation path:** It must integrate with per-client fog visibility without accidentally exposing hidden state or letting bots use omniscient data. It likely needs a temporary visibility override and clear world/UI feedback.

**Balance levers:** radius, duration, range, reveal precision, and whether it shows cells versus unit identity.

**Self-critique:** It is less clutch than the support fantasy calls for and may be weak in a short, high-lethality round structure because information arrives after plans are committed. It can also make fog less meaningful. A good future recon unit option, but not the strongest Commander identity.

## 9. Guard Formation

**Pitch:** The Commander creates a small aura that reduces incoming ally damage or shares part of it with itself for one combat window.

**Why it fits:** The fantasy of protecting a squad is direct and the ability is mechanically compact.

**Turn-to-turn use:** Brace for an expected alpha strike, hold a point, or escort a low-health teammate.

**Counterplay and information:** The aura can be publicly telegraphed, but players must inspect damage numbers to understand it. Opponents can focus outside the aura or overwhelm the protected group.

**Implementation path:** The existing damage pipeline can likely host a modifier, and the bot can value threatened allies with less new geometry reasoning than smoke.

**Balance levers:** radius, reduction percentage, damage cap, sharing ratio, and whether the Commander must remain alive.

**Self-critique:** It is an invisible combat-math effect in a game whose strongest interactions are positional and visual. It can produce confusing near-lethal survival and invites focus-fire breakpoints. It is technically tempting precisely because it is creatively weaker. Reject for v1.

## Selected implementation

The radial **Smoke Screen** is selected over the directional wall to reduce placement friction. It occupies a fixed 3×3 block—nine grid cells—centered on the plan-time target. Its duration is fixed: it applies for the round in which it is used and clears before the following planning phase. It blocks both shot and fog-of-war sightlines that cross its cell interiors; units at either endpoint retain close-range sight.

The wall remains documented only as a rejected prototype direction. The selectable ability should use existing square-targeting, not directional targeting.

## Prior recommendation

The earlier recommendation was to prototype Directional Smoke Wall first, with Radial Smoke Screen as a fallback. It has been superseded by the selected implementation above.

Radial Smoke Screen is now preferred because it is easier to place and gives a predictable nine-cell footprint. It must still make its exact footprint visible in the planning preview and public execution telegraph.

Do not implement either until these specific risks are answered:

1. A temporary LoS blocker can affect shots without mutating pathfinding or persisting into the next round.
2. The exact interaction with grenade and Area Lock LoS is defined and consistent.
3. A fog-bounded bot evaluator finds positive-value defensive placements and avoids blocking its own winning shot.
4. A single smoke use is impactful without making Commander mandatory; tune only size, range, or duration before adding damage, asymmetry, or a second use.

## Rejected shortcuts

- Do not revive Reroute or any mid-execution replan.
- Do not hide a smoke placement or make it one-way.
- Do not combine this rework with a new ability economy or a Commander stat overhaul.
- Do not unlock Commander in the roster until the ability, bot behavior, telegraph, and deterministic checks pass together.
