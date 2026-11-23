"""
process_sfx.py — clean up, unify and inspect game sound effects (Fluffterror audio pipeline).

Needs numpy, scipy, soundfile, matplotlib — all in the ComfyUI venv. Run:
  set PY=C:\\Users\\redux\\ComfyUI\\.venv\\Scripts\\python.exe
  %PY% -I Tools/Audio/process_sfx.py <command> ...

COMMANDS
  process  <in...> --out DIR [--category C --name N] [--mono] [--style lofi|clean] [--room]
           [--lufs -16] [--fade 0.05] [--max-len S] [--suffix _placeholder]
      Trim leading/trailing silence, fade-out tail, loudness-normalise (peak ceiling -1 dBFS),
      optional mono-fy and lo-fi style pass. Writes 44.1 kHz 16-bit WAV.
      With --category/--name the files are numbered SFX_<C>_<N>_NN.wav (in input order).
  split    <in> --out DIR --category C --name N [--min-gap 0.06] [--max-hits 12] [process options]
      Cut a long generation (e.g. "several footsteps") into separate one-shots by onset
      detection, then run each through `process`.
  vary     <in> --out DIR --count N [--pitch 0.6] [--eq 2.0] [--seed S]
      Make N variations: ±pitch semitones (tape-style, changes length too) + random
      low/high-shelf EQ tilt (±dB). The first output is the original. Naming continues the
      input's _NN numbering.
  layer    <transient> <texture> --out FILE [--gain-a 0 --gain-b -3 --offset 0]
      Mix an sfxr transient over an AI texture (peaks aligned, then + offset seconds).
  inspect  <in...> [--png FILE_OR_DIR] [--sheet]
      Print length / peak / loudness / silence / clipping / spectral centroid, and draw the
      waveform + spectrogram to PNG(s) — this is how an agent "listens".

STYLE (--style relaxed) — DEFAULT CANDIDATE for real recordings (Docs/ArtDirection.md §9 "Audio format
spec"): high-pass 60 Hz → proper anti-aliased resample to 22.05 kHz → 12-bit quantisation with TPDF
dither → gentle 12 dB/oct low-pass at 4.5 kHz (--lowpass) → back to 44.1 kHz. No fold, saturation or
clipping. Soft 4 ms attack, natural exponential tail (--fade, default 0.08 s for this style), optional
--room (tiny dark tail, --room-wet ≤ 0.10, default 0.08). Peaks: TRUE-PEAK ceiling -3 dBFS.

ROLES (--role) set the loudness target (max momentary, short-clip aware) + the -3 dBFS true-peak ceiling;
a clean look-ahead peak limiter (gain riding only, ≤ --limit dB, default 6) helps spiky clips reach it:
  footstep -26 · crouch -30 · action -22 (pickup/drop/jump/whoosh) · impact -20 (land/punch)
  ui -24 · idle -30 (idle creak) · ambient -30.  --lufs overrides the number.

STYLE (--style dark8bit) — legacy / stings (Docs/ArtDirection.md §9), dialled with --darkness 0..1
(default 0.5): high-pass 45 Hz, low shelf @160 Hz (+2 dB × darkness), high shelf @2.8 kHz
(-4 dB × darkness), optional --fold (0–1) toward a square wave, sample-and-hold decimation to
22050·2^-darkness Hz (≈15.6 kHz at 0.5; NO anti-aliasing — the aliasing is the point), quantisation to
round(10 - 2·darkness) bits (9 at 0.5), then a low-pass at 8000 - 3000·darkness Hz (6.5 kHz at 0.5).
--bits / --rate / --lowpass override single values. darkness 1.0 = the earlier, heavier pass.
Use it on everything, sfxr and Stable Audio alike.

STYLE (--style lofi, previous look, lighter): high-pass 70 Hz, sample-and-hold to ~22 kHz mixed 35 %, 12-bit crush mixed
25 %, gentle soft-clip. Same character on every sound so AI and procedural SFX sit together.
--room adds a very short (~0.25 s) dark stone-room tail at 12 % wet — leave it off for anything
that will play through EnvironmentReverb in-game (most 3D sounds); use it for UI/2D stingers.

LOUDNESS: "LUFS" here is the max momentary loudness (BS.1770 K-weighted, 400 ms window, or the clip
length if shorter) — the right measure for short one-shots. Peaks are always limited to -1 dBFS (the quieter wins).
"""
import argparse, glob, os, re, sys

import numpy as np
import soundfile as sf
from scipy.signal import lfilter, resample_poly, fftconvolve

SR = 44100


# ── I/O ──────────────────────────────────────────────────────────────────────────────────────

def load(path):
    x, sr = sf.read(path, dtype="float64", always_2d=True)
    if sr != SR:
        from math import gcd
        g = gcd(SR, sr)
        x = resample_poly(x, SR // g, sr // g, axis=0)
    return x  # (n, ch)


def save(path, x):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    sf.write(path, np.clip(x, -1, 1), SR, subtype="PCM_16")


def expand(paths):
    out = []
    for p in paths:
        out += sorted(glob.glob(p)) if any(c in p for c in "*?") else [p]
    return out


# ── Analysis ─────────────────────────────────────────────────────────────────────────────────

def _biquad(kind, f, q=0.707, gain_db=0.0):
    A = 10 ** (gain_db / 40)
    w = 2 * np.pi * f / SR
    cs, sn = np.cos(w), np.sin(w)
    al = sn / (2 * q)
    if kind == "hp":
        b, a = [(1 + cs) / 2, -(1 + cs), (1 + cs) / 2], [1 + al, -2 * cs, 1 - al]
    elif kind == "lp":
        b, a = [(1 - cs) / 2, 1 - cs, (1 - cs) / 2], [1 + al, -2 * cs, 1 - al]
    elif kind == "lowshelf":
        sq = 2 * np.sqrt(A) * al
        b = [A * ((A + 1) - (A - 1) * cs + sq), 2 * A * ((A - 1) - (A + 1) * cs), A * ((A + 1) - (A - 1) * cs - sq)]
        a = [(A + 1) + (A - 1) * cs + sq, -2 * ((A - 1) + (A + 1) * cs), (A + 1) + (A - 1) * cs - sq]
    elif kind == "highshelf":
        sq = 2 * np.sqrt(A) * al
        b = [A * ((A + 1) + (A - 1) * cs + sq), -2 * A * ((A - 1) + (A + 1) * cs), A * ((A + 1) + (A - 1) * cs - sq)]
        a = [(A + 1) - (A - 1) * cs + sq, 2 * ((A - 1) - (A + 1) * cs), (A + 1) - (A - 1) * cs - sq]
    else:
        raise ValueError(kind)
    b, a = np.array(b), np.array(a)
    return b / a[0], a / a[0]


def filt(x, kind, f, q=0.707, gain_db=0.0):
    b, a = _biquad(kind, f, q, gain_db)
    return lfilter(b, a, x, axis=0)


def momentary_lufs(x):
    """Max momentary loudness (K-weighted, channel-summed) in LUFS. The window is 400 ms, or the clip's
    own length when shorter (min 50 ms), so short one-shots are balanced by loudness, not by peak."""
    k = filt(x, "highshelf", 1681.97, 0.7072, 4.0)
    k = filt(k, "hp", 38.13, 0.5003)
    p = np.sum(k ** 2, axis=1)
    win = int(min(0.4, max(0.05, len(p) / SR)) * SR)
    if len(p) < win:
        p = np.pad(p, (0, win - len(p)))
    c = np.concatenate(([0.0], np.cumsum(p)))
    ms = (c[win:] - c[:-win]) / win
    return -0.691 + 10 * np.log10(np.max(ms) + 1e-12)


def db(v):
    return 20 * np.log10(max(v, 1e-12))


def silence_bounds(x, thresh_db=-50.0, hop=0.005):
    """Return (start, end) samples of the region above thresh_db relative to the peak."""
    m = np.max(np.abs(x), axis=1)
    peak = m.max() or 1.0
    above = np.where(m > peak * 10 ** (thresh_db / 20))[0]
    if len(above) == 0:
        return 0, len(x)
    return above[0], above[-1] + 1


# ── Processing ───────────────────────────────────────────────────────────────────────────────

def trim(x, thresh_db=-50, pre=0.004, fade=0.05):
    s, e = silence_bounds(x, thresh_db)
    s = max(0, s - int(pre * SR))
    x = x[s:e].copy()
    k = min(int(0.002 * SR), len(x) // 4)        # 2 ms anti-click fade-in
    if k:
        x[:k] *= np.linspace(0, 1, k)[:, None]
    f = min(int(fade * SR), len(x) // 2)         # fade-out tail
    if f:
        x[-f:] *= (np.linspace(1, 0, f) ** 2)[:, None]
    return x


def style_lofi(x, rng):
    x = filt(x, "hp", 70.0)
    # sample-and-hold decimation (aliasing crunch), mixed in
    hold = 2
    held = np.repeat(x[::hold], hold, axis=0)[:len(x)]
    x = 0.65 * x + 0.35 * held
    # 12-bit crush, mixed in
    q = 2 ** 11
    x = 0.75 * x + 0.25 * (np.round(x * q) / q)
    # gentle soft-clip for a bit of dirt, then tame the fizz above 12 kHz
    x = np.tanh(1.4 * x) / np.tanh(1.4)
    return filt(x, "lp", 12000.0, 0.6)


def dark8bit_params(darkness=0.5, bits=None, rate=None, lowpass=None):
    """Map the 0..1 `darkness` dial to the pass settings. 1.0 = the original heavy pass
    (8 bit, 11025 Hz, LP 5 kHz, -4 dB top); 0.5 = house default (≈9 bit, ≈15.6 kHz, LP 6.5 kHz,
    -2 dB top); 0 = light (10 bit, 22050 Hz, LP 8 kHz, flat top). Explicit values override."""
    d = float(np.clip(darkness, 0.0, 1.0))
    return dict(bits=bits if bits else int(round(10 - 2 * d)),
                rate=rate if rate else 22050 * 2 ** (-d),
                lowpass=lowpass if lowpass else 8000 - 3000 * d,
                top_db=-4.0 * d,
                low_db=2.0 * d)


def style_dark8bit(x, rng, bits=8, rate=11025, lowpass=5000.0, fold=0.0, top_db=-4.0, low_db=2.0):
    """8-bit console pass: EQ → optional square fold → sample-and-hold decimation to `rate`
    (no anti-aliasing, on purpose) → `bits` quantisation → low-pass to tame the top.
    Use dark8bit_params(darkness) to get the settings."""
    x = filt(x, "hp", 45.0)
    if low_db:
        x = filt(x, "lowshelf", 160.0, 0.7, low_db)     # a little weight in the thuds
    if top_db:
        x = filt(x, "highshelf", 2800.0, 0.7, top_db)   # darker top
    x = x / (np.max(np.abs(x)) or 1.0)
    if fold > 0:                                    # push toward a square wave (NES-ish buzz)
        x = (1 - fold) * x + fold * np.sign(x) * np.abs(x) ** 0.25
    # sample-and-hold decimation with a fractional step: hold each value for SR/rate samples
    idx = (np.floor(np.arange(len(x)) * rate / SR) * SR / rate).astype(np.int64)
    x = x[np.minimum(idx, len(x) - 1)]
    q = 2 ** (bits - 1)
    x = np.round(x * q) / q
    return filt(x, "lp", lowpass, 0.6)


def style_relaxed(x, rng, lowpass=4500.0, bits=12, rate=22050):
    """Soft retro pass for real recordings: HP 60 Hz, anti-aliased resample to `rate`, `bits` quantisation
    with TPDF dither, 12 dB/oct low-pass, back to 44.1 kHz. No saturation of any kind."""
    from math import gcd
    x = filt(x, "hp", 60.0)
    g = gcd(SR, int(rate))
    y = resample_poly(x, int(rate) // g, SR // g, axis=0)          # band-limited, no aliasing
    peak = np.max(np.abs(y)) or 1.0
    y = y / peak
    q = 2 ** (bits - 1)
    tpdf = (rng.random(y.shape) - rng.random(y.shape)) / q          # ±1 LSB triangular dither
    y = np.round((y + tpdf) * q) / q * peak
    y = resample_poly(y, SR // g, int(rate) // g, axis=0)
    y = filt(y, "lp", lowpass, 0.707)                               # 12 dB/oct
    return y


def true_peak(x):
    """4× oversampled peak (dBTP estimate, linear)."""
    return float(np.max(np.abs(resample_poly(x, 4, 1, axis=0)))) if len(x) > 8 else float(np.max(np.abs(x)))


ROLE_LUFS = {"footstep": -26.0, "crouch": -30.0, "action": -22.0, "impact": -20.0,
             "ui": -24.0, "idle": -30.0, "ambient": -30.0}


def room_tail(x, rng, length=0.25, wet=0.12):
    n = int(length * SR)
    ir = rng.standard_normal(n) * np.exp(-np.linspace(0, 7, n))
    ir = lfilter(*_biquad("lp", 3000.0), ir)
    ir /= np.sqrt(np.sum(ir ** 2))
    tail = np.stack([fftconvolve(x[:, c], ir)[:len(x) + n] for c in range(x.shape[1])], axis=1)
    dry = np.pad(x, ((0, n), (0, 0)))
    return (1 - wet) * dry + wet * tail


def peak_limit(x, ceiling, max_gr_db=6.0, lookahead=0.005, release=0.08):
    """Clean look-ahead peak limiter (gain riding only — no clipping/saturation). Reduces at most
    `max_gr_db`, so very spiky sounds still end up a little under their loudness target."""
    from scipy.ndimage import minimum_filter1d
    a = np.max(np.abs(x), axis=1)
    need = np.minimum(1.0, ceiling / np.maximum(a, 1e-9))
    need = np.maximum(need, 10 ** (-max_gr_db / 20))
    la = max(1, int(lookahead * SR))
    g = minimum_filter1d(need, size=2 * la + 1, origin=0, mode="nearest")   # start reducing before the peak
    k = 1 - np.exp(-1 / (release * SR))
    out = np.empty_like(g)
    cur = 1.0
    for i, v in enumerate(g):
        cur = v if v < cur else cur + (1 - cur) * k
        out[i] = min(cur, 1.0)
    return x * out[:, None]


def normalise(x, lufs=-16.0, ceiling_db=-1.0, use_true_peak=False, limit_db=0.0):
    ceil = 10 ** (ceiling_db / 20)
    if use_true_peak and limit_db > 0:
        # reach the loudness target first, let the limiter catch up to `limit_db` of the peaks
        x = x * 10 ** ((lufs - momentary_lufs(x)) / 20)
        if true_peak(x) > ceil:
            x = peak_limit(x, ceil * 0.97, limit_db)
    g_loud = 10 ** ((lufs - momentary_lufs(x)) / 20)
    pk = true_peak(x) if use_true_peak else np.max(np.abs(x))
    g_peak = ceil / (pk or 1.0)
    return x * min(g_loud, g_peak)


def process_one(x, a, rng):
    if a.mono and x.shape[1] > 1:
        x = x.mean(axis=1, keepdims=True)
    fade = a.fade if a.fade is not None else (0.08 if a.style == "relaxed" else 0.05)
    x = trim(x, a.thresh, fade=fade)
    if a.style == "relaxed":                       # softer attack than the 2 ms default
        k = min(int(0.004 * SR), len(x) // 4)
        if k:
            x[:k] *= np.linspace(0, 1, k)[:, None]
    if a.max_len and len(x) > a.max_len * SR:
        x = x[:int(a.max_len * SR)]
        f = min(int(fade * SR), len(x) // 2)
        if a.style == "relaxed":                   # natural (exponential) decay instead of a hard cut
            x[-f:] *= np.exp(-np.linspace(0, 5, f))[:, None]
        else:
            x[-f:] *= (np.linspace(1, 0, f) ** 2)[:, None]
    if a.style == "relaxed":
        x = style_relaxed(x, rng, a.lowpass or 4500.0)
    elif a.style == "lofi":
        x = style_lofi(x, rng)
    elif a.style == "dark8bit":
        x = style_dark8bit(x, rng, fold=a.fold, **dark8bit_params(a.darkness, a.bits, a.rate, a.lowpass))
    if a.room:
        x = room_tail(x, rng, wet=min(a.room_wet, 0.10) if a.style == "relaxed" else 0.12)
        x = trim(x, -60, pre=0, fade=0.02)
    if a.role:
        return normalise(x, a.lufs if a.lufs is not None else ROLE_LUFS[a.role], -3.0, use_true_peak=True,
                         limit_db=getattr(a, "limit", 6.0))
    return normalise(x, a.lufs if a.lufs is not None else -16.0)


def out_name(a, src, i):
    if a.category and a.name:
        return os.path.join(a.out, f"SFX_{a.category}_{a.name}{a.suffix}_{i + 1:02d}.wav")
    stem = os.path.splitext(os.path.basename(src))[0]
    return os.path.join(a.out, f"{stem}{a.suffix}.wav")


def cmd_process(a):
    rng = np.random.default_rng(a.seed)
    for i, p in enumerate(expand(a.inputs)):
        y = process_one(load(p), a, rng)
        dst = out_name(a, p, i + a.start - 1)
        save(dst, y)
        report(dst, y)


def onsets(x, min_gap=0.06, sens=6.0):
    """Energy-flux onset detection on a mono mix. Returns onset sample indices."""
    m = x.mean(axis=1)
    hop, win = int(0.005 * SR), int(0.02 * SR)
    frames = np.lib.stride_tricks.sliding_window_view(np.pad(m, (0, win)), win)[::hop]
    e = 10 * np.log10(np.mean(frames ** 2, axis=1) + 1e-10)
    floor = np.percentile(e, 20)
    peak = e.max()
    d = np.diff(e, prepend=e[0])
    cand = np.where((d > sens) & (e > max(floor + 12, peak - 40)))[0]
    out, last = [], -1e9
    for c in cand:
        if (c - last) * hop >= min_gap * SR:
            out.append(max(0, c * hop - int(0.01 * SR)))
            last = c
    return out


def cmd_split(a):
    rng = np.random.default_rng(a.seed)
    x = load(a.inputs[0])
    on = onsets(x, a.min_gap, a.sens)
    if not on:
        sys.exit("no onsets found — try a lower --sens")
    bounds = list(zip(on, on[1:] + [len(x)]))[:a.max_hits]
    print(f"{len(on)} onsets found, writing {len(bounds)}")
    for i, (s, e) in enumerate(bounds):
        y = process_one(x[s:e], a, rng)
        if len(y) < 0.03 * SR:
            continue
        dst = out_name(a, a.inputs[0], i + a.start - 1)
        save(dst, y)
        report(dst, y)


def cmd_vary(a):
    rng = np.random.default_rng(a.seed)
    src = a.inputs[0]
    x = load(src)
    stem = os.path.splitext(os.path.basename(src))[0]
    m = re.match(r"(.*)_(\d+)$", stem)
    base, n0 = (m.group(1), int(m.group(2))) if m else (stem, 1)
    for i in range(a.count):
        y = x
        if i > 0:
            semis = rng.uniform(-a.pitch, a.pitch)
            ratio = 2 ** (semis / 12)
            idx = np.arange(0, len(x) - 1, ratio)
            y = np.stack([np.interp(idx, np.arange(len(x)), x[:, c]) for c in range(x.shape[1])], axis=1)
            y = filt(y, "lowshelf", 250.0, 0.7, rng.uniform(-a.eq, a.eq))
            y = filt(y, "highshelf", 3500.0, 0.7, rng.uniform(-a.eq, a.eq))
            y = normalise(y, momentary_lufs(x))
        dst = os.path.join(a.out, f"{base}_{n0 + i:02d}.wav")
        save(dst, y)
        report(dst, y)


def cmd_layer(a):
    t, b = load(a.inputs[0]), load(a.inputs[1])
    ch = max(t.shape[1], b.shape[1])
    t, b = (np.repeat(v, ch, axis=1) if v.shape[1] < ch else v for v in (t, b))
    off = int(np.argmax(np.abs(b).max(1)) - np.argmax(np.abs(t).max(1)) + a.offset * SR)
    if off < 0:
        b, off = np.pad(b, ((-off, 0), (0, 0))), 0
    n = max(len(b), off + len(t))
    mix = np.zeros((n, ch))
    mix[:len(b)] += b * 10 ** (a.gain_b / 20)
    mix[off:off + len(t)] += t * 10 ** (a.gain_a / 20)
    mix = normalise(mix, momentary_lufs(mix))
    save(a.out, mix)
    report(a.out, mix)


# ── Inspect ──────────────────────────────────────────────────────────────────────────────────

def stats(x):
    s, e = silence_bounds(x, -50)
    m = x.mean(axis=1)
    spec = np.abs(np.fft.rfft(m * np.hanning(len(m)))) if len(m) > 16 else np.ones(2)
    fr = np.fft.rfftfreq(len(m), 1 / SR) if len(m) > 16 else np.ones(2)
    return dict(len=len(x) / SR, ch=x.shape[1], peak=db(np.max(np.abs(x))), lufs=momentary_lufs(x),
                lead=s / SR, tail=(len(x) - e) / SR, clip=int(np.sum(np.abs(x) >= 0.999)),
                centroid=float(np.sum(fr * spec) / (np.sum(spec) + 1e-12)))


def report(path, x):
    s = stats(x)
    print(f"{path}  {s['len']:.2f}s {s['ch']}ch peak {s['peak']:.1f} dBFS  {s['lufs']:.1f} LUFS(m)  "
          f"silence {s['lead'] * 1000:.0f}/{s['tail'] * 1000:.0f} ms  clip {s['clip']}  centroid {s['centroid']:.0f} Hz")


def plot(ax_w, ax_s, x, title):
    m = x.mean(axis=1)
    t = np.arange(len(m)) / SR
    ax_w.plot(t, m, lw=0.5, color="#C2502A")
    ax_w.set_ylim(-1, 1)
    ax_w.set_xlim(0, max(t[-1], 0.01))
    ax_w.set_title(title, fontsize=8, loc="left")
    ax_w.axhline(10 ** (-1 / 20), color="grey", lw=0.4, ls="--")
    ax_w.axhline(-10 ** (-1 / 20), color="grey", lw=0.4, ls="--")
    ax_s.specgram(m + np.random.default_rng(0).normal(0, 1e-6, len(m)), NFFT=512, Fs=SR, noverlap=384, cmap="magma", vmin=-120)
    ax_s.set_ylim(0, 16000)
    ax_s.set_ylabel("Hz", fontsize=7)
    ax_s.tick_params(labelsize=6)
    ax_w.tick_params(labelsize=6)


def cmd_inspect(a):
    files = expand(a.inputs)
    xs = [load(p) for p in files]
    for p, x in zip(files, xs):
        report(p, x)
    if not a.png:
        return
    import matplotlib
    matplotlib.use("Agg")
    import matplotlib.pyplot as plt
    if a.sheet or len(files) == 1:
        fig, axes = plt.subplots(len(files), 2, figsize=(11, 1.6 * len(files) + 0.4), squeeze=False)
        for (aw, asp), p, x in zip(axes, files, xs):
            s = stats(x)
            plot(aw, asp, x, f"{os.path.basename(p)}  {s['len']:.2f}s  {s['peak']:.1f}dBFS  {s['lufs']:.1f}LUFS")
        fig.tight_layout()
        dst = a.png if a.png.lower().endswith(".png") else os.path.join(a.png, "sheet.png")
        os.makedirs(os.path.dirname(os.path.abspath(dst)), exist_ok=True)
        fig.savefig(dst, dpi=90)
        print(f"plot: {dst}")
    else:
        os.makedirs(a.png, exist_ok=True)
        for p, x in zip(files, xs):
            fig, (aw, asp) = plt.subplots(2, 1, figsize=(8, 4))
            plot(aw, asp, x, os.path.basename(p))
            fig.tight_layout()
            dst = os.path.join(a.png, os.path.splitext(os.path.basename(p))[0] + ".png")
            fig.savefig(dst, dpi=90)
            plt.close(fig)
            print(f"plot: {dst}")


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("command", choices=["process", "split", "vary", "layer", "inspect"])
    ap.add_argument("inputs", nargs="+")
    ap.add_argument("--out")
    ap.add_argument("--category")
    ap.add_argument("--name")
    ap.add_argument("--suffix", default="")
    ap.add_argument("--start", type=int, default=1, help="first _NN index")
    ap.add_argument("--mono", action="store_true")
    ap.add_argument("--style", choices=["clean", "relaxed", "lofi", "dark8bit"], default="clean")
    ap.add_argument("--role", choices=sorted(ROLE_LUFS),
                    help="loudness target by role + -3 dBFS true-peak ceiling (see ROLES above)")
    ap.add_argument("--room-wet", type=float, default=0.08, help="relaxed: room tail wet amount (max 0.10)")
    ap.add_argument("--limit", type=float, default=6.0,
                    help="with --role: max dB of clean look-ahead peak limiting to reach the target (0 = off)")
    ap.add_argument("--darkness", type=float, default=0.5,
                    help="dark8bit intensity 0..1 (house default 0.5; 1 = the original heavy pass)")
    ap.add_argument("--bits", type=int, help="dark8bit: override bit depth")
    ap.add_argument("--rate", type=float, help="dark8bit: override hold rate in Hz")
    ap.add_argument("--lowpass", type=float, help="dark8bit: override final low-pass Hz")
    ap.add_argument("--fold", type=float, default=0.0, help="dark8bit: 0–1 square-wave fold amount")
    ap.add_argument("--room", action="store_true")
    ap.add_argument("--lufs", type=float, help="loudness target (default -16, or the --role target)")
    ap.add_argument("--thresh", type=float, default=-45.0, help="silence threshold, dB below peak")
    ap.add_argument("--fade", type=float, help="fade-out tail seconds (default 0.05; relaxed 0.08)")
    ap.add_argument("--max-len", type=float, default=0.0)
    ap.add_argument("--seed", type=int, default=0)
    ap.add_argument("--min-gap", type=float, default=0.06)
    ap.add_argument("--sens", type=float, default=6.0, help="split: dB rise per 5 ms that counts as an onset")
    ap.add_argument("--max-hits", type=int, default=12)
    ap.add_argument("--count", type=int, default=4)
    ap.add_argument("--pitch", type=float, default=0.6)
    ap.add_argument("--eq", type=float, default=2.0)
    ap.add_argument("--gain-a", type=float, default=0.0)
    ap.add_argument("--gain-b", type=float, default=-3.0)
    ap.add_argument("--offset", type=float, default=0.0)
    ap.add_argument("--png")
    ap.add_argument("--sheet", action="store_true")
    a = ap.parse_args()
    if a.command != "inspect" and not a.out:
        sys.exit("--out is required")
    {"process": cmd_process, "split": cmd_split, "vary": cmd_vary,
     "layer": cmd_layer, "inspect": cmd_inspect}[a.command](a)


if __name__ == "__main__":
    main()
