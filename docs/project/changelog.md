# Changelog

## 0.7.0: shamans and tools (2026-11-02)

**Shamans** (tamed with thistle)

- They heal hurt greenlings and tamed animals within 20 m, between trips, with their own prayer effect.
- They help strays settle in. Their company counts as "nearby" trust, and they go over and soothe strays (+6 trust).
- Only shamans plant. Plant a seed or two on tilled ground in front of one and it works out the field: spacing
  and rows copied from your crops (or the long side of the patch), filled row by row and replanted after
  harvest. Each crop lands slightly off its mark, so it looks hand-planted. It carries 5 seeds per trip and returns any leftovers.

**Tools**

- Planting needs a cultivator, and repairing needs a hammer. Tools are shown in the hand, using the item's own
  in-hand model, gripped from the hand bones (handle across the fist; long tools held a third of the way up).
- A greenling whose work needs a tool fetches one from a chest near the work (the job's own chest first) and
  puts back what it held. Handing the wrong tool to the wrong role is refused with a reason.

**Dev**

- New scenario tests: `shaman-heal`, `shaman-soothe`, `shaman-plant`, `repair-fetch`.
- New dev commands: `gldev_cultivate`, `gldev_plant`, `gldev_hurt`, and bone-focused `gldev_cam`.
- The testbed adds a shaman, a tilled strip, seeds and spare tools.

## 0.6.0: third playtest round (2026-11-02)

**Teaching**

- A coin starts a lesson for any trusting greenling. The old *curiosity* mood gate is gone.
- Every demonstrated step tops up attention. After a lesson the greenling stays attentive for a minute, so you can add
  steps (jobs of up to 3 steps) or correct one.
- Natural curiosity: idle, fed greenlings come over to watch and pick up no-regret habits. Watching you pick
  berries sends one foraging for you.

**Jobs**

- Jobs generalize within 20 m: every same-prefab producer, every same-fuel fire (lowest first), banks of
  machines within 8 m, and the chest that already holds the item.
- Cooking stations: whatever the station produces gets collected, burnt food included.
- New sinks: drop on the ground (feeding spot) and bring it to me.
- Fixed: a carrying greenling could freeze in front of a chest (seen with honey).

**Free time**

- Idle or blocked greenlings tidy up, help friends, run habits, repair with a hammer, rest and decorate at
  the nest, and play: tag, huddle, chasing animals and birds.
- Mischief no longer touches chests.

**Trust, movement, danger**

- Trust builds about 30% faster. The pat cooldown is 30 s. Strays tag along after a treat or a pat, and eat off the ground.
- E toggles follow/stay.
- Greenlings leave animals alone, and boars no longer attack them. Greylings flee to you or a brute, or panic.
  A pat calms them.
- Doors (not gates) are opened and closed behind them.
- Carried items and the hammer drop on death.
- Brutes are tamed with amber / amber pearls.

**Upkeep**

- Food comes only from the ground. Nests sleep 2 each and beds are shared out nightly.
- Fixed: the Greenling Nest piece wasn't registering.

**Visuals**

- Head plants are forest things only (no glowing mushrooms) and are pinned to the top of the skull, so they no
  longer glide off-centre.
- A quieter trust sound (the greyling's own chirp instead of boar noises).

## 0.5.x: second playtest round (2026-11-01)

- Two-stage taming: stray → trust → greenling, with tint and sprout growing with trust.
- Roles: greylings, greydwarfs (machines, gathering), brutes (smashing).
- Needs: food and the Greenling Nest. Traits, names, gifts, mimicry.

## Earlier

- Coin taming, the teaching core (watch → demonstrate → work), the first task adapters, and persistence across restarts.
