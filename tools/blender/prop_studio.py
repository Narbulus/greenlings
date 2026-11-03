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
                lean=(s.lean[0], s.lean[1], 0.0), roots=s.roots)
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
    scale: bpy.props.FloatProperty(name="Scale", default=1, min=0.2, max=3, update=_upd)
    upright: bpy.props.FloatProperty(name="Upright", default=0.75, min=0, max=1, update=_upd,
                                     description="1 = straight up, 0 = along the surface normal")
    lean: bpy.props.FloatVectorProperty(name="Lean", size=2, default=(0, 0.25), min=-1, max=1, update=_upd)
    sink: bpy.props.FloatProperty(name="Sink", default=0.03, min=-0.1, max=0.2, update=_upd,
                                  description="How far the base sits into the body")
    offset: bpy.props.FloatProperty(name="Root offset", default=0.008, min=0, max=0.05, update=_upd,
                                    description="Gap between roots and skin")
    roots: bpy.props.BoolProperty(name="Wrap roots", default=True, update=_upd,
                                  description="Bend the prop's spread-out base onto the skin (off: rigid)")
    bone: bpy.props.EnumProperty(name="Bone", items=[(b, b, "") for b in ("spine3", "spine2", "spine1", "head",
                                 "l_arm1", "r_arm1", "l_shoulder", "r_shoulder")], update=_upd)
    save_name: bpy.props.StringProperty(name="Name", default="trunk_back")
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
                          "offset": round(s.offset, 4), "bone": s.bone, "roots": s.roots}
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
        for k in ("spin", "scale", "upright", "lean", "sink", "roots", "offset", "hint", "bone"):
            box.prop(s, k)
        row = col.row()
        row.prop(s, "save_name")
        row.operator("prop_studio.save", text="", icon="FILE_TICK")
        row = col.row()
        row.prop(s, "saved", text="")
        row.operator("prop_studio.load", text="", icon="IMPORT")
        row.operator("prop_studio.delete", text="", icon="TRASH")


CLASSES = (Settings, PS_OT_anchor, PS_OT_save, PS_OT_load, PS_OT_delete, PS_PT_panel)


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
