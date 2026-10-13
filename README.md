# IdolDrops

Enemies drop Forge of Potential idols of their own biome. Tougher enemies drop more often, bosses always drop one. Works with creature mods. Server synced.

A Valheim mod by Onononoki. Get it on Thunderstore: https://thunderstore.io/c/valheim/p/Onononoki/IdolDrops/

The full description and changelog are in [package/README.md](package/README.md) and [package/CHANGELOG.md](package/CHANGELOG.md).

## Bug reports and feedback

Open an issue at https://github.com/Ononononoki/IdolDrops/issues. Please add your mod list and the BepInEx log (`BepInEx/LogOutput.log`). Turning on `LogDrops` in the config writes one line per idol roll.

## Building

Needs the .NET SDK, Valheim and a mod manager profile with BepInEx and Jotunn.

```
dotnet build -c Release -p:GamePath="<Valheim folder>" -p:ProfilePath="<mod manager profile folder>"
```

The dll is built into `bin/Release/net472` and copied to `package/`.

## License

MIT, see [LICENSE](LICENSE).
