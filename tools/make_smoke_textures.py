"""Generate billowy smoke-puff textures for the WombatGD coal smoke.

Each puff is a soft, lumpy "cauliflower" cloud: a noisy round silhouette filled
with billow noise, with fake top-lit self-shadowing baked into the RGB so the
puffs read as volumes rather than flat discs. The game multiplies RGB by the
particle colour (near-black for coal smoke), so the shading shows as subtle
depth in the dark core and more strongly as the smoke greys out.

Writes raw RGBA32 files (bottom row first, as Texture2D.LoadRawTextureData
expects) plus a PNG preview sheet.

Usage: python make_smoke_textures.py <out_dir> [count=4] [size=256]
"""

import subprocess
import sys

import numpy as np


def value_noise(size: int, cells: int, rng: np.random.Generator) -> np.ndarray:
    """Smooth value noise in [-1, 1] on a size x size grid."""
    grid = rng.uniform(-1.0, 1.0, (cells + 1, cells + 1))
    t = np.linspace(0, cells, size, endpoint=False)
    i = t.astype(int)
    f = t - i
    f = f * f * f * (f * (f * 6 - 15) + 10)  # quintic fade
    x0, x1 = i, i + 1
    a = grid[np.ix_(x0, x0)]
    b = grid[np.ix_(x0, x1)]
    c = grid[np.ix_(x1, x0)]
    d = grid[np.ix_(x1, x1)]
    fy, fx = f[:, None], f[None, :]
    top = a + (b - a) * fx
    bot = c + (d - c) * fx
    return top + (bot - top) * fy


def fbm(size, rng, base_cells=4, octaves=5, billow=False):
    out = np.zeros((size, size))
    amp, total = 1.0, 0.0
    for o in range(octaves):
        n = value_noise(size, base_cells * 2**o, rng)
        if billow:
            n = np.abs(n)  # rounded lobes split by soft creases -> cauliflower billows
        out += n * amp
        total += amp
        amp *= 0.5
    return out / total


def blur(a: np.ndarray, r: int) -> np.ndarray:
    k = np.exp(-(np.linspace(-2, 2, 2 * r + 1) ** 2))
    k /= k.sum()
    a = np.apply_along_axis(lambda v: np.convolve(v, k, mode="same"), 0, a)
    return np.apply_along_axis(lambda v: np.convolve(v, k, mode="same"), 1, a)


def puff(size: int, seed: int) -> np.ndarray:
    rng = np.random.default_rng(seed)
    y, x = np.mgrid[0:size, 0:size] / (size - 1) * 2 - 1
    r = np.sqrt(x * x + y * y)

    # Lumpy silhouette: radius wobbles with low-frequency noise.
    wobble = fbm(size, rng, base_cells=3, octaves=3)
    radius = 0.80 + 0.16 * wobble
    shape = np.clip(1.0 - r / radius, 0.0, 1.0) ** 0.6

    # Lumps: each billow is a rounded bump; their union is the cloud surface.
    lobes = blur(fbm(size, rng, base_cells=3, octaves=4, billow=True), 3)
    fine = fbm(size, rng, base_cells=12, octaves=3)
    height = shape * (0.45 + 0.9 * lobes)
    dens = np.clip((height - 0.10) * 2.2, 0.0, 1.0) * (0.9 + 0.1 * fine)

    # Fade to zero inside the border so rotated billboards never clip.
    edge = np.clip((0.97 - r) / 0.2, 0.0, 1.0)
    alpha = blur(dens * edge, 4)

    # Fake lighting from above-left: normals from the lump heightfield.
    h = blur(height, 6)
    gy, gx = np.gradient(h)
    light = np.clip(0.55 - 22.0 * gy - 8.0 * gx, 0.0, 1.0)  # rows grow downward
    # Creases between lumps sit in shadow.
    crease = np.clip(1.0 - lobes * 2.2, 0.0, 1.0)
    shade = 0.55 + 0.45 * light - 0.18 * crease * shape
    shade = np.clip(shade + 0.02 * fine, 0.3, 1.0)

    rgba = np.zeros((size, size, 4))
    rgba[..., 0] = shade
    rgba[..., 1] = shade * 0.98
    rgba[..., 2] = shade * 0.95
    rgba[..., 3] = alpha
    return (np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8)


def main() -> None:
    out_dir = sys.argv[1]
    count = int(sys.argv[2]) if len(sys.argv) > 2 else 4
    size = int(sys.argv[3]) if len(sys.argv) > 3 else 256
    puffs = []
    for i in range(count):
        p = puff(size, 1980 + i * 17)
        puffs.append(p)
        np.flipud(p).tofile(f"{out_dir}/puff{i}.rgba")  # Unity wants bottom row first
    # Preview: puffs over a mid-grey sky, tinted dark like coal smoke and plain.
    sheet = np.concatenate(puffs, axis=1).astype(np.float32) / 255
    rows = []
    for tint in (1.0, 0.18):
        bg = np.full((*sheet.shape[:2], 3), 0.62, np.float32)
        a = sheet[..., 3:4]
        rows.append(bg * (1 - a) + sheet[..., :3] * tint * a)
    prev = (np.concatenate(rows, axis=0) * 255).astype(np.uint8)
    subprocess.run(
        [
            "ffmpeg",
            "-v",
            "error",
            "-y",
            "-f",
            "rawvideo",
            "-pix_fmt",
            "rgb24",
            "-s",
            f"{prev.shape[1]}x{prev.shape[0]}",
            "-i",
            "-",
            f"{out_dir}/preview.png",
        ],
        input=prev.tobytes(),
        check=True,
    )
    print(f"wrote {count} puffs ({size}px) and preview.png to {out_dir}")


if __name__ == "__main__":
    main()
