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

Implemented so far: Pat (E), coin gifts, curiosity drift, Watching with attention, lesson recognition
(container/ground source, container/smelter-ore/smelter-fuel sink), Working loop, wait/resume.
Not yet: Idle mischief (vanilla tamed idle for now), carry visual, the VFX choice is a placeholder
(`vfx_HealthUpgrade`), fuel task untested, ground source untested, multiplayer untested.

v1 supported tasks: **fuel/ore into smelter-family stations** (`Smelter` component: smelter,
charcoal kiln, furnace, blast furnace…) and **hauling container → container** (or ground →
container).

## Later

Traits (lazy, clever, clumsy), skill progression (faster learning, bigger carries),
breeding (`Procreation`), tameable brutes and shamans, more task types.

## Decisions

- Every source/sink kind in the table below is wanted; adapters get added one at a time.
- E is only **Pat**. A pat starts a lesson when curiosity is high enough. Shift+E renames.
- Greenlings stay fragile (20 HP). Protecting them is part of the game.

## Task adapters

| Task | Source -> Sink | Component | Status |
|---|---|---|---|
| Feed smelter/furnace/kiln/windmill/spinning wheel/refinery | chest -> station | `Smelter` | ore done, fuel untested |
| Collect smelter output | ground -> chest | `ItemDrop` | ground source written, untested |
| Haul / restock | chest -> chest | `Container` | done |
| Tidy up drops | ground -> chest | `ItemDrop` | ground source written, untested |
| Fires, hearths, braziers, torches | chest -> fire | `Fireplace` | todo |
| Cooking | chest -> cooking station -> chest | `CookingStation` | todo |
| Honey / sap | hive/extractor -> chest | `Beehive`, `SapCollector` | todo |
| Fermenter | chest <-> fermenter | `Fermenter` | todo |
| Harvest + replant | crop -> chest, seed -> soil | `Pickable`, `Plant` | todo |
