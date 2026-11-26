import math
import random
from collections import deque
from dataclasses import dataclass
from functools import lru_cache

from . import geometry
from .maps import COLUMNS, ROWS, in_bounds
from .paths import is_legal_path
from .vision import PROJECTILE_RADIUS, body_radius, ray_circle, segment_crosses_cells, shared_board

SHOT_ZONE_WEIGHTS = (1.0, 1.0, 1.0)
BULLET_MAX_LIFETIME = 8.0
POST_DRAIN_PAUSE = 0.1
MAX_DISPLACEMENT_STEPS = 3
DISPLACEMENT_SLIDE_SECONDS = 0.3
LOCK_REVEAL_EXTRA = 0.5
RETURN_FIRE_SECONDS = 1.5
DIVE_RECOVERY_SECONDS = 2.0
MAX_ROUND_SECONDS = 120.0
RAY = 1e-7
FRAME = "frame"

HALF_HEIGHT = {"Sniper": 1.5605, "Ramrod": 1.6}
DEFAULT_HALF_HEIGHT = 1.7946

GRENADE, SMOKE, POGO, AREA_LOCK, RAM_CHARGE = "Grenade", "Smoke Screen", "Pogo", "Area Lock", "Ram Charge"
GRENADE_SECONDS, GRENADE_DAMAGE = 1.0, 80.0
SMOKE_SECONDS, SMOKE_ROUNDS, SMOKE_RADIUS = 0.35, 2, 1
POGO_SECONDS = 1.0
AREA_LOCK_DAMAGE, AREA_LOCK_WINDUP, AREA_LOCK_WINDOW = 130.0, 0.5, 3.0
AREA_LOCK_TURN, AREA_LOCK_REVEAL, AREA_LOCK_BEAM_SPEED = 720.0, 5.0, 40.0
AREA_LOCK_IMPACT_HOLD, AREA_LOCK_COMPOSURE = 0.08, 1.0
RAM_SPEED, RAM_CONTACT, RAM_STUN, RAM_BOOST = 4.5, 0.75, 0.6, 1.5
LINE_OVERSHOOT = 50.0 / 2.7


@dataclass
class Config:
    dt: float = 1 / 30
    game_frame: float = 1 / 60
    sample_hits: bool = False
    max_rounds: int = 40
    unity_frame_delays: bool = True
    fog_of_war: bool = True


ALL_CELLS = frozenset((x, y) for x in range(COLUMNS) for y in range(ROWS))


@lru_cache(maxsize=65536)
def smoke_between(start, end, smoke):
    return segment_crosses_cells(start, end, smoke)


def to_cell(x, y):
    return (round(x), round(y))


def rotate_towards(current, desired, max_step_deg):
    delta = geometry.wrap_angle(desired - current)
    step = math.radians(max_step_deg)
    if abs(delta) <= step:
        return desired
    return current + math.copysign(step, delta)


def turn_seconds(current, desired, speed_deg):
    return max(0.0, math.degrees(abs(geometry.wrap_angle(desired - current))) - 1.0) / speed_deg


def square_footprint(center, radius):
    return [(center[0] + dx, center[1] + dy) for dx in range(-radius, radius + 1) for dy in range(-radius, radius + 1)]


def directional_destination(start, direction, steps, walls):
    current = start
    for _ in range(steps):
        nxt = (current[0] + direction[0], current[1] + direction[1])
        if not in_bounds(nxt) or nxt in walls:
            break
        if direction[0] and direction[1]:
            if (current[0] + direction[0], current[1]) in walls and (current[0], current[1] + direction[1]) in walls:
                break
        current = nxt
    return current


class Motion:
    def __init__(self, kind, knots, on_done=None, cells=None, speed=None, pause=0.0):
        self.kind = kind
        self.knots = knots
        self.on_done = on_done
        self.cells = cells
        self.speed = speed
        self.pause = pause

    @property
    def end_time(self):
        return self.knots[-1][0]

    def position(self, t):
        knots = self.knots
        if t <= knots[0][0]:
            return knots[0][1], knots[0][2]
        for (ta, xa, ya), (tb, xb, yb) in zip(knots, knots[1:]):
            if t <= tb:
                f = (t - ta) / (tb - ta) if tb > ta else 1.0
                return xa + (xb - xa) * f, ya + (yb - ya) * f
        return knots[-1][1], knots[-1][2]

    def heading(self, t):
        for (ta, xa, ya), (tb, xb, yb) in zip(self.knots, self.knots[1:]):
            if ta <= t <= tb and (xb != xa or yb != ya):
                return math.atan2(yb - ya, xb - xa)
        return None


def walk_knots(t, position, cells, speed, pause):
    knots = [(t, position[0], position[1])]
    x, y = position
    for i, cell in enumerate(cells):
        if i > 0 or (abs(cell[0] - x) + abs(cell[1] - y)) >= 0.999:
            t += pause
            knots.append((t, x, y))
        t += math.hypot(cell[0] - x, cell[1] - y) / speed
        x, y = cell
        knots.append((t, x, y))
    return knots


class Unit:
    def __init__(self, uid, team, slot, stats, cell, facing):
        self.uid = uid
        self.team = team
        self.slot = slot
        self.stats = stats
        self.x, self.y = float(cell[0]), float(cell[1])
        self.facing = facing
        self.hp = float(stats.maxHealth)
        self.alive = True
        self.radius = body_radius(stats.name)
        self.half_height = HALF_HEIGHT.get(stats.name, DEFAULT_HALF_HEIGHT)
        self.planned_path = [cell]
        self.round_start_cell = cell
        self.motion = None
        self.moving = False
        self.collider = True
        self.cooldown = 0
        self.allow = True
        self.still = True
        self.gun_task = None
        self.recovery_task = None
        self.turn_target = None
        self.stunned_until = -1.0
        self.stun_task = None
        self.speed_mult = 1.0
        self.ammo = 0

    @property
    def cell(self):
        return to_cell(self.x, self.y)

    def position_at(self, t):
        return self.motion.position(t) if self.motion is not None else (self.x, self.y)


class Task:
    __slots__ = ("gen", "wake", "owner", "alive")

    def __init__(self, gen, owner):
        self.gen = gen
        self.wake = -math.inf
        self.owner = owner
        self.alive = True


@dataclass
class Shot:
    arrival: float
    shooter: "Unit"
    target: "Unit"
    damage: float
    origin: tuple


@dataclass
class Activation:
    unit: Unit
    target: tuple


class Match:
    def __init__(self, layout, rosters, spawns, config=None, seed=None):
        self.layout = layout
        self.walls = layout.walls
        self.board = shared_board(layout.walls)
        self.config = config or Config()
        self.rng = random.Random(seed)
        self.round_number = 0
        self.units = []
        for team, (roster, cells) in enumerate(zip(rosters, spawns)):
            facing = math.pi / 2 if team == 0 else -math.pi / 2
            for slot, (stats, cell) in enumerate(zip(roster, cells)):
                self.units.append(Unit(len(self.units), team, slot, stats, cell, facing))
        self.winner = None
        self.finished = False
        self.time = 0.0
        self.round_start = 0.0
        self.shots = []
        self.reveals = {}
        self._visible = {}
        self._positions_version = 0
        self.damage_dealt = [0.0, 0.0]
        self.shots_fired = {}
        self.smoke_deployments = []
        self.smoke = frozenset()
        self.tasks = []
        self.running_abilities = 0
        self.return_fire_until = -1.0
        self.being_shoved = {}
        self.last_seen = ({}, {})
        self.pending = None
        self.ability_affected = {}
        self.ability_damaged = set()
        self.round_dives = set()

    # ---------- queries ----------

    def living(self, team=None):
        return [u for u in self.units if u.alive and (team is None or u.team == team)]

    def stunned(self, unit):
        return unit.stunned_until > self.time + 1e-9

    def visible_cells(self, team):
        if not self.config.fog_of_war:
            return ALL_CELLS
        stamp = (self.time, self.smoke, self._positions_version)
        cached = self._visible.get(team)
        if cached is not None and cached[2] == stamp:
            return cached[1]
        key = (tuple(sorted((u.cell, u.stats.visionRange) for u in self.living(team))), self.smoke)
        if cached is not None and cached[0] == key:
            self._visible[team] = (key, cached[1], stamp)
            return cached[1]
        cells = set()
        for cell, vision in key[0]:
            cells |= self.board.viewer_cells(cell, vision, self.smoke)
        self._visible[team] = (key, cells, stamp)
        return cells

    def can_observe(self, team, enemy):
        if not self.config.fog_of_war:
            return True
        if self.reveals.get((enemy.uid, team), -1.0) >= self.time:
            return True
        return enemy.cell in self.visible_cells(team)

    def reveal(self, unit, team, seconds):
        key = (unit.uid, team)
        self.reveals[key] = max(self.reveals.get(key, -1.0), self.time + seconds)

    def face(self, unit, target):
        unit.turn_target = None
        unit.facing = math.atan2(target.y - unit.y, target.x - unit.x)

    def line_of_sight(self, unit, enemy):
        if not enemy.alive or not enemy.collider or not self.can_observe(unit.team, enemy):
            return False
        if self.smoke and smoke_between((unit.x, unit.y), (enemy.x, enemy.y), self.smoke):
            return False
        sx, sy = unit.x, unit.y
        dx, dy = enemy.x - sx, enemy.y - sy
        dist = math.hypot(dx, dy)
        if dist <= 1e-9:
            return False
        ux, uy = dx / dist, dy / dist
        reach = unit.stats.targetRange
        best_t, best = self.first_body_hit(sx, sy, ux, uy, enemy.team, PROJECTILE_RADIUS)
        if best is not enemy or best_t > reach:
            return False
        wall_t = self.board.first_wall_contact((sx, sy), (sx + ux * reach, sy + uy * reach), PROJECTILE_RADIUS)
        return wall_t is None or wall_t > best_t

    def first_body_hit(self, sx, sy, ux, uy, team, pad, exclude=None):
        best_t, best = math.inf, None
        for other in self.living(team):
            if other is exclude or not other.collider:
                continue
            radius = pad + other.radius
            if math.hypot(other.x - sx, other.y - sy) <= radius:
                continue
            t = ray_circle(sx, sy, ux, uy, other.x, other.y, radius)
            if t is not None and t < best_t:
                best_t, best = t, other
        return best_t, best

    def find_nearest_enemy(self, unit):
        candidates = sorted(self.living(1 - unit.team), key=lambda e: (e.x - unit.x) ** 2 + (e.y - unit.y) ** 2)
        for enemy in candidates:
            if self.line_of_sight(unit, enemy):
                return enemy
        return None

    # ---------- coroutines ----------

    def spawn(self, gen, owner=None):
        task = Task(gen, owner)
        self.tasks.append(task)
        self.step_task(task)
        return task

    def cancel(self, task):
        if task is not None and task.alive:
            task.alive = False
            try:
                task.gen.close()
            except ValueError:
                pass

    def step_task(self, task):
        dt = self.config.dt
        if task.owner is not None and not task.owner.alive:
            self.cancel(task)
            return
        while task.alive and self.time >= task.wake - 1e-9:
            try:
                wait = next(task.gen)
            except StopIteration:
                task.alive = False
                return
            task.wake = self.time + (dt if wait is FRAME or wait <= 0 else wait)

    # ---------- shooting ----------

    def pause_shooting(self, unit):
        self.cancel(unit.gun_task)
        unit.gun_task = None
        unit.turn_target = None
        unit.allow = True
        unit.still = True

    def stand_down(self, unit):
        self.pause_shooting(unit)
        unit.allow = False
        unit.still = False

    def start_shooting(self, unit):
        if self.stunned(unit):
            self.stand_down(unit)
            return
        self.pause_shooting(unit)
        unit.gun_task = self.spawn(self.initiate_shooting(unit), unit)

    def continue_shooting(self, unit):
        if self.stunned(unit):
            self.stand_down(unit)
            return
        unit.allow = True
        if not unit.still:
            unit.still = True
            unit.gun_task = self.spawn(self.initiate_shooting(unit), unit)

    def stop_motion(self, unit):
        unit.motion = None
        self.cancel(unit.recovery_task)
        unit.recovery_task = None

    def transition_to_shooting(self, unit, only_if_free=False):
        self.stop_motion(unit)
        unit.moving = False
        if (only_if_free and not unit.allow) or self.stunned(unit):
            self.stand_down(unit)
        else:
            self.start_shooting(unit)

    def initiate_shooting(self, unit):
        stats = unit.stats
        unit.ammo = stats.magazineSize
        while unit.allow:
            target = self.find_nearest_enemy(unit)
            if target is not None:
                yield from self.rotate_to(unit, target)
                self.lock_reveal(unit, target)
            lock_remaining = stats.targetLockDuration
            while unit.ammo > 0:
                if target is None or not self.line_of_sight(unit, target):
                    target = self.find_nearest_enemy(unit)
                    if target is not None:
                        lock_remaining = stats.targetLockDuration
                        yield from self.rotate_to(unit, target)
                        self.lock_reveal(unit, target)
                    if not unit.allow:
                        break
                    yield FRAME
                    continue
                if lock_remaining > 0:
                    self.face(unit, target)
                    lock_remaining -= self.config.dt
                    yield FRAME
                    continue
                self.face(unit, target)
                self.fire(unit, target)
                unit.ammo -= 1
                yield stats.timeBetweenShots
            if unit.allow:
                yield stats.reloadTime
                unit.ammo = stats.magazineSize
        while any(s.shooter is unit for s in self.shots):
            yield FRAME
        yield POST_DRAIN_PAUSE
        unit.still = False

    def rotate_to(self, unit, target):
        desired = math.atan2(target.y - unit.y, target.x - unit.x)
        duration = turn_seconds(unit.facing, desired, unit.stats.rotationSpeed)
        unit.turn_target = desired
        if duration > 0:
            yield duration
        if self.config.unity_frame_delays:
            yield FRAME
        unit.facing = desired
        unit.turn_target = None

    def lock_reveal(self, unit, target):
        if unit.stats.targetLockDuration > 0:
            self.reveal(unit, target.team, unit.stats.targetLockDuration + LOCK_REVEAL_EXTRA)

    def fire(self, unit, target):
        self.shots_fired[unit.uid] = self.shots_fired.get(unit.uid, 0) + 1
        stats = unit.stats
        origin = (unit.x, unit.y)
        aim = math.atan2(target.y - unit.y, target.x - unit.x)
        max_flight = min(stats.bulletRange / stats.bulletSpeed, BULLET_MAX_LIFETIME)
        spread = math.radians(stats.bulletSpread)
        obstacles = []
        flights = {}
        for enemy in self.living(1 - unit.team):
            if not enemy.collider:
                continue
            reach = enemy.radius + PROJECTILE_RADIUS
            flight = self.contact_flight(origin, enemy, reach, stats.bulletSpeed, max_flight)
            if flight is None:
                continue
            px, py = enemy.position_at(self.time + flight)
            dist = math.hypot(px - origin[0], py - origin[1])
            half_width = math.pi / 2 if dist <= reach else math.asin(reach / dist)
            offset = geometry.wrap_angle(math.atan2(py - origin[1], px - origin[0]) - aim)
            if offset + half_width < -spread or offset - half_width > spread:
                continue
            flights[enemy.uid] = flight
            obstacles.append((max(0.0, dist - reach), offset - half_width, offset + half_width, enemy))
        if not obstacles:
            return
        for near, centre, lo, hi in self.board.wall_obstacles(origin, stats.bulletRange, PROJECTILE_RADIUS):
            offset = geometry.wrap_angle(centre - aim)
            if offset + hi >= -spread and offset + lo <= spread:
                obstacles.append((near, offset + lo, offset + hi, None))
        shares = geometry.first_hit_shares(obstacles, spread)
        if self.config.sample_hits:
            roll, picked = self.rng.random(), {}
            for enemy, share in sorted(shares.items(), key=lambda item: item[0].uid):
                roll -= share
                if roll < 0:
                    picked = {enemy: 1.0}
                    break
            shares = picked
        for enemy, share in shares.items():
            self.shots.append(Shot(self.time + flights[enemy.uid], unit, enemy, stats.damage * share, origin))

    def contact_flight(self, origin, enemy, reach, speed, max_flight):
        if enemy.motion is None:
            dist = math.hypot(enemy.x - origin[0], enemy.y - origin[1])
            flight = max(0.0, dist - reach) / speed
            return flight if flight <= max_flight else None
        return geometry.intercept_time(origin, enemy.position_at, self.time, speed, max_flight, reach)

    def resolve_shots(self):
        now = self.time + 1e-9
        if not any(s.arrival <= now for s in self.shots):
            return
        landed = [s for s in self.shots if s.arrival <= now]
        self.shots = [s for s in self.shots if s.arrival > now]
        for shot in landed:
            target = shot.target
            if not target.alive or not target.collider:
                continue
            damage = shot.damage
            stats = shot.shooter.stats
            if stats.backstabMultiplier > 1 and self.is_backstab(shot):
                damage *= stats.backstabMultiplier
            self.apply_damage(target, damage, shot.shooter.team)

    def is_backstab(self, shot):
        target = shot.target
        to_shooter = math.atan2(shot.origin[1] - target.y, shot.origin[0] - target.x)
        angle = abs(geometry.wrap_angle(to_shooter - target.facing))
        return math.degrees(angle) >= shot.shooter.stats.backstabAngle

    def apply_damage(self, target, damage, source_team):
        if not target.alive:
            return
        target.hp -= damage
        self.damage_dealt[source_team] += damage
        if target.hp <= 1e-6:
            target.alive = False
            target.moving = False
            target.motion = None
            target.collider = False
            self.being_shoved.pop(target, None)
            self._positions_version += 1

    # ---------- stun and shoves ----------

    def note_ability_hit(self, victim, caster, damaged):
        self.ability_affected.setdefault(victim.uid, set()).add(caster.uid)
        if damaged:
            self.ability_damaged.add(victim.uid)

    def combos(self, team):
        enemies = {u.uid for u in self.units if u.team != team}
        layered = sum(len(casters) >= 2 for uid, casters in self.ability_affected.items() if uid in enemies)
        trapped = len(self.ability_damaged & self.round_dives & enemies)
        return layered + trapped

    def apply_stun(self, unit, seconds):
        if seconds <= 0 or not unit.alive:
            return
        self.cancel(unit.stun_task)
        unit.stunned_until = self.time + seconds
        self.stop_motion(unit)
        unit.moving = False
        unit.x, unit.y = map(float, unit.cell)
        self._positions_version += 1
        self.stand_down(unit)
        unit.stun_task = self.spawn(self.stun_expiry(unit, seconds), unit)

    def stun_expiry(self, unit, seconds):
        yield seconds
        unit.stunned_until = -1.0
        self.transition_to_shooting(unit, only_if_free=True)

    def slide(self, unit, destination, on_done=None):
        knots = [(self.time, unit.x, unit.y), (self.time + DISPLACEMENT_SLIDE_SECONDS, float(destination[0]), float(destination[1]))]
        self.being_shoved[unit] = destination

        def finish():
            self.being_shoved.pop(unit, None)
            if on_done:
                on_done()

        unit.motion = Motion("slide", knots, finish)

    def displacement_cell(self, origin, anchor, taken):
        frontier = deque([(origin, 0)])
        seen = {origin}
        options = []
        while frontier:
            cell, steps = frontier.popleft()
            if cell != origin and cell not in taken:
                options.append(cell)
            if steps == MAX_DISPLACEMENT_STEPS:
                continue
            for dx, dy in ((0, 1), (1, 0), (0, -1), (-1, 0)):
                nxt = (cell[0] + dx, cell[1] + dy)
                if nxt in seen or not in_bounds(nxt) or nxt in self.walls:
                    continue
                seen.add(nxt)
                frontier.append((nxt, steps + 1))
        if not options:
            return None
        return min(
            options,
            key=lambda c: (
                abs(c[0] - origin[0]) + abs(c[1] - origin[1]),
                abs(c[0] - anchor[0]) + abs(c[1] - anchor[1]),
                c[1],
                c[0],
            ),
        )

    # ---------- planning ----------

    def ability_target_ok(self, unit, target):
        stats = unit.stats
        start = unit.cell
        if not stats.abilityName or unit.cooldown > 0:
            return False
        if not stats.selectAbilitySquare:
            return target is None or target == start
        if target is None or not in_bounds(target):
            return False
        if stats.abilityName == SMOKE and not all(in_bounds(c) for c in square_footprint(target, SMOKE_RADIUS)):
            return False
        if stats.selectAbilityDirection:
            delta = (target[0] - start[0], target[1] - start[1])
            return stats.abilityFixedDistance > 0 and delta != (0, 0) and max(abs(delta[0]), abs(delta[1])) <= 1
        if abs(target[0] - start[0]) + abs(target[1] - start[1]) > stats.abilitySquareRange + 0.1:
            return False
        if not stats.responseDistLine and target in self.walls:
            return False
        return not stats.responseDistLine or target != start

    def legal_ability_targets(self, unit):
        if not unit.stats.abilityName or unit.cooldown > 0:
            return []
        return [(x, y) for x in range(COLUMNS) for y in range(ROWS) if self.ability_target_ok(unit, (x, y))]

    def sanitize_path(self, unit, raw, max_steps):
        start = unit.cell
        path = [start]
        for cell in raw or []:
            cell = tuple(cell)
            if len(path) - 1 >= max_steps:
                break
            last = path[-1]
            if abs(cell[0] - last[0]) + abs(cell[1] - last[1]) != 1:
                continue
            if cell in path or cell in self.walls or not in_bounds(cell):
                continue
            path.append(cell)
        return path

    def caster_end_cell(self, unit, target):
        name = unit.stats.abilityName
        if name == POGO:
            return target
        if name == RAM_CHARGE:
            start = unit.cell
            direction = (max(-1, min(1, target[0] - start[0])), max(-1, min(1, target[1] - start[1])))
            return directional_destination(start, direction, unit.stats.abilityFixedDistance, self.walls)
        return unit.cell

    def separate_friendly_end_cells(self, paths, abilities=None):
        abilities = abilities or {}
        for team in (0, 1):
            claimed = set()
            movers = []
            for unit in self.living(team):
                if unit.uid in abilities:
                    claimed.add(self.caster_end_cell(unit, abilities[unit.uid]))
                    continue
                path = paths[unit.uid]
                if len(path) < 2:
                    claimed.add(path[-1])
                else:
                    movers.append(unit)
            movers.sort(key=lambda u: (len(paths[u.uid]), u.slot))
            for unit in movers:
                path = paths[unit.uid]
                end = len(path) - 1
                while end > 0 and path[end] in claimed:
                    end -= 1
                paths[unit.uid] = path[: end + 1]
                claimed.add(path[end])

    def begin_round(self, plans):
        self.round_number += 1
        paths, abilities = {}, {}
        for unit in self.living():
            plan = plans.get(unit.uid)
            if isinstance(plan, tuple) and plan and plan[0] == "ability":
                target = tuple(plan[1]) if plan[1] is not None else None
                if self.ability_target_ok(unit, target):
                    abilities[unit.uid] = target if unit.stats.selectAbilitySquare else unit.cell
                paths[unit.uid] = [unit.cell]
                continue
            raw = [tuple(c) for c in (plan or [])]
            if raw and raw[0] == unit.cell:
                raw = raw[1:]
            paths[unit.uid] = self.sanitize_path(unit, raw, unit.stats.moveDist)
        self.pending = {"paths": paths, "abilities": abilities, "dives": set()}
        alerted, max_dive, telegraphs = self.compute_alerts(abilities)
        self.pending.update(alerted=alerted, max_dive=max_dive, telegraphs=telegraphs)
        return alerted

    def compute_alerts(self, abilities):
        alerted = {0: set(), 1: set()}
        max_dive = 0
        telegraphs = []
        for uid, target in sorted(abilities.items()):
            unit = self.units[uid]
            stats = unit.stats
            telegraphs.append((unit.team, stats.abilityName, target, self.telegraph_cells(unit, target)))
            if stats.responseRange <= 0:
                continue
            enemy_team = 1 - unit.team
            if stats.responseDistLine:
                end = self.line_endpoint(unit, target)
                hits = [
                    e for e in self.living(enemy_team)
                    if self.segment_distance((unit.x, unit.y), end, (e.x, e.y)) <= stats.responseRange
                ]
            else:
                limit = (2.7 * stats.responseRange) ** 2
                hits = [
                    e for e in self.living(enemy_team)
                    if 2.7 ** 2 * ((e.x - target[0]) ** 2 + (e.y - target[1]) ** 2) + e.half_height ** 2 <= limit
                ]
            if hits:
                max_dive = max(max_dive, stats.diveRange)
                alerted[enemy_team].update(e.uid for e in hits)
        return {t: s for t, s in alerted.items() if s}, max_dive, telegraphs

    def telegraph_cells(self, unit, target):
        name = unit.stats.abilityName
        if name == GRENADE:
            reach = unit.stats.abilityRadius
            return [
                (x, y) for x in range(COLUMNS) for y in range(ROWS)
                if (x - target[0]) ** 2 + (y - target[1]) ** 2 <= reach ** 2
            ]
        if name == AREA_LOCK:
            end = self.line_endpoint(unit, target)
            return [
                (x, y) for x in range(COLUMNS) for y in range(ROWS)
                if self.segment_distance((unit.x, unit.y), end, (x, y)) <= 0.5
            ]
        if name == SMOKE:
            return square_footprint(target, SMOKE_RADIUS)
        return [self.caster_end_cell(unit, target)]

    def line_endpoint(self, unit, target):
        sx, sy = unit.x, unit.y
        dx, dy = target[0] - sx, target[1] - sy
        dist = math.hypot(dx, dy)
        if dist <= 1e-9:
            return (sx, sy)
        ux, uy = dx / dist, dy / dist
        far = dist + LINE_OVERSHOOT
        wall_t = self.board.first_wall_contact((sx, sy), (sx + ux * far, sy + uy * far), RAY)
        length = wall_t if wall_t is not None else far
        return (sx + ux * length, sy + uy * length)

    @staticmethod
    def segment_distance(a, b, p):
        dx, dy = b[0] - a[0], b[1] - a[1]
        length_sq = dx * dx + dy * dy
        if length_sq <= 1e-12:
            return math.inf
        t = ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / length_sq
        if t < 0:
            return math.inf
        t = min(1.0, t)
        return math.hypot(p[0] - (a[0] + t * dx), p[1] - (a[1] + t * dy))

    def apply_dodges(self, dives):
        pending = self.pending
        for uid, raw in (dives or {}).items():
            unit = self.units[uid]
            if not unit.alive or uid not in pending["alerted"].get(unit.team, set()):
                continue
            raw = [tuple(c) for c in raw]
            if raw and raw[0] == unit.cell:
                raw = raw[1:]
            path = self.sanitize_path(unit, raw, pending["max_dive"])
            if len(path) < 2:
                continue
            pending["paths"][uid] = path
            pending["abilities"].pop(uid, None)
            pending["dives"].add(uid)

    def play_round(self, plans, dodge=None):
        alerted = self.begin_round(plans)
        if alerted and dodge is not None:
            self.apply_dodges(dodge(self, alerted))
        return self.execute()

    # ---------- execution ----------

    def execute(self):
        pending = self.pending
        self.pending = None
        paths, abilities, dives = pending["paths"], pending["abilities"], pending["dives"]
        self.ability_affected, self.ability_damaged, self.round_dives = {}, set(), set(dives)
        self.round_start = self.time
        self.return_fire_until = -1.0
        self.tasks = []
        self.running_abilities = 0
        started = set()
        activations = []
        for uid, target in sorted(abilities.items(), key=lambda item: (self.units[item[0]].team, self.units[item[0]].slot)):
            unit = self.units[uid]
            if unit.cooldown > 0:
                continue
            unit.cooldown = max(1, unit.stats.abilityCooldownRounds)
            started.add(uid)
            activations.append(Activation(unit, target))
        self.separate_friendly_end_cells(paths, {a.unit.uid: a.target for a in activations})

        self.held = {}
        for unit in self.living():
            unit.planned_path = paths[unit.uid]
            unit.round_start_cell = unit.cell
            unit.still = True
            unit.speed_mult = 1.0
            unit.ammo = 0
            unit.gun_task = None
            unit.recovery_task = None
            unit.stun_task = None
            self.held[unit] = unit.cell
        stationary = []
        for unit in self.living():
            path = paths[unit.uid]
            if len(path) > 1:
                self.start_walk(unit, path[1:], dive=unit.uid in dives)
            elif self.config.unity_frame_delays:
                stationary.append(unit)
            else:
                self.transition_to_shooting(unit)
        if stationary:
            self.spawn(self.settle_stationary(stationary))
        for activation in activations:
            self.running_abilities += 1
            self.spawn(self.run_ability(activation), activation.unit)
        self.resolving = True
        self.round_done = False
        self.spawn(self.round_loop())

        dt = self.config.dt
        while self.time - self.round_start < MAX_ROUND_SECONDS:
            self.time += dt
            self.update_motions(dt)
            if self.resolving:
                self.resolve_overlaps()
            for task in list(self.tasks):
                if task.alive:
                    self.step_task(task)
            self.tasks = [t for t in self.tasks if t.alive]
            self.resolve_shots()
            self.record_sightings()
            if self.round_done:
                break

        for unit in self.units:
            if unit.uid not in started and unit.cooldown > 0:
                unit.cooldown -= 1
        for unit in self.living():
            unit.speed_mult = 1.0
            unit.motion = None
            unit.moving = False
            unit.collider = True
            unit.stunned_until = -1.0
        self.being_shoved.clear()
        self.reveals = {k: v for k, v in self.reveals.items() if v >= self.time}
        self.advance_smoke()
        self.check_end()
        return self.time - self.round_start

    def settle_stationary(self, units):
        yield FRAME
        for unit in units:
            if unit.alive and unit.motion is None and not unit.moving and (unit.gun_task is None or not unit.gun_task.alive):
                self.transition_to_shooting(unit)

    def round_loop(self):
        def holding_fire():
            return any(u.moving for u in self.living()) or self.time < self.return_fire_until

        while (
            any(u.still for u in self.living())
            or self.running_abilities > 0
            or self.resolving
            or self.time < self.return_fire_until
        ):
            if holding_fire():
                for unit in self.living():
                    self.continue_shooting(unit)
                while holding_fire():
                    yield FRAME
                yield FRAME
            for unit in self.living():
                unit.allow = False
            yield FRAME
        while self.shots:
            yield FRAME
        self.round_done = True

    def start_walk(self, unit, cells, dive=False):
        speed = unit.stats.diveSpeed if dive else unit.stats.moveSpeed * unit.speed_mult
        knots = walk_knots(self.time, (unit.x, unit.y), [tuple(map(float, c)) for c in cells], speed, self.config.game_frame)
        unit.moving = True
        unit.motion = Motion("dive" if dive else "walk", knots, lambda: self.finish_walk(unit, dive), cells, speed, self.config.game_frame)

    def finish_walk(self, unit, dive):
        unit.moving = False
        if dive:
            unit.recovery_task = self.spawn(self.dive_recovery(unit), unit)
        else:
            self.transition_to_shooting(unit)

    def dive_recovery(self, unit):
        yield DIVE_RECOVERY_SECONDS
        unit.recovery_task = None
        self.transition_to_shooting(unit, only_if_free=True)

    def boost_walk(self, unit, multiplier):
        unit.speed_mult = multiplier
        motion = unit.motion
        if motion is None or motion.kind != "walk":
            return
        index = 0
        for i, (t, x, y) in enumerate(motion.knots):
            if t > self.time:
                index = i
                break
        targets = []
        for t, x, y in motion.knots[index:]:
            if not targets or targets[-1] != (x, y):
                targets.append((x, y))
        speed = unit.stats.moveSpeed * multiplier
        knots = walk_knots(self.time, (unit.x, unit.y), targets, speed, self.config.game_frame)
        unit.motion = Motion("walk", knots, motion.on_done, targets, speed, self.config.game_frame)

    def update_motions(self, dt):
        finished = []
        for unit in self.living():
            motion = unit.motion
            if motion is None:
                if unit.turn_target is not None:
                    unit.facing = rotate_towards(unit.facing, unit.turn_target, unit.stats.rotationSpeed * dt)
                continue
            px, py = motion.position(self.time)
            if motion.kind in ("walk", "dive"):
                heading = motion.heading(self.time)
                if heading is not None:
                    unit.facing = rotate_towards(unit.facing, heading, unit.stats.rotationSpeed * dt)
            unit.x, unit.y = px, py
            if self.time >= motion.end_time - 1e-9:
                finished.append(unit)
        for unit in finished:
            motion = unit.motion
            if motion is None:
                continue
            unit.x, unit.y = motion.knots[-1][1], motion.knots[-1][2]
            unit.motion = None
            if motion.on_done:
                motion.on_done()

    def resolve_overlaps(self):
        living = self.living()
        in_transit = [u for u in living if u.moving or u in self.being_shoved]
        claimed = {u.cell for u in living} | set(self.being_shoved.values())
        for unit in in_transit:
            if unit.motion is not None and unit.motion.kind in ("walk", "dive"):
                claimed.add(tuple(map(int, unit.motion.cells[-1])))
        stationary = [u for u in living if u not in in_transit]
        groups = {}
        for unit in stationary:
            if self.held.get(unit) != unit.cell:
                self.held.pop(unit, None)
            groups.setdefault(unit.cell, []).append(unit)
        displaced = set()
        for cell in sorted(groups, key=lambda c: (c[1], c[0])):
            group = groups[cell]
            if len(group) < 2:
                continue
            for unit in sorted(group, key=lambda u: (u.team, u.slot)):
                if self.held.get(unit) == cell:
                    continue
                destination = self.displacement_cell(cell, unit.round_start_cell, claimed)
                if destination is None:
                    continue
                claimed.add(destination)
                displaced.add(unit)
                self.slide(unit, destination)
        for unit in stationary:
            if unit not in displaced:
                self.held[unit] = unit.cell
        if not displaced and not any(u.moving for u in living) and self.running_abilities == 0 and not self.being_shoved:
            self.resolving = False

    def record_sightings(self):
        for team in (0, 1):
            seen = self.last_seen[team]
            for enemy in self.living(1 - team):
                if self.can_observe(team, enemy):
                    seen[enemy.uid] = (enemy.cell, self.round_number, self.time)

    # ---------- abilities ----------

    def run_ability(self, activation):
        try:
            yield FRAME
            name = activation.unit.stats.abilityName
            if name == GRENADE:
                yield from self.grenade(activation.unit, activation.target)
            elif name == SMOKE:
                yield from self.smoke_screen(activation.unit, activation.target)
            elif name == POGO:
                yield from self.pogo(activation.unit, activation.target)
            elif name == AREA_LOCK:
                yield from self.area_lock(activation.unit, activation.target)
            elif name == RAM_CHARGE:
                yield from self.ram_charge(activation.unit, activation.target)
        finally:
            self.running_abilities -= 1

    def grenade(self, soldier, target):
        yield GRENADE_SECONDS
        yield FRAME
        ox, oy = float(target[0]), float(target[1])
        for enemy in self.living(1 - soldier.team):
            if not enemy.collider:
                continue
            dist = math.hypot(enemy.x - ox, enemy.y - oy)
            if dist > soldier.stats.abilityRadius + enemy.radius:
                continue
            if dist > enemy.radius and self.splash_blocked((ox, oy), enemy, dist):
                continue
            self.apply_damage(enemy, GRENADE_DAMAGE, soldier.team)
            self.note_ability_hit(enemy, soldier, damaged=True)

    def splash_blocked(self, origin, enemy, dist):
        ux, uy = (enemy.x - origin[0]) / dist, (enemy.y - origin[1]) / dist
        own_contact = dist - enemy.radius
        wall_t = self.board.first_wall_contact(origin, (enemy.x, enemy.y), RAY)
        if wall_t is not None and wall_t < own_contact:
            return True
        body_t, body = self.first_body_hit(origin[0], origin[1], ux, uy, enemy.team, 0.0, exclude=enemy)
        return body is not None and body_t < own_contact

    def smoke_screen(self, commander, target):
        yield SMOKE_SECONDS
        yield FRAME
        cells = square_footprint(target, SMOKE_RADIUS)
        if all(in_bounds(c) for c in cells):
            self.register_smoke(cells, SMOKE_ROUNDS)

    def pogo(self, rider, target):
        self.stop_motion(rider)
        self.pause_shooting(rider)
        rider.moving = True
        rider.collider = False
        knots = [(self.time, rider.x, rider.y), (self.time + POGO_SECONDS + self.config.dt, float(target[0]), float(target[1]))]
        rider.motion = Motion("pogo", knots)
        while rider.motion is not None:
            yield FRAME
        rider.x, rider.y = float(target[0]), float(target[1])
        rider.collider = True
        self.return_fire_until = max(self.return_fire_until, self.time + RETURN_FIRE_SECONDS)
        self.transition_to_shooting(rider, only_if_free=True)

    def area_lock(self, sniper, target):
        for team in (0, 1):
            if team != sniper.team:
                self.reveal(sniper, team, AREA_LOCK_REVEAL)
        self.stop_motion(sniper)
        sniper.moving = False
        self.pause_shooting(sniper)
        yield AREA_LOCK_WINDUP
        sniper.x, sniper.y = map(float, sniper.cell)
        ox, oy = sniper.x, sniper.y
        dx, dy = target[0] - ox, target[1] - oy
        length = math.hypot(dx, dy)
        if length <= 1e-9:
            self.spawn(self.area_lock_cleanup(sniper), sniper)
            return
        ux, uy = dx / length, dy / length
        desired = math.atan2(uy, ux)
        turn = turn_seconds(sniper.facing, desired, AREA_LOCK_TURN)
        if turn > 0:
            yield turn
        sniper.facing = desired
        elapsed = 0.0
        far = 40.0
        while elapsed < AREA_LOCK_WINDOW:
            body_t, body = self.first_body_hit(ox, oy, ux, uy, 1 - sniper.team, 0.0)
            if body is not None:
                wall_t = self.board.first_wall_contact((ox, oy), (ox + ux * far, oy + uy * far), RAY)
                if (wall_t is None or wall_t > body_t) and not (
                    self.smoke and smoke_between((ox, oy), (body.x, body.y), self.smoke)
                ):
                    self.spawn(self.area_lock_beam(sniper, body), sniper)
                    return
            elapsed += self.config.dt
            yield FRAME
        self.spawn(self.area_lock_cleanup(sniper), sniper)

    def area_lock_beam(self, sniper, victim):
        yield math.hypot(victim.x - sniper.x, victim.y - sniper.y) / AREA_LOCK_BEAM_SPEED + AREA_LOCK_IMPACT_HOLD
        self.apply_damage(victim, AREA_LOCK_DAMAGE, sniper.team)
        self.note_ability_hit(victim, sniper, damaged=True)
        yield from self.area_lock_cleanup(sniper)

    def area_lock_cleanup(self, sniper):
        sniper.still = False
        yield AREA_LOCK_COMPOSURE
        if sniper.allow:
            self.start_shooting(sniper)

    def ram_charge(self, ramrod, target):
        start = ramrod.cell
        delta = (target[0] - start[0], target[1] - start[1])
        if delta == (0, 0) or max(abs(delta[0]), abs(delta[1])) > 1:
            return
        direction = (max(-1, min(1, delta[0])), max(-1, min(1, delta[1])))
        end = directional_destination(start, direction, ramrod.stats.abilityFixedDistance, self.walls)
        self.stop_motion(ramrod)
        self.pause_shooting(ramrod)
        ramrod.moving = True
        ramrod.x, ramrod.y = map(float, start)
        steps = max(abs(end[0] - start[0]), abs(end[1] - start[1]))
        swept = {(start[0] + direction[0] * k, start[1] + direction[1] * k) for k in range(1, steps + 1)}
        victims = [e for e in self.living(1 - ramrod.team) if e.cell in swept]
        footprint = set(square_footprint(start, 1))
        for ally in self.living(ramrod.team):
            if ally is not ramrod and ally.cell in footprint:
                self.boost_walk(ally, RAM_BOOST)
        desired = math.atan2(direction[1], direction[0])
        turn = turn_seconds(ramrod.facing, desired, ramrod.stats.rotationSpeed)
        if turn > 0:
            yield turn
        ramrod.facing = desired
        duration = math.hypot(end[0] - start[0], end[1] - start[1]) / RAM_SPEED
        if duration > 0:
            ramrod.motion = Motion("dash", [(self.time, ramrod.x, ramrod.y), (self.time + duration, float(end[0]), float(end[1]))])
        while ramrod.motion is not None:
            self.ram_contacts(ramrod, victims, direction)
            yield FRAME
        ramrod.x, ramrod.y = float(end[0]), float(end[1])
        self.ram_contacts(ramrod, victims, direction)
        ramrod.moving = False
        self.transition_to_shooting(ramrod, only_if_free=True)

    def ram_contacts(self, ramrod, victims, direction):
        for victim in list(victims):
            if not victim.alive:
                victims.remove(victim)
                continue
            if math.hypot(victim.x - ramrod.x, victim.y - ramrod.y) <= RAM_CONTACT:
                victims.remove(victim)
                self.note_ability_hit(victim, ramrod, damaged=False)
                self.spawn(self.knockback(victim, direction), ramrod)

    def knockback(self, victim, direction):
        self.apply_stun(victim, RAM_STUN + DISPLACEMENT_SLIDE_SECONDS)
        cell = victim.cell
        occupied = {u.cell for u in self.living() if u is not victim}

        def free(c):
            return in_bounds(c) and c not in self.walls and c not in occupied

        left = (cell[0] - direction[1], cell[1] + direction[0])
        right = (cell[0] + direction[1], cell[1] - direction[0])
        destination = left if free(left) else right if free(right) else self.displacement_cell(cell, cell, occupied)
        if destination is not None:
            self.slide(victim, destination)
            while victim in self.being_shoved and victim.alive:
                yield FRAME
        self.apply_stun(victim, RAM_STUN)

    # ---------- round boundaries ----------

    def register_smoke(self, cells, rounds):
        self.smoke_deployments.append([frozenset(cells), rounds])
        self.smoke = frozenset().union(*(c for c, _ in self.smoke_deployments))

    def advance_smoke(self):
        for deployment in self.smoke_deployments:
            deployment[1] -= 1
        self.smoke_deployments = [d for d in self.smoke_deployments if d[1] > 0]
        self.smoke = frozenset().union(*(c for c, _ in self.smoke_deployments)) if self.smoke_deployments else frozenset()

    def check_end(self):
        alive = [bool(self.living(team)) for team in (0, 1)]
        if all(alive) and self.round_number < self.config.max_rounds:
            return
        self.finished = True
        self.winner = None if alive[0] == alive[1] else (0 if alive[0] else 1)
