# SlaveGreylings (revival)

Valheim (1.0.20+, BepInEx 5) mod for taming greylings and putting them to work.
Successor to [RagnarsRokare/SlaveGreylings](https://github.com/di98feja/RagnarsRokare/tree/master/SlaveGreylings).

## Layout

| Path | What |
|---|---|
| `src/SlaveGreylings` | The mod. |
| `src/DevTools` | Dev-only harness plugin (never shipped): auto-loads into a dev world, runs commands from a file, takes screenshots. |
| `scripts/dev.ps1` | The dev loop. |
| `legacy/` | Upstream MobAILib + SlaveGreylings source at commit `1160a11`, for reference only (not built). |
| `.ref/` | Decompiled `assembly_valheim` (git-ignored; regenerate with `ilspycmd`). |

## Requirements

- Valheim (Steam) and Steam running
- .NET SDK 8+ (builds `net472`)
- r2modman profile **SlaveGreylingsDev** (BepInExPack only). Paths are in `Directory.Build.props`;
  override per machine in `Directory.Build.user.props`.

## Dev loop

```powershell
./scripts/dev.ps1 run      # build + deploy to the profile, restart the game, wait until standing in the SGDev world
./scripts/dev.ps1 cmd "spawn Greyling 3" "!wait 2" "sg_info" "!screenshot check"
./scripts/dev.ps1 stop
```

`cmd` lines are Valheim console commands (cheats on) or DevTools directives:
`!wait <sec>`, `!screenshot <name>` (saved to `<profile>/BepInEx/devtools/`), `!pos`, `!quit`.
Console output is mirrored into the BepInEx log and echoed back by the script.

DevTools creates a local character and world, both named `SGDev`, on first launch.
It does not touch your other characters or worlds.

To play the dev build yourself, launch the **SlaveGreylingsDev** profile from r2modman.
Set `AutoStart.Enabled = false` in `BepInEx/config/slavegreylings.devtools.cfg` if you want the normal menus.
