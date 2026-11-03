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

    if roots:
        rig_prop(prop)   # weights must be on the prop before the copy
    o = prop.copy()
    o.data = prop.data.copy()
    o.name = name or f"{prop.name}@{bone}"
    for c in body.users_collection:
        c.objects.link(o)
    o.matrix_world = Matrix.Identity(4)
    if not roots:
        for v in o.data.vertices:
            v.co = T @ v.co
    else:
        _pose_roots(o, prop, T, rot, scale, bvh, M, Mi, loc, n, offset)
    o.data.update()
    o.vertex_groups.clear()
    o.vertex_groups.new(name=bone).add(list(range(len(o.data.vertices))), 1.0, "REPLACE")
    return o



# ---------------------------------------------------------------- root rig

def _root_layout(prop):
    """Trunk radius, root directions (angles) and each root's underside line from the prop mesh."""
    src = prop.data.vertices
    top = max(v.co.z for v in src)
    mid = [Vector((v.co.x, v.co.y)).length for v in src if 0.45 * top < v.co.z < 0.8 * top]
    trunk_r = sorted(mid)[len(mid) // 2] if mid else 0.1 * top
    root_vs = [v.co for v in src if v.co.z < 0.5 * top and Vector((v.co.x, v.co.y)).length > trunk_r]
    hist = [0] * 36
    for c in root_vs:
        if Vector((c.x, c.y)).length > 1.5 * trunk_r:
            hist[int((math.atan2(c.y, c.x) + math.pi) / (2 * math.pi) * 36) % 36] += 1
    peaks = [i for i in range(36) if hist[i] > 2 and hist[i] >= hist[i - 1] and hist[i] >= hist[(i + 1) % 36]]
    dirs = [(i + 0.5) / 36 * 2 * math.pi - math.pi for i in sorted(peaks, key=lambda i: -hist[i])[:5]]
    roots = []
    for ang in dirs:
        d2 = Vector((math.cos(ang), math.sin(ang)))
        mine = [c for c in root_vs if abs((math.atan2(c.y, c.x) - ang + math.pi) % (2 * math.pi) - math.pi) < math.radians(25)]
        if not mine:
            continue
        L = max(Vector((c.x, c.y)).dot(d2) for c in mine)

        def under(sd, mine=mine, d2=d2):
            near = [c.z for c in mine if abs(Vector((c.x, c.y)).dot(d2) - sd) < 0.15 * L]
            return min(near) if near else 0.0
        roots.append((ang, L, under))
    return top, trunk_r, roots


def rig_prop(prop, segments=7):
    """Armature for a prop: a trunk bone and a chain of `segments` bones along each root's underside,
    from inside the trunk wall to the tip, shorter toward the tip (like finger joints) so tips can follow
    tight curves; the prop gets automatic weights on it. Cached on the prop (delete <prop>_rig to redo)."""
    name = prop.name + "_rig"
    if name in bpy.data.objects:
        return bpy.data.objects[name]
    top, trunk_r, roots = _root_layout(prop)
    arm = bpy.data.armatures.new(name)
    rig = bpy.data.objects.new(name, arm)
    for c in prop.users_collection:
        c.objects.link(rig)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    trunk = arm.edit_bones.new("trunk")
    trunk.head, trunk.tail = (0, 0, 0.0), (0, 0, top)
    for k, (ang, L, under) in enumerate(roots):
        d = Vector((math.cos(ang), math.sin(ang), 0))
        s0 = 0.6 * trunk_r
        prev = None
        for j in range(segments):
            # finger-like: long bones near the trunk, progressively shorter toward the tip
            f = lambda x: 1 - (1 - x) ** 2
            a_, b_ = s0 + (L - s0) * f(j / segments), s0 + (L - s0) * f((j + 1) / segments)
            eb = arm.edit_bones.new(f"root{k}_{j}")
            eb.head = d * a_ + Vector((0, 0, under(a_)))
            eb.tail = d * b_ + Vector((0, 0, under(b_)))
            eb.parent = prev or trunk
            eb.use_connect = prev is not None
            prev = eb
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.ops.object.select_all(action="DESELECT")
    prop.select_set(True)
    rig.select_set(True)
    bpy.context.view_layer.objects.active = rig
    prop.vertex_groups.clear()
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    # heat weighting can miss parts of a generated mesh: give those to the nearest bone
    names = [b.name for b in arm.bones]
    for v in prop.data.vertices:
        if not any(g.weight > 0 for g in v.groups):
            best = min(arm.bones, key=lambda b: _seg_dist(v.co, b.head_local, b.tail_local))
            (prop.vertex_groups.get(best.name) or prop.vertex_groups.new(name=best.name)).add([v.index], 1.0, "REPLACE")
    # everything above the roots (trunk, branches, leaf cards: loose pieces heat weighting can't
    # reach) rides the trunk bone only
    tg = prop.vertex_groups.get("trunk") or prop.vertex_groups.new(name="trunk")
    hi = [v.index for v in prop.data.vertices if v.co.z > 0.5 * top]
    for g in prop.vertex_groups:
        if g != tg:
            g.remove(hi)
    tg.add(hi, 1.0, "REPLACE")
    prop.parent = None
    for m in [m for m in prop.modifiers if m.type == "ARMATURE"]:
        prop.modifiers.remove(m)
    rig.hide_set(True)
    rig.hide_render = True
    return rig


def _seg_dist(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-12)))
    return (p - (a + ab * t)).length


def _pose_roots(o, prop, T, rot, scale, bvh, M, Mi, loc, n, offset):
    """Pose a copy of the prop's root rig so each root chain follows the body surface, bake it into o."""
    src_rig = rig_prop(prop)
    rig = src_rig.copy()
    rig.data = src_rig.data.copy()
    for c in o.users_collection:
        c.objects.link(rig)
    rig.hide_set(False)
    rig.matrix_world = T
    # o currently holds the prop's rest coordinates: bind it to the placed rig
    for m in [m for m in o.modifiers if m.type == "ARMATURE"]:
        o.modifiers.remove(m)
    o.matrix_world = T
    mod = o.modifiers.new("Arm", "ARMATURE")
    mod.object = rig
    bpy.context.view_layer.update()
    W = rig.matrix_world
    Wi = W.inverted()

    def surface(q, nrm):
        hit, hn, _, _ = bvh.ray_cast(q + nrm * 0.15, -nrm, 0.4)
        if hit is None:
            hit, hn, _, _ = bvh.find_nearest(q)
        return hit, (hn if hn.dot(nrm) > 0 else -hn).normalized()

    chains = {}
    for b in rig.data.bones:
        if b.name.startswith("root"):
            k, j = b.name[4:].split("_")
            chains.setdefault(int(k), []).append((int(j), b.name))
    n_loc = (Mi.to_3x3() @ n).normalized()
    for k, bones in chains.items():
        bones = [nm for _, nm in sorted(bones)]
        pb0 = rig.pose.bones[bones[0]]
        head_w = W @ pb0.bone.head_local
        q, qn = surface(Mi @ head_w, n_loc)
        d_w = (W.to_3x3() @ (pb0.bone.tail_local - pb0.bone.head_local)).normalized()
        d = (Mi.to_3x3() @ d_w).normalized()
        for nm in bones:
            pb = rig.pose.bones[nm]
            bpy.context.view_layer.update()
            head = W @ pb.head            # posed head (follows the parent)
            seg = (pb.bone.tail_local - pb.bone.head_local).length * scale
            tan = (d - qn * d.dot(qn)).normalized()
            q, qn = surface(q + tan * seg, qn)
            d = tan
            target = M @ (q + qn * offset)
            want = (target - head).normalized()
            rest = pb.bone.matrix_local
            rest_dir_w = (W.to_3x3() @ (rest.to_3x3() @ Vector((0, 1, 0)))).normalized()
            Rw = rest_dir_w.rotation_difference(want).to_matrix()
            world_m = Matrix.Translation(head) @ (Rw @ (W.to_3x3() @ rest.to_3x3())).to_4x4()
            pb.matrix = Wi @ world_m
    bpy.context.view_layer.update()
    ev = o.evaluated_get(bpy.context.evaluated_depsgraph_get())
    me = bpy.data.meshes.new_from_object(ev)
    o.modifiers.remove(mod)
    old = o.data
    o.data = me
    if old.users == 0:
        bpy.data.meshes.remove(old)
    me.transform(o.matrix_world)
    o.matrix_world = Matrix.Identity(4)
    bpy.data.objects.remove(rig, do_unlink=True)


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



# ---------------------------------------------------------------- Valheim leaf cards
# Valheim's foliage cards are tiny (64px) textures showing a whole spray/cluster of small leaves,
# point-filtered, alpha cut out. These use the game's own textures (exported with gldev_photo into
# .work/model/vanilla_leaves; not shipped: in game the vanilla materials are borrowed at runtime).
VANILLA_LEAVES = {
    # name: (texture file, UV box (u0, v0, u1, v1) of the leafy part, game prefab/material it comes from)
    "bush": ("texture_Bush01.png", (0.0, 0.0, 1.0, 1.0), "Bush01"),
    "spray": ("texture_shrub.png", (0.45, 0.0, 1.0, 0.62), "shrub_2"),
    "beech": ("texture_beech_leaf_small.png", (0.0, 0.0, 1.0, 1.0), "Beech_small1"),
}


def _vanilla_leaf_material(path, kind):
    name = "VLeaf_" + kind
    m = bpy.data.materials.get(name)
    if m:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = bpy.data.images.load(path, check_existing=True)
    tex.interpolation = "Closest"
    bsdf = nt.nodes["Principled BSDF"]
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    bsdf.inputs["Roughness"].default_value = 0.9
    nt.nodes.active = tex
    m.use_backface_culling = False
    return m


def make_vanilla_leaves(name, tex_dir, kind="spray", count=1, size=0.22, spread=80.0, droop=20.0):
    """Leaf prop from a vanilla foliage texture: `count` cards fanned from a base point at the origin.
    Each card is a pointed shape (wide at the top, pointed at the base) so it tucks into the body."""
    import bmesh
    import os
    tex, (u0, v0, u1, v1), _ = VANILLA_LEAVES[kind]
    aspect = (u1 - u0) / (v1 - v0)
    shape = [(0.0, 0.0), (0.45, 0.35), (0.5, 0.85), (0.15, 1.0), (-0.15, 1.0), (-0.5, 0.85), (-0.45, 0.35)]
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap")
    for i in range(count):
        a = 0.0 if count == 1 else -spread / 2 + spread * i / (count - 1)
        rot = Matrix.Rotation(math.radians(a), 3, "Y") @ Matrix.Rotation(math.radians(-droop), 3, "X")
        w, h = size * aspect, size
        vs = [bm.verts.new(rot @ Vector((x * w, i * 0.003, y * h))) for x, y in shape]
        f = bm.faces.new(vs)
        for l, (x, y) in zip(f.loops, shape):
            l[uvl].uv = (u0 + (x + 0.5) * (u1 - u0), v0 + y * (v1 - v0))
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    o = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(o)
    me.materials.append(_vanilla_leaf_material(os.path.join(tex_dir, tex), kind))
    return o
