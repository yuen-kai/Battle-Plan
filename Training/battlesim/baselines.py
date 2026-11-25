from .env import MOVE_ACTIONS, action_target, target_action
from .paths import STAY, step_cell
from .sim import AREA_LOCK, GRENADE, POGO, RAM_CHARGE, SMOKE


def manhattan(a, b):
    return abs(a[0] - b[0]) + abs(a[1] - b[1])


def advance_policy(rng, explore_bias=0.3, ability_chance=0.6):
    def choose(builder, memory):
        if builder.dodge:
            return dodge_step(builder)
        mask = builder.mask()
        unit = builder.unit
        head = builder.paths[unit.uid][-1]
        goals = [cell for cell, *_ in memory.last_known.values()]
        if not goals:
            goals = [(7, 9 if builder.team == 0 else 0)]
        if len(builder.paths[unit.uid]) == 1 and any(mask[MOVE_ACTIONS:]) and rng.random() < ability_chance:
            target = ability_target(unit, head, goals, [action_target(a) for a in range(MOVE_ACTIONS, len(mask)) if mask[a]], bool(memory.last_known), rng)
            if target is not None:
                return target_action(target)
        legal = [a for a in range(1, MOVE_ACTIONS) if mask[a]]
        if not legal:
            return STAY
        current = min(manhattan(head, g) for g in goals)
        if current <= max(1, int(unit.stats.targetRange) - 1) and mask[STAY]:
            return STAY
        best = min(legal, key=lambda a: (min(manhattan(step_cell(head, a), g) for g in goals), rng.random()))
        if rng.random() < explore_bias:
            best = rng.choice(legal)
        return best
    return choose


def ability_target(unit, head, goals, legal, enemies_known, rng):
    name = unit.stats.abilityName
    legal_set = set(legal)
    if name in (GRENADE, AREA_LOCK) and enemies_known:
        options = [g for g in goals if g in legal_set]
        return min(options, key=lambda g: manhattan(head, g)) if options else None
    if name == RAM_CHARGE and enemies_known:
        for cell in legal:
            direction = (cell[0] - head[0], cell[1] - head[1])
            line = {(head[0] + direction[0] * k, head[1] + direction[1] * k) for k in (1, 2, 3)}
            if line & set(goals):
                return cell
        return None
    if name == POGO and rng.random() < 0.5:
        return min(legal, key=lambda c: (abs(min(manhattan(c, g) for g in goals) - 4), rng.random()))
    if name == SMOKE and enemies_known and rng.random() < 0.3:
        nearest = min(goals, key=lambda g: manhattan(head, g))
        midpoint = ((head[0] + nearest[0]) // 2, (head[1] + nearest[1]) // 2)
        return min(legal, key=lambda c: manhattan(c, midpoint))
    return None


def dodge_step(builder):
    mask = builder.mask()
    head = builder.paths[builder.unit.uid][-1]
    threats = [target for team, _, target, _ in builder.match.pending["telegraphs"] if team != builder.team]
    if not threats:
        return STAY

    def safety(cell):
        return min(manhattan(cell, t) for t in threats)

    options = [a for a in range(1, MOVE_ACTIONS) if mask[a]]
    best = max(options, key=lambda a: safety(step_cell(head, a)), default=STAY)
    if best == STAY or safety(step_cell(head, best)) <= safety(head):
        return STAY
    return best
