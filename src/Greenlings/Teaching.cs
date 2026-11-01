using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Runs on the teacher's client. While any greenling is watching the local player, it turns
    /// the player's actions into a demonstration: take item X somewhere (source), then put X
    /// somewhere else (sink). A complete source->sink pair is sent to the watchers as a lesson.
    /// </summary>
    internal static class Teaching
    {
        private static Dictionary<string, int> _lastCounts;
        private static Container _lastContainer;
        private static Endpoint _source;
        private static string _sourceItem;
        private static float _sourceTime;
        private static readonly HashSet<Greenling> _watchersLastFrame = new HashSet<Greenling>();

        private static IEnumerable<Greenling> Watchers(Player p) =>
            Greenling.All.Where(g => g.IsGreenling && g.State == GreenlingState.Watching && g.Teacher == p.GetZDOID()
                                     && Vector3.Distance(g.transform.position, p.transform.position) < 40f);

        public static bool Active => Player.m_localPlayer && Watchers(Player.m_localPlayer).Any();

        /// <summary>Called every frame from the plugin.</summary>
        public static void Update()
        {
            var p = Player.m_localPlayer;
            if (!p) return;

            var watchers = Watchers(p).ToList();
            foreach (var gone in _watchersLastFrame.Where(g => g && !watchers.Contains(g)).ToList())
            {
                if (gone.State == GreenlingState.Watching) continue;
                p.Message(MessageHud.MessageType.Center, gone.State == GreenlingState.Working
                    ? $"{gone.GetName()} gets to work" : $"{gone.GetName()} loses interest");
            }
            _watchersLastFrame.Clear();
            foreach (var w in watchers) _watchersLastFrame.Add(w);

            if (watchers.Count == 0) { _lastCounts = null; _source = null; return; }

            // Container moves have many UI code paths; diff the inventory while a chest is open.
            var container = InventoryGui.instance ? InventoryGui.instance.m_currentContainer : null;
            var counts = Items.Counts(p.GetInventory());
            if (_lastCounts != null && container && container == _lastContainer)
            {
                foreach (var kv in counts)
                    if (kv.Value > (_lastCounts.TryGetValue(kv.Key, out var before) ? before : 0))
                        Source(Endpoint.For(EndpointKind.Container, container), kv.Key);
                foreach (var kv in _lastCounts)
                    if ((counts.TryGetValue(kv.Key, out var now) ? now : 0) < kv.Value)
                        Sink(Endpoint.For(EndpointKind.Container, container), kv.Key);
            }
            _lastCounts = counts;
            _lastContainer = container;
        }

        public static void Source(Endpoint where, string item)
        {
            if (!Active) return;
            _source = where;
            _sourceItem = item;
            _sourceTime = Time.time;
            Plugin.Log.LogInfo($"[GLEVT] demo_source item={item} at={where}");
        }

        public static void Sink(Endpoint where, string item)
        {
            if (!Active) return;
            Plugin.Log.LogInfo($"[GLEVT] demo_sink item={item} at={where}");
            if (_source == null || _sourceItem != item || Time.time - _sourceTime > Plugin.DemoWindow.Value || _source.SameAs(where))
                return; // incomplete demonstration (e.g. just tidying the chest)

            var task = new LaborTask { Item = item, Source = _source, Sink = where };
            foreach (var g in Watchers(Player.m_localPlayer).ToList())
            {
                Plugin.Log.LogInfo($"[GLEVT] lesson gl={g.Label} task=\"{task}\"");
                g.NView.InvokeRPC("GL_Learn", task.Serialize());
            }
            _source = null;
        }
    }

    // ---- hooks feeding Teaching ----------------------------------------------------------------

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Pickup))]
    internal static class PickupHook
    {
        private static void Prefix(Humanoid __instance, GameObject go, out (string item, Vector3 pos)? __state)
        {
            __state = null;
            if (__instance != Player.m_localPlayer || !go || !go.GetComponent<ItemDrop>()) return;
            __state = (Utils.GetPrefabName(go), go.transform.position);
        }

        private static void Postfix(bool __result, (string item, Vector3 pos)? __state)
        {
            if (__result && __state.HasValue) Teaching.Source(Endpoint.Ground(__state.Value.pos), __state.Value.item);
        }
    }

    [HarmonyPatch(typeof(Smelter), nameof(Smelter.OnAddOre))]
    internal static class SmelterOreHook
    {
        private static void Prefix(Smelter __instance, Humanoid user, ItemDrop.ItemData item, out string __state) =>
            __state = user == Player.m_localPlayer ? Items.PrefabOf(item ?? __instance.FindCookableItem(user.GetInventory())) : null;

        private static void Postfix(Smelter __instance, bool __result, string __state)
        {
            if (__result && __state != null) Teaching.Sink(Endpoint.For(EndpointKind.SmelterOre, __instance), __state);
        }
    }

    [HarmonyPatch(typeof(Smelter), nameof(Smelter.OnAddFuel))]
    internal static class SmelterFuelHook
    {
        private static void Postfix(Smelter __instance, Humanoid user, bool __result)
        {
            if (__result && user == Player.m_localPlayer && __instance.m_fuelItem)
                Teaching.Sink(Endpoint.For(EndpointKind.SmelterFuel, __instance), __instance.m_fuelItem.name);
        }
    }
}
