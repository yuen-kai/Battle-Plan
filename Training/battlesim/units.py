from dataclasses import dataclass, fields
from pathlib import Path
import re

REPO_ROOT = Path(__file__).resolve().parents[2]
UNIT_STATS_DIR = REPO_ROOT / "Assets" / "UnitStats"
ORIGINAL_FIVE = ("Commander", "PogoRider", "Ramrod", "Sniper", "Soldier")


@dataclass(frozen=True)
class UnitStats:
    name: str
    moveDist: int = 3
    moveSpeed: float = 1.0
    rotationSpeed: float = 360.0
    timeBetweenShots: float = 0.3
    magazineSize: int = 10
    reloadTime: float = 2.0
    bulletSpread: float = 3.0
    fireRateRampShots: int = 0
    fireRateRampStartDelay: float = 0.0
    fireRateRampResetDelay: float = 1.5
    canShootWhileMoving: bool = False
    bulletSpeed: float = 3.0
    targetRange: float = 5.0
    bulletRange: float = 7.0
    damage: int = 10
    backstabMultiplier: float = 1.0
    backstabAngle: float = 90.0
    targetLockDuration: float = 0.0
    visionRange: int = 5
    maxHealth: float = 100.0
    abilityName: str = ""
    diveSpeed: float = 4.0
    selectAbilitySquare: bool = True
    abilitySquareRange: int = 3
    selectAbilityDirection: bool = False
    abilityFixedDistance: int = 0
    abilityRadius: float = 0.0
    responseRange: float = 5.0
    responseDistLine: bool = False
    diveRange: int = 2
    abilityCooldownRounds: int = 1


_FIELD_TYPES = {f.name: f.type for f in fields(UnitStats) if f.name != "name"}
_LINE = re.compile(r"^  (\w+): (.*)$")


def _convert(kind, raw):
    if kind in (str, "str"):
        return raw.strip()
    if kind in (bool, "bool"):
        return raw.strip() == "1"
    if kind in (int, "int"):
        return int(float(raw))
    return float(raw)


def load_unit(name, stats_dir=UNIT_STATS_DIR):
    values = {}
    for line in (Path(stats_dir) / f"{name}.asset").read_text().splitlines():
        match = _LINE.match(line)
        if match and match.group(1) in _FIELD_TYPES:
            values[match.group(1)] = _convert(_FIELD_TYPES[match.group(1)], match.group(2))
    return UnitStats(name=name, **values)


def load_roster(names=ORIGINAL_FIVE, stats_dir=UNIT_STATS_DIR):
    return {name: load_unit(name, stats_dir) for name in names}
