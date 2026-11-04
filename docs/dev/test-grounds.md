# Fixed test grounds and text evidence

The dev-only harness can build a repeatable **48 × 48 metre raised platform** in the configured
dev world. It leaves terrain heights intact and clears trees, logs and tree-type vegetation from
an 80 × 80 metre footprint, including a border for overhanging canopies. Its origin is chosen above the terrain near the player
on first build, then saved per world UID in `greenlings.devtools.cfg`. Later builds use the same
origin and world axes, regardless of the player's position or facing.

```powershell
./scripts/dev.ps1 run
./scripts/dev.ps1 test tests/arena-layout.txt
./scripts/dev.ps1 test tests/arena-haul.txt
./scripts/dev.ps1 cmd "gldev_arena goto haul" "gldev_arena snapshot"
```

Use `./scripts/dev.ps1 run -Visible` to inspect or play in the dev game. Automated runs launch
with a hidden window by default; this does not make them headless.

Arena scenarios are explicitly selected, rather than included in the old default regression suite.
Run the older terrain-based scenarios off the raised platform; they still snap placements to terrain.
Each resets tagged arena fixtures and actors and clears vegetation in that footprint.
`reset` does not call `killall`, change terrain heights,
or remove unrelated construction. It does not clear untagged loot or reset the whole world.
The platform and fixtures persist through normal world saves; use a normal game save before quitting
if you want to keep them. The origin configuration alone does not save the world.

Use `gldev_arena clearview` to clear vegetation around an existing arena without resetting its actors
or inventories. Snapshots report the remaining loaded vegetation count in the clearing footprint.

`gldev_arena optimize` replaces older dense floors and perimeter walls with 36 floor slabs and 24
wall sections, keeping actors and inventories. New builds use this lighter deck automatically.
`gldev_arena perf` samples three seconds of frame times and reports mean/p95 milliseconds, CPU cores
used, memory, fixture counts and loaded characters (`[GLPERF]`, `arena-perf.txt`). Wait for its log
line before changing the scene when comparing measurements.

For a populated model showcase, run `./scripts/dev.ps1 test playtest/arena-showcase.txt` against an
existing arena. It adds ten named actors (the five current greydwarf bodies, three brute bodies,
the greyling and a vanilla-body shaman) and stocks the scenario chests. Existing actors stay.
Reset before rerunning it: duplicate actor names deliberately fail. These actors run their normal
AI and may wander; their names identify the model. `gldev_actor` accepts an optional pooled body name
after X/Z, selecting that body on one actor while leaving the species' random pool available.

`playtest/arena-supplies.txt` stocks the empty reinforced social chest (`@social/sink`) with coins,
amber/pearls, thistle, six foods, and nest materials plus a hammer. It verifies every requested count.
Like the showcase, run it once after a reset; it adds items rather than topping up existing stacks.

`tests/arena-head-decor.txt` checks the populated showcase for zero runtime head plants while the
Young body's authored decorations remain present. Snapshots and assertions expose `runtimePlants`
and `assetDecor` counts for this distinction.

## Layout

Coordinates below are X/Z relative to the platform origin; north is +Z. All snapshots use
relative X,Y,Z in metres. A perimeter wall keeps actors on the platform.

| Bay | Centre X,Z | Fixtures |
|---|---|---|
| haul | -14,-14 | `haul/source`, `haul/sink` |
| machines | 14,-14 | `machines/source`, `machines/sink`, `machines/smelter`, `machines/kiln` |
| doors | -14,14 | `doors/source`, `doors/sink`, a barrier with door `doors/barrier/0` |
| social | 14,14 | `social/source`, `social/sink`, open space for groups |

Source chests are at centre + (-4,-3); sinks at (+4,-3), except the doors sink at (+4,+4).
The door barrier has open ends, allowing navigation around it as well as through the door.
These are work areas, **not isolated simulations**: nearby actors can interact and jobs can
generalize across bays. Run one behavior scenario at a time for regression checks; deliberately
spawn multiple actors for interaction tests. Night, weather, traits, needs, and wandering still
affect behavior. A fixed layout makes outcomes easier to measure; it is not a deterministic engine.

## Exact targets

Use `@fixture` in place of a prefab for chest and station actions:

```text
gldev_fill @haul/source Wood 8
gldev_open @haul/source
gldev_goto @machines/smelter
gldev_interact @doors/barrier/0
gldev_actor hauler Greyling haul 0 0
gldev_select @hauler
gldev_give Coins 1
gldev_useitem Coins
```

`gldev_actor <name> <prefab> <bay> [x] [z]` spawns and selects a fully bonded actor with fixed
neutral traits and a freshly fed timestamp. Supported prefabs are `Greyling`, `Greydwarf`,
`Greydwarf_Elite`, and `Greydwarf_Shaman`. This is a behavior fixture, not a taming test.
`gldev_select` makes an existing actor the target for commands such as pat, bond, and useitem.
Fixtures carry names in ZDOs, so selectors work after reload. A missing or duplicate selector fails
instead of silently choosing another object. The area must be loaded: use `gldev_arena goto` first.

## Evidence ladder

1. Assert actual state and inventories; use event logs to establish which interactions happened.
2. Snapshot the scene and trace movement if an outcome fails.
3. Inspect visual assets in Blender or take a game screenshot when the question is visual.

`gldev_arena snapshot` emits `[GLARENA]` rows and writes `BepInEx/devtools/arena-latest.txt`.
Rows include fixture name, prefab and position, grouped inventory counts, actor health/trust/state,
current activity and carried item, machine queue/fuel, and door state. Actor navigation reports the
AI's last requested destination, cached path result, path age, and up to 16 remaining path corners.
These are the actual cached AI path, not a new pathfinding query. An old path may describe a previous
activity; check its age and the actor's current activity. An empty path alone does not mean stuck.

`gldev_arena trace 30 2` samples for 30 real seconds at two-second intervals, logging only changed
rows after the initial snapshot. It also writes `BepInEx/devtools/arena-trace.txt`. One trace can run
at a time; duration is capped at 300 seconds and the minimum interval is 0.2 seconds. Traces are
asynchronous: wait for `GLARENA\] trace done` if the next operation must not overlap.

The existing `gl_dump` remains useful for untagged world objects. Failures of `!waitfor` automatically
capture it and an arena snapshot. Failed assertions capture an arena snapshot. Test summaries are
written as JSON to `.work/test-results.json`.

## Assertions

```text
!assert @haul/source count:Wood == 8
!assert @hauler state == Working
!assert @hauler carry == 0
!assert @hauler hp > 0
!assert @hauler bond == 100
!assert @hauler distance:@haul/sink < 5
!eventually 90 @haul/sink count:Wood == 8
```

`!assert` checks once. `!eventually <seconds>` polls every 0.2 real seconds until it passes or times
out (maximum 300 seconds). Both fail the batch and skip remaining commands. Comparisons are `==`,
`!=`, `<`, `<=`, `>`, `>=`; fields are `count:<item prefab>`, `state`, `carry` (count), `hp`, `bond`,
`runtimePlants`, `assetDecor`, and `distance:@fixture` (3D metres). Inventory assertions inspect actual container contents rather
than inferring success from log messages.

Screenshots are **off by default**. Existing `!screenshot` lines capture `gl_dump` instead.
Enable them explicitly when needed:

```text
gldev_cfg greenlings.devtools Evidence Screenshots true
!screenshot visual-check
gldev_cfg greenlings.devtools Evidence Screenshots false
```

## Headless direction

Snapshots, traces and assertions do not depend on rendered frames. The current command runner and
setup still require a local player, a loaded scene, and `Console.instance`. A dedicated server or
`-nographics` launch is not validated or supported by this change. That needs a separate bootstrap,
command transport and area-loading strategy; simply adding launch flags would not establish that
the same scenarios exercise the same game code.
