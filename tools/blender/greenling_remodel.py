"""
Greyling -> Greenling remodel, applied to a copy of the imported game mesh.

Every edit moves existing vertices relative to the bone they follow (or adds new geometry
bound to the head), so the game's skeleton and animations keep working unchanged.
All numbers are in P (tune and re-run remodel()).
"""
import math

import bmesh
import bpy
from mathutils import Vector

P = dict(
    belly_peak_z=50.88, belly_width=0.24,   # where the bulge peaks (bind-pose world z) and how tall it is
    belly_front=0.40, belly_back=0.18, belly_side=0.45, belly_drop=0.03,
    belly_push=0.17,                        # absolute forward push of the front torso (metres, bind pose)
    lump_scale=1.25, lump_round=0.30,       # back boulder
    arm_thick=1.45, hand_thick=1.15, shoulder_thick=1.2,
    leg_thick=1.45, hip_thick=1.3, foot_thick=1.1,
    face_wide=1.30, beak_short=0.50, nose_flat=0.70,  # face below the hood: wider, shorter, flatter
    eye_scale=1.75, eye_spread=1.25,
    leaves_per_tip=4, leaf_len=0.13, leaf_wid=0.07,
)


def _dominant(obj, v):
    if not v.groups:
        return None
    g = max(v.groups, key=lambda g: g.weight)
    return obj.vertex_groups[g.group].name


def _closest_on_segment(p, a, b):
    ab = b - a
    t = max(0.0, min(1.0, (p - a).dot(ab) / max(ab.length_squared, 1e-9)))
    return a + ab * t


def _push_from_segment(p, a, b, k):
    c = _closest_on_segment(p, a, b)
    return c + (p - c) * k


def copy_collection(src_name, dst_name):
    old = bpy.data.collections.get(dst_name)
    if old:
        for o in list(old.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(old)
    src = bpy.data.collections[src_name]
    dst = bpy.data.collections.new(dst_name)
    bpy.context.scene.collection.children.link(dst)
    for o in src.objects:
        if o.type != "MESH":
            continue
        c = o.copy()
        c.data = o.data.copy()
        c.name = o.name + "_g"
        c.hide_render = False
        c.hide_viewport = False
        dst.objects.link(c)
    return dst


def remodel(src="Greyling_src", dst="Greenling", rig="Greyling_src_rig", offset_x=0.0):
    coll = copy_collection(src, dst)
    arm = bpy.data.objects[rig]
    H = {b.name: arm.matrix_world @ b.head_local for b in arm.data.bones}
    objs = {o["game_renderer"]: o for o in coll.objects}
    body, lump = objs["Cube"], objs["Cube.001"]
    eyes = [objs["Plane"], objs["Plane.001"]]

    # ---- face mapping (shared by the face vertices and the eye quads) ----
    Mb = body.matrix_world
    face_pts = [Mb @ v.co for v in body.data.vertices
                if _dominant(body, v) == "head" and (Mb @ v.co).y < H["head"].y - 0.04 and (Mb @ v.co).z < H["head"].z + 0.14]
    fc = sum(face_pts, Vector()) / max(len(face_pts), 1)

    def face_map(p):
        q = p.copy()
        q.x = fc.x + (p.x - fc.x) * P["face_wide"]
        if p.z < fc.z:
            q.z = fc.z + (p.z - fc.z) * P["beak_short"]
            q.y = fc.y + (p.y - fc.y) * P["nose_flat"]  # pull the beak back: rounder, less pointy
        return q

    # ---- body ----
    spine_axis_y = H["spine2"].y - 0.04
    segs = {}
    for s in ("l", "r"):
        segs[f"{s}_arm1"] = (H[f"{s}_arm1"], H[f"{s}_arm2"], P["arm_thick"])
        segs[f"{s}_arm2"] = (H[f"{s}_arm2"], H[f"{s}_hand"], P["arm_thick"])
        segs[f"{s}_hand"] = (H[f"{s}_hand"], H[f"{s}_middle1"], P["hand_thick"])
        segs[f"{s}_shoulder"] = (H[f"{s}_arm1"] - (H[f"{s}_arm2"] - H[f"{s}_arm1"]) * 0.3, H[f"{s}_arm1"], P["shoulder_thick"])
        segs[f"{s}_leg1"] = (H[f"{s}_leg1"], H[f"{s}_leg2"], P["leg_thick"])
        segs[f"{s}_leg2"] = (H[f"{s}_leg2"], H[f"{s}_foot"], P["leg_thick"])
        segs[f"{s}_hip"] = (H[f"{s}_leg1"] + (H[f"{s}_leg1"] - H[f"{s}_leg2"]) * 0.25, H[f"{s}_leg1"], P["hip_thick"])
        segs[f"{s}_foot"] = (H[f"{s}_foot"], H[f"{s}_foot"] + Vector((0, -0.12, -0.04)), P["foot_thick"])

    Mi = Mb.inverted()
    tips = {"l": None, "r": None}
    for v in body.data.vertices:
        p = Mb @ v.co
        bone = _dominant(body, v)
        if bone in ("spine1", "spine2", "spine3") or (bone in ("l_hip", "r_hip") and p.z > H["spine1"].z - 0.08):
            b = math.exp(-((p.z - P["belly_peak_z"]) / P["belly_width"]) ** 2)
            dy = p.y - spine_axis_y
            sy = 1 + (P["belly_front"] if dy < 0 else P["belly_back"]) * b
            p = Vector((p.x * (1 + P["belly_side"] * b), spine_axis_y + dy * sy, p.z - P["belly_drop"] * b))
            if dy < 0:  # the front of the torso is shallow: push it out so the belly reads from the side
                fx = max(0.0, 1.0 - (abs(p.x) / 0.42) ** 2)
                p.y -= P["belly_push"] * b * fx
        elif bone in segs:
            a, bb, k = segs[bone]
            p = _push_from_segment(p, a, bb, k)
        elif bone == "head":
            if p.y < H["head"].y - 0.04 and p.z < H["head"].z + 0.14:
                p = face_map(p)
        if abs(p.x) > 0.2 and p.z > H["head"].z + 0.15:  # shoulder branches (they follow spine3): remember the tips
            side = "l" if p.x > 0 else "r"
            if tips[side] is None or p.z > tips[side].z:
                tips[side] = p.copy()
        v.co = Mi @ p

    # ---- back lump: bigger and rounder ----
    Ml = lump.matrix_world
    pts = [Ml @ v.co for v in lump.data.vertices]
    lc = sum(pts, Vector()) / len(pts)
    avg_r = sum((q - lc).length for q in pts) / len(pts)
    for v, q in zip(lump.data.vertices, pts):
        d = q - lc
        d = d.lerp(d.normalized() * avg_r, P["lump_round"]) * P["lump_scale"]
        v.co = Ml.inverted() @ (lc + d)

    # ---- eyes: follow the face, then grow ----
    for e in eyes:
        Me = e.matrix_world
        pts = [Me @ v.co for v in e.data.vertices]
        ec = sum(pts, Vector()) / len(pts)
        nc = face_map(ec)
        nc.x = fc.x + (nc.x - fc.x) * P["eye_spread"] / P["face_wide"] * P["face_wide"] ** 0.5
        for v, q in zip(e.data.vertices, pts):
            v.co = Me.inverted() @ (nc + (q - ec) * P["eye_scale"])

    body.data.update(); lump.data.update()
    for e in eyes:
        e.data.update()

    _add_leaves(body, tips)
    for o in coll.objects:
        o.location.x += offset_x
    return {"face_center": list(fc), "tips": {k: list(v) if v else None for k, v in tips.items()}}


def _green_uvs(image):
    """Two texel UVs from the texture: a light and a dark leafy green."""
    w, h = image.size
    px = list(image.pixels)
    best = []
    for y in range(h):
        for x in range(w):
            i = (y * w + x) * 4
            r, g, b = px[i], px[i + 1], px[i + 2]
            if g > r * 1.25 and g > b * 1.4 and g > 0.18:
                best.append((g, (x + 0.5) / w, (y + 0.5) / h))
    best.sort()
    if not best:
        return [(0.5, 0.5), (0.5, 0.5)]
    return [(best[-1][1], best[-1][2]), (best[len(best) // 3][1], best[len(best) // 3][2])]


def _add_leaves(body, tips):
    img = next((n.image for m in body.data.materials for n in m.node_tree.nodes if n.type == "TEX_IMAGE"), None)
    greens = _green_uvs(img) if img else [(0.5, 0.5)] * 2
    bm = bmesh.new()
    bm.from_mesh(body.data)
    uv_layer = bm.loops.layers.uv.active
    deform = bm.verts.layers.deform.verify()
    head_group = body.vertex_groups["spine3"].index  # the branches grow from the back and follow spine3
    Mi = body.matrix_world.inverted()
    for side, tip in tips.items():
        if tip is None:
            continue
        sx = 1 if side == "l" else -1
        for i in range(P["leaves_per_tip"]):
            ang = math.radians(-40 + i * (100 / max(P["leaves_per_tip"] - 1, 1)))
            out = Vector((sx * math.cos(ang) * 0.6, -0.15 + 0.1 * (i % 2), math.sin(ang) * 0.6 + 0.55)).normalized()
            side_v = out.cross(Vector((0, 1, 0))).normalized()
            if side_v.length < 0.1:
                side_v = Vector((1, 0, 0))
            L, W = P["leaf_len"] * (0.85 + 0.15 * (i % 3)), P["leaf_wid"]
            quad = [tip, tip + out * L * 0.45 + side_v * W * 0.5, tip + out * L, tip + out * L * 0.45 - side_v * W * 0.5]
            uv = greens[i % 2]
            for flip in (False, True):  # double-sided: the game shader culls back faces
                vs = [bm.verts.new(Mi @ q) for q in quad]
                for bv in vs:
                    bv[deform][head_group] = 1.0
                f = bm.faces.new(list(reversed(vs)) if flip else vs)
                f.smooth = False
                for loop in f.loops:
                    loop[uv_layer].uv = uv
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()


def preview_eye_material(coll="Greenling"):
    """Blender-only stand-in for the game's glowing eye material."""
    mat = bpy.data.materials.get("PreviewEye") or bpy.data.materials.new("PreviewEye")
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = (1.0, 0.85, 0.45, 1)
    bsdf.inputs["Emission Color"].default_value = (1.0, 0.8, 0.35, 1)
    bsdf.inputs["Emission Strength"].default_value = 4.0
    for o in bpy.data.collections[coll].objects:
        if o.get("game_renderer", "").startswith("Plane"):
            o.data.materials.clear()
            o.data.materials.append(mat)
