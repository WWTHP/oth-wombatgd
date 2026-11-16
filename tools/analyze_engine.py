"""Estimate engine firing frequency over time in a recording.

Diesel clatter is a train of combustion knocks, so the autocorrelation of the
amplitude envelope peaks at the firing period. RPM = firing_hz * 120 / cylinders.

Usage: python analyze_engine.py <audio> [window_s=0.5]
Prints: time, rms_db, firing_hz, confidence
"""

import subprocess
import sys

import numpy as np

SR = 8000


def decode(path: str) -> np.ndarray:
    raw = subprocess.run(
        ["ffmpeg", "-v", "error", "-i", path, "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"],
        check=True,
        capture_output=True,
    ).stdout
    return np.frombuffer(raw, dtype=np.float32)


def firing(env: np.ndarray, esr: int, lo_hz=8.0, hi_hz=120.0):
    env = env - env.mean()
    if not env.any():
        return 0.0, 0.0
    ac = np.correlate(env, env, "full")[len(env) - 1 :]
    ac /= ac[0]
    lo, hi = int(esr / hi_hz), int(esr / lo_hz)
    seg = ac[lo:hi]
    i = int(np.argmax(seg))
    lag = lo + i
    # prefer the shortest lag with a comparably strong peak (avoid sub-harmonics)
    for div in (4, 3, 2):
        l2 = round(lag / div)
        if l2 >= lo:
            w = ac[max(lo, l2 - 2) : l2 + 3]
            if w.max() > 0.85 * seg[i]:
                lag = max(lo, l2 - 2) + int(np.argmax(w))
                break
    return esr / lag, float(ac[lag])


def main() -> None:
    path = sys.argv[1]
    win = float(sys.argv[2]) if len(sys.argv) > 2 else 0.5
    x = decode(path)
    # envelope: high-pass-ish (diff) -> rectify -> 1 ms smoothing -> 1 kHz
    hp = np.diff(x, prepend=x[0])
    env = np.abs(hp)
    k = 8
    env = np.convolve(env, np.ones(k) / k, "same")[::8]
    esr = SR // 8
    n = int(win * esr)
    for s in range(0, len(env) - n, n):
        seg = env[s : s + n]
        t = s / esr
        xs = x[int(t * SR) : int((t + win) * SR)]
        rms = 20 * np.log10(np.sqrt(np.mean(xs**2)) + 1e-9)
        hz, conf = firing(seg, esr)
        print(f"{t:6.1f}s  {rms:6.1f} dB  {hz:6.1f} Hz  conf {conf:4.2f}")


if __name__ == "__main__":
    main()
