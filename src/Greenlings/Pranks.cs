using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Something a greenling does when it isn't working: mischief, but also the endearing stuff
    /// (staring at you, bringing you presents). Tick returns false when it is over.
    /// </summary>
    internal class Prank
    {
        public string Kind;
        public string Detail = "";
        private readonly Greenling _g;
        private Vector3[] _points;
        private int _step;
        private float _time;
        private float _stepTime;
        private Container _chest;
        private ItemDrop _loot;
        private Pickable _pickable;
        private Player _player;
        private float _stareFor;
        private Greenling _target;

        /// <summary>Stop and watch another greenling's antics for a few seconds.</summary>
        public static Prank Watch(Greenling g, Greenling target) =>
            new Prank(g) { Kind = "watch", _target = target, _stareFor = Random.Range(3f, 6f) };

        /// <summary>Join in: run after the other one while it has the zoomies.</summary>
        public static Prank Chase(Greenling g, Greenling target) =>
            new Prank(g) { Kind = "chase", _target = target, _stareFor = Random.Range(6f, 10f) };

        private Prank(Greenling g) { _g = g; }

        private MonsterAI AI => _g.AI;

        public static Prank Pick(Greenling g, string kind)
        {
            var p = new Prank(g);
            var options = new List<string> { "zoomies" };
            p._target = null;
            var pos = g.transform.position;

            p._chest = Sources.Near<Container>(pos, 15f)
                .Where(c => !c.IsInUse() && c.GetInventory().NrOfItems() > 0
                            && c.m_nview && c.m_nview.IsValid() && c.GetComponent<Piece>())
                .OrderBy(_ => Random.value).FirstOrDefault();
            if (p._chest) options.Add("misplace");

            p._loot = LooseItemNear(pos, 12f);
            if (p._loot) options.Add("scatter");

            p._player = Player.GetClosestPlayer(pos, 20f);
            if (p._player)
            {
                if (Vector3.Distance(p._player.transform.position, pos) < 10f) { options.Add("stare"); options.Add("stare"); }
                p._pickable = GiftPickableNear(pos, 15f);
                if (p._loot || p._pickable || p._chest) options.Add("gift");
            }

            if (kind != null && options.Contains(kind)) p.Kind = kind;
            else
            {
                var weighted = options.SelectMany(o => Enumerable.Repeat(o, Traits.Weight(g, o))).ToList();
                p.Kind = weighted[Random.Range(0, weighted.Count)];
            }
            switch (p.Kind)
            {
                case "zoomies":
                    p._points = Enumerable.Range(0, 4).Select(_ => RandomPointNear(pos, 4f, 9f)).ToArray();
                    break;
                case "misplace":
                    p._points = new[] { p._chest.transform.position, RandomPointNear(p._chest.transform.position, 5f, 10f) };
                    p.Detail = $"chest={Utils.GetPrefabName(p._chest.gameObject)}";
                    break;
                case "scatter":
                    p._points = new[] { p._loot.transform.position, RandomPointNear(p._loot.transform.position, 5f, 10f) };
                    p.Detail = $"item={Utils.GetPrefabName(p._loot.gameObject)}";
                    break;
                case "stare":
                    p._stareFor = Random.Range(3f, 6f);
                    break;
                case "gift":
                    // prefer something lying around, then something it can pluck, then a chest
                    if (p._loot) { p._pickable = null; p._chest = null; p.Detail = "from=" + Utils.GetPrefabName(p._loot.gameObject); }
                    else if (p._pickable) { p._chest = null; p.Detail = "from=" + Utils.GetPrefabName(p._pickable.gameObject); }
                    else p.Detail = "from=" + Utils.GetPrefabName(p._chest.gameObject);
                    break;
            }
            return p;
        }

        public bool Tick(float dt)
        {
            _time += dt;
            _stepTime += dt;
            if (_time > 45f) return Finish();
            switch (Kind)
            {
                case "stare": return TickStare(dt);
                case "watch":
                    if (!_target || _stepTime > _stareFor) return Finish();
                    AI.StopMoving();
                    AI.LookAt(_target.Character.GetCenterPoint());
                    return true;
                case "chase":
                    if (!_target || _stepTime > _stareFor) return Finish();
                    AI.MoveTo(dt, _target.transform.position, 1.5f, true);
                    return true;
                case "gift": return TickGift(dt);
                default: return TickPoints(dt);
            }
        }

        // ---- zoomies / misplace / scatter: visit points ---------------------------------------

        private bool TickPoints(float dt)
        {
            if (_step >= _points.Length) return Finish();
            var reach = Kind == "zoomies" ? 1f : Plugin.StationReach.Value;
            if (!Arrive(dt, _points[_step], reach, Kind == "zoomies")) return true;

            if (Kind == "misplace" && _step == 0) StealFromChest();
            if (Kind == "scatter" && _step == 0 && _loot) _g.SetCarry(Utils.GetPrefabName(_loot.gameObject), Sources.PickUp(_loot, 1));
            _step++;
            _stepTime = 0f;
            return true;
        }

        // ---- stare: stop, turn to face you, look -------------------------------------------

        private bool TickStare(float dt)
        {
            if (!_player || _stepTime > _stareFor) return Finish();
            AI.StopMoving();
            AI.LookAt(_player.GetHeadPoint());
            return true;
        }

        // ---- gift: fetch something small, bring it to you, drop it, look up at you ------------

        private bool TickGift(float dt)
        {
            if (!_player) return Finish();
            switch (_step)
            {
                case 0: // go get it
                {
                    var at = _loot ? _loot.transform.position : _pickable ? _pickable.transform.position : _chest ? _chest.transform.position : Vector3.zero;
                    if (at == Vector3.zero) return Finish();
                    if (!Arrive(dt, at, _chest ? Plugin.StationReach.Value : 1.3f, false)) return true;
                    if (_loot) _g.SetCarry(Utils.GetPrefabName(_loot.gameObject), Sources.PickUp(_loot, 1));
                    else if (_pickable)
                    {
                        _pickable.m_nview.InvokeRPC("RPC_Pick", 0); // drops its item right there
                        _step = 10; // wait for it to land, then pick it up
                        _stepTime = 0f;
                        return true;
                    }
                    else StealFromChest();
                    break;
                }
                case 10: // collect what the plucked plant dropped
                {
                    if (_stepTime < 1f) return true;
                    var item = _pickable ? _pickable.m_itemPrefab.name : null;
                    var drop = item == null ? null : Physics.OverlapSphere(_g.transform.position, 3f, LayerMask.GetMask("item"))
                        .Select(c => c.attachedRigidbody ? c.attachedRigidbody.GetComponent<ItemDrop>() : null)
                        .FirstOrDefault(d => d && Utils.GetPrefabName(d.gameObject) == item);
                    if (!drop) return Finish();
                    _g.SetCarry(item, Sources.PickUp(drop, 1));
                    _step = 1;
                    _stepTime = 0f;
                    return true;
                }
                case 1: // bring it over (you may be moving)
                {
                    if (_g.CarryCount <= 0) return Finish();
                    if (!Arrive(dt, _player.transform.position, 1.6f, false)) return _stepTime < 25f || Finish();
                    var at = _g.transform.position + (_player.transform.position - _g.transform.position).normalized * 0.6f + Vector3.up * 0.5f;
                    Drop(at, _g.CarryItem, _g.CarryCount);
                    _g.Event("gift", $"item={_g.CarryItem} to={_player.GetPlayerName()}");
                    _g.SetCarry("", 0);
                    _stareFor = Random.Range(3f, 5f);
                    break;
                }
                case 2: // and look up at you, very pleased with itself
                    if (_stepTime > _stareFor) return Finish();
                    AI.StopMoving();
                    AI.LookAt(_player.GetHeadPoint());
                    return true;
                default:
                    return Finish();
            }
            _step++;
            _stepTime = 0f;
            return true;
        }

        // ---- helpers -------------------------------------------------------------------------

        private bool Arrive(float dt, Vector3 target, float reach, bool run) =>
            AI.MoveTo(dt, target, reach, run) ||
            (_stepTime > 4f && Vector3.Distance(_g.transform.position, target) < reach + 1.5f && AI.m_character.GetVelocity().magnitude < 0.2f) ||
            _stepTime > 15f; // unreachable: give up on this step

        private void StealFromChest()
        {
            if (!_chest || _chest.IsInUse() || !ChestOwnership.TryOwn(_chest)) return;
            var item = _chest.GetInventory().GetAllItems().Where(i => i.m_dropPrefab).OrderBy(_ => Random.value).FirstOrDefault();
            if (item == null) return;
            var prefab = item.m_dropPrefab.name;
            _chest.GetInventory().RemoveItem(item, 1); // small hands
            _g.SetCarry(prefab, 1);
            _g.Event("mischief_steal", $"item={prefab} n=1");
        }

        private bool Finish()
        {
            if (_g.CarryCount > 0)
            {
                Drop(_g.transform.position + _g.transform.forward * 0.5f + Vector3.up, _g.CarryItem, _g.CarryCount);
                _g.Event("mischief_drop", $"item={_g.CarryItem} n={_g.CarryCount}");
                _g.SetCarry("", 0);
            }
            _g.Event("mischief_done", "kind=" + Kind);
            return false;
        }

        internal static void Drop(Vector3 at, string item, int n)
        {
            var prefab = ObjectDB.instance.GetItemPrefab(item);
            if (!prefab || n <= 0) return;
            var drop = Object.Instantiate(prefab, at, Quaternion.identity).GetComponent<ItemDrop>();
            drop.SetStack(n);
        }

        private static ItemDrop LooseItemNear(Vector3 pos, float radius) =>
            Physics.OverlapSphere(pos, radius, LayerMask.GetMask("item"))
                .Select(c => c.attachedRigidbody ? c.attachedRigidbody.GetComponent<ItemDrop>() : null)
                .Where(d => d && d.GetComponent<ZNetView>().IsValid())
                .OrderBy(_ => Random.value).FirstOrDefault();

        /// <summary>Little wild things worth bringing to someone: stones, flint, mushrooms, flowers, berries.</summary>
        private static Pickable GiftPickableNear(Vector3 pos, float radius)
        {
            var allowed = Plugin.GiftPickables.Value.Split(',').Select(x => x.Trim()).ToList();
            return Sources.Near<Pickable>(pos, radius)
                .Where(p => p.m_nview && p.m_nview.IsValid() && !p.m_picked && p.m_itemPrefab
                            && allowed.Contains(Utils.GetPrefabName(p.gameObject)))
                .OrderBy(_ => Random.value).FirstOrDefault();
        }

        private static Vector3 RandomPointNear(Vector3 c, float min, float max)
        {
            var p = c + Quaternion.Euler(0, Random.Range(0, 360), 0) * Vector3.forward * Random.Range(min, max);
            ZoneSystem.instance.GetGroundHeight(p, out var y);
            p.y = y;
            return p;
        }
    }
}
