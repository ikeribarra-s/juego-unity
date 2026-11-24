"""
remaster_live.py — re-master the LIVE game SFX in place to the mix spec (Docs/ArtDirection.md §9 "Mix levels").

Same file names → same GUIDs → SoundDefinitions and scene wiring keep working. Writes into --out
(default: the Assets SFX folders themselves). The A/B folder (SFX/_AB) is never touched.

  %PY% -I Tools/Audio/remaster_live.py [--out <dir>] [--only Footsteps,Ambient,...]

What it does per group (MANIFEST below):
  • synth  — player / item / UI sounds: re-render the original `sfxr.py dark_*` preset with the original
             seeds at --darkness 0.35 (same character, far less crunch) → `--style relaxed` + role target.
  • file   — ambient one-shots: the current file → `--style relaxed` with a darker low-pass, shorter tail,
             role/LUFS target (distant things are dark and quiet).
  • loop   — area beds: loop-safe (filter the loop tiled ×3, keep the middle) 24 dB/oct low-pass to kill
             the 5–7 kHz hiss/whine + gain to the target. No trim / fade, so the loop stays seamless.
"""
import argparse, glob, os, sys
from types import SimpleNamespace

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import numpy as np            # noqa: E402
import process_sfx as P       # noqa: E402
import sfxr as X              # noqa: E402

SFX = os.path.abspath(os.path.join(HERE, "..", "..", "Assets", "_Game", "Audio", "SFX"))

# (category, name, kind, source, count/seed, lufs, lowpass, max_len, fade)
#   synth: source = sfxr preset, count/seed = (count, first seed)
#   file/loop: source = glob relative to SFX/, count/seed unused
MANIFEST = [
    # player — footsteps (role footstep -26 / crouch -30)
    ("Footsteps", "StoneWalk",   "synth", "dark_step",         (8, 11000), -26, 4000, None, 0.04),
    ("Footsteps", "StoneSprint", "synth", "dark_step_sprint",  (8, 12000), -26, 4200, None, 0.04),
    ("Footsteps", "StoneCrouch", "synth", "dark_step_crouch",  (6, 13000), -30, 3200, None, 0.04),
    # player — body / items (action -22, impact -20, idle -30)
    ("Character", "Jump",        "synth", "dark_jump",         (4, 15000), -22, 4500, None, 0.05),
    ("Character", "Land",        "synth", "dark_land",         (4, 14000), -20, 4000, None, 0.06),
    ("Character", "IdleCreak",   "synth", "dark_creak",        (4, 16000), -30, 3500, None, 0.08),
    ("Character", "BoneClack",   "synth", "dark_bone_clack",   (4, 26000), -24, 4500, None, 0.03),
    ("Items",     "Pickup",      "synth", "dark_pickup",       (4, 19000), -22, 4500, None, 0.05),
    ("Items",     "Drop",        "synth", "dark_drop",         (4, 20000), -22, 4000, None, 0.05),
    ("Items",     "TorchPickup", "synth", "dark_torch_pickup", (3, 18000), -22, 4500, None, 0.06),
    ("Items",     "TorchWhoosh", "synth", "dark_whoosh",       (4, 17000), -22, 4000, None, 0.08),
    ("Items",     "Powerup",     "synth", "dark_powerup",      (2, 25000), -24, 4500, None, 0.08),
    ("Combat",    "Punch",       "synth", "dark_punch",        (4, 21000), -20, 4500, None, 0.05),
    ("UI",        "Click",       "synth", "dark_ui_click",     (4, 22000), -24, 5000, None, 0.02),
    ("UI",        "Hover",       "synth", "dark_ui_hover",     (3, 23000), -28, 5000, None, 0.02),
    ("UI",        "Error",       "synth", "dark_ui_error",     (2, 24000), -24, 4500, None, 0.05),
    # ambient one-shots (quiet + dark; the 3D distance does the rest)
    ("Ambient", "Drip",          "file", "Ambient/SFX_Ambient_Drip_*.wav",          None, -30, 3500, 0.6, 0.15),
    ("Ambient", "Distant",       "file", "Ambient/SFX_Ambient_Distant_*.wav",       None, -26, 2500, 2.5, 0.4),
    ("Ambient", "Bell",          "file", "Ambient/SFX_Ambient_Bell_*.wav",          None, -26, 3000, None, 0.5),
    ("Ambient", "TorchGutter",   "file", "Ambient/SFX_Ambient_TorchGutter_*.wav",   None, -30, 3500, 1.2, 0.3),
    ("Ambient", "TorchOut",      "file", "Ambient/SFX_Ambient_TorchOut_*.wav",      None, -30, 3500, None, 0.3),
    ("Ambient", "TorchRelight",  "file", "Ambient/SFX_Ambient_TorchRelight_*.wav",  None, -30, 3500, None, 0.3),
    ("Ambient", "Sting",         "file", "Ambient/SFX_Ambient_Sting_*.wav",         None, -26, 4000, None, 0.4),
    # area beds (seamless loops)
    ("Ambient", "Bed",           "loop", "Ambient/SFX_Ambient_Bed*_01.wav",          None, -32, 3000, None, None),
]


def opts(lufs, lowpass, max_len, fade):
    return SimpleNamespace(mono=True, thresh=-50.0, fade=fade, max_len=max_len, style="relaxed",
                           lowpass=lowpass, room=False, room_wet=0.08, role="ambient", lufs=float(lufs),
                           limit=6.0, darkness=0.5, bits=None, rate=None, fold=0.0)


def loop_safe(x, lufs, lowpass):
    n = len(x)
    t = np.concatenate([x, x, x], axis=0)
    for _ in range(2):                                   # 2 × 12 dB/oct = 24 dB/oct
        t = P.filt(t, "lp", lowpass, 0.707)
    t = P.filt(t, "hp", 40.0)
    y = t[n:2 * n]
    y = y * 10 ** ((lufs - P.momentary_lufs(y)) / 20)
    pk = P.true_peak(y)
    if pk > 10 ** (-3 / 20):
        y *= 10 ** (-3 / 20) / pk
    return y


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--out", default=SFX)
    ap.add_argument("--only", help="comma list of categories")
    a = ap.parse_args()
    only = set(a.only.split(",")) if a.only else None
    rng = np.random.default_rng(1)

    for cat, name, kind, src, cs, lufs, lp, max_len, fade in MANIFEST:
        if only and cat not in only:
            continue
        outdir = os.path.join(a.out, cat)
        os.makedirs(outdir, exist_ok=True)
        if kind == "synth":
            count, seed = cs
            X.DARKNESS = 0.35
            fn = X.PRESETS[src][0]
            for i in range(count):
                r = np.random.default_rng(seed + i)
                raw = X.render(fn(r), r)[:, None]
                y = P.process_one(raw, opts(lufs, lp, max_len, fade), rng)
                dst = os.path.join(outdir, f"SFX_{cat}_{name}_{i + 1:02d}.wav")
                P.save(dst, y); P.report(dst, y)
        else:
            for f in sorted(glob.glob(os.path.join(SFX, src))):
                x = P.load(f)
                y = loop_safe(x.mean(axis=1, keepdims=True), lufs, lp) if kind == "loop" \
                    else P.process_one(x, opts(lufs, lp, max_len, fade), rng)
                dst = os.path.join(outdir, os.path.basename(f))
                P.save(dst, y); P.report(dst, y)


if __name__ == "__main__":
    main()
