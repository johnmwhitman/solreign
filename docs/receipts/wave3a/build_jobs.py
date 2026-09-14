#!/usr/bin/env python3
"""Build the WAVE 3A generation job list (26 AI-generation targets, 3 variants each),
split into 4 files for parallel mmx_worker.py processes. Reference-first: every prompt
below was written after viewing refs-tierAB.png / refs-extras.png (the actual NC PNGs),
never from the asset name."""
import json, os

SCR = os.path.dirname(os.path.abspath(__file__))

JOBS = [
    ("powersink", "powersink",
     "a tall boxy sci-fi mechanical device console, dark navy-black screen panel on top, "
     "flanked by two thin red antenna-like spikes, grey metal chassis body with small red "
     "accent details and control panel texture at the base, standing upright", "green"),
    ("handdrill", "handdrill",
     "a hand-held sci-fi power drill tool pointing right, grey and silver metal body, a "
     "violet-purple cylindrical grip section in the middle, black chuck, metal drill bit "
     "tip protruding forward", "magenta"),
    ("handdrilldiamond", "handdrill",
     "a hand-held sci-fi power drill tool pointing right, grey and silver metal body, a "
     "violet-purple cylindrical grip section in the middle, black chuck, glowing bright "
     "cyan diamond-tipped drill bit protruding forward", "magenta"),
    ("football", "icon",
     "a brown oval American football with white lace stitching on top, viewed from above "
     "at a slight angle", "green"),
    ("basketball", "icon",
     "an orange basketball sphere with black seam lines in the classic basketball pattern",
     "green"),
    ("beach_ball", "icon",
     "a white spherical beach ball with red, yellow, and blue pastel colored panel stripes",
     "magenta"),
    ("nanotrasen", "icon",
     "a shield-shaped navy blue balloon with a bold white letter N logo centered on it, "
     "thin grey curly string hanging below", "green"),
    ("corgi", "icon",
     "a round pink balloon printed with a cute corgi dog face, pointed ears and snout, "
     "thin grey curly string hanging below", "green"),
    ("syndicate", "icon",
     "a shield-shaped red balloon with a bold white numeral 5 logo centered on it, thin "
     "grey curly string hanging below", "green"),
    ("rubber_chicken", "icon",
     "a comical yellow rubber chicken toy lying on its side, small red comb on its head, "
     "orange beak, simple cartoon prop", "magenta"),
    ("pondering_orb", "icon",
     "a small round glowing pale cyan-blue orb with a soft light aura, floating, simple "
     "magical sphere", "magenta"),
    ("clownrecorder", "icon",
     "a rectangular red and white striped tape recorder device with small grey buttons "
     "and a slot at the top center, boxy retro cassette player", "cyan"),
    ("lamp", "icon",
     "a small desk lamp with a green conical lampshade, tan beige spring-arm body, round "
     "gold base", "magenta"),
    ("capgun", "icon",
     "a small grey and black toy cap-gun revolver with a brown wooden grip and a tiny "
     "light blue accent near the muzzle tip", "magenta"),
    ("foam_crossbow", "icon",
     "a compact grey toy crossbow with a black stock, a light blue foam-tipped dart bolt "
     "loaded across the top", "magenta"),
    ("foam_crossbow", "foambox",
     "a green rectangular ammo box container with a row of orange foam darts sticking up "
     "out of the top, toy foam dart box", "magenta"),
    ("foam_grenade", "icon",
     "a toy grenade shaped like an orange pineapple with a green pin and handle lever on "
     "top, foam toy prop", "magenta"),
    ("foam_blade", "icon",
     "a curved foam toy sword blade shaped like a scimitar, red top edge and white blade "
     "body, black handle grip", "cyan"),
    ("energy_crossbow", "icon",
     "a compact grey sci-fi crossbow with a black body and a glowing green energy "
     "bowstring", "magenta"),
    ("machete", "icon",
     "a diagonal steel machete blade with dark camouflage-spot patterning along the "
     "blade, dark green wrapped handle grip", "green"),
    ("singularityhammer", "icon",
     "a stylized fantasy warhammer held diagonally, dark obsidian-black hammer head "
     "rimmed in glowing orange-gold, dark maroon-purple handle", "cyan"),
    ("chainsaw", "icon",
     "a compact chainsaw tool viewed diagonally, grey serrated blade bar, red motor "
     "housing and handle", "green"),
    ("cult_halberd", "icon",
     "a polearm halberd held diagonally, tan bone-colored haft wrapped with red cloth at "
     "the grip, dark reddish curved axe blade at the top", "cyan"),
    ("mjollnir", "icon",
     "Thor's hammer held diagonally, silver-grey stone hammer head, brown wood handle "
     "with a light grey pommel, small yellow lightning spark accents around the head",
     "magenta"),
    ("cutlass", "icon",
     "a curved cutlass sword held diagonally, silver-white blade, ornate gold hilt and "
     "guard with a dark brown grip wrap", "cyan"),
    ("incomplete_bat", "icon",
     "a plain wooden baseball bat held diagonally, brown wood texture with darker wood "
     "grain segments, no nails, unfinished prop", "green"),
]

jobs = [{"asset": a, "state": s, "prompt": p, "key": k, "variants": [1, 2, 3]}
        for a, s, p, k in JOBS]

# split into 4 roughly-even shards for parallel workers
N = 4
shards = [[] for _ in range(N)]
for i, j in enumerate(jobs):
    shards[i % N].append(j)

for i, shard in enumerate(shards):
    fn = f"{SCR}/jobs_{chr(97+i)}.json"
    json.dump(shard, open(fn, "w"), indent=1)
    print(fn, len(shard), "jobs")

print("total jobs:", len(jobs), "total generations:", sum(len(j["variants"]) for j in jobs))
