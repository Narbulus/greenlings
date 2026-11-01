using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Makes the vanilla Greyling prefab tameable the same way boars are: it eats the
    /// configured item off the ground, and taming progresses while it is fed and calm.
    /// Runs on every client (and server) once ZNetScene has registered prefabs.
    /// </summary>
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class PrefabSetup
    {
        private static void Postfix(ZNetScene __instance)
        {
            var boar = __instance.GetPrefab("Boar");
            var tameItem = __instance.GetPrefab(Plugin.TameItem.Value)?.GetComponent<ItemDrop>();
            if (!boar || !tameItem)
            {
                Plugin.Log.LogError($"prefab setup failed (Boar={boar}, {Plugin.TameItem.Value}={tameItem})");
                return;
            }
            foreach (var name in Roles.TameablePrefabs)
            {
                var prefab = __instance.GetPrefab(name);
                if (prefab) MakeTameable(prefab, boar.GetComponent<Tameable>(), tameItem);
                else Plugin.Log.LogWarning("missing prefab " + name);
            }
        }

        private static void MakeTameable(GameObject greyling, Tameable boarTame, ItemDrop tameItem)
        {
            var tame = greyling.GetComponent<Tameable>() ?? greyling.AddComponent<Tameable>();
            tame.m_tamingTime = Plugin.TamingTime.Value;
            tame.m_fedDuration = Plugin.FedDuration.Value;
            tame.m_commandable = false; // E is Pat (see Interaction)
            tame.m_tamedEffect = boarTame.m_tamedEffect;
            tame.m_sootheEffect = boarTame.m_sootheEffect;
            tame.m_petEffect = boarTame.m_petEffect;

            var ai = greyling.GetComponent<MonsterAI>();
            ai.m_consumeItems = new List<ItemDrop> { tameItem };
            ai.m_consumeSearchRange = Plugin.CoinSearchRange.Value; // greedy for shiny things (boars: 5m)

            if (!greyling.GetComponent<Greenling>()) greyling.AddComponent<Greenling>();
            if (!greyling.GetComponent<CarryVisual>()) greyling.AddComponent<CarryVisual>();
            Plugin.Log.LogInfo($"{greyling.name} is now tameable with {tameItem.name} ({tame.m_tamingTime}s, fed {tame.m_fedDuration}s per item)");
        }
    }
}
