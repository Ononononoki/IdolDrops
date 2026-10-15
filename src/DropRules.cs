using System;
using System.Collections.Generic;
using System.Globalization;

namespace IdolDrops
{
    // what one creature prefab drops: the idol tier and the chance in percent, or a guaranteed boss idol
    internal struct Rule
    {
        public int Tier;
        public float Percent;
        public bool Boss;
    }

    // the default lists and the parsing and chance math. no unity calls, so the offline check can run all of it
    internal static class DropRules
    {
        internal const int MaxTier = 7;

        internal static readonly string[] Classes = { "Swarm", "Common", "Uncommon", "Strong", "Rare" };
        internal static readonly float[] DefaultPercents = { 0.5f, 0.67f, 1f, 2.5f, 5f };

        // Prefab:tier entries per class, tier 0 Meadows to 7 Deep North
        internal static readonly string[] DefaultLists =
        {
            "Greyling:0, Neck:0, Leech:2, Bat_Swamp:2, Bat:3, Leech_cave:3, Tick:5, SeekerBrood:5, Charred_Twitcher:6, BlobMorkMini:7",

            "Boar:0, Skeleton_Meadows:0, Skeleton_Meadows_noarcher:0, Greydwarf:1, Skeleton:1, Skeleton_NoArcher:1, " +
            "Draugr:2, Draugr_sleeping:2, Draugr_Ranged:2, Draugr_Ranged_sleeping:2, Skeleton_Swamps:2, Skeleton_Swamps_noarcher:2, " +
            "Wolf:3, Skeleton_Mountains:3, Skeleton_Mountains_noarcher:3, Goblin:4, GoblinArcher:4, Seeker:5, " +
            "Charred_Melee:6, Charred_Archer:6, Asksvin_hatchling:6, Elaking:7, Greydwarf_Frozen:7, Skeleton_DeepNorth:7, GoblinDeepNorth:7",

            "Greydwarf_Shaman:1, Ghost:1, Ghost_sleeping:1, Blob:2, Surtling:2, Hatchling:3, Ulv:3, BlobFrost:3, " +
            "Deathsquito:4, GoblinShaman:4, BlobTar:4, Dverger:5, DvergerMage:5, DvergerMageFire:5, DvergerMageIce:5, DvergerMageSupport:5, " +
            "Asksvin:6, Volture:6, BlobLava:6, DvergerAshlands:6, ElakingLantern:7, Greydwarf_Shaman_Frozen:7, ShadowPerson:7, BlobMork:7, DvergerDeepNorth:7",

            "Greydwarf_Elite:1, Troll:1, Troll_sleeping:1, Bjorn:1, Bjorn_sleeping:1, Draugr_Elite:2, Draugr_Elite_sleeping:2, " +
            "BlobElite:2, Wraith:2, Serpent:2, Fenring:3, Fenring_Cultist:3, GoblinBrute:4, Lox:4, Unbjorn:4, " +
            "Charred_Mage:6, BonemawSerpent:6, Barka:7, TrollFrost:7, ElakingMole:7, FallenWarrior:7",

            "Skeleton_Poison:1, Skeleton_Hildir_nochest:1, Abomination:2, Writhan:2, BogWitchKvastur:2, " +
            "StoneGolem:3, Fenring_Cultist_Hildir_nochest:3, " +
            "GoblinBruteBros_nochest:4, GoblinShaman_Hildir_nochest:4, SeekerBrute:5, Gjall:5, " +
            "Morgen:6, Morgen_NonSleeping:6, FallenValkyrie:6, Charred_Melee_Dyrnwyn:6, JotunWarrior:7, JotunWarriorDualWield:7, JotunWitch:7"
        };

        internal const string DefaultOverrides = "Tick:0.2, SeekerBrood:0.2";

        // the bosses, then Hildir's quest minibosses (the versions that guard her chests)
        internal const string DefaultBosses =
            "Eikthyr:0, gd_king:1, Bonemass:2, Dragon:3, GoblinKing:4, SeekerQueen:5, Fader:6, FrozenKing_p3:7, " +
            "Skeleton_Hildir:1, Fenring_Cultist_Hildir:3, GoblinBrute_Hildir:4, GoblinBruteBros:4, GoblinShaman_Hildir:4";

        // builds the lookup from the class lists, the overrides and the boss list. problems go to warnings
        internal static Dictionary<string, Rule> Build(string[] lists, float[] percents, string overrides, string bosses, List<string> warnings)
        {
            var rules = new Dictionary<string, Rule>(StringComparer.Ordinal);
            for (int c = 0; c < lists.Length; c++)
            {
                foreach (var entry in ParsePairs(lists[c], warnings))
                {
                    if (!TryTier(entry.Value, out int tier))
                    {
                        warnings.Add($"{Classes[c]}: {entry.Key} has no tier from 0 to {MaxTier}, skipped");
                        continue;
                    }
                    if (rules.ContainsKey(entry.Key))
                    {
                        warnings.Add($"{Classes[c]}: {entry.Key} is already in another list, skipped");
                        continue;
                    }
                    rules[entry.Key] = new Rule { Tier = tier, Percent = percents[c] };
                }
            }

            foreach (var entry in ParsePairs(overrides, warnings))
            {
                if (!TryPercent(entry.Value, out float percent))
                    warnings.Add($"Overrides: {entry.Key} has no valid percent, skipped");
                else if (!rules.TryGetValue(entry.Key, out var rule))
                    warnings.Add($"Overrides: {entry.Key} is in no class list, skipped");
                else
                {
                    rule.Percent = percent;
                    rules[entry.Key] = rule;
                }
            }

            foreach (var entry in ParsePairs(bosses, warnings))
            {
                if (!TryTier(entry.Value, out int tier))
                    warnings.Add($"Bosses: {entry.Key} has no tier from 0 to {MaxTier}, skipped");
                else
                    rules[entry.Key] = new Rule { Tier = tier, Percent = 100f, Boss = true };
            }
            return rules;
        }

        // "Name:value, Name:value" into pairs, spaces trimmed, broken entries reported
        internal static List<KeyValuePair<string, string>> ParsePairs(string value, List<string> warnings)
        {
            var list = new List<KeyValuePair<string, string>>();
            foreach (var part in (value ?? "").Split(','))
            {
                if (part.Trim().Length == 0) continue;
                var bits = part.Split(':');
                if (bits.Length == 2 && bits[0].Trim().Length > 0 && bits[1].Trim().Length > 0)
                    list.Add(new KeyValuePair<string, string>(bits[0].Trim(), bits[1].Trim()));
                else
                    warnings.Add($"\"{part.Trim()}\" is not Name:value, skipped");
            }
            return list;
        }

        private static bool TryTier(string text, out int tier) =>
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out tier) && tier >= 0 && tier <= MaxTier;

        private static bool TryPercent(string text, out float percent) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out percent) && percent >= 0f && percent <= 100f;

        // chance from 0 to 1 for one kill: class or override percent, times the star factor, the event factor and the global factor
        internal static float Chance(float percent, int stars, float perStar, bool fromEvent, float eventMultiplier, float globalMultiplier)
        {
            double chance = percent / 100.0 * Math.Pow(perStar, Math.Max(0, stars));
            if (fromEvent) chance *= eventMultiplier;
            chance *= globalMultiplier;
            return (float)Math.Max(0.0, Math.Min(1.0, chance));
        }

        // the game's idol prefabs: Upgrader0Weapon (Battle idol) to Upgrader7Armor (Protection idol)
        internal static string IdolName(int tier, bool weapon) => $"Upgrader{tier}{(weapon ? "Weapon" : "Armor")}";

        // why a kill does not count as a player kill, or null when it does. the game's damage over time (burning,
        // poison, spirit, smoke) hits with no attacker, so a last hit without one counts when a player damaged the
        // creature, and note says so
        internal static string PlayerKillSkip(bool lastHitHasAttacker, bool lastHitByPlayer, int playersWhoHit, out string note)
        {
            note = null;
            if (lastHitHasAttacker) return lastHitByPlayer ? null : "last hit not from a player";
            if (playersWhoHit <= 0) return "last hit had no attacker and no player hit it";
            note = "last hit without attacker (damage over time), a player hit it";
            return null;
        }

        // never auto-added: vanilla animals, summons, boss adds, test objects and creatures of unclear origin
        internal const string DefaultExcluded =
            "Deer, Deer_White, Chicken, Hen, Boar_piggy, Wolf_cub, Lox_Calf, Moose, Moose_calf, Seal, Seal_Pup, Hare, " +
            "Troll_Summoned, Charred_Twitcher_Summoned, Charred_Melee_Fader, Charred_Archer_Fader, Skeleton_aspect, BlobAspect, " +
            "Skeleton_Friendly, Mistile, TentaRoot, Tendril, Tendril_back, DvergerTest, TrainingDummy, " +
            "Hive, TheHive, Goblin_Gem, Ghost_Void, Ghost_old, TentaRoot_wild, FrozenKing_0, FrozenKing_p2, " +
            "Aspect_Bonemass, Aspect_Eikthyr, Aspect_Elder, Aspect_Fader, Aspect_Moder, Aspect_SeekerQueen, Aspect_TentaRoot, Aspect_Yagluth";

        internal static HashSet<string> ParseNames(string value)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in (value ?? "").Split(','))
                if (part.Trim().Length > 0) names.Add(part.Trim());
            return names;
        }

        // auto-add for creatures in no list (modded ones). fitted offline to the hand-made vanilla classes
        // (research\calibrate.py: 58 of 104 exact, 101 of 104 within one class, misses lean to the lower class)

        // health of a typical Common enemy per tier (median of the vanilla Common list)
        internal static readonly float[] BaselineHealth = { 30f, 40f, 100f, 75f, 175f, 200f, 400f, 175f };

        // health against the baseline from which a creature is Common, Uncommon, Strong, Rare
        internal static readonly float[] ClassRatios = { 0.4f, 1.1f, 2.5f, 20f };

        // spawn rate (spawns per hour of one world spawner) from which a creature is at most Common, and at most Swarm
        internal const float CommonRate = 50f;
        internal const float SwarmRate = 300f;

        // health of the vanilla bosses, tier 0 Eikthyr to tier 7 the Deep North boss's last phase
        internal static readonly float[] BossHealth = { 500f, 2500f, 5000f, 7500f, 10000f, 12500f, 25000f, 30000f };

        // tier of a biome flag set, the lowest if several. Meadows 0, Black Forest 1, Swamp 2, Ocean 2, Mountains 3,
        // Plains 4, Mistlands 5, Ashlands 6, Deep North 7. -1 for none
        internal static int BiomeTier(int biomes)
        {
            int[] flags = { 1, 8, 2, 256, 4, 16, 512, 32, 64 };
            int[] tiers = { 0, 1, 2, 2, 3, 4, 5, 6, 7 };
            for (int i = 0; i < flags.Length; i++)
                if ((biomes & flags[i]) != 0) return tiers[i];
            return -1;
        }

        // the highest tier whose vanilla boss has no more health than this one
        internal static int BossHealthTier(float health)
        {
            int tier = 0;
            for (int t = 0; t < BossHealth.Length; t++)
                if (health >= BossHealth[t]) tier = t;
            return tier;
        }

        // spawns per hour of one world spawner entry
        internal static float SpawnRate(float interval, float chance, int groupMin, int groupMax) =>
            interval <= 0f ? 0f : 3600f / interval * chance / 100f * (groupMin + groupMax) / 2f;

        // class index 0 Swarm to 4 Rare from health against the tier's baseline, capped by how often it spawns
        internal static int AutoClass(float health, int tier, float rate)
        {
            float ratio = health / BaselineHealth[Math.Max(0, Math.Min(MaxTier, tier))];
            int cls = 0;
            foreach (var r in ClassRatios)
                if (ratio >= r) cls++;
            if (rate >= SwarmRate) cls = 0;
            else if (rate >= CommonRate) cls = Math.Min(cls, 1);
            return cls;
        }
    }
}
