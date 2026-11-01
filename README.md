# Greenlings (revival)

Valheim (1.0.20+, BepInEx 5) mod for taming greylings and putting them to work.
Spiritual successor to [RagnarsRokare/SlaveGreylings](https://github.com/di98feja/RagnarsRokare/tree/master/SlaveGreylings).

## Layout

| Path | What |
|---|---|
| `src/Greenlings` | The mod. |
| `src/DevTools` | Dev-only harness plugin (never shipped): auto-loads into a dev world, runs commands from a file, takes screenshots. |
| `scripts/dev.ps1` | The dev loop. |
| `legacy/` | Upstream MobAILib + SlaveGreylings source at commit `1160a11`, for reference only (not built). |
| `.ref/` | Decompiled `assembly_valheim` (git-ignored; regenerate with `ilspycmd`). |

## Requirements

- Valheim (Steam) and Steam running
- .NET SDK 8+ (builds `net472`)
- r2modman profile **GreenlingsDev** (BepInExPack only). Paths are in `Directory.Build.props`;
  override per machine in `Directory.Build.user.props`.

See [TESTING.md](TESTING.md) for hands-on playtesting (testbed, commands, what to try).

## Dev loop

```powershell
./scripts/dev.ps1 run                                   # build + deploy, restart the game, wait until in the GLDev world
./scripts/dev.ps1 test                                  # all scenario tests (tests/*.txt), summary + non-zero exit on failure
./scripts/dev.ps1 test tests/teach-haul.txt             # just one
./scripts/dev.ps1 cmd "gldev_setup" "spawn Greyling 1" "gl_dump"     # ad-hoc commands against the live game
./scripts/dev.ps1 stop
./scripts/package.ps1                                   # Release build -> dist/Greenlings-<version>.zip (r2modman: Import local mod)
```

Persistence across a restart is a two-part scenario:
`dev.ps1 test tests/persist-1.txt; dev.ps1 run; dev.ps1 test tests/persist-2.txt`.

### Observability

- **`[GLEVT]` log lines**: every greenling decision as a one-line event
  (`state`, `curiosity`, `pat`, `pat_refused`, `gift`, `demo_source`, `demo_sink`, `lesson`,
  `learned`, `take`, `deposit`, `wait`, `drop`, `lost_interest`, `task_broken`).
- **`gl_dump [radius]`**: one JSON line per greenling, greyling, container and smelter nearby (`[GLDUMP] {...}`).
- Console output is mirrored into the BepInEx log, and `dev.ps1` echoes all of the above.

### Scenario files (`tests/*.txt`)

Lines are console commands or DevTools directives:

| Directive | |
|---|---|
| `!waitfor <regex> [timeout]` | Ordered expectation: matches log lines after the previous match. A failure skips the rest and fails the run. |
| `!wait <sec>` | Plain delay. |
| `!screenshot <name>` | Saved to `<profile>/BepInEx/devtools/`. |
| `!include <file>` | Inline another file (expanded by dev.ps1); `_setup.txt`, `_tamed.txt`, `_watching.txt` are shared preambles. |
| `!pos`, `!quit` | |

DevTools commands that play the player's part through real game code paths:
`gldev_setup` (god+ghost on, empty inventory), `gldev_clear`, `gldev_place <prefab> <right> <fwd>`,
`gldev_fill <chest> <item> <n>`, `gldev_open <chest>` / `gldev_close`, `gldev_take` / `gldev_put <item> <n>`,
`gldev_addore <smelter> <item>`, `gldev_addfuel <smelter>`, `gldev_give <item> <n>`, `gldev_useitem <item>`,
`gldev_pat`, `gldev_tame`, `gldev_rename <name>`, `gldev_move <right> <fwd>`, `gldev_interact <prefab>`,
`gldev_use <prefab> <item>`, `gldev_pickup <item>`, `gldev_ready <producer>`, `gldev_cfg <guid> <section> <key> <value>`,
`gldev_prefabs <substr>`, `gldev_inspect <prefab>`. Mod debug commands: `gl_dump [radius]`, `gl_prank [kind]`.
Commands addressing a prefab use the most recently placed instance, else the nearest one.

DevTools creates a local character and world, both named `GLDev`, on first launch.
It does not touch your other characters or worlds.

To play the dev build yourself, launch the **GreenlingsDev** profile from r2modman.
Set `AutoStart.Enabled = false` in `BepInEx/config/greenlings.devtools.cfg` if you want the normal menus.
