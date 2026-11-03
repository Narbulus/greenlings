"""
Props that sit on a greenling body: generated trunks (later mushrooms, twigs) wrapped onto the
skin so their roots hug the body's curve, like the vanilla stump's.

    import greenling_props as gp
    prop = gp.import_prop(glb, "trunk_flat", height=0.42)       # upright, base at the origin
    placed = gp.wrap(prop, body, anchor, normal_hint, spin=30)   # a new object on the body

A prop is modelled with its roots spread flat at its base. wrap() stands it on the body at the
anchor (base on the surface, mostly upright, turned by `spin` degrees) and then pulls every root
vertex onto the skin: a vertex low on the prop and further out than the trunk's radius moves to the
nearest point of the body surface plus a small offset, blended by how far out it is, so the trunk
itself stays rigid. The
result is weighted to one bone (the one under the anchor) and rides it rigidly in game.
Anchors are given in the vanilla bind pose, so a placement fits every variant of that body.
"""
import math

import bpy
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree

ANCHORS = {
    # name: (position on the body, outward hint, bone) in vanilla greydwarf-family bind pose
    "back_high": ((0.0, 0.32, 51.45), (0, 1, 0.6), "spine3"),
    "back_left": ((0.16, 0.30, 51.30), (0.4, 1, 0.3), "spine3"),
    "back_right": ((-0.16, 0.30, 51.30), (-0.4, 1, 0.3), "spine3"),
    "shoulder_left": ((0.24, 0.10, 51.42), (0.6, 0.3, 1), "spine3"),
    "shoulder_right": ((-0.24, 0.10, 51.42), (-0.6, 0.3, 1), "spine3"),
}


def import_prop(glb, name, height=0.42):
    """Import a Tripo prop, base at the origin, upright (+Z), scaled to `height`; transforms applied."""
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=glb)
    new = [o for o in bpy.data.objects if o not in before]
    o = next(x for x in new if x.type == "MESH")
    for x in new:
        if x is not o:
            bpy.data.objects.remove(x, do_unlink=True)
    o.name = name
    o.parent = None
    bpy.context.view_layer.update()
    o.data.transform(o.matrix_world)
    o.matrix_world = Matrix.Identity(4)
    ps = [v.co for v in o.data.vertices]
    zmin, zmax = min(p.z for p in ps), max(p.z for p in ps)
    base = [p for p in ps if p.z < zmin + 0.15 * (zmax - zmin)]
    cx = sum(p.x for p in base) / len(base)
    cy = sum(p.y for p in base) / len(base)
    k = height / (zmax - zmin)
    for v in o.data.vertices:
        v.co = Vector(((v.co.x - cx) * k, (v.co.y - cy) * k, (v.co.z - zmin) * k))
    o.data.update()
    return o


def wrap(prop, body, anchor, hint=(0, 0, 1), spin=0.0, offset=0.008, bone="spine3",
         name=None, scale=1.0, sink=0.03, upright=0.75, lean=(0, 0.25, 0), roots=True):
    """Place `prop` on `body` at `anchor` and wrap its roots onto the skin. Returns the new object."""
    dg = bpy.context.evaluated_depsgraph_get()
    bvh = BVHTree.FromObject(body, dg)
    M, Mi = body.matrix_world, body.matrix_world.inverted()
    loc, nrm, _, _ = bvh.find_nearest(Mi @ Vector(anchor))
    n = (M.to_3x3() @ nrm).normalized()
    hint = Vector(hint).normalized()
    if n.dot(hint) < 0:
        n = -n
    # trunks stand mostly upright like the vanilla stump, tilted a little by the surface and `lean`
    up = (Vector((0, 0, 1)) * upright + n * (1 - upright) + Vector(lean)).normalized()
    base = M @ loc - up * sink
    rot = Vector((0, 0, 1)).rotation_difference(up).to_matrix().to_4x4() @ Matrix.Rotation(math.radians(spin), 4, "Z")
    T = Matrix.Translation(base) @ rot @ Matrix.Scale(scale, 4)

    o = prop.copy()
    o.data = prop.data.copy()
    o.name = name or f"{prop.name}@{bone}"
    for c in body.users_collection:
        c.objects.link(o)
    o.matrix_world = Matrix.Identity(4)
    # roots: low on the prop and further out than the trunk. Each root vertex is laid along the skin:
    # walk from the anchor's SURFACE point (never from the sunk base, which may be inside the body)
    # along the surface toward the root's direction, then sit at the skin + offset + the vertex's height
    # above the root's own underside (so the underside touches the skin and the thickness is kept).
    # Sink only moves the rigid trunk.
    src = prop.data.vertices
    top = max(v.co.z for v in src)
    mid = [Vector((v.co.x, v.co.y)).length for v in src if 0.45 * top < v.co.z < 0.8 * top]
    trunk_r = sorted(mid)[len(mid) // 2] if mid else 0.1 * top
    bins = {}

    def key(c):
        return (round(math.atan2(c.y, c.x) / (math.pi / 16)), round(Vector((c.x, c.y)).length / (0.25 * trunk_r)))
    for v in src:
        k = key(v.co)
        bins[k] = min(bins.get(k, 9.0), v.co.z)

    def surface(q, nrm):
        """Project q onto the skin from outside along -nrm (falls back to nearest)."""
        hit, hn, _, _ = bvh.ray_cast(q + nrm * 0.15, -nrm, 0.4)
        if hit is None:
            hit, hn, _, _ = bvh.find_nearest(q)
        if hn.dot(nrm) < 0:
            hn = -hn
        return hit, hn

    start = loc.copy()
    n0 = Mi.to_3x3() @ n
    for v in o.data.vertices:
        p = T @ v.co
        r = Vector((v.co.x, v.co.y)).length
        if not roots or v.co.z > 0.5 * top or r <= trunk_r:
            v.co = p
            continue
        t = min(1.0, (r - trunk_r) / (0.35 * trunk_r))
        under = bins.get(key(v.co), 0.0)
        d = Mi.to_3x3() @ (rot.to_3x3() @ Vector((v.co.x, v.co.y, 0))).normalized()
        q, qn = start.copy(), n0.normalized()
        steps = 8
        for _ in range(steps):
            tan = (d - qn * d.dot(qn)).normalized()
            q, qn = surface(q + tan * (r * scale / steps), qn)
        s_pt = M @ (q + qn.normalized() * (offset + max(0.0, v.co.z - under) * scale))
        v.co = p.lerp(s_pt, t * t * (3 - 2 * t))
    o.data.update()
    o.vertex_groups.clear()
    o.vertex_groups.new(name=bone).add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
    return o



# ---------------------------------------------------------------- leaf cards ("feathers")

LEAVES = ("willow", "beech", "birch", "oak")   # cells of tools/props/leaf_atlas.png


def _leaf_material(atlas):
    m = bpy.data.materials.get("LeafCard")
    if m:
        return m
    m = bpy.data.materials.new("LeafCard")
    m.use_nodes = True
    nt = m.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(atlas, check_existing=True)
    tex.interpolation = "Closest"
    bsdf = nt.nodes["Principled BSDF"]
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    nt.nodes.active = tex
    m.blend_method = "CLIP" if hasattr(m, "blend_method") else None
    m.use_backface_culling = False
    return m


def _card(bm, uvl, cell, length, at=Vector(), rot=Matrix.Identity(3)):
    """One leaf card: a pointed 5-sided shape (base, two shoulders, tip, cut corners) fitted to the
    leaf's box in the atlas, so the alpha cut-out has little empty card around it. Base at `at`,
    pointing along rot's +Z, flat in its XZ plane."""
    u0, v0, u1, v1 = cell
    w = length * (u1 - u0) / (v1 - v0)
    shape = [(0.0, 0.0), (0.5, 0.18), (0.5, 0.72), (0.0, 1.0), (-0.5, 0.72), (-0.5, 0.18)]
    verts = [bm.verts.new(at + rot @ Vector((x * w, 0, y * length))) for x, y in shape]
    f = bm.faces.new(verts)
    for l, (x, y) in zip(f.loops, shape):
        l[uvl].uv = (u0 + (x + 0.5) * (u1 - u0), v0 + y * (v1 - v0))
    return f


def make_leaves(name, atlas, cells, kind="beech", count=1, length=0.16, spread=70.0, droop=25.0):
    """
    A leaf prop: `count` cards of one leaf kind fanned out from a shared base point at the origin
    (pointing up, +Z), `spread` degrees across, each tipped back by `droop`. count=1 is a single
    feather; 3-6 make a tuft. Mixed kinds: call per kind and join, or pass kind="mixed".
    """
    import bmesh
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    for i in range(count):
        k = LEAVES[i % 4] if kind == "mixed" else kind
        a = 0.0 if count == 1 else -spread / 2 + spread * i / (count - 1)
        rot = (Matrix.Rotation(math.radians(a), 3, "Y") @ Matrix.Rotation(math.radians(-droop), 3, "X"))
        _card(bm, uvl, cells[LEAVES.index(k)], length * (1.0 - 0.15 * abs(a) / max(spread, 1)), Vector((0, i * 0.002, 0)), rot)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    me.materials.append(_leaf_material(atlas))
    return o
