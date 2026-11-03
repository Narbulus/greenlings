"""
Procedural decoration of a greenling body with the prop set (greenling_props).

    import decorate
    decorate.run(body, opts)      # opts: see DEFAULTS; returns the placed objects (collection "Decor")

Points are sampled on the body surface inside zones (defined in the vanilla bind pose, so they fit
every variant), spaced at least `gap` apart. Each point gets a prop by weight:
- leaves (cards) emerge from the skin: base tucked `tuck` under the surface, pointing outward from
  the body and flowing down it by `droop` (like feathers), with some random roll;
- trunks / mushrooms / twigs stand on the skin with their roots wrapped (greenling_props.wrap).
A candidate is dropped if it overlaps anything already placed (BVH overlap test between props;
props may and should intersect the body itself).
"""
import math
import random

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

import greenling_props as gp

ZONES = {
    # name: test on a world-space surface point p with outward normal n (vanilla greydwarf-family layout)
    "back": lambda p, n: p.y > 0.12 and 50.95 < p.z < 51.6 and n.y > 0.25,
    "shoulders": lambda p, n: 0.1 < abs(p.x) < 0.3 and p.z > 51.3 and n.z > 0.3,   # stops before the arms
    "crown": lambda p, n: abs(p.x) < 0.18 and p.z > 51.42 and n.z > 0.4,
    "face": lambda p, n: abs(p.x) < 0.25 and p.y < -0.05 and 51.15 < p.z < 51.55 and n.y < -0.1 and abs(p.x) > 0.07,
}

DEFAULTS = dict(
    seed=1, count=10, gap=0.09, zones=("back", "shoulders", "crown"),
    w_leaf=6, w_tuft=3, w_trunk=1, w_mush=1, w_twig=1,
    leaf_size=(0.8, 1.2), droop=35.0, tuck=0.03, max_trunks=1, roll=25.0,
)

PROPS = {
    "leaf": ["leaf_bush_1", "leaf_spray_1"],
    "tuft": ["leaf_bush_tuft", "leaf_spray_tuft"],
    "trunk": ["trunk_flat2", "trunk_lean2"],
    "mush": ["mush_a"],
    "twig": ["twig_a", "twig_leafy"],
}


def _surface_points(body, zones, rnd, n=4000):
    """Random points on the body surface (area-weighted) that fall in any of the zones."""
    me = body.data
    M = body.matrix_world
    N3 = M.to_3x3().inverted().transposed()
    me.calc_loop_triangles()
    tris = []
    total = 0.0
    for t in me.loop_triangles:
        a, b, c = (M @ me.vertices[i].co for i in t.vertices)
        area = ((b - a).cross(c - a)).length / 2
        total += area
        tris.append((total, a, b, c, (N3 @ t.normal).normalized()))
    out = []
    import bisect
    keys = [t[0] for t in tris]
    for _ in range(n):
        _, a, b, c, nr = tris[bisect.bisect(keys, rnd.random() * total) % len(tris)]
        u, v = rnd.random(), rnd.random()
        if u + v > 1:
            u, v = 1 - u, 1 - v
        p = a + (b - a) * u + (c - a) * v
        # outward normal: generated meshes have some flipped faces; out = away from the body's axis
        axis = Vector((0.0, 0.1, p.z))
        if nr.dot(p - axis) < 0:
            nr = -nr
        z = next((zn for zn in zones if ZONES[zn](p, nr)), None)
        if z:
            out.append((p, nr, z))
    return out


def _bvh(o):
    me = o.data
    M = o.matrix_world
    return BVHTree.FromPolygons([M @ v.co for v in me.vertices], [tuple(p.vertices) for p in me.polygons])


def _place_leaf(prop, body, p, nrm, rnd, opts, name, roll=None, size=None):
    """Card emerging from the skin: out along the normal, flowing down the body, rolled at random."""
    down = Vector((0, 0, -1))
    flow = (down - nrm * down.dot(nrm))
    flow = flow.normalized() if flow.length > 1e-4 else Vector((0, 1, 0))
    a = math.radians(opts["droop"])
    out_dir = (nrm * math.cos(a) + flow * math.sin(a)).normalized()
    # card frame: up = out_dir, broad side across the flow (local X = sideways on the skin), so the
    # card springs out of the skin like a feather instead of lying flat or edge-on
    side = flow.cross(nrm).normalized()
    roll = rnd.uniform(-opts["roll"], opts["roll"]) if roll is None else roll
    side = Matrix.Rotation(math.radians(roll), 3, out_dir) @ side
    y = out_dir.cross(side).normalized()
    x = y.cross(out_dir).normalized()
    R = Matrix((x, y, out_dir)).transposed().to_4x4()
    s = rnd.uniform(*opts["leaf_size"]) if size is None else size
    T = Matrix.Translation(p - out_dir * opts["tuck"]) @ R @ Matrix.Scale(s, 4)
    o = prop.copy()
    o.data = prop.data.copy()
    o.name = name
    o.matrix_world = T
    o.data.transform(T)
    o.matrix_world = Matrix.Identity(4)
    o.vertex_groups.clear()
    o.vertex_groups.new(name="spine3").add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
    o["leaf"] = {"prop": prop.name, "anchor": list(p), "normal": list(nrm), "droop": opts["droop"], "roll": roll,
                 "size": s, "tuck": opts["tuck"]}
    return o


def leaf_from(c, body, name):
    """Re-place a leaf from its stored settings (o["leaf"])."""
    import random as _r
    return _place_leaf(bpy.data.objects[c["prop"]], body, Vector(c["anchor"]), Vector(c["normal"]).normalized(), _r.Random(0),
                       {"droop": c["droop"], "tuck": c["tuck"], "roll": 0, "leaf_size": (1, 1)}, name, roll=c["roll"], size=c["size"])


def _origin_at(o, p):
    """Put the object's origin at its base point so rotate/scale by hand pivot where it grows from."""
    o.data.transform(Matrix.Translation(-p))
    o.matrix_world = Matrix.Translation(p)


def run(body, opts=None):
    opts = {**DEFAULTS, **(opts or {})}
    rnd = random.Random(opts["seed"])
    coll = bpy.data.collections.get("Decor") or bpy.data.collections.new("Decor")
    if coll.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(coll)
    for o in list(coll.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    pts = _surface_points(body, opts["zones"], rnd)
    rnd.shuffle(pts)
    kinds = [k for k in ("leaf", "tuft", "trunk", "mush", "twig") for _ in range(int(opts["w_" + k]))]
    if not kinds or not pts:
        return []
    placed, trees, centres = [], [], []
    trunks = 0
    for p, nrm, zone in pts:
        if len(placed) >= opts["count"]:
            break
        if any((p - c).length < opts["gap"] for c in centres):
            continue
        kind = rnd.choice(kinds)
        if kind == "trunk" and (trunks >= opts["max_trunks"] or zone != "back"):
            kind = "tuft"     # trunks only on the back (they tower over the head elsewhere)
        if zone in ("face", "crown") and kind in ("mush", "twig"):
            kind = "leaf"
        names = [n for n in PROPS[kind] if n in bpy.data.objects]
        if not names:
            continue
        prop = bpy.data.objects[rnd.choice(names)]
        nm = f"D{len(placed):02d}_{prop.name}"
        if kind in ("leaf", "tuft"):
            o = _place_leaf(prop, body, p, nrm, rnd, opts, nm)
            for c in o.users_collection:
                c.objects.unlink(o)
        else:
            spin = rnd.uniform(-180, 180)
            scale = rnd.uniform(0.7, 1.0) * (0.85 if kind == "trunk" else 1.0)
            o = gp.wrap(prop, body, tuple(p), hint=tuple(nrm), spin=spin, name=nm,
                        scale=scale, sink=0.03, upright=0.6, lean=(0, 0, 0), roots=True)
            for c in list(o.users_collection):
                c.objects.unlink(o)
        coll.objects.link(o)
        if kind not in ("leaf", "tuft"):
            o["studio"] = {"prop": prop.name, "anchor": list(p), "hint": list(nrm), "spin": spin, "scale": scale,
                           "sink": 0.03, "upright": 0.6, "lean": [0, 0], "tilt": [0, 0], "roots": True, "offset": 0.008}
        _origin_at(o, p)
        t = _bvh(o)
        if any(t.overlap(u) for u in trees):
            bpy.data.objects.remove(o, do_unlink=True)
            continue
        placed.append(o)
        trees.append(t)
        centres.append(p)
        trunks += kind == "trunk"
    return placed
