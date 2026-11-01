using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Owner-side behaviour for the custom states (and idle mischief). Vanilla MonsterAI runs
    /// the rest of the time and whenever there is danger (see <see cref="BrainHook"/>).
    /// </summary>
    public class GreenlingBrain
    {
        private enum Phase { Fetch, Deliver, Wait }

        private readonly Greenling _g;
        private Phase _phase;
        private float _phaseTime;
        private float _actionCooldown;
        private string _lastWaitReason;
        private Prank _prank;

        /// <summary>Set while a greenling calls station RPCs, so Teaching ignores them.</summary>
        internal static bool Acting;

        public GreenlingBrain(Greenling g) { _g = g; }

        private MonsterAI AI => _g.AI;
        public bool WantsControl => _g.State != GreenlingState.Idle || _prank != null;

        internal void OnStateChanged(GreenlingState s)
        {
            _phase = Phase.Fetch;
            _phaseTime = 0f;
            _lastWaitReason = null;
            _prank = null;
            AI.StopMoving();
            if (s != GreenlingState.Working && _g.CarryCount > 0) DropCarry("state change");
        }

        /// <summary>Returns the value MonsterAI.UpdateAI would return (true = AI ran).</summary>
        public bool Tick(float dt)
        {
            switch (_g.State)
            {
                case GreenlingState.Watching: TickWatching(dt); break;
                case GreenlingState.Working: TickWorking(dt); break;
                default:
                    if (_prank != null && !_prank.Tick(dt)) _prank = null;
                    break;
            }
            return true;
        }

        // ---- watching ----------------------------------------------------------------------

        private void TickWatching(float dt)
        {
            var teacher = ZNetScene.instance.FindInstance(_g.Teacher)?.GetComponent<Player>();
            if (!teacher)
            {
                LoseInterest("teacher_gone");
                return;
            }

            var dist = Vector3.Distance(teacher.transform.position, _g.transform.position);
            if (dist > Plugin.WatchFollowDistance.Value)
                AI.MoveTo(dt, teacher.transform.position, Plugin.WatchFollowDistance.Value * 0.6f, dist > 12f);
            else
            {
                AI.StopMoving();
                AI.LookAt(teacher.GetHeadPoint());
            }

            var drain = dist > 15f ? 3f : 1f;
            var left = _g.Attention - dt * drain;
            _g.Zdo.Set(Greenling.ZAttention, left);
            if (left <= 0f) LoseInterest("attention_ran_out");
        }

        private void LoseInterest(string reason)
        {
            _g.Event("lost_interest", "reason=" + reason);
            _g.AddCuriosity(-Plugin.LoseInterestCost.Value, "lost_interest");
            _g.SetState(_g.Task != null ? GreenlingState.Working : GreenlingState.Idle, reason);
        }

        // ---- working -----------------------------------------------------------------------

        private void TickWorking(float dt)
        {
            var task = _g.Task;
            if (task == null) { _g.SetState(GreenlingState.Idle, "no_task"); return; }
            if (!task.Sink.Exists() || !task.Source.Exists() || !Sinks.Accepts(task.Sink, task.Item))
            {
                _g.Event("task_broken", $"task=\"{task}\"");
                _g.Zdo.Set(Greenling.ZTask, "");
                _g.SetState(GreenlingState.Idle, "task_broken");
                return;
            }

            _phaseTime += dt;
            _actionCooldown -= dt;

            switch (_phase)
            {
                case Phase.Fetch: Fetch(task, dt); break;
                case Phase.Deliver: Deliver(task, dt); break;
                case Phase.Wait:
                    // idle beside the sink, re-check now and then
                    if (Vector3.Distance(_g.transform.position, task.Sink.CurrentPos()) > 4f)
                        AI.MoveTo(dt, task.Sink.CurrentPos(), 3f, false);
                    else AI.StopMoving();
                    if (_phaseTime > Plugin.WaitRecheck.Value) Enter(_g.CarryCount > 0 ? Phase.Deliver : Phase.Fetch);
                    break;
            }
        }

        private void Fetch(LaborTask task, float dt)
        {
            var src = task.Source;
            var room = Plugin.CarryMax.Value - _g.CarryCount;
            if (room <= 0) { Enter(Phase.Deliver); return; }

            if (src.Kind == EndpointKind.Container)
            {
                if (!Sources.HasWork(src, task.Item, _g.transform.position)) { DeliverOrWait("source_empty"); return; }
                if (!Arrive(dt, src.CurrentPos(), Plugin.StationReach.Value)) return;
                var got = Sources.TakeFromContainer(src, task.Item, room);
                if (got > 0) { Took(task, got); Enter(Phase.Deliver); }
                else DeliverOrWait("source_busy_or_empty");
                return;
            }

            // Ground / producers: collect loose drops first, harvest when there are none.
            var drop = Sources.FindDrop(src, task.Item, _g.transform.position);
            if (drop)
            {
                if (!Arrive(dt, drop.transform.position, 1.2f)) return;
                var n = Sources.PickUp(drop, room);
                if (n > 0) Took(task, n);
                Enter(Phase.Fetch); // keep collecting until full or nothing left
                return;
            }

            if (_actionCooldown > 0f) return; // waiting for harvested items to land
            var producer = Sources.ProducerReady(src, task.Item, _g.transform.position);
            if (producer)
            {
                if (!Arrive(dt, producer.transform.position, Plugin.StationReach.Value)) return;
                AI.LookAt(producer.transform.position);
                Acting = true;
                try { Sources.Harvest(producer, _g.transform.position); }
                finally { Acting = false; }
                _g.Event("harvest", $"item={task.Item} from={Utils.GetPrefabName(producer)}#{producer.GetComponent<ZNetView>().GetZDO().m_uid.ID}");
                _actionCooldown = 2.5f; // fermenters tap with a delay; drops need a moment to settle
                return;
            }

            DeliverOrWait("source_empty");
        }

        private void DeliverOrWait(string reason)
        {
            if (_g.CarryCount > 0) Enter(Phase.Deliver);
            else Wait(reason);
        }

        private void Took(LaborTask task, int n)
        {
            _g.SetCarry(task.Item, _g.CarryCount + n);
            _g.Event("take", $"item={task.Item} n={n} from={task.Source} carrying={_g.CarryCount}");
            _lastWaitReason = null;
        }

        private void Deliver(LaborTask task, float dt)
        {
            if (_g.CarryCount <= 0) { Enter(Phase.Fetch); return; }
            if (!Arrive(dt, task.Sink.CurrentPos(), Plugin.StationReach.Value)) return;
            AI.LookAt(task.Sink.CurrentPos());
            if (_actionCooldown > 0f) return;
            _actionCooldown = Plugin.DepositInterval.Value;

            bool ok;
            string blocked;
            Acting = true;
            try { ok = Sinks.PutOne(task.Sink, task.Item, out blocked); }
            finally { Acting = false; }
            if (!ok) { Wait(blocked); return; }

            var left = _g.CarryCount - 1;
            _g.SetCarry(task.Item, left);
            _g.Event("deposit", $"item={task.Item} to={task.Sink} carrying={left}");
            _lastWaitReason = null;
            if (left <= 0) Enter(Phase.Fetch);
        }

        // ---- mischief ----------------------------------------------------------------------

        /// <summary>Called by the owner now and then while idle. Sulky greenlings misbehave more.</summary>
        internal void MaybeMisbehave()
        {
            if (_prank != null || _g.State != GreenlingState.Idle || AI.GetTargetCreature()) return;
            var chance = Plugin.MischiefChance.Value * (_g.Curiosity < 25f ? 3f : 1f);
            if (Random.value < chance) StartPrank();
        }

        internal void StartPrank(string kind = null)
        {
            if (_g.State != GreenlingState.Idle) return;
            _prank = Prank.Pick(_g, kind);
            if (_prank != null) _g.Event("mischief", $"kind={_prank.Kind} {_prank.Detail}".TrimEnd());
        }

        // ---- helpers -----------------------------------------------------------------------

        private bool Arrive(float dt, Vector3 target, float reach)
        {
            if (AI.MoveTo(dt, target, reach, false)) return true;
            // Stations carve holes in the navmesh; accept "close enough" when progress stalls.
            if (_phaseTime > 4f && Vector3.Distance(_g.transform.position, target) < reach + 1.5f && AI.m_character.GetVelocity().magnitude < 0.2f)
                return true;
            if (_phaseTime > Plugin.StuckTimeout.Value) Wait("stuck");
            return false;
        }

        private void Enter(Phase p)
        {
            _phase = p;
            _phaseTime = 0f;
        }

        private void Wait(string reason)
        {
            if (reason != _lastWaitReason) _g.Event("wait", "reason=" + reason);
            _lastWaitReason = reason;
            Enter(Phase.Wait);
        }

        private void DropCarry(string reason)
        {
            Prank.Drop(_g.transform.position + _g.transform.forward * 0.5f + Vector3.up, _g.CarryItem, _g.CarryCount);
            _g.Event("drop", $"item={_g.CarryItem} n={_g.CarryCount} reason={reason}");
            _g.SetCarry("", 0);
        }
    }

    /// <summary>
    /// Something a bored greenling does instead of being useful. Each prank is a short list of
    /// points to visit; Tick returns false when it is over.
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

        private Prank(Greenling g) { _g = g; }

        public static Prank Pick(Greenling g, string kind)
        {
            var p = new Prank(g);
            var options = new System.Collections.Generic.List<string> { "zoomies" };
            var pos = g.transform.position;

            p._chest = Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                .Where(c => Vector3.Distance(c.transform.position, pos) < 15f && !c.IsInUse() && c.GetInventory().NrOfItems() > 0
                            && c.m_nview && c.m_nview.IsValid() && c.GetComponent<Piece>())
                .OrderBy(_ => Random.value).FirstOrDefault();
            if (p._chest) options.Add("misplace");

            p._loot = Physics.OverlapSphere(pos, 12f, LayerMask.GetMask("item"))
                .Select(c => c.attachedRigidbody ? c.attachedRigidbody.GetComponent<ItemDrop>() : null)
                .FirstOrDefault(d => d && d.GetComponent<ZNetView>().IsValid());
            if (p._loot) options.Add("scatter");

            p.Kind = kind != null && options.Contains(kind) ? kind : options[Random.Range(0, options.Count)];
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
            }
            return p;
        }

        public bool Tick(float dt)
        {
            _time += dt;
            _stepTime += dt;
            if (_time > 40f || _step >= _points.Length) return Finish();

            var ai = _g.AI;
            var reach = Kind == "zoomies" ? 1f : Plugin.StationReach.Value;
            var arrived = ai.MoveTo(dt, _points[_step], reach, Kind == "zoomies") ||
                          (_stepTime > 4f && Vector3.Distance(_g.transform.position, _points[_step]) < reach + 1.5f && ai.m_character.GetVelocity().magnitude < 0.2f) ||
                          _stepTime > 15f; // unreachable point: move on
            if (!arrived) return true;

            if (Kind == "misplace" && _step == 0) StealFromChest();
            if (Kind == "scatter" && _step == 0 && _loot)
            {
                var item = Utils.GetPrefabName(_loot.gameObject);
                _g.SetCarry(item, Sources.PickUp(_loot, 999));
            }
            _step++;
            _stepTime = 0f;
            return true;
        }

        private void StealFromChest()
        {
            if (!_chest || _chest.IsInUse()) return;
            var item = _chest.GetInventory().GetAllItems().Where(i => i.m_dropPrefab).OrderBy(_ => Random.value).FirstOrDefault();
            if (item == null) return;
            if (!_chest.m_nview.IsOwner()) _chest.m_nview.ClaimOwnership();
            var n = Mathf.Min(item.m_stack, Random.Range(1, 4));
            var prefab = item.m_dropPrefab.name;
            _chest.GetInventory().RemoveItem(item, n);
            _g.SetCarry(prefab, n);
            _g.Event("mischief_steal", $"item={prefab} n={n}");
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

        private static Vector3 RandomPointNear(Vector3 c, float min, float max)
        {
            var p = c + Quaternion.Euler(0, Random.Range(0, 360), 0) * Vector3.forward * Random.Range(min, max);
            ZoneSystem.instance.GetGroundHeight(p, out var y);
            p.y = y;
            return p;
        }
    }

    /// <summary>
    /// Takes over MonsterAI for greenlings in custom states. Danger (a target, or being hurt
    /// recently) falls through to vanilla so they still fight and flee.
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    internal static class BrainHook
    {
        private static bool Prefix(MonsterAI __instance, float dt, ref bool __result)
        {
            var g = __instance.GetComponent<Greenling>();
            if (!g || !g.IsGreenling || !g.IsOwner || !g.Brain.WantsControl) return true;

            __instance.UpdateTarget((Humanoid)__instance.m_character, dt, out _, out _);
            if (__instance.m_targetCreature != null || __instance.m_timeSinceHurt < 5f) return true;

            // what BaseAI.UpdateAI does for the owner, minus the bits only vanilla movement needs
            __instance.UpdateRegeneration(dt);
            __instance.m_timeSinceHurt += dt;
            __result = g.Brain.Tick(dt);
            return false;
        }
    }
}
