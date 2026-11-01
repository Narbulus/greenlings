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
            var greyling = __instance.GetPrefab("Greyling");
            var boar = __instance.GetPrefab("Boar");
            var tameItem = __instance.GetPrefab(Plugin.TameItem.Value)?.GetComponent<ItemDrop>();
            if (!greyling || !boar || !tameItem)
            {
                Plugin.Log.LogError($"prefab setup failed (Greyling={greyling}, Boar={boar}, {Plugin.TameItem.Value}={tameItem})");
                return;
            }

            var boarTame = boar.GetComponent<Tameable>();
            var tame = greyling.GetComponent<Tameable>() ?? greyling.AddComponent<Tameable>();
            tame.m_tamingTime = Plugin.TamingTime.Value;
            tame.m_fedDuration = Plugin.FedDuration.Value;
            tame.m_commandable = true; // E toggles follow / stay, like wolves (placeholder until teaching)
            tame.m_tamedEffect = boarTame.m_tamedEffect;
            tame.m_sootheEffect = boarTame.m_sootheEffect;
            tame.m_petEffect = boarTame.m_petEffect;

            var ai = greyling.GetComponent<MonsterAI>();
            ai.m_consumeItems = new List<ItemDrop> { tameItem };

            if (!greyling.GetComponent<Greenling>()) greyling.AddComponent<Greenling>();
            Plugin.Log.LogInfo($"Greyling is now tameable with {tameItem.name} ({tame.m_tamingTime}s, fed {tame.m_fedDuration}s per item)");
        }
    }
}
