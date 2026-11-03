"""
Shrink prop textures to the body's texel density (~62 texels per unit, the Valheim pixel look).

    python tools/tripo/prop_textures.py <orig_dir> <out_dir> <name>=<size> ...

Step 1 (Blender) measures each prop texture's density (UV area x texture size^2 / surface area,
summed over every prop that uses the texture), saves the full-size texture to <orig_dir> and picks
<size> = the power of two nearest to size * 62 / density. Step 2 (this script) pads the UV islands
(skin.pad: the tiny result must not average black gutter into the islands) and box-filters down.
Step 3 (Blender) points the materials at the small textures with Closest interpolation and drops the
baked normal map (the game shades props flat). <name> is matched against the start of the file name.
"""
import os
import sys

from PIL import Image

sys.path.insert(0, os.path.dirname(__file__))
from skin import pad  # noqa: E402


def main(orig, out, sizes):
    os.makedirs(out, exist_ok=True)
    for f in sorted(os.listdir(orig)):
        size = next((s for k, s in sizes.items() if f.startswith(k)), None)
        if size is None:
            continue
        small = pad(Image.open(os.path.join(orig, f)).convert("RGB")).resize((size, size), Image.BOX)
        small.save(os.path.join(out, f))
        print(f, small.size)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], {k: int(v) for k, v in (a.split("=", 1) for a in sys.argv[3:])})
