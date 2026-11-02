"""
Generated (Tripo) creature mesh -> game-ready greenling on the vanilla Greyling skeleton.

    import_generated(glb, name)            align: face -Y, feet on the ground, scaled to the rig
    decimate(obj, tris)                    low-poly like the vanilla models
    bake_texture(obj, out_png, size)       downscale the generated texture, point-filtered look
    rig_to_vanilla(obj, rig, targets)      fit a copy of the rig to the mesh, weight it there, then
                                           pose the mesh back into the vanilla bind pose
    build_collection(...)                  objects tagged with game_renderer for greenling_io.export_creature

The vanilla skeleton, bind poses and animations are reused unchanged; only the mesh is new.
"""
import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

GROUND_Z = 50.0  # the imported vanilla rig stands at z=50 (see greenling_io)


def import_generated(glb, name, scale=2.75, yaw_deg=-90.0, coll_name="Generated"):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=glb)
    new = [o for o in bpy.data.objects if o not in before]
    coll = bpy.data.collections.get(coll_name) or bpy.data.collections.new(coll_name)
    if coll.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(coll)
    mesh_obj = next(o for o in new if o.type == "MESH")
    for o in new:
        for c in list(o.users_collection):
            c.objects.unlink(o)
        if o is mesh_obj:
            coll.objects.link(o)
        else:
            bpy.data.objects.remove(o, do_unlink=True)
    o = mesh_obj
    o.name = name
    o.parent = None
    o.rotation_mode = "XYZ"
    o.rotation_euler = (0, 0, math.radians(yaw_deg))  # Tripo faces +X; the vanilla rig faces -Y
    o.scale = (scale, scale, scale)
    bpy.context.view_layer.update()
    zmin = min((o.matrix_world @ v.co).z for v in o.data.vertices)
    o.location = (0, 0, GROUND_Z - zmin)
    bpy.context.view_layer.update()
    _apply_transform(o)
    return o


def _apply_transform(o):
    o.data.transform(o.matrix_world)
    o.matrix_world = Matrix.Identity(4)


def decimate(o, tris):
    o.data.update()
    cur = sum(len(p.vertices) - 2 for p in o.data.polygons)
    if cur > tris:
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-4)  # weld seams so collapse keeps the surface closed
        bm.to_mesh(o.data)
        bm.free()
        cur = sum(len(p.vertices) - 2 for p in o.data.polygons)
        m = o.modifiers.new("Decimate", "DECIMATE")
        m.ratio = tris / cur
        m.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = o
        bpy.ops.object.modifier_apply(modifier=m.name)
    return sum(len(p.vertices) - 2 for p in o.data.polygons)


def bake_texture(high, low, out_png, size=256, bake_size=1024, margin_px=2):
    """
    Fresh UVs on the low-poly mesh, then bake the generated colour from the full-detail mesh onto
    it and downscale to `size` (box filter). Used point-filtered, like the vanilla 128px textures.
    Decimating with the generated UVs smears colour across seams, so the low-poly is unwrapped anew.
    """
    # new UVs
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = low
    low.select_set(True)
    while low.data.uv_layers:
        low.data.uv_layers.remove(low.data.uv_layers[0])
    low.data.uv_layers.new(name="UVMap")
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(60), island_margin=margin_px * 2.0 / size)
    bpy.ops.object.mode_set(mode="OBJECT")

    img_name = low.name + "_" + os.path.basename(out_png)  # per object: several bakes share the file name
    img = bpy.data.images.get(img_name)
    if img:
        bpy.data.images.remove(img)
    img = bpy.data.images.new(img_name, bake_size, bake_size, alpha=False)
    mat = bpy.data.materials.new(low.name + "_mat")
    mat.use_nodes = True
    nodes = mat.node_tree.nodes
    tex = nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Closest"
    bsdf = nodes.get("Principled BSDF")
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.9
    nodes.active = tex
    low.data.materials.clear()
    low.data.materials.append(mat)

    scene = bpy.context.scene
    engine = scene.render.engine
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 4
    scene.render.bake.use_selected_to_active = True
    scene.render.bake.use_cage = False
    scene.render.bake.cage_extrusion = 0.06
    scene.render.bake.max_ray_distance = 0.3
    scene.render.bake.margin = 8
    scene.render.bake.use_pass_direct = False
    scene.render.bake.use_pass_indirect = False
    scene.render.bake.use_pass_color = True
    high.hide_set(False)
    high.hide_render = False
    bpy.ops.object.select_all(action="DESELECT")
    high.select_set(True)
    low.select_set(True)
    bpy.context.view_layer.objects.active = low
    bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"})
    scene.render.engine = engine

    img.scale(size, size)
    img.filepath_raw = out_png
    img.file_format = "PNG"
    img.save()
    return out_png


# ---------------------------------------------------------------- rigging

def _children(arm):
    kids = {}
    for b in arm.data.bones:
        if b.parent:
            kids.setdefault(b.parent.name, []).append(b.name)
    return kids


def rig_to_vanilla(o, rig_name, targets, weight_fix=None):
    """
    targets: {bone: world head position on the generated mesh}. Bones not listed keep their vanilla
    place. Each listed bone is moved rigidly (and turned so it still points at its child), the mesh
    gets automatic weights on that fitted rig, and is then posed back into the vanilla rest pose, so
    the game's bind poses and animations apply unchanged.
    """
    rig = bpy.data.objects[rig_name]
    W = rig.matrix_world
    Wi = W.inverted()
    rest = {b.name: b.matrix_local.copy() for b in rig.data.bones}  # armature space
    heads = {b.name: (W @ b.head_local) for b in rig.data.bones}
    kids = _children(rig)

    # fitted bone matrices (armature space)
    fit = {}
    for b in rig.data.bones:
        n = b.name
        new_head = targets.get(n, heads[n])
        ch = [c for c in kids.get(n, []) if (heads[c] - heads[n]).length > 1e-4]
        R = Matrix.Identity(3)
        if ch and (n in targets or any(c in targets for c in ch)):
            c = ch[0]
            d0 = heads[c] - heads[n]
            d1 = targets.get(c, heads[c]) - new_head
            R = d0.normalized().rotation_difference(d1.normalized()).to_matrix()
        Mw = W @ rest[n]
        R4 = Matrix.Translation(new_head) @ R.to_4x4() @ Matrix.Translation(-heads[n])
        fit[n] = Wi @ R4 @ Mw

    fit_rig = bpy.data.objects.get(rig_name + "_fit")
    if fit_rig:
        bpy.data.objects.remove(fit_rig, do_unlink=True)
    fit_rig = rig.copy()
    fit_rig.data = rig.data.copy()
    fit_rig.name = rig_name + "_fit"
    for c in o.users_collection:
        c.objects.link(fit_rig)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = fit_rig
    fit_rig.hide_set(False)
    bpy.ops.object.mode_set(mode="EDIT")
    for eb in fit_rig.data.edit_bones:
        L = eb.length
        eb.matrix = fit[eb.name]
        eb.length = L
    bpy.ops.object.mode_set(mode="OBJECT")

    # automatic weights on the fitted rig
    o.parent = None
    o.vertex_groups.clear()
    for m in [m for m in o.modifiers if m.type == "ARMATURE"]:
        o.modifiers.remove(m)
    bpy.ops.object.select_all(action="DESELECT")
    o.select_set(True)
    fit_rig.select_set(True)
    bpy.context.view_layer.objects.active = fit_rig
    bpy.ops.object.parent_set(type="ARMATURE_AUTO")
    weighted = sum(1 for v in o.data.vertices if v.groups)
    if weight_fix:
        weight_fix(o, fit_rig)

    # pose the fitted rig into the vanilla rest pose and bake that shape
    bpy.context.view_layer.objects.active = fit_rig
    bpy.ops.object.mode_set(mode="POSE")
    order = sorted(fit_rig.pose.bones, key=lambda pb: len(pb.parent_recursive))
    for pb in order:
        pb.matrix = rest[pb.name].copy()
        bpy.context.view_layer.update()
    bpy.ops.object.mode_set(mode="OBJECT")
    bpy.context.view_layer.objects.active = o
    mod = next(m for m in o.modifiers if m.type == "ARMATURE")
    bpy.ops.object.modifier_apply(modifier=mod.name)
    mw = o.matrix_world.copy()
    o.parent = None
    o.matrix_world = mw
    _apply_transform(o)
    for pb in fit_rig.pose.bones:
        pb.matrix_basis = Matrix.Identity(4)

    # bind to the vanilla rig for previews
    m = o.modifiers.new("Armature", "ARMATURE")
    m.object = rig
    return {"verts": len(o.data.vertices), "weighted": weighted, "groups": len(o.vertex_groups)}


# ---------------------------------------------------------------- assembly

def build_collection(body, src_coll, eyes, eye_scale=1.3, eye_push=0.012, dst="GreenlingGen"):
    """
    Game renderers for greenling_io.export_creature: Cube = the generated body, Cube.001 (vanilla
    back lump) = empty, Plane/Plane.001 = the vanilla glowing eye quads moved onto the new eyes.
    eyes: {"Plane": world centre, "Plane.001": world centre} (Plane is the creature's right eye).
    """
    old = bpy.data.collections.get(dst)
    if old:
        for o in list(old.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        bpy.data.collections.remove(old)
    coll = bpy.data.collections.new(dst)
    bpy.context.scene.collection.children.link(coll)

    b = body.copy()
    b.data = body.data.copy()
    b.name = dst + "_Cube"
    b["game_renderer"] = "Cube"
    coll.objects.link(b)

    lump_mesh = bpy.data.meshes.new(dst + "_lump")
    lump_mesh.uv_layers.new(name="UVMap")
    lump = bpy.data.objects.new(dst + "_Cube.001", lump_mesh)
    lump["game_renderer"] = "Cube.001"
    coll.objects.link(lump)

    src = {o.get("game_renderer"): o for o in bpy.data.collections[src_coll].objects}
    for name, centre in eyes.items():
        e = src[name].copy()
        e.data = src[name].data.copy()
        e.name = dst + "_" + name
        e["game_renderer"] = name
        coll.objects.link(e)
        M = e.matrix_world
        pts = [M @ v.co for v in e.data.vertices]
        c0 = sum(pts, Vector()) / len(pts)
        target = Vector(centre) + Vector((0, -eye_push, 0))
        Mi = M.inverted()
        for v, p in zip(e.data.vertices, pts):
            v.co = Mi @ (target + (p - c0) * eye_scale)
        e.data.update()
    return coll
