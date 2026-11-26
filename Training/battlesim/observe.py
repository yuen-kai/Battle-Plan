import numpy as np

from .maps import COLUMNS, ROWS, in_bounds
from .paths import STEPS
from .sim import DEFAULT_HALF_HEIGHT, HALF_HEIGHT
from .units import ORIGINAL_FIVE

UNIT_TYPES = ORIGINAL_FIVE
TYPE_INDEX = {name: i for i, name in enumerate(UNIT_TYPES)}
N_TYPES = len(UNIT_TYPES)
TEAM_SIZE = 5
MAX_MOVE = 5
MAX_ROUNDS = 40
MAX_COOLDOWN = 4.0

(
    WALL, VISIBLE, SMOKE, OWN_HP, OWN_COOLDOWN, ENEMY_HP, MEMORY_AGE,
    ACTIVE_PATH, ACTIVE_HEAD, CLAIMED, ENEMY_THREAT, ENEMY_MARKER, OWN_MARKER, ALERTED,
) = range(14)
OWN_TYPE = 14
ENEMY_TYPE = OWN_TYPE + N_TYPES
LEGACY_PLANES = ENEMY_TYPE + N_TYPES
TEAM_PATH = LEGACY_PLANES
ALLY_ABILITY = TEAM_PATH + 1
ESCAPE = ALLY_ABILITY + N_TYPES
PLANE_COUNT = ESCAPE + 1
CARD_FEATURES = N_TYPES + 3
LEGACY_SCALARS = N_TYPES + 11 + TEAM_SIZE * CARD_FEATURES
REVISING = LEGACY_SCALARS
SCALAR_COUNT = LEGACY_SCALARS + 1
TRUE_ENEMY_HP = 0
TRUE_ENEMY_COOLDOWN = 1
TRUE_ENEMY_TYPE = 2
PRIVILEGED_COUNT = TRUE_ENEMY_TYPE + N_TYPES
MOVE_MIRROR = (0, 3, 4, 1, 2)


def encode_decision(builder, memory):
    match, team, unit = builder.match, builder.team, builder.unit
    path = builder.paths[unit.uid]
    planes = np.zeros((PLANE_COUNT, ROWS, COLUMNS), dtype=np.float32)
    for x, y in match.layout.walls:
        planes[WALL, y, x] = 1.0
    for x, y in match.visible_cells(team):
        planes[VISIBLE, y, x] = 1.0
    for x, y in match.smoke:
        planes[SMOKE, y, x] = 1.0
    for own in match.living(team):
        x, y = own.cell
        planes[OWN_HP, y, x] = own.hp / own.stats.maxHealth
        planes[OWN_COOLDOWN, y, x] = own.cooldown / MAX_COOLDOWN
        planes[OWN_TYPE + TYPE_INDEX[own.stats.name], y, x] = 1.0
    for cell, name, hp_fraction, round_seen in memory.last_known.values():
        x, y = cell
        planes[ENEMY_HP, y, x] = hp_fraction
        planes[ENEMY_TYPE + TYPE_INDEX[name], y, x] = 1.0
        planes[MEMORY_AGE, y, x] = min(1.0, (match.round_number - round_seen) / 5.0)
    for x, y in path:
        planes[ACTIVE_PATH, y, x] = 1.0
    x, y = path[-1]
    planes[ACTIVE_HEAD, y, x] = 1.0
    for x, y in builder.claimed:
        planes[CLAIMED, y, x] = 1.0
    for ally, path_so_far, target in builder.teammate_plans():
        if target is None:
            for x, y in path_so_far[1:]:
                planes[TEAM_PATH, y, x] = 1.0
            continue
        for x, y in match.telegraph_cells(ally, target):
            if in_bounds((x, y)):
                planes[ALLY_ABILITY + TYPE_INDEX[ally.stats.name], y, x] = 1.0
        for x, y in escape_cells(match, ally, target, memory):
            planes[ESCAPE, y, x] = 1.0
    if builder.dodge:
        for caster_team, _, target, cells in match.pending["telegraphs"]:
            own_side = caster_team == team
            for x, y in cells:
                planes[OWN_MARKER if own_side else ENEMY_THREAT, y, x] = 1.0
            if not own_side:
                planes[ENEMY_MARKER, target[1], target[0]] = 1.0
        for uid in match.pending["alerted"].get(team, ()):
            x, y = match.units[uid].cell
            planes[ALERTED, y, x] = 1.0

    scalars = np.zeros(SCALAR_COUNT, dtype=np.float32)
    scalars[TYPE_INDEX[unit.stats.name]] = 1.0
    own = match.living(team)
    enemies = [u for u in match.units if u.team != team]
    scalars[N_TYPES:N_TYPES + 11] = (
        (len(path) - 1) / MAX_MOVE,
        unit.stats.moveDist / MAX_MOVE,
        unit.hp / unit.stats.maxHealth,
        unit.cooldown / MAX_COOLDOWN,
        match.round_number / MAX_ROUNDS,
        len(own) / TEAM_SIZE,
        sum(e.alive for e in enemies) / TEAM_SIZE,
        float(builder.dodge),
        (builder.max_dive if builder.dodge else 0) / 2.0,
        float(match.config.fog_of_war),
        float(team),
    )
    offset = N_TYPES + 11
    for enemy in sorted(enemies, key=lambda e: e.slot)[:TEAM_SIZE]:
        scalars[offset + TYPE_INDEX[enemy.stats.name]] = 1.0
        scalars[offset + N_TYPES] = max(0.0, enemy.hp) / enemy.stats.maxHealth
        scalars[offset + N_TYPES + 1] = float(enemy.alive)
        scalars[offset + N_TYPES + 2] = enemy.cooldown / MAX_COOLDOWN
        offset += CARD_FEATURES
    scalars[REVISING] = float(builder.revising)
    return planes, scalars


def escape_cells(match, caster, target, memory):
    stats = caster.stats
    if stats.responseRange <= 0:
        return set()
    end = match.line_endpoint(caster, target) if stats.responseDistLine else None
    cells = set()
    for cell, name, _, _ in memory.last_known.values():
        if end is not None:
            alerted = match.segment_distance((caster.x, caster.y), end, cell) <= stats.responseRange
        else:
            half_height = HALF_HEIGHT.get(name, DEFAULT_HALF_HEIGHT)
            reach = 2.7 * stats.responseRange
            alerted = 2.7 ** 2 * ((cell[0] - target[0]) ** 2 + (cell[1] - target[1]) ** 2) + half_height ** 2 <= reach ** 2
        if alerted:
            cells |= dive_reach(cell, stats.diveRange, match.walls)
    return cells


def dive_reach(start, steps, walls):
    reached, frontier = {start}, [(start,)]
    for _ in range(steps):
        frontier = [
            path + (nxt,)
            for path in frontier
            for dx, dy in STEPS.values()
            for nxt in [(path[-1][0] + dx, path[-1][1] + dy)]
            if in_bounds(nxt) and nxt not in walls and nxt not in path
        ]
        reached.update(path[-1] for path in frontier)
    return reached


def encode_privileged(match, team):
    planes = np.zeros((PRIVILEGED_COUNT, ROWS, COLUMNS), dtype=np.float32)
    for enemy in match.living(1 - team):
        x, y = enemy.cell
        planes[TRUE_ENEMY_HP, y, x] = enemy.hp / enemy.stats.maxHealth
        planes[TRUE_ENEMY_COOLDOWN, y, x] = enemy.cooldown / MAX_COOLDOWN
        planes[TRUE_ENEMY_TYPE + TYPE_INDEX[enemy.stats.name], y, x] = 1.0
    return planes


def canonical_planes(planes, team):
    return np.ascontiguousarray(planes[:, ::-1, ::-1]) if team == 1 else planes


def canonical_mask(mask, team):
    if team != 1:
        return mask
    moves = mask[:len(MOVE_MIRROR)]
    return np.concatenate([moves[list(MOVE_MIRROR)], mask[len(MOVE_MIRROR):][::-1]])


def world_action(action, team):
    if team != 1:
        return action
    if action < len(MOVE_MIRROR):
        return MOVE_MIRROR[action]
    return len(MOVE_MIRROR) + ROWS * COLUMNS - 1 - (action - len(MOVE_MIRROR))
