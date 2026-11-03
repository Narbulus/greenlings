"""
Bake a body's decoration (the Prop Studio "Decor" pieces) into its game model file.

    import decor_export
    decor_export.export(body_obj, template_json, glmesh_path)

Every piece is rigid on one bone (its vertex group). Pieces are merged per (bone, material) into one
static mesh in that bone's local space (from the body's bind poses), so in game each one is a plain
MeshRenderer parented to the bone: no extra skinning, about two draw calls per greenling.
- "props": every trunk/mushroom/twig, their small textures packed into one atlas, written beside
  the model as <name>.decor.png (point filtered, no mipmaps, so tiles cannot bleed).
- "leaf:<texture>": leaf cards, drawn with the game's own foliage material that uses <texture>.
The blocks are appended to the glmesh (any earlier decor blocks are replaced):
    decor <bone> <material>
    vertices / normals / uv / triangles <count> <values...>
    end
"""
import json
import math
import os

import bpy
import numpy as np
from mathutils import Matrix, Vector

import greenling_io as gio

TILE_PAD = 2


def _tex_of(o):
    """The colour texture image of a piece (first image node that is not a normal map)."""
    for m in o.data.materials:
        if m and m.use_nodes:
            for nd in m.node_tree.nodes:
                if nd.type == "TEX_IMAGE" and nd.image and "Normal" not in nd.image.name:
                    return nd.image
    return None


def _is_leaf(o):
    return o.get("leaf") is not None


def _leaf_key(img):
    # vanilla leaf textures were extracted as texture_<game texture name>.png
    n = img.name
    n = n[len("texture_"):] if n.startswith("texture_") else n
    return "leaf:" + os.path.splitext(n)[0]


def _atlas(images):
    """Shelf-pack images (padded by edge extension) into a square power-of-two atlas.
    Returns (pixels HxWx4 float array, size, {image name: (x0, y0, w, h) in texels})."""
    tiles = sorted(images, key=lambda im: -im.size[1])
    for A in (32, 64, 128, 256, 512, 1024):
        x = y = row = 0
        rects, ok = {}, True
        for im in tiles:
            w, h = im.size[0] + 2 * TILE_PAD, im.size[1] + 2 * TILE_PAD
            if x + w > A:
                x, y, row = 0, y + row, 0
            if y + h > A or w > A:
                ok = False
                break
            rects[im.name] = (x + TILE_PAD, y + TILE_PAD, im.size[0], im.size[1])
            x, row = x + w, max(row, h)
        if ok:
            break
    px = np.zeros((A, A, 4), dtype=np.float32)
    for im in tiles:
        x0, y0, w, h = rects[im.name]
        src = np.array(im.pixels[:], dtype=np.float32).reshape(h, w, 4)   # rows bottom-up, like UVs
        big = np.pad(src, ((TILE_PAD, TILE_PAD), (TILE_PAD, TILE_PAD), (0, 0)), mode="edge")
        px[y0 - TILE_PAD:y0 + h + TILE_PAD, x0 - TILE_PAD:x0 + w + TILE_PAD] = big
    px[..., 3] = 1.0
    return px, A, rects


def export(body, template_json, glmesh_path, pieces=None, scale=1.0, ground_z=None):
    """Write `pieces` (default: the body's decoration collection) into glmesh_path as decor blocks.
    scale/ground_z: the same skeleton scaling export_for applies (brutes)."""
    template = json.load(open(template_json, encoding="utf-8"))
    world = gio._world_matrices(template["transforms"])
    md = next(m for m in template["meshes"] if m["renderer"] == body["game_renderer"])
    bone_index = {b: i for i, b in enumerate(md["bones"])}
    to_local = gio.mat_u2b(world(md["renderer"])).inverted()
    if scale != 1.0:
        to_local = to_local @ (Matrix.Translation((0, 0, ground_z)) @ Matrix.Scale(scale, 4)
                               @ Matrix.Translation((0, 0, -ground_z)))
    swz = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))    # Blender -> Unity axes
    if pieces is None:
        import decorate
        pieces = [o for o in decorate.decor_collection(body).objects if o.type == "MESH"]

    prop_imgs = {}
    for o in pieces:
        img = _tex_of(o)
        if img and not _is_leaf(o):
            prop_imgs[img.name] = img
    atlas_px, A, rects = _atlas(list(prop_imgs.values())) if prop_imgs else (None, 0, {})

    groups = {}   # (bone, material) -> [verts, normals, uvs, tris]
    for o in pieces:
        if not o.vertex_groups:
            continue
        bone = o.vertex_groups[0].name
        if bone not in bone_index:
            continue
        img = _tex_of(o)
        mat = _leaf_key(img) if _is_leaf(o) and img else "props"
        bp = gio._unity_matrix(md["bindposes"][bone_index[bone]])
        L = bp @ swz @ to_local @ o.matrix_world
        Ln = L.to_3x3().inverted().transposed()
        me = o.data
        uvl = me.uv_layers.active.data if me.uv_layers else None
        g = groups.setdefault((bone, mat), [[], [], [], []])
        verts, norms, uvs, tris = g
        rect = rects.get(img.name) if img and mat == "props" else None
        me.calc_loop_triangles()
        cn = me.corner_normals
        flip = L.to_3x3().determinant() < 0
        for t in me.loop_triangles:
            idx = []
            for li in t.loops:
                p = L @ me.vertices[me.loops[li].vertex_index].co
                n = (Ln @ cn[li].vector).normalized()
                u, v = uvl[li].uv if uvl else (0.0, 0.0)
                if rect:
                    x0, y0, w, h = rect
                    u = (x0 + min(max(u, 0.0), 1.0) * w) / A
                    v = (y0 + min(max(v, 0.0), 1.0) * h) / A
                idx.append(len(verts))
                verts.append(p)
                norms.append(n)
                uvs.append((u, v))
            tris.append([idx[0], idx[2], idx[1]] if flip else idx)

    # replace earlier decor blocks
    lines = open(glmesh_path, encoding="utf-8").read().split(chr(10))
    out, skip = [], False
    for ln in lines:
        if ln.startswith("decor "):
            skip = True
        if not skip and ln:
            out.append(ln)
        if skip and ln == "end":
            skip = False
    f6 = lambda x: format(float(x), ".8f")   # bone-local units are small (bones are scaled x100)
    report = {}
    for (bone, mat), (verts, norms, uvs, tris) in sorted(groups.items()):
        # Unity culls back faces: winding must agree with the normals (see greenling_io.export_creature)
        agree = sum((verts[b] - verts[a]).cross(verts[c] - verts[a]).dot(norms[a] + norms[b] + norms[c]) > 0
                    for a, b, c in tris)
        if agree < len(tris) / 2:
            tris = [[a, c, b] for a, b, c in tris]
        out.append(f"decor {bone} {mat}")
        out.append("vertices %d %s" % (len(verts) * 3, " ".join(f6(c) for p in verts for c in p)))
        out.append("normals %d %s" % (len(norms) * 3, " ".join(f6(c) for p in norms for c in p)))
        out.append("uv %d %s" % (len(uvs) * 2, " ".join(f6(c) for p in uvs for c in p)))
        out.append("triangles %d %s" % (len(tris) * 3, " ".join(str(i) for t in tris for i in t)))
        out.append("end")
        report[f"{bone}/{mat}"] = len(tris)
    with open(glmesh_path, "w", encoding="utf-8") as f:
        f.write(chr(10).join(out) + chr(10))

    png = os.path.splitext(glmesh_path)[0] + ".decor.png"
    if atlas_px is not None:
        im = bpy.data.images.get("decor_atlas") or bpy.data.images.new("decor_atlas", A, A, alpha=True)
        if tuple(im.size) != (A, A):
            im.scale(A, A)
        im.pixels[:] = atlas_px.ravel()
        im.filepath_raw = png
        im.file_format = "PNG"
        im.save()
    elif os.path.exists(png):
        os.remove(png)
    report["atlas"] = A
    return report
