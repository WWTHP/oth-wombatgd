"""Derive an EQ curve that makes the synth's average spectrum match a reference.

Usage: python match_spectrum.py <ref_audio> <ref_start> <ref_len> <synth.wav> <out_eq.npy> [plot.png]
The curve is 1/3-octave smoothed, level-matched, limited to +/-8 dB, flat below 50 Hz.
"""

import subprocess
import sys

import numpy as np

SR = 48000
LIMIT = 8.0  # dB; the references are phone/YouTube recordings, so only trust broad shape


def load(path, start=None, length=None):
    cmd = ["ffmpeg", "-v", "error"]
    if start is not None:
        cmd += ["-ss", str(start), "-t", str(length)]
    cmd += ["-i", path, "-ac", "1", "-ar", str(SR), "-f", "f32le", "-"]
    return np.frombuffer(subprocess.run(cmd, check=True, capture_output=True).stdout, np.float32)


def ltas(x, n=8192):
    w = np.hanning(n)
    frames = [x[i : i + n] * w for i in range(0, len(x) - n, n // 2)]
    p = np.mean([np.abs(np.fft.rfft(f)) ** 2 for f in frames], axis=0)
    return np.fft.rfftfreq(n, 1 / SR), p


def smooth_oct(f, p, frac=3):
    out = np.empty_like(p)
    for i, fc in enumerate(f):
        lo, hi = fc * 2 ** (-0.5 / frac), fc * 2 ** (0.5 / frac)
        m = (f >= lo) & (f <= hi)
        out[i] = p[m].mean() if m.any() else p[i]
    return out


def main():
    ref, rs, rl, syn, out = sys.argv[1], float(sys.argv[2]), float(sys.argv[3]), sys.argv[4], sys.argv[5]
    f, pr = ltas(load(ref, rs, rl))
    _, ps = ltas(np.tile(load(syn), 4))
    pr, ps = smooth_oct(f, pr), smooth_oct(f, ps)
    band = (f > 40) & (f < 10000)
    db = 10 * np.log10(pr + 1e-20) - 10 * np.log10(ps + 1e-20)
    db -= np.median(db[band])
    db = np.clip(db, -LIMIT, LIMIT)
    db[f < 50] = db[np.argmax(f >= 50)]
    db[f > 12000] = db[np.argmax(f >= 12000)]
    np.save(out, np.vstack([f, 10 ** (db / 20)]))
    for fc in (31, 63, 125, 250, 500, 1000, 2000, 4000, 8000):
        i = np.argmin(abs(f - fc))
        ref_db, synth_db = 10 * np.log10(pr[i]), 10 * np.log10(ps[i])
        print(f"{fc:5d} Hz  ref {ref_db:6.1f}  synth {synth_db:6.1f}  eq {db[i]:+5.1f} dB")


if __name__ == "__main__":
    main()
