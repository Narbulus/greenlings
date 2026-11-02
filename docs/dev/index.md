# Development

- [Architecture](architecture.md): how a greenling works inside. State lives in ZDOs, behaviour in an owner-side brain.
- [Dev loop and harness](harness.md): building, deploying, and driving the game from scripts.
- [Scenario tests](tests.md): the in-game test suite (generated from `tests/*.txt`).
- [Playtesting](playtest.md): the testbed and handy console commands.
- [Writing the wiki](wiki.md): how this site is built and deployed.

## Repository layout

| Path | What |
|---|---|
| `src/Greenlings` | The mod |
| `src/DevTools` | Dev-only harness plugin, never shipped. It auto-loads into a dev world, runs commands from a file, and takes screenshots. |
| `scripts/dev.ps1` | The dev loop: build, deploy, run, test |
| `scripts/package.ps1` | Release build to `dist/Greenlings-<version>.zip` |
| `scripts/gen-docs.py` | Regenerates the wiki's config and test pages from the code |
| `tests/` | Scenario tests |
| `playtest/` | Testbed scripts for hands-on play (`gldev_run testbed`) |
| `assets/` | Thunderstore icon and README, reference renders |
| `docs/`, `mkdocs.yml` | This wiki |
| `DESIGN.md` | Living design notes |

## Requirements

- Valheim (Steam), with Steam running
- .NET SDK 8+ (builds `net472`)
- An r2modman profile **GreenlingsDev** with only BepInExPack. Paths are in `Directory.Build.props`; override
  them per machine in `Directory.Build.user.props`.
