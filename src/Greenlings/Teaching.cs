using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Runs on the teacher's client. While greenlings are watching the local player (paid with a
    /// coin, or just curious), the player's actions become demonstrations: take item X somewhere
    /// (source), then put X somewhere else (sink). A complete source->sink pair is a lesson.
    ///
    /// Paid watchers learn it as their job (and, for a minute after, pick up corrections and
    /// extra steps). Curious watchers only pick up simple, harmless chores, and may jump in.
    /// </summary>
    internal static class Teaching
    {
        private static Dictionary<string, int> _lastCounts;
        private static Container _lastContainer;
        private static Endpoint _source;
        private static string _sourceItem;
        private static float _sourceTime;
        private static readonly HashSet<Greenling> _watchersLastFrame = new HashSet<Greenling>();

        /// <summary>Everyone paying attention to this player: watching, or just taught and still attentive.</summary>
        private static IEnumerable<Greenling> Watchers(Player p) =>
            Greenling.All.Where(g => g && g.IsGreenling && g.Teacher == p.GetZDOID() && (g.State == GreenlingState.Watching || g.Attentive)
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
                    ? $"{gone.GetName()} gets on with it" : $"{gone.GetName()} loses interest");
            }
            _watchersLastFrame.Clear();
            foreach (var w in watchers) _watchersLastFrame.Add(w);

            if (watchers.Count == 0) { _lastCounts = null; _source = null; return; }

            ScanContainer();
        }

        /// <summary>
        /// Container moves have many UI code paths, so diff the player's inventory while a chest is
        /// open. Also runs just before the chest UI closes, so a take-and-close in one frame counts.
        /// </summary>
        public static void ScanContainer()
        {
            var p = Player.m_localPlayer;
            if (!p || !Active) { _lastCounts = null; return; }
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

        private static Pickable _picked;
        private static float _pickedTime;

        public static void NotePicked(Pickable p)
        {
            _picked = p;
            _pickedTime = Time.time;
        }

        /// <summary>An item just picked from a bush counts as "harvest this kind of plant around here".</summary>
        public static bool PickableSourceFor(string item, out Endpoint area)
        {
            area = null;
            if (!_picked || Time.time - _pickedTime > 5f || _picked.m_itemPrefab.name != item) return false;
            area = Endpoint.Pickables(_picked.transform.position, Utils.GetPrefabName(_picked.gameObject));
            return true;
        }

        /// <summary>Something worth watching happened: watchers stay interested a little longer.</summary>
        private static void Attend()
        {
            foreach (var g in Watchers(Player.m_localPlayer).Where(w => w.State == GreenlingState.Watching).ToList())
                g.NView.InvokeRPC("GL_Attend");
        }

        /// <summary>The player hit something breakable: watching brutes take that as their lesson.</summary>
        public static void Whack(Endpoint area)
        {
            if (!Active) return;
            Attend();
            var task = new LaborTask { Item = "", Source = area, Sink = Endpoint.LeaveIt(area.Pos) };
            Teach(task);
        }

        public static void Source(Endpoint where, string item)
        {
            if (!Active) return;
            _source = where;
            _sourceItem = item;
            _sourceTime = Time.time;
            Plugin.Log.LogInfo($"[GLEVT] demo_source item={item} at={where}");
            Attend();

            // picking berries in front of a curious greenling: it may run off and pick some for you
            if (where.Kind == EndpointKind.PickableArea)
            {
                var forage = new LaborTask { Item = item, Source = where, Sink = Endpoint.ToPlayer(Player.m_localPlayer) };
                foreach (var g in Watchers(Player.m_localPlayer).Where(w => w.CasualWatch).ToList())
                {
                    if (!Roles.CanLearn(g.Role, forage, out _)) continue;
                    Plugin.Log.LogInfo($"[GLEVT] lesson gl={g.Label} casual=true task=\"{forage}\"");
                    g.NView.InvokeRPC("GL_Habit", forage.Serialize());
                }
            }
        }

        private static float _lastFieldLesson = -100f;

        public static void Sink(Endpoint where, string item)
        {
            if (!Active) return;
            Plugin.Log.LogInfo($"[GLEVT] demo_sink item={item} at={where}");
            Attend();
            if (where.Kind == EndpointKind.Field && (_source == null || _sourceItem != item) && Time.time - _lastFieldLesson > 20f)
            {
                // planting straight from your pockets: the seeds come from the chest nearby that keeps them
                var chest = Sinks.Chests(where.Pos, Plugin.WorkRadius.Value)
                    .Where(c => c.GetInventory().CountItems(Items.SharedName(item)) > 0)
                    .OrderBy(c => Vector3.Distance(c.transform.position, where.Pos)).FirstOrDefault();
                if (chest) { _source = Endpoint.For(EndpointKind.Container, chest); _sourceItem = item; _sourceTime = Time.time; }
            }
            if (_source == null || _sourceItem != item || Time.time - _sourceTime > Plugin.DemoWindow.Value || _source.SameAs(where))
                return; // incomplete demonstration (e.g. just tidying the chest)

            Teach(new LaborTask { Item = item, Source = _source, Sink = where });
            if (where.Kind == EndpointKind.Field) _lastFieldLesson = Time.time;
            _source = null;
        }

        private static void Teach(LaborTask task)
        {
            foreach (var g in Watchers(Player.m_localPlayer).ToList())
            {
                if (!Roles.CanLearn(g.Role, task, out var why))
                {
                    if (g.CasualWatch) continue; // it was only idly watching
                    Plugin.Log.LogInfo($"[GLEVT] lesson_refused gl={g.Label} role={Roles.Name(g.Role)} reason=\"{why}\" task=\"{task}\"");
                    Player.m_localPlayer.Message(MessageHud.MessageType.Center, $"{g.GetName()} stares at you, baffled. ({why})");
                    continue;
                }
                if (g.CasualWatch)
                {
                    if (!Chores.NoRegret(task)) { Plugin.Log.LogInfo($"[GLEVT] lesson_ignored gl={g.Label} reason=not_a_simple_chore task=\"{task}\""); continue; }
                    Plugin.Log.LogInfo($"[GLEVT] lesson gl={g.Label} casual=true task=\"{task}\"");
                    g.NView.InvokeRPC("GL_Habit", task.Serialize());
                    continue;
                }
                Plugin.Log.LogInfo($"[GLEVT] lesson gl={g.Label} task=\"{task}\"");
                g.NView.InvokeRPC("GL_Learn", task.Serialize());
            }
        }
    }

    // ---- hooks feeding Teaching ----------------------------------------------------------------

    [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
    internal static class ContainerCloseHook
    {
        private static void Prefix() => Teaching.ScanContainer();
    }

    /// <summary>Items picked up: from a producer station if one is right there, else from the ground.</summary>
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
            if (!__result || !__state.HasValue || !Teaching.Active) return;
            var (item, pos) = __state.Value;
            if (Teaching.PickableSourceFor(item, out var area)) Teaching.Source(area, item);
            else Teaching.Source(Breakables.SourceFor(pos) ?? Sources.ProducerFor(pos, item) ?? Endpoint.Ground(pos), item);
        }
    }

    /// <summary>Dropping something on the ground (e.g. food for the animals) is a place to put things.</summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.DropItem))]
    internal static class DropHook
    {
        private static void Postfix(Humanoid __instance, ItemDrop.ItemData item, bool __result)
        {
            if (!__result || __instance != Player.m_localPlayer || !Teaching.Active || item?.m_dropPrefab == null) return;
            var p = (Player)__instance;
            Teaching.Sink(Endpoint.GroundDrop(p.transform.position + p.transform.forward * 0.8f), item.m_dropPrefab.name);
        }
    }

    /// <summary>Picking a bush/mushroom/crop: remember it so the drops it spawns count as "harvest here".</summary>
    [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
    internal static class PickableHook
    {
        private static void Postfix(Pickable __instance, Humanoid character, bool __result)
        {
            if (__result && character == Player.m_localPlayer && __instance.m_itemPrefab)
                Teaching.NotePicked(__instance);
        }
    }

    /// <summary>
    /// Every way a player feeds a station ends in one of a few add-item RPCs, so watch those
    /// instead of each station's UI code paths.
    /// </summary>
    [HarmonyPatch(typeof(ZNetView), nameof(ZNetView.InvokeRPC), typeof(string), typeof(object[]))]
    internal static class StationRpcHook
    {
        private static void Prefix(ZNetView __instance, string method, object[] parameters)
        {
            if (GreenlingBrain.Acting || !Teaching.Active || !__instance || !__instance.IsValid()) return;
            if (method != "RPC_AddOre" && method != "RPC_AddFuel" && method != "RPC_AddItem") return;
            if (Sinks.FromRpc(__instance, method, parameters, out var sink, out var item)) Teaching.Sink(sink, item);
        }
    }
}
