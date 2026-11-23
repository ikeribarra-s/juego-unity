"""
build_ab.py — build the A/B/C listening set in Assets/_Game/Audio/SFX/_AB/ (Docs/ArtDirection.md §9).

For each sound family it renders three candidates, all mono WAV at the role loudness target:
  A = real recording + `--style relaxed`   (CC0 pack from --source-dir; otherwise the Stable Audio stand-in
                                             takes kept in Tools/Audio/sources/stable_audio/)
  B = softened synth (sfxr soft_* preset, darkness 0.3) + `--style relaxed`
  C = the current dark8bit asset, re-mastered to the new role target only (no other filtering)
and writes _AB/README.md (table) + _AB/AB_compare_sheet.png (waveforms + spectrograms).

Run with the ComfyUI venv:
  %PY% -I Tools/Audio/build_ab.py [--source-dir <folder with extracted CC0 packs>] [--out Assets/_Game/Audio/SFX/_AB]

--source-dir: extract the downloaded packs (Kenney Impact Sounds / RPG Audio / Interface Sounds / Digital
Audio, Juhani Junkala "512 Sound Effects (8-bit style)", or a Sonniss GDC bundle) anywhere, each in its own
folder, and point this at the parent. Files are matched by the glob patterns in SOUNDS (first match wins,
footsteps take the 1st/2nd/3rd match); anything not found falls back to the stand-in. Every file used is
printed and listed in the README — copy those lines into Tools/Audio/SOURCES.md.
"""
import argparse, fnmatch, os, sys
from types import SimpleNamespace

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)                    # our own sibling modules (the venv runs us with -I)
import numpy as np                          # noqa: E402
import process_sfx as P                     # noqa: E402
import sfxr as X                            # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
STANDIN = os.path.join(HERE, "sources", "stable_audio")
SFX = os.path.join(ROOT, "Assets", "_Game", "Audio", "SFX")

# key, role, CC0 glob patterns (lower-case, matched on file name), match index, stand-in, max len (s),
# soft preset, seed, C source (current asset)
SOUNDS = [
    ("Walk1", "footstep", ["footstep_concrete_*", "footstep0*", "*footstep*stone*", "sfx_movement_footsteps*"], 0,
     "step1_01.flac", 0.25, "soft_step", 40000, "Footsteps/SFX_Footsteps_StoneWalk_01.wav"),
    ("Walk2", "footstep", ["footstep_concrete_*", "footstep0*", "*footstep*stone*", "sfx_movement_footsteps*"], 1,
     "step1_02.flac", 0.25, "soft_step", 40001, "Footsteps/SFX_Footsteps_StoneWalk_02.wav"),
    ("Walk3", "footstep", ["footstep_concrete_*", "footstep0*", "*footstep*stone*", "sfx_movement_footsteps*"], 2,
     "step1_06.flac", 0.25, "soft_step", 40002, "Footsteps/SFX_Footsteps_StoneWalk_03.wav"),
    ("Land", "impact", ["impactsoft_heavy_*", "impactgeneric_heavy_*", "*land*", "impactpunch_heavy_*"], 0,
     "thud_01.flac", 0.6, "soft_land", 40100, "Character/SFX_Character_Land_01.wav"),
    ("Jump", "action", ["sfx_movement_jump*", "*jump*", "cloth*"], 0,
     "jumpA_04.flac", 0.4, "soft_jump", 40200, "Character/SFX_Character_Jump_01.wav"),
    ("Pickup", "action", ["handlesmallleather*", "*pickup*", "cloth*", "metalclick*"], 0,
     "pickup_01.flac", 0.4, "soft_pickup", 40300, "Items/SFX_Items_Pickup_01.wav"),
    ("TorchWhoosh", "action", ["*whoosh*", "*swoosh*", "*swish*", "knifeslice*"], 0,
     "swoosh_02.flac", 0.6, "soft_whoosh", 40400, "Items/SFX_Items_TorchWhoosh_01.wav"),
    ("UIClick", "ui", ["click_00*", "*menu*select*", "*click*"], 0,
     "clickA_04.flac", 0.2, "soft_ui_click", 40500, "UI/SFX_UI_Click_01.wav"),
]
AUDIO_EXT = (".wav", ".ogg", ".flac")


def opts(style, role, max_len, fade=None):
    return SimpleNamespace(mono=True, thresh=-50.0, fade=fade, max_len=max_len, style=style, lowpass=None,
                           room=False, room_wet=0.08, role=role, lufs=None, darkness=0.5, bits=None,
                           rate=None, fold=0.0)


def find_cc0(source_dir, patterns, index):
    if not source_dir:
        return None
    files = []
    for dp, _, fns in os.walk(source_dir):
        files += [os.path.join(dp, f) for f in fns if f.lower().endswith(AUDIO_EXT)]
    for pat in patterns:
        hits = sorted(f for f in files if fnmatch.fnmatch(os.path.basename(f).lower(), pat))
        if len(hits) > index:
            return hits[index]
    return None


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--source-dir")
    ap.add_argument("--out", default=os.path.join(SFX, "_AB"))
    a = ap.parse_args()
    os.makedirs(a.out, exist_ok=True)
    rng = np.random.default_rng(0)
    rows, sheet = [], []

    for key, role, pats, idx, standin, max_len, preset, seed, c_src in SOUNDS:
        # A — real recording
        src = find_cc0(a.source_dir, pats, idx)
        a_label = os.path.relpath(src, a.source_dir).replace("\\", "/") if src else f"stand-in: Stable Audio `{standin}`"
        src = src or os.path.join(STANDIN, standin)
        A = P.process_one(P.load(src), opts("relaxed", role, max_len), rng)

        # B — softened synth
        X.DARKNESS = 0.3
        r = np.random.default_rng(seed)
        raw = X.render(X.PRESETS[preset][0](r), r)[:, None]
        B = P.process_one(raw, opts("relaxed", role, max_len), rng)

        # C — current dark8bit asset, loudness only
        C = P.process_one(P.load(os.path.join(SFX, c_src)), opts("clean", role, None, fade=0.015), rng)

        lufs = []
        for tag, y in (("A", A), ("B", B), ("C", C)):
            path = os.path.join(a.out, f"AB_{key}_{tag}.wav")
            P.save(path, y)
            P.report(path, y)
            sheet.append(path)
            lufs.append(P.momentary_lufs(y))
        rows.append((key, role, a_label, f"`{preset}` seed {seed} (darkness 0.3)", f"`{os.path.basename(c_src)}`",
                     " / ".join(f"{v:.1f}" for v in lufs), P.ROLE_LUFS[role]))

    # README
    with open(os.path.join(a.out, "README.md"), "w", encoding="utf-8") as f:
        f.write("# Sound A/B/C — pick one column per sound\n\n"
                "Listen in the Unity Project window (select a clip → play in the Inspector preview). "
                "Built by `Tools/Audio/build_ab.py`; spec in `Docs/ArtDirection.md` §9 *Audio format spec*.\n\n"
                "- **A** = real recording + `relaxed` filter (22.05 kHz, 12-bit dithered, LP 4.5 kHz, soft attack)\n"
                "- **B** = softened synth (`sfxr.py soft_*`, darkness 0.3) + `relaxed` filter\n"
                "- **C** = current dark8bit sound, only re-mastered to the new (quieter) role loudness\n\n"
                + ("" if a.source_dir else
                   "> **A is a stand-in.** The CC0 packs (Kenney, Junkala) couldn't be downloaded automatically "
                   "(their sites block scripted downloads), so A uses realistic Stable Audio Open takes kept in "
                   "`Tools/Audio/sources/stable_audio/`. Judge A as *\"real recording + relaxed filter\"*; to hear "
                   "the actual CC0 recordings, download the packs listed in `Tools/Audio/SOURCES.md`, extract them "
                   "into one folder and run `build_ab.py --source-dir <folder>`.\n\n") +
                "| Sound | Role (target LUFS) | A source | B preset | C original | LUFS A / B / C |\n|---|---|---|---|---|---|\n")
        for key, role, al, bl, cl, lu, tgt in rows:
            f.write(f"| {key} | {role} ({tgt:g}) | {al} | {bl} | {cl} | {lu} |\n")
        f.write("\nLUFS = max momentary loudness (clip-length window for short clips); all peaks ≤ -3 dBFS true peak.\n"
                "Very short clips (clicks, footsteps) may sit below target: the -3 dBTP ceiling wins first.\n")
    # sheet
    P.cmd_inspect(SimpleNamespace(inputs=sheet, png=os.path.join(a.out, "AB_compare_sheet.png"), sheet=True))


if __name__ == "__main__":
    main()
