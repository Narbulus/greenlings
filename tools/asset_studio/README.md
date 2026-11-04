# Asset Studio

From the repository root:

```powershell
python tools/asset_studio/server.py
```

Open http://127.0.0.1:8765 in your browser. Requires Python 3.11+, `requests`, and Pillow (the same dependencies as the generation scripts). Existing OpenRouter and Tripo environment variables or local secrets file are used; keys never enter the browser.

Describe a change, generate 1–4 reference options, compare them, and approve one for a paid Tripo build. Technical settings are collapsed by default. Experiments and copied source boards live in `.work/asset-studio`; each generated candidate retains its hash, prompt, model, usage, and parent source. Known 3D tasks can resume after interruption. Ambiguous submissions are not retried automatically.

This first increment connects reference generation, review, multiview generation, retopology and mesh download. Blender rigging, pose QA, saved recipes, enforced spending caps and game promotion remain to be implemented. Image cost is recorded when returned by the provider; 65 Tripo credits is a repository estimate, not live pricing. Unknown charges are displayed as unknown. No paid calls are made until an explicit UI action.
