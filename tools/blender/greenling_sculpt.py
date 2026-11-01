"""
Greenling sculpt: new geometry for the parts that can't be had by nudging vertices.

    build() -> report dict
        1. copy the game greyling (limbs thickened, back lump rounded: greenling_remodel)
        2. replace the beak/hood front with a broad, rounded face (+ small nose), bound to "head"
        3. add a round, mossy belly mass on the lower torso, bound to spine1/spine2
        4. leafy twigs on the shoulder branches, bound to "spine3"
        5. eyes re-seated on the new face, bigger
        6. a greenling texture: the original atlas (2x, nearest) plus painted belly/face/leaf patches

All new geometry gets explicit skin weights on existing bones, so the vanilla skeleton and
animations still drive the model. Shapes live in S; tune and re-run build().
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import greenling_remodel as gr

S = dict(
    face_center=(0.0, -0.15, 51.18), face_radius=0.13, face_scale=(1.22, 0.8, 1.0),
    face_segments=10, face_rings=7, nose_forward=0.045, nose_drop=0.035,
    eye_offset_x=0.056, eye_offset_z=0.03, eye_w=0.056, eye_h=0.034,
    belly_radius=0.26, belly_scale=(1.1, 0.85, 0.86), belly_z=50.85, belly_out=0.17,
    belly_segments=12, belly_rings=8,
    leaf_len=0.10, leaf_wid=0.065, leaves=4, twig_len=0.07,
    moss_boost=1.18,
)

# texture regions (uv rects u0 u1 v0 v1) of the ORIGINAL atlas, so new parts share its chunky style:
# the mossiest and the most wood-like 12px windows (found by scanning the 128px greyling texture)
PATCH_BELLY = (48 / 128, 60 / 128, 40 / 128, 52 / 128)
PATCH_FACE = (18 / 128, 30 / 128, 8 / 128, 20 / 128)
PATCH_LEAF = None  # set from the brightest moss texels at build time


# ---------------------------------------------------------------------------------- helpers

def _ellipsoid(bm, center, radius, scale, segments, rings):
    """Low-poly UV sphere as new bmesh geometry; returns its verts."""
    before = set(bm.verts)
    bmesh.ops.create_uvsphere(bm, u_segments=segments, v_segments=rings, radius=radius)
    new = [v for v in bm.verts if v not in before]
    for v in new:
        v.co = Vector((v.co.x * scale[0], v.co.y * scale[1], v.co.z * scale[2])) + Vector(center)
    return new


def _faces_of(bm, verts):
    vs = set(verts)
    return [f for f in bm.faces if all(v in vs for v in f.verts)]


def _planar_uv(bm, faces, uv_layer, rect, axes=("x", "z")):
    """Front projection of faces into a texture rect."""
    vs = {v for f in faces for v in f.verts}
    a = [getattr(v.co, axes[0]) for v in vs]
    b = [getattr(v.co, axes[1]) for v in vs]
    amin, amax, bmin, bmax = min(a), max(a), min(b), max(b)
    u0, u1, v0, v1 = rect
    for f in faces:
        for loop in f.loops:
            co = loop.vert.co
            loop[uv_layer].uv = (u0 + (getattr(co, axes[0]) - amin) / max(amax - amin, 1e-6) * (u1 - u0),
                                 v0 + (getattr(co, axes[1]) - bmin) / max(bmax - bmin, 1e-6) * (v1 - v0))


def _weight(bm, verts, group_weights):
    deform = bm.verts.layers.deform.verify()
    for v in verts:
        v[deform].clear()
        for g, w in group_weights(v).items():
            if w > 0:
                v[deform][g] = w


# ---------------------------------------------------------------------------------- texture

def make_texture(src_image, out_path, name="GreenlingTex"):
    w, h = src_image.size
    src = list(src_image.pixels)
    W, H = w * 2, h * 2
    img = bpy.data.images.get(name) or bpy.data.images.new(name, W, H, alpha=True)
    if tuple(img.size) != (W, H):
        img.scale(W, H)
    px = [0.0] * (W * H * 4)
    for y in range(H):
        for x in range(W):
            i = ((y // 2) * w + (x // 2)) * 4
            r, g, b, a = src[i:i + 4]
            if g > r * 1.1 and g > b * 1.2:  # moss: a bit greener and brighter
                r, g, b = r * 0.95, min(1.0, g * S["moss_boost"]), b * 0.9
            o = (y * W + x) * 4
            px[o:o + 4] = [r, g, b, a]

    rnd = random.Random(7)

    def paint(rect, fn, cell=2):
        u0, u1, v0, v1 = rect
        x0, x1, y0, y1 = int(u0 * W) - 2, int(u1 * W) + 2, int(v0 * H) - 2, int(v1 * H) + 2
        for cy in range(y0, y1, cell):
            for cx in range(x0, x1, cell):
                c = fn((cx - x0) / max(x1 - x0, 1), (cy - y0) / max(y1 - y0, 1), rnd.random())
                for y in range(cy, min(cy + cell, y1)):
                    for x in range(cx, min(cx + cell, x1)):
                        o = (y * W + x) * 4
                        px[o:o + 4] = [c[0], c[1], c[2], 1.0]

    def moss(u, v, r):
        base = (0.34, 0.55, 0.20)
        blot = 0.5 + 0.5 * math.sin(u * 9.0 + math.sin(v * 7.0) * 2.0) * math.cos(v * 8.0 + u * 3.0)
        k = 0.82 + 0.3 * blot + 0.12 * (r - 0.5)
        if r > 0.94:
            return (0.46, 0.32, 0.18)  # brown fleck
        return (base[0] * k, base[1] * k, base[2] * k)

    def wood(u, v, r):
        grain = 0.5 + 0.5 * math.sin(u * 22.0 + math.sin(v * 5.0) * 1.5)
        k = 0.9 + 0.12 * grain + 0.06 * (r - 0.5)
        cheek = math.exp(-((u - 0.5) ** 2 + (v - 0.45) ** 2) * 6) * 0.08  # warmer, lighter middle
        return (min(1, 0.86 * k + cheek), min(1, 0.55 * k + cheek * 0.6), 0.30 * k)

    def leaf(u, v, r):
        k = 0.85 + 0.25 * v + 0.08 * (r - 0.5)
        return (0.42 * k, 0.72 * k, 0.26 * k)

    paint(PATCH_BELLY, moss)
    paint(PATCH_FACE, wood)
    paint(PATCH_LEAF, leaf)
    img.pixels[:] = px
    img.filepath_raw = out_path
    img.file_format = "PNG"
    img.save()
    return img


def _greenling_material(src_mat, image):
    mat = bpy.data.materials.get("GreenlingMat") or src_mat.copy()
    mat.name = "GreenlingMat"
    for n in mat.node_tree.nodes:
        if n.type == "TEX_IMAGE":
            n.image = image
    return mat


# ---------------------------------------------------------------------------------- parts

def _remove_beak(bm, body, H, Mb):
    deform = bm.verts.layers.deform.verify()
    head = body.vertex_groups["head"].index
    hy, hz = H["head"].y, H["head"].z

    def is_face(v):
        p = Mb @ v.co
        w = v[deform].get(head, 0.0)
        return w > 0.5 and p.y < hy - 0.03 and p.z < hz + 0.12

    doomed = [f for f in bm.faces if all(is_face(v) for v in f.verts)]
    bmesh.ops.delete(bm, geom=doomed, context="FACES_ONLY")
    loose = [v for v in bm.verts if not v.link_faces]
    bmesh.ops.delete(bm, geom=loose, context="VERTS")
    return len(doomed)


def _add_face(bm, body, Mi, uv_layer):
    c = Vector(S["face_center"])
    verts = _ellipsoid(bm, c, S["face_radius"], S["face_scale"], S["face_segments"], S["face_rings"])
    # nose: the front vertex just below the middle comes forward a little
    front = sorted(verts, key=lambda v: (v.co.y, abs(v.co.z - (c.z - S["nose_drop"]))))
    nose = min(front[:S["face_segments"]], key=lambda v: abs(v.co.x) + abs(v.co.z - (c.z - S["nose_drop"])))
    nose.co.y -= S["nose_forward"]
    for v in verts:
        v.co = Mi @ v.co
    faces = _faces_of(bm, verts)
    for f in faces:
        f.smooth = True
    head = body.vertex_groups["head"].index
    _weight(bm, verts, lambda v: {head: 1.0})
    # project in world space orientation (x across, z up) after converting back temporarily
    _planar_uv(bm, faces, uv_layer, PATCH_FACE, axes=("x", "y"))  # body-local axes: x across, local y is world z
    return c, verts


def _add_belly(bm, body, Mb, Mi, uv_layer):
    spine1 = body.vertex_groups["spine1"].index
    spine2 = body.vertex_groups["spine2"].index
    deform = bm.verts.layers.deform.verify()
    # how far forward does the torso front reach at belly height?
    front_y = min((Mb @ v.co).y for v in bm.verts
                  if abs((Mb @ v.co).x) < 0.22 and abs((Mb @ v.co).z - S["belly_z"]) < 0.08
                  and (v[deform].get(spine1, 0) + v[deform].get(spine2, 0)) > 0.3)
    ry = S["belly_radius"] * S["belly_scale"][1]
    c = Vector((0.0, front_y - S["belly_out"] + ry, S["belly_z"]))
    verts = _ellipsoid(bm, c, S["belly_radius"], S["belly_scale"], S["belly_segments"], S["belly_rings"])

    def wts(v):
        t = max(0.0, min(1.0, (v.co.z - (S["belly_z"] - 0.05)) / 0.3))
        return {spine1: 1.0 - 0.55 * t, spine2: 0.55 * t}

    _weight(bm, verts, wts)  # weights computed from world z before converting
    for v in verts:
        v.co = Mi @ v.co
    faces = _faces_of(bm, verts)
    for f in faces:
        f.smooth = True
    _planar_uv(bm, faces, uv_layer, PATCH_BELLY, axes=("x", "y"))
    return c, front_y


def _add_twigs(bm, body, Mi, uv_layer, tips):
    spine3 = body.vertex_groups["spine3"].index
    added = []
    for side, tip in tips.items():
        if tip is None:
            continue
        sx = 1 if side == "l" else -1
        twig_dir = Vector((sx * 0.35, -0.1, 1.0)).normalized()
        end = tip + twig_dir * S["twig_len"]
        # twig: thin 3-sided spike from the branch tip
        side_a = twig_dir.cross(Vector((0, 1, 0))).normalized() * 0.008
        side_b = twig_dir.cross(side_a).normalized() * 0.008
        ring = [bm.verts.new(Mi @ (tip + side_a)), bm.verts.new(Mi @ (tip - side_a * 0.5 + side_b)), bm.verts.new(Mi @ (tip - side_a * 0.5 - side_b))]
        apex = bm.verts.new(Mi @ end)
        twig_faces = [bm.faces.new((ring[i], ring[(i + 1) % 3], apex)) for i in range(3)]
        for f in twig_faces:
            for loop in f.loops:
                loop[uv_layer].uv = (0.10, 0.62)  # branch wood
        added += ring + [apex]
        # leaves fanned around the twig end, plus one halfway along
        anchors = [end] * (S["leaves"] - 1) + [tip + twig_dir * S["twig_len"] * 0.45]
        for i, a in enumerate(anchors):
            yaw = math.radians(i * (360 / S["leaves"]) + (25 if side == "l" else -25))
            out = Vector((math.cos(yaw), math.sin(yaw) * 0.8, 0.85)).normalized()
            if i == len(anchors) - 1:
                out = Vector((sx, -0.3, 0.4)).normalized()
            wv = out.cross(Vector((0, 0, 1)))
            wv = (wv if wv.length > 0.1 else Vector((1, 0, 0))).normalized()
            L, Wd = S["leaf_len"] * (0.8 + 0.1 * (i % 3)), S["leaf_wid"]
            pts = [a, a + out * L * 0.4 + wv * Wd * 0.5 + Vector((0, 0, 0.008)), a + out * L, a + out * L * 0.4 - wv * Wd * 0.5 + Vector((0, 0, 0.008))]
            for flip in (False, True):
                vs = [bm.verts.new(Mi @ p) for p in pts]
                f = bm.faces.new(list(reversed(vs)) if flip else vs)
                f.smooth = False
                for loop, uv in zip(f.loops if not flip else f.loops, [(0.5, 0.0), (0.0, 0.4), (0.5, 1.0), (1.0, 0.4)] if not flip else [(1.0, 0.4), (0.5, 1.0), (0.0, 0.4), (0.5, 0.0)]):
                    u0, u1, v0, v1 = PATCH_LEAF
                    loop[uv_layer].uv = (u0 + uv[0] * (u1 - u0), v0 + uv[1] * (v1 - v0))
                added += vs
    _weight(bm, added, lambda v: {spine3: 1.0})


def _seat_eyes(eyes, face_center):
    c = Vector(face_center)
    rx = S["face_radius"] * S["face_scale"][0]
    ry = S["face_radius"] * S["face_scale"][1]
    rz = S["face_radius"] * S["face_scale"][2]
    for e in eyes:
        Me = e.matrix_world
        pts = [Me @ v.co for v in e.data.vertices]
        oc = sum(pts, Vector()) / len(pts)
        sx = 1 if oc.x > 0 else -1
        x, z = sx * S["eye_offset_x"], S["eye_offset_z"]
        y = -ry * math.sqrt(max(0.05, 1 - (x / rx) ** 2 - (z / rz) ** 2)) - 0.004
        nc = c + Vector((x, y, z))
        wid = max(q.x for q in pts) - min(q.x for q in pts)
        hgt = max(q.z for q in pts) - min(q.z for q in pts)
        for v, q in zip(e.data.vertices, pts):
            o = q - oc
            v.co = Me.inverted() @ (nc + Vector((o.x / max(wid, 1e-4) * S["eye_w"], 0.0, o.z / max(hgt, 1e-4) * S["eye_h"])))
        e.data.update()


# ---------------------------------------------------------------------------------- build

def build(src="Greyling_src", dst="Greenling", rig="Greyling_src_rig", texture_out=None):
    # base: limbs + back lump from the remodel, without its vertex-dragging belly/face/leaves
    gr.P.update(belly_front=0.0, belly_back=0.0, belly_side=0.0, belly_drop=0.0, belly_push=0.0,
                face_wide=1.0, beak_short=1.0, nose_flat=1.0, eye_scale=1.0, eye_spread=1.0, leaves_per_tip=0)
    info = gr.remodel(src, dst, rig)
    coll = bpy.data.collections[dst]
    objs = {o["game_renderer"]: o for o in coll.objects}
    body, lump = objs["Cube"], objs["Cube.001"]
    arm = bpy.data.objects[rig]
    H = {b.name: arm.matrix_world @ b.head_local for b in arm.data.bones}
    Mb = body.matrix_world
    Mi = Mb.inverted()
    tips = {k: (Vector(v) if v else None) for k, v in info["tips"].items()}

    global PATCH_LEAF
    src_mat = body.data.materials[0]
    src_img = next(n.image for n in src_mat.node_tree.nodes if n.type == "TEX_IMAGE")
    (gu, gv), _ = gr._green_uvs(src_img)
    PATCH_LEAF = (gu - 1 / 256, gu + 1 / 256, gv - 1 / 256, gv + 1 / 256)
    img = make_texture(src_img, texture_out) if texture_out else None
    if img:
        mat = _greenling_material(src_mat, img)
        for o in (body, lump):
            o.data.materials.clear()
            o.data.materials.append(mat)

    bm = bmesh.new()
    bm.from_mesh(body.data)
    uv_layer = bm.loops.layers.uv.active
    removed = _remove_beak(bm, body, H, Mb)
    face_c, _ = _add_face(bm, body, Mi, uv_layer)
    belly_c, front_y = _add_belly(bm, body, Mb, Mi, uv_layer)
    _add_twigs(bm, body, Mi, uv_layer, tips)
    bm.to_mesh(body.data)
    bm.free()
    _seat_eyes([objs["Plane"], objs["Plane.001"]], face_c)

    # smooth shading from the new shape (custom normals from the game would no longer fit)
    for o in (body, lump):
        if o.data.has_custom_normals:
            with bpy.context.temp_override(object=o, active_object=o, selected_objects=[o]):
                bpy.ops.mesh.customdata_custom_splitnormals_clear()
        o.data.update()
    return {"beak_faces_removed": removed, "face_center": list(face_c), "belly_center": list(belly_c),
            "torso_front_y": front_y, "verts": len(body.data.vertices)}
