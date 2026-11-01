using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Owner-side behaviour for the custom states. Vanilla MonsterAI still runs in Idle and
    /// whenever there is danger (see <see cref="BrainHook"/>).
    /// </summary>
    public class GreenlingBrain
    {
        private enum Phase { Fetch, Deliver, Wait }

        private readonly Greenling _g;
        private Phase _phase;
        private float _phaseTime;
        private float _actionCooldown;
        private string _lastWaitReason;

        public GreenlingBrain(Greenling g) { _g = g; }

        private MonsterAI AI => _g.AI;

        internal void OnStateChanged(GreenlingState s)
        {
            _phase = Phase.Fetch;
            _phaseTime = 0f;
            _lastWaitReason = null;
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
            if (!task.Sink.Exists() || !task.Sink.Accepts(task.Item))
            {
                _g.Event("task_broken", $"task=\"{task}\"");
                _g.Zdo.Set(Greenling.ZTask, "");
                _g.SetState(GreenlingState.Idle, "sink_gone");
                return;
            }

            _phaseTime += dt;
            _actionCooldown -= dt;
            if (_g.CarryCount > 0 && _phase == Phase.Fetch) Enter(Phase.Deliver); // e.g. resumed after a fight

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
            if (task.Source.Available(task.Item) <= 0) { Wait("source_empty"); return; }

            if (task.Source.Kind == EndpointKind.Ground)
            {
                var drop = task.Source.FindGroundItem(task.Item);
                if (!drop) { Wait("source_empty"); return; }
                if (!Arrive(dt, drop.transform.position, 1.2f)) return;
                var n = PickUp(drop, Plugin.CarryMax.Value);
                if (n > 0) Took(task, n);
                return;
            }

            if (!Arrive(dt, task.Source.CurrentPos(), Plugin.StationReach.Value)) return;
            var got = task.Source.TakeFromContainer(task.Item, Plugin.CarryMax.Value);
            if (got > 0) Took(task, got);
            else Wait("source_busy_or_empty");
        }

        private void Took(LaborTask task, int n)
        {
            _g.SetCarry(task.Item, n);
            _g.Event("take", $"item={task.Item} n={n} from={task.Source}");
            _lastWaitReason = null;
            Enter(Phase.Deliver);
        }

        private void Deliver(LaborTask task, float dt)
        {
            if (!Arrive(dt, task.Sink.CurrentPos(), Plugin.StationReach.Value)) return;
            AI.LookAt(task.Sink.CurrentPos());
            if (_actionCooldown > 0f) return;
            _actionCooldown = Plugin.DepositInterval.Value;

            if (!task.Sink.PutOne(task.Item)) { Wait("sink_full"); return; }
            var left = _g.CarryCount - 1;
            _g.SetCarry(task.Item, left);
            _g.Event("deposit", $"item={task.Item} to={task.Sink} carrying={left}");
            _lastWaitReason = null;
            if (left <= 0) Enter(Phase.Fetch);
        }

        // ---- helpers -----------------------------------------------------------------------

        private bool Arrive(float dt, Vector3 target, float reach)
        {
            if (AI.MoveTo(dt, target, reach, false)) return true;
            // Stations carve holes in the navmesh; accept "close enough" when progress stalls.
            if (_phaseTime > 4f && Vector3.Distance(_g.transform.position, target) < reach + 1.5f && AI.m_character.GetVelocity().magnitude < 0.2f)
                return true;
            if (_phaseTime > Plugin.StuckTimeout.Value) { Wait("stuck"); }
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

        private int PickUp(ItemDrop drop, int max)
        {
            var nview = drop.GetComponent<ZNetView>();
            if (!nview.IsOwner()) nview.ClaimOwnership();
            var n = Mathf.Min(max, drop.m_itemData.m_stack);
            if (n >= drop.m_itemData.m_stack) ZNetScene.instance.Destroy(drop.gameObject);
            else { drop.m_itemData.m_stack -= n; drop.Save(); }
            return n;
        }

        private void DropCarry(string reason)
        {
            var prefab = ObjectDB.instance.GetItemPrefab(_g.CarryItem);
            if (prefab)
            {
                var drop = Object.Instantiate(prefab, _g.transform.position + Vector3.up, Quaternion.identity).GetComponent<ItemDrop>();
                drop.SetStack(_g.CarryCount);
            }
            _g.Event("drop", $"item={_g.CarryItem} n={_g.CarryCount} reason={reason}");
            _g.SetCarry("", 0);
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
            if (!g || !g.IsGreenling || !g.IsOwner || g.State == GreenlingState.Idle) return true;

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
