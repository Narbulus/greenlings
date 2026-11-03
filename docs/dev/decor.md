# Decorating greenling bodies (Blender -> game)

## One-time setup in Blender
Open `.work/props.blend` (backup of the prop scene). In Blender's Python console:

    import sys; sys.path.insert(0, r"C:\Users\shugh\Projects\greenlings\tools\blender")
    import prop_studio; prop_studio.register(r"C:\Users\shugh\Projects\greenlings")

The panel is in the 3D viewport sidebar (N) > **Greenlings** > **Prop Studio**.

## Decorate a body
1. **Body**: pick one (e.g. `GL_GD_beak_Cube`). Each body keeps its own pieces (collection "Decor <body>").
2. **Props list**: tick the props to use; the number is how often they're picked.
3. Put pieces on:
   - **Place**: Shift+right-click the body (3D cursor), highlight a prop in the list, press Place.
   - **Paint**: click/drag on the body. Ctrl+click erases, Ctrl+Z undoes the last dab, Esc stops.
   - **Decorate** (sub-panel): automatic fill of the ticked zones; New seed rerolls. It only replaces
     its own earlier pieces; hand-placed/painted ones stay.
4. **Edit**: click any piece. Its sliders change it live; Shift+right-click or G moves it (roots
   re-wrap). Use the sliders for rotation/scale (R/S are lost on the next rebuild). Trash deletes it.

## Send it to the game
1. Prop Studio > **Export to game (Greydwarf/<name>)**. Writes into the dev profile:
   `BepInEx/plugins/Greenlings/models/Greydwarf/<name>.glmesh` (+ `<name>.decor.png`).
2. Start the dev game: `./scripts/dev.ps1 run` (PowerShell, repo root).
3. In game console (F5): `devcommands`, then `gl_model reload`.
   To see one body on every greydwarf: `gl_model Greydwarf/<name>`; back to random: `gl_model pool`.
   Daytime: `tod 0.5`. Spawn + tame: `spawn Greydwarf 1`, `gldev_tame`.

## New props (cheap)
- References: edit an existing board with `tools/images/openrouter_image.py` (see tools/tripo/WORKFLOW.md).
- Several props in one Tripo run: slice each board (`tools/tripo/board.py <png> <dir> <name> --grid`),
  stack 4 per column (`tools/tripo/stack.py <name> <prop...>` in that dir), then
  `tools/tripo/tripo.py multiview ...` and `retopo <task> <dir> face_limit=2000` (~65 credits for 4).
- Import + split in Blender by height band, scale, add to the "Props" collection; textures shrunk
  with `tools/tripo/prop_textures.py`. Mushrooms: set the object's custom property `no_roots = 1`.
- Press **Reset list** in Prop Studio to pick them up.

## Known issues (2026-11-03)
- **Ragdoll**: the death ragdoll's skeleton has different bones, so it logs "bone mismatch" and keeps
  the vanilla body (decor attaches only if the bone exists). Fix needed in `GreenlingModel.ForBones`
  (map missing bones to the nearest existing parent instead of giving up).
- Leaves: found in game (Bush01_blueberry / shrub materials); check how they look up close.
- Only Greydwarf bodies export (brutes not set up: `prop_studio.PREFABS`).
- Generated trunks `trunk_squat` / `trunk_fork` are rigid (their roots don't wrap).
