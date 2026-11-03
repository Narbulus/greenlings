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
    "trunk": ["trunk_flat2", "trunk_lean2", "trunk_squat", "trunk_fork"],
    "mush": ["mush_a", "mush_red3", "mush_red1", "mush_one", "mush_short"],
    "twig": ["twig_a", "twig_leafy", "twig_sapling", "twig_bush"],
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
        nr = outward(p, nr)
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


def kind_of(name):
    """leaf / tuft (cards) or trunk / mush / twig (wrapped props) from a prop's name."""
    if name.startswith("leaf_"):
        return "tuft" if name.endswith("_tuft") else "leaf"
    return next((k for k in ("trunk", "mush", "twig") if name.startswith(k)), "twig")


def default_palette(opts=None):
    """[(prop name, weight)] from the per-kind weights (w_leaf ...), split across each kind's props."""
    opts = {**DEFAULTS, **(opts or {})}
    out = []
    for k, names in PROPS.items():
        names = [n for n in names if n in bpy.data.objects]
        out += [(n, opts["w_" + k] / len(names)) for n in names]
    return out


def place(prop, body, p, nrm, rnd, opts, name, spin=None, scale=None):
    """One decoration piece at surface point p (outward normal nrm), linked into the Decor collection,
    origin at its base, its settings stored on it for re-editing (o["leaf"] / o["studio"])."""
    opts = {**DEFAULTS, **opts}
    coll = decor_collection(body)
    if kind_of(prop.name) in ("leaf", "tuft"):
        o = _place_leaf(prop, body, p, nrm, rnd, opts, name)
    else:
        spin = rnd.uniform(-180, 180) if spin is None else spin
        if scale is None:
            scale = rnd.uniform(*opts.get("prop_scale", (0.7, 1.0))) * (0.85 if kind_of(prop.name) == "trunk" else 1.0)
        up = opts.get("upright", 0.6)
        o = gp.wrap(prop, body, tuple(p), hint=tuple(nrm), spin=spin, name=name,
                    scale=scale, sink=0.03, upright=up, lean=(0, 0, 0), roots=True)
        o["studio"] = {"prop": prop.name, "anchor": list(p), "hint": list(nrm), "spin": spin, "scale": scale,
                       "sink": 0.03, "upright": up, "lean": [0, 0], "tilt": [0, 0], "roots": True, "offset": 0.008}
    for c in list(o.users_collection):
        c.objects.unlink(o)
    coll.objects.link(o)
    _origin_at(o, p)
    return o


def decor_name(body):
    """Each body keeps its own decoration: collection "Decor <body collection>" inside "Decor"."""
    return "Decor " + (body.users_collection[0].name if body.users_collection else body.name)


def decor_collection(body=None):
    """The decoration collection of `body` (default: the Prop Studio body)."""
    root = bpy.data.collections.get("Decor") or bpy.data.collections.new("Decor")
    if root.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(root)
    if body is None:
        s = getattr(bpy.context.scene, "prop_studio", None)
        body = s.body if s else None
    if body is None:
        return root
    name = decor_name(body)
    coll = bpy.data.collections.get(name) or bpy.data.collections.new(name)
    if coll.name not in root.children:
        root.children.link(coll)
    return coll


def outward(p, nr):
    """Flip a surface normal to point away from the body's axis (generated meshes have flipped faces)."""
    return -nr if nr.dot(p - Vector((0.0, 0.1, p.z))) < 0 else nr


def overlaps(o, others):
    """Name of the first of `others` that o cuts into, or None."""
    t = _bvh(o)
    return next((u.name for u in others if u != o and t.overlap(_bvh(u))), None)


def run(body, opts=None):
    """opts["palette"] = [(prop name, weight)] picks the props (default: per-kind weights). Trunks only on
    the back (at most max_trunks), no mushrooms/twigs on the face/crown."""
    opts = {**DEFAULTS, **(opts or {})}
    rnd = random.Random(opts["seed"])
    coll = decor_collection(body)
    # replace only earlier procedural pieces; hand-placed and painted ones stay (and are avoided)
    for o in [o for o in coll.objects if o.get("auto")]:
        bpy.data.objects.remove(o, do_unlink=True)
    kept = list(coll.objects)
    pts = _surface_points(body, opts["zones"], rnd)
    rnd.shuffle(pts)
    palette = [(n, w) for n, w in (opts.get("palette") or default_palette(opts)) if w > 0 and n in bpy.data.objects]
    if not palette or not pts:
        return []
    placed, trees, centres = [], [_bvh(o) for o in kept], [o.matrix_world.translation.copy() for o in kept]
    trunks = sum(kind_of(o.get("studio", {}).get("prop", "")) == "trunk" for o in kept)
    for p, nrm, zone in pts:
        if len(placed) >= opts["count"]:
            break
        if any((p - c).length < opts["gap"] for c in centres):
            continue
        ok = [(n, w) for n, w in palette
              if not (kind_of(n) == "trunk" and (trunks >= opts["max_trunks"] or zone != "back"))
              and not (zone in ("face", "crown") and kind_of(n) in ("mush", "twig"))]
        if not ok:
            continue
        prop = bpy.data.objects[rnd.choices([n for n, _ in ok], [w for _, w in ok])[0]]
        k = len(placed)
        while f"D{k:02d}_{prop.name}" in bpy.data.objects:
            k += 1
        o = place(prop, body, p, nrm, rnd, opts, f"D{k:02d}_{prop.name}")
        o["auto"] = 1
        t = _bvh(o)
        if any(t.overlap(u) for u in trees):
            bpy.data.objects.remove(o, do_unlink=True)
            continue
        placed.append(o)
        trees.append(t)
        centres.append(p)
        trunks += kind_of(prop.name) == "trunk"
    return placed
