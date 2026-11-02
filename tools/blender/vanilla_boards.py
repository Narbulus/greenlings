"""
Reference boards of the vanilla creatures, laid out like the Tripo briefs: one image, four views
side by side (front, side facing image right, side facing image left, back), bind pose (arms out),
orthographic, game textures, dark background.

    import vanilla_boards as vb
    vb.render_all(r"<repo>/.work/model/vanilla", r"<out dir>")

Inputs per prefab in the vanilla dir: <Prefab>.json (gldev_export, root at the origin facing -Y)
and texture_<material>.png (gldev_photo). Shader colour shifts (_Hue/_Saturation, greyling only)
are applied to the texture so the board matches the game.
"""
import colorsys
import math
import os

import bpy
from mathutils import Vector

import greenling_io as gio

PREFABS = {
    # prefab: (skin texture, shader hue, shader saturation, eye colour)
    "Greyling": ("texture_greyling.png", -0.03, 0.121, (1.0, 0.8, 0.2)),
    "Greydwarf": ("texture_greydwarf.png", 0.0, 0.0, (0.4, 0.6, 1.0)),
    "Greydwarf_Elite": ("texture_greydwarf_elite.png", 0.0, 0.0, (1.0, 0.1, 0.2)),
    "Greydwarf_Shaman": ("texture_greydwarf_shaman.png", 0.0, 0.0, (0.4, 1.0, 0.3)),
}
BG = (0.92, 0.92, 0.92)    # light grey board background
VIEWS = (("front", 0), ("right", 90), ("left", -90), ("back", 180))   # camera yaw around the creature


def _shader_texture(path, hue, sat):
    img = bpy.data.images.load(path, check_existing=False)
    if hue or sat:
        px = list(img.pixels)
        for i in range(0, len(px), 4):
            h, s, v = colorsys.rgb_to_hsv(px[i], px[i + 1], px[i + 2])
            px[i:i + 3] = colorsys.hsv_to_rgb((h + hue) % 1.0, min(1.0, s + sat), v)
        img.pixels[:] = px
    img.alpha_mode = "NONE"   # some exports carry alpha=0 texels; the game ignores alpha here
    return img


PARENTS = {
    "l_hip": "root", "l_leg1": "l_hip", "l_leg2": "l_leg1", "l_foot": "l_leg2",
    "r_hip": "root", "r_leg1": "r_hip", "r_leg2": "r_leg1", "r_foot": "r_leg2",
    "spine1": "root", "spine2": "spine1", "spine3": "spine2", "head": "spine3",
    "l_shoulder": "spine3", "l_arm1": "l_shoulder", "l_arm2": "l_arm1", "l_hand": "l_arm2",
    "r_shoulder": "spine3", "r_arm1": "r_shoulder", "r_arm2": "r_arm1", "r_hand": "r_arm2",
}
for _s in "lr":
    for _f in ("index", "middle", "pinky"):
        PARENTS[f"{_s}_{_f}1"] = f"{_s}_hand"
        PARENTS[f"{_s}_{_f}2"] = f"{_s}_{_f}1"


def stand(rig, lift):
    """
    Straighten the bind pose's deep crouch toward the in-game stance: raise everything by `lift`
    (world units) and re-solve each leg (two bones, lengths kept) so the feet stay planted and the
    knees bend in their original plane. Arms stay straight out. Returns helper objects (none).
    """
    from mathutils import Matrix
    bpy.context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode="EDIT")
    eb = rig.data.edit_bones
    for child, parent in PARENTS.items():
        if child in eb and parent in eb:
            eb[child].parent = eb[parent]
    bpy.ops.object.mode_set(mode="OBJECT")
    W = rig.matrix_world
    Wi = W.inverted()
    rest = {b.name: W @ b.matrix_local for b in rig.data.bones}   # world space
    head = {n: m.translation.copy() for n, m in rest.items()}
    up = Vector((0, 0, lift))
    want = {"root": Matrix.Translation(up) @ rest["root"]}
    for side in "lr":
        hip0, knee0, foot = head[f"{side}_leg1"], head[f"{side}_leg2"], head[f"{side}_foot"]
        hip = hip0 + up
        a, b = (knee0 - hip0).length, (foot - knee0).length
        to_foot = foot - hip
        d = min(to_foot.length, (a + b) * 0.999)
        axis = to_foot.normalized()
        bend = (knee0 - hip0) - axis * (knee0 - hip0).dot(axis)   # knee direction off the hip-foot line
        bend = bend.normalized() if bend.length > 1e-6 else Vector((0, -1, 0))
        along = (a * a - b * b + d * d) / (2 * d)
        knee = hip + axis * along + bend * max(0.0, a * a - along * along) ** 0.5
        r1 = (knee0 - hip0).normalized().rotation_difference((knee - hip).normalized()).to_matrix().to_4x4()
        r2 = (foot - knee0).normalized().rotation_difference((foot - knee).normalized()).to_matrix().to_4x4()
        want[f"{side}_leg1"] = Matrix.Translation(hip) @ r1 @ Matrix.Translation(-hip0) @ rest[f"{side}_leg1"]
        want[f"{side}_leg2"] = Matrix.Translation(knee) @ r2 @ Matrix.Translation(-knee0) @ rest[f"{side}_leg2"]
        want[f"{side}_foot"] = rest[f"{side}_foot"]
    for pb in sorted(rig.pose.bones, key=lambda pb: len(pb.parent_recursive)):
        if pb.name in want:
            pb.matrix = Wi @ want[pb.name]
            bpy.context.view_layer.update()
    return []


def render(prefab, vanilla_dir, out_dir, view_px=(600, 800), exposure=2.4, lift=0.12, parts=None, tag=""):
    """parts: renderer names to show (None = all; "Cube" = body, "Cube.001" = back piece, "Plane*" = eyes).
    Framing follows the shown parts; files are <prefab><tag>_<view>.png."""
    tex_name, hue, sat, eye = PREFABS[prefab]
    name = "VB_" + prefab
    old = bpy.data.collections.get(name)
    if old:
        for o in list(old.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(old)
    coll = gio.import_creature(os.path.join(vanilla_dir, prefab + ".json"), name=name)
    img = _shader_texture(os.path.join(vanilla_dir, tex_name), hue, sat)
    eye_mat = bpy.data.materials.new(name + "_eye")
    eye_mat.diffuse_color = (*eye, 1)
    meshes = [o for o in coll.objects if o.type == "MESH"]
    for o in meshes:
        o.hide_render = False
        if o.get("game_renderer", "").startswith("Plane"):
            o.data.materials.clear()
            o.data.materials.append(eye_mat)
        else:
            mat = o.data.materials[0]
            nodes = mat.node_tree.nodes
            tex = next((n for n in nodes if n.type == "TEX_IMAGE"), None) or nodes.new("ShaderNodeTexImage")
            tex.image = img
            tex.interpolation = "Closest"
            nodes.active = tex   # Workbench shows the active image node
            mat.node_tree.links.new(tex.outputs["Color"], nodes["Principled BSDF"].inputs["Base Color"])
    rig = next(o for o in coll.objects if o.type == "ARMATURE")
    height = max((o.matrix_world @ v.co).z for o in meshes for v in o.data.vertices) - 50.0
    helpers = stand(rig, lift * height) if lift else []
    if parts is not None:
        meshes = [o for o in meshes if any(o.get("game_renderer", "") == p or (p.endswith("*") and o.get("game_renderer", "").startswith(p[:-1])) for p in parts)]
    dg = bpy.context.evaluated_depsgraph_get()
    ps = []
    for o in meshes:
        ev = o.evaluated_get(dg)
        me = ev.to_mesh()
        ps += [o.matrix_world @ v.co for v in me.vertices]
        ev.to_mesh_clear()
    lo = Vector((min(p.x for p in ps), min(p.y for p in ps), min(p.z for p in ps)))
    hi = Vector((max(p.x for p in ps), max(p.y for p in ps), max(p.z for p in ps)))
    centre = (lo + hi) / 2
    # ortho_scale spans the render's longer side (the height here)
    span = max(max(hi.x - lo.x, hi.y - lo.y) * view_px[1] / view_px[0], hi.z - lo.z) * 1.06

    sc = bpy.context.scene
    cam = bpy.data.objects.get("BoardCam") or bpy.data.objects.new("BoardCam", bpy.data.cameras.new("BoardCam"))
    if cam.name not in sc.collection.objects:
        sc.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = span
    vs = sc.view_settings
    prev = (sc.camera, sc.render.engine, sc.render.resolution_x, sc.render.resolution_y, sc.render.film_transparent,
            vs.view_transform, vs.exposure)
    sc.camera = cam
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.render.resolution_x, sc.render.resolution_y = view_px
    sc.render.film_transparent = True   # composited onto BG afterwards (board())
    vs.view_transform, vs.exposure = "Standard", exposure   # the game lights its creatures much brighter
    sh = sc.display.shading
    sh.light, sh.color_type, sh.show_cavity, sh.show_shadows = "STUDIO", "TEXTURE", False, False
    sh.show_xray, sh.show_specular_highlight, sh.show_object_outline = False, False, False
    others = {o.name: o.hide_render for o in sc.objects if o.type == "MESH"}
    for o in sc.objects:
        if o.type == "MESH":
            o.hide_render = o not in meshes
    paths = []
    try:
        for view, yaw in VIEWS:
            a = math.radians(yaw)
            # the creature faces -Y; yaw 0 puts the camera in front of it
            cam.location = centre + Vector((math.sin(a) * 10, -math.cos(a) * 10, 0))
            cam.rotation_euler = (centre - cam.location).to_track_quat("-Z", "Y").to_euler()
            sc.render.filepath = os.path.join(out_dir, f"{prefab}{tag}_{view}.png")
            bpy.ops.render.render(write_still=True)
            paths.append(sc.render.filepath)
    finally:
        (sc.camera, sc.render.engine, sc.render.resolution_x, sc.render.resolution_y, sc.render.film_transparent,
         vs.view_transform, vs.exposure) = prev
        for o in sc.objects:
            if o.name in others:
                o.hide_render = others[o.name]
        for o in meshes:
            o.hide_render = True
        for e in helpers:
            bpy.data.objects.remove(e, do_unlink=True)
    return paths


def board(paths, out_png):
    """Four transparent view renders side by side on the briefs' background."""
    from PIL import Image
    ims = [Image.open(p).convert("RGBA") for p in paths]
    w, h = ims[0].size
    out = Image.new("RGBA", (w * len(ims), h), tuple(int(c * 255) for c in BG) + (255,))
    for i, im in enumerate(ims):
        out.alpha_composite(im, (i * w, 0))
    out.convert("RGB").save(out_png)
    return out_png


def render_all(vanilla_dir, out_dir):
    os.makedirs(out_dir, exist_ok=True)
    return {p: board(render(p, vanilla_dir, out_dir), os.path.join(out_dir, f"board_{p}.png")) for p in PREFABS}
