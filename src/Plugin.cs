using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;

namespace IdolDrops
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency("com.jotunn.jotunn", BepInDependency.DependencyFlags.HardDependency)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string ModGuid = "onononoki.IdolDrops";
        public const string ModName = "IdolDrops";
        public const string ModVersion = "1.0.1";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> GlobalMultiplier;
        internal static ConfigEntry<float> ChancePerStar;
        internal static ConfigEntry<float> EventMultiplier;
        internal static ConfigEntry<float> WeaponShare;
        internal static ConfigEntry<bool> RequirePlayerKill;
        internal static ConfigEntry<bool> SummonsDrop;
        internal static ConfigEntry<int> MaxStars;
        internal static ConfigEntry<bool> AutoAddCreatures;
        internal static ConfigEntry<bool> LogDrops;
        private static ConfigEntry<string> _excluded;
        private static readonly ConfigEntry<float>[] ClassPercents = new ConfigEntry<float>[DropRules.Classes.Length];
        private static readonly ConfigEntry<string>[] ClassLists = new ConfigEntry<string>[DropRules.Classes.Length];
        private static ConfigEntry<string> _overrides;
        private static ConfigEntry<string> _bosses;

        // prefab name to its drop rule, rebuilt after every config change or server sync
        internal static Dictionary<string, Rule> Rules = new Dictionary<string, Rule>();

        private void Awake()
        {
            Log = Logger;
            var sync = new ConfigurationManagerAttributes { IsAdminOnly = true };

            Enabled = Config.Bind("1 - General", "Enabled", true,
                new ConfigDescription("Enemies drop Forge of Potential idols of their biome's tier.", null, sync));
            GlobalMultiplier = Config.Bind("1 - General", "GlobalMultiplier", 1f,
                new ConfigDescription("Multiplies every drop chance. Bosses always drop one idol.", new AcceptableValueRange<float>(0f, 100f), sync));
            ChancePerStar = Config.Bind("1 - General", "ChancePerStar", 2f,
                new ConfigDescription("Chance factor per star. 2 doubles the chance at 1 star and quadruples it at 2 stars, like the game's own star loot.", new AcceptableValueRange<float>(1f, 10f), sync));
            EventMultiplier = Config.Bind("1 - General", "EventMultiplier", 0.5f,
                new ConfigDescription("Chance factor for raid creatures and creatures killed during a world event such as the Jotun invasion.", new AcceptableValueRange<float>(0f, 1f), sync));
            WeaponShare = Config.Bind("1 - General", "WeaponShare", 50f,
                new ConfigDescription("Percent of drops that are Battle idols. The rest are Protection idols.", new AcceptableValueRange<float>(0f, 100f), sync));
            RequirePlayerKill = Config.Bind("1 - General", "RequirePlayerKill", true,
                new ConfigDescription("Only creatures killed by a player drop idols. A kill finished by burning, poison or other damage over time counts when a player hit the creature. Stops farms and fights between creatures from making idols. Bosses always drop.", null, sync));
            SummonsDrop = Config.Bind("1 - General", "SummonsDrop", false,
                new ConfigDescription("Creatures spawned by other creatures and bosses (Bonemass's skeletons, Gjall's ticks, the Queen's brood, an Oozer's blobs) can drop idols too.", null, sync));
            MaxStars = Config.Bind("1 - General", "MaxStars", 2,
                new ConfigDescription("Stars above this count no more for the chance, for mods that add more than 2 stars.", new AcceptableValueRange<int>(0, 10), sync));
            AutoAddCreatures = Config.Bind("1 - General", "AutoAddCreatures", true,
                new ConfigDescription("Enemies in no list (for example from other mods) drop idols too. Tier from the biome they spawn in, class from their health and spawn rate, bosses always drop. Each one is written to " + ModGuid + ".auto.txt next to this file.", null, sync));

            for (int i = 0; i < DropRules.Classes.Length; i++)
            {
                string name = DropRules.Classes[i];
                ClassPercents[i] = Config.Bind("2 - Chances", name, DropRules.DefaultPercents[i],
                    new ConfigDescription($"Drop chance in percent for the creatures in the {name} list.", new AcceptableValueRange<float>(0f, 100f), sync));
                ClassLists[i] = Config.Bind("3 - Creatures", name, DropRules.DefaultLists[i],
                    new ConfigDescription($"Creatures that drop at the {name} chance, as PrefabName:tier separated by commas. Tier 0 Meadows, 1 Black Forest, 2 Swamp, 3 Mountains, 4 Plains, 5 Mistlands, 6 Ashlands, 7 Deep North.", null, sync));
            }
            _overrides = Config.Bind("3 - Creatures", "Overrides", DropRules.DefaultOverrides,
                new ConfigDescription("Own chance for single creatures, as PrefabName:percent separated by commas. The creature must also be in a class list.", null, sync));
            _bosses = Config.Bind("3 - Creatures", "Bosses", DropRules.DefaultBosses,
                new ConfigDescription("Bosses that always drop one idol, as PrefabName:tier separated by commas.", null, sync));
            _excluded = Config.Bind("3 - Creatures", "Excluded", DropRules.DefaultExcluded,
                new ConfigDescription("Creatures that are never auto-added, as prefab names separated by commas. Listed creatures still drop.", null, sync));

            LogDrops = Config.Bind("9 - Debug", "LogDrops", false,
                "Write one line to the log for every idol roll. Only affects your own game.");

            foreach (var entry in ClassPercents) entry.SettingChanged += (s, e) => BuildRules();
            foreach (var entry in ClassLists) entry.SettingChanged += (s, e) => BuildRules();
            _overrides.SettingChanged += (s, e) => BuildRules();
            _bosses.SettingChanged += (s, e) => BuildRules();
            _excluded.SettingChanged += (s, e) => BuildRules();
            SynchronizationManager.OnConfigurationSynchronized += (s, e) => BuildRules();
            BuildRules();

            new Harmony(ModGuid).PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo($"{ModName} {ModVersion} loaded");
        }

        private static void BuildRules()
        {
            var lists = new string[ClassLists.Length];
            var percents = new float[ClassPercents.Length];
            for (int i = 0; i < lists.Length; i++)
            {
                lists[i] = ClassLists[i].Value;
                percents[i] = ClassPercents[i].Value;
            }
            var warnings = new List<string>();
            Rules = DropRules.Build(lists, percents, _overrides.Value, _bosses.Value, warnings);
            AutoRules.SetExcluded(_excluded.Value);
            foreach (var warning in warnings) Log.LogWarning(warning);
            Log.LogInfo($"{Rules.Count} creatures drop idols");
        }

        // chance in percent of a class, 0 Swarm to 4 Rare
        internal static float ClassPercent(int cls) => ClassPercents[cls].Value;
    }
}
