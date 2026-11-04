# Dev loop and harness

For fixed layouts, exact fixture selectors, state assertions and movement traces, see
[Fixed test grounds and text evidence](test-grounds.md).

## Commands

```powershell
./scripts/dev.ps1 run                                   # build + deploy, restart the game, wait until in the GLDev world
./scripts/dev.ps1 test                                  # all scenario tests, summary + non-zero exit on failure
./scripts/dev.ps1 test tests/teach-haul.txt             # just one
./scripts/dev.ps1 cmd "gldev_setup" "spawn Greyling 1" "gl_dump"     # ad-hoc commands against the live game
./scripts/dev.ps1 stop
./scripts/package.ps1                                   # Release build -> dist/Greenlings-<version>.zip
```

The game runs from the r2modman profile **GreenlingsDev**. DevTools creates a local character and world, both
named `GLDev`, on first launch, and never touches other saves. To play the dev build with the normal menus, set
`AutoStart.Enabled = false` in `BepInEx/config/greenlings.devtools.cfg`.

## Observability

Tests and bug reports rely on logs, not screenshots.

- **`[GLEVT]` lines**: every greenling decision as a one-line event, for example
  `[GLEVT] deposit gl=Grug(…) item=Wood to=Container:piece_chest_wood`. Event kinds include `state`, `lesson`,
  `learned`, `step`, `take`, `deposit`, `wait`, `chore`, `habit`, `mischief`, `scared`, `calm`, `follow`, `door`,
  `repair`, `sleep`, `ate`, `bond`, `bonded` and `died`.
- **`gl_dump [radius]`** prints one JSON line per greenling, container and station nearby (`[GLDUMP] {...}`).
- The log is `%APPDATA%\r2modmanPlus-local\Valheim\profiles\GreenlingsDev\BepInEx\LogOutput.log`.
  Screenshots go to `…\BepInEx\devtools\`.

## Scenario file directives

| Directive | Does |
|---|---|
| `!waitfor <regex> [timeout]` | Ordered expectation: matches a log line after the previous match. A failure skips the rest and fails the run. |
| `!wait <sec>` | Plain delay |
| `!assert @fixture field operator value` | Check actual state or inventory; fail the batch on mismatch |
| `!eventually <seconds> @fixture field operator value` | Poll a state assertion until it passes or times out |
| `!screenshot <name>` | Opt-in via `Evidence.Screenshots`; otherwise captures `gl_dump` |
| `!include <file>` | Inlines another file (expanded by `dev.ps1`) |
| `!pos`, `!quit` | Log the player position / quit the game |

## DevTools console commands

These play the player's part through real game code paths. A command that names a prefab uses the most recently
placed instance, or else the nearest one.

| Command | Does |
|---|---|
| `gldev_setup` / `gldev_clear` | God + ghost mode, empty inventory, all greenlings config reset to defaults / remove everything nearby |
| `gldev_place <prefab> <right> <fwd>` | Place a piece or creature relative to the player |
| `gldev_fill <chest> <item> <n>`, `gldev_open`, `gldev_close`, `gldev_take`, `gldev_put <item> <n>` | Chest handling |
| `gldev_addore`, `gldev_addfuel`, `gldev_use <prefab> <item>`, `gldev_interact <prefab>` | Using stations |
| `gldev_give`, `gldev_useitem`, `gldev_pickup`, `gldev_dropitem` | Inventory |
| `gldev_pat`, `gldev_tame`, `gldev_tamelast`, `gldev_bond <n>`, `gldev_rename <name>` | Greenlings |
| `gldev_move <right> <fwd>`, `gldev_act swing\|jump\|emote <x>`, `gldev_cam <prefab> <dist>` | Player and camera |
| `gldev_damage`, `gldev_kill`, `gldev_ready <producer>` | World state |
| `gldev_cfg <guid> <section> <key> <value>` | Change a config entry live |
| `gldev_run <file>` | Run a playtest script (`playtest/*.txt`) |
| `gldev_prefabs <substr>`, `gldev_inspect <prefab>` | Exploring the game |

Mod commands (shipped): `gl_dump [radius]` and `gl_prank [kind]`.
