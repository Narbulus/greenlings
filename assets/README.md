# Greenlings

Tame greylings with coins and teach them to do your chores.

## Taming

Drop **coins** near a greyling and keep out of sight. It nibbles one coin at a time, and while
it is fed and calm it slowly warms up to you, just like taming a boar. A tamed greyling turns
**green** and becomes a **Greenling**. Wild greylings hate greenlings and attack them on sight,
and greenlings are fragile, so keep them safe.

- **Shift+E**: name your greenling.
- **Use a coin on it** (hotbar): makes it more curious.

## Teaching

Greenlings have moods. Hover over one to see whether it is *sulky*, *bored*, *curious* or *eager*.

1. **Pat it (E)** while it is curious. If it is in the mood, it perks up and **watches you**.
   If not, it swats your hand away, and a coin might change its mind.
2. **Show it a job** before it gets bored: take something from somewhere, then put it somewhere else.
3. When it understands, there is a **burst of green light** and it gets to work, carrying items back
   and forth until the source runs dry or the destination is full. Then it waits nearby and checks again later.

Things a greenling can learn:

| Take from | Put into |
|---|---|
| a chest | another chest |
| items lying on the ground | a smelter, furnace, kiln, windmill, spinning wheel or eitr refinery (ore or fuel) |
| a beehive, sap extractor, cooking station (done food) or fermenter (finished mead) | a fire, hearth, brazier or torch (fuel) |
| berry bushes, mushrooms and other pickables around a spot | a cooking station, or a fermenter (mead base) |

Idle greenlings get up to mischief: they run around, kick things about, and occasionally "borrow"
items from your chests and drop them somewhere else.

## Configuration

Everything (taming time, curiosity, attention span, carry size, mischief) is in
`BepInEx/config/greenlings.cfg`.

## Multiplayer

Every player and the server need the mod (it is not enforced yet, and the config is not synced
from the server yet: give everyone the same `greenlings.cfg`). Multiplayer has not been tested on a
dedicated server yet.

## Removing the mod

Greenlings adds no items or buildings of its own, so removing it can't break a world. But without it,
your greenlings stay **tame**: they become plain, inert greylings that never attack you but fight
wild creatures, can't be patted, named or taught, and wander around wherever they are. Anything a
greenling was carrying exists only in the mod's data and would be lost.

To leave a tidy world, run this **before** removing the mod, in single player or on the computer
hosting the world. Open the console with F5 (if it does not open, add `-console` to Valheim's launch
options in Steam); no cheats are needed.

| Command | Does |
|---|---|
| `gl_uninstall` | Reports what it found and changes nothing. |
| `gl_uninstall untame` | Turns every greenling and stray back into a wild greyling. **They will attack you**, so stand clear of your base's greenlings. |
| `gl_uninstall remove` | Deletes every greenling and stray. |
| `gl_uninstall keep` | Leaves them tame (inert friendly greylings, as above). |

All three put carried items on the ground (by the greenling, or at your feet if it is far away) and
clear the mod's saved data from every greenling in the world, loaded or not. Afterwards the mod
goes quiet for the rest of the session: **log out** (this saves the world), then remove the mod.
`gl_uninstall resume` wakes it up again if you change your mind, but it does not restore what was cleared.

For a dedicated server, copy the world files to your PC, open the world in single player, run the
command, log out, and copy the files back.
