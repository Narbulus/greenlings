using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Greenlings
{
    public enum EndpointKind { Container, SmelterOre, SmelterFuel, Ground }

    /// <summary>
    /// One end of a labor task: a station or a patch of ground. Knows how to take items from
    /// it (source) and put items into it (sink). New station types plug in here.
    /// </summary>
    public class Endpoint
    {
        public EndpointKind Kind;
        public ZDOID Id;      // ZDOID.None for Ground
        public Vector3 Pos;

        public const float GroundRadius = 6f;

        public static Endpoint For(EndpointKind kind, Component c) =>
            new Endpoint { Kind = kind, Id = c.GetComponent<ZNetView>().GetZDO().m_uid, Pos = c.transform.position };

        public static Endpoint Ground(Vector3 pos) => new Endpoint { Kind = EndpointKind.Ground, Id = ZDOID.None, Pos = pos };

        public bool SameAs(Endpoint o) =>
            o != null && Kind == o.Kind && (Kind == EndpointKind.Ground ? Vector3.Distance(Pos, o.Pos) < GroundRadius : Id == o.Id);

        /// <summary>Live object for this endpoint, or null if destroyed / not loaded.</summary>
        public GameObject Resolve() => Kind == EndpointKind.Ground ? null : ZNetScene.instance.FindInstance(Id);

        public Vector3 CurrentPos()
        {
            var go = Resolve();
            if (go) Pos = go.transform.position;
            return Pos;
        }

        public bool Exists() => Kind == EndpointKind.Ground || Resolve() != null;

        public override string ToString() =>
            Kind == EndpointKind.Ground ? $"Ground@{Pos.x:0},{Pos.z:0}" : $"{Kind}:{NameOf(Resolve())}#{Id.ID}";

        private static string NameOf(GameObject go) => go ? Utils.GetPrefabName(go) : "?";

        public string Serialize() => string.Join(":", (int)Kind, Id.UserID, Id.ID,
            Pos.x.ToString(CultureInfo.InvariantCulture), Pos.y.ToString(CultureInfo.InvariantCulture), Pos.z.ToString(CultureInfo.InvariantCulture));

        public static Endpoint Parse(string s)
        {
            var p = s.Split(':');
            return new Endpoint
            {
                Kind = (EndpointKind)int.Parse(p[0]),
                Id = new ZDOID(long.Parse(p[1]), uint.Parse(p[2])),
                Pos = new Vector3(F(p[3]), F(p[4]), F(p[5])),
            };
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        // ---- source side ---------------------------------------------------------------

        /// <summary>Ground source: the nearest matching item lying around this endpoint.</summary>
        public ItemDrop FindGroundItem(string item)
        {
            ItemDrop best = null;
            var bestDist = float.MaxValue;
            foreach (var c in Physics.OverlapSphere(Pos, GroundRadius, LayerMask.GetMask("item")))
            {
                var drop = c.attachedRigidbody ? c.attachedRigidbody.GetComponent<ItemDrop>() : null;
                if (!drop || Utils.GetPrefabName(drop.gameObject) != item || !drop.GetComponent<ZNetView>().IsValid()) continue;
                var d = Vector3.Distance(drop.transform.position, Pos);
                if (d < bestDist) { best = drop; bestDist = d; }
            }
            return best;
        }

        /// <summary>How many of the item the source has right now (ground: whether any is lying around).</summary>
        public int Available(string item)
        {
            switch (Kind)
            {
                case EndpointKind.Container:
                    var c = Resolve()?.GetComponent<Container>();
                    return c ? c.GetInventory().CountItems(Items.SharedName(item)) : 0;
                case EndpointKind.Ground:
                    return FindGroundItem(item) ? 1 : 0;
                default:
                    return 0; // smelter slots are sinks only (output lands on the ground)
            }
        }

        /// <summary>Take up to max from a container. Returns the count taken (0 if busy/empty).</summary>
        public int TakeFromContainer(string item, int max)
        {
            var c = Resolve()?.GetComponent<Container>();
            if (!c || c.IsInUse()) return 0;
            var nview = c.GetComponent<ZNetView>();
            if (!nview.IsOwner()) nview.ClaimOwnership();
            var inv = c.GetInventory();
            var n = Mathf.Min(max, inv.CountItems(Items.SharedName(item)));
            if (n > 0) inv.RemoveItem(Items.SharedName(item), n); // Container saves itself via m_onChanged
            return n;
        }

        // ---- sink side -----------------------------------------------------------------

        /// <summary>Whether this sink accepts the item at all.</summary>
        public bool Accepts(string item)
        {
            var go = Resolve();
            switch (Kind)
            {
                case EndpointKind.Container: return go && go.GetComponent<Container>();
                case EndpointKind.SmelterOre: return go && go.GetComponent<Smelter>() is Smelter s && s.IsItemAllowed(item);
                case EndpointKind.SmelterFuel: return go && go.GetComponent<Smelter>() is Smelter f && f.m_fuelItem && f.m_fuelItem.name == item;
                default: return false;
            }
        }

        /// <summary>
        /// Put one item in. Returns false if the sink is full or busy (caller waits and retries).
        /// Smelters go through their own RPCs, exactly like a player adding ore or fuel.
        /// </summary>
        public bool PutOne(string item)
        {
            var go = Resolve();
            if (!go) return false;
            switch (Kind)
            {
                case EndpointKind.Container:
                {
                    var c = go.GetComponent<Container>();
                    if (c.IsInUse()) return false;
                    var nview = c.GetComponent<ZNetView>();
                    if (!nview.IsOwner()) nview.ClaimOwnership();
                    var prefab = ObjectDB.instance.GetItemPrefab(item);
                    if (!c.GetInventory().CanAddItem(prefab, 1)) return false;
                    return c.GetInventory().AddItem(prefab, 1);
                }
                case EndpointKind.SmelterOre:
                {
                    var s = go.GetComponent<Smelter>();
                    if (s.GetQueueSize() >= s.m_maxOre) return false;
                    s.m_nview.InvokeRPC("RPC_AddOre", item, false);
                    return true;
                }
                case EndpointKind.SmelterFuel:
                {
                    var s = go.GetComponent<Smelter>();
                    if (s.GetFuel() > s.m_maxFuel - 1f) return false;
                    s.m_nview.InvokeRPC("RPC_AddFuel");
                    return true;
                }
                default:
                    return false;
            }
        }
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
            if (!go) return e.Kind == EndpointKind.Ground ? "the ground" : "somewhere";
            var piece = go.GetComponent<Piece>();
            return piece ? Localization.instance.Localize(piece.m_name) : Utils.GetPrefabName(go);
        }
    }

    internal static class Items
    {
        public static string SharedName(string prefab) =>
            ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? prefab;

        public static string DisplayName(string prefab) => Localization.instance.Localize(SharedName(prefab));

        public static string PrefabOf(ItemDrop.ItemData item) => item?.m_dropPrefab ? item.m_dropPrefab.name : null;

        /// <summary>Count of each item (by prefab name) in an inventory.</summary>
        public static System.Collections.Generic.Dictionary<string, int> Counts(Inventory inv) =>
            inv.GetAllItems().Where(i => i.m_dropPrefab)
               .GroupBy(i => i.m_dropPrefab.name)
               .ToDictionary(g => g.Key, g => g.Sum(i => i.m_stack));
    }
}
