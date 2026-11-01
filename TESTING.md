# Playtesting Greenlings

## Start

1. In r2modman, select the **GreenlingsDev** profile and press **Start modded**.
   The game skips the menus and puts you in the `GLDev` world (a dev character and world that
   don't touch your real saves).
2. Press **F5** to open the console and run:

   ```
   gldev_run testbed
   ```

   This wipes everything built within 40 m and builds a fresh test base around you. Run it again any
   time to reset.

## What the testbed gives you

| Where (relative to where you stood) | What |
|---|---|
| your inventory | Hammer, Iron Sword, 100 Coins, Wood, Copper Ore, Coal, Raw Meat, Mead Base |
| behind you | Workbench (so the hammer works) |
| front-left | **Supply chest** (small): Wood, Copper Ore, Tin Ore, Coal, Raw Meat, Mead Base |
| front-right | **Storage chest** (big): empty |
| ahead, left to right | Smelter, Charcoal Kiln, cooking spit over a **lit** fire, Fermenter |
| right | Hearth (unlit, needs wood), Beehive (full of honey) |
| far left | 4 Raspberry bushes |
| ahead, far | a pile of loose Wood to tidy up |
| in front of you | three trusting greenlings (random names, traits and head sprouts) |
| front right | a fresh **stray** to bond with, plus a mushroom, dandelion and flint for it to bring you |
| far left-back | a **Greenling Nest** |

God mode is on. Monsters still see you (ghost mode is off), and auto-pickup is on.

## Things to try

**Teaching**
1. Hover a greenling to see its mood. Use a **coin** on it (hotbar) until it says *curious*.
2. **E** (Pat) starts watching. A second E while it watches makes it forget its job.
3. Show a job before it gets bored (about 60 s): take an item from somewhere, put it somewhere else.

| Job | Show it like this |
|---|---|
| Haul | take Wood from the supply chest, put it in the storage chest |
| Smelt | take Copper Ore from the supply chest, add it to the smelter |
| Fuel | take Coal from the supply chest, add it to the smelter's fuel (or Wood into the hearth) |
| Charcoal | take Wood from the supply chest, add it to the kiln |
| Cook | take Raw Meat from the supply chest, put it on the spit |
| Collect cooking | take finished meat off the spit, put it in the storage chest |
| Brew | take Mead Base from the supply chest, put it in the fermenter |
| Honey | take honey from the beehive, put it in the storage chest |
| Forage | pick a raspberry bush, put the berries in the storage chest |
| Tidy up | pick up a piece of the loose wood, put it in the storage chest |

**Bonding a stray**: feed it (berries, mushrooms and honey are in your pack), pat it (E, with a 45 s cooldown),
or just stay near it. Watch it green up and its sprout grow. At 100% trust you'll get a message.

**Roles**: `gldev_place Greydwarf 0 4` or `gldev_place Greydwarf_Elite 0 4`, then `gldev_tame` and
`gldev_bond 100`. Greydwarfs can run the smelter and smash bushes. Brutes only smash: hit a log or rock
while one watches.

**Needs**: hungry greenlings raid the supply chest for berries; at night (`tod 0`) they go to the nest.
`gldev_cfg greenlings Needs MealInterval 10` makes them hungry fast.

**Other things to look at**
- Re-teaching a working greenling, and teaching two greenlings at once.
- Mischief: leave greenlings idle near chests. Force it with `gl_prank misplace`, `gl_prank zoomies` or `gl_prank scatter`.
- Taming from scratch: `gldev_run testbed_wild` puts two wild greylings 25 m ahead. Drop coins near
  one and keep out of sight. Wild greylings also attack greenlings.
- Restarting the game: jobs, names and sprouts should survive.

## Handy commands (F5)

| Command | Does |
|---|---|
| `gldev_run testbed` / `testbed_wild` / `testbed_help` | reset the base / add wild greylings / show tips |
| `gl_dump` | writes the state of every greenling and station nearby to the log |
| `gl_prank [zoomies\|misplace\|scatter\|stare\|gift]` | make the nearest idle greenling do something |
| `gldev_place <prefab> <right> <fwd>` | place any piece or creature, e.g. `gldev_place smelter 0 4` |
| `gldev_tame` / `gldev_bond 100` | tame the nearest greyling instantly / make it fully trusting |
| `gldev_act swing\|jump\|emote wave` | make yourself do something (to see mimicry) |
| `gldev_cam Greyling 1.5` / `gldev_cam off` | camera close-up on the nearest greyling (for looking at sprouts) |
| `spawn <item> <n>`, `nocost`, `debugmode` (Z to fly), `tod 0.5` (noon) | vanilla dev commands |

## Tuning while you play

Anything in `greenlings.cfg` can be changed live:

```
gldev_cfg greenlings Teaching AttentionSpan 120
gldev_cfg greenlings Labor ActionTime 0.4
gldev_cfg greenlings Visuals SproutSize 1.5
```

The `[Taming]` and `[Visuals] TamedTint` settings only apply after a restart. Note that this dev
profile sets `Taming.TamingTime = 20` (seconds) so taming is quick; the real default is 600.

## Reporting a problem

Run `gl_dump` right after it happens, then tell me roughly what you did. The log is at
`%APPDATA%\r2modmanPlus-local\Valheim\profiles\GreenlingsDev\BepInEx\LogOutput.log`, and I can read
it directly. Every greenling decision is in there as a `[GLEVT]` line.
