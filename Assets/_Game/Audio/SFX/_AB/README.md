# Sound A/B/C — pick one column per sound

Listen in the Unity Project window (select a clip → play in the Inspector preview). Built by `Tools/Audio/build_ab.py`; spec in `Docs/ArtDirection.md` §9 *Audio format spec*.

- **A** = real recording + `relaxed` filter (22.05 kHz, 12-bit dithered, LP 4.5 kHz, soft attack)
- **B** = softened synth (`sfxr.py soft_*`, darkness 0.3) + `relaxed` filter
- **C** = current dark8bit sound, only re-mastered to the new (quieter) role loudness

> **A is a stand-in.** The CC0 packs (Kenney, Junkala) couldn't be downloaded automatically (their sites block scripted downloads), so A uses realistic Stable Audio Open takes kept in `Tools/Audio/sources/stable_audio/`. Judge A as *"real recording + relaxed filter"*; to hear the actual CC0 recordings, download the packs listed in `Tools/Audio/SOURCES.md`, extract them into one folder and run `build_ab.py --source-dir <folder>`.

| Sound | Role (target LUFS) | A source | B preset | C original | LUFS A / B / C |
|---|---|---|---|---|---|
| Walk1 | footstep (-26) | stand-in: Stable Audio `step1_01.flac` | `soft_step` seed 40000 (darkness 0.3) | `SFX_Footsteps_StoneWalk_01.wav` | -26.0 / -26.0 / -26.0 |
| Walk2 | footstep (-26) | stand-in: Stable Audio `step1_02.flac` | `soft_step` seed 40001 (darkness 0.3) | `SFX_Footsteps_StoneWalk_02.wav` | -26.0 / -26.0 / -26.0 |
| Walk3 | footstep (-26) | stand-in: Stable Audio `step1_06.flac` | `soft_step` seed 40002 (darkness 0.3) | `SFX_Footsteps_StoneWalk_03.wav` | -26.0 / -26.0 / -26.0 |
| Land | impact (-20) | stand-in: Stable Audio `thud_01.flac` | `soft_land` seed 40100 (darkness 0.3) | `SFX_Character_Land_01.wav` | -20.9 / -20.0 / -20.0 |
| Jump | action (-22) | stand-in: Stable Audio `jumpA_04.flac` | `soft_jump` seed 40200 (darkness 0.3) | `SFX_Character_Jump_01.wav` | -24.2 / -22.0 / -22.0 |
| Pickup | action (-22) | stand-in: Stable Audio `pickup_01.flac` | `soft_pickup` seed 40300 (darkness 0.3) | `SFX_Items_Pickup_01.wav` | -22.7 / -22.0 / -22.0 |
| TorchWhoosh | action (-22) | stand-in: Stable Audio `swoosh_02.flac` | `soft_whoosh` seed 40400 (darkness 0.3) | `SFX_Items_TorchWhoosh_01.wav` | -22.0 / -22.0 / -22.0 |
| UIClick | ui (-24) | stand-in: Stable Audio `clickA_04.flac` | `soft_ui_click` seed 40500 (darkness 0.3) | `SFX_UI_Click_01.wav` | -24.3 / -24.0 / -24.0 |

LUFS = max momentary loudness (clip-length window for short clips); all peaks ≤ -3 dBFS true peak.
Very short clips (clicks, footsteps) may sit below target: the -3 dBTP ceiling wins first.
