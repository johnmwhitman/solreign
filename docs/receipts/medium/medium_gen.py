#!/usr/bin/env python3
"""MEDIUM-bucket mob generation (MiniMax image-01). Refs viewed first per binding rule.
Outputs raw 1024x candidates to medium-gen/<asset>/<state>_v<n>_raw.png"""
import base64, json, os, subprocess, sys, time, urllib.request

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "medium-gen")
KEY = subprocess.check_output(["security", "find-generic-password", "-s", "minimax-api-key", "-w"]).decode().strip()

STYLE = ("32x32 pixel art game sprite, flat 2D orthographic view, muted retro palette, "
         "thin clean dark outline, no 3D rendering, no bevel, no cast shadow, no text, "
         "centered, solid {key} background")

# identity sentences repeated verbatim across every state of a family
POSSUM = "a scruffy grey opossum with a pale pointed white face, round dark eyes, pink nose and a thin bald pink tail"
RACCOON = "a grey raccoon with a black bandit eye-mask, pointed ears, and a bushy grey-and-black ring-striped tail"
FERRET = "a long slender low-bodied ferret with a cream-colored body, dark brown eye mask, and brown paws"
SCURRET = "a small plump white slug-like creature standing upright with a smooth soft rounded body, two tiny dark dot eyes and no limbs"
GUARDIAN = "a translucent ice-blue crystalline holographic sprite-fairy with spread angular wings"

JOBS = [
    # (asset, state, prompt-core, chroma key)
    ("possum_old", "front", f"{POSSUM}, standing on all fours facing directly toward the viewer", "green"),
    ("possum_old", "back",  f"{POSSUM}, standing on all fours facing directly away from the viewer, tail visible", "green"),
    ("possum_old", "side",  f"{POSSUM}, full side profile standing on all fours facing right", "green"),
    ("possum_old", "dead",  f"{POSSUM}, playing dead lying flat on its side with X-shaped closed eyes and tongue sticking out", "green"),
    ("raccoon", "front", f"{RACCOON}, standing on all fours facing directly toward the viewer", "magenta"),
    ("raccoon", "back",  f"{RACCOON}, standing on all fours facing directly away from the viewer, striped tail visible", "magenta"),
    ("raccoon", "side",  f"{RACCOON}, full side profile standing on all fours facing right", "magenta"),
    ("raccoon", "dead",  f"{RACCOON}, lying flat on its side with X-shaped closed eyes", "magenta"),
    ("ferret", "front", f"{FERRET}, standing on all fours facing directly toward the viewer", "magenta"),
    ("ferret", "back",  f"{FERRET}, standing on all fours facing directly away from the viewer", "magenta"),
    ("ferret", "side",  f"{FERRET}, full side profile standing on all fours facing right, long body stretched out", "magenta"),
    ("ferret", "dead",  f"{FERRET}, lying flat on its side with X-shaped closed eyes", "magenta"),
    ("scurret", "front", f"{SCURRET}, facing directly toward the viewer", "magenta"),
    ("scurret", "back",  f"{SCURRET}, seen from behind", "magenta"),
    ("scurret", "side",  f"{SCURRET}, full side profile facing right", "magenta"),
    ("scurret", "rip",   f"a pale translucent ghost of {SCURRET}, rising upward with a wavy wispy lower edge and closed arc eyes", "magenta"),
    ("scurret", "oof",   f"{SCURRET}, flattened and splatted face-down on the ground like a dropped pancake, dizzy swirl eyes", "magenta"),
    ("guardian_info", "hologram", f"{GUARDIAN}, hovering above a small flat grey dome pedestal base", "magenta"),
    ("guardian_info", "icon", "a jagged dark charcoal-grey spiky shadowy imp creature with a spiny silhouette, hunched pose", "magenta"),
]

def gen(prompt, out_path, tries=3):
    body = {"model": "image-01", "prompt": prompt, "aspect_ratio": "1:1",
            "response_format": "url", "n": 1, "prompt_optimizer": True}
    last = None
    for t in range(tries):
        for base in ("https://api.minimax.io", "https://api.minimaxi.chat"):
            try:
                req = urllib.request.Request(f"{base}/v1/image_generation",
                    data=json.dumps(body).encode(),
                    headers={"Authorization": f"Bearer {KEY}", "Content-Type": "application/json"})
                resp = json.load(urllib.request.urlopen(req, timeout=120))
                urls = (resp.get("data") or {}).get("image_urls") or []
                if urls:
                    urllib.request.urlretrieve(urls[0], out_path)
                    return True
                b64 = (resp.get("data") or {}).get("images") or []
                if b64:
                    open(out_path, "wb").write(base64.b64decode(b64[0]))
                    return True
                last = f"{base}: {str(resp)[:160]}"
            except Exception as e:
                last = f"{base}: {e}"
        time.sleep(3)
    print(f"FAIL {out_path}: {last}", flush=True)
    return False

ok = fail = 0
for asset, state, core, key in JOBS:
    os.makedirs(f"{OUT}/{asset}", exist_ok=True)
    for v in (1, 2):
        path = f"{OUT}/{asset}/{state}_v{v}_raw.png"
        if os.path.exists(path):
            ok += 1; continue
        prompt = f"{core}, {STYLE.format(key=key)}"
        t0 = time.time()
        if gen(prompt, path):
            ok += 1; print(f"OK {asset}/{state} v{v} ({time.time()-t0:.0f}s)", flush=True)
        else:
            fail += 1
print(f"SUMMARY {ok} ok, {fail} failed", flush=True)
