# Generated creature workflow: what works and what broke

The pipeline that works (greydwarf variants, 2026-11). Read this before generating any model.
Briefing rules are in BRIEFS.md; this file is the whole process and its lessons.

## 1. Reference image (OpenRouter, `tools/images/openrouter_image.py`)
- Start from the vanilla creature itself: `tools/blender/vanilla_boards.py` renders the bind pose
  (arms out), light background, game texture, brightened. Use the body-only render, laid out 2x2
  (front | side facing right / side facing left | back) so it fits the 3:2 image output.
- Model: `openai/gpt-5.4-image-2`. Gemini makes bolder edits but breaks the views.
- Word every request as an EDIT: "EDIT this image, keep everything, change only...". "Redraw"
  makes a new creature. Change one or two things per pass and chain the passes (each edits the
  previous result). Asking for "subtle" changes gets nothing; ask for explicit amounts.
- Attaching a second image (e.g. vanilla as a style reference) makes GPT blend the two. It works
  only for a texture/style pass worded "use the second image ONLY for texture"; check that the
  shapes held afterwards.
- Keep the vanilla structure: head and face jut FORWARD past the shoulders (not above them), same
  limb lengths, shoulder height and stance. Variation goes in the head/face shape, beak, trunk on
  the head, belly. The user wants personality in faces, not colour/material variants.
- The trunk on the head belongs to the body. Branches/twigs on the shoulders do not (props).
- Valheim look: simple low-res hand-painted bark/moss, no pixel noise; eyes are small flat glowing
  DIAMONDS (no iris/highlight); no mouth; plank-like chin, not a monkey face.
- Show the user every reference before spending Tripo credits.

## 2. Tripo (`tools/tripo/tripo.py`, ~65 credits per body)
- Slice: `tools/tripo/board.py <board> <dir> <name> --grid`. The grid's top-right figure (facing
  image right) is the creature's LEFT side; Tripo's left/right are the creature's own.
- `multiview` (full mode, 30) then `retopo <task_id> <dir> face_limit=1500` (v3.0, 35): Tripo's own
  retopology rebuilds clean topology, unwraps and bakes the texture. Never decimate in Blender:
  that gave jagged rims, blocky 256px bakes and seam-black outlines.
- Low-poly mode is only worth it for leafy shapes; full mode + retopo is better otherwise.

## 3. Rig (`greenling_variants.rig_auto`)
- Scale by SHOULDER height (arms at 1.21 above the feet), not total height: a tall trunk shrank
  one body 17% and hunched its shoulders.
- Align depth on the T-pose ARMS (vanilla arm bones at y=-0.07), not on the feet: vanilla feet sit
  ~10cm in front of the arms; feet-centring put bodies 10cm back and the fit then shoved the
  shoulders forward and wedged the head down.
- Weld only exactly coincident seam vertices (1e-6/1e-7); a looser weld merges touching surfaces.
- Weight fixes, in this order, all automatic: whole head unit (face, chin plank, trunk) on the head
  bone with a soft neck blend, applied BEFORE the pose-back to the vanilla bind pose (else a wide
  face bakes in shoulder distortion); head region kept to the face protrusion + trunk (a larger
  region put the shoulders on the head and made arm webs); arm keeps only its own skin; limbs keep
  to their own side; shoulders smoothed.
- Eyes: sample the texture across face triangles (low-poly faces have few vertices), brightest
  glow per side, then ray-cast onto the surface. The mod tints the eye quads pale yellow.
- Checks before showing anything: `remove_stretch(..., dry=True)` (good bodies: <20 faces),
  `bone_colour_render` in head-turn / arms-down / reach poses, textured renders, then in game.
- Hand tuning: `setup_paint(name)` (test-pose animation, Weight Paint mode), `export_hand(...)`.

## 4. Game
- Bodies live in `plugins/Greenlings/models/<Prefab>/*.glmesh` (+ .png); each greenling picks one
  by a seed in its ZDO. `gl_model reload`, `gl_model <Prefab>/<name>` to force, `gl_model pool`.
- `tools/tripo/skin.py` pads the UV islands (mipmaps bled black seams) and grades toward the
  vanilla texture of that creature.
- Greyling, Greydwarf and Brute share one mesh/skeleton (brute = x1.875, `export_for`); the
  shaman has its own body. Vanilla `Cube.001` is the stump on the head (weighted to the spine);
  the brute's club and flower are separate rigid meshes: props are the vanilla way.
- Don't drive the dev game while the user is playing in it (check before cam/screenshot batches).

## 5. Props (`greenling_props.py`, `prop_studio.py`)
- Trunk props: generate from the vanilla stump's own views with roots spread FLAT (3-4 roots max),
  full mode + retopo face_limit=500. `greenling_props.wrap` stands a prop on the body and walks each
  root along the skin so it bends with the body's curve; the trunk stays rigid; one bone each.
- Hand placement: `prop_studio.register(repo)` adds View3D > sidebar > Greenlings > Prop Studio.
  Placements save to tools/props/placements.json in vanilla bind-pose coordinates (fit every variant).
- Root wrapping is done with a rig (`greenling_props.rig_prop`): a trunk bone plus a 7-bone chain (shorter toward the tip)
  along each root's underside, automatic weights; `wrap` poses each chain onto the skin and bakes it.
  Per-vertex projection (tried first) crushed roots and tore the root/trunk junction.
- Prop references: edit the vanilla stump's own views, then a "Valheim pass" with a vanilla render
  attached as STYLE-ONLY reference, then "draw it as a game model of about 60 triangles" (this is
  what finally killed the clean GPT look), plus "slightly wonky/lopsided, irregular facets". Branches
  smooth and tapered, not zigzag. No leaves on generated models: leaves are cards added in Blender.
- Leaves: use the GAME'S foliage textures (gldev_photo Bush01 / shrub_2 / Beech_small1 ->
  .work/model/vanilla_leaves; not committed). Valheim leaf cards are 64px sprays/clusters of many small
  leaves, point-filtered, alpha cut, on pointed (non-square) cards: `make_vanilla_leaves`. A generated
  "single big leaf" texture looked nothing like Valheim. In game, borrow the vanilla material at runtime.
- Prop set (Blender scene "Props" collection): trunk_flat2, trunk_lean2, mush_a, twig_a, twig_leafy,
  leaf_{bush,spray,beech}_{1,tuft}. Everything above a prop's roots rides its trunk bone (loose leaf
  cards would otherwise get root weights and fly off).
- Texel density: the vanilla greydwarf body is ~62 texels per unit (128px texture, point-filtered):
  that IS the Valheim pixel look. Match it: body skins ship at 128px (bake big, then downscale the
  final skin; 1024px was ~8x too fine), leaf cards sample a texture window sized for 62 px/unit
  (`greenling_props.TEXEL_DENSITY`). Check new assets with the density measure before shipping.
