using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace IdolDrops
{
    // drop rules for creatures in no list, such as creatures from other mods. the tier comes from the biomes the
    // game's spawn lists put them in (or the biome they die in), the class from their health and spawn rate
    internal static class AutoRules
    {
        private static readonly FieldInfo SpawnInstances = AccessTools.Field(typeof(SpawnSystem), "m_instances");

        // what was learned about a prefab once, the tier may still come from the death biome each time
        private class Info
        {
            public string Skip;
            public bool Boss;
            public float Health;
            public int SpawnTier = -1;
            public float Rate;
        }

        private static readonly Dictionary<string, Info> Cache = new Dictionary<string, Info>(StringComparer.Ordinal);
        private static readonly HashSet<string> Written = new HashSet<string>(StringComparer.Ordinal);
        private static HashSet<string> _excluded = new HashSet<string>(StringComparer.Ordinal);

        internal static void SetExcluded(string value)
        {
            _excluded = DropRules.ParseNames(value);
            Cache.Clear();
        }

        internal static bool TryGet(Character character, string name, out Rule rule, out string source)
        {
            rule = default;
            source = null;
            if (!Plugin.AutoAddCreatures.Value || _excluded.Contains(name)) return false;
            if (!Cache.TryGetValue(name, out var info))
            {
                info = Learn(character, name);
                if (info == null) return false;
                Cache[name] = info;
            }
            if (info.Skip != null) return false;

            int tier = info.SpawnTier;
            source = "spawn list";
            if (tier < 0)
            {
                tier = DropRules.BiomeTier((int)Heightmap.FindBiome(character.transform.position));
                source = "death biome";
            }
            if (tier < 0) return false;

            if (info.Boss)
            {
                tier = Math.Max(tier, DropRules.BossHealthTier(info.Health));
                rule = new Rule { Tier = tier, Percent = 100f, Boss = true };
                Write(name, $"Bosses: {name}:{tier}", info, source);
                return true;
            }
            int cls = DropRules.AutoClass(info.Health, tier, info.Rate);
            rule = new Rule { Tier = tier, Percent = Plugin.ClassPercent(cls) };
            Write(name, $"{DropRules.Classes[cls]}: {name}:{tier}", info, source);
            return true;
        }

        // reads the creature's components and the world spawn lists. null when no spawn system is loaded yet
        private static Info Learn(Character character, string name)
        {
            var info = new Info { Boss = character.IsBoss(), Health = character.m_health };
            var faction = character.m_faction;
            if (faction == Character.Faction.Players || faction == Character.Faction.AnimalsVeg
                || faction == Character.Faction.PlayerSpawned || faction == Character.Faction.TrainingDummy)
                info.Skip = $"faction {faction}";
            else if (!(character.GetBaseAI() is MonsterAI ai))
                info.Skip = "no monster AI (an animal)";
            else if (ai.m_passiveAggresive && !info.Boss)
                info.Skip = "passive until attacked";
            else if (character.GetComponent<Growup>())
                info.Skip = "a baby that grows up";
            else if (character is Humanoid humanoid && !info.Boss && Count(humanoid.m_defaultItems) + Count(humanoid.m_randomWeapon) + Count(humanoid.m_randomSets) == 0)
                info.Skip = "no attacks";
            if (info.Skip != null)
            {
                if (Plugin.LogDrops.Value) Plugin.Log.LogInfo($"{name}: not auto-added, {info.Skip}");
                return info;
            }

            var systems = SpawnInstances?.GetValue(null) as List<SpawnSystem>;
            var system = systems?.FirstOrDefault(s => s);
            if (!system) return null;
            int biomes = 0;
            foreach (var list in system.m_spawnLists)
            {
                if (!list) continue;
                foreach (var spawn in list.m_spawners)
                {
                    if (spawn == null || !spawn.m_enabled || !spawn.m_prefab || spawn.m_prefab.name != name) continue;
                    if (!string.IsNullOrEmpty(spawn.m_requiredPersistentEvent)) continue;
                    biomes |= (int)spawn.m_biome;
                    info.Rate = Math.Max(info.Rate, DropRules.SpawnRate(spawn.m_spawnInterval, spawn.m_spawnChance, spawn.m_groupSizeMin, spawn.m_groupSizeMax));
                }
            }
            info.SpawnTier = DropRules.BiomeTier(biomes);
            return info;
        }

        private static int Count(Array items) => items?.Length ?? 0;

        // one line per auto-added creature, in the cfg's own format, so admins can copy it into the lists
        private static void Write(string name, string entry, Info info, string source)
        {
            var path = Path.Combine(BepInEx.Paths.ConfigPath, Plugin.ModGuid + ".auto.txt");
            if (Written.Count == 0) LoadWritten(path);
            if (!Written.Add(name)) return;
            string line = $"{entry,-40} health {info.Health:0}, spawn rate {info.Rate:0.#} per hour, tier from {source}";
            Plugin.Log.LogInfo("auto-added " + line);
            try
            {
                if (!File.Exists(path))
                    File.WriteAllText(path, "# creatures IdolDrops added on its own. copy an entry into a list in " + Plugin.ModGuid + ".cfg to change it\n");
                File.AppendAllText(path, line + "\n");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("could not write the auto list: " + ex.Message);
            }
        }

        // creatures already in the file from earlier sessions, so each one is written once
        private static void LoadWritten(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                foreach (var line in File.ReadAllLines(path))
                {
                    var bits = line.Split(new[] { ':', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (bits.Length >= 2 && !line.StartsWith("#")) Written.Add(bits[1]);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("could not read the auto list: " + ex.Message);
            }
        }
    }
}
