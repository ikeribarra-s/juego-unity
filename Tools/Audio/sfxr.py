"""
sfxr.py — procedural retro sound effects (sfxr-style synth), no AI, no GPU.

Each preset is a small recipe of one or more *voices* (oscillator + pitch envelope + amp envelope +
swept filters) whose parameters are randomised from a seed, so `--count 6` gives six related but
different takes. Output: 44.1 kHz, 16-bit, mono WAV, peak-normalised to -1 dBFS.

Needs numpy + scipy. Run with the ComfyUI venv (has both):
  C:\\Users\\redux\\ComfyUI\\.venv\\Scripts\\python.exe -I Tools/Audio/sfxr.py <preset> --out <dir> [--count N] [--seed S]

STYLE: the game's sound is DARK 8-BIT (Docs/ArtDirection.md §9). Use the `dark_*` presets — NES-like
channels only (square 12.5/25/50 %, 4-bit triangle `tri4`, 15-bit LFSR noise `lfsr` / metallic
`lfsr_short`), 60 Hz stepped pitch/volume (`chip=True`), falling or dissonant pitch only — then run
`process_sfx.py process --style dark8bit`. The original presets (jump, pickup, …) are brighter and
cartoony; they're kept for reference only.

USAGE
  sfxr.py --list
  sfxr.py dark_step --out <tmp> --count 8 --seed 100
  sfxr.py jump      --out Assets/_Game/Audio/SFX/Character --count 4 --seed 100
  sfxr.py ui_click  --out Assets/_Game/Audio/SFX/UI --name Click
  sfxr.py hit --out tmp --wave-only          (one file per take, no name suffix when --count 1)

Files are named SFX_<Category>_<Name>_<NN>.wav (Docs/ArtDirection.md → Audio). --category / --name
override the preset defaults. Prints one line per file: path + seed (re-run with that seed to reproduce).
The raw output is already usable; run Tools/Audio/process_sfx.py with --style lofi so it matches
AI-generated sounds.
"""
import argparse, os, sys, wave
from dataclasses import dataclass, field, replace

import numpy as np
from scipy.signal import lfilter

SR = 44100


# ── Synth ────────────────────────────────────────────────────────────────────────────────────

@dataclass
class Voice:
    wave: str = "square"        # square | saw | triangle | sine | noise | pink
                                # chip channels: tri4 (NES 4-bit stepped triangle), lfsr (NES long noise,
                                # `freq` = clock Hz), lfsr_short (NES 93-step "metallic" noise, freq = clock Hz)
    freq: float = 440.0         # start frequency, Hz (for noise: sample-and-hold rate / 32)
    freq_min: float = 20.0      # pitch never slides below this
    slide: float = 0.0          # octaves per second (negative = falling)
    slide_accel: float = 0.0    # octaves per second²
    vib_depth: float = 0.0      # fraction of freq
    vib_hz: float = 0.0
    arp_mult: float = 1.0       # pitch jump multiplier …
    arp_time: float = 0.0       # … applied after this many seconds (0 = off)
    duty: float = 0.5           # square duty cycle
    duty_sweep: float = 0.0     # per second
    attack: float = 0.0         # seconds
    sustain: float = 0.05
    punch: float = 0.0          # extra level at the start of sustain (0..1)
    decay: float = 0.1
    decay_curve: float = 1.0    # 1 = linear, >1 = snappier exponential-ish tail
    lpf: float = 0.0            # low-pass cutoff Hz (0 = off)
    lpf_sweep: float = 0.0      # octaves per second
    lpf_q: float = 0.707
    hpf: float = 0.0            # high-pass cutoff Hz (0 = off)
    hpf_sweep: float = 0.0
    bpf: float = 0.0            # band-pass centre Hz (0 = off) — great for bony clacks / whooshes
    bpf_sweep: float = 0.0
    bpf_q: float = 4.0
    repeat: float = 0.0         # restart the pitch envelope every N seconds (0 = off)
    gain: float = 1.0
    delay: float = 0.0          # start offset inside the final mix, seconds
    chip: bool = False          # NES-style: pitch + volume update only at 60 Hz frames, volume in 16 steps


def _biquad(kind, f, q):
    f = float(np.clip(f, 20.0, SR * 0.45))
    w = 2 * np.pi * f / SR
    cs, sn = np.cos(w), np.sin(w)
    al = sn / (2 * q)
    if kind == "lp":
        b = [(1 - cs) / 2, 1 - cs, (1 - cs) / 2]
    elif kind == "hp":
        b = [(1 + cs) / 2, -(1 + cs), (1 + cs) / 2]
    else:  # band-pass, constant 0 dB peak
        b = [al, 0.0, -al]
    a = [1 + al, -2 * cs, 1 - al]
    return np.array(b) / a[0], np.array(a) / a[0]


def _swept_filter(x, kind, f0, sweep, q, block=128):
    """Biquad whose cutoff glides `sweep` octaves/second; coefficients updated per block."""
    if f0 <= 0:
        return x
    y = np.empty_like(x)
    zi = np.zeros(2)
    for s in range(0, len(x), block):
        f = f0 * 2 ** (sweep * s / SR)
        b, a = _biquad(kind, f, q)
        y[s:s + block], zi = lfilter(b, a, x[s:s + block], zi=zi)
    return y


_LFSR = {}


def _lfsr(short):
    """NES APU noise: 15-bit LFSR, feedback bit0 ^ bit1 (long, 32767 steps) or bit0 ^ bit6 (short, 93)."""
    if short not in _LFSR:
        reg, tap, out = 1, (6 if short else 1), []
        for _ in range(93 if short else 32767):
            fb = (reg & 1) ^ ((reg >> tap) & 1)
            reg = (reg >> 1) | (fb << 14)
            out.append(1.0 if reg & 1 else -1.0)
        _LFSR[short] = np.array(out)
    return _LFSR[short]


def _frames(t, n):
    """Sample index of the start of each sample's 60 Hz frame (for chip-style stepping)."""
    return np.minimum((np.floor(t * 60) / 60 * SR).astype(np.int64), n - 1)


def render_voice(v: Voice, rng: np.random.Generator) -> np.ndarray:
    length = v.attack + v.sustain + v.decay
    n = max(1, int(length * SR))
    t = np.arange(n) / SR
    tp = np.mod(t, v.repeat) if v.repeat > 0 else t

    # pitch trajectory (log domain), arpeggio, vibrato
    log_f = np.log2(max(v.freq, 1.0)) + v.slide * tp + 0.5 * v.slide_accel * tp ** 2
    f = np.maximum(2 ** log_f, v.freq_min)
    if v.arp_time > 0:
        f = np.where(tp >= v.arp_time, f * v.arp_mult, f)
    if v.vib_depth > 0:
        f = f * (1 + v.vib_depth * np.sin(2 * np.pi * v.vib_hz * t))
    if v.chip:
        f = f[_frames(t, n)]
    phase = np.cumsum(f / SR)
    frac = phase % 1.0

    if v.wave == "square":
        duty = np.clip(v.duty + v.duty_sweep * t, 0.05, 0.95)
        x = np.where(frac < duty, 1.0, -1.0)
    elif v.wave == "saw":
        x = 2 * frac - 1
    elif v.wave == "triangle":
        x = 1 - 4 * np.abs(frac - 0.5)
    elif v.wave == "tri4":
        x = np.round((1 - 4 * np.abs(frac - 0.5)) * 7.5) / 7.5
    elif v.wave in ("lfsr", "lfsr_short"):
        seq = _lfsr(v.wave == "lfsr_short")
        offset = int(rng.integers(0, len(seq)))
        x = seq[(np.floor(phase).astype(np.int64) + offset) % len(seq)]
    elif v.wave == "sine":
        x = np.sin(2 * np.pi * phase)
    elif v.wave == "noise":  # sfxr-style sample-and-hold: 32 random values per period
        idx = np.floor(phase * 32).astype(np.int64)
        table = rng.uniform(-1, 1, idx[-1] + 2)
        x = table[idx]
    elif v.wave == "pink":   # smoother noise, independent of pitch
        w = rng.standard_normal(n)
        x = lfilter([0.049922035, -0.095993537, 0.050612699, -0.004408786],
                    [1, -2.494956002, 2.017265875, -0.522189400], w)
        x /= np.max(np.abs(x)) + 1e-9
    else:
        raise ValueError(f"unknown wave {v.wave}")

    # amplitude envelope: attack → sustain (+punch) → decay
    env = np.zeros(n)
    a_n, s_n = int(v.attack * SR), int(v.sustain * SR)
    d_n = n - a_n - s_n
    if a_n:
        env[:a_n] = np.linspace(0, 1, a_n, endpoint=False)
    if s_n:
        env[a_n:a_n + s_n] = 1 + v.punch * np.linspace(1, 0, s_n)
    if d_n > 0:
        env[a_n + s_n:] = np.linspace(1, 0, d_n) ** v.decay_curve
    if v.chip:
        env = env[_frames(t, n)]
        top = max(env.max(), 1e-9)
        env = np.round(env / top * 15) / 15 * top
    x = x * env

    x = _swept_filter(x, "lp", v.lpf, v.lpf_sweep, v.lpf_q)
    x = _swept_filter(x, "hp", v.hpf, v.hpf_sweep, 0.707)
    x = _swept_filter(x, "bp", v.bpf, v.bpf_sweep, v.bpf_q)
    return x * v.gain


def render(voices, rng) -> np.ndarray:
    parts = [(int(v.delay * SR), render_voice(v, rng)) for v in voices]
    n = max(o + len(p) for o, p in parts)
    out = np.zeros(n)
    for o, p in parts:
        out[o:o + len(p)] += p
    # tiny fade-in/out to avoid clicks at the very edges
    k = min(32, n // 4)
    out[:k] *= np.linspace(0, 1, k)
    out[-k:] *= np.linspace(1, 0, k)
    peak = np.max(np.abs(out)) or 1.0
    return out / peak * 10 ** (-1 / 20)


def write_wav(path, x):
    pcm = (np.clip(x, -1, 1) * 32767).astype("<i2")
    with wave.open(path, "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(pcm.tobytes())


# ── Presets ──────────────────────────────────────────────────────────────────────────────────
# Each returns a list of Voices. `r` is a seeded numpy Generator; `u(a, b)` = uniform helper.

def _u(r):
    return lambda a, b: float(r.uniform(a, b))


def p_jump(r):
    u = _u(r)
    return [Voice(wave=r.choice(["square", "square", "triangle"]), freq=u(260, 420), slide=u(2.5, 5.0),
                  duty=u(0.25, 0.5), duty_sweep=u(-0.6, 0.0), sustain=u(0.03, 0.07), decay=u(0.12, 0.22),
                  punch=u(0.0, 0.3), lpf=u(3500, 7000)),
            # bony "clack" on take-off — the skeleton's knees
            Voice(wave="noise", freq=u(2500, 4000), sustain=0.0, decay=u(0.02, 0.04), decay_curve=2.5,
                  bpf=u(1800, 3200), bpf_q=6, gain=u(0.35, 0.6))]


def p_land(r):
    u = _u(r)
    return [Voice(wave="sine", freq=u(110, 160), slide=u(-5, -3), sustain=0.01, decay=u(0.12, 0.2),
                  decay_curve=2, punch=0.6, gain=1.0),
            Voice(wave="noise", freq=u(600, 1200), sustain=0.0, decay=u(0.08, 0.14), decay_curve=2,
                  lpf=u(1800, 3000), lpf_sweep=-6, gain=0.55),
            # bones rattling on impact: 2–3 quick clacks
            *[Voice(wave="noise", freq=u(2800, 4200), sustain=0.0, decay=u(0.015, 0.03), decay_curve=3,
                    bpf=u(1600, 3400), bpf_q=8, gain=u(0.25, 0.45), delay=u(0.0, 0.01) + k * u(0.025, 0.045))
              for k in range(int(r.integers(2, 4)))]]


def p_hit(r):
    u = _u(r)
    return [Voice(wave="noise", freq=u(900, 2000), sustain=u(0.0, 0.02), decay=u(0.1, 0.2), decay_curve=2,
                  punch=u(0.3, 0.8), lpf=u(3000, 6000), lpf_sweep=u(-8, -4)),
            Voice(wave="sine", freq=u(90, 140), slide=u(-6, -3), sustain=0.01, decay=u(0.1, 0.16),
                  decay_curve=2, gain=u(0.7, 1.0)),
            Voice(wave="noise", freq=u(3000, 4500), sustain=0.0, decay=u(0.02, 0.035), decay_curve=3,
                  bpf=u(2000, 3500), bpf_q=5, gain=u(0.4, 0.6))]


def p_pickup(r):
    u = _u(r)
    f = u(700, 1100)
    return [Voice(wave=r.choice(["square", "triangle"]), freq=f, duty=u(0.3, 0.5), arp_mult=u(1.3, 1.6),
                  arp_time=u(0.04, 0.07), sustain=u(0.04, 0.08), decay=u(0.12, 0.2), punch=u(0.2, 0.5),
                  lpf=u(5000, 9000))]


def p_drop(r):
    u = _u(r)
    return [Voice(wave="triangle", freq=u(350, 500), slide=u(-4, -2), sustain=0.02, decay=u(0.1, 0.16),
                  lpf=4000),
            Voice(wave="noise", freq=u(500, 900), sustain=0.0, decay=u(0.05, 0.09), decay_curve=2,
                  lpf=u(1500, 2500), gain=0.5, delay=u(0.06, 0.1)),
            Voice(wave="sine", freq=u(120, 170), slide=-4, sustain=0.0, decay=0.08, decay_curve=2,
                  gain=0.6, delay=u(0.06, 0.1))]


def p_ui_click(r):
    u = _u(r)
    return [Voice(wave="square", freq=u(1400, 2200), duty=0.5, sustain=0.0, decay=u(0.02, 0.035),
                  decay_curve=2.5, lpf=u(5000, 8000)),
            Voice(wave="noise", freq=u(4000, 6000), sustain=0.0, decay=0.012, decay_curve=3, bpf=u(2500, 4000),
                  bpf_q=3, gain=0.5)]


def p_ui_hover(r):
    u = _u(r)
    return [Voice(wave="sine", freq=u(900, 1300), slide=u(0.5, 1.5), attack=0.004, sustain=0.0,
                  decay=u(0.04, 0.07), decay_curve=2, gain=0.8)]


def p_error(r):
    u = _u(r)
    f = u(140, 200)
    return [Voice(wave="square", freq=f, duty=u(0.4, 0.5), sustain=u(0.08, 0.12), decay=0.04,
                  repeat=0, lpf=2500, gain=0.9),
            Voice(wave="square", freq=f * u(0.75, 0.84), duty=0.5, sustain=u(0.12, 0.18), decay=0.08,
                  lpf=2000, gain=0.9, delay=u(0.13, 0.16))]


def p_powerup(r):
    u = _u(r)
    return [Voice(wave=r.choice(["square", "saw"]), freq=u(250, 400), slide=u(3, 5), repeat=u(0.07, 0.11),
                  duty=u(0.3, 0.5), sustain=u(0.25, 0.4), decay=u(0.15, 0.3), vib_depth=u(0, 0.04),
                  vib_hz=u(8, 14), lpf=u(4000, 8000))]


def p_bone_clack(r):
    """Dry hollow bone knock — the skeleton's signature. Two quick resonant ticks."""
    u = _u(r)
    return [Voice(wave="noise", freq=u(3000, 5000), sustain=0.0, decay=u(0.02, 0.04), decay_curve=3,
                  bpf=u(1200, 2600), bpf_q=u(6, 12), gain=1.0, delay=k * u(0.03, 0.06))
            for k in range(int(r.integers(1, 3)))] + \
           [Voice(wave="sine", freq=u(500, 800), slide=-3, sustain=0.0, decay=0.03, decay_curve=3, gain=0.35)]


def p_footstep(r):
    """Placeholder bony footstep on stone: soft low scuff + short bone tick."""
    u = _u(r)
    return [Voice(wave="pink", freq=1, sustain=0.0, decay=u(0.07, 0.12), decay_curve=2, lpf=u(1200, 2200),
                  hpf=120, gain=0.9),
            Voice(wave="sine", freq=u(90, 130), slide=-4, sustain=0.0, decay=0.06, decay_curve=2, gain=0.5),
            Voice(wave="noise", freq=u(3000, 4500), sustain=0.0, decay=u(0.015, 0.03), decay_curve=3,
                  bpf=u(1500, 3000), bpf_q=8, gain=u(0.35, 0.55), delay=u(0.005, 0.02))]


def p_whoosh(r):
    """Air swish (e.g. torch throw placeholder): band-passed noise sweeping up then fading."""
    u = _u(r)
    return [Voice(wave="pink", freq=1, attack=u(0.08, 0.15), sustain=u(0.02, 0.06), decay=u(0.18, 0.3),
                  bpf=u(350, 600), bpf_sweep=u(2.5, 4.5), bpf_q=1.6, gain=1.0)]


def p_creak(r):
    """Subtle bone creak/rattle (idle placeholder): slow saw grind through a resonant band."""
    u = _u(r)
    return [Voice(wave="saw", freq=u(35, 60), vib_depth=u(0.15, 0.3), vib_hz=u(5, 11), attack=0.05,
                  sustain=u(0.2, 0.35), decay=0.15, bpf=u(700, 1200), bpf_q=5, gain=0.8),
            *[Voice(wave="noise", freq=u(3000, 4500), sustain=0.0, decay=0.02, decay_curve=3,
                    bpf=u(1500, 3000), bpf_q=8, gain=u(0.15, 0.3), delay=u(0.05, 0.4))
              for _ in range(int(r.integers(1, 4)))]]


# ── Dark 8-bit presets (Docs/ArtDirection.md §9) ─────────────────────────────────────────────
# NES-ish palette only: square (12.5/25/50 % duty), 4-bit triangle, LFSR noise (long + short/metallic).
# ≤ 3–4 voices, short decays, low thuds, pitch only ever falls or moves by dissonant steps
# (minor 2nd, tritone) — no rising boings, no happy arpeggios.

SEMI = 2 ** (1 / 12)
TRITONE = 2 ** 0.5
DARKNESS = 0.5   # set by --darkness (0..1). 1.0 = the original, heaviest version of these presets.


def _lift():
    """Pitch/cutoff multiplier: 1.0 at darkness 1, ≈1.23 (+3.6 semitones) at 0.5, 1.52 at 0."""
    return 2 ** ((1 - DARKNESS) * 0.6)


def _dis():
    """The 'wrong' interval: a tritone only when very dark, otherwise a minor third."""
    return TRITONE if DARKNESS >= 0.75 else 2 ** (3 / 12)


def _clarity():
    """Extra gain for transients (bone ticks, attacks) as darkness goes down: 1.0 → 1.3."""
    return 1 + 0.6 * (1 - DARKNESS) if DARKNESS < 1 else 1.0


def _bone_tick(u, gain=(0.25, 0.45), delay=0.0):
    """Short metallic-noise tick = the skeleton's bone clack in chip form."""
    return Voice(wave="lfsr_short", freq=u(9000, 16000), sustain=0.0, decay=u(0.012, 0.025), decay_curve=2.5,
                 hpf=u(900, 1500), lpf=u(5000, 7000) * _lift(), gain=u(*gain) * _clarity(), delay=delay)


def d_step(r, sprint=False, crouch=False):
    """Dark chip footstep on stone: LFSR scuff + 4-bit triangle thud + bone tick."""
    u = _u(r)
    dec = u(0.035, 0.05) if sprint else u(0.05, 0.08)
    L = _lift()
    voices = [Voice(wave="lfsr", freq=u(3000, 7000) if not crouch else u(1500, 3000), sustain=0.0, decay=dec,
                    decay_curve=1.8, lpf=(u(2000, 3500) if not crouch else u(900, 1400)) * L, hpf=150,
                    gain=1.0 if not crouch else 0.8, chip=False),
              Voice(wave="tri4", freq=u(70, 105) * L, slide=-u(3, 6), sustain=0.0,
                    decay=u(0.06, 0.09) if sprint else u(0.09, 0.13),
                    decay_curve=1.5, gain=u(0.6, 0.9) if not crouch else u(0.4, 0.6), delay=u(0.0, 0.006))]
    if not crouch or r.random() < 0.5:
        voices.append(_bone_tick(u, gain=(0.35, 0.6) if sprint else (0.2, 0.4) if not crouch else (0.08, 0.15),
                                 delay=u(0.004, 0.02)))
    return voices


def d_step_sprint(r):
    """Faster, harder chip step (sprint)."""
    return d_step(r, sprint=True)


def d_step_crouch(r):
    """Muffled sneaking chip step (crouch)."""
    return d_step(r, crouch=True)


def d_land(r):
    """Heavy low thud + noise slam + bone rattle (2–3 metallic ticks)."""
    u = _u(r)
    L = _lift()
    return [Voice(wave="tri4", freq=u(65, 90) * L, slide=-u(4, 7), sustain=0.01, decay=u(0.16, 0.24), punch=0.4,
                  chip=True),
            Voice(wave="lfsr", freq=u(2500, 5000), slide=-u(3, 5), sustain=0.0, decay=u(0.1, 0.16), decay_curve=1.6,
                  lpf=u(1400, 2200) * L, lpf_sweep=-4, gain=0.8 * _clarity(), chip=True),
            *[_bone_tick(u, gain=(0.2, 0.4), delay=u(0.03, 0.05) + k * u(0.03, 0.05))
              for k in range(int(r.integers(2, 4)))]]


def d_jump(r):
    """Take-off: noise puff + low 12.5 % square blip that steps DOWN a semitone + knee clack."""
    u = _u(r)
    f = u(98, 147) * _lift()
    return [Voice(wave="lfsr", freq=u(4000, 8000), attack=0.0, sustain=0.0, decay=u(0.05, 0.08), decay_curve=1.5,
                  bpf=u(700, 1200) * _lift(), bpf_q=1.5, gain=0.9),
            Voice(wave="square", freq=f, duty=0.125, arp_mult=1 / SEMI, arp_time=u(0.03, 0.05), sustain=u(0.03, 0.05),
                  decay=u(0.05, 0.08), lpf=u(1800, 2600) * _lift(), gain=u(0.35, 0.5), chip=True),
            _bone_tick(u, gain=(0.3, 0.5), delay=u(0.0, 0.01))]


def d_creak(r):
    """Idle bone creak: slow detuned 12.5 % square grind through a dark band + sparse ticks."""
    u = _u(r)
    return [Voice(wave="square", freq=u(28, 45), duty=0.125, vib_depth=u(0.2, 0.35), vib_hz=u(3, 7), attack=u(0.06, 0.12),
                  sustain=u(0.2, 0.4), decay=u(0.15, 0.25), bpf=u(450, 800) * _lift(), bpf_q=4, gain=0.8, chip=True),
            *[_bone_tick(u, gain=(0.15, 0.35), delay=u(0.05, 0.5)) for _ in range(int(r.integers(2, 5)))]]


def d_whoosh(r):
    """Torch throw: LFSR noise whose clock + band sweep up, with fire crackle ticks."""
    u = _u(r)
    return [Voice(wave="lfsr", freq=u(1500, 2500), slide=u(2, 3.5), attack=u(0.06, 0.1), sustain=u(0.03, 0.06),
                  decay=u(0.18, 0.28), decay_curve=1.4, bpf=u(400, 650), bpf_sweep=u(2, 3.5), bpf_q=1.4,
                  gain=1.0, chip=True),
            Voice(wave="tri4", freq=u(45, 60), attack=0.05, sustain=0.05, decay=0.15, gain=0.3),
            *[Voice(wave="lfsr_short", freq=u(6000, 11000), sustain=0.0, decay=0.01, decay_curve=2,
                    hpf=1200, gain=u(0.12, 0.25), delay=u(0.03, 0.35)) for _ in range(int(r.integers(3, 6)))]]


def d_torch_pickup(r):
    """Grabbing the torch: low tritone square dyad (unsettling) + flame flick + crackle."""
    u = _u(r)
    f = u(110, 147) * _lift()
    return [Voice(wave="square", freq=f, duty=0.25, sustain=u(0.04, 0.07), decay=u(0.12, 0.18), lpf=2200 * _lift(),
                  gain=0.45, chip=True),
            Voice(wave="square", freq=f * _dis() * u(0.99, 1.01), duty=0.125, sustain=u(0.04, 0.07),
                  decay=u(0.12, 0.18), lpf=2200 * _lift(), gain=0.35, chip=True),
            Voice(wave="lfsr", freq=u(3000, 6000), attack=0.02, sustain=0.0, decay=u(0.12, 0.2), bpf=u(900, 1400),
                  bpf_q=1.2, gain=0.6),
            *[Voice(wave="lfsr_short", freq=u(6000, 11000), sustain=0.0, decay=0.01, hpf=1200,
                    gain=u(0.12, 0.22), delay=u(0.02, 0.2)) for _ in range(2)]]


def d_pickup(r):
    """Evidence grab: detuned low square pair, short, steps down a semitone — wrong-feeling blip."""
    u = _u(r)
    f = u(196, 262)
    det = 1 + u(0.015, 0.03) * (0.4 + 0.6 * DARKNESS)
    return [Voice(wave="square", freq=f, duty=0.25, arp_mult=1 / SEMI, arp_time=u(0.04, 0.06), sustain=u(0.06, 0.09),
                  decay=u(0.06, 0.1), lpf=2600, gain=0.5, chip=True),
            Voice(wave="square", freq=f * det, duty=0.125, arp_mult=1 / SEMI, arp_time=u(0.04, 0.06),
                  sustain=u(0.06, 0.09), decay=u(0.06, 0.1), lpf=2600, gain=0.4, chip=True),
            _bone_tick(u, gain=(0.2, 0.35))]


def d_drop(r):
    """Evidence release/drop: triangle clunk + dull noise knock."""
    u = _u(r)
    return [Voice(wave="tri4", freq=u(95, 135) * _lift(), slide=-u(4, 6), sustain=0.0, decay=u(0.09, 0.14),
                  decay_curve=1.4, chip=True),
            Voice(wave="lfsr", freq=u(2000, 4000), sustain=0.0, decay=u(0.05, 0.09), decay_curve=1.8,
                  lpf=u(1000, 1600) * _lift(), gain=0.7, delay=u(0.0, 0.01)),
            _bone_tick(u, gain=(0.12, 0.25), delay=u(0.03, 0.06))]


def d_punch(r):
    """Hit: noise slam with closing low-pass, low triangle body, metallic crack."""
    u = _u(r)
    return [Voice(wave="lfsr", freq=u(5000, 9000), slide=-u(3, 5), sustain=u(0.0, 0.015), decay=u(0.08, 0.14),
                  decay_curve=1.6, punch=0.5, lpf=u(3500, 5000), lpf_sweep=u(-9, -6), chip=True),
            Voice(wave="tri4", freq=u(60, 85) * _lift(), slide=-u(4, 7), sustain=0.01, decay=u(0.1, 0.15),
                  gain=u(0.7, 0.9), chip=True),
            Voice(wave="lfsr_short", freq=u(7000, 12000), sustain=0.0, decay=u(0.02, 0.035), decay_curve=2,
                  hpf=1000, gain=u(0.35, 0.5))]


def d_bone_clack(r):
    """Skeleton bone knock in chip form: 1–3 metallic LFSR ticks over a tiny triangle knock."""
    u = _u(r)
    return [_bone_tick(u, gain=(0.7, 1.0), delay=k * u(0.03, 0.06)) for k in range(int(r.integers(1, 4)))] +            [Voice(wave="tri4", freq=u(160, 240), slide=-4, sustain=0.0, decay=0.03, gain=0.4)]


def d_ui_click(r):
    """Menu click: very short low 12.5 % square tick."""
    u = _u(r)
    return [Voice(wave="square", freq=u(330, 520) * _lift(), duty=0.125, sustain=0.0, decay=u(0.02, 0.03),
                  decay_curve=1.5, lpf=3500 * _lift()),
            Voice(wave="lfsr_short", freq=u(8000, 12000), sustain=0.0, decay=0.008, hpf=1500, gain=0.3)]


def d_ui_hover(r):
    """Menu hover: soft 4-bit triangle blip, slightly detuned."""
    u = _u(r)
    return [Voice(wave="tri4", freq=u(220, 330) * _lift() * u(0.985, 1.015), attack=0.003, sustain=0.0,
                  decay=u(0.04, 0.06),
                  decay_curve=1.5, gain=0.9)]


def d_ui_error(r):
    """Denied: two low squares falling a tritone."""
    u = _u(r)
    f = u(130, 175) * _lift()
    return [Voice(wave="square", freq=f, duty=0.25, sustain=u(0.07, 0.09), decay=0.03, lpf=1800 * _lift(), chip=True),
            Voice(wave="square", freq=f / _dis(), duty=0.5, sustain=u(0.12, 0.16), decay=0.08, lpf=1500 * _lift(),
                  chip=True, delay=u(0.11, 0.13))]


def d_powerup(r):
    """Objective sting (dark): descending minor triad on a 25 % square over a low drone."""
    u = _u(r)
    f = u(220, 262)
    notes = [1.0, 1 / 2 ** (3 / 12), 1 / 2 ** (7 / 12), 1 / 2 ** (13 / 12)]  # root, m3 down, 5th down, b9 down
    step = u(0.07, 0.09)
    return [Voice(wave="square", freq=f * k, duty=0.25, sustain=step * 0.8, decay=0.06, lpf=2500, gain=0.6,
                  chip=True, delay=i * step) for i, k in enumerate(notes)] + \
           [Voice(wave="tri4", freq=f / 4, attack=0.02, sustain=step * 4, decay=0.2, gain=0.5)]


# ── Soft presets (B candidates for the "relaxed" direction) ──────────────────────────────────
# Smooth triangle + band-passed pink noise instead of squares/LFSR, 3–8 ms attacks, longer exponential
# tails, no chip stepping. Use with --darkness 0.3 and process_sfx.py --style relaxed --role <role>.

def s_step(r):
    """Soft stone step: band-passed pink scuff + low triangle thud + faint dry tick."""
    u = _u(r)
    L = _lift()
    return [Voice(wave="pink", freq=1, attack=0.003, sustain=0.0, decay=u(0.09, 0.14), decay_curve=2.2,
                  bpf=u(600, 1100) * L, bpf_q=0.9, lpf=2500 * L, gain=0.9),
            Voice(wave="triangle", freq=u(80, 115) * L, slide=-u(2, 4), attack=0.004, sustain=0.0,
                  decay=u(0.1, 0.15), decay_curve=2.0, gain=u(0.6, 0.8), delay=u(0.0, 0.006)),
            Voice(wave="pink", freq=1, attack=0.001, sustain=0.0, decay=u(0.012, 0.02), decay_curve=2.5,
                  bpf=u(1800, 2600), bpf_q=4, gain=u(0.12, 0.22), delay=u(0.004, 0.015))]


def s_land(r):
    """Soft heavy land: low triangle body + dull noise thump, gentle rattle."""
    u = _u(r)
    L = _lift()
    return [Voice(wave="triangle", freq=u(60, 80) * L, slide=-u(2, 4), attack=0.004, sustain=0.01,
                  decay=u(0.22, 0.3), decay_curve=2.2, gain=1.0),
            Voice(wave="pink", freq=1, attack=0.003, sustain=0.0, decay=u(0.14, 0.2), decay_curve=2.0,
                  lpf=u(900, 1300) * L, lpf_sweep=-2, gain=0.8),
            *[Voice(wave="pink", freq=1, attack=0.001, sustain=0.0, decay=0.02, decay_curve=2.5,
                    bpf=u(1500, 2400), bpf_q=4, gain=u(0.08, 0.15), delay=u(0.04, 0.12)) for _ in range(2)]]


def s_jump(r):
    """Soft take-off: breathy noise puff + small falling triangle."""
    u = _u(r)
    L = _lift()
    return [Voice(wave="pink", freq=1, attack=0.006, sustain=0.0, decay=u(0.1, 0.14), decay_curve=1.8,
                  bpf=u(700, 1000) * L, bpf_q=1.0, gain=0.9),
            Voice(wave="triangle", freq=u(150, 200) * L, slide=-u(1, 2), attack=0.005, sustain=0.0,
                  decay=u(0.08, 0.12), decay_curve=2.0, gain=0.35)]


def s_pickup(r):
    """Soft grab: two quiet triangle notes a minor third apart, rounded attack."""
    u = _u(r)
    f = u(330, 440) * _lift() / 1.2
    return [Voice(wave="triangle", freq=f, attack=0.005, sustain=0.03, decay=u(0.12, 0.16), decay_curve=2.0,
                  gain=0.6),
            Voice(wave="triangle", freq=f * 2 ** (3 / 12), attack=0.005, sustain=0.02, decay=u(0.14, 0.2),
                  decay_curve=2.2, gain=0.5, delay=u(0.05, 0.07)),
            Voice(wave="pink", freq=1, attack=0.002, sustain=0.0, decay=0.04, bpf=1600, bpf_q=2, gain=0.2)]


def s_whoosh(r):
    """Soft air swish: pink noise through a slowly sweeping wide band, long fade."""
    u = _u(r)
    return [Voice(wave="pink", freq=1, attack=u(0.08, 0.12), sustain=u(0.03, 0.06), decay=u(0.25, 0.35),
                  decay_curve=1.6, bpf=u(350, 500), bpf_sweep=u(1.5, 2.5), bpf_q=0.9, gain=1.0),
            Voice(wave="triangle", freq=u(55, 70), attack=0.06, sustain=0.05, decay=0.2, gain=0.15)]


def s_ui_click(r):
    """Soft menu click: short rounded triangle tick."""
    u = _u(r)
    return [Voice(wave="triangle", freq=u(700, 950), attack=0.002, sustain=0.0, decay=u(0.035, 0.05),
                  decay_curve=2.2, gain=0.8),
            Voice(wave="pink", freq=1, attack=0.001, sustain=0.0, decay=0.01, bpf=2500, bpf_q=2, gain=0.15)]


# name → (function, default category, default name)
PRESETS = {
    # soft set (relaxed-direction B candidates)
    "soft_step": (s_step, "Footsteps", "StoneWalk"),
    "soft_land": (s_land, "Character", "Land"),
    "soft_jump": (s_jump, "Character", "Jump"),
    "soft_pickup": (s_pickup, "Items", "Pickup"),
    "soft_whoosh": (s_whoosh, "Items", "TorchWhoosh"),
    "soft_ui_click": (s_ui_click, "UI", "Click"),
    # dark 8-bit set (current style)
    "dark_step": (d_step, "Footsteps", "StoneWalk"),
    "dark_step_sprint": (d_step_sprint, "Footsteps", "StoneSprint"),
    "dark_step_crouch": (d_step_crouch, "Footsteps", "StoneCrouch"),
    "dark_land": (d_land, "Character", "Land"),
    "dark_jump": (d_jump, "Character", "Jump"),
    "dark_creak": (d_creak, "Character", "IdleCreak"),
    "dark_whoosh": (d_whoosh, "Items", "TorchWhoosh"),
    "dark_torch_pickup": (d_torch_pickup, "Items", "TorchPickup"),
    "dark_pickup": (d_pickup, "Items", "Pickup"),
    "dark_drop": (d_drop, "Items", "Drop"),
    "dark_punch": (d_punch, "Combat", "Punch"),
    "dark_ui_click": (d_ui_click, "UI", "Click"),
    "dark_ui_hover": (d_ui_hover, "UI", "Hover"),
    "dark_ui_error": (d_ui_error, "UI", "Error"),
    "dark_powerup": (d_powerup, "Items", "Powerup"),
    "dark_bone_clack": (d_bone_clack, "Character", "BoneClack"),
    # original (brighter/cartoony) set — kept for reference

    "jump": (p_jump, "Character", "Jump"),
    "land": (p_land, "Character", "Land"),
    "hit": (p_hit, "Combat", "Punch"),
    "punch": (p_hit, "Combat", "Punch"),
    "pickup": (p_pickup, "Items", "Pickup"),
    "drop": (p_drop, "Items", "Drop"),
    "ui_click": (p_ui_click, "UI", "Click"),
    "ui_hover": (p_ui_hover, "UI", "Hover"),
    "error": (p_error, "UI", "Error"),
    "powerup": (p_powerup, "Items", "Powerup"),
    "bone_clack": (p_bone_clack, "Character", "BoneClack"),
    "footstep": (p_footstep, "Footsteps", "Stone"),
    "whoosh": (p_whoosh, "Items", "Whoosh"),
    "creak": (p_creak, "Character", "IdleCreak"),
}


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("preset", nargs="?")
    ap.add_argument("--out")
    ap.add_argument("--count", type=int, default=1)
    ap.add_argument("--seed", type=int)
    ap.add_argument("--category")
    ap.add_argument("--name")
    ap.add_argument("--suffix", default="", help="appended to the name, e.g. _placeholder")
    ap.add_argument("--list", action="store_true")
    ap.add_argument("--darkness", type=float, default=0.5,
                    help="dark_* presets: 0..1 (house default 0.5; 1 = heaviest, lower = brighter/less dissonant)")
    a = ap.parse_args()
    if a.list or not a.preset:
        for k, (fn, cat, nm) in PRESETS.items():
            print(f"{k:11s} -> SFX_{cat}_{nm}_NN.wav   {(fn.__doc__ or '').strip()}")
        return
    if a.preset not in PRESETS:
        sys.exit(f"unknown preset '{a.preset}'. --list shows them.")
    if not a.out:
        sys.exit("--out is required")
    global DARKNESS
    DARKNESS = float(np.clip(a.darkness, 0, 1))
    fn, cat, nm = PRESETS[a.preset]
    cat, nm = a.category or cat, (a.name or nm) + a.suffix
    os.makedirs(a.out, exist_ok=True)
    seed = a.seed if a.seed is not None else int(np.random.default_rng().integers(0, 2**31 - 1))
    for i in range(a.count):
        r = np.random.default_rng(seed + i)
        x = render(fn(r), r)
        path = os.path.join(a.out, f"SFX_{cat}_{nm}_{i + 1:02d}.wav")
        write_wav(path, x)
        print(f"{path}  (seed {seed + i}, {len(x) / SR:.2f} s)")


if __name__ == "__main__":
    main()
