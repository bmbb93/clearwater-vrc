# /// script
# dependencies = ["numpy", "pillow"]
# ///
"""Generate the seamless sand-bed textures for the Sand bed look (1024x1024, one tile = 1 m, tiles on a torus).
Everything is procedural (no photographs): beige-to-brown quartz and feldspar grains with a few dark mineral and
white shell grains, mottled over a few centimetres, a scatter of shell fragments and small pebbles; and a height map
(the grain relief, the fragments standing a little proud). Made in the manner of the original clearwater demo's
tools/make_pebbles.py.
Usage: uv run make_sand.py [out_dir]  ->  Sand.jpg (colour, sRGB) and SandHeight.png (height, linear)"""
import sys, os
import numpy as np
from PIL import Image

S = 1024
rng = np.random.default_rng(23)
out = sys.argv[1] if len(sys.argv) > 1 else "."

fx = np.fft.fftfreq(S)[None, :]
fy = np.fft.fftfreq(S)[:, None]
fr = np.sqrt(fx * fx + fy * fy)


def band(lo, hi, seed):
    """periodic noise with energy between wavelengths S/hi and S/lo pixels, normalised to unit deviation"""
    r = np.random.default_rng(seed)
    spec = np.fft.fft2(r.normal(size=(S, S)))
    f = fr * S
    mask = np.exp(-((np.log(np.maximum(f, 1e-3)) - np.log(np.sqrt(lo * hi))) ** 2) / (2 * (np.log(hi / lo) / 2.5) ** 2))
    n = np.real(np.fft.ifft2(spec * mask))
    return (n - n.mean()) / (n.std() + 1e-9)


# --- grains: a cell per ~1.5 px, each a mineral colour (sRGB)
pal = np.array([
    [0.78, 0.70, 0.56], [0.72, 0.63, 0.49], [0.66, 0.57, 0.44], [0.82, 0.76, 0.64],  # quartz / feldspar beiges
    [0.58, 0.48, 0.36], [0.50, 0.42, 0.33],                                          # browner grains
    [0.90, 0.88, 0.83],                                                              # shell / white quartz
    [0.30, 0.28, 0.26], [0.42, 0.40, 0.37],                                          # dark minerals
])
pw = np.array([22, 18, 12, 10, 8, 5, 3, 1.2, 2.0]); pw /= pw.sum()
G = S // 2  # grain lattice (2 px per grain)
gi = rng.choice(len(pal), size=(G, G), p=pw)
gcol = pal[gi] * rng.uniform(0.9, 1.1, size=(G, G, 1))
col = np.repeat(np.repeat(gcol, 2, axis=0), 2, axis=1)
# grain relief: each grain a little dome, jittered
gh = rng.uniform(0.3, 1.0, size=(G, G))
yy, xx = np.mgrid[0:S, 0:S]
dome = 1.0 - (((xx % 2) - 0.5) ** 2 + ((yy % 2) - 0.5) ** 2) * 1.2
height = np.repeat(np.repeat(gh, 2, axis=0), 2, axis=1) * dome * 0.25

# --- mottling: sorting of the grains over centimetres and decimetres (lighter drifts, darker wet-looking patches)
m1 = band(4, 30, 1)    # ~3-25 cm
m2 = band(30, 120, 2)  # ~1-3 cm
col *= (1.0 + 0.06 * m1[..., None] + 0.035 * m2[..., None])
col[..., 2] *= (1.0 - 0.02 * m1)  # the darker patches a touch browner
height += 0.25 * band(8, 60, 3)

# --- scattered shell fragments and small pebbles (a few per square decimetre, standing proud)
def blob(cx, cy, a, b, ang, c, h, mask_rough):
    R = int(a + 2)
    xs = (np.arange(int(cx) - R, int(cx) + R + 1)) % S
    ys = (np.arange(int(cy) - R, int(cy) + R + 1)) % S
    X, Y = np.meshgrid(np.arange(-R, R + 1) + (int(cx) - cx), np.arange(-R, R + 1) + (int(cy) - cy))
    ca, sa = np.cos(ang), np.sin(ang)
    u = (X * ca + Y * sa) / a
    v = (-X * sa + Y * ca) / b
    d = u * u + v * v
    inside = d < 1.0 + mask_rough * (rng.random(d.shape) - 0.5)
    iy, ix = np.ix_(ys, xs)
    sub = col[iy, ix]
    shade = (1.0 - 0.25 * np.clip(u * 0.6 + v * 0.8, -1, 1))[..., None]  # lit from one side
    sub[inside] = (np.array(c) * shade)[inside]
    col[iy, ix] = sub
    hs = height[iy, ix]
    hs[inside] = np.maximum(hs[inside], (h * np.sqrt(np.clip(1.0 - d, 0, 1)))[inside])
    height[iy, ix] = hs

for _ in range(150):   # shell fragments: flat, pale, angular
    a = rng.uniform(2.5, 7.0)
    blob(rng.uniform(0, S), rng.uniform(0, S), a, a * rng.uniform(0.35, 0.8), rng.uniform(0, np.pi),
         pal[6] * rng.uniform(0.85, 1.0) + rng.normal(0, 0.02, 3), 0.9, 0.35)
for _ in range(35):    # small pebbles: rounded, grey-brown
    a = rng.uniform(4.0, 11.0)
    c = np.array([rng.uniform(0.40, 0.62)] * 3) * np.array([1.0, 0.96, 0.9])
    blob(rng.uniform(0, S), rng.uniform(0, S), a, a * rng.uniform(0.6, 0.95), rng.uniform(0, np.pi), c, 1.6, 0.05)

# --- a very soft blur (grains are not pixel-sharp at this scale), periodic
k = np.exp(-(fr * S) ** 2 / (2 * (S / 2.2) ** 2))
col = np.stack([np.real(np.fft.ifft2(np.fft.fft2(col[..., i]) * k)) for i in range(3)], -1)
height = np.real(np.fft.ifft2(np.fft.fft2(height) * k))

col = np.clip(col, 0, 1)
height = (height - height.min()) / (height.max() - height.min())
os.makedirs(out, exist_ok=True)
Image.fromarray((col * 255 + 0.5).astype(np.uint8)).save(os.path.join(out, "Sand.jpg"), quality=92)
Image.fromarray((height * 255 + 0.5).astype(np.uint8)).save(os.path.join(out, "SandHeight.png"), optimize=True)
print("mean colour (sRGB):", col.reshape(-1, 3).mean(0).round(3))
