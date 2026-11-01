using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Observability. gl_dump prints one machine-readable line per greenling / station nearby:
    ///   [GLDUMP] {"type":"greenling",...}
    /// Designed for scripts (dev.ps1) and for players filing bug reports.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class Commands
    {
        private static void Postfix()
        {
            new Terminal.ConsoleCommand("gl_dump", "[radius=40] Greenlings: dump greenlings, greylings and stations near you as JSON lines", args =>
            {
                var player = Player.m_localPlayer;
                if (!player) { args.Context.AddString("gl_dump: no local player"); return; }
                var radius = args.TryParameterFloat(1, 40f);
                var lines = Dump(player.transform.position, radius).ToList();
                foreach (var l in lines) Plugin.Log.LogInfo("[GLDUMP] " + l);
                args.Context.AddString($"gl_dump: {lines.Count} objects within {radius}m (see log)");
            });

            new Terminal.ConsoleCommand("gl_burst", "play the burst of understanding on the nearest greenling (for tuning the effect)", args =>
            {
                var p = Player.m_localPlayer;
                var g = Greenling.All.Where(x => x.IsGreenling).OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                if (!g) { args.Context.AddString("gl_burst: no greenling nearby"); return; }
                g.NView.InvokeRPC(ZRoutedRpc.Everybody, "GL_Burst");
                args.Context.AddString("gl_burst: " + g.Label);
            }, isCheat: true);

            new Terminal.ConsoleCommand("gl_prank", "[zoomies|misplace|scatter] make the nearest idle greenling misbehave now", args =>
            {
                var p = Player.m_localPlayer;
                var g = Greenling.All.Where(x => x.IsGreenling && x.IsOwner).OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                if (!g) { args.Context.AddString("gl_prank: no greenling you own nearby"); return; }
                g.Brain.StartPrank(args.Length > 1 ? args[1] : null);
                args.Context.AddString("gl_prank: " + g.Label);
            }, isCheat: true);
        }

        public static IEnumerable<string> Dump(Vector3 at, float radius)
        {
            var chars = new List<Character>();
            Character.GetCharactersInRange(at, radius, chars);
            foreach (var c in chars.Where(c => c.GetComponent<Greenling>()))
            {
                var g = c.GetComponent<Greenling>();
                var ai = g.AI;
                var j = new Json()
                    .S("type", g.IsGreenling ? "greenling" : "greyling")
                    .S("id", g.Zdo?.m_uid.ID.ToString() ?? "?")
                    .S("name", g.GetName())
                    .V("pos", c.transform.position)
                    .N("hp", c.GetHealth())
                    .B("owner", g.IsOwner);
                if (g.IsGreenling)
                {
                    j.S("state", g.State.ToString()).N("curiosity", g.Curiosity).S("mood", g.MoodWord())
                     .N("attention", g.Attention).S("task", g.Task?.ToString() ?? "")
                     .S("carry", g.CarryItem).N("carryN", g.CarryCount);
                }
                else
                {
                    j.N("tameness", c.GetComponent<Tameable>()?.GetTameness() ?? 0)
                     .B("hungry", c.GetComponent<Tameable>()?.IsHungry() ?? false);
                }
                if (ai)
                {
                    j.S("target", ai.GetTargetCreature() ? Utils.GetPrefabName(ai.GetTargetCreature().gameObject) : "")
                     .B("alerted", ai.IsAlerted())
                     .B("despawnInDay", ai.DespawnInDay()).B("event", ai.IsEventCreature()).B("sleeping", ai.IsSleeping())
                     .S("eating", ai.m_consumeTarget ? $"{Utils.GetPrefabName(ai.m_consumeTarget.gameObject)}@{Vector3.Distance(ai.m_consumeTarget.transform.position, c.transform.position):0.0}m" : "");
                }
                yield return j.ToString();
            }

            foreach (var co in Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                         .Where(x => Vector3.Distance(x.transform.position, at) < radius && x.GetComponent<ZNetView>()?.IsValid() == true))
            {
                var items = string.Join(",", Items.Counts(co.GetInventory()).Select(kv => $"{kv.Key}x{kv.Value}"));
                yield return new Json().S("type", "container").S("prefab", Utils.GetPrefabName(co.gameObject))
                    .S("id", co.GetComponent<ZNetView>().GetZDO().m_uid.ID.ToString()).V("pos", co.transform.position)
                    .B("inUse", co.IsInUse()).S("items", items).ToString();
            }

            foreach (var f in Near<Fireplace>(at, radius))
                yield return Station("fire", f).N("fuel", f.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel)).N("maxFuel", f.m_maxFuel)
                    .S("fuelItem", f.m_fuelItem ? f.m_fuelItem.name : "").ToString();
            foreach (var k in Near<CookingStation>(at, radius))
            {
                var slots = string.Join(",", Enumerable.Range(0, k.m_slots.Length).Select(i =>
                {
                    k.GetSlot(i, out var n, out _, out var st, out _);
                    return n == "" ? "-" : n + ":" + st;
                }));
                yield return Station("cooking", k).S("slots", slots).B("fireLit", !k.m_requireFire || k.IsFireLit()).ToString();
            }
            foreach (var h in Near<Beehive>(at, radius)) yield return Station("beehive", h).N("honey", h.GetHoneyLevel()).ToString();
            foreach (var c in Near<SapCollector>(at, radius)) yield return Station("sap", c).N("level", c.GetLevel()).ToString();
            foreach (var f in Near<Fermenter>(at, radius)) yield return Station("fermenter", f).S("status", f.GetStatus().ToString()).ToString();
            var drops = Physics.OverlapSphere(at, radius, LayerMask.GetMask("item"))
                .Select(c => c.attachedRigidbody ? c.attachedRigidbody.GetComponent<ItemDrop>() : null).Where(d => d)
                .GroupBy(d => Utils.GetPrefabName(d.gameObject)).Select(g => g.Key + "x" + g.Sum(d => d.m_itemData.m_stack));
            var where = Physics.OverlapSphere(at, radius, LayerMask.GetMask("item"))
                .Select(c => c.attachedRigidbody ? c.attachedRigidbody.GetComponent<ItemDrop>() : null).Where(d => d).Take(12)
                .Select(d => string.Format(CultureInfo.InvariantCulture, "{0}@{1:0.0},{2:0.0},{3:0.0}", Utils.GetPrefabName(d.gameObject), d.transform.position.x, d.transform.position.y, d.transform.position.z));
            yield return new Json().S("type", "drops").S("items", string.Join(",", drops)).S("where", string.Join(" ", where)).ToString();

            foreach (var s in Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None)
                         .Where(x => Vector3.Distance(x.transform.position, at) < radius && x.m_nview && x.m_nview.IsValid()))
            {
                yield return new Json().S("type", "smelter").S("prefab", Utils.GetPrefabName(s.gameObject))
                    .S("id", s.m_nview.GetZDO().m_uid.ID.ToString()).V("pos", s.transform.position)
                    .N("queue", s.GetQueueSize()).N("maxOre", s.m_maxOre)
                    .N("fuel", s.GetFuel()).N("maxFuel", s.m_maxFuel)
                    .S("fuelItem", s.m_fuelItem ? s.m_fuelItem.name : "").ToString();
            }
        }

        private static IEnumerable<T> Near<T>(Vector3 at, float radius) where T : MonoBehaviour =>
            Object.FindObjectsByType<T>(FindObjectsSortMode.None).Where(x => Vector3.Distance(x.transform.position, at) < radius
                                                                       && x.GetComponent<ZNetView>()?.IsValid() == true);

        private static Json Station(string type, MonoBehaviour m) =>
            new Json().S("type", type).S("prefab", Utils.GetPrefabName(m.gameObject))
                .S("id", m.GetComponent<ZNetView>().GetZDO().m_uid.ID.ToString()).V("pos", m.transform.position);

        /// <summary>Tiny JSON object builder (no dependency on a JSON library).</summary>
        private class Json
        {
            private readonly List<string> _f = new List<string>();
            public Json S(string k, string v) { _f.Add($"\"{k}\":\"{(v ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"")}\""); return this; }
            public Json N(string k, float v) { _f.Add($"\"{k}\":{v.ToString("0.##", CultureInfo.InvariantCulture)}"); return this; }
            public Json B(string k, bool v) { _f.Add($"\"{k}\":{(v ? "true" : "false")}"); return this; }
            public Json V(string k, Vector3 v) => S(k, string.Format(CultureInfo.InvariantCulture, "{0:0.0},{1:0.0},{2:0.0}", v.x, v.y, v.z));
            public override string ToString() => "{" + string.Join(",", _f) + "}";
        }
    }
}
