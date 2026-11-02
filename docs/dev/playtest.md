# Playtesting

## Start

1. In r2modman, select the **GreenlingsDev** profile and press **Start modded**. The game skips the menus
   and puts you in the `GLDev` world.
2. Press ++f5++ for the console and run `gldev_run testbed`. This wipes everything built within 40 m and builds a
   fresh test base around you. Run it again any time to reset. `gldev_run testbed_help` prints tips in game.

## What the testbed gives you

| Where | What |
|---|---|
| Inventory | Hammer, sword, coins, wood, ore, coal, raw meat, mead base, food |
| Behind you | Workbench |
| Front-left / front-right | **Supply chest** (stocked) / **Storage chest** (empty) |
| Ahead | Smelter, charcoal kiln, cooking spit over a lit fire, fermenter |
| Right | Hearth, beehive full of honey, torches |
| Far left | Raspberry bushes, and berries lying on the ground |
| Ahead, far | Loose wood to tidy up, resin |
| In front of you | Three trusting greenlings, a greydwarf and a brute |
| Front right | A fresh **stray** to bond with |
| Far left-back | Two **Greenling Nests** |
| Nearby | A damaged wall for a hammer-holding greenling to repair |

`gldev_run testbed_wild` adds wild greylings 25 m ahead for taming from scratch.

## Handy commands

| Command | Does |
|---|---|
| `gl_dump` | Writes the state of every greenling and station nearby to the log |
| `gl_prank <kind>` | Makes the nearest idle greenling do something: `zoomies`, `scatter`, `stare`, `gift`, `tag`, `huddle`, `herd`, `birds` |
| `gldev_tame` / `gldev_bond 100` | Tame the nearest one instantly / make it fully trusting |
| `gldev_cfg greenlings <Section> <Key> <value>` | Tune live, e.g. `gldev_cfg greenlings Needs MealInterval 10` |
| `tod 0` / `tod 0.5` | Night (bedtime) / noon |

The dev profile sets `TamingTime = 20` so taming is quick. The real default is 600.

## Reporting a problem

Run `gl_dump` right after it happens, then say roughly what you did. Every greenling decision is a
`[GLEVT]` line in `LogOutput.log`.
