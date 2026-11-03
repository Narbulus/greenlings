"""
Prop Studio: one sidebar panel (View3D > N panel > Greenlings) for putting props on a greenling body,
by hand or procedurally. Every piece lives in the "Decor" collection and stays editable.

    import prop_studio; prop_studio.register(r"<repo>")

Use: pick the body, then
- Place: Shift+right-click on the body (3D cursor), press Place: the prop picked in the list goes there.
- Paint: click/drag on the body to scatter props from the ticked list (Ctrl+click erase, Esc done).
- Decorate: procedural fill of the zones (replaces only its own earlier pieces).
- Select any piece (click it) and it is the one being edited: the sliders change it live; moving the
  3D cursor or grabbing it (G) moves it, its roots re-wrapping onto the skin where it lands.
Save stores the selected piece's settings by name in tools/props/placements.json (vanilla bind-pose
coordinates, so they fit every variant); Load places a saved one again.
"""
import json
import os
import random

import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree

import greenling_props as gp

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
    return (o.type == "MESH" and o.name.startswith(("trunk_", "leaf_", "mush", "twig"))
            and not o.name.endswith("_rig"))


def _is_body(self, o):
    return o.type == "MESH" and o.name.startswith("GL_") and o.name.endswith("_Cube")


def _is_piece(o):
    return o is not None and (o.get("studio") is not None or o.get("leaf") is not None)


def _saved_items(self, context):
    return [(k, k, "") for k in sorted(_load_all())] or [("", "(none saved)", "")]


def _surface(body, q):
    """Nearest point on the body to q and its outward normal (world space)."""
    import decorate
    bvh = BVHTree.FromObject(body, bpy.context.evaluated_depsgraph_get())
    M = body.matrix_world
    hit, hn, _, _ = bvh.find_nearest(M.inverted() @ Vector(q))
    p = M @ hit
    return p, decorate.outward(p, (M.to_3x3() @ hn).normalized())


def _params(o):
    """('leaf' | 'prop', settings dict) stored on a piece."""
    if o.get("leaf") is not None:
        return "leaf", o["leaf"].to_dict()
    return "prop", o["studio"].to_dict()


def build_piece(s, name):
    """Build a piece from the sliders at s.anchor (snapped to the skin), in the Decor collection, origin at
    its base, settings stored on it. The prop's name decides leaf card or wrapped prop."""
    import decorate
    p, n = _surface(s.body, s.anchor)
    if decorate.kind_of(s.prop.name) in ("leaf", "tuft"):
        o = decorate.leaf_from({"prop": s.prop.name, "anchor": list(p), "normal": list(n), "droop": s.l_droop,
                                "roll": s.l_roll, "size": s.l_size, "tuck": s.l_tuck}, s.body, name)
    else:
        o = gp.wrap(s.prop, s.body, tuple(p), hint=tuple(n), spin=s.spin, offset=s.offset,
                    bone=s.bone, name=name, scale=s.scale, sink=s.sink, upright=s.upright,
                    lean=(s.lean[0], s.lean[1], 0.0), roots=s.roots, tilt=tuple(s.tilt))
        o["studio"] = {"prop": s.prop.name, "anchor": list(p), "hint": list(n), "spin": s.spin, "scale": s.scale,
                       "sink": s.sink, "upright": s.upright, "lean": list(s.lean), "tilt": list(s.tilt),
                       "roots": s.roots, "offset": s.offset, "bone": s.bone}
    for c in list(o.users_collection):
        c.objects.unlink(o)
    decorate.decor_collection().objects.link(o)
    decorate._origin_at(o, p)
    return o, p


def _select(o):
    vl = bpy.context.view_layer
    for x in list(vl.objects.selected):
        if x:
            x.select_set(False)
    o.hide_set(False)
    o.select_set(True)
    vl.objects.active = o


def rebuild(self=None, context=None):
    """Re-make the piece being edited from the sliders, in place (same name, stays selected)."""
    s = bpy.context.scene.prop_studio
    old = bpy.data.objects.get(s.editing)
    if not (s.body and s.prop and old):
        return
    name = old.name
    old.name = name + "~"
    o, p = build_piece(s, name)
    _select(o)
    bpy.data.objects.remove(old, do_unlink=True)
    o.name = name
    _quiet(lambda: setattr(s, "anchor", p))


def _quiet(fn):
    rebuild.busy = True
    try:
        fn()
    finally:
        rebuild.busy = False


def _upd(self, context):
    if not getattr(rebuild, "busy", False):
        _quiet(rebuild)


def load_piece(s, o):
    """Sliders <- the piece's stored settings."""
    kind, c = _params(o)

    def go():
        if c["prop"] in bpy.data.objects:
            s.prop = bpy.data.objects[c["prop"]]
        s.anchor = c["anchor"]
        if kind == "leaf":
            s.l_droop, s.l_roll, s.l_size, s.l_tuck = c["droop"], c["roll"], c["size"], c["tuck"]
        else:
            s.spin, s.scale, s.sink, s.upright = c["spin"], c["scale"], c["sink"], c["upright"]
            s.lean, s.tilt, s.roots, s.offset = c["lean"], c.get("tilt", (0, 0)), c["roots"], c["offset"]
            s.bone = c.get("bone", "spine3")
        s.editing = o.name
    _quiet(go)


class PaletteItem(bpy.types.PropertyGroup):
    obj: bpy.props.PointerProperty(type=bpy.types.Object, poll=_is_prop)
    on: bpy.props.BoolProperty(default=True, description="Use in Paint and Decorate")
    weight: bpy.props.FloatProperty(name="Weight", default=1, min=0, max=20)


class Settings(bpy.types.PropertyGroup):
    body: bpy.props.PointerProperty(type=bpy.types.Object, poll=_is_body)
    palette: bpy.props.CollectionProperty(type=PaletteItem)
    palette_i: bpy.props.IntProperty(description="The prop Place puts down")
    p_overlap: bpy.props.BoolProperty(name="Avoid overlaps", default=True,
                                      description="Skip a dab that would cut into another decoration")
    # the selected piece
    editing: bpy.props.StringProperty(default="")
    prop: bpy.props.PointerProperty(name="Prop", type=bpy.types.Object, poll=_is_prop, update=_upd,
                                    description="Swap the selected piece's prop")
    anchor: bpy.props.FloatVectorProperty(size=3, update=_upd)
    spin: bpy.props.FloatProperty(name="Spin", default=0, min=-180, max=180, update=_upd)
    scale: bpy.props.FloatProperty(name="Scale", default=1, min=0.05, max=5, update=_upd)
    upright: bpy.props.FloatProperty(name="Upright", default=0.75, min=0, max=1, update=_upd,
                                     description="1 = straight up, 0 = along the surface normal")
    lean: bpy.props.FloatVectorProperty(name="Lean", size=2, default=(0, 0), min=-5, max=5, update=_upd)
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
    l_droop: bpy.props.FloatProperty(name="Droop", default=35, min=-90, max=180, update=_upd)
    l_roll: bpy.props.FloatProperty(name="Roll", default=0, min=-180, max=180, update=_upd)
    l_size: bpy.props.FloatProperty(name="Size", default=1, min=0.1, max=4, update=_upd)
    l_tuck: bpy.props.FloatProperty(name="Tuck", default=0.03, min=-0.1, max=0.3, update=_upd)
    save_name: bpy.props.StringProperty(name="Name", default="trunk_back")
    saved: bpy.props.EnumProperty(name="Saved", items=_saved_items)
    # procedural decoration and new-piece defaults
    d_seed: bpy.props.IntProperty(name="Seed", default=1, min=0)
    d_count: bpy.props.IntProperty(name="Count", default=10, min=1, max=60)
    d_gap: bpy.props.FloatProperty(name="Spacing", default=0.09, min=0.01, max=0.5,
                                   description="Minimum distance between pieces (Decorate and Paint strokes)")
    d_back: bpy.props.BoolProperty(name="Back", default=True)
    d_shoulders: bpy.props.BoolProperty(name="Shoulders", default=True)
    d_crown: bpy.props.BoolProperty(name="Crown", default=True)
    d_face: bpy.props.BoolProperty(name="Face", default=False)
    d_max_trunks: bpy.props.IntProperty(name="Max trunks", default=1, min=0, max=5)
    d_droop: bpy.props.FloatProperty(name="Leaf droop", default=35, min=0, max=90,
                                     description="0 = leaves stick straight out, 90 = lie flat down the body")
    d_tuck: bpy.props.FloatProperty(name="Leaf tuck", default=0.03, min=0, max=0.15,
                                    description="How deep leaf bases go into the skin")
    d_size: bpy.props.FloatVectorProperty(name="Leaf size", size=2, default=(0.8, 1.2), min=0.2, max=3)
    p_scale: bpy.props.FloatVectorProperty(name="Prop scale", size=2, default=(0.7, 1.0), min=0.05, max=5)
    p_upright: bpy.props.FloatProperty(name="Prop upright", default=0.6, min=0, max=1,
                                       description="New trunks/mushrooms/twigs: 1 = straight up, 0 = along the normal")


def _palette(s):
    return [(i.obj.name, i.weight) for i in s.palette if i.on and i.obj and i.weight > 0]


def _brush_opts(s):
    return dict(leaf_size=tuple(s.d_size), droop=s.d_droop, tuck=s.d_tuck,
                prop_scale=tuple(s.p_scale), upright=s.p_upright)


def _new_name(prop):
    k = 0
    while f"P{k:02d}_{prop.name}" in bpy.data.objects:
        k += 1
    return f"P{k:02d}_{prop.name}"


def fill_palette(s):
    """Every prop in the scene, weighted like the procedural defaults (beech leaves off)."""
    import decorate
    s.palette.clear()
    w = dict(decorate.default_palette())
    for o in sorted((o for o in bpy.data.objects if _is_prop(None, o)), key=lambda o: o.name):
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
    bl_description = "Add the props selected in the viewport to the list"

    def execute(self, context):
        s = context.scene.prop_studio
        have = {i.obj for i in s.palette}
        for o in context.selected_objects:
            if _is_prop(None, o) and o not in have:
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


class PS_OT_place(bpy.types.Operator):
    bl_idname = "prop_studio.place"
    bl_label = "Place"
    bl_description = "Put the prop highlighted in the list at the 3D cursor (Shift+right-click on the body)"

    def execute(self, context):
        import decorate
        s = context.scene.prop_studio
        it = s.palette[s.palette_i] if 0 <= s.palette_i < len(s.palette) else None
        if not (s.body and it and it.obj):
            self.report({"WARNING"}, "pick a body and highlight a prop in the list")
            return {"CANCELLED"}
        p, n = _surface(s.body, context.scene.cursor.location)
        o = decorate.place(it.obj, s.body, p, n, random.Random(), _brush_opts(s), _new_name(it.obj))
        _select(o)
        load_piece(s, o)
        return {"FINISHED"}


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
            f = 1.0 - 0.5 * t / max(tries - 1, 1)
            opts = _brush_opts(s)
            opts["leaf_size"] = tuple(x * f for x in opts["leaf_size"])
            opts["prop_scale"] = tuple(x * f for x in opts["prop_scale"])
            o = decorate.place(prop, s.body, p, n, self.rnd, opts, _new_name(prop))
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


class PS_OT_delete_piece(bpy.types.Operator):
    bl_idname = "prop_studio.delete_piece"
    bl_label = "Delete piece"

    def execute(self, context):
        s = context.scene.prop_studio
        o = bpy.data.objects.get(s.editing)
        if o:
            bpy.data.objects.remove(o, do_unlink=True)
        s.editing = ""
        return {"FINISHED"}


class PS_OT_save(bpy.types.Operator):
    bl_idname = "prop_studio.save"
    bl_label = "Save placement"
    bl_description = "Store the selected piece's settings under this name"

    def execute(self, context):
        s = context.scene.prop_studio
        o = bpy.data.objects.get(s.editing)
        if not o:
            self.report({"ERROR"}, "select a piece first")
            return {"CANCELLED"}
        kind, c = _params(o)
        c = {k: ([round(x, 4) for x in v] if isinstance(v, list) else round(v, 4) if isinstance(v, float) else v)
             for k, v in c.items()}
        c["kind"] = kind
        d = _load_all()
        d[s.save_name] = c
        _save_all(d)
        s.saved = s.save_name
        self.report({"INFO"}, f"saved {s.save_name} to {_path()}")
        return {"FINISHED"}


class PS_OT_load(bpy.types.Operator):
    bl_idname = "prop_studio.load"
    bl_label = "Load"
    bl_description = "Place the saved piece again (as a new piece)"

    def execute(self, context):
        s = context.scene.prop_studio
        c = _load_all().get(s.saved)
        if not (c and s.body and c["prop"] in bpy.data.objects):
            return {"CANCELLED"}
        prop = bpy.data.objects[c["prop"]]
        tmp = bpy.data.objects.new(_new_name(prop), None)
        tmp["leaf" if c.get("kind") == "leaf" else "studio"] = {k: v for k, v in c.items() if k != "kind"}
        load_piece(s, tmp)
        name = tmp.name
        bpy.data.objects.remove(tmp)
        o, p = build_piece(s, name)
        _select(o)
        load_piece(s, o)
        s.save_name = s.saved
        return {"FINISHED"}


class PS_OT_delete(bpy.types.Operator):
    bl_idname = "prop_studio.delete"
    bl_label = "Delete saved"

    def execute(self, context):
        d = _load_all()
        d.pop(context.scene.prop_studio.saved, None)
        _save_all(d)
        return {"FINISHED"}


class PS_OT_decorate(bpy.types.Operator):
    bl_idname = "prop_studio.decorate"
    bl_label = "Decorate"
    bl_description = "Fill the zones with props from the list (replaces only earlier Decorate pieces)"

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


class PS_OT_reroll(bpy.types.Operator):
    bl_idname = "prop_studio.reroll"
    bl_label = "New seed"

    def execute(self, context):
        context.scene.prop_studio.d_seed += 1
        return bpy.ops.prop_studio.decorate()


class PS_OT_clear_decor(bpy.types.Operator):
    bl_idname = "prop_studio.clear_decor"
    bl_label = "Clear all pieces"

    def execute(self, context):
        c = bpy.data.collections.get("Decor")
        if c:
            for o in list(c.objects):
                bpy.data.objects.remove(o, do_unlink=True)
        context.scene.prop_studio.editing = ""
        return {"FINISHED"}


class PS_PT_panel(bpy.types.Panel):
    bl_label = "Prop Studio"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Greenlings"

    def draw(self, context):
        s = context.scene.prop_studio
        col = self.layout.column()
        col.prop(s, "body")
        box = col.box()
        row = box.row()
        row.label(text="Props")
        row.operator("prop_studio.palette_add", text="", icon="ADD")
        row.operator("prop_studio.palette_remove", text="", icon="REMOVE")
        row.operator("prop_studio.palette_fill", text="", icon="FILE_REFRESH")
        box.template_list("PS_UL_palette", "", s, "palette", s, "palette_i", rows=6)
        row = box.row(align=True)
        row.operator("prop_studio.place", icon="PIVOT_CURSOR")
        row.operator("prop_studio.paint", icon="BRUSH_DATA")
        box.prop(s, "p_overlap")

        o = bpy.data.objects.get(s.editing)
        box = col.box()
        if not o:
            box.label(text="Click a piece to edit it", icon="RESTRICT_SELECT_OFF")
        else:
            row = box.row()
            row.label(text=o.name, icon="OBJECT_DATA")
            row.operator("prop_studio.delete_piece", text="", icon="TRASH")
            box.label(text="Move: Shift+right-click, or G")
            box.prop(s, "prop")
            keys = (("l_droop", "l_roll", "l_size", "l_tuck") if o.get("leaf") is not None else
                    ("spin", "tilt", "scale", "upright", "lean", "sink", "roots", "offset", "bone"))
            for k in keys:
                box.prop(s, k)
            row = box.row()
            row.prop(s, "save_name")
            row.operator("prop_studio.save", text="", icon="FILE_TICK")
        row = col.row()
        row.prop(s, "saved", text="")
        row.operator("prop_studio.load", text="", icon="IMPORT")
        row.operator("prop_studio.delete", text="", icon="TRASH")


class PS_PT_decorate(bpy.types.Panel):
    bl_label = "Decorate"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Greenlings"
    bl_parent_id = "PS_PT_panel"

    def draw(self, context):
        s = context.scene.prop_studio
        col = self.layout.column()
        row = col.row()
        row.operator("prop_studio.decorate", icon="OUTLINER_OB_POINTCLOUD")
        row.operator("prop_studio.reroll", text="", icon="FILE_REFRESH")
        row.operator("prop_studio.clear_decor", text="", icon="TRASH")
        col.prop(s, "d_seed")
        col.prop(s, "d_count")
        col.prop(s, "d_gap")
        row = col.row(align=True)
        for z in ("back", "shoulders", "crown", "face"):
            row.prop(s, "d_" + z, toggle=True)
        col.prop(s, "d_max_trunks")


class PS_PT_defaults(bpy.types.Panel):
    bl_label = "New piece defaults"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Greenlings"
    bl_parent_id = "PS_PT_panel"
    bl_options = {"DEFAULT_CLOSED"}

    def draw(self, context):
        s = context.scene.prop_studio
        col = self.layout.column()
        for k in ("d_droop", "d_tuck", "d_size", "p_scale", "p_upright"):
            col.prop(s, k)


CLASSES = (PaletteItem, Settings, PS_OT_palette_fill, PS_OT_palette_add, PS_OT_palette_remove, PS_UL_palette,
           PS_OT_place, PS_OT_paint, PS_OT_delete_piece, PS_OT_save, PS_OT_load, PS_OT_delete,
           PS_OT_decorate, PS_OT_reroll, PS_OT_clear_decor, PS_PT_panel, PS_PT_decorate, PS_PT_defaults)


_last_cursor = None


def _transforming():
    return any(not m.bl_idname.startswith("PROP_STUDIO") for w in bpy.context.window_manager.windows
               for m in getattr(w, "modal_operators", ()))


def _tick():
    """Selection drives editing: the active piece loads into the sliders; moving the 3D cursor, or
    grabbing the piece (once the grab is confirmed), moves it and re-wraps it onto the skin."""
    global _last_cursor
    sc = bpy.context.scene
    s = getattr(sc, "prop_studio", None)
    if s is None or _transforming():
        return 0.2
    try:
        o = bpy.context.view_layer.objects.active
        cur = tuple(round(x, 5) for x in sc.cursor.location)
        if not _is_piece(o) or not s.body:
            if s.editing:
                s.editing = ""
        elif o.name != s.editing:
            load_piece(s, o)
        elif (o.matrix_world.translation - Vector(s.anchor)).length > 1e-4:
            s.anchor = o.matrix_world.translation          # grabbed with G: rebuild where it landed
        elif _last_cursor is not None and cur != _last_cursor:
            s.anchor = sc.cursor.location
        _last_cursor = cur
    except ReferenceError:
        pass
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
    bpy.app.timers.register(_tick, first_interval=0.2, persistent=True)


def unregister():
    for f in (globals().get("_tick"), globals().get("_follow_cursor")):
        if f and bpy.app.timers.is_registered(f):
            bpy.app.timers.unregister(f)
    for c in reversed(CLASSES):
        try:
            bpy.utils.unregister_class(c)
        except RuntimeError:
            pass
