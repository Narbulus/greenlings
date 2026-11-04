"""Stack props' view squares into one column per view (a single consistent 3D scene for Tripo)."""
import sys
from PIL import Image

def rows_cols(im, bg, t=40):
    px = im.load(); W, H = im.size
    ys = [y for y in range(0, H, 2) if any(sum(abs(a - b) for a, b in zip(px[x, y][:3], bg)) > t for x in range(0, W, 4))]
    xs = [x for x in range(0, W, 2) if any(sum(abs(a - b) for a, b in zip(px[x, y][:3], bg)) > t for y in range(0, H, 4))]
    return (min(ys), max(ys)), (min(xs), max(xs))

name, props = sys.argv[1], sys.argv[2:]
views = ("front", "left", "right", "back")
ims = {(p, v): Image.open(f"views/{p}_{v}.png").convert("RGB") for p in props for v in views}
bg = ims[(props[0], "front")].getpixel((5, 5))
spans, half = {}, 0
for p in props:
    ys, xs = zip(*(rows_cols(ims[(p, v)], bg) for v in views))
    spans[p] = (min(y[0] for y in ys) - 12, max(y[1] for y in ys) + 12)
    W = ims[(p, "front")].size[0]
    half = max(half, max(max(W // 2 - x[0], x[1] - W // 2) for x in xs) + 30)
gap = 90
for v in views:
    H = sum(b - a for a, b in spans.values()) + gap * (len(props) + 1)
    out = Image.new("RGB", (2 * half, H), bg)
    y = gap
    for p in props:
        a, b = spans[p]
        W = ims[(p, v)].size[0]
        tile = ims[(p, v)].crop((W // 2 - half, a, W // 2 + half, b))
        # each source board has its own background shade: cut the prop out onto the shared one
        tb = ims[(p, v)].getpixel((5, 5))
        mask = tile.point(lambda c: 0).convert("L")
        mpx, tpx = mask.load(), tile.load()
        for yy in range(tile.size[1]):
            for xx in range(tile.size[0]):
                if sum(abs(c - d) for c, d in zip(tpx[xx, yy], tb)) > 18:
                    mpx[xx, yy] = 255
        out.paste(tile, (0, y), mask)
        y += b - a + gap
    # Tripo wants roughly square inputs: pad the sides to a square, background colour
    S = max(out.size)
    sq = Image.new("RGB", (S, S), bg)
    sq.paste(out, ((S - out.size[0]) // 2, 0))
    sq.save(f"{name}_{v}.png")
print(name, out.size)
