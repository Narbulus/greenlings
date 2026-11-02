# Implementation status

What's in the mod as of **0.7.0**, and how sure we are that it works.

| Pill | Meaning |
|---|---|
| <span class="st ok">tested</span> | Covered by an automated in-game scenario test (`tests/*.txt`), which passes |
| <span class="st part">partly</span> | Part of it is covered by a test. The note says which part isn't. |
| <span class="st built">built</span> | Written, and it works in play, but there's no scenario test yet |
| <span class="st plan">planned</span> | Not started. See the [roadmap](roadmap.md). |

*Last full run: see the [changelog](changelog.md) (shamans and tools, 2026-11-02).*

## Taming and trust

| Feature | Status | Test |
|---|---|---|
| Coin taming (eat off the ground, fed + calm, like boars) | <span class="st ok">tested</span> | `tame` |
| Brutes tamed with amber / amber pearls | <span class="st built">built</span> | |
| Shamans tamed with thistle | <span class="st built">built</span> | (taming loop is the same as `tame`) |
| Strays: random Norse-ish name, mischief only, can't be taught | <span class="st ok">tested</span> | `bonding` |
| Trust from pats (with cooldown), food, and being nearby | <span class="st ok">tested</span> | `bonding`, `feeding` |
| Strays tag along after a pat or treat | <span class="st ok">tested</span> | `follow` |
| Green tint and head sprout grow with trust | <span class="st built">built</span> | checked visually |
| Head sprouts pinned to the skull (crown vertices on the head bone) | <span class="st built">built</span> | checked visually (old model only) |
| Rename (Shift+E) | <span class="st ok">tested</span> | `persist-1/2` |
| Wild greylings hostile to greenlings | <span class="st ok">tested</span> | `hostility` |

## Teaching

| Feature | Status | Test |
|---|---|---|
| Coin starts a lesson; attention drains, demos top it up | <span class="st ok">tested</span> | `teach-*`, `lose-interest` |
| Pat while watching = forget the job | <span class="st ok">tested</span> | `reteach` |
| Re-teaching a working greenling | <span class="st ok">tested</span> | `reteach` |
| One demo teaches every watcher | <span class="st ok">tested</span> | `multi` |
| Multi-step jobs (attentive after a lesson, up to 3 steps) | <span class="st ok">tested</span> | `cook-job` |
| Correcting a step (same source or sink) | <span class="st built">built</span> | |
| Natural curiosity: watch unpaid, learn no-regret habits | <span class="st ok">tested</span> | `chores`, `forage` |
| Forage for the player (drops at your feet) | <span class="st ok">tested</span> | `forage` |
| Understanding burst VFX + chime | <span class="st built">built</span> | |
| Jobs and names survive a restart | <span class="st ok">tested</span> | `persist-1`, `persist-2` |

## Task adapters

| Task | Source → sink | Status | Test |
|---|---|---|---|
| Haul / restock | chest → chest | <span class="st ok">tested</span> | `teach-haul` |
| Tidy loose items | ground → chest | <span class="st ok">tested</span> | `teach-ground` |
| Smelter family (smelter, kiln, furnace, windmill, spinning wheel…) | chest → station | <span class="st ok">tested</span> | `teach-smelter`, `teach-fuel` |
| Fires, hearths, torches (lowest first, all within range) | chest → fire | <span class="st ok">tested</span> | `teach-fire`, `generalize-fire` |
| Cooking | chest → station → chest | <span class="st ok">tested</span> | `teach-cook`, `teach-cook-collect`, `cook-job` |
| Honey (every hive in range, plus loose honey) | hive → chest | <span class="st ok">tested</span> | `teach-beehive` |
| Harvest pickables (area) | bushes → chest | <span class="st ok">tested</span> | `teach-pickable` |
| Fermenter loading | chest → fermenter | <span class="st ok">tested</span> | `teach-fermenter` |
| Fermenter tapping | fermenter → chest | <span class="st built">built</span> | untested |
| Sap | extractor → chest | <span class="st built">built</span> | untested (needs an Ashlands tree) |
| Feeding spot (keeps a small pile) | chest → ground | <span class="st ok">tested</span> | `feeding` |
| Smashing (greydwarf bushes, brute logs/rocks; never trees) | — | <span class="st ok">tested</span> | `smash` |
| Roles gate machines (greylings can't) | — | <span class="st ok">tested</span> | `roles` |
| Planting (shamans only): a neat grid from your crops, filling the tilled patch, replanting after harvest | chest → field | <span class="st ok">tested</span> | `shaman-plant` |

## Shamans and tools

| Feature | Status | Test |
|---|---|---|
| Healing hurt greenlings and tamed animals | <span class="st ok">tested</span> | `shaman-heal` |
| Soothing strays (+trust) | <span class="st ok">tested</span> | `shaman-soothe` |
| Shaman company counts as "nearby" trust for strays | <span class="st built">built</span> | |
| Field layout: spacing and rows from your crops (or the patch's long side), row by row, with jitter | <span class="st ok">tested</span> | `shaman-plant` (+ visual check) |
| Leftover seeds go back to the chest when the field is full | <span class="st built">built</span> | |
| Tools held in hand (hammer, cultivator), gripped from the rig's own hand bones | <span class="st built">built</span> | checked visually on all rigs |
| Planting needs a cultivator; fetched from the job's chest | <span class="st ok">tested</span> | `shaman-plant` |
| Hammer fetched from a chest when something is worn | <span class="st ok">tested</span> | `repair-fetch` |
| Handing over the wrong tool is refused with a reason | <span class="st built">built</span> | |

## Free time

| Feature | Status | Test |
|---|---|---|
| Doing something useful when idle or blocked (no standing around) | <span class="st part">partly</span> | `chores` (the "blocked" case has no test of its own) |
| Tidy into the chest that already holds the item (never food) | <span class="st ok">tested</span> | `chores` |
| Help a working friend | <span class="st built">built</span> | |
| Repair with a hammer | <span class="st ok">tested</span> | `repair` |
| Rest and decorate at the nest | <span class="st ok">tested</span> | `needs` |
| Tag, huddle | <span class="st ok">tested</span> | `games` |
| Chase tame animals, chase birds | <span class="st built">built</span> | |
| Zoomies, scatter (never chests) | <span class="st ok">tested</span> | `mischief` |
| Gifts and staring | <span class="st ok">tested</span> | `gift` |
| Traits, mimicry, watching each other's antics | <span class="st ok">tested</span> | `personality` |
| Brute + greydwarf coordination (smash, then haul the pieces) | <span class="st built">built</span> | each half is tested; not as one scenario |

## Upkeep, movement, danger

| Feature | Status | Test |
|---|---|---|
| Hunger; eat only off the ground | <span class="st ok">tested</span> | `needs`, `feeding` |
| Greenling Nest piece; sleep at night; 2 per nest, shared out nightly | <span class="st ok">tested</span> | `needs` |
| Unmet needs: slower, more mischief, sulking | <span class="st built">built</span> | |
| Follow / stay | <span class="st ok">tested</span> | `follow` |
| Doors (not gates), closed behind them | <span class="st built">built</span> | no test yet |
| Passive towards animals (and boars no longer attack them) | <span class="st ok">tested</span> | `passive` |
| Fleeing to a player or brute, or panicking; pat to calm | <span class="st ok">tested</span> | `passive` |
| Drop carried item and hammer on death | <span class="st ok">tested</span> | `death-drop` |
| Carry visual (item in hand) | <span class="st built">built</span> | |

## Platform

| Feature | Status | Notes |
|---|---|---|
| Single-player | <span class="st ok">tested</span> | the whole suite runs in single-player |
| Multiplayer (state in ZDOs, RPCs for player actions) | <span class="st built">built</span> | designed for it, but never tested with two clients |
| Server-synced config | <span class="st plan">planned</span> | |
| Custom greenling model | <span class="st part">partly</span> | in progress on a separate branch (`gl_model`) |
| Game version | <span class="st ok">tested</span> | Valheim 1.0.21 |
