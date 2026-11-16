"""Procedural Mercedes OM617 (3.0 L naturally aspirated inline-5 diesel) engine loops.

Renders a bank of seamless loops at fixed RPMs, on throttle (fuelled: combustion
knock, strong exhaust pulses) and off throttle (coasting: no injection, just
pumping and mechanical noise). The mod crossfades between neighbouring RPMs and
pitches each loop only slightly, so every speed sounds like a real engine
running at that speed rather than one recording stretched.

What gives an OM617 its character, and how it is modelled:
  * 5 cylinders, firing order 1-2-4-5-3, one firing every 144 deg of crank:
    firing rate = rpm / 24 Hz (29 Hz at idle).
  * Small cylinder-to-cylinder differences (fuel delivery, manifold runner
    length) give the half-order "warble" of a five.
  * Diesel combustion knock: each firing is a sharp pressure rise that rings
    the block - the Mercedes clatter. Strong at idle, dominant under load.
  * Injector and valvetrain ticking on top.
  * Exhaust: pressure pulses through a downpipe and muffler (low-passed, with
    pipe resonances). Intake: a breathy roar that grows with RPM and throttle.

Every stage runs on a circular buffer exactly N engine cycles long, and all
filtering is done in the frequency domain on that buffer, so loops are
seamless by construction.

Usage: python synth_om617.py <out_dir> [--eq eq.npy] [--prefix om617]
"""

import argparse
import subprocess

import numpy as np

SR = 48000
RPMS = [700, 1000, 1400, 1900, 2500, 3200, 4000, 4700]
LOOP_SECONDS = 3.0
FIRING_ORDER = [1, 2, 4, 5, 3]
# Per-cylinder fuelling/combustion/manifold differences (index = cylinder - 1).
CYL_FUEL = np.array([1.00, 0.88, 1.07, 0.94, 1.10])
CYL_KNOCK = np.array([1.00, 1.18, 0.86, 1.08, 0.93])
CYL_DELAY = np.array([0.000, 0.018, -0.012, 0.025, -0.008])  # fraction of firing interval
REDLINE = 4700.0


def band(freqs, lo, hi, order=2):
    """Magnitude of a smooth band-pass (Butterworth-like) response."""
    f = np.maximum(freqs, 1e-3)
    hp = 1.0 / np.sqrt(1.0 + (lo / f) ** (2 * order)) if lo else 1.0
    lp = 1.0 / np.sqrt(1.0 + (f / hi) ** (2 * order)) if hi else 1.0
    return hp * lp


def peak(freqs, f0, q, gain_db):
    """Resonant peak (gain at f0, unity far away)."""
    g = 10 ** (gain_db / 20.0) - 1.0
    x = (freqs - f0) / (f0 / q)
    return 1.0 + g / (1.0 + x * x)


def circ_filter(x, resp_fn):
    X = np.fft.rfft(x)
    freqs = np.fft.rfftfreq(len(x), 1.0 / SR)
    return np.fft.irfft(X * resp_fn(freqs), len(x))


def place(n, times, amps, kernel):
    """Circularly add `kernel` at sample times (fractional) with amplitudes."""
    out = np.zeros(n)
    k = len(kernel)
    for t, a in zip(times, amps, strict=True):
        i = int(np.floor(t))
        frac = t - i
        # linear-interpolated placement keeps sub-sample timing
        idx = (np.arange(k) + i) % n
        out[idx] += a * (1 - frac) * kernel
        idx2 = (idx + 1) % n
        out[idx2] += a * frac * kernel
    return out


def render(rpm: float, load: float, rng: np.random.Generator) -> np.ndarray:
    cycle = 120.0 / rpm  # one 4-stroke cycle, s
    ncyc = max(1, round(LOOP_SECONDS / cycle))
    n = round(ncyc * cycle * SR)
    nev = ncyc * 5
    interval = n / nev  # samples between firings
    r = rpm / REDLINE
    idle_lump = 0.12 * (1.0 - r) + 0.03  # diesels idle lumpy

    times, cyl = [], []
    for e in range(nev):
        c = FIRING_ORDER[e % 5] - 1
        cyl.append(c)
        jitter = rng.normal(0, 0.006 + 0.01 * (1 - r))
        times.append((e + CYL_DELAY[c] + jitter) * interval)
    times = np.array(times) % n
    cyl = np.array(cyl)
    var = 1.0 + rng.normal(0, idle_lump, nev)

    # --- exhaust pressure pulses ---
    tau = np.clip(0.22 * interval / SR, 0.0012, 0.007)
    tk = np.arange(int(tau * SR * 8)) / SR
    pulse = (tk / tau) * np.exp(1 - tk / tau)
    pulse -= pulse.mean()  # pressure returns to ambient
    fuel = 0.22 + 0.78 * load
    ex_amp = CYL_FUEL[cyl] ** (1.5 * load + 0.3) * var * fuel
    exhaust = place(n, times, ex_amp, pulse)
    exhaust = circ_filter(
        exhaust,
        lambda f: (
            band(f, 40, 520 + 400 * r + 200 * load, 2)
            * peak(f, 92, 3.0, 3)
            * peak(f, 260, 2.5, 6)
            * peak(f, 450, 3.0, 5)
        ),
    )
    exhaust *= 0.30 + 0.7 * r

    # --- combustion knock (the clatter) ---
    knock = np.zeros(n)
    if load > 0:
        kt = np.arange(int(0.006 * SR)) / SR
        for t, c, v in zip(times, cyl, var, strict=True):
            burst = rng.normal(0, 1, len(kt)) * np.exp(-kt / 0.0011)
            amp = CYL_KNOCK[c] * (0.7 + 0.3 * v) * load
            knock += place(n, [t + 0.05 * interval], [amp], burst)
        knock = circ_filter(
            knock,
            lambda f: band(f, 600, 5200, 2) * peak(f, 1700, 4, 6) * peak(f, 2400, 5, 7) * peak(f, 3500, 6, 5),
        )
        knock *= 0.55 + 0.35 * (1 - r)  # clatter stands out most at low revs

    # --- injector ticks ---
    tick = np.zeros(n)
    tt = np.arange(int(0.0015 * SR)) / SR
    tick_k = np.sin(2 * np.pi * 6400 * tt) * np.exp(-tt / 0.00035)
    tick = place(n, times - 0.12 * interval, np.full(nev, 0.6 if load > 0 else 0.15), tick_k)
    tick = circ_filter(tick, lambda f: band(f, 3000, 9000, 2))

    # --- valvetrain / mechanical: 10 valve events per cycle, plus a bed of noise ---
    vt = np.arange(int(0.003 * SR)) / SR
    valve_k = rng.normal(0, 1, len(vt)) * np.exp(-vt / 0.0006)
    vtimes = (np.arange(ncyc * 10) * (n / (ncyc * 10)) + 0.31 * interval) % n
    valves = place(n, vtimes, rng.uniform(0.6, 1.0, len(vtimes)), valve_k)
    mech = valves * 0.5 + rng.normal(0, 0.06, n)
    mech = circ_filter(mech, lambda f: band(f, 1800, 7000, 2) * peak(f, 2800, 3, 4))
    mech *= 0.25 + 0.75 * r

    # --- intake roar, pulsing with the firings ---
    phase = (np.arange(n) / interval) * 2 * np.pi
    breath = rng.normal(0, 1, n) * (1.0 + 0.6 * np.cos(phase))
    intake = circ_filter(breath, lambda f: band(f, 120, 900, 2) * peak(f, 380, 2, 4))
    intake *= (0.05 + 0.95 * r**1.6) * (0.35 + 0.65 * load)

    # --- mix ---
    mix = 1.00 * exhaust / (np.std(exhaust) + 1e-9) * 0.30 * (0.35 + 0.65 * r) * fuel
    if load > 0:
        mix = mix + 0.20 * knock / (np.std(knock) + 1e-9) * (0.55 + 0.45 * load)
    mix = mix + 0.05 * tick / (np.std(tick) + 1e-9)
    mix = mix + 0.06 * mech / (np.std(mech) + 1e-9) * (0.3 + 0.7 * r)
    mix = mix + 0.10 * intake / (np.std(intake) + 1e-9) * (0.15 + 0.85 * r**1.4) * (0.4 + 0.6 * load)
    return mix


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("out_dir")
    ap.add_argument("--eq", help="npy file: [freqs, gain] correction curve to apply")
    ap.add_argument("--prefix", default="om617")
    ap.add_argument("--rpms", default=",".join(map(str, RPMS)))
    args = ap.parse_args()
    rng = np.random.default_rng(617)
    rpms = [float(x) for x in args.rpms.split(",")]

    eq = np.load(args.eq) if args.eq else None
    loops = {}
    for rpm in rpms:
        for name, load in (("on", 1.0), ("off", 0.0)):
            x = render(rpm, load, rng)
            if eq is not None:
                x = circ_filter(x, lambda f: np.interp(f, eq[0], eq[1]))
            # Loudness: about 6 dB more at redline than at idle; coasting a bit
            # quieter than fuelled rather than the raw model's ~13 dB.
            x = x * 10 ** ((-6.0 * (1.0 - rpm / REDLINE) + (4.0 if load == 0 else 0.0)) / 20.0)
            loops[(rpm, name)] = x
    peak_all = max(np.max(np.abs(x)) for x in loops.values())
    for (rpm, name), x in loops.items():
        y = x / peak_all * 0.95
        pcm = (y * 32767).astype("<i2").tobytes()
        dst = f"{args.out_dir}/{args.prefix}_{name}_{int(rpm)}.wav"
        subprocess.run(
            ["ffmpeg", "-v", "error", "-y", "-f", "s16le", "-ar", str(SR), "-ac", "1", "-i", "-", dst],
            input=pcm,
            check=True,
        )
        print(f"{dst}: {len(y) / SR:.2f}s rms {20 * np.log10(np.std(y) + 1e-9):.1f} dBFS")


if __name__ == "__main__":
    main()
