# IdolDrops

**Idols for the Forge of Potential no longer hide only in chests. Enemies drop them too.**

Every enemy has a small chance to drop a Battle or Protection idol of its own biome. Greydwarfs drop Black Forest idols, Fulings drop Plains idols. Bosses always drop one. The Fuling on the icon found out the hard way.

## How it works

- Each enemy has a 0.5% to 5% chance, depending on how tough and how common it is. A Greydwarf has 0.67%, a Troll 2.5%.
- Every star doubles the chance.
- Bosses and minibosses always drop one.
- Only your kills count. Tamed creatures, summons and animals drop nothing.
- Raids and the Jotun invasion drop at half the chance.
- Enemies from many other mods (Monstrum, MonsterLabZ and others) are added on their own, by the biome they live in. Not every mod is tested, so if something looks wrong, please tell me on GitHub.

## Settings

Everything is in `BepInEx/config/onononoki.IdolDrops.cfg` and synced from the server: the chances, the creature lists, a global multiplier, the star bonus. Creatures added from other mods are listed in `onononoki.IdolDrops.auto.txt` next to it, ready to copy into the config.

## Install

- Use a mod manager, or put `IdolDrops.dll` into `BepInEx/plugins`. Needs BepInExPack Valheim and Jotunn.
- In multiplayer the server and every player need it.

## Feedback

Bugs and ideas: [GitHub issues](https://github.com/Ononononoki/IdolDrops/issues).
