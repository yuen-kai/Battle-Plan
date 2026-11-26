import random

from .maps import COLUMNS, ROWS, parse_catalog
from .paths import STAY, legal_steps, step_cell
from .sim import Match
from .spawns import spawn_cells
from .units import ORIGINAL_FIVE, load_roster

TEAM_SIZE = 5
MOVE_ACTIONS = 5
ACTION_COUNT = MOVE_ACTIONS + COLUMNS * ROWS


def target_action(cell):
    return MOVE_ACTIONS + cell[1] * COLUMNS + cell[0]


def action_target(action):
    index = action - MOVE_ACTIONS
    return (index % COLUMNS, index // COLUMNS)


class TeamMemory:
    def __init__(self):
        self.last_known = {}

    def update(self, match, team):
        sightings = match.last_seen[team]
        for enemy in match.living(1 - team):
            seen = sightings.get(enemy.uid)
            if seen is None:
                continue
            cell, round_seen, _ = seen
            known = self.last_known.get(enemy.uid)
            if known is None or round_seen >= known[3]:
                self.last_known[enemy.uid] = (cell, enemy.stats.name, enemy.hp / enemy.stats.maxHealth, round_seen)
        visible = match.visible_cells(team)
        occupied = {e.cell for e in match.living(1 - team) if match.can_observe(team, e)}
        dead = {u.uid for u in match.units if not u.alive}
        for uid, (cell, *_rest) in list(self.last_known.items()):
            if uid in dead or (cell in visible and cell not in occupied):
                del self.last_known[uid]


class PathBuilder:
    dodge = False

    def __init__(self, match, team, revise=False, rng=None):
        self.match = match
        self.team = team
        self.units = sorted(match.living(team), key=lambda u: u.uid)
        if rng is not None:
            rng.shuffle(self.units)
        self.paths = {u.uid: [u.cell] for u in self.units}
        self.abilities = {}
        self.index = 0
        self.revise = revise
        self.revising = False
        self.claimed = set()
        self._targets = {}

    @property
    def done(self):
        return self.index >= len(self.units)

    @property
    def unit(self):
        return self.units[self.index]

    def teammate_plans(self):
        visible = self.units if self.revising else self.units[: self.index]
        current = self.unit
        return [(u, self.paths[u.uid], self.abilities.get(u.uid)) for u in visible if u is not current]

    def end_cell(self, unit):
        target = self.abilities.get(unit.uid)
        return self.match.caster_end_cell(unit, target) if target is not None else self.paths[unit.uid][-1]

    def next_unit(self):
        self.index += 1
        if self.index >= len(self.units) and self.revise and not self.revising:
            self.revising = True
            self.index = 0
        if self.revising and not self.done:
            unit = self.unit
            self.paths[unit.uid] = [unit.cell]
            self.abilities.pop(unit.uid, None)
            self.claimed = {self.end_cell(other) for other in self.units if other is not unit}

    def ability_targets(self, unit):
        if unit.uid not in self._targets:
            self._targets[unit.uid] = self.match.legal_ability_targets(unit)
        return self._targets[unit.uid]

    def mask(self):
        unit = self.unit
        path = self.paths[unit.uid]
        mask = legal_steps(path, unit.stats.moveDist, self.match.walls) + [False] * (ACTION_COUNT - MOVE_ACTIONS)
        if path[-1] in self.claimed and any(mask[1:MOVE_ACTIONS]):
            mask[STAY] = False
        if len(path) == 1:
            for cell in self.ability_targets(unit):
                mask[target_action(cell)] = True
        return mask

    def act(self, action):
        unit = self.unit
        path = self.paths[unit.uid]
        if not self.mask()[action]:
            action = STAY
        if action >= MOVE_ACTIONS:
            target = action_target(action)
            self.abilities[unit.uid] = target
            self.claimed.add(self.match.caster_end_cell(unit, target))
            self.next_unit()
            return
        if action != STAY:
            path.append(step_cell(path[-1], action))
            if len(path) - 1 < unit.stats.moveDist:
                return
        self.claimed.add(path[-1])
        self.next_unit()

    def plans(self):
        plans = dict(self.paths)
        for uid, target in self.abilities.items():
            plans[uid] = ("ability", target)
        return plans


class DodgeBuilder:
    dodge = True
    revising = False

    def teammate_plans(self):
        return []

    def __init__(self, match, team, alerted):
        self.match = match
        self.team = team
        self.units = sorted((match.units[uid] for uid in alerted), key=lambda u: u.uid)
        self.paths = {u.uid: [u.cell] for u in self.units}
        self.max_dive = match.pending["max_dive"]
        self.claimed = set()
        self.index = 0

    @property
    def done(self):
        return self.index >= len(self.units)

    @property
    def unit(self):
        return self.units[self.index]

    def mask(self):
        path = self.paths[self.unit.uid]
        return legal_steps(path, self.max_dive, self.match.walls) + [False] * (ACTION_COUNT - MOVE_ACTIONS)

    def act(self, action):
        path = self.paths[self.unit.uid]
        if 0 < action < MOVE_ACTIONS and self.mask()[action]:
            path.append(step_cell(path[-1], action))
            if len(path) - 1 < self.max_dive:
                return
        self.index += 1

    def dives(self):
        return {uid: path for uid, path in self.paths.items() if len(path) > 1}


def random_policy(rng):
    def choose(builder, memory):
        legal = [a for a, ok in enumerate(builder.mask()) if ok]
        return rng.choice(legal)
    return choose


class Game:
    def __init__(self, map_name=None, rosters=None, config=None, seed=None):
        self.rng = random.Random(seed)
        self.maps = parse_catalog()
        self.layout = self.maps[map_name or self.rng.choice(sorted(self.maps))]
        roster_stats = load_roster()
        self.rosters = rosters or [
            [self.rng.choice(ORIGINAL_FIVE) for _ in range(TEAM_SIZE)] for _ in range(2)
        ]
        spawns = spawn_cells(self.layout, TEAM_SIZE)
        self.match = Match(
            self.layout,
            [[roster_stats[name] for name in roster] for roster in self.rosters],
            spawns,
            config,
            seed=self.rng.randrange(1 << 30),
        )
        self.memory = [TeamMemory(), TeamMemory()]
        self.match.record_sightings()
        for team in (0, 1):
            self.memory[team].update(self.match, team)

    def run_builder(self, builder, policy):
        while not builder.done:
            builder.act(policy(builder, self.memory[builder.team]))
        return builder

    def plan(self, team, policy):
        revise = getattr(policy, "revise", False)
        builder = PathBuilder(self.match, team, revise=revise, rng=self.rng if revise else None)
        return self.run_builder(builder, policy).plans()

    def dodge(self, team, uids, policy):
        return self.run_builder(DodgeBuilder(self.match, team, uids), policy).dives()

    def play_round(self, policies):
        plans = {}
        for team in (0, 1):
            plans.update(self.plan(team, policies[team]))
        alerted = self.match.begin_round(plans)
        dives = {}
        for team, uids in alerted.items():
            dives.update(self.dodge(team, uids, policies[team]))
        self.match.apply_dodges(dives)
        return self.finish_round()

    def resolve(self, plans, dives=None):
        self.match.begin_round(plans)
        self.match.apply_dodges(dives or {})
        return self.finish_round()

    def finish_round(self):
        duration = self.match.execute()
        for team in (0, 1):
            self.memory[team].update(self.match, team)
        return duration

    def play(self, policies):
        while not self.match.finished:
            self.play_round(policies)
        return self.match.winner
