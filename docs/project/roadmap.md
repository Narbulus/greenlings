# Roadmap

## Next up

- [ ] **Doors scenario test.** Build an enclosure from a test script and check open, walk through, close.
- [ ] **Scenario tests** for chasing birds and animals, help-a-friend, and a brute + greydwarf crew working together.
- [ ] **Sound check.** Does the trust-gain chirp sound right?
- [ ] **Custom greenling model.** A remodelled greyling with the head sprout sitting in its new crown.
- [ ] **Git history.** Bring the project history over to this repo and push it to GitHub. This wiki then deploys from there.

## Before a first release

- [ ] **Multiplayer test** with two clients and a dedicated server.
- [ ] **Server-synced config** (ServerSync or Jötunn), so the host's settings win.
- [ ] **Thunderstore package**: icon, manifest, README (`scripts/package.ps1` already builds the zip).
- [ ] Untested adapters: **fermenter tapping** and **sap extractors**.
- [ ] Performance pass with a large crew (20+ greenlings).

## Later

- [ ] **Beasts of Burden integration.** Greenlings hauling with carts.
- [ ] **More tools**: axes and pickaxes for greydwarf gathering, and a tool-wear system.
- [ ] **Shamans calming wild creatures** to speed up taming itself, not just a stray's trust.
- [x] **Shamans**: healing, soothing strays, planting (0.7.0).
- [ ] **Skill progression**: learning faster and carrying more over time.
- [ ] **Breeding** (vanilla `Procreation`).

## Decided, and not changing

- E is **Pat**: affection for strays, follow/stay for greenlings, never-mind while watching, calm when scared.
  Shift+E renames. A **coin** teaches.
- Greenlings stay fragile (20 HP). Protecting them is part of the game.
- Neglect never kills or untames. It makes them slower and naughtier.
- No weight limits on items: one item per trip.
- Brutes never fell standing trees.
- Greenlings never learn jobs from each other, only habits from you.

## History

- **2026-11-01, second playtest:** two-stage taming (stray → trust → greenling), roles, needs, charm and traits.
- **2026-11-02, shamans and tools:** shamans heal, soothe strays and plant neat (slightly wonky) fields;
  tools are held in hand and fetched from chests.
- **2026-11-01, third playtest:** teaching paid for with a coin instead of a curiosity mood; jobs that generalize;
  free-time chores and games; follow; fleeing; doors. See the [changelog](changelog.md).

The full design notes are in `DESIGN.md` at the repo root. They're kept as a living document alongside this wiki.
