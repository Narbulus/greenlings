# Blender source snapshot

`greenlings.blend` is a copy of `C:\Users\shugh\Documents\Untitled.blend`, saved on 2026-11-03. The original remains untouched.

SHA-256: `f15a4b02dbedb43e36459bca3abd3a774abb1b6d3bd829ec274717961cf3f246`.

Additional scenes and their existing paths are preserved in `.work/props.blend`, `.work/props.blend1`, and `.work/rigging.blend`. Blender 5.2 is installed on the source machine. External paths saved inside these scenes may need remapping after cloning; this snapshot does not establish that every scene is self-contained.

Verified that the Documents snapshot opens in Blender 5.2.3 LTS and has no linked Blender libraries. Most unpacked images map to files included under `.work`. Two image datablocks refer to the absent `.work/tripo/brutes/brute1_full/tex256_final.png`, and one refers to absent `tools/props/leaf_atlas.png`; these paths were already missing on the source machine. Existing textures were preserved without guessing replacements. The saved file also retains paths relative to its original Documents location, so use Blender's Find Missing Files on the clone's `.work` directory to reconnect included images.
