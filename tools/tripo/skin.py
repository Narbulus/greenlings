"""
Prepare a baked skin texture for Valheim's Custom/Creature shader.

    python tools/tripo/skin.py <in.png> <out.png> [--vanilla texture_greyling.png --grade 0.5]
                               [--hue -0.03 --sat 0.121]

The creature material adjusts colour in the shader (greyling: _Hue=-0.03, _Saturation=0.121, see
gldev_export), which is why vanilla skin textures look dull on disk. A generated texture is already
"finished", so we undo that shift here: hue moved back, saturation taken off, and the shader puts it
back in game. --grade pulls the colour statistics part of the way toward a vanilla texture.
"""
import argparse
import colorsys

from PIL import Image


def stats(im):
    px = [p[:3] for p in im.getdata() if sum(p[:3]) > 30]
    n = len(px)
    m = [sum(p[i] for p in px) / n for i in range(3)]
    s = [(sum((p[i] - m[i]) ** 2 for p in px) / n) ** 0.5 for i in range(3)]
    return m, s


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--vanilla")
    ap.add_argument("--grade", type=float, default=0.0)
    ap.add_argument("--hue", type=float, default=-0.03)
    ap.add_argument("--sat", type=float, default=0.121)
    a = ap.parse_args()

    im = Image.open(a.src).convert("RGB")
    if a.vanilla and a.grade > 0:
        mv, sv = stats(Image.open(a.vanilla).convert("RGBA"))
        mg, sg = stats(im)
        tm = [mg[i] + (mv[i] - mg[i]) * a.grade for i in range(3)]
        ts = [sg[i] + (sv[i] - sg[i]) * a.grade for i in range(3)]
        im = im.point(lambda v: v)  # copy
        px = im.load()
        for y in range(im.height):
            for x in range(im.width):
                p = px[x, y]
                px[x, y] = tuple(max(0, min(255, int((p[i] - mg[i]) / max(sg[i], 1) * ts[i] + tm[i]))) for i in range(3))

    px = im.load()
    for y in range(im.height):
        for x in range(im.width):
            r, g, b = (c / 255 for c in px[x, y])
            h, s, v = colorsys.rgb_to_hsv(r, g, b)
            h = (h - a.hue) % 1.0
            s = max(0.0, s - a.sat)
            px[x, y] = tuple(int(round(c * 255)) for c in colorsys.hsv_to_rgb(h, s, v))
    im.save(a.dst)


if __name__ == "__main__":
    main()
