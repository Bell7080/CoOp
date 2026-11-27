# Texture atlas for the alarm-clock spider (512x512, imported at 256 with point filtering).
# The dial (numbers, grinning face, hour hand) is cut straight out of the concept image so the in-game
# creature wears the same face; the rest are small hand-painted swatches the mesh UVs point into.
# usage: python paint_clockspider.py <concept image> <out.png>
#   left 384x384: round dial          right column / bottom row: swatches (see SW in make_clockspider.py)
import random
import sys

from PIL import Image, ImageDraw

src, dst = sys.argv[1], sys.argv[2]
random.seed(7)
ref = Image.open(src).convert("RGB")
W = 512
atlas = Image.new("RGB", (W, W), (40, 12, 10))

# Dial: centre and radius of the white face in the 1402x1122 concept image (just inside the red case).
CX, CY, R = 737, 522, 300
dial = ref.crop((CX - R, CY - R, CX + R, CY + R)).resize((384, 384), Image.LANCZOS)
dial = dial.point(lambda v: min(255, int(v * 1.18)))  # the concept is lit dimly; the game adds its own darkness
atlas.paste(dial, (0, 0))


def noisy(box, base, spread, dark_spots=0.0, spot=(0, 0, 0)):
    x0, y0, x1, y1 = box
    px = atlas.load()
    for y in range(y0, y1):
        for x in range(x0, x1):
            if random.random() < dark_spots:
                c = spot
            else:
                k = random.uniform(-spread, spread)
                c = tuple(max(0, min(255, int(v * (1 + k)))) for v in base)
            px[x, y] = c


# right column (x 384..512), each 128 wide x 64 tall
noisy((384, 0, 512, 64), (128, 34, 26), 0.18, 0.05, (70, 22, 16))      # case: worn red paint
noisy((384, 64, 512, 128), (150, 132, 128), 0.12, 0.03, (90, 80, 78))  # bells: dull nickel
noisy((384, 128, 512, 192), (112, 88, 70), 0.10)                       # handle / hammer: tarnished brass
noisy((384, 192, 512, 256), (60, 20, 16), 0.15)                        # back plate: dark red
noisy((384, 256, 512, 320), (225, 212, 190), 0.05)                     # dial rim: off-white
noisy((384, 320, 512, 384), (25, 18, 16), 0.10)                        # keys / feet: near black
# bottom row (y 384..512): legs, a vertical gradient top = hip (pale flesh) -> bottom = tip (dark raw red)
px = atlas.load()
for y in range(384, 512):
    t = (y - 384) / 127
    if t < 0.55:
        k = t / 0.55
        base = (int(150 - 30 * k), int(96 - 40 * k), int(88 - 34 * k))
    else:
        k = (t - 0.55) / 0.45
        base = (int(120 - 70 * k), int(56 - 40 * k), int(54 - 38 * k))
    for x in range(0, W):
        n = random.uniform(-0.12, 0.12)
        px[x, y] = tuple(max(0, min(255, int(v * (1 + n)))) for v in base)
atlas.save(dst)
print("atlas", dst)
