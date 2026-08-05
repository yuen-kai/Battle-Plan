"""Round-4 death builder: the final authored palette, graded."""
from _b4_hue import report

print("--- the void, authored cyan-cold so even the dark material carries hue ---")
report("hole floor", (0.0060, 0.0092, 0.0115))
report("inner wall (far)", (0.0115, 0.0180, 0.0225))
report("deck lip", (0.0290, 0.0420, 0.0500))
report("lip x1.35 grain", (0.0392, 0.0567, 0.0675))
report("lip x0.65 grain", (0.0189, 0.0273, 0.0325))

print()
print("--- the light in it ---")
report("wash", (0.020, 0.86, 2.15))
report("wash 0.6", (0.012, 0.516, 1.29))
report("wash 0.3", (0.006, 0.258, 0.645))
report("wash 0.12", (0.0024, 0.103, 0.258))
report("core+wash", (0.050, 2.71, 7.75))
report("glint 0.5 total", (2.85, 4.10, 6.15))
report("glint 1.0 total", (5.65, 8.05, 12.20))

print()
print("--- where does white actually start ---")
for k in (2.0, 3.0, 4.0, 4.5, 5.0, 5.5, 6.0):
    report(f"neutral {k}", (k, k * 1.02, k * 1.05))

print()
print("--- shards of deck, and the corpse silhouetted on the light ---")
report("shard lit", (0.0620, 0.0700, 0.0800))
report("shard shadow", (0.0075, 0.0105, 0.0130))
report("shard underlight", (0.0090, 0.2600, 0.6400))
report("corpse early", (0.1400, 0.1500, 0.1650))
report("corpse late", (0.0180, 0.0215, 0.0260))

print()
print("--- the board it all sits on ---")
report("deck (measured 185)", (0.44, 0.50, 0.54))
