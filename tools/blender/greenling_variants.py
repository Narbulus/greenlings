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


def arm_tube(o, rig, radius=0.11, leaf=None, keep=0.6, keep_reach=0.22):
    """
    Strict version of arm_bleed for meshes with leaves draped over the arms (leafy): only the arm's
    own skin, within `radius` of the arm bone segments, keeps shoulder/arm weights. Everything else
    rides the torso, so a draped leaf stays put as a cape when the arm moves instead of being split
    between arm and torso and stretching into strips. The upper arm is thicker than `radius` near the
    shoulder, so vertices the arm owns (weight >= `keep`) also stay on it unless leaf(vertex index)
    says they are leaf.
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
        p = o.matrix_world @ v.co
        arm = [(names[g.group], g.weight) for g in v.groups if names[g.group][2:] in ("shoulder", "arm1", "arm2") and g.weight > 0]
        d = dist(p, "l" if p.x > 0 else "r") if arm else 9.0
        if not arm or d <= radius:
            continue
        # thick upper arm near the shoulder: bark the arm owns, but not the back/top of the body
        if d <= keep_reach and sum(w for _, w in arm) >= keep and not (leaf and leaf(v.index)):
            continue
        for name, _ in arm:
            o.vertex_groups[name].remove([v.index])
        o.vertex_groups["spine3"].add([v.index], sum(w for _, w in arm), "ADD")
        moved += 1
    return moved


def leaf_test(o, image):
    """leaf(vertex index) -> True where most of the vertex's texels are green (leaves, not bark)."""
    w, h = image.size
    pix = image.pixels[:]
    uv = o.data.uv_layers.active.data
    votes = {}
    for li, loop in enumerate(o.data.loops):
        u, v = uv[li].uv
        i = (min(h - 1, max(0, int(v * h))) * w + min(w - 1, max(0, int(u * w)))) * 4
        votes.setdefault(loop.vertex_index, []).append(pix[i + 1] > pix[i] * 1.05)
    green = {vi for vi, vs in votes.items() if sum(vs) * 2 > len(vs)}
    return green.__contains__


def fill_holes(o, max_sides=12, image=None):
    """
    Close the small holes a generated mesh can have (back-face culling makes them see-through in
    game). Each patch takes one UV from its rim, i.e. a single texel colour: interpolating the rim's
    UVs smears stripes across the patch. With `image`, the rim texel nearest the rim's median colour
    is used, so a patch doesn't pick up a stray highlight.
    """
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(o.data)
    uvl = bm.loops.layers.uv.active
    known = {}
    for f in bm.faces:
        for l in f.loops:
            known.setdefault(l.vert.index, l[uvl].uv.copy())
    if image:
        w, h = image.size
        pix = image.pixels[:]

        def colour(uv):
            i = (min(h - 1, max(0, int(uv.y * h))) * w + min(w - 1, max(0, int(uv.x * w)))) * 4
            return pix[i:i + 3]
    bm.normal_update()
    made = bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=max_sides)["faces"]
    # holes_fill skips rims that aren't simple loops (zigzag cuts): close each remaining rim as a
    # fan of triangles from a new vertex at its centre, carrying the rim's averaged weights
    deform = bm.verts.layers.deform.active
    seen = set()
    for e0 in [e for e in bm.edges if e.is_boundary]:
        if e0 in seen:
            continue
        rim, stack = [], [e0]
        seen.add(e0)
        while stack:
            e = stack.pop()
            rim.append(e)
            for v in e.verts:
                for e2 in v.link_edges:
                    if e2.is_boundary and e2 not in seen:
                        seen.add(e2)
                        stack.append(e2)
        verts = list({v for e in rim for v in e.verts})
        # only clean closed rims (every vertex on exactly two rim edges): fanning anything else
        # stacks faces onto edges that already have two (non-manifold)
        if len(verts) != len(rim) or any(sum(1 for x in v.link_edges if x.is_boundary) != 2 for v in verts):
            continue
        mid = sum((v.co for v in verts), verts[0].co * 0) / len(verts)
        # dome the patch outward along the surrounding surface so it isn't a sunken flat fan
        normal = sum((f.normal for e in rim for f in e.link_faces), verts[0].co * 0)
        radius = sum((v.co - mid).length for v in verts) / len(verts)
        if normal.length > 1e-6:
            mid = mid + normal.normalized() * radius * 0.35
        centre = bm.verts.new(mid)
        if deform:
            acc = {}
            for v in verts:
                for k, wt in v[deform].items():
                    acc[k] = acc.get(k, 0.0) + wt / len(verts)
            for k, wt in sorted(acc.items(), key=lambda t: -t[1])[:4]:
                centre[deform][k] = wt
        for e in rim:
            # orient each triangle against its neighbour across the rim edge
            other = e.link_faces[0]
            ol = next(x for x in other.loops if x.edge is e)
            a_, b_ = ol.vert, ol.link_loop_next.vert
            try:
                made.append(bm.faces.new((b_, a_, centre)))
            except ValueError:
                pass

    for f in made:
        rim = [known[l.vert.index] for l in f.loops if l.vert.index in known]
        if not rim:
            continue
        uv = rim[0]
        if image:
            cols = [colour(u) for u in rim]
            med = [sorted(c[k] for c in cols)[len(cols) // 2] for k in range(3)]
            uv = min(rim, key=lambda u: sum((a - b) ** 2 for a, b in zip(colour(u), med)))
        for l in f.loops:
            l[uvl].uv = uv
    for f in made:
        f.smooth = True  # new faces default to flat shading, which reads as pale facets
    bm.to_mesh(o.data)
    bm.free()
    return len(made)


def cut_shoulders(o, rig, image, max_area=0.012):
    """
    Remove the leaf flaps that stand over the shoulders, and the twig on the left
    shoulder (leafy brief): they cannot follow both arm and torso without stretching, and the body
    has detail enough without them. Green faces only, and not the few large ones (body surface).
    Returns faces removed; run fill_holes afterwards.
    """
    import bmesh
    from mathutils.geometry import intersect_point_line
    H = {b.name: rig.matrix_world @ b.head_local for b in rig.data.bones}

    def adist(p):
        s = "l" if p.x > 0 else "r"
        best = 9.0
        for a, b in ((f"{s}_arm1", f"{s}_arm2"), (f"{s}_arm2", f"{s}_hand")):
            _, t = intersect_point_line(p, H[a], H[b])
            best = min(best, (p - H[a].lerp(H[b], max(0.0, min(1.0, t)))).length)
        return best

    w, h = image.size
    pix = image.pixels[:]
    bm = bmesh.new()
    bm.from_mesh(o.data)
    uvl = bm.loops.layers.uv.active
    M = o.matrix_world

    def green(f):
        g = 0
        for l in f.loops:
            u, v = l[uvl].uv
            i = (min(h - 1, max(0, int(v * h))) * w + min(w - 1, max(0, int(u * w)))) * 4
            g += pix[i + 1] > pix[i] * 1.05
        return g * 2 > len(f.loops)

    cut = []
    for f in bm.faces:
        c = M @ f.calc_center_median()
        small = f.calc_area() * (M.to_scale()[0] ** 2) < max_area
        flap = 0.28 < abs(c.x) < 0.65 and 51.3 < c.z < 51.7 and adist(c) > 0.1 and small and green(f)
        twig = 0.15 < c.x < 0.65 and c.z >= 51.45
        if flap or twig:
            cut.append(f)
    bmesh.ops.delete(bm, geom=cut, context="FACES")
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(o.data)
    bm.free()
    return len(cut)


def clip_torso(o, half_width=0.38, z_band=(51.02, 51.45), arm_max=0.3):
    """
    Torso-weighted geometry that reaches out along the arms in the bind (T) pose stays horizontal
    when the arms hang down, and reads as spikes beside them. Clip torso-only faces in the shoulder
    band to the torso's width (vanilla greyling: ~0.35 half-width there) and cap the cut. Faces that
    involve the arm (any vertex with arm weight > arm_max) are left alone: they are the arm and armpit.
    """
    import bmesh
    from mathutils import Vector
    arm_bones = ("arm1", "arm2", "hand", "index1", "index2", "middle1", "middle2", "pinky1", "pinky2")
    arm_ids = [g.index for g in o.vertex_groups if g.name[2:] in arm_bones]
    sp3 = o.vertex_groups["spine3"].index
    M = o.matrix_world
    Mi = M.inverted()
    bm = bmesh.new()
    bm.from_mesh(o.data)
    deform = bm.verts.layers.deform.active

    def arm_w(v):
        d = v[deform]
        return sum(d.get(i, 0.0) for i in arm_ids)

    removed = 0
    for side in (1, -1):
        sel = [f for f in bm.faces
               if all(arm_w(v) <= arm_max for v in f.verts)
               and z_band[0] < (M @ f.calc_center_median()).z < z_band[1]
               and any((M @ v.co).x * side > half_width for v in f.verts)]
        if not sel:
            continue
        geom = list({e for f in sel for e in f.edges}) + sel + list({v for f in sel for v in f.verts})
        co = Mi @ Vector((side * half_width, 0, 0))
        no = (Mi.to_3x3() @ Vector((side, 0, 0))).normalized()
        before = len(bm.faces)
        bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no, clear_outer=True)
        removed += before - len(bm.faces)
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        # new cut vertices normally carry interpolated weights; any without go to spine3
        for v in bm.verts:
            if not v[deform]:
                v[deform][sp3] = 1.0
    bm.to_mesh(o.data)
    bm.free()
    return removed


def hug_arm(o, rig, radius=0.075):
    """
    Pull arm-owned vertices (leaf fringe on the upper arm) in to `radius` around the arm bones, so
    they lie on the arm instead of standing off it as plates when the arm hangs down.
    """
    from mathutils.geometry import intersect_point_line
    H = {b.name: rig.matrix_world @ b.head_local for b in rig.data.bones}
    names = {g.index: g.name for g in o.vertex_groups}
    M = o.matrix_world
    Mi = M.inverted()
    pulled = 0
    for v in o.data.vertices:
        p = M @ v.co
        w = sum(g.weight for g in v.groups if names[g.group][2:] in ("shoulder", "arm1", "arm2", "hand"))
        if w < 0.5 or abs(p.x) < 0.2:
            continue
        s = "l" if p.x > 0 else "r"
        best = None
        for a, b in ((f"{s}_arm1", f"{s}_arm2"), (f"{s}_arm2", f"{s}_hand")):
            _, t = intersect_point_line(p, H[a], H[b])
            q = H[a].lerp(H[b], max(0.0, min(1.0, t)))
            if best is None or (p - q).length < (p - best).length:
                best = q
        if (p - best).length > radius:
            v.co = Mi @ p.lerp(best + (p - best).normalized() * radius, min(1.0, w))
            pulled += 1
    return pulled


ARM_TEST_POSES = (
    {"l_arm1": (0, 0, -60), "r_arm1": (0, 0, 60)},                                    # arms hanging
    {"l_arm1": (-70, 0, -20), "r_arm1": (-70, 0, 20)},                                # reaching forward
    {"l_arm1": (0, 0, -80), "r_arm1": (0, 0, 80), "l_arm2": (0, 0, -40), "r_arm2": (0, 0, 40)},
)


def remove_stretch(o, rig, image=None, ratio=1.8, min_len=0.08, passes=4, dry=False):
    """
    Pose the rig through ARM_TEST_POSES, find edges that stretch more than `ratio` (and end up longer
    than `min_len`), delete their faces plus any small floating fragments left behind, and fill the
    holes. Repeats until nothing stretches. Returns faces removed per pass; with dry=True, only
    reports the stretching face indices (nothing is changed).
    """
    import math
    import bmesh

    def lengths():
        bpy.context.view_layer.update()
        ev = o.evaluated_get(bpy.context.evaluated_depsgraph_get())
        me = ev.to_mesh()
        co = [o.matrix_world @ v.co for v in me.vertices]
        out = {tuple(sorted(e.vertices)): (co[e.vertices[0]] - co[e.vertices[1]]).length for e in me.edges}
        ev.to_mesh_clear()
        return out

    def pose(p):
        for pb in rig.pose.bones:
            pb.rotation_mode = "XYZ"
            pb.rotation_euler = (0, 0, 0)
        for b, e in p.items():
            rig.pose.bones[b].rotation_euler = tuple(math.radians(a) for a in e)

    log = []
    for _ in range(passes):
        pose({})
        rest = lengths()
        worst = {}
        for p in ARM_TEST_POSES:
            pose(p)
            for k, l1 in lengths().items():
                if rest.get(k, 0) > 1e-5:
                    worst[k] = max(worst.get(k, 0), l1 / rest[k])
        pose({})
        bad = {k for k, s in worst.items() if s > ratio and rest[k] * s > min_len}
        faces = [f.index for f in o.data.polygons if any(tuple(sorted(e)) in bad for e in f.edge_keys)]
        if dry:
            return faces
        log.append(len(faces))
        if not faces:
            break
        bm = bmesh.new()
        bm.from_mesh(o.data)
        bm.faces.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.faces[i] for i in faces], context="FACES")
        seen, drop = set(), []
        for v in bm.verts:
            if v in seen:
                continue
            comp, stack = [], [v]
            seen.add(v)
            while stack:
                x = stack.pop()
                comp.append(x)
                for e in x.link_edges:
                    y = e.other_vert(x)
                    if y not in seen:
                        seen.add(y)
                        stack.append(y)
            if len(comp) < 25:
                drop += comp
        bmesh.ops.delete(bm, geom=drop, context="VERTS")
        bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
        bm.to_mesh(o.data)
        bm.free()
        fill_holes(o, max_sides=64, image=image)
    return log


def smooth_shoulders(o, iterations=6, x_range=(0.12, 0.55), z_range=(50.85, 51.65)):
    """
    Spread the arm/torso weight transition over the shoulder region (Laplacian smoothing of all
    weights there, renormalised to 4 influences), so a hanging arm bends the side of the body
    gradually instead of stretching one row of large low-poly faces into a web.
    """
    import bmesh
    M = o.matrix_world
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.verts.ensure_lookup_table()
    region = [v.index for v in bm.verts
              if x_range[0] < abs((M @ v.co).x) < x_range[1] and z_range[0] < (M @ v.co).z < z_range[1]]
    nbrs = {i: [e.other_vert(bm.verts[i]).index for e in bm.verts[i].link_edges] for i in region}
    bm.free()
    weights = [{g.group: g.weight for g in v.groups} for v in o.data.vertices]
    for _ in range(iterations):
        new = {}
        for i in region:
            acc = dict(weights[i])
            for j in nbrs[i]:
                for k, w in weights[j].items():
                    acc[k] = acc.get(k, 0.0) + w
            n = 1 + len(nbrs[i])
            top = sorted(((k, w / n) for k, w in acc.items()), key=lambda t: -t[1])[:4]
            total = sum(w for _, w in top) or 1.0
            new[i] = {k: w / total for k, w in top}
        for i, d in new.items():
            weights[i] = d
    for i in region:
        for g in list(o.data.vertices[i].groups):
            o.vertex_groups[g.group].remove([i])
        for k, w in weights[i].items():
            o.vertex_groups[k].add([i], w, "REPLACE")
    return len(region)


def rebake(o, rig, fit_rig, high, out_png, size=256):
    """
    New UVs and texture for the finished game mesh, baked from the generated high-detail model, so
    patches left by cuts and hole fills get real bark/leaf texture instead of one stretched texel.
    The game mesh sits in the vanilla bind pose; it is posed back into the generated model's pose
    (the fitted rig's rest) for the bake, then the UVs are copied over loop for loop.
    """
    import greenling_tripo as gt
    rest = {b.name: b.matrix_local.copy() for b in fit_rig.data.bones}
    for pb in sorted(rig.pose.bones, key=lambda pb: len(pb.parent_recursive)):
        pb.matrix = rest[pb.name]
        bpy.context.view_layer.update()
    ev = o.evaluated_get(bpy.context.evaluated_depsgraph_get())
    tmp_mesh = bpy.data.meshes.new_from_object(ev)
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()
    bpy.context.view_layer.update()
    tmp = bpy.data.objects.new(o.name + "_bake", tmp_mesh)
    tmp.matrix_world = o.matrix_world.copy()
    for c in o.users_collection:
        c.objects.link(tmp)
    gt.bake_texture(high, tmp, out_png, size, bake_size=max(1024, size * 2))
    src = tmp.data.uv_layers.active.data
    dst = o.data.uv_layers.active.data
    for i in range(len(dst)):
        dst[i].uv = src[i].uv
    bpy.data.objects.remove(tmp, do_unlink=True)
    return out_png


def separate_arms(o, arm_min=0.5, inner_x=0.12):
    """
    Cut each arm off the body as its own piece, the way low-poly creatures are built: generated
    bodies weld the shoulder shell onto the upper arm, and those faces stretch into a web whenever
    the arm moves. Faces are classed arm/body by their vertices' arm weight; every edge between the
    two classes is split, arm-piece vertices keep only arm weights and body-piece vertices lose
    theirs, and both openings are capped by fill_holes afterwards. The arm root ends up inside the
    body and turns there freely. Returns the number of seam edges cut.
    """
    import bmesh
    arm_ids = {g.index for g in o.vertex_groups if g.name[2:] in ("shoulder",) + ARM_CHAIN}
    sp3 = o.vertex_groups["spine3"].index
    M = o.matrix_world
    bm = bmesh.new()
    bm.from_mesh(o.data)
    deform = bm.verts.layers.deform.active

    def arm_w(v):
        return sum(w for k, w in v[deform].items() if k in arm_ids)

    is_arm = {f: (sum(arm_w(v) for v in f.verts) / len(f.verts) > arm_min and abs((M @ f.calc_center_median()).x) > inner_x)
              for f in bm.faces}
    seam = [e for e in bm.edges if len(e.link_faces) == 2 and is_arm[e.link_faces[0]] != is_arm[e.link_faces[1]]]
    bmesh.ops.split_edges(bm, edges=seam)
    for v in bm.verts:
        cls = {is_arm.get(f) for f in v.link_faces}
        d = v[deform]
        if cls == {True}:
            keep = {k: w for k, w in d.items() if k in arm_ids}
        elif cls == {False}:
            keep = {k: w for k, w in d.items() if k not in arm_ids}
        else:
            continue
        total = sum(keep.values())
        for k in list(d.keys()):
            del d[k]
        if total <= 0:
            d[sp3] = 1.0
        else:
            for k, w in keep.items():
                d[k] = w / total
    bm.to_mesh(o.data)
    bm.free()
    return len(seam)


def slim_legs(o, rig, factor=0.75, bones=("leg1", "leg2")):
    """
    Pull leg-owned vertices toward their leg bones: generated thighs come out ~1.6x the vanilla
    width, and hanging arms swing through them. The feet (and anything not mostly leg) stay.
    """
    from mathutils.geometry import intersect_point_line
    H = {b.name: rig.matrix_world @ b.head_local for b in rig.data.bones}
    seg = {f"{s}_leg1": (f"{s}_leg1", f"{s}_leg2") for s in "lr"}
    seg.update({f"{s}_leg2": (f"{s}_leg2", f"{s}_foot") for s in "lr"})
    names = {g.index: g.name for g in o.vertex_groups}
    M = o.matrix_world
    Mi = M.inverted()
    moved = 0
    for v in o.data.vertices:
        if not v.groups:
            continue
        g = max(v.groups, key=lambda x: x.weight)
        name = names[g.group]
        if name[2:] not in bones or g.weight < 0.5:
            continue
        a, b = seg[name]
        p = M @ v.co
        _, t = intersect_point_line(p, H[a], H[b])
        q = H[a].lerp(H[b], max(0.0, min(1.0, t)))
        v.co = Mi @ q.lerp(p, 1.0 - (1.0 - factor) * g.weight)
        moved += 1
    o.data.update()
    return moved


def face_to_head(o, eyes, inner=0.13, outer=0.24, depth=0.08):
    """
    The face (eyes, beak) rides the head bone, like the vanilla greyling's, so it turns with the
    head and the glowing eye quads (skinned to the head) stay on it; nothing else does. Heat weights
    give the face only ~10% head and sprinkle head weight over the shell around and above it, so
    shell pieces turned with the head and slid over the rest. Full head weight within `inner` of
    the face centre (between and just below the eyes), fading out at `outer`, and only in front of
    the shell (no further back than `depth` behind the eyes); everywhere else head weight moves to
    spine3.
    """
    c = sum((Vector(p) for p in eyes.values()), Vector()) / len(eyes) + Vector((0, -0.02, -0.06))
    back = max(p[1] for p in eyes.values()) + depth
    head = o.vertex_groups["head"].index
    sp3 = o.vertex_groups["spine3"].index
    M = o.matrix_world
    n = 0
    for v in o.data.vertices:
        p = M @ v.co
        d = (p - c).length
        t = 0.0 if d >= outer or p.y > back else 1.0 if d <= inner else (outer - d) / (outer - inner)
        old = {g.group: g.weight for g in v.groups}
        h = old.pop(head, 0.0)
        if t == 0.0 and h == 0.0:
            continue
        if t == 0.0:   # stray head weight on the shell or body: give it to the torso
            o.vertex_groups[head].remove([v.index])
            o.vertex_groups[sp3].add([v.index], old.get(sp3, 0.0) + h, "REPLACE")
            continue
        for k, w in old.items():
            o.vertex_groups[k].add([v.index], w * (1 - t), "REPLACE")
        o.vertex_groups[head].add([v.index], h * (1 - t) + t, "REPLACE")
        n += 1
    return n


def drop_fragments(o, min_verts=25):
    """Delete small loose pieces (left floating by cuts)."""
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(o.data)
    seen, drop = set(), []
    for v in bm.verts:
        if v in seen:
            continue
        comp, stack = [], [v]
        seen.add(v)
        while stack:
            x = stack.pop()
            comp.append(x)
            for e in x.link_edges:
                y = e.other_vert(x)
                if y not in seen:
                    seen.add(y)
                    stack.append(y)
        if len(comp) < min_verts:
            drop += comp
    bmesh.ops.delete(bm, geom=drop, context="VERTS")
    bm.to_mesh(o.data)
    bm.free()
    return len(drop)


def fix_winding(o):
    """
    Make face winding consistent across each connected piece, pointing outward (cuts, fills and
    decimation leave some faces flipped; the game culls back faces, so those show as holes).
    """
    import bmesh
    bm = bmesh.new()
    bm.from_mesh(o.data)
    before = len(bm.faces)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()
    return before


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


def finish(name, leg_x, arm_dz, eyes, out_json, template_json, eye_push=0.012, arm_dy=0.13):
    o = bpy.data.objects[name]
    info = gt.rig_to_vanilla(o, RIG, targets(leg_x, arm_dz, arm_dy), weight_fix=weight_fix)
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


# ---------------------------------------------------------------- automatic pipeline

HEIGHT = 1.6         # standing height in rig space; the vanilla greyling mesh is 1.56 (brute: x1.875)
VANILLA_ARM = (-0.07, 51.21)   # vanilla arm bone (y, z) around the forearm


def import_auto(glb, name, coll_name):
    """
    Import a generated model scaled to the vanilla height (HEIGHT) and centred on its feet. Tripo
    normalises each model to its bounding box, so it has to be rescaled anyway.
    Returns (object, held_side): held_side is "r"/"l" when one hand holds something (its outer
    arm is much thicker than the other's), else None.
    """
    o = gt.import_generated(glb, name, scale=1.0, coll_name=coll_name)
    ps = [v.co for v in o.data.vertices]
    zmin = min(p.z for p in ps)
    h = max(p.z for p in ps) - zmin
    feet = [p for p in ps if p.z < zmin + 0.08 * h]
    cx = (min(p.x for p in feet) + max(p.x for p in feet)) / 2
    cy = (min(p.y for p in feet) + max(p.y for p in feet)) / 2
    left, right = max(p.x for p in ps) - cx, cx - min(p.x for p in ps)
    # a held club or stone makes the outer third of that arm much thicker than the empty hand's
    def thickness(sign, reach):
        zs = [p.z for p in ps if (p.x - cx) * sign > reach * 0.66]
        return max(zs) - min(zs) if zs else 0.0
    tl, tr = thickness(1, left), thickness(-1, right)
    held = "r" if tr > tl * 1.5 else "l" if tl > tr * 1.5 else None
    # scale by shoulder height (arms at vanilla 1.21): a trunk or anything on the head must not shrink the body
    arm = [p for p in ps if abs(p.x - cx) > 0.55 * max(left, right)]
    scale = 1.21 / ((sum(p.z for p in arm) / len(arm)) - zmin) if arm else HEIGHT / h
    for v in o.data.vertices:
        v.co = Vector(((v.co.x - cx) * scale, (v.co.y - cy) * scale, gt.GROUND_Z + (v.co.z - zmin) * scale))
    o.data.update()
    return o, held


def measure(o):
    """Leg spacing, arm offset and eye centres of a generated model, in rig space (see targets())."""
    M = o.matrix_world
    ps = [M @ v.co for v in o.data.vertices]
    legs = [p.x for p in ps if 50.25 < p.z < 50.55 and 0.04 < p.x < 0.6]
    leg_x = (min(legs) + max(legs)) / 2 if legs else 0.25
    arm = [p for p in ps if 0.7 < abs(p.x) < 0.9]
    free = [p for p in arm if p.x > 0] or arm   # measure the creature's left arm unless it holds something
    arm_dz = sum(p.z for p in free) / len(free) - VANILLA_ARM[1]
    arm_dy = sum(p.y for p in free) / len(free) - VANILLA_ARM[0]
    eyes = eye_centres(o)
    return {"leg_x": round(leg_x, 3), "arm_dz": round(arm_dz, 3), "arm_dy": round(arm_dy, 3), "eyes": eyes}


def eye_centres(o, depth=0.25, steps=8):
    """
    Centres of the glowing eye texels on the front of the face, per side (Plane = creature's right).
    The texture is sampled across every triangle of the head front (a low-poly face has few vertices,
    the eyes fall between them); the eye is the cluster of samples nearly as bright as the brightest
    (pale yellow, white or bluish glow) on each side.
    """
    me = o.data
    M = o.matrix_world
    img = next(n.image for n in me.materials[0].node_tree.nodes if n.type == "TEX_IMAGE" and n.image and "Normal" not in n.image.name)
    w, h = img.size
    pix = img.pixels[:]
    uv = me.uv_layers.active.data
    co = [M @ v.co for v in me.vertices]
    head = [p for p in co if abs(p.x) < 0.3 and p.z > 50.95]
    front = min(p.y for p in head)
    me.calc_loop_triangles()
    samples = []
    for tri in me.loop_triangles:
        ps = [co[i] for i in tri.vertices]
        if all(abs(p.x) > 0.3 or p.z < 50.95 or p.y > front + depth for p in ps):
            continue
        uvs = [uv[li].uv for li in tri.loops]
        for i in range(steps + 1):
            for j in range(steps + 1 - i):
                a_, b_ = i / steps, j / steps
                c_ = 1 - a_ - b_
                p = ps[0] * a_ + ps[1] * b_ + ps[2] * c_
                if abs(p.x) > 0.3 or p.y > front + depth or abs(p.x) < 0.01:
                    continue
                u = uvs[0].x * a_ + uvs[1].x * b_ + uvs[2].x * c_
                v = uvs[0].y * a_ + uvs[1].y * b_ + uvs[2].y * c_
                k = (min(h - 1, max(0, int(v * h))) * w + min(w - 1, max(0, int(u * w)))) * 4
                r, g, bb = pix[k:k + 3]
                if g > 0.55 or bb >= r:
                    samples.append((r + g + bb, p))
    out = {}
    for name, sign in (("Plane", -1), ("Plane.001", 1)):
        mine = [t for t in samples if t[1].x * sign > 0]
        if not mine:
            continue
        cut = max(b for b, _ in mine) - 0.2   # the glow itself, not the lighter face paint around it
        pts = [p for b, p in mine if b >= cut]
        out[name] = tuple(round(sum(c[j] for c in pts) / len(pts), 3) for j in range(3))
    return out


def rigid_beyond_hand(o, side, start=0.78):
    """Whatever the hand holds (club, stone) rides the hand bone alone: finger bones would bend it."""
    sx = 1 if side == "l" else -1
    hand = o.vertex_groups[f"{side}_hand"]
    ids = [v.index for v in o.data.vertices if (o.matrix_world @ v.co).x * sx > start]
    for g in o.vertex_groups:
        g.remove(ids)
    hand.add(ids, 1.0, "REPLACE")
    return len(ids)


def build(glb, name, work_dir, template_json, tris=3000, max_stretch=60, clean_max=12, tex_size=256):
    """
    Generated model -> rigged game mesh, no hand tuning. Writes <work_dir>/<name>.glmesh and
    tex256.png (run tools/tripo/skin.py on it for the game). Returns a report; report["ok"] is
    False when more than `max_stretch` faces stretch in the arm-pose check. Those are left in place
    (deleting that many tears the mesh): look at them, they usually mean the brief should change
    (see tools/tripo/BRIEFS.md).
    """
    import os
    rig = bpy.data.objects[RIG]
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()
    for n in (name, name + "_high"):
        if n in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)
    high, held = import_auto(glb, name + "_high", "Gen_" + name)
    low, _ = import_auto(glb, name, "Gen_" + name)
    t = gt.decimate(low, tris)
    fill_holes(low)
    png = os.path.join(work_dir, "tex256.png")
    gt.bake_texture(high, low, png, 256)
    high.hide_set(True)
    m = measure(high)
    eyes = m["eyes"] if len(m["eyes"]) == 2 else {"Plane": (-0.08, -0.45, 51.37), "Plane.001": (0.08, -0.45, 51.37)}
    rep = finish(name, m["leg_x"], m["arm_dz"], eyes, os.path.join(work_dir, name.lower() + ".json"),
                 template_json, eye_push=0.03, arm_dy=m["arm_dy"])
    g = bpy.data.objects["GL_" + name + "_Cube"]
    img = bpy.data.images.load(png, check_existing=False)
    next(x for x in g.data.materials[0].node_tree.nodes if x.type == "TEX_IMAGE").image = img
    if held:
        rep["held_rigid"] = rigid_beyond_hand(g, held)
    # heat weights let the arm bones claim the shoulder caps and hump: keep the arm to its own skin,
    # then cut it free of the body (no web between shell and arm), cap both openings, slim the legs
    rep["arm_tube"] = arm_tube(g, rig, radius=0.1, keep_reach=0.1)  # strict: shell just above the arm is not arm
    rep["arm_seam"] = separate_arms(g)
    rep["fragments"] = drop_fragments(g)
    rep["legs_slimmed"] = slim_legs(g, rig)
    rep["face"] = face_to_head(g, eyes)
    fill_holes(g, max_sides=64, image=img)
    fix_winding(g)
    # caps and cuts get real texture: new UVs baked from the generated model
    png_final = os.path.join(work_dir, "tex_final.png")
    rebake(g, rig, bpy.data.objects[RIG + "_fit"], high, png_final, size=tex_size)
    high.hide_set(True)
    img = bpy.data.images.load(png_final, check_existing=False)
    next(x for x in g.data.materials[0].node_tree.nodes if x.type == "TEX_IMAGE").image = img
    rep["texture"] = png_final
    rep["stretch_faces"] = len(remove_stretch(g, rig, dry=True))
    if rep["stretch_faces"] <= clean_max:   # a few stray faces: remove them; more would tear the mesh
        rep["stretch"] = remove_stretch(g, rig, image=img)
    lone = [v.index for v in g.data.vertices if not any(x.weight > 0 for x in v.groups)]
    for vg in g.vertex_groups:
        vg.remove(lone)
    g.vertex_groups["spine3"].add(lone, 1.0, "REPLACE")
    rep["counts"] = gio.export_creature("GL_" + name, template_json, os.path.join(work_dir, name.lower() + ".json"))
    rep.update(tris=t, held=held, measure=m, ok=rep["stretch_faces"] <= max_stretch)
    return rep


# Other creatures whose skeleton is the greyling's, uniformly scaled (same bone names and
# proportions; checked against gldev_export): the game mesh is the greyling-rigged one scaled about
# the feet and written against that creature's bind poses (its export JSON as template).
SKELETON_SCALE = {"Greyling": 1.0, "Greydwarf_Elite": 1.875}


def export_for(coll_name, prefab, template_json, out_json):
    import greenling_io as gio
    from mathutils import Matrix
    k = SKELETON_SCALE[prefab]
    S = Matrix.Translation((0, 0, gt.GROUND_Z)) @ Matrix.Scale(k, 4) @ Matrix.Translation((0, 0, -gt.GROUND_Z))
    objs = list(bpy.data.collections[coll_name].objects)
    saved = {o.name: o.matrix_world.copy() for o in objs}
    try:
        for o in objs:
            o.matrix_world = S @ saved[o.name]
        bpy.context.view_layer.update()
        return gio.export_creature(coll_name, template_json, out_json)
    finally:
        for o in objs:
            o.matrix_world = saved[o.name]
        bpy.context.view_layer.update()


# ---------------------------------------------------------------- debug views

BONE_COLOURS = {
    "head": (1, 0.1, 0.1), "spine3": (0.1, 0.8, 0.1), "spine2": (0.1, 0.5, 0.3), "spine1": (0.2, 0.3, 0.2), "root": (0.3, 0.3, 0.3),
    "l_shoulder": (1, 0.6, 0), "r_shoulder": (0.6, 0.3, 0), "l_arm1": (0.2, 0.4, 1), "r_arm1": (0.6, 0.2, 1),
    "l_arm2": (0.3, 0.8, 1), "r_arm2": (1, 0.3, 0.8), "l_hand": (0, 1, 1), "r_hand": (1, 0, 1),
    "l_leg1": (1, 1, 0), "r_leg1": (0.7, 0.7, 0), "l_leg2": (0.9, 0.9, 0.5), "r_leg2": (0.6, 0.6, 0.3),
}


def bone_colour_render(o, out_prefix, shots, cam_name="PreviewCam"):
    """
    Render `o` coloured by the bone that drives each vertex (paler where it is split between
    bones), posed per shot: shots = [(pose {bone: (x, y, z) degrees}, yaw, ortho_scale, look_z)].
    Shows weights that leak between parts (shell on the arm, shell on the head) at a glance.
    """
    import math
    rig = bpy.data.objects[RIG]
    names = {g.index: g.name for g in o.vertex_groups}
    dbg = bpy.data.objects.get("DBG")
    if dbg:
        bpy.data.objects.remove(dbg, do_unlink=True)
    dbg = o.copy()
    dbg.data = o.data.copy()
    dbg.name = "DBG"
    bpy.context.scene.collection.objects.link(dbg)
    ca = dbg.data.color_attributes.new("bone", "FLOAT_COLOR", "POINT")
    for v in dbg.data.vertices:
        c = (1, 1, 1)
        if v.groups:
            top = max(v.groups, key=lambda x: x.weight)
            base = BONE_COLOURS.get(names[top.group], (0.8, 0.8, 0.8))
            c = tuple(ci * top.weight + 0.9 * (1 - top.weight) for ci in base)
        ca.data[v.index].color = (*c, 1)
    dbg.data.color_attributes.active_color = ca
    sc = bpy.context.scene
    cam = bpy.data.objects[cam_name]
    sh = sc.display.shading
    old = sh.color_type
    sh.color_type = "VERTEX"
    objs = [x for x in sc.objects if x.type == "MESH"]
    saved = {x.name: x.hide_render for x in objs}
    for x in objs:
        x.hide_render = x is not dbg
    paths = []
    try:
        for k, (pose, yaw, scale, look_z) in enumerate(shots):
            for pb in rig.pose.bones:
                pb.rotation_mode = "XYZ"
                pb.rotation_euler = (0, 0, 0)
            for b, e in pose.items():
                rig.pose.bones[b].rotation_euler = tuple(math.radians(a) for a in e)
            cam.data.ortho_scale = scale
            a = math.radians(yaw)
            cam.location = Vector((-math.sin(a) * 4, -math.cos(a) * 4, look_z + 0.6))
            cam.rotation_euler = (Vector((0, 0, look_z)) - cam.location).to_track_quat("-Z", "Y").to_euler()
            sc.render.filepath = f"{out_prefix}_{k}.png"
            bpy.ops.render.render(write_still=True)
            paths.append(sc.render.filepath)
    finally:
        for pb in rig.pose.bones:
            pb.rotation_euler = (0, 0, 0)
        sh.color_type = old
        for x in objs:
            x.hide_render = saved.get(x.name, x.hide_render)
        bpy.data.objects.remove(dbg, do_unlink=True)
    return paths



# ---------------------------------------------------------------- vanilla-proportioned bodies

def fit_targets(leg=(0.17, 0.23, 0.25), leg_y=(0.10, -0.08, -0.04), leg_z=(50.73, 50.45, 50.08),
                arm_dy=0.0, arm_dz=0.0, arm_scale=1.0):
    """
    Bone targets for a body drawn over the vanilla bind pose (tools/blender/vanilla_boards.py):
    legs from measured columns (hip, knee, ankle: x, y, z), the arm chain shifted by (arm_dy, arm_dz)
    and scaled along x from the shoulder by arm_scale (generated arms come out a little short).
    """
    rig = bpy.data.objects[RIG]
    W = rig.matrix_world
    T = {}
    for s, sx in (("l", 1), ("r", -1)):
        for b, x, y, z in zip(("leg1", "leg2", "foot"), leg, leg_y, leg_z):
            T[f"{s}_{b}"] = Vector((sx * x, y, z))
        root = W @ rig.data.bones[f"{s}_arm1"].head_local
        for b in ARM_CHAIN:
            h = W @ rig.data.bones[f"{s}_{b}"].head_local
            T[f"{s}_{b}"] = Vector((root.x + (h.x - root.x) * arm_scale, h.y + arm_dy, h.z + arm_dz))
    return T


def rig_body(o, name, eyes, out_json, template_json, targets, keep_stump=True, eye_push=0.03, head_fn=None):
    """Rig an already low-poly, textured body (Tripo retopology) on the vanilla skeleton and export it.
    head_fn(mesh) runs before the mesh is posed back into the vanilla bind pose: weights that are wrong
    at that point (face on the shoulders) would bake a distortion into the shape."""
    def fixes(m, fit_rig):
        weight_fix(m, fit_rig)
        if head_fn:
            head_fn(m)
    info = gt.rig_to_vanilla(o, RIG, targets, weight_fix=fixes)
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.objects.active = o
    o.select_set(True)
    if o.data.has_custom_normals:
        bpy.ops.mesh.customdata_custom_splitnormals_clear()
    bpy.ops.object.shade_smooth()
    coll = gt.build_collection(o, SRC, eyes, eye_push=eye_push, dst="GL_" + name, keep_stump=keep_stump)
    g = bpy.data.objects["GL_" + name + "_Cube"]
    rig = bpy.data.objects[RIG]
    info["arm_tube"] = arm_tube(g, rig, radius=0.1, keep_reach=0.1)
    smooth_shoulders(g)
    info["face"] = face_to_head(g, eyes)
    if head_fn:
        info["head"] = head_fn(g)   # again: arm_tube and face_to_head above redistribute some of it
    lone = [v.index for v in g.data.vertices if not any(x.weight > 0 for x in v.groups)]
    for vg in g.vertex_groups:
        vg.remove(lone)
    g.vertex_groups["spine3"].add(lone, 1.0, "REPLACE")
    info["stretch_faces"] = len(remove_stretch(g, rig, dry=True))
    info["counts"] = gio.export_creature(coll.name, template_json, out_json)
    return info


def head_to_head(o, eyes, arm_z, iterations=3):
    """
    The whole head unit (face, chin plank and the trunk on top) rides the head bone, blended into the
    torso over a few rings at the neck. A face partly on the head and partly on the torso folds when
    the head turns. Members: everything above the shoulders near the centre line (trunk, top of the
    head) and the front protrusion around and below the eyes (face, chin).
    """
    import bmesh
    eye_z = sum(p[2] for p in eyes.values()) / len(eyes)
    eye_y = sum(p[1] for p in eyes.values()) / len(eyes)
    M = o.matrix_world
    head = o.vertex_groups["head"].index
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bm.verts.ensure_lookup_table()
    w = {}
    for v in bm.verts:
        p = M @ v.co
        # trunk/top of head: above both the shoulders and the eyes, near the centre line
        top = p.z > max(arm_z, eye_z) + 0.1 and abs(p.x) < 0.18 and p.y < 0.12
        # face and chin: the forward protrusion around and below the eyes, not the chest behind it
        face = abs(p.x) < 0.15 and p.y < eye_y + 0.05 and eye_z - 0.3 < p.z < eye_z + 0.2
        w[v.index] = 1.0 if (top or face) else 0.0
    for _ in range(iterations):   # soften the border so the neck bends instead of creasing
        nw = dict(w)
        for v in bm.verts:
            nb = [w[e.other_vert(v).index] for e in v.link_edges]
            if nb and (w[v.index] > 0 or any(nb)):
                nw[v.index] = (w[v.index] + sum(nb)) / (1 + len(nb))
        w = nw
    bm.free()
    n = 0
    for v in o.data.vertices:
        t = w[v.index]
        if t <= 0.0:
            continue
        old = {g.group: g.weight for g in v.groups if g.group != head}
        h = next((g.weight for g in v.groups if g.group == head), 0.0)
        for k, x in old.items():
            o.vertex_groups[k].add([v.index], x * (1 - t), "REPLACE")
        o.vertex_groups[head].add([v.index], h * (1 - t) + t, "REPLACE")
        n += 1
    return n


def place_eyes(o, eyes, push=0.006):
    """Move each eye centre onto the face surface along -Y (front), so the glow quads sit on it."""
    from mathutils.bvhtree import BVHTree
    bvh = BVHTree.FromObject(o, bpy.context.evaluated_depsgraph_get())
    Mi = o.matrix_world.inverted()
    out = {}
    for k, p in eyes.items():
        origin = Mi @ (Vector(p) + Vector((0, -0.5, 0)))
        hit, _, _, _ = bvh.ray_cast(origin, Vector((0, 1, 0)))
        out[k] = tuple(o.matrix_world @ hit + Vector((0, -push, 0))) if hit else p
    return out


def rig_auto(glb, name, out_json, template_json, keep_stump=False):
    """
    Tripo retopology GLB -> rigged greydwarf-family body, no hand tuning: import at the vanilla
    height, weld seam duplicates, find the eyes, measure the arms, fit and export. keep_stump=False
    when the body has its own trunk on top (the vanilla stump is hidden).
    """
    import bmesh
    rig = bpy.data.objects[RIG]
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()
    for n in (name, RIG + "_fit"):
        if n in bpy.data.objects:
            bpy.data.objects.remove(bpy.data.objects[n], do_unlink=True)
    old = bpy.data.collections.get("GL_" + name)
    if old:
        for x in list(old.objects):
            bpy.data.objects.remove(x, do_unlink=True)
        bpy.data.collections.remove(old)
    o, _ = import_auto(glb, name, "Gen_" + name)
    bm = bmesh.new()
    bm.from_mesh(o.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-6)
    bm.to_mesh(o.data)
    bm.free()
    eyes = place_eyes(o, eye_centres(o))
    ps = [o.matrix_world @ v.co for v in o.data.vertices]
    arm = [p for p in ps if 0.6 < abs(p.x) < 0.9]
    arm_z = sum(p.z for p in arm) / len(arm)
    reach = max(p.x for p in ps)
    T = fit_targets(arm_dy=sum(p.y for p in arm) / len(arm) - VANILLA_ARM[0],
                    arm_dz=sum(p.z for p in arm) / len(arm) - VANILLA_ARM[1],
                    arm_scale=(reach - 0.17) / (1.252 - 0.17))
    info = rig_body(o, name, eyes, out_json, template_json, T, keep_stump=keep_stump, eye_push=0.0,
                    head_fn=lambda g: head_to_head(g, eyes, arm_z))
    info["eyes"] = eyes
    return info



def cut_crown(o, z, radius=0.22, y_range=(-0.3, 0.16), dome=0.25):
    """
    Remove what sits on top of the head (a generated trunk) above world height z, inside a column
    around the head (|x| < radius, y in y_range, so the back and shoulders are untouched), and close
    the opening with a slightly domed cap: the crown is then smooth and a trunk prop can sit on it.
    Cap vertices take averaged weights and UVs from the rim (fill_holes).
    """
    import bmesh
    M = o.matrix_world
    Mi = M.inverted()
    bm = bmesh.new()
    bm.from_mesh(o.data)
    inside = lambda p: abs(p.x) < radius and y_range[0] < p.y < y_range[1]
    sel = [f for f in bm.faces if any((M @ v.co).z > z for v in f.verts) and inside(M @ f.calc_center_median())]
    geom = list({e for f in sel for e in f.edges}) + sel + list({v for f in sel for v in f.verts})
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=Mi @ Vector((0, 0, z)), plane_no=(Mi.to_3x3() @ Vector((0, 0, 1))).normalized(),
                           clear_outer=True)
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if not v.link_faces], context="VERTS")
    bm.to_mesh(o.data)
    bm.free()
    img = next((n.image for n in o.data.materials[0].node_tree.nodes if n.type == "TEX_IMAGE" and n.image and "Normal" not in n.image.name), None)
    made = fill_holes(o, max_sides=200, image=img)
    if dome:
        # raise the cap centre a little so the crown is rounded, not a flat lid
        import bmesh as bm_
        b = bm_.new()
        b.from_mesh(o.data)
        top = [v for v in b.verts if abs((M @ v.co).z - z) < 1e-3 and inside(M @ v.co)]
        if top:
            c = sum((M @ v.co for v in top), Vector()) / len(top)
            r = max(((M @ v.co) - c).length for v in top) or 1
            for v in b.verts:
                p = M @ v.co
                if abs(p.z - z) < 1e-3 and inside(p):
                    continue
                if inside(p) and p.z >= z - 1e-4 and (p - c).length < r:
                    v.co = Mi @ (p + Vector((0, 0, dome * r * (1 - ((p - c).length / r) ** 2))))
        b.to_mesh(o.data)
        b.free()
    o.data.update()
    return made



# ---------------------------------------------------------------- hand tuning

TEST_POSES = [   # (frame, {bone: (x, y, z) degrees}) keyed on the rig by setup_paint(), loops over 1..70
    (1, {}),
    (10, {"head": (20, 0, 30)}),
    (20, {"head": (-15, 0, -30)}),
    (30, {"head": (30, 0, 0), "spine3": (10, 0, 0)}),
    (40, {"l_arm1": (0, 0, -60), "r_arm1": (0, 0, 60), "l_arm2": (0, 0, -15), "r_arm2": (0, 0, 15)}),
    (50, {"l_arm1": (-70, 0, -20), "r_arm1": (-70, 0, 20)}),
    (60, {"l_leg1": (35, 0, 0), "l_leg2": (-45, 0, 0), "r_leg1": (-25, 0, 0), "l_arm1": (-20, 0, -60), "r_arm1": (25, 0, 60)}),
    (70, {}),
]


def setup_paint(name):
    """
    Get a body ready for hand weight painting: only it and the rig visible, a looping test-pose
    animation on the rig (frames 1-70: head turns, arms down/forward, a step) to scrub while painting,
    and the body in Weight Paint mode with the rig selected (Ctrl+click a bone to pick its group).
    Export afterwards with export_hand(name).
    """
    import math
    rig = bpy.data.objects[RIG]
    body = bpy.data.objects[f"GL_{name}_Cube"]
    coll = bpy.data.collections[f"GL_{name}"]
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    for o in bpy.context.view_layer.objects:
        o.hide_set(not (o is rig or o.name in coll.objects))
    act = bpy.data.actions.get("TestPoses") or bpy.data.actions.new("TestPoses")
    rig.animation_data_create()
    rig.animation_data.action = act
    for fc in list(act.fcurves) if hasattr(act, "fcurves") else []:
        act.fcurves.remove(fc)
    bones = {b for _, p in TEST_POSES for b in p}
    for frame, pose in TEST_POSES:
        for b in bones:
            pb = rig.pose.bones[b]
            pb.rotation_mode = "XYZ"
            pb.rotation_euler = tuple(math.radians(a) for a in pose.get(b, (0, 0, 0)))
            pb.keyframe_insert("rotation_euler", frame=frame)
    sc = bpy.context.scene
    sc.frame_start, sc.frame_end = 1, 70
    sc.frame_set(1)
    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.mode_set(mode="WEIGHT_PAINT")
    return {"body": body.name, "rig": rig.name, "groups": len(body.vertex_groups)}


def export_hand(name, template_json, out_json, models_dir=None, file_name=None):
    """Export a hand-tuned body as it stands (no rebuild) and optionally copy it into the game's models folder."""
    import os
    import shutil
    rig = bpy.data.objects[RIG]
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    if rig.animation_data:
        rig.animation_data.action = None
    for pb in rig.pose.bones:
        pb.matrix_basis.identity()
    counts = gio.export_creature(f"GL_{name}", template_json, out_json)
    glmesh = out_json[:-5] + ".glmesh"
    if models_dir:
        shutil.copy(glmesh, os.path.join(models_dir, (file_name or os.path.basename(glmesh))))
    return counts
