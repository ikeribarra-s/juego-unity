"""
gen.py — generate images (SDXL) or sound effects (Stable Audio Open) with the local ComfyUI
and copy them into the Unity project.

Stdlib only. ComfyUI must be running (C:\\Users\\redux\\ComfyUI\\start-comfyui.bat → http://127.0.0.1:8188).

USAGE
  python Tools/ComfyUI/gen.py <workflow> "<subject>" --out <dir> [--name N] [--count K] [--seed S]

  <workflow>  icon     → workflows/sdxl_icon_transparent.json (1024², transparent PNG)
              concept  → workflows/sdxl_concept.json          (1344×768, opaque)
              sfx      → workflows/stable_audio_sfx.json      (Stable Audio Open 1.0, stereo 44.1 kHz FLAC)
              or a path to any API-format workflow JSON containing the word SUBJECT in a prompt.
  --out       destination folder (e.g. Assets/Art/UI/Icons). Created if missing.
  --name      base filename (default: slug of the subject).
  --count     number of variations (different seeds). Default 1.
  --seed      first seed (default: random).
  --steps / --cfg   override sampler settings.
  --seconds   audio length for sfx workflows (EmptyLatentAudio). Default 3. Keep it short: 1–4 s for one-shots.

Audio outputs (flac/wav/mp3/opus) are copied with their own extension. Raw Stable Audio output is
stereo with silence/tails — run Tools/Audio/process_sfx.py on it before importing into Unity.
Models: python Tools/ComfyUI/download_stable_audio.py

Prints one line per saved file: the destination path.
"""
import argparse, json, os, random, re, shutil, sys, time, urllib.request, urllib.error

HERE = os.path.dirname(os.path.abspath(__file__))
SERVER = os.environ.get("COMFY_URL", "http://127.0.0.1:8188")
COMFY_OUTPUT = os.environ.get("COMFY_OUTPUT", r"C:\Users\redux\ComfyUI\output")
PRESETS = {
    "icon": os.path.join(HERE, "workflows", "sdxl_icon_transparent.json"),
    "concept": os.path.join(HERE, "workflows", "sdxl_concept.json"),
    "sfx": os.path.join(HERE, "workflows", "stable_audio_sfx.json"),
}


def call(path, payload=None):
    data = json.dumps(payload).encode() if payload is not None else None
    req = urllib.request.Request(SERVER + path, data=data, headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=30) as r:
        return json.load(r)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("workflow")
    ap.add_argument("subject")
    ap.add_argument("--out", required=True)
    ap.add_argument("--name")
    ap.add_argument("--count", type=int, default=1)
    ap.add_argument("--seed", type=int)
    ap.add_argument("--steps", type=int)
    ap.add_argument("--cfg", type=float)
    ap.add_argument("--seconds", type=float, default=3.0)
    a = ap.parse_args()

    try:
        call("/system_stats")
    except (urllib.error.URLError, OSError):
        sys.exit(f"ComfyUI is not running at {SERVER}. Start C:\\Users\\redux\\ComfyUI\\start-comfyui.bat")

    template = json.load(open(PRESETS.get(a.workflow, a.workflow), encoding="utf-8"))
    name = a.name or re.sub(r"[^a-z0-9]+", "_", a.subject.lower()).strip("_")[:40]
    os.makedirs(a.out, exist_ok=True)
    seed = a.seed if a.seed is not None else random.randint(0, 2**31 - 1)

    for i in range(a.count):
        wf = json.loads(json.dumps(template))
        for node in wf.values():
            ins = node.get("inputs", {})
            if isinstance(ins.get("text"), str):
                ins["text"] = ins["text"].replace("SUBJECT", a.subject)
            if node.get("class_type") == "KSampler":
                ins["seed"] = seed + i
                if a.steps: ins["steps"] = a.steps
                if a.cfg: ins["cfg"] = a.cfg
            if node.get("class_type") == "EmptyLatentAudio":
                ins["seconds"] = a.seconds
        pid = call("/prompt", {"prompt": wf})["prompt_id"]
        while True:
            hist = call(f"/history/{pid}")
            if pid in hist:
                break
            time.sleep(1.5)
        entry = hist[pid]
        if entry["status"]["status_str"] != "success":
            sys.exit(f"generation failed: {json.dumps(entry['status'].get('messages'))[-800:]}")
        suffix = f"_{i + 1:02d}" if a.count > 1 else ""
        for out in entry["outputs"].values():
            # images → .png; audio nodes report under "audio" (older builds: "audios")
            for item in out.get("images", []) + out.get("audio", []) + out.get("audios", []):
                if item.get("type") == "temp":
                    continue
                src = os.path.join(COMFY_OUTPUT, item.get("subfolder", ""), item["filename"])
                ext = os.path.splitext(item["filename"])[1] or ".png"
                dst = os.path.join(a.out, f"{name}{suffix}{ext}")
                shutil.copyfile(src, dst)
                print(f"{dst}  (seed {seed + i})")


if __name__ == "__main__":
    main()
