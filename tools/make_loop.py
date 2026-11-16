"""Cut a seamless loop from a recording.

Takes [start, start+length+xfade) at 48 kHz mono, levels it with ffmpeg's
dynaudnorm + loudnorm, then crossfades the tail into the head so the end of
the file flows straight into its start. Writes 16-bit WAV.

Usage: python make_loop.py <in> <out.wav> <start_s> <length_s> [xfade_s=0.5]
"""

import subprocess
import sys

import numpy as np

SR = 48000


def main() -> None:
    src, dst = sys.argv[1], sys.argv[2]
    start, length = float(sys.argv[3]), float(sys.argv[4])
    xfade = float(sys.argv[5]) if len(sys.argv) > 5 else 0.5
    raw = subprocess.run(
        [
            "ffmpeg",
            "-v",
            "error",
            "-ss",
            str(start),
            "-t",
            str(length + xfade + 0.2),
            "-i",
            src,
            "-ac",
            "1",
            "-ar",
            str(SR),
            "-af",
            "highpass=f=30,dynaudnorm=f=250:g=15,loudnorm=I=-16:TP=-1.5:LRA=7",
            "-f",
            "f32le",
            "-",
        ],
        check=True,
        capture_output=True,
    ).stdout
    x = np.frombuffer(raw, dtype=np.float32).copy()
    n, d = int(length * SR), int(xfade * SR)
    if len(x) < n + d:
        sys.exit(f"source too short: got {len(x) / SR:.2f}s, need {(n + d) / SR:.2f}s")
    t = np.linspace(0.0, np.pi / 2, d, dtype=np.float32)
    out = x[:n].copy()
    # equal-power crossfade: the sample after out[-1] in the source is x[n],
    # so blending x[n:n+d] into the head makes the wrap continuous.
    out[:d] = x[:d] * np.sin(t) + x[n : n + d] * np.cos(t)
    peak = np.max(np.abs(out))
    if peak > 0.99:
        out *= 0.99 / peak
    pcm = (out * 32767).astype("<i2").tobytes()
    subprocess.run(
        ["ffmpeg", "-v", "error", "-y", "-f", "s16le", "-ar", str(SR), "-ac", "1", "-i", "-", dst],
        input=pcm,
        check=True,
    )
    print(f"{dst}: {n / SR:.2f}s loop, xfade {xfade}s, peak {peak:.2f}")


if __name__ == "__main__":
    main()
