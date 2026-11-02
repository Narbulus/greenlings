using System.Collections.Generic;
using System.Linq;
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
            if (!boar)
            {
                Plugin.Log.LogError("prefab setup failed (no Boar prefab to copy taming from)");
                return;
            }
            foreach (var name in Roles.TameablePrefabs)
            {
                var prefab = __instance.GetPrefab(name);
                if (!prefab) { Plugin.Log.LogWarning("missing prefab " + name); continue; }
                var role = Roles.Of(prefab);
                var items = (role == Role.Brute ? Plugin.BruteTameItems.Value : role == Role.Shaman ? Plugin.ShamanTameItems.Value : Plugin.TameItem.Value)
                    .Split(',').Select(s => __instance.GetPrefab(s.Trim())?.GetComponent<ItemDrop>()).Where(d => d).ToList();
                if (items.Count == 0) { Plugin.Log.LogError($"no taming items found for {name}"); continue; }
                MakeTameable(prefab, boar.GetComponent<Tameable>(), items);
            }
        }

        /// <summary>The boar's effects without the boar noises, plus the greyling's own little chirp.</summary>
        private static EffectList Quiet(EffectList boar, EffectList own)
        {
            var visuals = boar.m_effectPrefabs.Where(e => e.m_prefab && !e.m_prefab.GetComponentInChildren<AudioSource>(true) && !e.m_prefab.GetComponentInChildren<ZSFX>(true));
            return new EffectList { m_effectPrefabs = visuals.Concat(own?.m_effectPrefabs ?? new EffectList.EffectData[0]).ToArray() };
        }

        private static void MakeTameable(GameObject greyling, Tameable boarTame, List<ItemDrop> tameItems)
        {
            var tame = greyling.GetComponent<Tameable>() ?? greyling.AddComponent<Tameable>();
            tame.m_tamingTime = Plugin.TamingTime.Value;
            tame.m_fedDuration = Plugin.FedDuration.Value;
            tame.m_commandable = false; // E is Pat (see Interaction)
            var ai = greyling.GetComponent<MonsterAI>();
            tame.m_tamedEffect = boarTame.m_tamedEffect;
            tame.m_sootheEffect = Quiet(boarTame.m_sootheEffect, ai.m_idleSound);
            tame.m_petEffect = Quiet(boarTame.m_petEffect, ai.m_idleSound);

            ai.m_consumeItems = tameItems;
            ai.m_consumeSearchRange = Plugin.CoinSearchRange.Value; // greedy for shiny things (boars: 5m)

            if (!greyling.GetComponent<Greenling>()) greyling.AddComponent<Greenling>();
            if (!greyling.GetComponent<CarryVisual>()) greyling.AddComponent<CarryVisual>();
            Plugin.Log.LogInfo($"{greyling.name} is now tameable with {string.Join("/", tameItems.Select(i => i.name))} ({tame.m_tamingTime}s, fed {tame.m_fedDuration}s per item)");
        }
    }
}
