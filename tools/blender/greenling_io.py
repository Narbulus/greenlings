"""
Round-trip Valheim creature meshes between the game (gldev_export JSON) and Blender.

    import_creature(json_path, texture_path=None) -> collection
        One mesh object per game renderer (vertex groups = game bones), an armature for
        reference, and the game texture. Vertices are welded for editing; the original
        normals are kept as custom normals.

    export_creature(collection, json_template, out_path)
        Writes the edited meshes back in the game's layout: same bone list and bind poses,
        vertices split per (position, uv, normal) the way Unity needs, 4 weights per vertex.

Coordinates: Unity is left-handed Y-up, Blender right-handed Z-up. Swapping Y and Z converts
between them (it is its own inverse and flips handedness, so triangle winding stays correct).
"""
import json
import math

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

C = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))


def u2b(x, y, z):
    return Vector((x, z, y))


def mat_u2b(m):
    return C @ m @ C


def _unity_matrix(cols16):
    # column-major 4x4 -> mathutils (row-major)
    return Matrix([[cols16[c * 4 + r] for c in range(4)] for r in range(4)])


def _world_matrices(transforms):
    by_name = {t["name"]: t for t in transforms}
    cache = {}

    def world(name):
        if name in cache:
            return cache[name]
        t = by_name[name]
        q = t["rot"]
        local = (Matrix.Translation(Vector(t["pos"])) @ Quaternion((q[3], q[0], q[1], q[2])).to_matrix().to_4x4()
                 @ Matrix.Diagonal(Vector(t["scale"] + [1.0])))
        w = world(t["parent"]) @ local if t["parent"] else local
        cache[name] = w
        return w

    return world


def import_creature(json_path, texture_path=None, name=None):
    data = json.load(open(json_path, encoding="utf-8"))
    name = name or data["prefab"]
    world = _world_matrices(data["transforms"])

    coll = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(coll)

    mat = bpy.data.materials.new(name + "_mat")
    mat.use_nodes = True
    if texture_path:
        bsdf = mat.node_tree.nodes.get("Principled BSDF")
        tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(texture_path, check_existing=True)
        tex.interpolation = "Closest"
        mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        bsdf.inputs["Roughness"].default_value = 0.9

    arm_obj = None
    for md in data["meshes"]:
        r_world = mat_u2b(world(md["renderer"]))
        verts = md["vertices"]
        n = len(verts) // 3
        positions = [u2b(*verts[i * 3:i * 3 + 3]) for i in range(n)]
        normals = [u2b(*md["normals"][i * 3:i * 3 + 3]) for i in range(n)]
        uvs = [(md["uv"][i * 2], md["uv"][i * 2 + 1]) for i in range(n)] if md["uv"] else None
        tris = [tuple(sub[i:i + 3]) for sub in md["submeshes"] for i in range(0, len(sub), 3)]

        mesh = bpy.data.meshes.new(md["renderer"])
        mesh.from_pydata([tuple(p) for p in positions], [], tris)
        mesh.update()
        if uvs:
            uv_layer = mesh.uv_layers.new(name="UVMap")
            for poly in mesh.polygons:
                for li in poly.loop_indices:
                    uv_layer.data[li].uv = uvs[mesh.loops[li].vertex_index]
        loop_normals = [normals[l.vertex_index] for l in mesh.loops]
        mesh.normals_split_custom_set(loop_normals)
        mesh.materials.append(mat)

        obj = bpy.data.objects.new(md["renderer"], mesh)
        obj.matrix_world = r_world
        coll.objects.link(obj)
        obj["game_renderer"] = md["renderer"]

        # vertex groups = game bones
        groups = [obj.vertex_groups.new(name=b) for b in md["bones"]]
        w = md["weights"]
        for vi in range(n):
            for k in range(4):
                bi, wt = int(w[vi * 8 + k * 2]), w[vi * 8 + k * 2 + 1]
                if wt > 0:
                    groups[bi].add([vi], wt, "REPLACE")

        # weld seam duplicates for editing (export re-splits by uv/normal)
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
        bm.to_mesh(mesh)
        bm.free()

        if arm_obj is None and md["bones"]:
            arm_obj = _build_armature(name + "_rig", md, r_world, coll)
        if arm_obj:
            obj.parent = arm_obj
            obj.matrix_parent_inverse = arm_obj.matrix_world.inverted()
            mod = obj.modifiers.new("Armature", "ARMATURE")
            mod.object = arm_obj
    return coll


def _build_armature(name, md, r_world, coll):
    arm = bpy.data.armatures.new(name)
    arm_obj = bpy.data.objects.new(name, arm)
    coll.objects.link(arm_obj)
    bpy.context.view_layer.objects.active = arm_obj
    bpy.ops.object.mode_set(mode="EDIT")
    heads = {}
    mats = {}
    for b, bp in zip(md["bones"], md["bindposes"]):
        m = r_world @ mat_u2b(_unity_matrix(bp).inverted())
        mats[b] = m
        heads[b] = m.translation.copy()
    for b, m in mats.items():
        eb = arm.edit_bones.new(b)
        eb.head = heads[b]
        # point along the bone's own Y axis; length from the nearest other head, clamped
        y = (m.to_3x3() @ Vector((0, 1, 0))).normalized()
        others = [(h - heads[b]).length for o, h in heads.items() if o != b and (h - heads[b]).length > 1e-4]
        length = max(0.02, min(0.25, min(others) if others else 0.1))
        eb.tail = heads[b] + y * length
    bpy.ops.object.mode_set(mode="OBJECT")
    arm_obj.show_in_front = True
    return arm_obj


def export_creature(coll_name, json_template, out_path):
    """Write the collection's meshes back as game JSON (same schema as gldev_export)."""
    template = json.load(open(json_template, encoding="utf-8"))
    world = _world_matrices(template["transforms"])
    coll = bpy.data.collections[coll_name]
    out_meshes = []
    for md in template["meshes"]:
        obj = next((o for o in coll.objects if o.get("game_renderer") == md["renderer"]), None)
        if obj is None:
            continue
        # evaluate without the armature (rest shape), in renderer-local game space
        arm_mods = [m for m in obj.modifiers if m.type == "ARMATURE"]
        for m in arm_mods:
            m.show_viewport = False
        dg = bpy.context.evaluated_depsgraph_get()
        ev = obj.evaluated_get(dg)
        mesh = ev.to_mesh()
        for m in arm_mods:
            m.show_viewport = True
        to_local = mat_u2b(world(md["renderer"])).inverted() @ obj.matrix_world
        bm = bmesh.new()
        bm.from_mesh(mesh)
        bmesh.ops.triangulate(bm, faces=bm.faces[:])
        bm.to_mesh(mesh)
        bm.free()
        mesh.calc_loop_triangles() if hasattr(mesh, "calc_loop_triangles") else None

        bone_index = {b: i for i, b in enumerate(md["bones"])}
        group_bone = {g.index: bone_index.get(g.name) for g in obj.vertex_groups}
        uv_layer = mesh.uv_layers.active.data if mesh.uv_layers else None
        corner_normals = mesh.corner_normals

        verts, normals, uvs, weights, tris = [], [], [], [], []
        key_index = {}
        nrot = to_local.to_3x3().inverted().transposed()
        for poly in mesh.polygons:
            tri = []
            for li in poly.loop_indices:
                vi = mesh.loops[li].vertex_index
                uv = tuple(round(c, 5) for c in uv_layer[li].uv) if uv_layer else (0.0, 0.0)
                nb = (nrot @ corner_normals[li].vector).normalized()
                key = (vi, uv, tuple(round(c, 3) for c in nb))
                idx = key_index.get(key)
                if idx is None:
                    idx = len(verts)
                    key_index[key] = idx
                    p = to_local @ mesh.vertices[vi].co
                    verts.append(p)
                    normals.append(nb)
                    uvs.append(uv)
                    ws = sorted(((group_bone.get(g.group), g.weight) for g in mesh.vertices[vi].groups
                                 if group_bone.get(g.group) is not None and g.weight > 0), key=lambda x: -x[1])[:4]
                    total = sum(w for _, w in ws) or 1.0
                    ws = [(b, w / total) for b, w in ws] + [(0, 0.0)] * (4 - len(ws))
                    weights.append(ws)
                tri.append(idx)
            tris.append(tri)
        ev.to_mesh_clear()

        flat = lambda vs: [c for v in vs for c in (v.x, v.z, v.y)]  # Blender -> Unity
        out_meshes.append({
            **{k: md[k] for k in ("renderer", "mesh", "rootBone", "materials", "bones", "bindposes")},
            "vertices": flat(verts),
            "normals": flat(normals),
            "uv": [c for uv in uvs for c in uv],
            "submeshes": [[i for t in tris for i in t]],
            "weights": [x for ws in weights for b, w in ws for x in (b, round(w, 5))],
            # flat copies for Unity's JsonUtility (no nested arrays)
            "triangles": [i for t in tris for i in t],
            "bindposesFlat": [c for bp in md["bindposes"] for c in bp],
        })
    out = {"prefab": template["prefab"], "transforms": template["transforms"], "meshes": out_meshes}
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(out, f, separators=(",", ":"))
    return {m["renderer"]: len(m["vertices"]) // 3 for m in out_meshes}
