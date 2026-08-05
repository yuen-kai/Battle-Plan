import sys, json
from pathlib import Path
sys.path.insert(0, 'Tools/abilityjuice')
import juice

for tag in ('r1', 'r2'):
    d = Path(f'Captures/AbilityJuice/shots/{tag}/hitreact')
    p = juice.pick(d, 'hitreact')
    print(tag, p)
