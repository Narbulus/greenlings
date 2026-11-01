using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Where a task takes items from or puts them into. Sources and sinks share one type so a
    /// task serializes as two endpoints. Adapters for each kind live in <see cref="Sources"/>
    /// and <see cref="Sinks"/>.
    /// </summary>
    public enum EndpointKind
    {
        // sources and sinks
        Container = 0,
        // sinks
        SmelterOre = 1, SmelterFuel = 2, FireFuel = 4, CookAdd = 5, CookFuel = 6, FermenterAdd = 7,
        // sources
        Ground = 3,        // loose drops around a spot
        Extract = 8,       // a producer station: beehive, sap collector, cooking station, fermenter
        PickableArea = 9,  // any pickable of one prefab around a spot (berry bushes, mushrooms, crops)
    }

    public class Endpoint
    {
        public EndpointKind Kind;
        public ZDOID Id = ZDOID.None; // station endpoints
        public Vector3 Pos;
        public string Prefab = "";    // PickableArea: which pickable

        public static Endpoint For(EndpointKind kind, Component c) =>
            new Endpoint { Kind = kind, Id = c.GetComponent<ZNetView>().GetZDO().m_uid, Pos = c.transform.position, Prefab = Utils.GetPrefabName(c.gameObject) };

        public static Endpoint Ground(Vector3 pos) => new Endpoint { Kind = EndpointKind.Ground, Pos = pos };

        public static Endpoint Pickables(Vector3 pos, string prefab) => new Endpoint { Kind = EndpointKind.PickableArea, Pos = pos, Prefab = prefab };

        public bool IsStation => Kind != EndpointKind.Ground && Kind != EndpointKind.PickableArea;

        /// <summary>How far from <see cref="Pos"/> loose items count as belonging to this source.</summary>
        public float CollectRadius => Kind switch
        {
            EndpointKind.Ground => 6f,
            EndpointKind.PickableArea => 12f,
            _ => 3f,
        };

        public bool SameAs(Endpoint o) =>
            o != null && Kind == o.Kind && (IsStation ? Id == o.Id : Vector3.Distance(Pos, o.Pos) < CollectRadius);

        public GameObject Resolve() => IsStation ? ZNetScene.instance.FindInstance(Id) : null;

        public T Get<T>() where T : Component => Resolve()?.GetComponent<T>();

        public Vector3 CurrentPos()
        {
            var go = Resolve();
            if (go) Pos = go.transform.position;
            return Pos;
        }

        public bool Exists() => !IsStation || Resolve() != null;

        public override string ToString() =>
            IsStation ? $"{Kind}:{(Resolve() ? Utils.GetPrefabName(Resolve()) : Prefab)}#{Id.ID}"
                      : $"{Kind}{(Prefab.Length > 0 ? ":" + Prefab : "")}@{Pos.x:0},{Pos.z:0}";

        public string Serialize() => string.Join(":", (int)Kind, Id.UserID, Id.ID, F(Pos.x), F(Pos.y), F(Pos.z), Prefab);

        public static Endpoint Parse(string s)
        {
            var p = s.Split(':');
            return new Endpoint
            {
                Kind = (EndpointKind)int.Parse(p[0]),
                Id = new ZDOID(long.Parse(p[1]), uint.Parse(p[2])),
                Pos = new Vector3(P(p[3]), P(p[4]), P(p[5])),
                Prefab = p.Length > 6 ? p[6] : "",
            };
        }

        private static string F(float f) => f.ToString(CultureInfo.InvariantCulture);
        private static float P(string s) => float.Parse(s, CultureInfo.InvariantCulture);
    }

    /// <summary>"Move Item from Source to Sink", learned from a demonstration.</summary>
    public class LaborTask
    {
        public string Item;
        public Endpoint Source, Sink;

        public string Serialize() => $"{Item}|{Source.Serialize()}|{Sink.Serialize()}";

        public static LaborTask Parse(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var p = s.Split('|');
            return new LaborTask { Item = p[0], Source = Endpoint.Parse(p[1]), Sink = Endpoint.Parse(p[2]) };
        }

        public override string ToString() => $"{Item}: {Source} -> {Sink}";

        public string Describe() => $"{Items.DisplayName(Item)} to {Station(Sink)}";

        private static string Station(Endpoint e)
        {
            var go = e.Resolve();
            if (!go) return "somewhere";
            var piece = go.GetComponent<Piece>();
            return piece ? Localization.instance.Localize(piece.m_name) : Utils.GetPrefabName(go);
        }
    }

    /// <summary>Source adapters: what is available, and how to get it into a greenling's hands.</summary>
    internal static class Sources
    {
        /// <summary>Loose drops of the item near the source, nearest first.</summary>
        public static ItemDrop FindDrop(Endpoint src, string item, Vector3 from)
        {
            ItemDrop best = null;
            var bestDist = float.MaxValue;
            foreach (var c in Physics.OverlapSphere(src.CurrentPos(), src.CollectRadius, LayerMask.GetMask("item")))
            {
                var drop = c.attachedRigidbody ? c.attachedRigidbody.GetComponent<ItemDrop>() : null;
                if (!drop || Utils.GetPrefabName(drop.gameObject) != item || !drop.GetComponent<ZNetView>().IsValid()) continue;
                var d = Vector3.Distance(drop.transform.position, from);
                if (d < bestDist) { best = drop; bestDist = d; }
            }
            return best;
        }

        /// <summary>Is there anything to fetch right now (stock, ready produce, or loose drops)?</summary>
        public static bool HasWork(Endpoint src, string item, Vector3 from)
        {
            if (src.Kind == EndpointKind.Container)
            {
                var c = src.Get<Container>();
                return c && c.GetInventory().CountItems(Items.SharedName(item)) > 0;
            }
            return FindDrop(src, item, from) || ProducerReady(src, item, from) != null;
        }

        /// <summary>
        /// For producer sources: the object to "harvest" now (station or a specific pickable),
        /// or null if nothing is ready.
        /// </summary>
        public static GameObject ProducerReady(Endpoint src, string item, Vector3 from)
        {
            switch (src.Kind)
            {
                case EndpointKind.Extract:
                {
                    var go = src.Resolve();
                    if (!go) return null;
                    if (go.GetComponent<Beehive>() is Beehive hive) return hive.GetHoneyLevel() > 0 ? go : null;
                    if (go.GetComponent<SapCollector>() is SapCollector sap) return sap.GetLevel() > 0 ? go : null;
                    if (go.GetComponent<CookingStation>() is CookingStation cook) return cook.HaveDoneItem() ? go : null;
                    if (go.GetComponent<Fermenter>() is Fermenter ferm) return ferm.GetStatus() == Fermenter.Status.Ready ? go : null;
                    return null;
                }
                case EndpointKind.PickableArea:
                    return Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None)
                        .Where(p => Utils.GetPrefabName(p.gameObject) == src.Prefab && !p.m_picked && p.m_nview && p.m_nview.IsValid()
                                    && Vector3.Distance(p.transform.position, src.Pos) < src.CollectRadius)
                        .OrderBy(p => Vector3.Distance(p.transform.position, from))
                        .Select(p => p.gameObject).FirstOrDefault();
                default:
                    return null;
            }
        }

        /// <summary>Make a producer spit out its items (they land as drops, like for players).</summary>
        public static void Harvest(GameObject producer, Vector3 greenlingPos)
        {
            var nview = producer.GetComponent<ZNetView>();
            if (producer.GetComponent<Beehive>() || producer.GetComponent<SapCollector>()) nview.InvokeRPC("RPC_Extract");
            else if (producer.GetComponent<CookingStation>()) nview.InvokeRPC("RPC_RemoveDoneItem", greenlingPos, 1);
            else if (producer.GetComponent<Fermenter>()) nview.InvokeRPC("RPC_Tap");
            else if (producer.GetComponent<Pickable>()) nview.InvokeRPC("RPC_Pick", 0);
        }

        /// <summary>Take up to max from a container. Returns the count taken (0 if busy/empty).</summary>
        public static int TakeFromContainer(Endpoint src, string item, int max)
        {
            var c = src.Get<Container>();
            if (!c || c.IsInUse()) return 0;
            var nview = c.GetComponent<ZNetView>();
            if (!nview.IsOwner()) nview.ClaimOwnership();
            var inv = c.GetInventory();
            var n = Mathf.Min(max, inv.CountItems(Items.SharedName(item)));
            if (n > 0) inv.RemoveItem(Items.SharedName(item), n); // Container saves itself via m_onChanged
            return n;
        }

        /// <summary>Pick a drop up off the ground (all of it, or max). Returns the count taken.</summary>
        public static int PickUp(ItemDrop drop, int max)
        {
            var nview = drop.GetComponent<ZNetView>();
            if (!nview.IsOwner()) nview.ClaimOwnership();
            var n = Mathf.Min(max, drop.m_itemData.m_stack);
            if (n >= drop.m_itemData.m_stack) ZNetScene.instance.Destroy(drop.gameObject);
            else { drop.m_itemData.m_stack -= n; drop.Save(); }
            return n;
        }

        /// <summary>
        /// Teaching: which producer (if any) an item picked up at this spot came from.
        /// </summary>
        public static Endpoint ProducerFor(Vector3 at, string item)
        {
            foreach (var c in Physics.OverlapSphere(at, 3.5f))
            {
                var go = c.GetComponentInParent<ZNetView>()?.gameObject;
                if (!go) continue;
                if (go.GetComponent<Beehive>() is Beehive h && h.m_honeyItem && h.m_honeyItem.name == item) return Endpoint.For(EndpointKind.Extract, h);
                if (go.GetComponent<SapCollector>() is SapCollector s && s.m_spawnItem && s.m_spawnItem.name == item) return Endpoint.For(EndpointKind.Extract, s);
                if (go.GetComponent<CookingStation>() is CookingStation k &&
                    (k.m_conversion.Any(x => x.m_to && x.m_to.name == item) || (k.m_overCookedItem && k.m_overCookedItem.name == item)))
                    return Endpoint.For(EndpointKind.Extract, k);
                if (go.GetComponent<Fermenter>() is Fermenter f && f.m_conversion.Any(x => x.m_to && x.m_to.name == item)) return Endpoint.For(EndpointKind.Extract, f);
            }
            return null;
        }
    }

    /// <summary>Sink adapters. Stations are fed through their own RPCs, exactly like a player.</summary>
    internal static class Sinks
    {
        public static bool Accepts(Endpoint sink, string item)
        {
            var go = sink.Resolve();
            if (!go) return false;
            switch (sink.Kind)
            {
                case EndpointKind.Container: return go.GetComponent<Container>();
                case EndpointKind.SmelterOre: return go.GetComponent<Smelter>() is Smelter s && s.IsItemAllowed(item);
                case EndpointKind.SmelterFuel: return go.GetComponent<Smelter>() is Smelter f && f.m_fuelItem && f.m_fuelItem.name == item;
                case EndpointKind.FireFuel: return go.GetComponent<Fireplace>() is Fireplace fp && fp.m_fuelItem && fp.m_fuelItem.name == item;
                case EndpointKind.CookAdd: return go.GetComponent<CookingStation>() is CookingStation c && c.IsItemAllowed(item);
                case EndpointKind.CookFuel: return go.GetComponent<CookingStation>() is CookingStation cf && cf.m_fuelItem && cf.m_fuelItem.name == item;
                case EndpointKind.FermenterAdd: return go.GetComponent<Fermenter>() is Fermenter fe && fe.IsItemAllowed(item.GetStableHashCode());
                default: return false;
            }
        }

        /// <summary>Put one item in. False means full/busy/blocked right now: wait and retry.</summary>
        public static bool PutOne(Endpoint sink, string item, out string blocked)
        {
            blocked = "sink_full";
            var go = sink.Resolve();
            if (!go) { blocked = "sink_gone"; return false; }
            var nview = go.GetComponent<ZNetView>();
            switch (sink.Kind)
            {
                case EndpointKind.Container:
                {
                    var c = go.GetComponent<Container>();
                    if (c.IsInUse()) { blocked = "sink_in_use"; return false; }
                    if (!nview.IsOwner()) nview.ClaimOwnership();
                    var prefab = ObjectDB.instance.GetItemPrefab(item);
                    return c.GetInventory().CanAddItem(prefab, 1) && c.GetInventory().AddItem(prefab, 1);
                }
                case EndpointKind.SmelterOre:
                {
                    var s = go.GetComponent<Smelter>();
                    if (s.GetQueueSize() >= s.m_maxOre) return false;
                    nview.InvokeRPC("RPC_AddOre", item, false);
                    return true;
                }
                case EndpointKind.SmelterFuel:
                {
                    var s = go.GetComponent<Smelter>();
                    if (s.GetFuel() > s.m_maxFuel - 1f) return false;
                    nview.InvokeRPC("RPC_AddFuel");
                    return true;
                }
                case EndpointKind.FireFuel:
                {
                    var f = go.GetComponent<Fireplace>();
                    if (!f.m_canRefill || f.m_infiniteFuel) { blocked = "sink_unfuelable"; return false; }
                    if (Mathf.CeilToInt(nview.GetZDO().GetFloat(ZDOVars.s_fuel)) >= f.m_maxFuel) return false;
                    nview.InvokeRPC("RPC_AddFuel");
                    return true;
                }
                case EndpointKind.CookAdd:
                {
                    var c = go.GetComponent<CookingStation>();
                    if (c.m_requireFire && !c.IsFireLit()) { blocked = "sink_no_fire"; return false; }
                    if (c.GetFreeSlot() == -1) return false;
                    nview.InvokeRPC("RPC_AddItem", item, false);
                    return true;
                }
                case EndpointKind.CookFuel:
                {
                    var c = go.GetComponent<CookingStation>();
                    if (c.GetFuel() > c.m_maxFuel - 1f) return false;
                    nview.InvokeRPC("RPC_AddFuel");
                    return true;
                }
                case EndpointKind.FermenterAdd:
                {
                    var f = go.GetComponent<Fermenter>();
                    if (f.GetStatus() != Fermenter.Status.Empty) return false;
                    nview.InvokeRPC("RPC_AddItem", item.GetStableHashCode(), false);
                    return true;
                }
                default:
                    blocked = "sink_unsupported";
                    return false;
            }
        }

        /// <summary>
        /// Teaching: map an add-item RPC the local player is sending to a sink endpoint.
        /// Covers every UI path (hotbar use, interact, switches) in one place.
        /// </summary>
        public static bool FromRpc(ZNetView nview, string method, object[] args, out Endpoint sink, out string item)
        {
            sink = null;
            item = null;
            var go = nview.gameObject;
            switch (method)
            {
                case "RPC_AddOre" when go.GetComponent<Smelter>() is Smelter s:
                    sink = Endpoint.For(EndpointKind.SmelterOre, s); item = args[0] as string; break;
                case "RPC_AddFuel" when go.GetComponent<Smelter>() is Smelter s && s.m_fuelItem:
                    sink = Endpoint.For(EndpointKind.SmelterFuel, s); item = s.m_fuelItem.name; break;
                case "RPC_AddFuel" when go.GetComponent<Fireplace>() is Fireplace f && f.m_fuelItem:
                    sink = Endpoint.For(EndpointKind.FireFuel, f); item = f.m_fuelItem.name; break;
                case "RPC_AddFuel" when go.GetComponent<CookingStation>() is CookingStation c && c.m_fuelItem:
                    sink = Endpoint.For(EndpointKind.CookFuel, c); item = c.m_fuelItem.name; break;
                case "RPC_AddItem" when go.GetComponent<CookingStation>() is CookingStation c:
                    sink = Endpoint.For(EndpointKind.CookAdd, c); item = args[0] as string; break;
                case "RPC_AddItem" when go.GetComponent<Fermenter>() is Fermenter f && args[0] is int hash:
                    sink = Endpoint.For(EndpointKind.FermenterAdd, f);
                    item = f.m_conversion.Select(x => x.m_from).FirstOrDefault(x => x && x.name.GetStableHashCode() == hash)?.name;
                    break;
            }
            return sink != null && item != null;
        }
    }

    internal static class Items
    {
        public static string SharedName(string prefab) =>
            ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? prefab;

        public static string DisplayName(string prefab) => Localization.instance.Localize(SharedName(prefab));

        public static string PrefabOf(ItemDrop.ItemData item) => item?.m_dropPrefab ? item.m_dropPrefab.name : null;

        /// <summary>Count of each item (by prefab name) in an inventory.</summary>
        public static Dictionary<string, int> Counts(Inventory inv) =>
            inv.GetAllItems().Where(i => i.m_dropPrefab)
               .GroupBy(i => i.m_dropPrefab.name)
               .ToDictionary(g => g.Key, g => g.Sum(i => i.m_stack));
    }
}
