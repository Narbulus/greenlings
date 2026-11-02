# Shamans

Greydwarf shamans are the camp's healers and gardeners. Tame one with **thistle**: drop it near a wild shaman
and keep out of sight, as with coins for greylings.

## Healing

A trusting shaman keeps an eye on everyone tamed within 20 m: greenlings and your animals. When one drops
below 75% health, the shaman goes over between work trips, casts its healing prayer and restores 40% of the
patient's health. It needs about 10 seconds before it can heal again. It can also heal players
(`HealPlayers`, off by default).

Shamans aren't fighters. Like greydwarfs, they flee monsters once they're hurt.

## Helping strays settle in

- **Company.** A stray near a trusting shaman gains trust as if you were standing next to it, even while
  you're away. If you're both there, it counts twice.
- **Soothing.** Now and then an idle shaman walks over to a stray and murmurs at it. The stray calms down and
  gains a little trust (+6). The same stray can be soothed once a minute.

## Planting

Only shamans plant. It works like any other lesson:

1. Till some ground with the cultivator.
2. Use a **coin** on the shaman.
3. Take seeds from a chest, then plant one or two in front of it.

The shaman fills the rest of the tilled patch and keeps it filled: when you harvest a crop, it replants the
gap. It carries five seeds per trip, and takes back whatever is left over when the field is full.

!!! tip "Planting from your pockets"
    If you plant seeds you were already carrying, the shaman uses the nearest chest that has those seeds.

### How it lays out the field

The shaman doesn't scatter seeds. It works out a grid and fills it row by row:

- **Spacing.** It matches the distance between the crops you planted. With nothing to copy, it leaves just
  enough room for the crop to grow.
- **Rows.** They line up with your crops, or with the long side of the tilled patch if you only planted one.
- **The field's edge.** It's the tilled ground connected to where you planted. Untilled ground and other
  crops mark the boundary, so a turnip bed next door stays a turnip bed.
- **Order.** It finishes the row you started before moving on to the next.

A greydwarf did the planting, though, so each crop lands a little off its mark (`CropJitter`). The rows are
clear, but they don't look machine-made. Every crop still gets its full growing room.

## Tools

Planting needs a **cultivator**. Hand one to a shaman (use it on the shaman), or leave one in a chest near the
field. A shaman with a planting job and no cultivator goes and gets it.
See [Tools](jobs.md#tools).
