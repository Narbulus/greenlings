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
            prefab.StartsWith("@") ? TestGrounds.Resolve(prefab).GetComponent<T>() :
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

        private static readonly string[] TamePrefabs = { "Greyling", "Greydwarf", "Greydwarf_Elite", "Greydwarf_Shaman" };
        private static GameObject _lastTame;

        /// <summary>The tameable creature most recently placed by gldev_place, else the nearest greyling/greydwarf/brute.</summary>
        private static Tameable NearestTame() =>
            _lastTame && _lastTame.GetComponent<Tameable>() ? _lastTame.GetComponent<Tameable>() :
            Object.FindObjectsByType<Tameable>(FindObjectsSortMode.None)
                .Where(t => TamePrefabs.Contains(Utils.GetPrefabName(t.gameObject)))
                .OrderBy(t => Vector3.Distance(t.transform.position, P.transform.position))
                .FirstOrDefault();

        private static GameObject NearestObject(string prefab) =>
            prefab.StartsWith("@") ? TestGrounds.Resolve(prefab) :
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
            pos.y = target.GetComponentInParent<ZNetView>()?.GetZDO()?.GetString(TestGrounds.Tag).Length > 0 ? to.y + 0.2f : y;
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
            Cmd("gldev_actor", "<name> <prefab> <bay> [x=0] [z=0] [body] spawn a bonded named arena actor", a =>
            {
                if (a.Length > 6 && !GreenlingModel.BodyNames(a[2]).Contains(a[6])) throw new System.ArgumentException("unknown pooled body " + a[6]);
                TestGrounds.SpawnActor(a[1], a[2], a[3], a.TryParameterFloat(4), a.TryParameterFloat(5));
                _lastTame = TestGrounds.Resolve(a[1]);
                if (a.Length > 6 && !GreenlingModel.SelectBody(_lastTame.GetComponent<Greenling>(), a[6])) throw new System.InvalidOperationException("could not select actor body");
            });
            Cmd("gldev_select", "<@fixture> select an exact actor/object for existing interaction commands", a =>
            {
                var go = TestGrounds.Resolve(a[1]);
                LastPlaced[Utils.GetPrefabName(go)] = go;
                if (go.GetComponent<Tameable>()) _lastTame = go;
            });
            Cmd("gldev_setup", "[play] tests: god+ghost on, auto-pickup off, empty inventory. 'play': god on, ghost off, auto-pickup on, inventory kept", a =>
            {
                var play = a.Length > 1 && a[1] == "play";
                P.SetGodMode(true);
                P.SetGhostMode(!play);
                Player.m_enableAutoPickup = play; // tests pick things up explicitly
                if (!play) P.GetInventory().RemoveAll();
                // every test starts from default Greenlings settings, whatever an earlier (failed) test changed
                if (!play && BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue("greenlings", out var gl))
                    foreach (var key in gl.Instance.Config.Keys.ToList()) gl.Instance.Config[key].BoxedValue = gl.Instance.Config[key].DefaultValue;
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
                if (go.GetComponent<Tameable>()) _lastTame = go;
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
                _lastTame = null;
                // and anything a player could have built here (hammer/cultivator pieces), e.g. by hand while testing
                var buildable = new System.Collections.Generic.HashSet<string>(
                    ObjectDB.instance.m_items.Select(i => i.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces)
                        .Where(t => t != null).SelectMany(t => t.m_pieces).Where(x => x).Select(x => x.name));
                foreach (var v in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
                    if (v && v.IsValid() && buildable.Contains(Utils.GetPrefabName(v.gameObject))
                        && v.GetZDO().GetString(TestGrounds.Tag).Length == 0
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
                var t = NearestTame();
                if (!t) { Say(a, "no greyling"); return; }
                Say(a, "pat " + (t.Interact(P, false, false) ? "ok" : "refused"));
            });

            Cmd("gldev_useitem", "<item> use an item from the inventory on the nearest greyling", a =>
            {
                var t = NearestTame();
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

            Cmd("gldev_scale", "<prefab> <scale> rescale the most recently placed object of that prefab (local preview only, not saved)", a =>
            {
                var go = NearestObject(a[1]);
                if (!go) { Say(a, "no " + a[1]); return; }
                go.transform.localScale = Vector3.one * a.TryParameterFloat(2, 1f);
                Say(a, $"{a[1]} scale {a.TryParameterFloat(2, 1f)}");
            });

            Cmd("gldev_rename", "<name> rename the nearest tamed greyling like Shift+E would", a =>
            {
                var t = NearestTame();
                if (!t) { Say(a, "no greyling"); return; }
                t.SetText(a[1]);
                Say(a, "renamed to " + a[1]);
            });

            Cmd("gldev_bond", "<0-100> set the nearest tamed greyling's trust (100 = teachable greenling)", a =>
            {
                var t = NearestTame();
                var v = t ? t.GetComponent<ZNetView>() : null;
                if (!v) { Say(a, "no greyling"); return; }
                if (!v.IsOwner()) v.ClaimOwnership();
                v.GetZDO().Set("gl_bond", a.TryParameterFloat(1, 100f));
                Say(a, $"bond = {a.TryParameterFloat(1, 100f)}");
            });

            Cmd("gldev_act", "<swing|jump|emote name> make the player do something (for mimicry)", a =>
            {
                switch (a[1])
                {
                    case "swing": Say(a, "swing " + P.StartAttack(null, false)); break;
                    case "jump": P.Jump(true); Say(a, "jump"); break;
                    case "emote": Say(a, "emote " + P.StartEmote(a.Length > 2 ? a[2] : "wave")); break;
                    default: Say(a, "unknown action " + a[1]); break;
                }
            });

            Cmd("gldev_hit", "<prefab> [times=1] hit the nearest object of that prefab as the player (chop+pickaxe 60, tier 3)", a =>
            {
                var go = NearestObject(a[1]);
                if (!go) { Say(a, "no " + a[1]); return; }
                StepTo(go.transform);
                var col = go.GetComponentInChildren<Collider>();
                for (var i = 0; i < a.TryParameterInt(2, 1); i++)
                {
                    var hit = new HitData { m_point = col ? col.ClosestPoint(P.GetCenterPoint()) : go.transform.position, m_toolTier = 3, m_hitCollider = col };
                    hit.m_dir = (hit.m_point - P.GetCenterPoint()).normalized;
                    hit.m_damage.m_chop = 60f;
                    hit.m_damage.m_pickaxe = 60f;
                    hit.SetAttacker(P);
                    if (go.GetComponent<TreeLog>() is TreeLog log) log.Damage(hit);
                    else if (go.GetComponent<MineRock5>() is MineRock5 m5) m5.Damage(hit);
                    else if (go.GetComponent<MineRock>() is MineRock m) m.Damage(hit);
                    else if (go.GetComponent<Destructible>() is Destructible d) d.Damage(hit);
                    else if (go.GetComponent<TreeBase>() is TreeBase t) t.Damage(hit);
                }
                Say(a, "hit " + a[1]);
            });

            Cmd("gldev_tame", "instantly tame the nearest greyling", a =>
            {
                var t = NearestTame();
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

            Cmd("gldev_dropitem", "<item> [n=1] drop items from the inventory like the player would (counts as a demonstration)", a =>
            {
                var item = P.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab && i.m_dropPrefab.name == a[1]);
                if (item == null) { Say(a, "no " + a[1] + " in inventory"); return; }
                Say(a, "dropitem " + (P.DropItem(P.GetInventory(), item, a.TryParameterInt(2, 1)) ? "ok" : "refused"));
            });

            Cmd("gldev_damage", "<prefab> <healthFraction> wear down the nearest piece of that prefab (for repair tests)", a =>
            {
                var go = NearestObject(a[1]);
                var wear = go ? go.GetComponent<WearNTear>() : null;
                if (!wear) { Say(a, "no piece " + a[1]); return; }
                var v = wear.GetComponent<ZNetView>();
                if (!v.IsOwner()) v.ClaimOwnership();
                v.GetZDO().Set(ZDOVars.s_health, wear.m_health * a.TryParameterFloat(2, 0.5f));
                v.InvokeRPC(ZNetView.Everybody, "RPC_HealthChanged", wear.m_health * a.TryParameterFloat(2, 0.5f));
                Say(a, $"{a[1]} health {wear.GetHealthPercentage():0.00}");
            });

            Cmd("gldev_kill", "kill the nearest tamed greyling", a =>
            {
                var t = NearestTame();
                var c = t ? t.GetComponent<Character>() : null;
                if (!c) { Say(a, "no greyling"); return; }
                var hit = new HitData();
                hit.m_damage.m_damage = 99999f;
                c.Damage(hit);
                Say(a, "killed " + c.m_name);
            });

            Cmd("gldev_tamelast", "<prefab> instantly tame the nearest creature of that prefab (boars, wolves...)", a =>
            {
                var t = Nearest<Tameable>(a[1]);
                if (!t) { Say(a, "no " + a[1]); return; }
                t.Tame();
                Say(a, "tamed " + a[1]);
            });

            Cmd("gldev_hurt", "<prefab> <healthFraction> set the nearest creature of that prefab to a fraction of its health", a =>
            {
                var c = Nearest<Character>(a[1]);
                if (!c) { Say(a, "no " + a[1]); return; }
                var v = c.GetComponent<ZNetView>();
                if (!v.IsOwner()) v.ClaimOwnership();
                c.SetHealth(c.GetMaxHealth() * a.TryParameterFloat(2, 0.3f));
                Say(a, $"{a[1]} health {c.GetHealthPercentage():0.00}");
            });

            Cmd("gldev_cultivate", "<right> <forward> <width> <length> till a patch of ground (centre relative to the player, rows along the player's facing)", a =>
            {
                var op = ZNetScene.instance.GetPrefab("cultivate");
                if (!op) { Say(a, "no cultivate prefab"); return; }
                var t = P.transform;
                var centre = t.position + t.right * a.TryParameterFloat(1) + t.forward * a.TryParameterFloat(2);
                float w = a.TryParameterFloat(3, 3f), l = a.TryParameterFloat(4, 6f);
                var n = 0;
                for (var x = -w / 2f; x <= w / 2f + 0.01f; x += 1f)
                for (var z = -l / 2f; z <= l / 2f + 0.01f; z += 1f)
                {
                    var pos = centre + t.right * x + t.forward * z;
                    ZoneSystem.instance.GetGroundHeight(pos, out var y);
                    pos.y = y;
                    TerrainModifier.SetTriggerOnPlaced(true);
                    Object.Instantiate(op, pos, Quaternion.identity);
                    TerrainModifier.SetTriggerOnPlaced(false);
                    n++;
                }
                Say(a, $"cultivated {w}x{l} ({n} ops) at {centre}");
            });

            Cmd("gldev_plant", "<piece> <right> <forward> plant a crop as the player would with the cultivator (counts as a demonstration)", a =>
            {
                var piece = ZNetScene.instance.GetPrefab(a[1])?.GetComponent<Piece>();
                if (!piece) { Say(a, "no piece " + a[1]); return; }
                var t = P.transform;
                var pos = t.position + t.right * a.TryParameterFloat(2) + t.forward * a.TryParameterFloat(3);
                ZoneSystem.instance.GetGroundHeight(pos, out var y);
                pos.y = y;
                P.PlacePiece(piece, pos, Quaternion.identity, false);
                Say(a, $"planted {a[1]} at {pos}");
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
