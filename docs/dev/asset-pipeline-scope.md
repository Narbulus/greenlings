# Asset pipeline improvement scope

Scoping assessment, 2026-11-03. No paid generation was performed. This is a proposal, not an implemented runner.

## Recommendation

Keep the existing Python and Blender tools. Add a small, local experiment runner and visual comparison report around them before adopting a node editor. The valuable change is making each attempt traceable, resumable, comparable, and budgeted. A graph editor could become an optional frontend later.

The existing pipeline already encodes hard-earned knowledge about Valheim proportions, multiview layout, topology, rigging, texel density, and rigid decorations. Replacing those parts would create work without first addressing the gaps in experimentation.

## What exists

| Stage | Existing implementation | Scope implication |
|---|---|---|
| Vanilla source/reference | `tools/blender/vanilla_boards.py`; `assets/reference`; local `.work/refs` | Keep a canonical source board and hash it for each experiment. |
| Reference image edits | `tools/images/openrouter_image.py` supports prompt files, multiple image inputs, model ID, aspect ratio | Model comparison needs orchestration and recording, rather than a new image client. |
| Board preparation | `tools/tripo/board.py` crops views and emits a check image; `stack.py` batches props | Preserve explicit view conventions and record batch membership. |
| 3D generation and retopology | `tools/tripo/tripo.py`; multiview, retopo, task fetch, balance | Add durable task submission and recovery around these calls. |
| Rigging and validation | `greenling_variants.py`: `rig_auto`, `rig_body`, dry stretch checks, pose renders and hand tuning | Wrap the current path; avoid selecting the older `build` path just because it looks like an entry point. |
| Texture and decorations | `skin.py`, `prop_textures.py`, Prop Studio, `decorate.py`, `decor_export.py` | Separate body, prop, and decoration recipes; allow cheap downstream variation. |
| Game inspection | Model reload/selection commands and DevTools photo facilities | Keep final in-game review; do not manipulate a game the user is playing. |

Local artifacts show many prompt revisions, comparison sheets, model variants and Blender scenes. These are useful historical examples, but filenames alone do not establish complete provenance or historical spend.

## Specific gaps found

- The image CLI prints usage/cost but does not save a structured record of the prompt, model, inputs, response ID, timing, or usage alongside its images. Its default is Gemini, whereas the workflow recommends an OpenAI model. Explicit recipe settings would eliminate this silent difference.
- Tripo task IDs are printed immediately, but `task.json` is written during download, after task completion. An interruption can leave a paid task recoverable only from terminal logs. Record submission before polling.
- Tripo balance changes are printed rather than associated with an experiment. Account balance differences can include unrelated jobs and should not be treated as exact per-task charges.
- Downloads do not check HTTP status before saving. A failed download can look like an asset. Add checked downloads, temporary files and validation before marking a step complete.
- There is no shared run manifest, experiment matrix, durable review decision, cost cap, or downstream cache across the inspected tools.
- Guidance conflicts: `BRIEFS.md` initially recommends low-poly generation and decimation, then later recommends full generation plus retopology. It also excludes head trunks while `WORKFLOW.md` includes them in bodies. Select and version the intended recipe explicitly rather than guessing which paragraph wins.
- Rig checks exist, but `rig_body` exports after recording stretch counts; recording a metric does not enforce an acceptance gate. Document the threshold and make promotion conditional on it. The older `build` defaults also differ from the workflow's good-body guidance.

## Proposed workflow

`recipe → reference candidates → comparison/review → approved board → views → Tripo → retopo → rig/texture → QA → staged export → in-game review → promote`

Reference edit chains should remain first-class: each edit has a parent image and an exact prompt. A comparison can branch from the same vanilla board or the same approved intermediate, without losing which changes produced a candidate.

Use three recipe families:

1. **Bodies:** conservative geometry edits, fixed multiview layout, full generation plus retopology, vanilla rig, pose checks.
2. **Props:** rigid geometry, optional root wrapping, small texture targets, up to roughly four compatible props per generation batch, followed by split validation.
3. **Decoration:** reuse approved bodies/props and vary placement seed, density and weights locally. This is the cheapest place to produce variety without another image or 3D generation call.

Keep reference approval before spending Tripo credits, as required by the current workflow. Bind approval to the exact board hash: changing the board invalidates it. Once approved, the normal sequence should advance automatically within its budget until a failure or review gate.

## Minimal architecture

- **Versioned recipes and prompts in Git:** resolved model IDs and parameters, prompt templates, board order, face targets, texture settings, rig/QA policy. Secrets remain outside recipes and logs.
- **Immutable run folders:** `.work/asset-runs/<experiment>/<run-id>/`, with copied prompts, input/output hashes, parent IDs, settings, code revision and dirty state, timestamps, provider/request/task IDs, usage, costs, metrics, review notes and thumbnails. Record Blender version and template/skeleton hashes too.
- **Small Python runner:** explicit stage dependencies; JSON manifests/events initially. A database is optional once querying or concurrency warrants it. Keep it local and single-user first.
- **Existing tools as adapters:** start with OpenRouter, Tripo, and headless Blender. Add another image or 3D provider only when a real comparison needs it. Do not assume all image models accept the same parameters.
- **Generated local HTML comparison report:** contact sheets with model, prompt revision, cost, duration, pass/fail reasons and links to pose renders. A read-only report is enough initially; editing/review controls can follow.
- **Explicit staging and promotion:** QA produces candidates in run folders. Only deliberate promotion updates the dev model pool; preserve the prior promoted files for rollback.

Suggested operations are `plan`, `run`, `resume`, `compare`, `approve`, and `promote`. These are proposed interfaces, not commands available today.

### Resume and caching rules

Persist a submitted task ID before waiting; fetch it after a timeout instead of generating again. If a submission times out before an ID is received, mark it ambiguous and reconcile provider history where supported; do not blindly retry a chargeable request. Do not promise exactly-once submission without provider support.

Cache deterministic stages by input hashes, resolved settings and tool version. Reuse an existing generated artifact only by explicit selection; requesting another stochastic sample must create a new attempt even with identical inputs. Record seeds where supported, but do not promise identical images from hosted models.

## Cost management

Store Tripo credits and image-provider currency separately. The repo currently estimates 30 credits for multiview plus 35 for retopology: approximately 65 per body, or 16.25 per prop for a successful four-prop batch before rejects and rework. These are repository planning assumptions, not verified current prices; the client's credit-to-dollar comment should not become a universal conversion.

Before dispatch, show candidate count, paid stages, estimated credits/currency and maximum outstanding work. Reserve estimates for in-flight jobs so concurrency cannot oversubscribe a budget. Track provider-reported actuals separately; missing charges remain unknown rather than zero. An estimate cannot guarantee a hard dollar cap for variable-price requests without a reliable upper bound. Use provider limits where available, conservative reservations and bounded batches.

Optimize **cost and hands-on minutes per accepted asset**, alongside total elapsed time. A cheaper image model can cost more overall if it produces inconsistent views or difficult rigs. Include rejected candidates and failed attempts in the experiment total.

## First experiment

Start with a body experiment because its downstream failures are costly:

1. Freeze one vanilla board, one desired facial change, aspect ratio and review rubric.
2. Compare two currently available image models, two prompt formulations and two independent samples per combination: eight references. Check availability and pricing at execution time; use the documented baseline explicitly rather than the CLI default.
3. Review model identity blind where practical. Score view consistency, vanilla proportions, face personality, arm/torso clearance and material style. Reject hard failures before 3D spend.
4. Advance at most two approved candidates through the same Tripo/retopo/rig recipe. At repository estimates, that is 130 Tripo credits plus image costs. Advance fewer if nothing qualifies.
5. Compare stretch counts, shoulder alignment, eye placement, pose renders, texel density, manual repair time and in-game appearance. A small pilot guides the next experiment; it cannot establish statistical reliability.

Follow with a separate prop-batching trial and a decoration-only trial. Avoid changing the image model, prompt, topology settings and rig logic in the same comparison.

## Tool choice

| Option | Fit here | Recommendation |
|---|---|---|
| Small runner + comparison report | Reuses custom Blender/game integration and makes review/cost policy explicit | Start here. Avoid building a general workflow platform. |
| ComfyUI | Useful for visual image graphs and potentially local image processing/generation; workflows can be executed through its API/CLI | Pilot later if visually editing image graphs becomes the bottleneck. Custom adapters and cost/review tracking are still needed for this pipeline. |
| MLflow | Existing experiment/parameter/metric/artifact tracking UI | Consider if experiment search and multi-user tracking outgrow local manifests. Asset review and orchestration would still require integration. |

ComfyUI's documented workflow formats and execution support make it a viable optional image backend, not a prerequisite: [official CLI workflow documentation](https://github.com/Comfy-Org/comfy-cli/blob/main/docs/json-output.md), [API specification](https://github.com/Comfy-Org/ComfyUI/blob/master/openapi.yaml). MLflow documents experiment tracking and artifact storage: [tracking](https://www.mlflow.org/docs/latest/tracking/), [artifact stores](https://mlflow.org/docs/latest/self-hosting/architecture/artifact-store/).

## Delivery sequence and boundaries

Rough engineering estimates, assuming the existing tools work in the current local environment:

| Phase | Deliverable | Effort |
|---|---|---|
| 1 | Resolve recipe contradictions; structured image/Tripo records; durable task recovery; checked downloads; dry-run cost plan | 1–2 days |
| 2 | Reference experiment matrix, immutable branches, contact-sheet report, approval records and budget reservations | 2–3 days |
| 3 | Headless Blender wrapper, QA report, deterministic stage reuse, staged exports and rollback | 2–4 days |
| 4, optional | Interactive review UI, ComfyUI adapter or shared tracking | Re-scope after the pilot |

The first useful milestone is phases 1–2, roughly 3–5 engineering days, followed by the bounded pilot. Blender scene dependencies and integration failures could extend phase 3. No model training, broad provider migration, hosted service, custom graph editor or historical billing reconstruction is included.

Acceptance for the initial milestone: an eight-reference comparison is generated from one recipe; every output links to its exact inputs/settings/cost record; no unapproved board reaches Tripo; interrupted known tasks resume without a new submission; unknown charges are visible; and a chosen reference can become the next baseline without overwriting prior attempts. Verify billing/recovery behavior with mocked failure cases before exercising paid calls.

This assessment inspected source, workflow documentation and local artifact names. It did not execute generation, validate historical visual quality, launch Blender/game sessions, or measure existing acceptance rates. Begin capturing those measurements with the first pilot.
