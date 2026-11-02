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
        BreakArea = 10,    // smash things of one category around a spot (bushes, logs, rocks); Prefab = category
        // sink for brute jobs
        Leave = 11,        // just leave the pieces where they fall (someone else hauls them)
        // sink: put it on the ground here (a feeding spot), keeping a small pile
        GroundDrop = 12,
        // sink: bring it to a player (a curious greenling helping you forage); Id = the player
        ToPlayer = 13,
        // sink: plant it (seeds) in a field, on a neat grid (shamans only); Prefab = the crop piece
        Field = 14,
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

        public static Endpoint GroundDrop(Vector3 pos) => new Endpoint { Kind = EndpointKind.GroundDrop, Pos = pos };

        public static Endpoint ToPlayer(Player p) => new Endpoint { Kind = EndpointKind.ToPlayer, Id = p.GetZDOID(), Pos = p.transform.position };

        public Player Player => Kind == EndpointKind.ToPlayer ? ZNetScene.instance.FindInstance(Id)?.GetComponent<Player>() : null;

        public static Endpoint Pickables(Vector3 pos, string prefab) => new Endpoint { Kind = EndpointKind.PickableArea, Pos = pos, Prefab = prefab };

        public static Endpoint BreakArea(Vector3 pos, string category) => new Endpoint { Kind = EndpointKind.BreakArea, Pos = pos, Prefab = category };

        public bool IsStation => Kind != EndpointKind.Ground && Kind != EndpointKind.PickableArea && Kind != EndpointKind.BreakArea
                                 && Kind != EndpointKind.Leave && Kind != EndpointKind.GroundDrop && Kind != EndpointKind.ToPlayer
                                 && Kind != EndpointKind.Field;

        public static Endpoint LeaveIt(Vector3 pos) => new Endpoint { Kind = EndpointKind.Leave, Pos = pos };

        /// <summary>How far from <see cref="Pos"/> loose items count as belonging to this source.</summary>
        public float CollectRadius => Kind switch
        {
            EndpointKind.Ground => 10f,
            EndpointKind.PickableArea => Plugin.WorkRadius.Value,
            EndpointKind.Extract => Plugin.WorkRadius.Value, // stray honey lying about gets put away too
            EndpointKind.BreakArea => 12f,
            _ => 3f,
        };

        public bool SameAs(Endpoint o) =>
            o != null && Kind == o.Kind && (IsStation ? Id == o.Id || (Prefab == o.Prefab && Vector3.Distance(Pos, o.Pos) < 1f)
                                                      : Vector3.Distance(Pos, o.Pos) < Mathf.Min(CollectRadius, 6f));

        private GameObject _cached;

        /// <summary>
        /// The live station object. ZDOIDs are reassigned when a world loads, so fall back to
        /// "the same kind of piece standing at the same spot" and adopt its new id.
        /// </summary>
        public GameObject Resolve()
        {
            if (!IsStation) return null;
            if (_cached) return _cached;
            var go = Id.IsNone() ? null : ZNetScene.instance.FindInstance(Id);
            if (go && Prefab.Length > 0 && Utils.GetPrefabName(go) != Prefab) go = null;
            if (!go) go = FindAtPosition();
            if (go)
            {
                _cached = go;
                Id = go.GetComponent<ZNetView>().GetZDO().m_uid;
            }
            return go;
        }

        private GameObject FindAtPosition()
        {
            if (Prefab.Length == 0) return null;
            foreach (var c in Physics.OverlapSphere(Pos, 1.5f))
            {
                var nview = c.GetComponentInParent<ZNetView>();
                if (nview && nview.IsValid() && Utils.GetPrefabName(nview.gameObject) == Prefab && Vector3.Distance(nview.transform.position, Pos) < 1f)
                    return nview.gameObject;
            }
            return null;
        }

        /// <summary>Whether the area around this endpoint is loaded (otherwise "missing" just means "not here yet").</summary>
        public bool AreaLoaded => ZNetScene.instance.IsAreaReady(Pos);

        public T Get<T>() where T : Component => Resolve()?.GetComponent<T>();

        public Vector3 CurrentPos()
        {
            if (Kind == EndpointKind.ToPlayer && Player is Player p) return Pos = p.transform.position;
            var go = Resolve();
            if (go) Pos = go.transform.position;
            return Pos;
        }

        /// <summary>False only when the area is loaded and the station is really gone.</summary>
        public bool Exists() => Kind == EndpointKind.ToPlayer ? Player != null : !IsStation || Resolve() != null || !AreaLoaded;

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

        /// <summary>A sibling station: same kind of endpoint on another object.</summary>
        public static Endpoint Like(Endpoint e, Component c) => For(e.Kind, c);

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

        /// <summary>A job is one or more tasks, one per line.</summary>
        public static List<LaborTask> ParseJob(string s) =>
            string.IsNullOrEmpty(s) ? new List<LaborTask>() : s.Split('\n').Where(l => l.Length > 0).Select(Parse).ToList();

        public static string SerializeJob(IEnumerable<LaborTask> job) => string.Join("\n", job.Select(t => t.Serialize()));

        public override string ToString() => IsWhack ? $"smash: {Source}" : $"{Item}: {Source} -> {Sink}";

        public bool IsWhack => Sink.Kind == EndpointKind.Leave;

        /// <summary>
        /// Showing a step again with the same place to take from (or the same place to put into)
        /// corrects that step instead of adding another.
        /// </summary>
        public bool Corrects(LaborTask o) => o != null && (Source.SameAs(o.Source) || Sink.SameAs(o.Sink));

        /// <summary>Stations that take several kinds of input: any of them will do, not just the one shown.</summary>
        public bool AnyAcceptedItem => Sink.Kind == EndpointKind.CookAdd || Sink.Kind == EndpointKind.SmelterOre || Sink.Kind == EndpointKind.FermenterAdd;

        public string Describe() => IsWhack ? $"smashing {Source.Prefab}s" : $"{Items.DisplayName(Item)} to {Station(Sink)}";

        public static string Describe(List<LaborTask> job) => string.Join(", then ", job.Select(t => t.Describe()));

        private static string Station(Endpoint e)
        {
            if (e.Kind == EndpointKind.GroundDrop) return "a feeding spot";
            if (e.Kind == EndpointKind.ToPlayer) return "you";
            if (e.Kind == EndpointKind.Field) return "the field";
            var go = e.Resolve();
            if (!go) return "somewhere";
            var piece = go.GetComponent<Piece>();
            return piece ? Localization.instance.Localize(piece.m_name) : Utils.GetPrefabName(go);
        }
    }

    /// <summary>Source adapters: what is available, and how to get it into a greenling's hands.</summary>
    internal static class Sources
    {
        public static IEnumerable<ItemDrop> DropsNear(Vector3 at, float radius) =>
            Physics.OverlapSphere(at, radius, LayerMask.GetMask("item"))
                .Select(c => c.attachedRigidbody ? c.attachedRigidbody.GetComponent<ItemDrop>() : null)
                .Where(d => d && d.GetComponent<ZNetView>() && d.GetComponent<ZNetView>().IsValid())
                .Distinct();

        /// <summary>Loose drops of the item near the source, nearest first (not ones in someone's feeding pile).</summary>
        public static ItemDrop FindDrop(Endpoint src, string item, Vector3 from)
        {
            if (src.Kind == EndpointKind.Container) return null;
            return DropsNear(src.CurrentPos(), src.CollectRadius)
                .Where(d => Matches(src, item, Utils.GetPrefabName(d.gameObject)) && !Sinks.InFeedingPile(d))
                .OrderBy(d => Vector3.Distance(d.transform.position, from)).FirstOrDefault();
        }

        /// <summary>
        /// Whether a loose item counts for this source: the item shown, or for a cooking station or
        /// fermenter anything it makes (collect the grilled neck tails too, and the burnt bits).
        /// </summary>
        public static bool Matches(Endpoint src, string taught, string drop)
        {
            if (drop == taught) return true;
            if (src.Kind != EndpointKind.Extract) return false;
            var go = src.Resolve();
            if (!go) return false;
            if (go.GetComponent<CookingStation>() is CookingStation k)
                return k.m_conversion.Any(c => c.m_to && c.m_to.name == drop) || (k.m_overCookedItem && k.m_overCookedItem.name == drop);
            if (go.GetComponent<Fermenter>() is Fermenter f) return f.m_conversion.Any(c => c.m_to && c.m_to.name == drop);
            return false;
        }

        /// <summary>Is there anything to fetch for this task right now (stock, ready produce, loose drops or things to smash)?</summary>
        public static bool HasWork(LaborTask t, Vector3 from)
        {
            var src = t.Source;
            if (src.Kind == EndpointKind.Container) return ContainerItem(t) != null;
            if (src.Kind == EndpointKind.BreakArea) return FindDrop(src, t.Item, from) || Breakables.Nearest(src, from);
            return FindDrop(src, t.Item, from) || ProducerReady(src, t.Item, from) != null;
        }

        /// <summary>Which item to take out of the source chest: the one shown, or anything else the station takes.</summary>
        public static string ContainerItem(LaborTask t)
        {
            var c = t.Source.Get<Container>();
            if (!c) return null;
            var inv = c.GetInventory();
            if (inv.CountItems(Items.SharedName(t.Item)) > 0) return t.Item;
            if (!t.AnyAcceptedItem) return null;
            return inv.GetAllItems().Select(Items.PrefabOf).FirstOrDefault(p => p != null && Sinks.Accepts(t.Sink, p));
        }

        /// <summary>
        /// For producer sources: the object to "harvest" now (a station of the same kind nearby, or a
        /// specific pickable), or null if nothing is ready.
        /// </summary>
        public static GameObject ProducerReady(Endpoint src, string item, Vector3 from)
        {
            switch (src.Kind)
            {
                case EndpointKind.Extract:
                {
                    var prefab = src.Prefab.Length > 0 ? src.Prefab : (src.Resolve() ? Utils.GetPrefabName(src.Resolve()) : "");
                    return Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None)
                        .Where(n => n && n.IsValid() && Utils.GetPrefabName(n.gameObject) == prefab
                                    && Vector3.Distance(n.transform.position, src.Pos) < Plugin.WorkRadius.Value)
                        .Select(n => n.gameObject)
                        .Where(Ready)
                        .OrderBy(go => Vector3.Distance(go.transform.position, from))
                        .FirstOrDefault();
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

        private static bool Ready(GameObject go)
        {
            if (go.GetComponent<Beehive>() is Beehive hive) return hive.GetHoneyLevel() > 0;
            if (go.GetComponent<SapCollector>() is SapCollector sap) return sap.GetLevel() > 0;
            if (go.GetComponent<CookingStation>() is CookingStation cook) return cook.HaveDoneItem();
            if (go.GetComponent<Fermenter>() is Fermenter ferm) return ferm.GetStatus() == Fermenter.Status.Ready;
            return false;
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

        /// <summary>Take up to max of an item from a container. Returns the count taken (0 if busy/empty).</summary>
        public static int TakeFromContainer(Endpoint src, string item, int max)
        {
            var c = src.Get<Container>();
            if (!c || c.IsInUse() || item == null) return 0;
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
            if (sink.Kind == EndpointKind.Leave || sink.Kind == EndpointKind.GroundDrop || sink.Kind == EndpointKind.ToPlayer) return true;
            if (sink.Kind == EndpointKind.Field) return Fields.SeedOf(sink.Prefab) == item;
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

        /// <summary>
        /// Where to take this item now. Jobs generalize: a torch job feeds whichever fire nearby
        /// burns lowest, a kiln job works the whole row of kilns, and things go back into the chest
        /// that already holds them. Null = nowhere has room right now.
        /// </summary>
        public static Endpoint Choose(LaborTask t, string item, Vector3 from)
        {
            var taught = t.Sink;
            switch (taught.Kind)
            {
                case EndpointKind.Leave:
                case EndpointKind.ToPlayer:
                    return taught;
                case EndpointKind.GroundDrop:
                    return PileSize(taught.Pos, item) < Plugin.FeedPile.Value ? taught : null;
                case EndpointKind.Field:
                {
                    // the next free spot on the field's grid
                    var spot = Fields.NextSpot(taught, from);
                    return spot.HasValue ? Fields.FieldAt(spot.Value, taught.Prefab) : null;
                }
                case EndpointKind.Container:
                {
                    var home = taught.Get<Container>();
                    var chests = ChestsNear(taught.Pos, Plugin.WorkRadius.Value).ToList();
                    bool Holds(Container c) => c.GetInventory().CountItems(Items.SharedName(item)) > 0;
                    bool Room(Container c) => !c.IsInUse() && c.GetInventory().CanAddItem(ObjectDB.instance.GetItemPrefab(item), 1);
                    // the chest it was shown, unless this item clearly lives somewhere else
                    if (home && Room(home) && (Holds(home) || !chests.Any(c => c != home && Holds(c) && Room(c)))) return taught;
                    var other = chests.Where(c => c != home && Holds(c) && Room(c)).OrderBy(c => Vector3.Distance(c.transform.position, from)).FirstOrDefault();
                    if (other) return Endpoint.Like(taught, other);
                    return home && Room(home) ? taught : null;
                }
                case EndpointKind.FireFuel:
                {
                    // every fire (torch, hearth, brazier) nearby that burns the same fuel: lowest first
                    var best = Object.FindObjectsByType<Fireplace>(FindObjectsSortMode.None)
                        .Where(f => f.m_nview && f.m_nview.IsValid() && f.m_canRefill && !f.m_infiniteFuel && f.m_fuelItem && f.m_fuelItem.name == item
                                    && Vector3.Distance(f.transform.position, taught.Pos) < Plugin.WorkRadius.Value)
                        .Select(f => (f, frac: f.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel) / Mathf.Max(1f, f.m_maxFuel)))
                        .Where(x => Mathf.CeilToInt(x.f.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel)) < x.f.m_maxFuel)
                        .OrderBy(x => x.frac).ThenBy(x => Vector3.Distance(x.f.transform.position, from))
                        .Select(x => x.f).FirstOrDefault();
                    return best ? Endpoint.Like(taught, best) : null;
                }
                default:
                {
                    // machines: the one shown, or another of the same kind standing next to it
                    var station = taught.Resolve();
                    if (!station) return null;
                    var prefab = Utils.GetPrefabName(station);
                    var bank = Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None)
                        .Where(n => n && n.IsValid() && Utils.GetPrefabName(n.gameObject) == prefab
                                    && Vector3.Distance(n.transform.position, station.transform.position) < Plugin.StationGroupRadius.Value)
                        .Select(n => Endpoint.Like(taught, n))
                        .Where(e => Accepts(e, item) && HasRoom(e, item))
                        .OrderBy(e => e.SameAs(taught) ? 0 : 1).ThenBy(e => Vector3.Distance(e.CurrentPos(), from))
                        .FirstOrDefault();
                    return bank;
                }
            }
        }

        /// <summary>Could this sink take one more right now (without doing it)?</summary>
        public static bool HasRoom(Endpoint sink, string item)
        {
            var go = sink.Resolve();
            if (sink.Kind == EndpointKind.Leave || sink.Kind == EndpointKind.ToPlayer) return true;
            if (sink.Kind == EndpointKind.GroundDrop) return PileSize(sink.Pos, item) < Plugin.FeedPile.Value;
            if (sink.Kind == EndpointKind.Field) return Fields.NextSpot(sink, sink.Pos).HasValue;
            if (!go) return false;
            switch (sink.Kind)
            {
                case EndpointKind.Container:
                    var c = go.GetComponent<Container>();
                    return !c.IsInUse() && c.GetInventory().CanAddItem(ObjectDB.instance.GetItemPrefab(item), 1);
                case EndpointKind.SmelterOre: return go.GetComponent<Smelter>().GetQueueSize() < go.GetComponent<Smelter>().m_maxOre;
                case EndpointKind.SmelterFuel: return go.GetComponent<Smelter>().GetFuel() <= go.GetComponent<Smelter>().m_maxFuel - 1f;
                case EndpointKind.FireFuel:
                    var f = go.GetComponent<Fireplace>();
                    return f.m_canRefill && !f.m_infiniteFuel && Mathf.CeilToInt(f.m_nview.GetZDO().GetFloat(ZDOVars.s_fuel)) < f.m_maxFuel;
                case EndpointKind.CookAdd:
                    var k = go.GetComponent<CookingStation>();
                    return k.GetFreeSlot() != -1 && (!k.m_requireFire || k.IsFireLit());
                case EndpointKind.CookFuel: return go.GetComponent<CookingStation>().GetFuel() <= go.GetComponent<CookingStation>().m_maxFuel - 1f;
                case EndpointKind.FermenterAdd: return go.GetComponent<Fermenter>().GetStatus() == Fermenter.Status.Empty;
                default: return false;
            }
        }

        private static IEnumerable<Container> ChestsNear(Vector3 at, float radius) =>
            Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                .Where(c => c.m_nview && c.m_nview.IsValid() && c.GetComponent<Piece>() && Vector3.Distance(c.transform.position, at) < radius);

        /// <summary>Chests the player built, near a point.</summary>
        public static IEnumerable<Container> Chests(Vector3 at, float radius) => ChestsNear(at, radius);

        private static int PileSize(Vector3 at, string item) =>
            Sources.DropsNear(at, 2f).Where(d => Utils.GetPrefabName(d.gameObject) == item).Sum(d => d.m_itemData.m_stack);

        /// <summary>Lying in a feeding spot some greenling keeps stocked: leave it alone.</summary>
        public static bool InFeedingPile(ItemDrop d)
        {
            foreach (var g in Greenling.All)
            {
                if (!g || !g.IsGreenling) continue;
                foreach (var t in g.Job)
                    if (t.Sink.Kind == EndpointKind.GroundDrop && Vector3.Distance(t.Sink.Pos, d.transform.position) < 2.5f) return true;
            }
            return false;
        }

        /// <summary>Put one item in. False means full/busy/blocked right now: wait and retry.</summary>
        public static bool PutOne(Endpoint sink, string item, out string blocked)
        {
            blocked = "sink_full";
            if (sink.Kind == EndpointKind.ToPlayer)
            {
                var p = sink.Player;
                if (!p) { blocked = "player_gone"; return false; }
                Prank.Drop(p.transform.position + p.transform.forward * 0.6f + Vector3.up * 0.4f, item, 1);
                return true;
            }
            if (sink.Kind == EndpointKind.GroundDrop)
            {
                Prank.Drop(sink.Pos + new Vector3(Random.Range(-0.4f, 0.4f), 0.4f, Random.Range(-0.4f, 0.4f)), item, 1);
                return true;
            }
            if (sink.Kind == EndpointKind.Field)
            {
                blocked = "spot_taken";
                // someone may have planted here since this spot was picked
                if (Physics.OverlapSphere(sink.Pos, 0.3f).Any(c => c.GetComponentInParent<Plant>() || c.GetComponentInParent<Pickable>())) return false;
                return Fields.Plant(sink, sink.Pos);
            }
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
