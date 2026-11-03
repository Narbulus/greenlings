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
         name=None, scale=1.0, sink=0.03, upright=0.75, lean=(0, 0.25, 0)):
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
    # walk outward from the trunk base over the body surface (repeated nearest-point steps), so a root
    # bends with the body's curve instead of being pulled straight in (which shortened and crumpled it).
    top = max(v.co.z for v in prop.data.vertices)
    mid = [Vector((v.co.x, v.co.y)).length for v in prop.data.vertices if 0.45 * top < v.co.z < 0.8 * top]
    trunk_r = sorted(mid)[len(mid) // 2] if mid else 0.1 * top
    centre = Mi @ base
    for v in o.data.vertices:
        p = T @ v.co
        r = Vector((v.co.x, v.co.y)).length * scale
        if v.co.z > 0.5 * top or r <= trunk_r * scale:
            v.co = p
            continue
        t = min(1.0, (r - trunk_r * scale) / (trunk_r * scale))
        # direction of this root in the prop's own plane, turned onto the body
        d = (rot.to_3x3() @ Vector((v.co.x, v.co.y, 0))).normalized()
        q = centre.copy()
        step = r / 6
        for _ in range(6):
            q = q + (Mi.to_3x3() @ d) * step
            hit, hn, _, _ = bvh.find_nearest(q)
            if hit is None:
                break
            q = hit
        s_pt = M @ q + (M.to_3x3() @ bvh.find_nearest(q)[1]).normalized() * offset
        h = v.co.z * scale   # keep the root's own thickness above the skin
        s_pt = s_pt + (M.to_3x3() @ bvh.find_nearest(q)[1]).normalized() * h
        v.co = p.lerp(s_pt, t * t * (3 - 2 * t))
    o.data.update()
    o.vertex_groups.clear()
    o.vertex_groups.new(name=bone).add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
    return o
