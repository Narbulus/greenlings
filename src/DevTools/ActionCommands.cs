using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings.DevTools
{
    /// <summary>
    /// "Play the player" commands so test scripts can exercise real code paths without UI:
    /// open a chest, move items, add ore, pat a creature... Objects are addressed by prefab
    /// name and resolved to the nearest instance.
    /// </summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class ActionCommands
    {
        private static readonly List<GameObject> Placed = new List<GameObject>();
        private static readonly Dictionary<string, GameObject> LastPlaced = new Dictionary<string, GameObject>();

        private static Player P => Player.m_localPlayer;
        private static string PlacedLog => System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "devtools", "placed.txt");
        private static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        private static void Say(Terminal.ConsoleEventArgs a, string s) => a.Context.AddString(s);

        /// <summary>The instance most recently placed by gldev_place, else the nearest one.</summary>
        private static T Nearest<T>(string prefab) where T : Component =>
            LastPlaced.TryGetValue(prefab, out var last) && last && last.GetComponent<T>() ? last.GetComponent<T>() :
            Object.FindObjectsByType<T>(FindObjectsSortMode.None)
                .Where(c => prefab == "*" || Utils.GetPrefabName(c.gameObject) == prefab)
                .OrderBy(c => Vector3.Distance(c.transform.position, P.transform.position))
                .FirstOrDefault();

        /// <summary>AddItem(prefab, n) caps at one stack; add as many stacks as needed.</summary>
        private static void AddStacks(Inventory inv, string item, int n)
        {
            var prefab = ObjectDB.instance.GetItemPrefab(item);
            var max = Mathf.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
            while (n > 0 && inv.AddItem(prefab, Mathf.Min(n, max))) n -= Mathf.Min(n, max);
        }

        private static GameObject NearestObject(string prefab) =>
            LastPlaced.TryGetValue(prefab, out var last) && last ? last :
            Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None)
                .Where(v => v.IsValid() && Utils.GetPrefabName(v.gameObject) == prefab)
                .OrderBy(v => Vector3.Distance(v.transform.position, P.transform.position))
                .Select(v => v.gameObject).FirstOrDefault();

        /// <summary>Step up to a station like a player would (UI auto-closes beyond ~4m).</summary>
        private static void StepTo(Component target) => StepTo(target.transform);

        private static void StepTo(Transform target)
        {
            var to = target.transform.position;
            var dir = P.transform.position - to;
            dir.y = 0;
            var pos = to + (dir.sqrMagnitude > 0.01f ? dir.normalized : Vector3.back) * 2f;
            ZoneSystem.instance.GetGroundHeight(pos, out var y);
            pos.y = y;
            SetPlayerPos(pos, Quaternion.LookRotation(new Vector3(to.x - pos.x, 0, to.z - pos.z)));
        }

        /// <summary>Move the player for real: the rigidbody wins over a bare transform write.</summary>
        private static void SetPlayerPos(Vector3 pos, Quaternion rot)
        {
            P.transform.SetPositionAndRotation(pos, rot);
            P.m_body.position = pos;
            P.m_body.rotation = rot;
            P.m_body.linearVelocity = Vector3.zero;
        }

        private static void Cmd(string name, string help, Terminal.ConsoleEvent action) =>
            new Terminal.ConsoleCommand(name, help, args =>
            {
                if (!P) { Say(args, name + ": no local player"); return; }
                action(args);
            });

        private static System.Collections.IEnumerator RemoveDropsSoon()
        {
            yield return new WaitForSeconds(0.5f);
            Console.instance.TryRunCommand("removedrops", true, true);
        }

        private static void Postfix()
        {
            Cmd("gldev_setup", "[play] tests: god+ghost on, auto-pickup off, empty inventory. 'play': god on, ghost off, auto-pickup on, inventory kept", a =>
            {
                var play = a.Length > 1 && a[1] == "play";
                P.SetGodMode(true);
                P.SetGhostMode(!play);
                Player.m_enableAutoPickup = play; // tests pick things up explicitly
                if (!play) P.GetInventory().RemoveAll();
                Say(a, play ? "setup: god on, ghost off, auto-pickup on" : "setup: god+ghost on, auto-pickup off, inventory emptied");
            });

            Cmd("gldev_goto", "<prefab> walk up to the most recently placed (else nearest) object of that prefab", a =>
            {
                var go = NearestObject(a[1]);
                if (!go) { Say(a, "no " + a[1]); return; }
                StepTo(go.transform);
                Say(a, "at " + a[1]);
            });

            Cmd("gldev_place", "<prefab> <right> <forward> place a prefab relative to the player, snapped to ground", a =>
            {
                var prefab = ZNetScene.instance.GetPrefab(a[1]);
                if (!prefab) { Say(a, "no prefab " + a[1]); return; }
                var t = P.transform;
                var pos = t.position + t.right * a.TryParameterFloat(2) + t.forward * a.TryParameterFloat(3);
                ZoneSystem.instance.GetGroundHeight(pos, out var y);
                pos.y = y;
                var go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(-t.forward));
                Placed.Add(go);
                LastPlaced[a[1]] = go;
                System.IO.File.AppendAllText(PlacedLog, string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1} {2} {3}\n", a[1], pos.x, pos.y, pos.z));
                Say(a, $"placed {a[1]} id={go.GetComponent<ZNetView>()?.GetZDO()?.m_uid.ID} at {pos}");
            });

            Cmd("gldev_clear", "destroy everything placed by gldev_place, kill nearby creatures, remove drops", a =>
            {
                foreach (var go in Placed.Where(g => g)) ZNetScene.instance.Destroy(go);
                Placed.Clear();
                // also objects placed in earlier game sessions (Placed only knows this one)
                var removed = 0;
                if (System.IO.File.Exists(PlacedLog))
                {
                    foreach (var line in System.IO.File.ReadAllLines(PlacedLog))
                    {
                        var p = line.Split(' ');
                        if (p.Length < 4) continue;
                        var at = new Vector3(F(p[1]), F(p[2]), F(p[3]));
                        foreach (var v in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
                            if (v && v.IsValid() && Utils.GetPrefabName(v.gameObject) == p[0] && Vector3.Distance(v.transform.position, at) < 1f)
                            {
                                if (!v.IsOwner()) v.ClaimOwnership();
                                ZNetScene.instance.Destroy(v.gameObject);
                                removed++;
                            }
                    }
                    System.IO.File.Delete(PlacedLog);
                }
                LastPlaced.Clear();
                // and anything a player could have built here (hammer/cultivator pieces), e.g. by hand while testing
                var buildable = new System.Collections.Generic.HashSet<string>(
                    ObjectDB.instance.m_items.Select(i => i.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces)
                        .Where(t => t != null).SelectMany(t => t.m_pieces).Where(x => x).Select(x => x.name));
                foreach (var v in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
                    if (v && v.IsValid() && buildable.Contains(Utils.GetPrefabName(v.gameObject))
                        && Vector3.Distance(v.transform.position, P.transform.position) < 40f)
                    {
                        if (!v.IsOwner()) v.ClaimOwnership();
                        ZNetScene.instance.Destroy(v.gameObject);
                        removed++;
                    }
                Console.instance.TryRunCommand("killall", true, true);
                P.StartCoroutine(RemoveDropsSoon()); // corpses drop loot a moment after dying
                Say(a, $"cleared ({removed} more: earlier sessions / hand-built within 40m)");
            });

            Cmd("gldev_give", "<item> <n> add items to the player's inventory", a =>
            {
                AddStacks(P.GetInventory(), a[1], a.TryParameterInt(2, 1));
                Say(a, $"gave {a[1]} x{a.TryParameterInt(2, 1)}");
            });

            Cmd("gldev_fill", "<containerPrefab> <item> <n> put items straight into the nearest container", a =>
            {
                var c = Nearest<Container>(a[1]);
                if (!c) { Say(a, "no container " + a[1]); return; }
                AddStacks(c.GetInventory(), a[2], a.TryParameterInt(3, 1));
                Say(a, $"filled {a[1]} with {a[2]} x{a.TryParameterInt(3, 1)}");
            });

            Cmd("gldev_open", "<containerPrefab> open the nearest container like pressing E on it", a =>
            {
                var c = Nearest<Container>(a[1]);
                if (!c) { Say(a, "no container " + a[1]); return; }
                StepTo(c);
                c.Interact(P, false, false);
                Say(a, $"opened {a[1]} (player {Vector3.Distance(P.transform.position, c.transform.position):0.0}m away)");
            });

            Cmd("gldev_close", "close the inventory/container UI", a => { InventoryGui.instance.Hide(); Say(a, "closed"); });

            Cmd("gldev_take", "<item> <n> move items from the open container into the player inventory", a =>
            {
                var c = InventoryGui.instance.m_currentContainer;
                if (!c) { Say(a, "no container open"); return; }
                var item = c.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab && i.m_dropPrefab.name == a[1]);
                if (item == null) { Say(a, "container has no " + a[1]); return; }
                var n = Mathf.Min(a.TryParameterInt(2, 1), item.m_stack);
                var slot = P.GetInventory().FindEmptySlot(true);
                P.GetInventory().MoveItemToThis(c.GetInventory(), item, n, slot.x, slot.y);
                Say(a, $"took {a[1]} x{n}");
            });

            Cmd("gldev_put", "<item> <n> move items from the player inventory into the open container", a =>
            {
                var c = InventoryGui.instance.m_currentContainer;
                if (!c) { Say(a, "no container open"); return; }
                var item = P.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab && i.m_dropPrefab.name == a[1]);
                if (item == null) { Say(a, "player has no " + a[1]); return; }
                var n = Mathf.Min(a.TryParameterInt(2, 1), item.m_stack);
                var slot = c.GetInventory().FindEmptySlot(true);
                c.GetInventory().MoveItemToThis(P.GetInventory(), item, n, slot.x, slot.y);
                Say(a, $"put {a[1]} x{n}");
            });

            Cmd("gldev_addore", "<smelterPrefab> <item> add one item to the nearest smelter's ore slot (as the player)", a =>
            {
                var s = Nearest<Smelter>(a[1]);
                var item = P.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab && i.m_dropPrefab.name == a[2]);
                if (!s || item == null) { Say(a, $"need smelter {a[1]} and {a[2]} in inventory"); return; }
                StepTo(s);
                Say(a, "addore " + (s.OnAddOre(null, P, item) ? "ok" : "refused"));
            });

            Cmd("gldev_addfuel", "<smelterPrefab> add one fuel to the nearest smelter (as the player)", a =>
            {
                var s = Nearest<Smelter>(a[1]);
                if (!s) { Say(a, "no smelter " + a[1]); return; }
                StepTo(s);
                Say(a, "addfuel " + (s.OnAddFuel(null, P, null) ? "ok" : "refused"));
            });

            Cmd("gldev_pat", "press E on the nearest tamed creature", a =>
            {
                var t = Nearest<Tameable>("Greyling");
                if (!t) { Say(a, "no greyling"); return; }
                Say(a, "pat " + (t.Interact(P, false, false) ? "ok" : "refused"));
            });

            Cmd("gldev_useitem", "<item> use an item from the inventory on the nearest greyling", a =>
            {
                var t = Nearest<Tameable>("Greyling");
                var item = P.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab && i.m_dropPrefab.name == a[1]);
                if (!t || item == null) { Say(a, $"need a greyling and {a[1]} in inventory"); return; }
                Say(a, "useitem " + (t.UseItem(P, item) ? "ok" : "refused"));
            });

            Cmd("gldev_run", "<script> run a script from the DevTools scripts folder (e.g. testbed)", a =>
            {
                if (a.Length < 2)
                {
                    var names = System.IO.Directory.Exists(DevToolsPlugin.ScriptDir)
                        ? System.IO.Directory.GetFiles(DevToolsPlugin.ScriptDir, "*.txt").Select(System.IO.Path.GetFileNameWithoutExtension).Where(n => !n.StartsWith("_"))
                        : Enumerable.Empty<string>();
                    Say(a, "scripts: " + string.Join(", ", names));
                    return;
                }
                Say(a, DevToolsPlugin.Instance.RunScript(a[1]) ? "running " + a[1] : "no script " + a[1]);
            });

            Cmd("gldev_drop", "<item> <n> <right> <forward> drop n single items on the ground at a spot relative to the player", a =>
            {
                var prefab = ObjectDB.instance.GetItemPrefab(a[1]);
                if (!prefab) { Say(a, "no item " + a[1]); return; }
                var t = P.transform;
                var at = t.position + t.right * a.TryParameterFloat(3) + t.forward * a.TryParameterFloat(4);
                ZoneSystem.instance.GetGroundHeight(at, out var y);
                var n = a.TryParameterInt(2, 1);
                for (var i = 0; i < n; i++)
                {
                    var p = at + new Vector3(Random.Range(-0.8f, 0.8f), 0f, Random.Range(-0.8f, 0.8f));
                    p.y = y + 0.3f + i * 0.05f;
                    Object.Instantiate(prefab, p, Quaternion.Euler(0, Random.Range(0, 360), 0));
                }
                Say(a, $"dropped {n} {a[1]}");
            });

            Cmd("gldev_say", "<text...> show a message in the middle of the screen and in the console", a =>
            {
                var text = a.FullLine.Substring(a.FullLine.IndexOf(' ') + 1);
                P.Message(MessageHud.MessageType.Center, text);
                Say(a, text);
            });

            Cmd("gldev_remove", "<prefab> [radius=40] destroy every instance of a prefab near the player", a =>
            {
                var radius = a.TryParameterFloat(2, 40f);
                var hits = Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None)
                    .Where(v => v && v.IsValid() && Utils.GetPrefabName(v.gameObject) == a[1]
                                && Vector3.Distance(v.transform.position, P.transform.position) < radius).ToList();
                foreach (var v in hits)
                {
                    if (!v.IsOwner()) v.ClaimOwnership();
                    ZNetScene.instance.Destroy(v.gameObject);
                }
                Say(a, $"removed {hits.Count} {a[1]}");
            });

            Cmd("gldev_rename", "<name> rename the nearest tamed greyling like Shift+E would", a =>
            {
                var t = Nearest<Tameable>("Greyling");
                if (!t) { Say(a, "no greyling"); return; }
                t.SetText(a[1]);
                Say(a, "renamed to " + a[1]);
            });

            Cmd("gldev_tame", "instantly tame the nearest greyling", a =>
            {
                var t = Nearest<Tameable>("Greyling");
                if (!t) { Say(a, "no greyling"); return; }
                t.Tame();
                Say(a, "tamed");
            });

            Cmd("gldev_move", "<right> <forward> teleport the player relative to where they stand", a =>
            {
                var t = P.transform;
                var pos = t.position + t.right * a.TryParameterFloat(1) + t.forward * a.TryParameterFloat(2);
                ZoneSystem.instance.GetGroundHeight(pos, out var y);
                pos.y = y;
                SetPlayerPos(pos, P.transform.rotation);
                Say(a, $"moved to {pos}");
            });

            Cmd("gldev_interact", "<prefab> press E on the nearest object of that prefab (pick, extract, tap, take food...)", a =>
            {
                var go = NearestObject(a[1]);
                var it = go ? go.GetComponentInChildren<Interactable>() : null;
                if (it == null) { Say(a, "nothing interactable: " + a[1]); return; }
                StepTo(go.transform);
                Say(a, "interact " + (it.Interact(P, false, false) ? "ok" : "refused"));
            });

            Cmd("gldev_use", "<prefab> <item> use an inventory item on the nearest object of that prefab (hotbar use)", a =>
            {
                var go = NearestObject(a[1]);
                var it = go ? go.GetComponentInChildren<Interactable>() : null;
                var item = P.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab && i.m_dropPrefab.name == a[2]);
                if (it == null || item == null) { Say(a, $"need {a[1]} nearby and {a[2]} in inventory"); return; }
                StepTo(go.transform);
                Say(a, "use " + (it.UseItem(P, item) ? "ok" : "refused"));
            });

            Cmd("gldev_pickup", "<itemPrefab> pick up the nearest loose drop of that item", a =>
            {
                var drop = Nearest<ItemDrop>(a[1]);
                if (!drop) { Say(a, "no drop " + a[1]); return; }
                StepTo(drop.transform);
                Say(a, "pickup " + (P.Pickup(drop.gameObject, false, false) ? "ok" : "refused"));
            });

            Cmd("gldev_ready", "<prefab> make the nearest producer ready now (beehive, sap collector, fermenter)", a =>
            {
                var go = NearestObject(a[1]);
                var zdo = go ? go.GetComponent<ZNetView>()?.GetZDO() : null;
                if (zdo == null) { Say(a, "no " + a[1]); return; }
                if (go.GetComponent<Beehive>() is Beehive h) zdo.Set(ZDOVars.s_level, h.m_maxHoney);
                else if (go.GetComponent<SapCollector>() is SapCollector sc) zdo.Set(ZDOVars.s_level, sc.m_maxLevel);
                else if (go.GetComponent<Fermenter>() is Fermenter f)
                {
                    if (zdo.GetInt(ZDOVars.s_content) == 0) zdo.Set(ZDOVars.s_content, f.m_conversion[0].m_from.name.GetStableHashCode());
                    zdo.Set(ZDOVars.s_startTime, ZNet.instance.GetTime().AddSeconds(-f.m_fermentationDuration - 10).Ticks);
                }
                else { Say(a, "not a producer: " + a[1]); return; }
                Say(a, "ready " + a[1]);
            });

            Cmd("gldev_cfg", "<pluginGuid> <section> <key> <value> change a plugin config entry at runtime", a =>
            {
                if (!BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(a[1], out var info)) { Say(a, "no plugin " + a[1]); return; }
                var entry = info.Instance.Config.Keys.FirstOrDefault(k => k.Section == a[2] && k.Key == a[3]);
                if (entry == null) { Say(a, $"no config {a[2]}.{a[3]}"); return; }
                info.Instance.Config[entry].SetSerializedValue(a[4]);
                Say(a, $"{a[1]} {a[2]}.{a[3]} = {info.Instance.Config[entry].BoxedValue}");
            });

            Cmd("gldev_prefabs", "<substring> list prefab names containing the substring", a =>
            {
                var hits = ZNetScene.instance.m_prefabs.Where(p => p && p.name.ToLower().Contains(a[1].ToLower())).Select(p => p.name).OrderBy(n => n).ToList();
                foreach (var chunk in hits.Select((n, i) => (n, i)).GroupBy(x => x.i / 15))
                    Say(a, string.Join(" ", chunk.Select(x => x.n)));
                Say(a, $"{hits.Count} prefabs");
            });
        }
    }
}
