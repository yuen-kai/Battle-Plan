from dataclasses import dataclass
import re

from .units import REPO_ROOT

COLUMNS = 15
ROWS = 10
CELL_SIZE = 2.7
MAP_CATALOG = REPO_ROOT / "Assets" / "Scripts" / "Map" / "MapCatalog.cs"

_DEFINITION = re.compile(
    r"public static readonly MapDefinition (\w+) = new\((.*?)\n    \);", re.S
)
_ROW = re.compile(r'"([.#]{%d})"' % COLUMNS)
_DEPLOY = re.compile(r"hostDeploymentColumns: new Vector2Int\((\d+), (\d+)\)")


@dataclass(frozen=True)
class MapLayout:
    name: str
    walls: frozenset
    host_deployment_columns: tuple = None


def parse_catalog(source=MAP_CATALOG):
    maps = {}
    for match in _DEFINITION.finditer(source.read_text()):
        rows = _ROW.findall(match.group(2))
        if len(rows) != ROWS:
            continue
        walls = frozenset(
            (x, ROWS - 1 - index)
            for index, row in enumerate(rows)
            for x, char in enumerate(row)
            if char == "#"
        )
        deploy = _DEPLOY.search(match.group(2))
        maps[match.group(1)] = MapLayout(
            match.group(1),
            walls,
            (int(deploy.group(1)), int(deploy.group(2))) if deploy else None,
        )
    return maps


def in_bounds(cell):
    return 0 <= cell[0] < COLUMNS and 0 <= cell[1] < ROWS
