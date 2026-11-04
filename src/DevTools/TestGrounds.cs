using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace Greenlings.DevTools
{
    /// <summary>Fixed coordinates, persistent fixture names, and renderer-independent evidence.</summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class TestGrounds
    {
        internal const string Tag = "gldev_fixture";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Dictionary<string, Vector3> Bays = new Dictionary<string, Vector3>
        {
            ["haul"] = new Vector3(-14, 0, -14), ["machines"] = new Vector3(14, 0, -14),
            ["doors"] = new Vector3(-14, 0, 14), ["social"] = new Vector3(14, 0, 14)
        };
        private static bool _building;
        private static bool _tracing;
        private static string AnchorKey => "World" + ZNet.instance.GetWorld().m_uid;
        private static BepInEx.Configuration.ConfigEntry<Vector3> Anchor =>
            DevToolsPlugin.Instance.Config.Bind("TestGrounds", AnchorKey, Vector3.zero, "Fixed platform origin for this dev world.");
        private static Vector3 Origin => Anchor.Value;
        private static string V(Vector3 p) => string.Format(Inv, "{0:0.0},{1:0.0},{2:0.0}", p.x, p.y, p.z);
        private static string Clean(string s) => (s ?? "").Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
        internal static bool Busy => _building;
        internal static bool BuildSucceeded { get; private set; }
        private static IEnumerable<ZNetView> Fixtures() => UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None)
            .Where(v => v && v.IsValid() && v.GetZDO().GetString(Tag).Length > 0);

        internal static GameObject Resolve(string selector)
        {
            var matches = Fixtures().Where(v => v.GetZDO().GetString(Tag) == selector.TrimStart('@')).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException($"fixture {selector}: expected one loaded object, found {matches.Length}; visit the grounds first");
            return matches[0].gameObject;
        }

        private static void Guard()
        {
            if (!Player.m_localPlayer || !ZNet.instance || ZNet.instance.GetWorldName() != DevToolsPlugin.WorldName.Value)
                throw new InvalidOperationException("test grounds require the configured dev world and a local player");
            if (_building) throw new InvalidOperationException("test grounds are still building");
        }

        private static void Postfix()
        {
            new Terminal.ConsoleCommand("gldev_arena", "build | reset | optimize | perf | clearview | goto [haul|machines|doors|social] | snapshot | trace [seconds=10] [interval=1]", a =>
            {
                Guard();
                var op = a.Length > 1 ? a[1] : "snapshot";
                switch (op)
                {
                    case "build":
                    case "reset":
                        if (_tracing) throw new InvalidOperationException("wait for the active trace before resetting the grounds");
                        DevToolsPlugin.Instance.StartCoroutine(Build()); break;
                    case "goto":
                        if (Origin == Vector3.zero) throw new InvalidOperationException("build test grounds first");
                        MovePlayer(Origin + (a.Length > 2 ? Bays[a[2]] : Vector3.zero) + Vector3.up * 1.5f);
                        break;
                    case "snapshot": Snapshot(); break;
                    case "clearview": ClearVegetation(); break;
                    case "optimize":
                        if (_tracing) throw new InvalidOperationException("wait for the trace before optimizing");
                        DevToolsPlugin.Instance.StartCoroutine(OptimizeDeck()); break;
                    case "perf": DevToolsPlugin.Instance.StartCoroutine(MeasurePerformance()); break;
                    case "trace":
                        var duration = a.TryParameterFloat(2, 10); var interval = a.TryParameterFloat(3, 1);
                        if (_tracing || float.IsNaN(duration) || float.IsNaN(interval) || duration <= 0 || duration > 300 || interval < 0.2f || interval > duration)
                            throw new ArgumentException("one trace at a time; duration 0..300, interval 0.2..duration");
                        _tracing = true;
                        DevToolsPlugin.Instance.StartCoroutine(Trace(duration, interval)); break;
                    default: throw new ArgumentException("unknown arena operation " + op);
                }
            });
        }

        private static void MovePlayer(Vector3 p)
        {
            var player = Player.m_localPlayer;
            player.transform.SetPositionAndRotation(p, Quaternion.identity);
            player.m_body.position = p;
            player.m_body.rotation = Quaternion.identity;
            player.m_body.linearVelocity = Vector3.zero;
        }

        private static GameObject Spawn(string id, string prefab, Vector3 p, float yaw = 0)
        {
            var go = UnityEngine.Object.Instantiate(ZNetScene.instance.GetPrefab(prefab), p, Quaternion.Euler(0, yaw, 0));
            go.GetComponent<ZNetView>().GetZDO().Set(Tag, id);
            var wear = go.GetComponent<WearNTear>();
            if (wear) { wear.m_noSupportWear = false; wear.m_noRoofWear = false; }
            return go;
        }

        // Include a 16m border: trees outside the floor can still put their canopy over it.
        private static ZNetView[] SiteVegetation()
        {
            if (Origin == Vector3.zero) throw new InvalidOperationException("build test grounds first");
            var origin = Origin;
            return UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None)
                .Where(v => v && v.IsValid() && Mathf.Abs(v.transform.position.x - origin.x) <= 40
                    && Mathf.Abs(v.transform.position.z - origin.z) <= 40
                    && v.GetZDO().GetString(Tag).Length == 0
                    && (v.GetComponent<TreeBase>() || v.GetComponent<TreeLog>()
                        || v.GetComponent<Destructible>()?.m_destructibleType == DestructibleType.Tree))
                .ToArray();
        }

        private static void ClearVegetation()
        {
            if (!ZNet.instance || ZNet.instance.GetWorldName() != DevToolsPlugin.WorldName.Value)
                throw new InvalidOperationException("vegetation clearing is restricted to the configured dev world");
            var trees = SiteVegetation();
            foreach (var tree in trees)
            {
                if (!tree.IsOwner()) tree.ClaimOwnership();
                // No falling trunks or dropped wood contaminating scenarios.
                ZNetScene.instance.Destroy(tree.gameObject);
            }
            DevToolsPlugin.Log.LogInfo($"[GLARENA] cleared vegetation={trees.Length} footprint=80x80 remaining={SiteVegetation().Length}");
        }

        private static IEnumerator Build()
        {
            _building = true;
            BuildSucceeded = false;
            var ready = false;
            try
            {
                var required = new[] { "stone_floor_2x2", "piece_chest_wood", "piece_chest", "smelter", "charcoal_kiln", "woodwall", "wood_door", "Greyling", "Greydwarf", "Greydwarf_Elite", "Greydwarf_Shaman" };
                foreach (var name in required)
                    if (!ZNetScene.instance.GetPrefab(name)) throw new InvalidOperationException("missing arena prefab " + name);
                var origin = Origin;
                if (origin == Vector3.zero)
                {
                    origin = Player.m_localPlayer.transform.position;
                    origin.x = Mathf.Round(origin.x); origin.z = Mathf.Round(origin.z);
                    var top = origin.y;
                    for (var x = -24; x <= 24; x += 4)
                        for (var z = -24; z <= 24; z += 4)
                        { ZoneSystem.instance.GetGroundHeight(origin + new Vector3(x, 0, z), out var h); top = Mathf.Max(top, h); }
                    origin.y = Mathf.Ceil(top) + 6;
                    Anchor.Value = origin;
                }
                MovePlayer(origin + Vector3.up * 3);
                var deadline = Time.realtimeSinceStartup + 30;
                while (!ZNetScene.instance.IsAreaReady(origin))
                {
                    if (Time.realtimeSinceStartup > deadline) throw new InvalidOperationException("arena area did not load");
                    yield return null;
                }
                yield return new WaitForSeconds(1);
                ClearVegetation();
                // Reset fixtures without using killall/removedrops.
                foreach (var v in Fixtures().ToArray())
                { if (!v.IsOwner()) v.ClaimOwnership(); ZNetScene.instance.Destroy(v.gameObject); }
                yield return null;
                yield return BuildDeck();
                foreach (var bay in Bays)
                {
                    var at = origin + bay.Value + Vector3.up * 0.3f;
                    Spawn(bay.Key + "/source", "piece_chest_wood", at + new Vector3(-4, 0, -3));
                    Spawn(bay.Key + "/sink", "piece_chest", at + new Vector3(4, 0, bay.Key == "doors" ? 4 : -3));
                }
                Spawn("machines/smelter", "smelter", origin + Bays["machines"] + new Vector3(-3, 0.3f, 3));
                Spawn("machines/kiln", "charcoal_kiln", origin + Bays["machines"] + new Vector3(4, 0.3f, 3));
                for (var x = -6; x <= 6; x += 2)
                    Spawn("doors/barrier/" + x, x == 0 ? "wood_door" : "woodwall", origin + Bays["doors"] + new Vector3(x, 0.3f, 0));
                MovePlayer(origin + Vector3.up * 1.5f);
                ready = BuildSucceeded = true;
            }
            finally
            {
                _building = false;
                DevToolsPlugin.Log.LogInfo(ready ? "[GLARENA] ready origin=" + V(Origin) + " size=48x48 bays=haul,machines,doors,social" : "[GLARENA] FAILED build; reset to retry");
            }
        }

        internal const string ScaleKey = "gldev_scale";

        private static void Scale(GameObject go, Vector3 scale)
        {
            go.GetComponent<ZNetView>().GetZDO().Set(ScaleKey, scale);
            go.transform.localScale = scale;
        }

        private static IEnumerator BuildDeck()
        {
            for (var x = -24; x < 24; x += 8)
            {
                for (var z = -24; z < 24; z += 8)
                    Scale(Spawn($"floor/{x}/{z}", "stone_floor_2x2", Origin + new Vector3(x + 4, 0, z + 4)), new Vector3(4, 1, 4));
                yield return null;
            }
            for (var offset = -20; offset < 24; offset += 8)
            {
                Scale(Spawn("edge/n/" + offset, "woodwall", Origin + new Vector3(offset, 0.3f, 24)), new Vector3(4, 1, 1));
                Scale(Spawn("edge/s/" + offset, "woodwall", Origin + new Vector3(offset, 0.3f, -24)), new Vector3(4, 1, 1));
                Scale(Spawn("edge/e/" + offset, "woodwall", Origin + new Vector3(24, 0.3f, offset), 90), new Vector3(4, 1, 1));
                Scale(Spawn("edge/w/" + offset, "woodwall", Origin + new Vector3(-24, 0.3f, offset), 90), new Vector3(4, 1, 1));
            }
        }

        private static IEnumerator OptimizeDeck()
        {
            if (Origin == Vector3.zero) throw new InvalidOperationException("build test grounds first");
            _building = true;
            BuildSucceeded = false;
            try
            {
                var deck = Fixtures().Where(v => v.GetZDO().GetString(Tag).StartsWith("floor/") || v.GetZDO().GetString(Tag).StartsWith("edge/")).ToArray();
                foreach (var v in deck) { if (!v.IsOwner()) v.ClaimOwnership(); ZNetScene.instance.Destroy(v.gameObject); }
                yield return BuildDeck();
                BuildSucceeded = true;
                DevToolsPlugin.Log.LogInfo($"[GLARENA] optimized deck old={deck.Length} new=60 actors-and-inventories=preserved");
            }
            finally { _building = false; }
        }

        private static IEnumerator MeasurePerformance()
        {
            var frames = new List<float>();
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var startCpu = process.TotalProcessorTime.TotalSeconds;
            var start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 3)
            {
                yield return null;
                frames.Add(Time.unscaledDeltaTime * 1000);
            }
            frames.Sort();
            var elapsed = Time.realtimeSinceStartup - start;
            var fixtures = Fixtures().ToArray();
            var actors = fixtures.Count(v => v.GetComponent<Character>());
            var report = string.Format(Inv, "[GLPERF] fps={0:0.0} frameMeanMs={1:0.0} frameP95Ms={2:0.0} cpuCores={3:0.00} memoryMB={4:0} fixtures={5} arenaActors={6} loadedCharacters={7} fpsCap={8}",
                frames.Count / elapsed, frames.Average(), frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.95f))],
                (process.TotalProcessorTime.TotalSeconds - startCpu) / elapsed, process.WorkingSet64 / 1048576d,
                fixtures.Length, actors, Character.GetAllCharacters().Count, Application.targetFrameRate);
            DevToolsPlugin.Log.LogInfo(report);
            File.WriteAllText(Path.Combine(Paths.BepInExRootPath, "devtools", "arena-perf.txt"), report);
            process.Dispose();
        }

        internal static void SpawnActor(string id, string prefab, string bay, float x, float z)
        {
            Guard();
            if (Origin == Vector3.zero || !Bays.ContainsKey(bay)) throw new ArgumentException("build grounds and choose a known bay");
            if (Fixtures().Any(v => v.GetZDO().GetString(Tag) == id)) throw new ArgumentException("duplicate fixture " + id);
            if (!new[] { "Greyling", "Greydwarf", "Greydwarf_Elite", "Greydwarf_Shaman" }.Contains(prefab)) throw new ArgumentException("unsupported actor prefab");
            var actor = Spawn(id, prefab, Origin + Bays[bay] + new Vector3(x, 1, z));
            actor.GetComponent<Tameable>().Tame();
            actor.GetComponent<ZNetView>().GetZDO().Set("gl_bond", 100f);
            actor.GetComponent<ZNetView>().GetZDO().Set("gl_traits", "none");
            actor.GetComponent<ZNetView>().GetZDO().Set("gl_last_fed", ZNet.instance.GetTime().Ticks);
            actor.GetComponent<Tameable>().SetText(id);
        }

        internal static string Snapshot(bool emit = true)
        {
            Guard();
            var rows = new List<string> { "[GLARENA] snapshot t=" + Time.time.ToString("0.0", Inv) + " origin=" + V(Origin) + " xyz=relative metres; path=remaining corners",
                "[GLARENA] site|vegetation=" + (Origin == Vector3.zero ? "unbuilt" : SiteVegetation().Length.ToString(Inv)) + "|clearance=80x80" };
            foreach (var v in Fixtures().OrderBy(v => v.GetZDO().GetString(Tag), StringComparer.Ordinal))
            {
                var id = v.GetZDO().GetString(Tag);
                if (id.StartsWith("floor/") || id.StartsWith("edge/")) continue;
                var row = "[GLARENA] " + id + "|" + Utils.GetPrefabName(v.gameObject) + "|p=" + V(v.transform.position - Origin);
                var co = v.GetComponent<Container>();
                if (co) row += "|items=" + string.Join(",", co.GetInventory().GetAllItems().GroupBy(i => i.m_dropPrefab.name).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Key + ":" + g.Sum(i => i.m_stack)));
                var g = v.GetComponent<Greenling>();
                if (g)
                {
                    var visuals = g.GetComponentsInChildren<Transform>(true);
                    row += "|runtimePlants=" + visuals.Count(t => t.name.StartsWith("GreenlingSprout_"))
                        + "|assetDecor=" + visuals.Count(t => t.name.StartsWith("gl_decor_"));
                    row += "|state=" + g.State + "|doing=" + Clean(g.Brain.Doing()) + "|carry=" + g.CarryItem + ":" + g.CarryCount + "|bond=" + g.Bond.ToString("0", Inv) + "|hp=" + g.Character.GetHealth().ToString("0", Inv);
                    var ai = g.AI;
                    row += ai.m_lastFindPathTime <= 0 ? "|goal=-|path=not_requested" :
                        "|goal=" + V(ai.m_lastFindPathTarget - Origin) + "|pathOK=" + ai.m_lastFindPathResult + "|pathAge=" + (Time.time - ai.m_lastFindPathTime).ToString("0.0", Inv)
                        + "|path=" + string.Join(";", ai.m_path.Take(16).Select(p => V(p - Origin))) + (ai.m_path.Count > 16 ? ";..." : "");
                }
                var door = v.GetComponent<Door>();
                if (door) row += "|door=" + v.GetZDO().GetInt("state");
                var smelter = v.GetComponent<Smelter>();
                if (smelter) row += "|queue=" + smelter.GetQueueSize() + "|fuel=" + smelter.GetFuel().ToString("0.0", Inv);
                rows.Add(row);
            }
            var result = string.Join("\n", rows);
            File.WriteAllText(Path.Combine(Paths.BepInExRootPath, "devtools", "arena-latest.txt"), result);
            if (emit) foreach (var row in rows) DevToolsPlugin.Log.LogInfo(row);
            return result;
        }

        private static IEnumerator Trace(float duration, float interval)
        {
            var path = Path.Combine(Paths.BepInExRootPath, "devtools", "arena-trace.txt");
            var previous = new HashSet<string>();
            try
            {
                File.WriteAllText(path, "");
                var end = Time.realtimeSinceStartup + duration;
                do
                {
                    var rows = Snapshot(false).Split('\n');
                    var delta = rows.Where(r => !previous.Contains(r)).ToArray();
                    previous = new HashSet<string>(rows);
                    foreach (var row in delta) DevToolsPlugin.Log.LogInfo(row);
                    File.AppendAllText(path, string.Join("\n", delta) + "\n");
                    yield return new WaitForSecondsRealtime(interval);
                }
                while (Time.realtimeSinceStartup < end);
                DevToolsPlugin.Log.LogInfo("[GLARENA] trace done " + path);
            }
            finally { _tracing = false; }
        }

        internal static void Assert(string expression, bool emit = true)
        {
            // Deliberately small grammar; no eval, reflection, or locale-dependent comparisons.
            var p = expression.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length != 4) throw new ArgumentException("!assert @fixture field operator value (fields: count:Wood, state, carry, hp, bond, distance:@other)");
            var go = Resolve(p[0]);
            string actual;
            if (p[1].StartsWith("count:")) actual = go.GetComponent<Container>().GetInventory().GetAllItems().Where(i => i.m_dropPrefab.name == p[1].Substring(6)).Sum(i => i.m_stack).ToString(Inv);
            else if (p[1].StartsWith("distance:@")) actual = Vector3.Distance(go.transform.position, Resolve(p[1].Substring(9)).transform.position).ToString(Inv);
            else
            {
                var g = go.GetComponent<Greenling>();
                switch (p[1])
                {
                    case "state": actual = g.State.ToString(); break;
                    case "carry": actual = g.CarryCount.ToString(Inv); break;
                    case "hp": actual = go.GetComponent<Character>().GetHealth().ToString(Inv); break;
                    case "bond": actual = g.Bond.ToString(Inv); break;
                    case "runtimePlants": actual = go.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("GreenlingSprout_")).ToString(Inv); break;
                    case "assetDecor": actual = go.GetComponentsInChildren<Transform>(true).Count(t => t.name.StartsWith("gl_decor_")).ToString(Inv); break;
                    default: throw new ArgumentException("unknown assertion field " + p[1]);
                }
            }
            bool pass;
            if (p[2] == "==" || p[2] == "!=")
            {
                var equal = float.TryParse(actual, NumberStyles.Float, Inv, out var left) && float.TryParse(p[3], NumberStyles.Float, Inv, out var right) ? left == right : actual == p[3];
                pass = p[2] == "==" ? equal : !equal;
            }
            else
            {
                var left = float.Parse(actual, Inv); var right = float.Parse(p[3], Inv);
                switch (p[2]) { case ">=": pass = left >= right; break; case "<=": pass = left <= right; break; case ">": pass = left > right; break; case "<": pass = left < right; break; default: throw new ArgumentException("unknown comparison " + p[2]); }
            }
            if (!pass) throw new InvalidOperationException(expression + "; actual=" + actual);
            if (emit) DevToolsPlugin.Marker("PASS assert " + expression);
        }
    }

    // Floor stability persists after reload without changing the shipped mod or normal pieces.
    [HarmonyPatch(typeof(WearNTear), "UpdateWear")]
    internal static class ArenaFloorWear
    {
        private static void Prefix(WearNTear __instance)
        {
            if ((__instance.GetComponent<ZNetView>()?.GetZDO()?.GetString(TestGrounds.Tag) ?? "").Length == 0) return;
            __instance.m_noSupportWear = false;
            __instance.m_noRoofWear = false;
        }
    }

    [HarmonyPatch(typeof(ZNetView), "Awake")]
    internal static class ArenaFixtureScale
    {
        private static void Postfix(ZNetView __instance)
        {
            var zdo = __instance.GetZDO();
            if (zdo == null || zdo.GetString(TestGrounds.Tag).Length == 0) return;
            var scale = zdo.GetVec3(TestGrounds.ScaleKey, Vector3.zero);
            if (scale != Vector3.zero) __instance.transform.localScale = scale;
        }
    }
}
