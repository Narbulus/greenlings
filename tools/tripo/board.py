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


def grid(board, out_dir, name):
    """
    Slice a 2x2 board. Cells: TL front, TR the creature facing image right = its LEFT side (Tripo's
    left/right are the creature's own), BL facing image left = its RIGHT side, BR back. Each column
    is split at the empty band between its two figures (they can cross the middle), each view is
    cropped to its figure and centred at one common scale.
    """
    im = Image.open(board).convert("RGB")
    W, H = im.size
    bg = im.getpixel((5, 5))
    px = im.load()

    def fg(x, y):
        return sum(abs(px[x, y][k] - bg[k]) for k in range(3)) > 40

    found = {}
    for col, (top_view, bottom_view) in enumerate((("front", "right"), ("left", "back"))):
        x0, x1 = col * W // 2, (col + 1) * W // 2
        rows = [sum(fg(x, y) for x in range(x0, x1, 2)) for y in range(H)]
        # split at the middle of the empty band closest to the image centre (figures can cross H/2)
        empty = [y for y in range(H // 5, 4 * H // 5) if rows[y] == 0]
        split = H // 2
        if empty:
            bands, start = [], empty[0]
            for a, b in zip(empty, empty[1:] + [None]):
                if b != a + 1:
                    bands.append((start, a))
                    start = b
            lo, hi = min(bands, key=lambda bd: abs((bd[0] + bd[1]) / 2 - H / 2))
            split = (lo + hi) // 2
        for view, (y0, y1) in ((top_view, (0, split)), (bottom_view, (split, H))):
            pts = [(x, y) for x in range(x0, x1, 2) for y in range(y0, y1, 2) if fg(x, y)]
            b = (min(p[0] for p in pts), min(p[1] for p in pts), max(p[0] for p in pts) + 1, max(p[1] for p in pts) + 1)
            found[view] = (im, b)
    found = {v: found[v] for v in ("front", "left", "right", "back")}
    S = int(max(max(b[2] - b[0], b[3] - b[1]) for _, b in found.values()) * 1.1)
    os.makedirs(out_dir, exist_ok=True)
    check = Image.new("RGB", (1024, 256))
    for i, (view, (c, b)) in enumerate(found.items()):
        out = Image.new("RGB", (S, S), bg)
        out.paste(c.crop(b), (S // 2 - (b[2] - b[0]) // 2, S // 2 - (b[3] - b[1]) // 2))  # c is the whole board
        out = out.resize((1024, 1024), Image.LANCZOS)
        out.save(os.path.join(out_dir, f"{name}_{view}.png"))
        check.paste(out.resize((256, 256)), (i * 256, 0))
    check.save(os.path.join(out_dir, f"{name}_check.png"))
    print(name, {v: b for v, (_, b) in found.items()})


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("board")
    ap.add_argument("out_dir")
    ap.add_argument("name", nargs="?", default="view")
    ap.add_argument("--order", default="front,right,left,back")
    ap.add_argument("--grid", action="store_true",
                    help="2x2 board (reference edits): top-left front, top-right side facing right, "
                         "bottom-left side facing left, bottom-right back")
    a = ap.parse_args()
    if a.grid:
        return grid(a.board, a.out_dir, a.name)
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
