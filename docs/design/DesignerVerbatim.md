# Designer Verbatim Log

Source of truth for character-design direction. Only the designer's own words go in this file,
copied exactly as given (typos and all) — no paraphrasing, no Claude commentary, no analysis.
Interpretation, feasibility notes, and specs derived from these words belong in other docs under
`docs/design/`, never mixed into this one. New entries are appended below with a date heading;
existing entries are never edited or reworded after the fact.

## 2026-08-20

I have a list of character reworks/additions for you to make. I want you to figure out how to divide up the tasks (even more granular than how i have it; eg one for model, effects, character mechanics, etc) and spawn a fleet of subagents. Dont run too many subagents at once because my laptop cant handle that much. Make sure to have the subagents check that someone is not using the editor before they use it to test. Don't set an arbitrary amount of time to wait for editor testing to complete. It should be easy for me to test and give feedback/accept/reject on each character addition or rework individually.

All character design questions (direction changing, not small things like number balancing) go through me (ideally front load your questions to me at the start of development). They should go into a single doc that only my verbartim text can be in. This should be the source of truth; do not hide new guiding comments in the code. If there is anything you are either unsure of or you don't have a concrete way of doing (eg if you dont know how the characters of clash mini work), you pause and tell me.

The List:
- Have Shotgunner Dash be less about the shield and more about the dash.
    - Have it knock enemies out of the way and stun them. Shotgunner is an initiator.
- Names for characters need to be more than their weapon (eg since multiple shotgunners)
- have commander's smoke ability last for two rounds instead of one

- Have another character take over the shield (not the dash, as a defensive ability). Give the character a pistol.
- Have a machine gun character. 0 reload time (or whatever it needs to be to make it seem to shoot without interruption). Shooting needs to warm up a bit. Ability is that it shoots a wide barrage of bullets through adjacent walls. Ability can be interrupted if stunned.
- Have a rocket launcher character. High area damage but slow projectile speed. Ability is shooting a big rocket that does big area damage and breaks walls.
- Electricity guy that chains or attacks multiple people. Ability is ewiz from clash mini with less stun.
- Magic archer from clash mini
 - Someone whos ability is jumps aggressively and deals damage with that jump (unlike pogo which does it as movement); add the debris animation for the ability. Maybe an agile/damage-dealer shotgunner.
- Someone that deals more damage if their attack hits consecutively. Ability is attack speeds increase for it and (less so) for nearby allies.
- Run and gunner

## 2026-08-20 — clarifying answers, round 1

Q: How should stun + knockback actually behave when Shotgunner dashes through enemies? (Full lock, telegraphed / Soft lock / Let me describe it)
A: Full lock, telegraphed. for shotgun dash specifically, knockback should be pushing enemies off the path of the dash

Q: The rocket launcher's ability is supposed to 'break walls' — but there's no wall-destruction system at all right now. How far should this go? (Visual-only crumble / Permanent removal / Skip wall-breaking for now)
A: Permanent removal

Q: None of the 6-8 new characters have any 3D models, rigs, or animations yet. How should I handle art for this batch? (AI-generate placeholders now / Blockout with primitives/spares / Skip new art entirely for now)
A: AI-generate placeholders now (Recommended)

Q: You flagged that names need to be more than the weapon (e.g. multiple shotgunners). Since names are character identity, how do you want to handle naming? (I draft candidates, you approve / You'll name them)
A: Give them a proper name for now. Draft candidate names to give me to approve as future steps.

## 2026-08-20 — clarifying answers, round 2

Q: For 'Magic archer from clash mini' — I know Clash Royale's version (piercing arrow that hits everyone along a line, extended range) but I'm not certain Clash Mini's matches it, and your note doesn't say whether this describes the basic weapon or just the ability. How should I proceed? (Ability only, Royale-style / Whole kit pierces)
A: Basic kit pierces. Ability is it launches a cone of three arrows horizontally

Q: For the electric guy's ability ('ewiz from clash mini with less stun') — I know Clash Royale's Electro Wizard (zaps everyone in a small radius around him, damage + brief stun on every hit) but I'm not certain Clash Mini's version matches exactly. OK to use the Clash Royale version as the reference, just with a shorter stun? (Yes, use Clash Royale's version / No, Clash Mini's differs)
A: Not its different. Basic attack is like pistol (single fire, no stun), but looks like a weak lighting bolt from his hands. Ability is he zaps (good damage, some stun) up to 5 enemies in range using lightning bolts from his hand. Dodge radius is a largeish radius around him. No targeting square needed.

Q: 'Run and gunner' has no ability description in your list — what's the core gimmick? (Shoots while moving / Mobility + burst ability / Something else)
A: Shoots while moving (Recommended)

Q: For the 'aggressive jump' damage-dealer — you hedged with 'maybe an agile/damage-dealer shotgunner.' Lock in shotgun as its weapon? (Yes, shotgun / No, different weapon / Your call)
A: Yes, shotgun

## 2026-08-21

and have proper visuals

and rocket launcher ability should be a horizontal rocket (no arch)

fire rate and ability speed should be tuned down for all the new characters. Piercing bullets dont work. Bullets should be destroyed if they hit a wall. Rocket launcher ability should have ability selection like that of the sniper (whole board, rocket range is determined seperately)

rocket luancher's basic attack should explode. its ability should be less big and less damaging

breach's ability should have a set range (unless interrupted by an enemy or wall). lets say this range should be 8 for now

I said slow all fire rate and ability time for all the new characters. also lower the magazine sizes. the total damage should be comparable to the original 5 characters. there should be a ability recovery time before the unit can shoot.

abiility recovery is specific to each ability. just before you stop the ability, you just have it wait for some time

ability recovery only for the new characters (such as rocket launcher)

for the voltaic sandbox, have more enemies in range (to test out the ability)

although voltaic ability does not have square selection, it should have ability radius (similar to how grenade has the blast radius) shown centered on the unit

remove zealot from the game. outrider should not have an ability
