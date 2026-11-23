"""
download_stable_audio.py — fetch Stable Audio Open 1.0 + its T5-base text encoder into the local ComfyUI.

Stdlib only. Run with any Python:
  python Tools/ComfyUI/download_stable_audio.py            # default: official ungated sources
  python Tools/ComfyUI/download_stable_audio.py --source stabilityai   # gated original repo (needs HF_TOKEN)

Sources (all official — never use third-party mirrors to dodge the licence gate):
  checkpoint  Comfy-Org/stable-audio-open-1.0 (ComfyUI's own repackage, ungated)
              → models/checkpoints/stable-audio-open-1.0.safetensors (~4.5 GB)
              Fallback --source stabilityai: stabilityai/stable-audio-open-1.0 model.safetensors
              (gated: accept the licence at https://huggingface.co/stabilityai/stable-audio-open-1.0,
              create a READ token at https://huggingface.co/settings/tokens, then
              `set HF_TOKEN=hf_xxx` (cmd) / `$env:HF_TOKEN="hf_xxx"` (PowerShell) in the same shell,
              or log in once with `hf auth login` — the cached token is picked up automatically.)
  text enc.   google-t5/t5-base model.safetensors (Apache-2.0, ungated)
              → models/text_encoders/t5-base.safetensors (~0.9 GB)

LICENCE: Stable Audio Open 1.0 is under the Stability AI Community License — free for
research/non-commercial and for commercial use under USD 1M annual revenue; above that an
enterprise licence is required. Outputs may be used in the game under those terms.

The token is read from HF_TOKEN or the huggingface cache only, and is never printed or written anywhere.
"""
import argparse, os, sys, urllib.request

COMFY = os.environ.get("COMFY_ROOT", r"C:\Users\redux\ComfyUI")
FILES = {
    "comfy-org": ("https://huggingface.co/Comfy-Org/stable-audio-open-1.0/resolve/main/stable-audio-open-1.0.safetensors", False),
    "stabilityai": ("https://huggingface.co/stabilityai/stable-audio-open-1.0/resolve/main/model.safetensors", True),
}
T5 = "https://huggingface.co/google-t5/t5-base/resolve/main/model.safetensors"


def fetch(url, dst, token=None):
    if os.path.exists(dst) and os.path.getsize(dst) > 1_000_000:
        print(f"already present: {dst}")
        return
    os.makedirs(os.path.dirname(dst), exist_ok=True)
    req = urllib.request.Request(url, headers={"Authorization": f"Bearer {token}"} if token else {})
    tmp = dst + ".part"
    try:
        with urllib.request.urlopen(req, timeout=60) as r, open(tmp, "wb") as f:
            total = int(r.headers.get("Content-Length", 0))
            done = 0
            while chunk := r.read(1 << 20):
                f.write(chunk)
                done += len(chunk)
                if total:
                    print(f"\r{os.path.basename(dst)}: {done / total:6.1%} of {total / 1e9:.2f} GB", end="", flush=True)
        print()
    except urllib.error.HTTPError as e:
        if os.path.exists(tmp):
            os.remove(tmp)
        if e.code in (401, 403):
            sys.exit(f"HTTP {e.code}: accept the licence on the model page and set HF_TOKEN to a READ token.")
        raise
    os.replace(tmp, dst)
    print(f"saved {dst}")


def _cached_token():
    """Token saved by `hf auth login` (~/.cache/huggingface/token). Read only, never printed."""
    path = os.path.join(os.environ.get("HF_HOME", os.path.expanduser(r"~/.cache/huggingface")), "token")
    try:
        with open(path, encoding="utf-8") as f:
            return f.read().strip() or None
    except OSError:
        return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--source", choices=list(FILES), default="comfy-org")
    a = ap.parse_args()
    url, gated = FILES[a.source]
    token = os.environ.get("HF_TOKEN") or _cached_token()
    if gated and not token:
        sys.exit("This source is gated: set HF_TOKEN in the environment first (see the docstring).")
    fetch(url, os.path.join(COMFY, "models", "checkpoints", "stable-audio-open-1.0.safetensors"), token if gated else None)
    fetch(T5, os.path.join(COMFY, "models", "text_encoders", "t5-base.safetensors"))
    print("Done. Restart ComfyUI (or refresh) so the new models appear.")


if __name__ == "__main__":
    main()
