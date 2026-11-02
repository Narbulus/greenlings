using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Something a greenling does when it isn't working: mischief and games, the endearing stuff
    /// (staring at you, bringing you presents), and looking after itself (meals, naps, its nest).
    /// Tick returns false when it is over.
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
        private float _duration = 45f;
        private ItemDrop _loot;
        private Pickable _pickable;
        private Player _player;
        private float _stareFor;
        private Greenling _target;
        private Character _animal;
        private RandomFlyingBird _bird;
        private Piece _nest;
        private WearNTear _piece;
        private Vector3 _center;
        private float _angle;
        private float _nextChatter;
        private bool _it;
        private string _giftItem;
        private Character _patient;
        private Container _chest;
        private string _tool;

        public Greenling Target => _target;
        public Piece NestPiece => _nest;
        public Character Patient => _patient;

        private Prank(Greenling g) { _g = g; }

        private MonsterAI AI => _g.AI;

        public string Describe() => Kind switch
        {
            "sleep" => "asleep in its nest",
            "rest" => "having a little rest",
            "decorate" => "doing up its nest",
            "meal" => "having a snack",
            "huddle" => "chatting with friends",
            "tag" => "playing tag",
            "herd" => "chasing the animals",
            "birds" => "chasing birds",
            "repair" => "fixing things",
            "gift" => "bringing you something",
            "stare" => "staring at you",
            "heal" => "tending to someone hurt",
            "fetch_tool" => $"fetching a {Tools.Name(_tool)}",
            "soothe" => "soothing a stray",
            _ => "messing about",
        };

        // ---- needs ---------------------------------------------------------------------------

        /// <summary>Hungry: walk to food lying on the ground and eat it.</summary>
        public static Prank Meal(Greenling g, ItemDrop food) =>
            new Prank(g) { Kind = "meal", _loot = food, Detail = Utils.GetPrefabName(food.gameObject) };

        /// <summary>Night: go to the nest and sleep there until morning.</summary>
        public static Prank Sleep(Greenling g, Piece nest) =>
            new Prank(g) { Kind = "sleep", _nest = nest, Detail = Nest.Id(nest) };

        /// <summary>A daytime visit to the nest: a short rest, or planting something pretty around it.</summary>
        public static Prank Rest(Greenling g)
        {
            var nest = Nest.Assign(g);
            if (!nest) return null;
            var decorate = Nest.Decorations(nest) < Plugin.NestDecorations.Value && Random.value < 0.4f;
            return new Prank(g)
            {
                Kind = decorate ? "decorate" : "rest", _nest = nest, Detail = "nest=" + Nest.Id(nest),
                _stareFor = Random.Range(15f, 30f),
            };
        }

        /// <summary>Has a hammer: go round fixing whatever is worn.</summary>
        public static Prank Repair(Greenling g)
        {
            var piece = Damaged(g.transform.position);
            return piece ? new Prank(g) { Kind = "repair", _piece = piece, Detail = "piece=" + Utils.GetPrefabName(piece.gameObject), _duration = 90f } : null;
        }

        // ---- tools ----------------------------------------------------------------------------

        /// <summary>The work needs a tool and there's one in a chest nearby: go and get it.</summary>
        public static Prank FetchTool(Greenling g, Container chest, string tool) =>
            new Prank(g) { Kind = "fetch_tool", _chest = chest, _tool = tool, Detail = $"tool={tool} chest={Utils.GetPrefabName(chest.gameObject)}", _duration = 40f };

        private bool TickFetchTool(float dt)
        {
            if (!_chest) return Finish();
            if (_step == 0)
            {
                if (!Arrive(dt, _chest.transform.position, Plugin.StationReach.Value, false)) return true;
                AI.StopMoving();
                AI.LookAt(_chest.transform.position);
                _g.Character.m_zanim.SetTrigger("attack"); // rummage
                _step = 1;
                _stepTime = 0f;
                return true;
            }
            if (_stepTime < Plugin.ActionTime.Value) return true;
            if (!Tools.TakeFrom(_g, _chest, _tool)) _g.Event("tool_gone", $"item={_tool}");
            return Finish();
        }

        // ---- shamans --------------------------------------------------------------------------

        /// <summary>Someone nearby is hurt: go over and heal them.</summary>
        public static Prank Heal(Greenling g)
        {
            var patient = Shamans.Patient(g);
            return patient ? new Prank(g) { Kind = "heal", _patient = patient, Detail = "patient=" + Utils.GetPrefabName(patient.gameObject), _duration = 30f } : null;
        }

        /// <summary>Go and settle a stray down (it trusts a little more).</summary>
        public static Prank Soothe(Greenling g)
        {
            var stray = Shamans.StrayToSoothe(g);
            return stray ? new Prank(g) { Kind = "soothe", _target = stray, Detail = "stray=" + stray.Label, _duration = 30f } : null;
        }

        private bool TickHeal(float dt)
        {
            if (!_patient || _patient.IsDead()) return Finish();
            if (_step == 0)
            {
                if (_patient != _g.Character && !Arrive(dt, _patient.transform.position, 2.5f, true)) return true;
                AI.StopMoving();
                AI.LookAt(_patient.GetCenterPoint());
                _g.Character.m_zanim.SetTrigger(Shamans.HealAnimation(_g));
                _step = 1;
                _stepTime = 0f;
                return true;
            }
            if (_stepTime < 1.2f) return true;
            Shamans.Heal(_g, _patient);
            return Finish();
        }

        private bool TickSoothe(float dt)
        {
            if (!_target || !_target.IsStray) return Finish();
            if (_step == 0)
            {
                if (!Arrive(dt, _target.transform.position, 1.8f, false)) return true;
                AI.StopMoving();
                _step = 1;
                _stepTime = 0f;
                return true;
            }
            AI.LookAt(_target.Character.GetCenterPoint());
            if (_stepTime < 2.5f) return true; // a little murmur
            _target.NView.InvokeRPC("GL_Soothe", _g.NView.GetZDO().m_uid);
            _g.Event("soothe", "stray=" + _target.Label);
            return Finish();
        }

        // ---- reactions to others -------------------------------------------------------------

        /// <summary>Stop and watch another greenling's antics for a few seconds.</summary>
        public static Prank Watch(Greenling g, Greenling target) =>
            new Prank(g) { Kind = "watch", _target = target, _stareFor = Random.Range(3f, 6f), Detail = "target=" + target.Label };

        /// <summary>Join in: run after the other one while it has the zoomies.</summary>
        public static Prank Chase(Greenling g, Greenling target) =>
            new Prank(g) { Kind = "chase", _target = target, _stareFor = Random.Range(6f, 10f), Detail = "target=" + target.Label };

        /// <summary>The other half of a game of tag: run from whoever is "it".</summary>
        public static Prank RunFrom(Greenling g, Greenling chaser) =>
            new Prank(g) { Kind = "tag", _target = chaser, _it = false, _duration = Random.Range(10f, 16f), Detail = "it=" + chaser.Label };

        public static Prank Huddle(Greenling g, Vector3 center, float angle, float duration) =>
            new Prank(g) { Kind = "huddle", _center = center, _angle = angle, _duration = duration };

        /// <summary>Gather the others for a natter: everyone stands in a ring facing the middle.</summary>
        public static void InviteToHuddle(Greenling host, Prank hostPrank)
        {
            var others = Free(host, 25f).Take(4).ToList();
            hostPrank.Detail = "guests=" + others.Count;
            var ring = new List<Greenling> { host };
            ring.AddRange(others);
            var center = ring.Aggregate(Vector3.zero, (s, x) => s + x.transform.position) / ring.Count;
            hostPrank._center = center;
            for (var i = 0; i < ring.Count; i++)
            {
                var angle = 360f * i / ring.Count;
                if (i == 0) hostPrank._angle = angle;
                else ring[i].Brain.Join(Huddle(ring[i], center, angle, hostPrank._duration), "huddle");
            }
        }

        // ---- picking something to do ---------------------------------------------------------

        public static Prank Pick(Greenling g, string kind)
        {
            var p = new Prank(g);
            var options = new List<string> { "zoomies" };
            var pos = g.transform.position;

            p._loot = LooseItemNear(pos, 12f);
            if (p._loot) options.Add("scatter");

            p._player = Player.GetClosestPlayer(pos, 20f);
            if (p._player)
            {
                if (Vector3.Distance(p._player.transform.position, pos) < 10f) { options.Add("stare"); options.Add("stare"); }
                p._pickable = GiftPickableNear(pos, 15f);
                if (p._loot || p._pickable) options.Add("gift");
            }

            var friends = Free(g, 12f).ToList();
            if (friends.Count > 0) { p._target = friends[Random.Range(0, friends.Count)]; options.Add("tag"); }
            if (Free(g, 25f).Any() && !g.IsStray) options.Add("huddle");

            p._animal = TameAnimalNear(pos, 15f);
            if (p._animal) options.Add("herd");
            p._bird = BirdNear(pos, 20f);
            if (p._bird) options.Add("birds");

            if (kind != null && options.Contains(kind)) p.Kind = kind;
            else if (kind != null) return null;
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
                case "scatter":
                    p._points = new[] { p._loot.transform.position, RandomPointNear(p._loot.transform.position, 5f, 10f) };
                    p.Detail = $"item={Utils.GetPrefabName(p._loot.gameObject)}";
                    break;
                case "stare":
                    p._stareFor = Random.Range(3f, 6f);
                    break;
                case "gift":
                    // prefer something lying around, then something it can pluck
                    if (p._loot) { p._pickable = null; p.Detail = "from=" + Utils.GetPrefabName(p._loot.gameObject); }
                    else p.Detail = "from=" + Utils.GetPrefabName(p._pickable.gameObject);
                    break;
                case "tag":
                    p._it = true;
                    p._duration = Random.Range(10f, 16f);
                    p.Detail = "target=" + p._target.Label;
                    break;
                case "huddle":
                    p._duration = Random.Range(12f, 20f);
                    break;
                case "herd":
                    p._stareFor = Random.Range(6f, 10f);
                    p.Detail = "animal=" + Utils.GetPrefabName(p._animal.gameObject);
                    break;
                case "birds":
                    p._stareFor = Random.Range(5f, 8f);
                    p.Detail = "bird=" + Utils.GetPrefabName(p._bird.gameObject);
                    break;
            }
            return p;
        }

        public bool Tick(float dt)
        {
            _time += dt;
            _stepTime += dt;
            if (_time > _duration) return Finish();
            switch (Kind)
            {
                case "stare": return TickStare();
                case "meal": return TickMeal(dt);
                case "sleep": return TickSleep(dt);
                case "rest": return TickRest(dt);
                case "decorate": return TickDecorate(dt);
                case "repair": return TickRepair(dt);
                case "heal": return TickHeal(dt);
                case "fetch_tool": return TickFetchTool(dt);
                case "soothe": return TickSoothe(dt);
                case "huddle": return TickHuddle(dt);
                case "tag": return TickTag(dt);
                case "watch":
                    if (!_target || _stepTime > _stareFor) return Finish();
                    AI.StopMoving();
                    AI.LookAt(_target.Character.GetCenterPoint());
                    return true;
                case "chase":
                    if (!_target || _stepTime > _stareFor) return Finish();
                    AI.MoveTo(dt, _target.transform.position, 1.5f, true);
                    return true;
                case "herd":
                    if (!_animal || _animal.IsDead() || _stepTime > _stareFor) return Finish();
                    AI.MoveTo(dt, _animal.transform.position, 1.5f, true);
                    return true;
                case "birds": return TickBirds(dt);
                case "gift": return TickGift(dt);
                default: return TickPoints(dt);
            }
        }

        // ---- zoomies / scatter: visit points ----------------------------------------------------

        private bool TickPoints(float dt)
        {
            if (_step >= _points.Length) return Finish();
            var reach = Kind == "zoomies" ? 1f : Plugin.StationReach.Value;
            if (!Arrive(dt, _points[_step], reach, Kind == "zoomies")) return true;
            if (Kind == "scatter" && _step == 0 && _loot) _g.SetCarry(Utils.GetPrefabName(_loot.gameObject), Sources.PickUp(_loot, 1));
            _step++;
            _stepTime = 0f;
            return true;
        }

        // ---- needs ---------------------------------------------------------------------------

        private bool TickMeal(float dt)
        {
            if (!_loot) return Finish(); // someone else ate it
            if (!Arrive(dt, _loot.transform.position, 1.2f, false)) return true;
            if (_step == 0)
            {
                AI.LookAt(_loot.transform.position);
                _g.Character.m_zanim.SetTrigger("consume");
                _step = 1;
                _stepTime = 0f;
                return true;
            }
            if (_stepTime < 0.6f) return true;
            var item = Utils.GetPrefabName(_loot.gameObject);
            if (_loot.RemoveOne())
            {
                _g.Tameable.m_sootheEffect.Create(_g.Character.GetCenterPoint(), Quaternion.identity);
                _g.Fed(item, null, "found");
            }
            return Finish();
        }

        private bool TickSleep(float dt)
        {
            _time = 0f; // sleeping is allowed to take all night
            if (!_nest || !Needs.IsNight) { SetSleeping(false); _g.Event("woke"); return Finish(); }
            if (_step == 0)
            {
                if (!Arrive(dt, NestSpot(), 0.8f, false) && _stepTime < 40f) return true;
                _step = 1;
                SetSleeping(true);
            }
            AI.StopMoving();
            return true;
        }

        private bool TickRest(float dt)
        {
            if (!_nest) return Finish();
            if (_step == 0)
            {
                if (!Arrive(dt, NestSpot(), 0.8f, false) && _stepTime < 30f) return true;
                _step = 1;
                _stepTime = 0f;
                SetSleeping(true);
            }
            AI.StopMoving();
            if (_stepTime < _stareFor) return true;
            SetSleeping(false);
            return Finish();
        }

        private bool TickDecorate(float dt)
        {
            if (!_nest) return Finish();
            if (_step == 0)
            {
                _center = RandomPointNear(_nest.transform.position, 1.6f, 3f);
                _step = 1;
            }
            if (_step == 1)
            {
                if (!Arrive(dt, _center, 0.8f, false) && _stepTime < 30f) return true;
                AI.LookAt(_center);
                _g.Character.m_zanim.SetTrigger("attack"); // dig
                _step = 2;
                _stepTime = 0f;
                return true;
            }
            if (_stepTime < 1.2f) return true;
            var options = Plugin.NestPlants.Value.Split(',').Select(s => s.Trim()).Where(s => ZNetScene.instance.GetPrefab(s)).ToList();
            if (options.Count > 0 && Nest.Decorations(_nest) < Plugin.NestDecorations.Value)
            {
                var plant = options[Random.Range(0, options.Count)];
                Object.Instantiate(ZNetScene.instance.GetPrefab(plant), _center, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                _g.Event("decorate", $"plant={plant} nest={Nest.Id(_nest)}");
            }
            return Finish();
        }

        private void SetSleeping(bool on)
        {
            _g.Character.m_zanim.SetBool("sleeping", on);
        }

        /// <summary>Where in (or by) the nest to curl up: spread out so two don't stand in one spot.</summary>
        private Vector3 NestSpot()
        {
            var id = _g.Zdo?.m_uid.ID ?? 0;
            var a = (id % 6) * 60f;
            return _nest.transform.position + Quaternion.Euler(0f, a, 0f) * Vector3.forward * 0.5f;
        }

        // ---- a hammer: fix what is worn ---------------------------------------------------------

        private bool TickRepair(float dt)
        {
            if (!_piece || _piece.GetHealthPercentage() >= 0.999f)
            {
                _piece = Damaged(_g.transform.position);
                _step = 0;
                _stepTime = 0f;
                if (!_piece) return Finish();
            }
            if (_step == 0)
            {
                if (!Arrive(dt, _piece.transform.position, 2.4f, false)) return true;
                AI.LookAt(_piece.transform.position);
                _g.Character.m_zanim.SetTrigger("attack"); // tap tap
                _step = 1;
                _stepTime = 0f;
                return true;
            }
            if (_stepTime < 1f) return true;
            if (_piece.Repair())
            {
                var piece = _piece.GetComponent<Piece>();
                if (piece) piece.m_placeEffect.Create(_piece.transform.position, _piece.transform.rotation);
                _g.Event("repair", "piece=" + Utils.GetPrefabName(_piece.gameObject));
            }
            _piece = null;
            return true;
        }

        /// <summary>Something nearby that a greenling with a hammer would fix.</summary>
        internal static bool AnythingToRepair(Vector3 at) => Damaged(at);

        private static WearNTear Damaged(Vector3 at) =>
            Object.FindObjectsByType<WearNTear>(FindObjectsSortMode.None)
                .Where(w => w.m_nview && w.m_nview.IsValid() && w.GetHealthPercentage() < Plugin.RepairBelow.Value
                            && Vector3.Distance(w.transform.position, at) < Plugin.WorkRadius.Value)
                .OrderBy(w => Vector3.Distance(w.transform.position, at)).FirstOrDefault();

        // ---- games -----------------------------------------------------------------------------

        private bool TickHuddle(float dt)
        {
            var spot = _center + Quaternion.Euler(0f, _angle, 0f) * Vector3.forward * 1.3f;
            if (_step == 0)
            {
                if (!Arrive(dt, spot, 0.5f, false) && _stepTime < 15f) return true;
                _step = 1;
                _nextChatter = _time + Random.Range(0.5f, 2f);
            }
            AI.StopMoving();
            if (_time < _nextChatter) return true;
            _nextChatter = _time + Random.Range(1.2f, 3f);
            // natter: look at someone in the ring, say something, now and then a little hop
            var others = Greenling.All.Where(o => o && o != _g && o.Brain.CurrentPrank?.Kind == "huddle"
                                                  && Vector3.Distance(o.transform.position, _center) < 4f).ToList();
            AI.LookAt(others.Count > 0 ? others[Random.Range(0, others.Count)].Character.GetTopPoint() : _center);
            var r = Random.value;
            if (r < 0.5f) AI.m_idleSound.Create(_g.transform.position, Quaternion.identity);
            else if (r < 0.65f) _g.Character.m_zanim.SetTrigger("jump");
            return true;
        }

        private bool TickTag(float dt)
        {
            if (!_target || _target.Character.IsDead()) return Finish();
            if (_it)
            {
                AI.MoveTo(dt, _target.transform.position, 0.8f, true);
                if (Vector3.Distance(_g.transform.position, _target.transform.position) < 1.3f && _stepTime > 1.5f)
                {
                    // tagged! now the other one is "it"
                    _g.Event("tag", "tagged=" + _target.Label);
                    _it = false;
                    _stepTime = 0f;
                    var theirs = _target.Brain.CurrentPrank;
                    if (theirs != null && theirs.Kind == "tag") { theirs._it = true; theirs._stepTime = 0f; }
                }
                return true;
            }
            if (_stepTime < 1f) { AI.StopMoving(); return true; } // a beat to get away
            AI.Flee(dt, _target.transform.position);
            return true;
        }

        private bool TickBirds(float dt)
        {
            if (!_bird || _stepTime > _stareFor) return Finish();
            AI.MoveTo(dt, _bird.transform.position, 1f, true);
            return true;
        }

        // ---- stare: stop, turn to face you, look -------------------------------------------

        private bool TickStare()
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
                    var at = _loot ? _loot.transform.position : _pickable ? _pickable.transform.position : Vector3.zero;
                    if (at == Vector3.zero) return Finish();
                    if (!Arrive(dt, at, 1.3f, false)) return true;
                    if (_loot) _g.SetCarry(Utils.GetPrefabName(_loot.gameObject), Sources.PickUp(_loot, 1));
                    else
                    {
                        _center = _pickable.transform.position; // the plant may vanish once picked
                        _giftItem = _pickable.m_itemPrefab ? _pickable.m_itemPrefab.name : null;
                        _pickable.m_nview.InvokeRPC("RPC_Pick", 0); // drops its item right there
                        _step = 10; // wait for it to land, then pick it up
                        _stepTime = 0f;
                        return true;
                    }
                    break;
                }
                case 10: // collect what the plucked plant dropped
                {
                    if (_stepTime < 1f) return true;
                    var item = _giftItem;
                    var near = _center;
                    var drop = item == null ? null : Sources.DropsNear(near, 4f)
                        .OrderBy(d => Vector3.Distance(d.transform.position, near))
                        .FirstOrDefault(d => Utils.GetPrefabName(d.gameObject) == item);
                    if (!drop) return _stepTime < 3f || Finish(); // give it a moment to land
                    if (!Arrive(dt, drop.transform.position, 1.2f, false)) return true;
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
            (AI.MoveTo(dt, target, reach, run) && Utils.DistanceXZ(target, _g.transform.position) < reach + 1.5f) ||
            (_stepTime > 4f && Vector3.Distance(_g.transform.position, target) < reach + 1.5f && AI.m_character.GetVelocity().magnitude < 0.2f) ||
            _stepTime > 15f; // unreachable: give up on this step

        /// <summary>Interrupted: undo anything visible (no sleeping on the run).</summary>
        public void Stop()
        {
            if (Kind == "sleep" || Kind == "rest") SetSleeping(false);
        }

        private bool Finish()
        {
            if (Kind == "sleep" || Kind == "rest") SetSleeping(false);
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

        /// <summary>Other greenlings nearby with nothing on (this client drives them).</summary>
        private static IEnumerable<Greenling> Free(Greenling g, float radius) =>
            Greenling.All.Where(o => o && o != g && o.IsGreenling && o.IsOwner && o.State == GreenlingState.Idle && !o.Brain.Busy
                                     && !o.Scared && o.CarryCount == 0 && !o.FollowTarget
                                     && Vector3.Distance(o.transform.position, g.transform.position) < radius);

        private static ItemDrop LooseItemNear(Vector3 pos, float radius) =>
            Sources.DropsNear(pos, radius).Where(d => !Sinks.InFeedingPile(d)).OrderBy(_ => Random.value).FirstOrDefault();

        /// <summary>Little wild things worth bringing to someone: stones, flint, mushrooms, flowers, berries.</summary>
        private static Pickable GiftPickableNear(Vector3 pos, float radius)
        {
            var allowed = Plugin.GiftPickables.Value.Split(',').Select(x => x.Trim()).ToList();
            return Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None)
                .Where(p => p.m_nview && p.m_nview.IsValid() && !p.m_picked && p.m_itemPrefab
                            && allowed.Contains(Utils.GetPrefabName(p.gameObject))
                            && Vector3.Distance(p.transform.position, pos) < radius)
                .OrderBy(_ => Random.value).FirstOrDefault();
        }

        private static readonly List<Character> _chars = new List<Character>();

        /// <summary>A farm animal to chase about (tamed, not one of us).</summary>
        private static Character TameAnimalNear(Vector3 pos, float radius)
        {
            _chars.Clear();
            Character.GetCharactersInRange(pos, radius, _chars);
            return _chars.Where(c => c.IsTamed() && !c.GetComponent<Greenling>() && c.GetFaction() == Character.Faction.AnimalsVeg && !c.IsDead())
                         .OrderBy(_ => Random.value).FirstOrDefault();
        }

        private static RandomFlyingBird BirdNear(Vector3 pos, float radius) =>
            Object.FindObjectsByType<RandomFlyingBird>(FindObjectsSortMode.None)
                .Where(b => b.m_nview && b.m_nview.IsValid() && b.m_nview.GetZDO().GetBool(ZDOVars.s_landed)
                            && Vector3.Distance(b.transform.position, pos) < radius)
                .OrderBy(b => Vector3.Distance(b.transform.position, pos)).FirstOrDefault();

        internal static Vector3 RandomPointNear(Vector3 c, float min, float max)
        {
            var p = c + Quaternion.Euler(0, Random.Range(0, 360), 0) * Vector3.forward * Random.Range(min, max);
            ZoneSystem.instance.GetGroundHeight(p, out var y);
            p.y = y;
            return p;
        }
    }
}
