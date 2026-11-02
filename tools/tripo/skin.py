"""
Prepare a baked skin texture for Valheim's Custom/Creature shader.

    python tools/tripo/skin.py <in.png> <out.png> [--vanilla texture_greyling.png --grade 0.5]
                               [--hue -0.03 --sat 0.121]

The creature material adjusts colour in the shader (greyling: _Hue=-0.03, _Saturation=0.121, see
gldev_export), which is why vanilla skin textures look dull on disk. A generated texture is already
"finished", so we undo that shift here: hue moved back, saturation taken off, and the shader puts it
back in game. --grade pulls the colour statistics part of the way toward a vanilla texture (use the
vanilla texture of the creature the skin is for: tools/tripo exports from gldev_photo).

The baked texture's unused space between UV islands is black; the game's mipmaps blend that into the
island edges, which darkens the model at a distance and outlines every seam. pad() fills it with the
nearest island colour first.
"""
import argparse
import colorsys

from PIL import Image, ImageFilter, ImageMath


def stats(im):
    px = [p[:3] for p in im.getdata() if sum(p[:3]) > 30]
    n = len(px)
    m = [sum(p[i] for p in px) / n for i in range(3)]
    s = [(sum((p[i] - m[i]) ** 2 for p in px) / n) ** 0.5 for i in range(3)]
    return m, s


def pad(im, empty=6, rim=2):
    """Fill the empty (near-black) texels with the colour of the nearest island: push-pull over a
    pyramid of box-filtered levels, each texel taking the finest level that has coverage there.
    The outermost `rim` texels of each island are replaced too: downscaling the bake already
    blended them with the black."""
    mask = im.convert("L").point(lambda v: 255 if v > empty else 0)
    for _ in range(rim):
        mask = mask.filter(ImageFilter.MinFilter(3))
    levels = [(im, mask)]
    while levels[-1][0].width > 1:
        pm, m = levels[-1]
        w, h = max(1, pm.width // 2), max(1, pm.height // 2)
        premul = Image.composite(pm, Image.new("RGB", pm.size), m).resize((w, h), Image.BOX)
        cover = m.resize((w, h), Image.BOX)
        chans = [ImageMath.lambda_eval(lambda e: e["convert"](e["min"](e["c"] * 255 / e["max"](e["k"], 1), 255), "L"),
                                       c=c, k=cover) for c in premul.split()]
        levels.append((Image.merge("RGB", chans), cover.point(lambda v: 255 if v > 0 else 0)))
    filled = levels[-1][0]
    for pm, m in reversed(levels[:-1]):
        filled = Image.composite(pm, filled.resize(pm.size, Image.BILINEAR), m)
    return filled


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("src")
    ap.add_argument("dst")
    ap.add_argument("--vanilla")
    ap.add_argument("--grade", type=float, default=0.0)
    ap.add_argument("--hue", type=float, default=-0.03)
    ap.add_argument("--sat", type=float, default=0.121)
    a = ap.parse_args()

    im = pad(Image.open(a.src).convert("RGB"))
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
