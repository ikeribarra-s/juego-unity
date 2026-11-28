"""
make_salt_sfx.py - procedural salt sounds for the salt bag (Assets/_Game/Audio/SFX/Items/), house style = soft + dark
(`relaxed` one-shots, loop-safe pour). Seeds fixed, so re-running reproduces the files.

  C:\\Users\\redux\\ComfyUI\\.venv\\Scripts\\python.exe -I Tools/Audio/make_salt_sfx.py [--out <dir>]

  SFX_Items_SaltPour_01.wav      1.6 s seamless loop: soft hiss + a dense rain of tiny grain ticks   (-31 LUFS)
  SFX_Items_SaltShake_01..03.wav ~0.4 s cloth rustle + a few grains: the bag is lifted / tilted        (-26 LUFS)
  SFX_Items_SaltLand_01..04.wav  ~0.12 s tiny granular tick-cluster where the salt hits the floor      (-30 LUFS)
"""
import argparse, os, sys
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import numpy as np                 # noqa: E402
from scipy.signal import fftconvolve  # noqa: E402
import process_sfx as P            # noqa: E402
import remaster_live as R          # noqa: E402

SR = P.SR


def kernel(rng, ms, decay, hp=None):
    n = max(8, int(SR * ms / 1000))
    k = rng.standard_normal(n) * np.exp(-np.linspace(0, decay, n))
    if hp:
        k = P.filt(k[:, None], "hp", hp)[:, 0]
    return k


def ticks(rng, n, rate, amp=(0.2, 1.0), circular=False, t0=0.0, t1=None):
    """Poisson impulses (rate /s) convolved with a few different short noise-burst kernels."""
    t1 = n / SR if t1 is None else t1
    count = rng.poisson(rate * (t1 - t0))
    pos = rng.integers(int(t0 * SR), max(int(t0 * SR) + 1, int(t1 * SR)), size=count)
    a = rng.uniform(*amp, size=count) ** 2
    out = np.zeros(n)
    kernels = [kernel(rng, 2.5, 6.0, 1500), kernel(rng, 4.0, 7.0, 2500), kernel(rng, 1.8, 5.0, 3500)]
    for ki, k in enumerate(kernels):
        imp = np.zeros(n)
        sel = (np.arange(count) % 3) == ki
        np.add.at(imp, pos[sel] % n, a[sel] * rng.choice([-1.0, 1.0], size=int(sel.sum())))
        if circular:
            tiled = np.concatenate([imp, imp, imp])
            out += fftconvolve(tiled, k)[n:2 * n]
        else:
            out += fftconvolve(imp, k)[:n]
    return out


def hiss(rng, n, lo=1200.0, hi=6500.0, circular=True):
    x = rng.standard_normal(n * 3 if circular else n)[:, None]
    x = P.filt(x, "hp", lo); x = P.filt(x, "lp", hi)
    x = x[:, 0]
    return x[n:2 * n] if circular else x


def pour_loop(seed, dur=1.6):
    rng = np.random.default_rng(seed)
    n = int(dur * SR)
    t = np.arange(n) / SR
    wob = 1.0 + 0.18 * np.sin(2 * np.pi * 3.0 * t / dur * 1.0 + 0.7) + 0.10 * np.sin(2 * np.pi * 7.0 * t / dur + 2.1)  # integer cycles = seamless
    h = hiss(rng, n) * 0.30 * wob
    g = ticks(rng, n, 1400, circular=True) * 1.0
    x = (h + g)[:, None]
    return x / np.max(np.abs(x))


def shake(seed, dur=0.42):
    rng = np.random.default_rng(seed)
    n = int(dur * SR)
    t = np.arange(n) / SR
    cloth = hiss(rng, n, 250.0, 2600.0, circular=False)
    env = np.zeros(n)
    for c, w, a in zip(rng.uniform(0.05, dur - 0.1, 3), rng.uniform(0.03, 0.08, 3), rng.uniform(0.5, 1.0, 3)):
        env += a * np.exp(-0.5 * ((t - c) / w) ** 2)
    g = ticks(rng, n, 260, t0=0.04, t1=dur * 0.8) * 0.9 * np.minimum(1.0, env + 0.2)
    x = (cloth * env * 0.8 + g)[:, None]
    return x / np.max(np.abs(x))


def land(seed, dur=0.13):
    rng = np.random.default_rng(seed)
    n = int(dur * SR)
    t = np.arange(n) / SR
    g = ticks(rng, n, 420, amp=(0.3, 1.0), t0=0.0, t1=dur * 0.7) * np.exp(-t / 0.05)
    x = (g + hiss(rng, n, 1500.0, 5000.0, circular=False) * np.exp(-t / 0.03) * 0.25)[:, None]
    return x / np.max(np.abs(x))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", default=os.path.join(R.SFX, "Items"))
    a = ap.parse_args()
    rng = np.random.default_rng(31000)

    y = R.loop_safe(pour_loop(31000), -31, 4800)
    dst = os.path.join(a.out, "SFX_Items_SaltPour_01.wav"); P.save(dst, y); P.report(dst, y)

    for i in range(3):
        x = shake(31100 + i)
        y = P.process_one(x, R.opts(-26, 3800, None, 0.08), rng)
        dst = os.path.join(a.out, f"SFX_Items_SaltShake_{i + 1:02d}.wav"); P.save(dst, y); P.report(dst, y)

    for i in range(4):
        x = land(31200 + i)
        y = P.process_one(x, R.opts(-30, 4200, None, 0.04), rng)
        dst = os.path.join(a.out, f"SFX_Items_SaltLand_{i + 1:02d}.wav"); P.save(dst, y); P.report(dst, y)


if __name__ == "__main__":
    main()
