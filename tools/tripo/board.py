"""
Split a 4-view turnaround board into the square view images tripo.py multiview takes.

    python tools/tripo/board.py <board.webp> <out_dir> [name] [--order front,right,left,back]

Figures are found as columns of non-background pixels; every view is cropped at the same scale
(so the views agree on size), centred on the figure's feet (a club or a raised arm must not shift
the body between views), padded with the background colour, 1024px.
Default order is the briefs' usual layout: front, creature's right side (facing image right),
creature's left side, back. Writes <name>_<view>.png and <name>_check.png (all four side by side).
"""
import argparse
import os

from PIL import Image


def figures(im, thresh=40, gap=15, min_width=30):
    W, H = im.size
    px = im.load()
    bg = px[5, 5]
    cols, rows = [0] * W, [0] * H
    for y in range(H):
        for x in range(W):
            p = px[x, y]
            if abs(p[0] - bg[0]) + abs(p[1] - bg[1]) + abs(p[2] - bg[2]) > thresh:
                cols[x] += 1
                rows[y] += 1
    segs, start = [], None
    for x, c in enumerate(cols + [0]):
        if c > 2 and start is None:
            start = x
        elif c <= 2 and start is not None:
            if segs and start - segs[-1][1] < gap:
                segs[-1] = (segs[-1][0], x)
            else:
                segs.append((start, x))
            start = None
    segs = [s for s in segs if s[1] - s[0] > min_width]
    ys = [y for y in range(H) if rows[y] > 2]
    return bg, segs, ys[0], ys[-1]


def feet_centre(im, bg, x0, x1, top, bot, thresh=40, band=0.12):
    """Midpoint of the foreground in the bottom `band` of the figure's rows."""
    px = im.load()
    xs = [x for y in range(int(bot - (bot - top) * band), bot + 1) for x in range(x0, x1)
          if sum(abs(px[x, y][k] - bg[k]) for k in range(3)) > thresh]
    return (min(xs) + max(xs)) // 2 if xs else (x0 + x1) // 2


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("board")
    ap.add_argument("out_dir")
    ap.add_argument("name", nargs="?", default="view")
    ap.add_argument("--order", default="front,right,left,back")
    a = ap.parse_args()
    order = a.order.split(",")

    im = Image.open(a.board).convert("RGB")
    bg, segs, top, bot = figures(im)
    if len(segs) != len(order):
        raise SystemExit(f"found {len(segs)} figures {segs}, expected {len(order)}")
    centres = [feet_centre(im, bg, x0, x1, top, bot) for x0, x1 in segs]
    reach = max(max(cx - x0, x1 - cx) for (x0, x1), cx in zip(segs, centres))
    S = int(max(2 * reach, bot - top) * 1.12)
    y0, y1 = top - 8, bot + 8
    oy = (S - (y1 - y0)) // 2
    os.makedirs(a.out_dir, exist_ok=True)
    check = Image.new("RGB", (256 * len(order), 256))
    for i, ((x0, x1), view, cx) in enumerate(zip(segs, order, centres)):
        out = Image.new("RGB", (S, S), bg)
        a0, a1 = max(0, x0 - 4), x1 + 4   # only this figure's columns: neighbours' arms stay out
        out.paste(im.crop((a0, y0, a1, y1)), (a0 - (cx - S // 2), oy))
        out = out.resize((1024, 1024), Image.LANCZOS)
        out.save(os.path.join(a.out_dir, f"{a.name}_{view}.png"))
        check.paste(out.resize((256, 256)), (i * 256, 0))
    check.save(os.path.join(a.out_dir, f"{a.name}_check.png"))
    print(a.name, "figures", segs, "rows", (top, bot))


if __name__ == "__main__":
    main()
