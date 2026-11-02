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
        centre = bm.verts.new(sum((v.co for v in verts), verts[0].co * 0) / len(verts))
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


def remove_stretch(o, rig, image=None, ratio=1.8, min_len=0.08, passes=4):
    """
    Pose the rig through ARM_TEST_POSES, find edges that stretch more than `ratio` (and end up longer
    than `min_len`), delete their faces plus any small floating fragments left behind, and fill the
    holes. Repeats until nothing stretches. Returns faces removed per pass.
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
