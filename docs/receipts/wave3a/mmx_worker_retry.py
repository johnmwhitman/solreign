#!/usr/bin/env python3
"""WAVE 3A worker (MiniMax image-01). Copied verbatim from wave2's medium/mmx_worker.py
(just OUT dir changed). Refs viewed first per binding rule.
Outputs raw 1024x candidates to wave3a-gen/<asset>/<state>_v<n>_raw.png"""
import base64, json, os, subprocess, sys, time, urllib.request

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "wave3a-gen")
KEY = subprocess.check_output(["security", "find-generic-password", "-s", "minimax-api-key", "-w"]).decode().strip()

STYLE = ("32x32 pixel art game sprite, flat 2D orthographic view, muted retro palette, "
         "thin clean dark outline, no 3D rendering, no bevel, no cast shadow, no text, "
         "centered, solid {key} background")

def gen(prompt, out_path, tries=5):
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
        time.sleep(12)
    print(f"FAIL {out_path}: {last}", flush=True)
    return False


jobs = json.load(open(sys.argv[1]))
ok = fail = 0
for j in jobs:
    os.makedirs(f"{OUT}/{j['asset']}", exist_ok=True)
    for v in j["variants"]:
        path = f"{OUT}/{j['asset']}/{j['state']}_v{v}_raw.png"
        if os.path.exists(path):
            ok += 1; continue
        t0 = time.time()
        if gen(j["prompt"] + ", " + STYLE.format(key=j["key"]), path):
            ok += 1; print(f"OK {j['asset']}/{j['state']} v{v} ({time.time()-t0:.0f}s)", flush=True)
        else:
            fail += 1
print(f"SUMMARY {ok} ok, {fail} failed", flush=True)
