# Restoring the full project

Install Git LFS before cloning this private repository, then run `git lfs pull` in the clone. Large Blender scenes, generated meshes, working images, release ZIPs, and the runtime snapshot use LFS. Preserve the `.work` directory: it contains source scenes, generation attempts, references, textures, and model exports used by the workflows.

- `assets/blender/greenlings.blend`: the Documents Blender scene copied on 2026-11-03. See its README for provenance.
- `.work/props.blend`, `.work/rigging.blend`: existing working scenes, with the prop backup retained too.
- `assets/runtime/Greenlings`: snapshot of the installed mod plugin and its model/texture directory. Copy the `Greenlings` folder into the destination profile's `BepInEx/plugins` to restore this snapshot, or rebuild the plugin from source.
- `dist`: existing packaged releases.
- `tools/tripo/WORKFLOW.md`, `docs/dev/decor.md`, `tools/asset_studio/README.md`: asset generation and editing workflows.
- `README.md`, `TESTING.md`, `scripts/dev.ps1`, `tests`, `playtest`: build and game test workflows.

Install Valheim, Steam, BepInEx/r2modman, .NET SDK 8+, Python and Blender separately. Local game DLLs, decompiled `.ref` files, virtual environments, build caches and credentials are excluded. Configure game/profile paths for the destination machine; `scripts/dev.ps1` still contains source-machine defaults. Blender scenes may also contain absolute external texture paths that need remapping to the included `.work` files.

Supply OpenRouter/Tripo credentials separately using the existing environment-variable or ignored `secrets.txt` mechanisms. Never commit credentials, even to a private repository.

The wiki workflow retains its build, but Pages deployment requires repository variable `ENABLE_PAGES_DEPLOY=true`. Check the desired website visibility before enabling it.

This is a complete project asset snapshot, not yet a portable remote runner. It does not include the Valheim installation or personal game saves.
