"""
Prop Studio: a sidebar panel (View3D > N panel > Greenlings) for placing props on a greenling body
by hand. Wraps the prop live with greenling_props.wrap and saves named placements to
tools/props/placements.json (positions in the vanilla bind pose, so they fit every variant).

    import prop_studio; prop_studio.register(r"<repo>")

Use: pick the body and the prop, Shift+right-click on the body to put the 3D cursor where the prop
should stand, press "Anchor here", then tweak the sliders (the preview updates live). Name it and
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


class Settings(bpy.types.PropertyGroup):
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
    # procedural decoration
    d_seed: bpy.props.IntProperty(name="Seed", default=1, min=0)
    d_count: bpy.props.IntProperty(name="Count", default=10, min=1, max=60)
    d_gap: bpy.props.FloatProperty(name="Spacing", default=0.09, min=0.01, max=0.5)
    d_back: bpy.props.BoolProperty(name="Back", default=True)
    d_shoulders: bpy.props.BoolProperty(name="Shoulders", default=True)
    d_crown: bpy.props.BoolProperty(name="Crown", default=True)
    d_face: bpy.props.BoolProperty(name="Face", default=False)
    w_leaf: bpy.props.IntProperty(name="Leaf", default=6, min=0, max=20)
    w_tuft: bpy.props.IntProperty(name="Tuft", default=3, min=0, max=20)
    w_trunk: bpy.props.IntProperty(name="Trunk", default=1, min=0, max=20)
    w_mush: bpy.props.IntProperty(name="Mushroom", default=1, min=0, max=20)
    w_twig: bpy.props.IntProperty(name="Twig", default=1, min=0, max=20)
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
        c = o.get("studio") if o else None
        if not c:
            self.report({"ERROR"}, "select a wrapped decoration piece (leaves: move/rotate them directly)")
            return {"CANCELLED"}
        c = c.to_dict()
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
        opts = dict(seed=s.d_seed, count=s.d_count, gap=s.d_gap, zones=zones, w_leaf=s.w_leaf, w_tuft=s.w_tuft,
                    w_trunk=s.w_trunk, w_mush=s.w_mush, w_twig=s.w_twig, leaf_size=tuple(s.d_size),
                    droop=s.d_droop, tuck=s.d_tuck, max_trunks=s.d_max_trunks)
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
        row = col.row()
        row.operator("prop_studio.edit_piece", icon="GREASEPENCIL")
        if s.editing:
            row.operator("prop_studio.apply_piece", icon="CHECKMARK")
            col.label(text="Editing " + s.editing + " (sliders above)")
        col.prop(s, "d_seed")
        col.prop(s, "d_count")
        col.prop(s, "d_gap")
        row = col.row(align=True)
        for z in ("back", "shoulders", "crown", "face"):
            row.prop(s, "d_" + z, toggle=True)
        box = col.box()
        box.label(text="Weights")
        for k in ("w_leaf", "w_tuft", "w_trunk", "w_mush", "w_twig", "d_max_trunks"):
            box.prop(s, k)
        box = col.box()
        box.label(text="Leaves")
        for k in ("d_droop", "d_tuck", "d_size"):
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


CLASSES = (Settings, PS_OT_anchor, PS_OT_save, PS_OT_load, PS_OT_delete, PS_OT_edit_piece, PS_OT_apply_piece,
           PS_OT_decorate, PS_OT_reroll,
           PS_OT_clear_decor, PS_PT_panel, PS_PT_decorate)


def register(repo):
    global _repo
    _repo = repo
    unregister()
    for c in CLASSES:
        bpy.utils.register_class(c)
    bpy.types.Scene.prop_studio = bpy.props.PointerProperty(type=Settings)


def unregister():
    for c in reversed(CLASSES):
        try:
            bpy.utils.unregister_class(c)
        except RuntimeError:
            pass
