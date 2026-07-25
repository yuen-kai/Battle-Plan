# Battle Plan: UI content inventory

Purpose: a complete list of what must appear on each screen, and the text currently used for it.
This is a content brief only. It says what has to be present, never how it should look or where it
should sit.

## How to use the quoted text

Treat the quoted strings as the current copy and the record of what each piece of text has to
communicate. They are not locked, apart from the list in "Strings you cannot change" below. You may
rewrite wording where you can say the same thing better.

What rewriting means here:

- Same meaning, same job, fewer or equal words. Every string exists because the player needs that
  information at that moment. Carry the information; drop anything that is not information.
- Shorter is free. Longer needs a reason you could defend, and "it reads better" is not one.
- Never add a string that has no entry here. No taglines, no subtitles under headings, no helper
  text beside a control that already explains itself, no section labels for sections that read fine
  unlabelled, no encouragement, no personality lines. If a slot exists and the content list does not
  fill it, leave it empty or remove it.
- Never split one entry into several, and never invent a state that does not exist. The state lists
  here are exhaustive.
- A label names the thing. A button says what pressing it does. An error says what happened and what
  to do next. A status says what the game is doing right now. None of them sell, apologise, or
  explain twice.
- Keep the voice already here: plain, sentence case, no exclamation marks except in the win and lose
  results where they already appear.

For scale: turning "How should this match play?" into "Match settings" is in bounds. Adding a line
beneath it that explains you should pick a mode and an opponent is not, because no entry here asks
for one.

Mechanical conventions that survive any rewrite:

- Every string describing something in progress ends in a real ellipsis character (`…`), never three
  periods.
- Placeholders shown with sample values (a unit name, a number) are filled at runtime. You may change
  the text around a placeholder, but not the values themselves or how many there are.
- Where a term appears on more than one screen, rewriting it once means rewriting it everywhere. The
  recurring terms are listed in the final section.

Tech context: all UI is Unity UI Toolkit (UXML + USS). There is no world space or TextMeshPro UI.
Four screens exist, one per scene.

| Screen | Scene | Document |
| --- | --- | --- |
| Title | `Title Screen` | `Assets/UI/Title/TitleScreen.uxml` |
| Match setup | `JoinGame` | `Assets/UI/Join/JoinGame.uxml` |
| Crew selection | `HomeScreen` | `Assets/UI/Home/CharacterSelection.uxml` |
| Match HUD | `Game` | `Assets/UI/Game/GameHUD.uxml` |

Navigation between them: Title to Match setup (Play), Match setup back to Title, Match setup to Crew
selection (once a network session is established), Crew selection to Match HUD (both crews
confirmed), Match HUD back to Crew selection (Play again) or Title (Main menu). A disconnect during
Crew selection returns to Match setup.

## Strings you cannot change

Everything else is open. These are not. If you believe one of them is wrong, say so rather than
editing it.

Asserted character for character by automated tests, so rewording fails the checks:

- "Lock in" and "Unlock", the two labels of the commit action
- "ACTIVE" and "ELIMINATED", the unit card state words
- "Move only"
- The enemy card health format, "75 / 120 HP"
- The enemy card ability format, "Area Lock · ready" and "Area Lock · ready in 2 rounds"
- The cooldown badge, a bare number with nothing around it
- Every result status listed in section 4, from "You win!" through "Match complete."
- All three dodge phase texts: "DODGE now — drag flashing units to safety", "Opponent is dodging your ability", "Waiting for dodge response"
- The crew error "That unit is unavailable for deployment. Choose another unit."
- The checkbox labels "Fog of war" and "Local multiplayer (dev)"

Game data rather than UI copy. Unit names, unit descriptions, and ability names are authored in
`Assets/UnitStats` and read by gameplay code:

- "Commander", "PogoRider", "Shotgunner", "Sniper", "Soldier" and their descriptions.
- "Smoke Screen", "Pogo", "Shield Rush", "Area Lock", "Grenade".

Supplied from outside the UI:

- "Low", "Medium", "High", the project's quality levels.
- "127.0.0.1:7777", a real address.
- The generated Relay join code.

Names:

- "Battle Plan", "Yuen Kai", "Logan Suite".

Product terms used across screens and in gameplay:

- "Elimination", "King of the Hill", "Capture the Flag".

---

## 1. Title screen

### What must be present

- The game wordmark.
- Five actions: Play, Controls, Settings, Credits, Quit. Play is the primary action of the screen.
  Controls and Settings are peers of equal weight. Credits and Quit are the lowest-weight pair.
- An icon accompanies every action except Quit.
- Three dialogs: Settings, How to play (opened by Controls), and Credits. One at a time. Each closes
  by its own close control and by Escape.
- A 3D model is rendered on this screen. It carries no text.

### Current text

Actions:

- "Play"
- "Controls"
- "Settings"
- "Credits"
- "Quit"

Settings dialog:

- Title: "Settings"
- Label: "Graphics quality"
- A dropdown whose options are the project quality levels: "Low", "Medium", "High". The dropdown
  itself carries no label text.
- Close control: "Close"

How to play dialog. Two labelled groups of term-and-explanation pairs. The body can exceed the
available height and must be readable in full:

- Title: "How to play"
- Group label: "Round flow"
  - "Planning" / "Both players choose orders at the same time."
  - "Choose an order" / "Select a unit. Click it again with no route drawn to switch to its ability; its card switches back to movement."
  - "Ability cooldown" / "Abilities replace movement and recharge after their listed cooldown."
  - "Lock in" / "Lock in early. You can unlock while waiting; otherwise orders submit with the timer."
  - "Dodge" / "Drag threatened units to safety."
  - "Fog" / "Enemies disappear outside your vision."
- Group label: "Win conditions"
  - "Elimination" / "Knock out the other crew."
  - "King of the Hill" / "Hold the hill for three rounds in a row."
- Close control: "Close"

Credits dialog. Two entries, each a name paired with a role:

- Title: "Credits"
- "Yuen Kai" / "Programming and game design"
- "Logan Suite" / "3D art, animation, music, and code"
- Close control: "Close"

---

## 2. Match setup screen

### What must be present

- A control returning to the title, carrying both an icon and a label.
- A screen heading.
- Two mutually exclusive views, Create and Join, each reachable by a tab control with an icon and a
  label. Create is active on entry. Exactly one view is available at a time.
- A picture or 3D element. It carries no text.

The **Create** view must contain:

- A heading for the view.
- A mode choice of three options. Each option carries an icon, a name, and a supporting line. Two
  are selectable and Elimination is the default. Capture the Flag is permanently disabled and its
  supporting line states availability rather than describing the mode.
- An opponent choice of two options. Each carries an icon, a name, and a supporting line. Player is
  the default.
- A dev-only checkbox with a supporting line. Hidden outside the main Unity editor, and disabled
  when shown.
- A fog-of-war checkbox with an icon and a supporting line. On by default.
- A status line, empty until there is something to report, with a normal and an error state.
- The primary action. Its label changes with the selected opponent and the dev checkbox.
- A connection readout, hidden until a host has been started, containing a heading, the code itself,
  its own status line, and a cancel control.

The **Join** view must contain:

- A heading for the view.
- A labelled single-line text input. It has no placeholder text. Typed characters are uppercased and
  whitespace is stripped as the user types.
- A status line, empty until there is something to report, with a normal and an error state.
- The primary action, which is replaced by a cancel control while a join is in progress.

Behaviour affecting content: Escape cancels an operation in progress, or otherwise returns to the
title. Enter submits from the Join view. All controls are disabled while a network operation runs.

### Current text

- Back control: "Title"
- Heading: "Set up a match"
- Tab: "Create"
- Tab: "Join"

Create view:

- Heading: "How should this match play?"
- Group label: "Mode"
  - "Elimination" / "Knock out the other crew."
  - "King of the Hill" / "Hold the middle."
  - "Capture the Flag" / "Coming later"
- Group label: "Opponent"
  - "Player" / "Online"
  - "AI" / "Local"
- Dev-only checkbox: "Local multiplayer (dev)", supporting line "Connect MPPM Player 2 at 127.0.0.1:7777."
- Fog checkbox: "Fog of war", supporting line "Hide enemies outside your vision."
- Primary action, one of:
  - "Create online match" (opponent Player)
  - "Start vs AI" (opponent AI)
  - "Start local match" (opponent Player with the dev checkbox on)
- Connection readout heading, one of:
  - "Relay code" (default, and for online hosting)
  - "Local endpoint" (dev local hosting)
- Connection readout value: the generated Relay join code, or `127.0.0.1:7777` for a local host.
- Cancel control: "Cancel"

Create view status lines. The first two belong to the view's own status line, the rest to the
connection readout's status line:

- "Match creation was cancelled."
- "Match creation failed. Check the connection and try again."
- "Host transport failed. Try creating the match again."
- "Starting local match…"
- "Creating Relay host…"
- "Starting local host…"
- "Waiting for second player."
- "Waiting for MPPM Player 2."
- "Second player connected."
- "Second player disconnected. Waiting for another player."

Join view:

- Heading: "Join a friend"
- Input label: "Relay code"
- Primary action: "Join match"
- Cancel control: "Cancel"

Join view status lines:

- "Enter a valid Relay code."
- "Preparing Relay connection…"
- "Connecting…"
- "Connected. Waiting for host."
- "Connection cancelled."
- "Connection was cancelled."
- "Join failed. Check the code and try again."
- "Connection closed before the match started."
- "Connection transport failed. Check the code and try again."
- "This match is full or has already started."

---

## 3. Crew selection screen

### What must be present

- A screen title and an instruction line.
- A match summary: a headline stating how many units to pick, plus three key-and-value pairs
  echoing the choices made during match setup.
- A 3D model. It carries no text.
- A labelled unit library listing every unit in the catalog. Each entry is selectable and shows a
  portrait, the unit's name, its description, and its ability name preceded by a marker. An entry
  can also show a single status word: picked once, picked more than once, or unavailable.
- A labelled board preview: an image and a caption.
- A labelled crew of five slots, numbered 1 to 5. An empty slot shows placeholder name and detail
  text. A filled slot shows the unit's name and that unit's ability name. Selecting a filled slot
  empties it.
- A status line for the crew.
- A confirm action carrying an icon and a label. It is disabled until all five slots hold a valid
  crew.

Behaviour affecting content: the same unit may be picked more than once. Every entry, slot, and
action carries a tooltip; the tooltips vary by state and are listed below.

### Current text

- Title: "Assemble your crew"
- Instruction line: "Pick 5. Repeats are allowed. Select a filled slot to remove it."

Match summary:

- Headline: "Pick 5 units"
- "Mode" / one of "Elimination", "King of the Hill"
- "Opponent" / one of "Player", "AI"
- "Visibility" / one of "Fog on", "Fog off"

Labels:

- "All units"
- "The board"
- "Your crew"

Board preview caption: "Industrial sector · 15 × 10"

Confirm action: "Confirm"

Unit library entry status word, shown only when it applies:

- "Selected"
- "Selected ×2" (the number is the pick count, any value above 1)
- "Unavailable"

Crew slot placeholders:

- Name: "Open slot"
- Detail: "Choose a unit"

Fallbacks if unit data is missing:

- Name: "Unknown unit"
- Description: "Unit data unavailable."
- Ability: "Move only"

Crew status line, normal state:

- "Connecting to match…"
- "0 / 5 selected" (the first number counts filled slots, 0 through 5)
- "Waiting for players (1 / 2)" (confirmed players, then expected players)
- "Deploying…"

Crew status line, error state:

- "No units are available."
- "That unit is no longer available. Choose another unit."
- "That unit is unavailable for deployment. Choose another unit."
- "Crew full. Remove a unit before choosing another."
- "Only the host selects a crew in an AI match."
- "Choose exactly 5 units."
- "That crew is not valid. Choose 5 units again."
- "The unit roster is unavailable. Try again."
- "At least 5 eligible units are required to start a match."
- "One selected unit is no longer available. Choose another unit."
- "The AI crew could not be created. Choose exactly 5 units." (the second sentence is whichever
  roster message applies, taken from the list above)
- "The host disconnected. Returning to match setup…"
- "The other player disconnected. Returning to match setup…"

Tooltips:

- Unit library entry, authored default: "Add this unit to your crew"
- Unit library entry, selectable: "Add Sniper to the crew"
- Unit library entry, already picked at least once: "Add another Sniper to the crew"
- Unit library entry, ineligible: "Sniper is unavailable for deployment"
- Unit library entry, locked: "Crew selection is locked"
- Unit library entry, no unit data: "Add unit"
- Crew slot, authored default: "Remove this unit from the crew"
- Crew slot, filled: "Remove Sniper from the crew"
- Crew slot, empty: "Open crew slot"

---

## 4. Match HUD

This sits over live gameplay. Everything except the interactive controls must let pointer input
through to the board.

### What must be present

- Five enemy contact cards, one per enemy unit.
- Five cards for the player's own units, one per unit.
- A hill control readout, present only in King of the Hill matches: a key label and a value.
- A phase readout: a mark, the current phase text, and a countdown number. The countdown is blank
  when no timer is running and has a distinct urgent state at five seconds or fewer.
- A feedback line, hidden unless there is something to say, with an error state and a success state.
- A commit control, hidden outside planning: a status word plus an action whose label changes with
  that word.
- A control that opens the match help.
- A deployment state shown on entry: a mark, a title, and a status line.
- A match help state: a title, two labelled groups of term-and-explanation pairs whose body can
  exceed the available height, and a close control. It also closes with Escape.
- A results state: a small label, a mark, the result status, and two actions.
- A screen flash tied to phase changes. It carries no text.

Behaviour affecting content: the deployment, help, and results states are mutually exclusive, and
while any of them is up the unit cards and commit control are unavailable.

### Current text

Hill control readout:

- Key: "Hill control"
- Value, one of:
  - "No control · 0/3"
  - "Blue control · 2/3" (the first number is the current streak, 0 through 3)
  - "Red control · 2/3"
  - "Contested · streak reset"

Phase text:

- Initial: "Preparing match"
- Then one of:
  - "Planning"
  - "Executing Moves"
  - "DODGE now — drag flashing units to safety"
  - "Opponent is dodging your ability"
  - "Waiting for dodge response"
  - "Host disconnected"

Countdown: a whole number of seconds, or blank.

Commit control. The status word, the action label, and the tooltip change together:

- "READY" / "Lock in" / "Lock current orders"
- "SENDING" / "Locking…" / "Sending orders"
- "WAITING" / "Unlock" / "Unlock to edit orders"
- "UNLOCKING" / "Unlocking…" / "Unlocking orders"
- "LOCKED" / "Locked" / "Orders are final"

Help control: "Controls"

Feedback line, error state:

- "Choose an ability target before locking in."
- "This unit can move only."
- "Grenade recharges in 2 rounds." (the ability name and the count vary; the unit is "round" at one
  and "rounds" otherwise)
- "Another unit ends its move there — your route will stop short."
- "Choose a highlighted target cell."
- "This ability activates on its caster."
- "This unit has no targetable ability."
- "Choose a target inside the battlefield."
- "Choose one of the eight adjacent direction cells."
- "Target is outside this ability's range."
- "That ability cannot target a wall cell."
- "Aim away from the square you are standing on."

Feedback line, success state:

- "Target locked."

Deployment state:

- Title: "Deploying crews"
- Status: "Preparing the battlefield and both crews."

Match help state. The group labels, terms, and explanations are word for word the same as the title
screen's How to play. Only the title and the close control differ:

- Title: "Match controls"
- Group label: "Round flow"
  - "Planning" / "Both players choose orders at the same time."
  - "Choose an order" / "Select a unit. Click it again with no route drawn to switch to its ability; its card switches back to movement."
  - "Ability cooldown" / "Abilities replace movement and recharge after their listed cooldown."
  - "Lock in" / "Lock in early. You can unlock while waiting; otherwise orders submit with the timer."
  - "Dodge" / "Drag threatened units to safety."
  - "Fog" / "Enemies disappear outside your vision."
- Group label: "Win conditions"
  - "Elimination" / "Knock out the other crew."
  - "King of the Hill" / "Hold the hill for three rounds in a row."
- Close control: "Return to match"

Results state:

- Label: "Battle report"
- Status, one of:
  - "You win!"
  - "You lose!"
  - "You win! Held the hill for 3 consecutive rounds."
  - "You lose! Held the hill for 3 consecutive rounds."
  - "You win! Opponent disconnected."
  - "You lose! Opponent disconnected."
  - "Draw — both crews eliminated."
  - "Draw."
  - "Match complete." (unresolved result, and the fallback when no status was supplied)
- Action: "Play again"
- Action: "Main menu"

---

## 5. Unit card content

One card serves two roles on the match HUD: the player's own units, and enemy contacts. Both show a
portrait, a name, an ability line, and a state word. Only the enemy version shows health. Only the
player version shows a cooldown badge and is selectable.

### Player version

- Name: the unit's name, or "Unit" when data is missing.
- Ability line: the unit's ability name, or "Move only" when the unit has no ability.
- State word: empty, or "ELIMINATED".
- Cooldown badge: the number of rounds remaining, present only while the ability is recharging.
- Tooltip, authored default: "Select this unit"
- Tooltip, no ability: "Select this unit. This unit can move only."
- Tooltip, recharging: "Grenade recharges after 1 more completed round." / "Grenade recharges after 3 more completed rounds."
- Tooltip, ability ready, unit not selected: "Grenade ready."
- Tooltip, ability ready, unit selected: "Grenade ready. Select again to use it instead of movement."
- Tooltip, ability chosen, unit selected: "Grenade selected. Select again to plan movement."
- Tooltip, ability chosen, unit not selected: "Grenade order set for this unit."

### Enemy version

- Name: the unit's name, or "Enemy" before contact.
- Health: "80 / 120 HP", or "HP UNKNOWN" when the maximum is unknown, or "-- / -- HP" before
  contact.
- Ability line, one of:
  - "Awaiting status" (before contact)
  - "Move only"
  - "Grenade · ready"
  - "Grenade · ready in 1 round"
  - "Grenade · ready in 3 rounds"
- State word: "LINKING", "ACTIVE", or "ELIMINATED".
- Tooltip, alive: "Live enemy health and ability status."
- Tooltip, eliminated: "Enemy unit eliminated."
- Authored fallbacks before any data arrives: name "Unit", health "HP UNKNOWN", ability "Move only".

---

## 6. Unit catalog content

Five units, in catalog order. Names, descriptions, and ability names are authored data.

| Name | Description | Ability |
| --- | --- | --- |
| Commander | He wishes to one day to become the player | Smoke Screen |
| PogoRider | Boing | Pogo |
| Shotgunner | Boom | Shield Rush |
| Sniper | Nothing escapes his view. High damage, high accuracy, great for area control | Area Lock |
| Soldier | Jack of all trades. | Grenade |

Descriptions appear in full only in the unit library on the crew selection screen. Ability names
appear in the unit library, in filled crew slots, on player unit cards, in enemy card ability lines,
and inside player card tooltips.

---

## 7. Text that recurs

These appear in more than one place and must read identically everywhere. Rewriting one means
rewriting every occurrence of it.

- Mode names: "Elimination", "King of the Hill", "Capture the Flag". Locked.
- "Move only", the ability line for a unit with no ability. Locked.
- "ELIMINATED", a unit state word. Locked.
- "Relay code", both a connection readout heading and the join input's label. Open.
- "Cancel" and "Close", dismissal labels. Open.
- The whole body of the help content, shared by the title screen's How to play and the match HUD's
  Match controls. Open, but the two stay in sync; only their titles and close controls differ.
