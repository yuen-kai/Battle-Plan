import numpy as np

from .maps import COLUMNS, ROWS
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
PLANE_COUNT = ENEMY_TYPE + N_TYPES
CARD_FEATURES = N_TYPES + 3
SCALAR_COUNT = N_TYPES + 9 + TEAM_SIZE * CARD_FEATURES


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
    scalars[N_TYPES:N_TYPES + 9] = (
        (len(path) - 1) / MAX_MOVE,
        unit.stats.moveDist / MAX_MOVE,
        unit.hp / unit.stats.maxHealth,
        unit.cooldown / MAX_COOLDOWN,
        match.round_number / MAX_ROUNDS,
        len(own) / TEAM_SIZE,
        sum(e.alive for e in enemies) / TEAM_SIZE,
        float(builder.dodge),
        (builder.max_dive if builder.dodge else 0) / 2.0,
    )
    offset = N_TYPES + 9
    for enemy in sorted(enemies, key=lambda e: e.slot)[:TEAM_SIZE]:
        scalars[offset + TYPE_INDEX[enemy.stats.name]] = 1.0
        scalars[offset + N_TYPES] = max(0.0, enemy.hp) / enemy.stats.maxHealth
        scalars[offset + N_TYPES + 1] = float(enemy.alive)
        scalars[offset + N_TYPES + 2] = enemy.cooldown / MAX_COOLDOWN
        offset += CARD_FEATURES
    return planes, scalars
