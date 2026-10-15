using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace IdolDrops
{
    // adds the idol to a creature's drop list. runs on the machine that owns the creature, for both the
    // plain death (CharacterDrop.OnDeath) and the ragdoll path (Ragdoll.SaveLootList)
    [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
    internal static class AddIdol
    {
        private static readonly AccessTools.FieldRef<CharacterDrop, Character> CharacterOf =
            AccessTools.FieldRefAccess<CharacterDrop, Character>("m_character");
        private static readonly AccessTools.FieldRef<Character, HitData> LastHitOf =
            AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");

        // last, so loot mods (Drop That, StarLevelSystem) have changed the list before the idol is added
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(CharacterDrop __instance, List<KeyValuePair<GameObject, int>> __result)
        {
            if (!Plugin.Enabled.Value || __result == null) return;
            var character = CharacterOf(__instance);
            if (!character) return;
            string name = Utils.GetPrefabName(__instance.gameObject);
            string auto = null;
            if (!Plugin.Rules.TryGetValue(name, out var rule) && !AutoRules.TryGet(character, name, out rule, out auto)) return;

            string skip = SkipReason(character, rule.Boss, out string note);
            if (skip == null && __result.Any(d => d.Key && d.Key.name.StartsWith("Upgrader"))) skip = "it already drops an idol";
            if (skip != null)
            {
                if (Plugin.LogDrops.Value) Plugin.Log.LogInfo($"{name}: no idol, {skip}");
                return;
            }

            bool weapon = Random.value * 100f < Plugin.WeaponShare.Value;
            string origin = auto == null ? "" : $" (auto, tier from {auto})";
            if (rule.Boss)
            {
                Add(__result, name, rule.Tier, weapon, "boss" + origin);
                return;
            }

            int stars = Mathf.Clamp(character.GetLevel() - 1, 0, Plugin.MaxStars.Value);
            bool fromEvent = FromEvent(character);
            float chance = DropRules.Chance(rule.Percent, stars, Plugin.ChancePerStar.Value, fromEvent,
                Plugin.EventMultiplier.Value, Plugin.GlobalMultiplier.Value);
            float roll = Random.value;
            string detail = $"{stars} star, tier {rule.Tier}, chance {chance * 100f:0.###}%{(fromEvent ? " (event)" : "")}, roll {roll * 100f:0.###}{origin}{(note == null ? "" : ", " + note)}";
            if (roll < chance)
                Add(__result, name, rule.Tier, weapon, detail);
            else if (Plugin.LogDrops.Value)
                Plugin.Log.LogInfo($"{name}: no idol, {detail}");
        }

        // why this creature gives no idol, or null when it may. note tells when a last hit without attacker counted
        private static string SkipReason(Character character, bool boss, out string note)
        {
            note = null;
            if (character.IsTamed()) return "tamed";
            if (character.m_faction == Character.Faction.PlayerSpawned) return "player summon";
            if (!Plugin.SummonsDrop.Value && SummonMark.IsMarked(character)) return "spawned by another creature";
            if (boss || !Plugin.RequirePlayerKill.Value) return null;
            var hit = LastHitOf(character);
            bool hasAttacker = hit != null && hit.HaveAttacker();
            var attacker = hasAttacker ? hit.GetAttacker() : null;
            return DropRules.PlayerKillSkip(hasAttacker, attacker && attacker.IsPlayer(), hasAttacker ? 0 : PlayersWhoHit(character), out note);
        }

        // how many players damaged the creature, counted by the game itself in Character.RPC_Damage
        private static int PlayersWhoHit(Character character)
        {
            var view = character.GetComponent<ZNetView>();
            var zdo = view ? view.GetZDO() : null;
            return zdo == null ? 0 : zdo.GetInt(ZDOVars.s_attackers, 0);
        }

        // raid creatures, and anything killed inside an active world event such as the Jotun invasion
        private static bool FromEvent(Character character)
        {
            if (character.GetBaseAI() is MonsterAI ai && ai.IsEventCreature()) return true;
            var events = PersistentEventSystem.instance;
            return events && events.GetActiveEvent(character.transform.position) != null;
        }

        private static void Add(List<KeyValuePair<GameObject, int>> drops, string creature, int tier, bool weapon, string detail)
        {
            string idol = DropRules.IdolName(tier, weapon);
            var prefab = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(idol) : null;
            if (!prefab)
            {
                Plugin.Log.LogWarning($"{creature}: idol {idol} not found in the item database");
                return;
            }
            drops.Add(new KeyValuePair<GameObject, int>(prefab, 1));
            if (Plugin.LogDrops.Value) Plugin.Log.LogInfo($"{creature}: drops {idol}, {detail}");
        }
    }

    // marks creatures that other creatures spawn, so they give no idols. the mark lives in the creature's ZDO,
    // so every client sees it and it survives a reload
    internal static class SummonMark
    {
        internal const string Key = "IdolDrops_summoned";

        // UnityEngine.Object.Instantiate<GameObject>(GameObject, Vector3, Quaternion), the call the three spawners use
        internal static readonly MethodInfo Original = typeof(Object).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .First(m => m.Name == nameof(Object.Instantiate) && m.IsGenericMethodDefinition && m.GetParameters().Length == 3
                        && m.GetParameters()[1].ParameterType == typeof(Vector3) && m.GetParameters()[2].ParameterType == typeof(Quaternion))
            .MakeGenericMethod(typeof(GameObject));

        internal static readonly MethodInfo Wrapper = AccessTools.Method(typeof(SummonMark), nameof(Instantiate));

        internal static GameObject Instantiate(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            var spawned = Object.Instantiate(prefab, position, rotation);
            if (spawned && spawned.GetComponent<Character>())
            {
                var view = spawned.GetComponent<ZNetView>();
                var zdo = view ? view.GetZDO() : null;
                if (zdo != null) zdo.Set(Key, true);
            }
            return spawned;
        }

        internal static bool IsMarked(Character character)
        {
            var view = character.GetComponent<ZNetView>();
            var zdo = view ? view.GetZDO() : null;
            return zdo != null && zdo.GetBool(Key, false);
        }

        // swaps every Instantiate call in a spawner for the marking wrapper, labels and blocks kept
        internal static IEnumerable<CodeInstruction> Swap(IEnumerable<CodeInstruction> code)
        {
            foreach (var instruction in code)
            {
                if (instruction.Calls(Original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = Wrapper;
                }
                yield return instruction;
            }
        }
    }

    // boss and creature abilities: Bonemass, Gjall, the Hive, the Seeker Queen, Fader, the Charred Mage
    [HarmonyPatch]
    internal static class MarkAbilitySpawns
    {
        private static MethodBase TargetMethod() => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(SpawnAbility), "Spawn"));
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) => SummonMark.Swap(code);
    }

    // the spawn holes in the Seeker Queen's room
    [HarmonyPatch(typeof(TriggerSpawner), "Spawn")]
    internal static class MarkTriggerSpawns
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) => SummonMark.Swap(code);
    }

    // creatures dropped on death, such as the two blobs an Oozer splits into
    [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.DropItems))]
    internal static class MarkDeathSpawns
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) => SummonMark.Swap(code);
    }
}
