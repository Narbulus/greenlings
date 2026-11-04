# Greenlings — design notes

Living doc. Spec items map onto Valheim systems as described below; "verified" means
exercised in-game through the dev harness.

## Ground rules (from research + reading the game code)

- **Behaviour via Harmony, state via ZDO.** Each networked object's ZDO is its synced
  state. Only the *owner* client runs a creature's AI (`MonsterAI.UpdateAI` returns early
  elsewhere), so the greenling "brain" runs on the owner and writes its state (mood, task,
  carried item) to the ZDO. Every client derives visuals from the ZDO.
- **Player actions on other objects go through that object's RPCs** (e.g. a smelter's
  add-ore/add-fuel RPCs), never by mutating someone else's ZDO directly.
- **Everyone needs the mod.** Prefab changes happen in `ZNetScene.Awake` on every
  client and the server. Gameplay config should become server-synced (ServerSync or
  Jötunn) before multiplayer release.
- **Vanilla first.** Reuse `Tameable`, `MonsterAI.MoveTo`, `EffectList`s and vanilla RPCs
  wherever they fit. Each one is a system other mods already cooperate with.

## Taming (milestone 1 — verified)

- Greyling prefab gets a `Tameable` (effects borrowed from Boar) and
  `MonsterAI.m_consumeItems = [Coins]`.
- Same loop as boars: drop coins near it. It eats **one coin per bite**, each bite keeps it
  fed for `FedDuration`, and taming progresses only while it is fed and **not alerted**.
  Hide, sneak, or build a pen, as with boars.
- Config: `TamingTime` 600 s (boar 1800), `FedDuration` 60 s, so about 10 coins if calm.
- Tamed means green tint (per-instance MaterialPropertyBlock on the `Custom/Creature` skin,
  eyes untouched) and the name **Greenling**. Driven by the ZDO tamed flag, so all clients agree.
- Naming: vanilla pet rename (Shift+E on a tamed one) works unchanged.
- Hostility: greylings have no creature group, so vanilla `BaseAI.IsEnemy` already makes
  wild ↔ tamed enemies. No patch needed.

## Pathfinding (what's available)

- Unity NavMesh baked at runtime in 32 m tiles around active agents, rebuilt about
  every 5 s, including player-built pieces. Greylings use agent `Humanoid`
  (radius 0.4, height 1.8, climb 0.3).
- `BaseAI.MoveTo(dt, point, stopDistance, run)` does path + steering, and
  `HavePath(target)` checks reachability, so we get base navigation for free.
- **To test:** doors (likely treated as obstacles), stairs/second floors, and tight build
  spaces. The old mod's notes say the greyling gets stuck in tight corners.

## Greenling brain (milestone 2 — teaching core verified by tests/teach-*.txt)

An owner-side state machine replaces `MonsterAI.UpdateAI` for tamed greenlings, except when
in danger: if there's a target, or it's hurt or fleeing, vanilla combat and flee code runs.

```
             coin / time                E (willing)              demo recognised
 MISCHIEF ───────────────▶ (curious) ───────────────▶ WATCHING ─────────────────▶ WORKING
    ▲   wander near home, poke chests,     │   face player, follow if far,    burst VFX,   loop source→sink
    │   throw pebbles, steal & drop        │   attention timer drains         task saved   until empty/full
    │                                      ▼                                     in ZDO
    └──────────── bored / gave up ◀─── (attention runs out)  ◀──── E again / task impossible
```

- **Mood: curiosity 0–100.** It drifts over time and is shown in the hover text
  ("curious", "sulky", "bored"). Using a coin on a greenling (the vanilla use-item key on
  hover) raises it. E while curious enough starts **Watching**. Otherwise it refuses with a
  shrug/emote and a message.
- **Watching:** stands still facing you and follows when you're more than about 6 m away.
  Attention drains faster the further away you are and when nothing teachable is happening.
- **Observation** hooks player actions and builds a *demonstration*:
  - take item X from container A, or pick X up off the ground near A
  - put X into sink B: smelter/kiln/furnace fuel or ore slot, or another container

  A recognised source→sink pair is a supported task. That triggers the burst-of-understanding
  VFX and saves the task (`{type, item, sourceZDOID, sinkZDOID}`) in the ZDO.
- **Working:** path to source, take one stack's worth, carry it to the sink, insert via the
  sink's own RPC, repeat. Idles near the sink when the source is empty or the sink is full,
  and rechecks periodically.
- **Carry visual** (stretch goal): attach the item's drop model to the greyling's hand bone.
  The greyling prefab has no `VisEquipment`, so this needs a manual attach. It looks doable.

Implemented and covered by scenario tests: coin taming, Pat (E) / second pat = forget job,
coin gifts, curiosity drift, Watching with attention, lesson recognition, Working loop with
wait/resume, re-teaching, idle mischief (zoomies / misplace items from chests / scatter drops),
carry visual (item model on the right hand), the understanding burst (`fx_guardstone_permitted_add`),
survival across world reloads (stations re-found by prefab + position since ZDOIDs change on load).
Not yet: multiplayer testing, server-synced config, crop replanting.

v1 supported tasks: **fuel/ore into smelter-family stations** (`Smelter` component: smelter,
charcoal kiln, furnace, blast furnace…) and **hauling container → container** (or ground →
container).

## Roadmap decided 2026-11-01 (second playtest round)

**Two-stage taming.** Coins make a wild greyling interested: it is tamed (not hostile, stays) but is
a **Stray**: wanders camp, mischief only, can't be taught. **Bonding** (0-100) grows by feeding it
real food (not coins), pats (with a cooldown), and time spent near the player. It turns gradually
greener as its bond grows. Head decorations are authored in the asset pipeline, rather than spawned
or grown at runtime. At 100 it is a **Greenling** and can be taught.

**Charm.** Staring at you; bringing you little gifts (like a cat with a bird); clumsy mimicry of what
you do; emergent **traits**; greenlings noticing and reacting to each other's antics (but never
learning jobs from each other). Procedurally generated short Norse-ish names (Grug, Skag, Skorby),
still renameable.

**Roles.**
- Greyling: light chores only: chest work, gathering loose items, sorting, keeping torches and braziers lit. No machines.
- Greydwarf: all of the above plus operating machines (smelters, kilns, cooking), gathering
  (breaking small bushes, picking up stone/flint), rock-thrower guard.
- Brute: heavy hauling (more per trip, slower), splitting logs, smashing rocks. Never fells trees.
- Shaman: later (farming, healing).
- No weight restriction on items.

**Needs/upkeep.** A nest (buildable piece) and food. Unmet needs mean less productive and more
unruly; they never die or untame from neglect.

## Third playtest round (2026-11-01): what changed

**Teaching is bought, not gated.** Curiosity as a gate is gone ("upkeep is the work, not mood
management"). A **coin** used on any trusting greenling starts a lesson. Attention times out, but
every demonstrated step tops it up. After a lesson it stays **attentive** for a minute: more demos
add steps to a multi-step **job** (max 3), and a demo with the same source or sink corrects that
step. A pat while watching = never mind.

**Natural curiosity.** Idle, fed greenlings sometimes come and watch you unpaid. They only take
**no-regret** chores from that (put away from ground/forage/hive, feed fires, brute smashing; never
chest-to-chest or machines), keep them as **habits** (max 3) and jump in for a few trips. Picking
berries in front of one sends it foraging for you (drops them at your feet).

**Jobs generalize.** Producer sources work every same-prefab producer within WorkRadius (20m) and
collect matching loose items there (stray honey; anything a cooking station makes). Fire jobs feed
whichever same-fuel fire nearby is lowest. Machine jobs work the bank of same-prefab stations within
8m. Chest sinks prefer the chest that already holds the item. Stations that take several inputs
take any accepted item from the source chest. Step choice per trip: first step with work AND
somewhere to put it.

**Free time.** Idle (or stuck waiting with empty hands) greenlings: tidy (loose item -> a chest that
already holds it; never food), help a working friend nearby with one of its steps, run a habit,
repair with a hammer, rest/decorate at the nest, games (tag, huddle in a ring, chase tame animals,
chase birds, which really take off), or mischief that never touches chests.

**Follow.** E on a trusting greenling toggles follow/stay. Strays tag along ~20s after a pat or a treat.

**Danger.** Greenlings ignore animals (never target non-monsters). Greylings and hurt greydwarfs
flee monsters: to a player or brute (hide behind it) or panic; a pat calms one once the threat is
gone. Brutes and healthy greydwarfs fight monsters (vanilla).

**Upkeep.** Food only from the ground (no chest raiding); a greenling can be taught to stock a
feeding spot (GroundDrop sink, keeps a small pile, never tidied away). Nests sleep 2 each, assigned
dynamically each night; "homeless" uses a fixed pecking order so all clients agree.

**Misc.** Doors (not gates) opened when walking into one or when no path exists. Carried items and
hammer drop on death. Brutes tame on amber / amber pearls. Head plants limited to forest things and
pinned to the top of the skull mesh (bind-pose crown vertices, parented to the head bone).

## Shamans and tools (2026-11-02)

**Shamans** (Greydwarf_Shaman, tamed with thistle): greyling chores + planting (nobody else plants). Heal tamed
creatures nearby between trips (owner-side "heal" prank, Character.Heal routes via RPC). Their company counts for
stray trust, and they actively soothe strays (GL_Soothe, per-stray cooldown).

**Fields.** Planting is a sink (EndpointKind.Field, Prefab = crop piece). The grid is inferred once per field and kept:
spacing = upper quartile of nearest-neighbour distances between the crops already there (robust to jitter),
or the crop's grow radius; row direction = 4-fold circular mean of those steps (a square grid's two axes agree),
or the cultivated patch's principal axis. The field is a flood fill over grid cells from the taught spot through
cultivated ground, stopping at other crops. Next spot = lowest row rank (start row, then alternating outward),
nearest to the shaman. Each crop is jittered within the slack its growing room allows. Harvested gaps get replanted.

**Tools.** One tool slot (gl_tool). Steps can need a tool (Field needs Cultivator); a missing tool is fetched from the
job's source chest or the nearest chest near the work, else the greenling waits ("needs_cultivator"). Hammers get
fetched for repair when something nearby is worn. Visual: the item's own "attach" model; grip fitted from the
hand bones (knuckles give the fist and the across-the-fist axis, wrist->knuckles gives the forearm), with the tool's
handle (attach origin -> model centre) across the fist and its head along the forearm. Long tools are held a third of the way up.

## Later

Skill progression (faster learning, bigger carries), breeding (`Procreation`),
Beasts of Burden cart integration, more tools (axes/pickaxes), shamans calming wild creatures.

## Decisions

- Every source/sink kind in the table below is wanted; adapters get added one at a time.
- E is **Pat**: affection for strays, follow/stay for greenlings, never-mind while watching, calm when scared. Shift+E renames. A coin teaches.
- Greenlings stay fragile (20 HP). Protecting them is part of the game.

## Task adapters

| Task | Source -> Sink | Component | Status |
|---|---|---|---|
| Feed smelter/furnace/kiln/windmill/spinning wheel/refinery | chest -> station | `Smelter` | done (teach-smelter, teach-fuel) |
| Collect smelter output / tidy drops | ground -> chest | `ItemDrop` | done (teach-ground) |
| Haul / restock | chest -> chest | `Container` | done (teach-haul) |
| Fires, hearths, braziers, torches | chest -> fire | `Fireplace` | done (teach-fire) |
| Cooking | chest -> cooking station -> chest | `CookingStation` | done (teach-cook, teach-cook-collect) |
| Honey | hive -> chest | `Beehive` | done (teach-beehive) |
| Sap | extractor -> chest | `SapCollector` | written, untested (needs an Ashlands tree) |
| Fermenter | chest -> fermenter | `Fermenter` | load done (teach-fermenter); tapping written, untested |
| Harvest pickables | bushes/mushrooms -> chest | `Pickable` | done (teach-pickable) |
| Replant crops | seed -> soil | `Plant` | todo |
