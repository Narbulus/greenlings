"""
Rig and export the generated greenling variants (tools/tripo briefs: bare, mushroom, leafy, sprout).

    import greenling_variants as gv
    gv.finish("Bare", fit, out_json)     # after greenling_tripo.import_generated/decimate/bake_texture

fit = {"leg": (x, ...), "arm_dz": dz, "eyes": {...}}: measured on each generated mesh (leg column
centres from horizontal slices, arm height from slices along x, eye centres from the bright eye
texels). All four briefs share the greyling body, so the leg shape is the same and only the
spacing, arm height and eyes differ.
"""
import collections

import bpy
from mathutils import Vector

import greenling_io as gio
import greenling_tripo as gt

RIG = "Greyling_src_rig"
SRC = "Greyling_src"
LEG_BONES = ("l_hip", "l_leg1", "l_leg2", "l_foot", "r_hip", "r_leg1", "r_leg2", "r_foot")
ARM_CHAIN = ("arm1", "arm2", "hand", "index1", "index2", "middle1", "middle2", "pinky1", "pinky2")


def targets(leg_x, arm_dz, arm_dy=0.13):
    rig = bpy.data.objects[RIG]
    W = rig.matrix_world
    T = {}
    for s, sx in (("l", 1), ("r", -1)):
        # bent-knee legs: hip behind, knee forward, ankle back (the Tripo bodies stand like this)
        T[f"{s}_leg1"] = Vector((sx * (leg_x - 0.04), 0.10, 50.72))
        T[f"{s}_leg2"] = Vector((sx * leg_x, -0.02, 50.49))
        T[f"{s}_foot"] = Vector((sx * (leg_x + 0.01), 0.18, 50.09))
        # the generated arms sit a little higher and further back than the vanilla ones
        for b in ARM_CHAIN:
            T[f"{s}_{b}"] = W @ rig.data.bones[f"{s}_{b}"].head_local + Vector((0, arm_dy, arm_dz))
    return T


def weight_fix(o, rig):
    """Shoulder ornaments (branches, mushrooms, leaf tufts) ride the torso; leaf skirts don't follow the legs."""
    names = {g.index: g.name for g in o.vertex_groups}
    top, skirt = [], []
    for v in o.data.vertices:
        p = o.matrix_world @ v.co
        dom = names[max(v.groups, key=lambda g: g.weight).group] if v.groups else None
        if abs(p.x) > 0.18 and p.z > 51.45 and abs(p.x) < 0.75:
            top.append(v.index)
        elif dom in LEG_BONES and p.z > 50.55 and (p.y > 0.26 or abs(p.x) > 0.36):
            skirt.append(v.index)
    for g in o.vertex_groups:
        g.remove(top + skirt)
    o.vertex_groups["spine3"].add(top, 1.0, "REPLACE")
    o.vertex_groups["spine1"].add(skirt, 1.0, "REPLACE")
    o["weight_fix"] = {"spine3": len(top), "spine1": len(skirt), "sides": own_side(o), "arm_bleed": arm_bleed(o, rig)}


def own_side(o, margin=0.04):
    """
    Both shoulder bones start at the same point, so heat weights hand some left-shoulder vertices to
    r_shoulder (and the reverse); those then follow the other arm and stretch across the chest.
    Vertices clearly on one side (|x| > margin) move any other-side weight to their own side's twin.
    """
    names = {g.index: g.name for g in o.vertex_groups}
    moved = 0
    for v in o.data.vertices:
        x = (o.matrix_world @ v.co).x
        if abs(x) <= margin:
            continue
        own, other = ("l_", "r_") if x > 0 else ("r_", "l_")
        # copy out first: editing groups invalidates the vertex's group elements
        for name, w in [(names[g.group], g.weight) for g in v.groups if names[g.group].startswith(other) and g.weight > 0]:
            o.vertex_groups[name].remove([v.index])
            o.vertex_groups[own + name[2:]].add([v.index], w, "ADD")
            moved += 1
    return moved


def arm_bleed(o, rig, reach=0.18, keep=0.6):
    """
    Heat weights let the arm bones claim leaves and torso hanging beside the arm, which then stretch
    into long strips from shoulder to elbow when the arm swings. Anything further than `reach` from
    the arm's bone segments loses its shoulder/arm weight to spine3 (the vanilla torso bone), unless
    the arm already owns most of it (weight >= `keep`): those are arm surface, and cutting them loose
    turns the long triangles of a low-poly arm into sails from shoulder to elbow.
    """
    from mathutils.geometry import intersect_point_line
    H = {b.name: rig.matrix_world @ b.head_local for b in rig.data.bones}
    names = {g.index: g.name for g in o.vertex_groups}

    def dist(p, s):
        best = 9.0
        for a, b in ((f"{s}_arm1", f"{s}_arm2"), (f"{s}_arm2", f"{s}_hand")):
            _, t = intersect_point_line(p, H[a], H[b])
            best = min(best, (p - H[a].lerp(H[b], max(0.0, min(1.0, t)))).length)
        return best

    moved = 0
    for v in o.data.vertices:
        arm = [g for g in v.groups if names[g.group][2:] in ("shoulder", "arm1", "arm2") and g.weight > 0]
        w = sum(g.weight for g in arm)
        if not arm or w >= keep or dist(o.matrix_world @ v.co, names[arm[0].group][0]) <= reach:
            continue
        for g in arm:
            o.vertex_groups[g.group].remove([v.index])
        o.vertex_groups["spine3"].add([v.index], w, "ADD")
        moved += 1
    return moved


def finish(name, leg_x, arm_dz, eyes, out_json, template_json, eye_push=0.012):
    o = bpy.data.objects[name]
    info = gt.rig_to_vanilla(o, RIG, targets(leg_x, arm_dz), weight_fix=weight_fix)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    if o.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    bpy.ops.object.shade_smooth()
    coll = gt.build_collection(o, SRC, eyes, eye_push=eye_push, dst="GL_" + name)
    counts = gio.export_creature(coll.name, template_json, out_json)
    names = {g.index: g.name for g in o.vertex_groups}
    dom = collections.Counter(names[max(v.groups, key=lambda g: g.weight).group] if v.groups else None for v in o.data.vertices)
    return {**info, "fix": dict(o["weight_fix"]), "counts": counts, "unweighted": dom.get(None, 0)}
