"""
Prop Studio: a sidebar panel (View3D > N panel > Greenlings) for placing props on a greenling body
by hand. Wraps the prop live with greenling_props.wrap and saves named placements to
tools/props/placements.json (positions in the vanilla bind pose, so they fit every variant).

    import prop_studio; prop_studio.register(r"<repo>")

Use: pick the body and the prop, Shift+right-click on the body to put the 3D cursor where the prop
should stand, the prop (or the decoration piece being edited) follows the cursor; then tweak the sliders (the preview updates live). Name it and
press Save. Load / Delete work on the saved list.
"""
import json
import os

import bpy
from mathutils import Vector

import greenling_props as gp

PREVIEW = "PropPreview"
_repo = None


def _path():
    return os.path.join(_repo, "tools", "props", "placements.json")


def _load_all():
    try:
        with open(_path(), encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return {}


def _save_all(d):
    os.makedirs(os.path.dirname(_path()), exist_ok=True)
    with open(_path(), "w", encoding="utf-8") as f:
        json.dump(d, f, indent=1, sort_keys=True)


def _is_prop(self, o):
    return o.type == "MESH" and o.name != PREVIEW and o.name.startswith(("trunk_", "leaf_", "mush", "twig"))


def _is_body(self, o):
    return o.type == "MESH" and o.name.startswith("GL_") and o.name.endswith("_Cube")


def _saved_items(self, context):
    return [(k, k, "") for k in sorted(_load_all())] or [("", "(none saved)", "")]


def rebuild(self=None, context=None):
    s = bpy.context.scene.prop_studio
    old = bpy.data.objects.get(PREVIEW)
    if old:
        bpy.data.objects.remove(old, do_unlink=True)
    if not (s.body and s.prop and s.has_anchor):
        return
    if s.edit_leaf:
        import decorate
        # surface direction at the (possibly moved) anchor, pointing away from the body's axis
        from mathutils.bvhtree import BVHTree
        body = s.body
        bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
        hit, hn, _, _ = bvh.find_nearest(body.matrix_world.inverted() @ Vector(s.anchor))
        p = body.matrix_world @ hit if hit else Vector(s.anchor)
        n = (body.matrix_world.to_3x3() @ hn).normalized() if hit else Vector((0, 1, 0))
        if n.dot(p - Vector((0.0, 0.1, p.z))) < 0:
            n = -n
        nrm = list(n)
        o = decorate.leaf_from({"prop": s.prop.name, "anchor": list(s.anchor), "normal": list(nrm), "droop": s.l_droop,
                                "roll": s.l_roll, "size": s.l_size, "tuck": s.l_tuck}, s.body, PREVIEW)
        bpy.context.scene.collection.objects.link(o)
        o.hide_set(False)
        return
    o = gp.wrap(s.prop, s.body, tuple(s.anchor), hint=tuple(s.hint), spin=s.spin, offset=s.offset,
                bone=s.bone, name=PREVIEW, scale=s.scale, sink=s.sink, upright=s.upright,
                lean=(s.lean[0], s.lean[1], 0.0), roots=s.roots, tilt=tuple(s.tilt))
    o.hide_set(False)
    o.hide_render = False


def _upd(self, context):
    if not getattr(rebuild, "busy", False):
        rebuild.busy = True
        try:
            rebuild()
        finally:
            rebuild.busy = False


class PaletteItem(bpy.types.PropertyGroup):
    obj: bpy.props.PointerProperty(type=bpy.types.Object, poll=_is_prop)
    on: bpy.props.BoolProperty(default=True)
    weight: bpy.props.FloatProperty(name="Weight", default=1, min=0, max=20)


class Settings(bpy.types.PropertyGroup):
    palette: bpy.props.CollectionProperty(type=PaletteItem)
    palette_i: bpy.props.IntProperty()
    p_overlap: bpy.props.BoolProperty(name="Avoid overlaps", default=True,
                                      description="Skip a dab that would cut into another decoration")
    p_scale: bpy.props.FloatVectorProperty(name="Prop scale", size=2, default=(0.7, 1.0), min=0.05, max=5)
    p_upright: bpy.props.FloatProperty(name="Prop upright", default=0.6, min=0, max=1,
                                       description="Painted trunks/mushrooms/twigs: 1 = straight up, 0 = along the normal")
    body: bpy.props.PointerProperty(type=bpy.types.Object, poll=_is_body, update=_upd)
    prop: bpy.props.PointerProperty(type=bpy.types.Object, poll=_is_prop, update=_upd)
    has_anchor: bpy.props.BoolProperty(default=False)
    anchor: bpy.props.FloatVectorProperty(size=3, update=_upd)
    hint: bpy.props.FloatVectorProperty(size=3, default=(0, 1, 0.5), update=_upd,
                                        description="Which way 'out' is (decides the side of a thin part)")
    spin: bpy.props.FloatProperty(name="Spin", default=0, min=-180, max=180, update=_upd)
    scale: bpy.props.FloatProperty(name="Scale", default=1, min=0.05, max=5, update=_upd)
    upright: bpy.props.FloatProperty(name="Upright", default=0.75, min=0, max=1, update=_upd,
                                     description="1 = straight up, 0 = along the surface normal")
    lean: bpy.props.FloatVectorProperty(name="Lean", size=2, default=(0, 0.25), min=-5, max=5, update=_upd)
    tilt: bpy.props.FloatVectorProperty(name="Tilt", size=2, default=(0, 0), min=-180, max=180, update=_upd,
                                        description="Rotate about the prop's own X / Y axes, degrees")
    sink: bpy.props.FloatProperty(name="Sink", default=0.03, min=-0.5, max=0.5, update=_upd,
                                  description="How far the base sits into the body")
    offset: bpy.props.FloatProperty(name="Root offset", default=0.008, min=-0.1, max=0.3, update=_upd,
                                    description="Gap between roots and skin")
    roots: bpy.props.BoolProperty(name="Wrap roots", default=True, update=_upd,
                                  description="Bend the prop's spread-out base onto the skin (off: rigid)")
    bone: bpy.props.EnumProperty(name="Bone", items=[(b, b, "") for b in ("spine3", "spine2", "spine1", "head",
                                 "l_arm1", "r_arm1", "l_shoulder", "r_shoulder")], update=_upd)
    save_name: bpy.props.StringProperty(name="Name", default="trunk_back")
    editing: bpy.props.StringProperty(default="")   # name of the decoration piece being re-edited
    edit_leaf: bpy.props.BoolProperty(default=False)
    l_droop: bpy.props.FloatProperty(name="Droop", default=35, min=-90, max=180, update=_upd)
    l_roll: bpy.props.FloatProperty(name="Roll", default=0, min=-180, max=180, update=_upd)
    l_size: bpy.props.FloatProperty(name="Size", default=1, min=0.1, max=4, update=_upd)
    l_tuck: bpy.props.FloatProperty(name="Tuck", default=0.03, min=-0.1, max=0.3, update=_upd)
    # procedural decoration
    d_seed: bpy.props.IntProperty(name="Seed", default=1, min=0)
    d_count: bpy.props.IntProperty(name="Count", default=10, min=1, max=60)
    d_gap: bpy.props.FloatProperty(name="Spacing", default=0.09, min=0.01, max=0.5)
    d_back: bpy.props.BoolProperty(name="Back", default=True)
    d_shoulders: bpy.props.BoolProperty(name="Shoulders", default=True)
    d_crown: bpy.props.BoolProperty(name="Crown", default=True)
    d_face: bpy.props.BoolProperty(name="Face", default=False)
    d_max_trunks: bpy.props.IntProperty(name="Max trunks", default=1, min=0, max=5)
    d_droop: bpy.props.FloatProperty(name="Leaf droop", default=35, min=0, max=90, description="0 = leaves stick straight out, 90 = lie flat down the body")
    d_tuck: bpy.props.FloatProperty(name="Leaf tuck", default=0.03, min=0, max=0.15, description="How deep leaf bases go into the skin")
    d_size: bpy.props.FloatVectorProperty(name="Leaf size", size=2, default=(0.8, 1.2), min=0.2, max=3)
    saved: bpy.props.EnumProperty(name="Saved", items=_saved_items)


class PS_OT_anchor(bpy.types.Operator):
    bl_idname = "prop_studio.anchor"
    bl_label = "Anchor here (3D cursor)"
    bl_description = "Use the 3D cursor (Shift+right-click on the body) as the prop's base"

    def execute(self, context):
        s = context.scene.prop_studio
        s.has_anchor = True
        s.anchor = context.scene.cursor.location
        return {"FINISHED"}


class PS_OT_save(bpy.types.Operator):
    bl_idname = "prop_studio.save"
    bl_label = "Save placement"

    def execute(self, context):
        s = context.scene.prop_studio
        if not (s.prop and s.has_anchor):
            self.report({"ERROR"}, "pick a prop and an anchor first")
            return {"CANCELLED"}
        d = _load_all()
        d[s.save_name] = {"prop": s.prop.name, "anchor": [round(x, 4) for x in s.anchor],
                          "hint": [round(x, 3) for x in s.hint], "spin": round(s.spin, 2), "scale": round(s.scale, 3),
                          "upright": round(s.upright, 3), "lean": [round(x, 3) for x in s.lean], "sink": round(s.sink, 4),
                          "offset": round(s.offset, 4), "bone": s.bone, "roots": s.roots, "tilt": [round(x, 2) for x in s.tilt]}
        _save_all(d)
        s.saved = s.save_name
        self.report({"INFO"}, f"saved {s.save_name} to {_path()}")
        return {"FINISHED"}


class PS_OT_load(bpy.types.Operator):
    bl_idname = "prop_studio.load"
    bl_label = "Load"

    def execute(self, context):
        s = context.scene.prop_studio
        c = _load_all().get(s.saved)
        if not c:
            return {"CANCELLED"}
        rebuild.busy = True
        try:
            if c["prop"] in bpy.data.objects:
                s.prop = bpy.data.objects[c["prop"]]
            s.anchor, s.hint, s.spin, s.scale = c["anchor"], c["hint"], c["spin"], c["scale"]
            s.upright, s.lean, s.sink, s.offset, s.bone = c["upright"], c["lean"], c["sink"], c["offset"], c["bone"]
            s.roots = c.get("roots", True)
            s.tilt = c.get("tilt", (0, 0))
            s.has_anchor, s.save_name = True, s.saved
        finally:
            rebuild.busy = False
        rebuild()
        return {"FINISHED"}


class PS_OT_delete(bpy.types.Operator):
    bl_idname = "prop_studio.delete"
    bl_label = "Delete"

    def execute(self, context):
        d = _load_all()
        d.pop(context.scene.prop_studio.saved, None)
        _save_all(d)
        return {"FINISHED"}


class PS_OT_edit_piece(bpy.types.Operator):
    bl_idname = "prop_studio.edit_piece"
    bl_label = "Edit selected"
    bl_description = "Load the selected decoration (trunk/mushroom/twig) into the sliders; Apply replaces it"

    def execute(self, context):
        o = context.active_object
        s = context.scene.prop_studio
        if o and o.get("leaf"):
            c = o["leaf"].to_dict()
            rebuild.busy = True
            try:
                s.prop = bpy.data.objects[c["prop"]]
                s.anchor, s.l_droop, s.l_roll, s.l_size, s.l_tuck = c["anchor"], c["droop"], c["roll"], c["size"], c["tuck"]
                s.has_anchor, s.editing, s.edit_leaf = True, o.name, True
            finally:
                rebuild.busy = False
            o.hide_set(True)
            rebuild()
            return {"FINISHED"}
        c = o.get("studio") if o else None
        if not c:
            self.report({"ERROR"}, "select a decoration piece")
            return {"CANCELLED"}
        c = c.to_dict()
        s.edit_leaf = False
        rebuild.busy = True
        try:
            s.prop = bpy.data.objects[c["prop"]]
            s.anchor, s.hint, s.spin, s.scale = c["anchor"], c["hint"], c["spin"], c["scale"]
            s.sink, s.upright, s.lean, s.tilt = c["sink"], c["upright"], c["lean"], c["tilt"]
            s.roots, s.offset, s.has_anchor, s.editing = c["roots"], c["offset"], True, o.name
        finally:
            rebuild.busy = False
        o.hide_set(True)
        rebuild()
        return {"FINISHED"}


class PS_OT_apply_piece(bpy.types.Operator):
    bl_idname = "prop_studio.apply_piece"
    bl_label = "Apply"
    bl_description = "Replace the piece being edited with the current preview"

    def execute(self, context):
        s = context.scene.prop_studio
        old = bpy.data.objects.get(s.editing)
        pv = bpy.data.objects.get(PREVIEW)
        if not (old and pv):
            return {"CANCELLED"}
        name, colls = old.name, list(old.users_collection)
        bpy.data.objects.remove(old, do_unlink=True)
        for c in list(pv.users_collection):
            c.objects.unlink(pv)
        for c in colls:
            c.objects.link(pv)
        pv.name = name
        if s.edit_leaf:
            # anchor may have moved: keep the old normal unless it moved far (then take the surface's)
            import decorate
            decorate._origin_at(pv, Vector(s.anchor))
            s.editing, s.edit_leaf = "", False
            return {"FINISHED"}
        pv["studio"] = {"prop": s.prop.name, "anchor": list(s.anchor), "hint": list(s.hint), "spin": s.spin,
                        "scale": s.scale, "sink": s.sink, "upright": s.upright, "lean": list(s.lean),
                        "tilt": list(s.tilt), "roots": s.roots, "offset": s.offset}
        import decorate
        decorate._origin_at(pv, Vector(s.anchor))
        s.editing = ""
        return {"FINISHED"}


class PS_OT_decorate(bpy.types.Operator):
    bl_idname = "prop_studio.decorate"
    bl_label = "Decorate"
    bl_description = "Place props procedurally on the body (collection Decor)"

    def execute(self, context):
        import decorate
        s = context.scene.prop_studio
        if not s.body:
            return {"CANCELLED"}
        zones = tuple(z for z in ("back", "shoulders", "crown", "face") if getattr(s, "d_" + z))
        opts = dict(_brush_opts(s), seed=s.d_seed, count=s.d_count, gap=s.d_gap, zones=zones,
                    max_trunks=s.d_max_trunks, palette=_palette(s))
        placed = decorate.run(s.body, opts)
        self.report({"INFO"}, f"placed {len(placed)}")
        return {"FINISHED"}


def _palette(s):
    return [(i.obj.name, i.weight) for i in s.palette if i.on and i.obj and i.weight > 0]


def _brush_opts(s):
    return dict(leaf_size=tuple(s.d_size), droop=s.d_droop, tuck=s.d_tuck,
                prop_scale=tuple(s.p_scale), upright=s.p_upright)


def fill_palette(s):
    """Every prop in the scene, weighted like the procedural defaults (beech leaves off)."""
    import decorate
    s.palette.clear()
    w = dict(decorate.default_palette())
    for o in sorted((o for o in bpy.data.objects if _is_prop(None, o) and not o.name.endswith("_rig")),
                    key=lambda o: o.name):
        it = s.palette.add()
        it.obj = o
        it.weight = round(w.get(o.name, 1.0), 2)
        it.on = o.name in w


class PS_OT_palette_fill(bpy.types.Operator):
    bl_idname = "prop_studio.palette_fill"
    bl_label = "Reset list"
    bl_description = "List every prop in the scene with default weights"

    def execute(self, context):
        fill_palette(context.scene.prop_studio)
        return {"FINISHED"}


class PS_OT_palette_add(bpy.types.Operator):
    bl_idname = "prop_studio.palette_add"
    bl_label = "Add selected props"
    bl_description = "Add the props selected in the viewport (or the Prop Studio prop) to the list"

    def execute(self, context):
        s = context.scene.prop_studio
        have = {i.obj for i in s.palette}
        for o in [o for o in context.selected_objects if _is_prop(None, o)] or [s.prop]:
            if o and o not in have:
                s.palette.add().obj = o
        return {"FINISHED"}


class PS_OT_palette_remove(bpy.types.Operator):
    bl_idname = "prop_studio.palette_remove"
    bl_label = "Remove from list"

    def execute(self, context):
        s = context.scene.prop_studio
        if 0 <= s.palette_i < len(s.palette):
            s.palette.remove(s.palette_i)
            s.palette_i = max(0, s.palette_i - 1)
        return {"FINISHED"}


class PS_UL_palette(bpy.types.UIList):
    def draw_item(self, context, layout, data, item, icon, active_data, active_property, index=0, flt_flag=0):
        row = layout.row(align=True)
        row.prop(item, "on", text="")
        row.label(text=item.obj.name if item.obj else "(missing)")
        row.prop(item, "weight", text="")


class PS_OT_paint(bpy.types.Operator):
    """Paint props from the list onto the body: click or drag to place, Ctrl+click to erase,
    Ctrl+Z to undo the last dab, Esc / right-click to stop. Navigation works as usual"""
    bl_idname = "prop_studio.paint"
    bl_label = "Paint"

    def _hit(self, context, event):
        import decorate
        from bpy_extras import view3d_utils
        s = context.scene.prop_studio
        # the viewport's main region, not the sidebar the Paint button was pressed in
        r, rv = self.region, self.area.spaces.active.region_3d
        xy = (event.mouse_x - r.x, event.mouse_y - r.y)
        o3 = view3d_utils.region_2d_to_origin_3d(r, rv, xy)
        d3 = view3d_utils.region_2d_to_vector_3d(r, rv, xy)
        M = s.body.matrix_world
        Mi = M.inverted()
        ok, p, n, _ = s.body.ray_cast(Mi @ o3, (Mi.to_3x3() @ d3).normalized())
        if not ok:
            return None
        p = M @ p
        return p, decorate.outward(p, (Mi.to_3x3().transposed() @ n).normalized())

    def _dab(self, context, event):
        import decorate
        s = context.scene.prop_studio
        hit = self._hit(context, event)
        if not hit:
            return
        p, n = hit
        if self.last is not None and (p - self.last).length < s.d_gap:
            return
        pal = _palette(s)
        if not pal:
            self.report({"WARNING"}, "no props ticked in the list")
            return
        coll = decorate.decor_collection()
        near = [u for u in coll.objects if (u.matrix_world.translation - p).length < 0.6]
        # crowded spots: try other props / rotations, shrinking, before giving up
        tries = 8 if s.p_overlap else 1
        for t in range(tries):
            prop = bpy.data.objects[self.rnd.choices([a for a, _ in pal], [w for _, w in pal])[0]]
            k = 0
            while f"P{k:02d}_{prop.name}" in bpy.data.objects:
                k += 1
            f = 1.0 - 0.5 * t / max(tries - 1, 1)
            opts = _brush_opts(s)
            opts["leaf_size"] = tuple(x * f for x in opts["leaf_size"])
            opts["prop_scale"] = tuple(x * f for x in opts["prop_scale"])
            o = decorate.place(prop, s.body, p, n, self.rnd, opts, f"P{k:02d}_{prop.name}")
            hit = decorate.overlaps(o, near) if s.p_overlap else None
            if not hit:
                self.placed.append(o.name)
                self.last = p
                self.msg = ""
                return
            bpy.data.objects.remove(o, do_unlink=True)
        self.msg = f"  | no room: blocked by {hit} (Ctrl+click it, or untick Avoid overlaps)"

    def _erase(self, context, event):
        hit = self._hit(context, event)
        coll = bpy.data.collections.get("Decor")
        if not hit or not coll or not coll.objects:
            return
        o = min(coll.objects, key=lambda o: (o.matrix_world.translation - hit[0]).length)
        if (o.matrix_world.translation - hit[0]).length < 0.15:
            bpy.data.objects.remove(o, do_unlink=True)

    def _status(self, context):
        self.area.header_text_set(
            f"Paint props: click/drag place, Ctrl+click erase, Ctrl+Z undo, Esc/right-click done  ({len(self.placed)} placed)"
            + self.msg)

    def invoke(self, context, event):
        import random
        s = context.scene.prop_studio
        if not s.body or context.area.type != "VIEW_3D":
            self.report({"WARNING"}, "pick a body and run from the 3D viewport")
            return {"CANCELLED"}
        if not s.palette:
            fill_palette(s)
        self.area = context.area
        self.region = next(r for r in context.area.regions if r.type == "WINDOW")
        self.rnd = random.Random()
        self.placed, self.last, self.down, self.msg = [], None, False, ""
        context.window_manager.modal_handler_add(self)
        self._status(context)
        return {"RUNNING_MODAL"}

    def modal(self, context, event):
        if event.type in {"ESC", "RIGHTMOUSE"} and event.value == "PRESS" or not self.area.regions:
            self.area.header_text_set(None)
            return {"FINISHED"}
        if event.type == "Z" and event.ctrl and event.value == "PRESS":
            while self.placed:
                o = bpy.data.objects.get(self.placed.pop())
                if o:
                    bpy.data.objects.remove(o, do_unlink=True)
                    break
            self._status(context)
            return {"RUNNING_MODAL"}
        r = self.region
        inside = r.x <= event.mouse_x < r.x + r.width and r.y <= event.mouse_y < r.y + r.height
        # the sidebar overlaps the main region's right edge: clicks there go to its buttons
        ui = next((u for u in self.area.regions if u.type == "UI" and u.width > 1), None)
        if ui and ui.x <= event.mouse_x < ui.x + ui.width and ui.y <= event.mouse_y < ui.y + ui.height:
            inside = False
        if event.type == "LEFTMOUSE" and (inside or event.value == "RELEASE"):
            if event.value == "PRESS":
                self.down, self.last = True, None
                (self._erase if event.ctrl else self._dab)(context, event)
            elif event.value == "RELEASE":
                self.down = False
            self._status(context)
            return {"RUNNING_MODAL"}
        if event.type == "MOUSEMOVE" and self.down and inside and not event.ctrl:
            self._dab(context, event)
            self._status(context)
            return {"RUNNING_MODAL"}
        return {"PASS_THROUGH"}


class PS_OT_reroll(bpy.types.Operator):
    bl_idname = "prop_studio.reroll"
    bl_label = "New seed"

    def execute(self, context):
        context.scene.prop_studio.d_seed += 1
        return bpy.ops.prop_studio.decorate()


class PS_OT_clear_decor(bpy.types.Operator):
    bl_idname = "prop_studio.clear_decor"
    bl_label = "Clear"

    def execute(self, context):
        c = bpy.data.collections.get("Decor")
        if c:
            for o in list(c.objects):
                bpy.data.objects.remove(o, do_unlink=True)
        return {"FINISHED"}


class PS_PT_decorate(bpy.types.Panel):
    bl_label = "Decorate"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Greenlings"

    def draw(self, context):
        s = context.scene.prop_studio
        col = self.layout.column()
        row = col.row()
        row.operator("prop_studio.decorate", icon="OUTLINER_OB_POINTCLOUD")
        row.operator("prop_studio.reroll", text="", icon="FILE_REFRESH")
        row.operator("prop_studio.clear_decor", text="", icon="TRASH")
        box = col.box()
        row = box.row()
        row.label(text="Props")
        row.operator("prop_studio.palette_add", text="", icon="ADD")
        row.operator("prop_studio.palette_remove", text="", icon="REMOVE")
        row.operator("prop_studio.palette_fill", text="", icon="FILE_REFRESH")
        box.template_list("PS_UL_palette", "", s, "palette", s, "palette_i", rows=6)
        box.operator("prop_studio.paint", icon="BRUSH_DATA")
        box.prop(s, "p_overlap")
        row = col.row()
        row.operator("prop_studio.edit_piece", icon="GREASEPENCIL")
        if s.editing:
            row.operator("prop_studio.apply_piece", icon="CHECKMARK")
            if s.edit_leaf:
                box = col.box()
                box.label(text="Editing leaf " + s.editing)
                box.operator("prop_studio.anchor", icon="PIVOT_CURSOR")
                for k in ("prop", "l_droop", "l_roll", "l_size", "l_tuck"):
                    box.prop(s, k)
            else:
                col.label(text="Editing " + s.editing + " (sliders above)")
        col.prop(s, "d_seed")
        col.prop(s, "d_count")
        col.prop(s, "d_gap")
        row = col.row(align=True)
        for z in ("back", "shoulders", "crown", "face"):
            row.prop(s, "d_" + z, toggle=True)
        col.prop(s, "d_max_trunks")
        box = col.box()
        box.label(text="Placement (decorate and paint)")
        for k in ("d_droop", "d_tuck", "d_size", "p_scale", "p_upright"):
            box.prop(s, k)


class PS_PT_panel(bpy.types.Panel):
    bl_label = "Prop Studio"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Greenlings"

    def draw(self, context):
        s = context.scene.prop_studio
        col = self.layout.column()
        col.prop(s, "body")
        col.prop(s, "prop")
        col.operator("prop_studio.anchor", icon="PIVOT_CURSOR")
        if s.has_anchor:
            col.label(text="Anchor %.2f %.2f %.2f" % tuple(s.anchor))
        box = col.box()
        for k in ("spin", "tilt", "scale", "upright", "lean", "sink", "roots", "offset", "hint", "bone"):
            box.prop(s, k)
        row = col.row()
        row.prop(s, "save_name")
        row.operator("prop_studio.save", text="", icon="FILE_TICK")
        row = col.row()
        row.prop(s, "saved", text="")
        row.operator("prop_studio.load", text="", icon="IMPORT")
        row.operator("prop_studio.delete", text="", icon="TRASH")


CLASSES = (PaletteItem, Settings, PS_OT_palette_fill, PS_OT_palette_add, PS_OT_palette_remove, PS_UL_palette,
           PS_OT_paint, PS_OT_anchor, PS_OT_save, PS_OT_load, PS_OT_delete, PS_OT_edit_piece, PS_OT_apply_piece,
           PS_OT_decorate, PS_OT_reroll,
           PS_OT_clear_decor, PS_PT_panel, PS_PT_decorate)


_last_cursor = None


def _follow_cursor():
    """Moving the 3D cursor (Shift+right-click) moves the prop being placed or edited: no button."""
    global _last_cursor
    sc = bpy.context.scene
    s = getattr(sc, "prop_studio", None)
    if s is None:
        return 0.2
    cur = tuple(round(x, 5) for x in sc.cursor.location)
    if _last_cursor is not None and cur != _last_cursor and (s.prop or s.editing):
        s.has_anchor = True
        s.anchor = sc.cursor.location
    _last_cursor = cur
    return 0.2


def register(repo):
    global _repo, _last_cursor
    _repo = repo
    unregister()
    for c in CLASSES:
        bpy.utils.register_class(c)
    bpy.types.Scene.prop_studio = bpy.props.PointerProperty(type=Settings)
    _last_cursor = None
    try:
        for sc in bpy.data.scenes:
            if not sc.prop_studio.palette:
                fill_palette(sc.prop_studio)
    except Exception:
        pass      # restricted context at startup: the list fills on first Paint / Reset list
    bpy.app.timers.register(_follow_cursor, first_interval=0.2, persistent=True)


def unregister():
    if bpy.app.timers.is_registered(_follow_cursor):
        bpy.app.timers.unregister(_follow_cursor)
    for c in reversed(CLASSES):
        try:
            bpy.utils.unregister_class(c)
        except RuntimeError:
            pass
