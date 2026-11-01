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
        private float _actTimer = -1f;

        /// <summary>Set while a greenling calls station RPCs, so Teaching ignores them.</summary>
        internal static bool Acting;

        public GreenlingBrain(Greenling g) { _g = g; }

        private MonsterAI AI => _g.AI;
        public bool WantsControl => _g.State != GreenlingState.Idle || _prank != null;
        public bool Busy => _prank != null;

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
            if (_prank != null)
            {
                if (!_prank.Tick(dt)) { _prank = null; Enter(Phase.Fetch); }
                return true;
            }
            switch (_g.State)
            {
                case GreenlingState.Watching: TickWatching(dt); break;
                case GreenlingState.Working: TickWorking(dt); break;
                default:
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
            if (!task.Sink.Exists() || !task.Source.Exists() || (task.Sink.Resolve() && !Sinks.Accepts(task.Sink, task.Item)))
            {
                _g.Event("task_broken", $"task=\"{task}\"");
                _g.Zdo.Set(Greenling.ZTask, "");
                _g.SetState(GreenlingState.Idle, "task_broken");
                return;
            }

            _phaseTime += dt;
            _actionCooldown -= dt;

            if (task.IsWhack) { Whack(task, dt); return; }

            if (!task.Source.AreaLoaded || !task.Sink.AreaLoaded || (task.Sink.IsStation && !task.Sink.Resolve()) || (task.Source.IsStation && !task.Source.Resolve()))
            {
                if (_phase != Phase.Wait) Wait("station_not_loaded");
                AI.StopMoving();
                if (_phaseTime > Plugin.WaitRecheck.Value) Enter(Phase.Fetch);
                return;
            }

            switch (_phase)
            {
                case Phase.Fetch: Fetch(task, dt); break;
                case Phase.Deliver: Deliver(task, dt); break;
                case Phase.Wait:
                    // idle beside the sink, re-check now and then
                    if (Vector3.Distance(_g.transform.position, task.Sink.CurrentPos()) > 4f)
                        AI.MoveTo(dt, task.Sink.CurrentPos(), 3f, false);
                    else AI.StopMoving();
                    if (_phaseTime > Plugin.WaitRecheck.Value * Traits.WaitRecheck(_g)) Enter(_g.CarryCount > 0 ? Phase.Deliver : Phase.Fetch);
                    break;
            }
        }

        private void Fetch(LaborTask task, float dt)
        {
            // neglected greenlings sometimes just... don't (checked once per trip)
            if (_phaseTime == 0f && _g.CarryCount == 0 && Needs.Unmet(_g) >= 2 && Random.value < Plugin.SulkChance.Value)
            {
                Wait("sulking");
                return;
            }
            var src = task.Source;
            var room = _g.CarryPerTrip - _g.CarryCount;
            if (room <= 0) { Enter(Phase.Deliver); return; }

            if (src.Kind == EndpointKind.Container)
            {
                if (!Sources.HasWork(src, task.Item, _g.transform.position)) { DeliverOrWait("source_empty"); return; }
                if (!Arrive(dt, src.CurrentPos(), Plugin.StationReach.Value)) return;
                AI.LookAt(src.CurrentPos());
                if (!Act(dt, "attack")) return; // reach in
                var got = Sources.TakeFromContainer(src, task.Item, room);
                if (got > 0) { ChestSound(src); Took(task, got); Enter(Phase.Deliver); }
                else DeliverOrWait("source_busy_or_empty");
                return;
            }

            // Ground / producers: collect loose drops first, harvest when there are none.
            var drop = Sources.FindDrop(src, task.Item, _g.transform.position);
            if (drop)
            {
                if (!Arrive(dt, drop.transform.position, 1.2f)) return;
                AI.LookAt(drop.transform.position);
                if (!Act(dt, "attack")) return; // scoop it up
                var n = Sources.PickUp(drop, room);
                if (n > 0) Took(task, n);
                Enter(_g.CarryCount >= _g.CarryPerTrip ? Phase.Deliver : Phase.Fetch); // keep collecting until hands are full
                return;
            }

            if (_actionCooldown > 0f) return; // waiting for harvested items to land

            if (src.Kind == EndpointKind.BreakArea)
            {
                var target = Breakables.Nearest(src, _g.transform.position);
                if (!target) { DeliverOrWait("source_empty"); return; }
                if (!Arrive(dt, target.transform.position, Plugin.StationReach.Value)) return;
                AI.LookAt(target.transform.position);
                if (!Act(dt, "attack")) return; // whack
                var label = $"{Utils.GetPrefabName(target)}#{target.GetComponent<ZNetView>().GetZDO().m_uid.ID}"; // the hit may destroy it
                Breakables.Hit(target, _g.Character, _g.Role);
                _g.Event("smash", $"target={label} category={src.Prefab}");
                _actionCooldown = 0.4f;
                return;
            }

            var producer = Sources.ProducerReady(src, task.Item, _g.transform.position);
            if (producer)
            {
                if (!Arrive(dt, producer.transform.position, Plugin.StationReach.Value)) return;
                AI.LookAt(producer.transform.position);
                if (!Act(dt, "attack")) return;
                Acting = true;
                try { Sources.Harvest(producer, _g.transform.position); }
                finally { Acting = false; }
                _g.Event("harvest", $"item={task.Item} from={Utils.GetPrefabName(producer)}#{producer.GetComponent<ZNetView>().GetZDO().m_uid.ID}");
                _actionCooldown = 2.5f; // fermenters tap with a delay; drops need a moment to settle
                return;
            }

            DeliverOrWait("source_empty");
        }

        /// <summary>Brute work: smash everything of the category in the area, leave the pieces.</summary>
        private void Whack(LaborTask task, float dt)
        {
            if (_phase == Phase.Wait)
            {
                AI.StopMoving();
                if (_phaseTime > Plugin.WaitRecheck.Value * Traits.WaitRecheck(_g)) Enter(Phase.Fetch);
                return;
            }
            if (_actionCooldown > 0f) return;
            var target = Breakables.Nearest(task.Source, _g.transform.position);
            if (!target) { Wait("nothing_to_smash"); return; }
            if (!Arrive(dt, target.transform.position, Plugin.StationReach.Value + 0.5f)) return;
            AI.LookAt(target.transform.position);
            if (!Act(dt, "attack")) return;
            var label = $"{Utils.GetPrefabName(target)}#{target.GetComponent<ZNetView>().GetZDO().m_uid.ID}";
            Breakables.Hit(target, _g.Character, _g.Role);
            _g.Event("smash", $"target={label} category={task.Source.Prefab}");
            _lastWaitReason = null;
            _actionCooldown = 0.3f;
            Enter(Phase.Fetch);
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
            // clumsy ones sometimes drop it on the way (checked once per trip, as it sets off)
            if (_phaseTime == 0f && Random.value < Traits.FumbleChance(_g))
            {
                Prank.Drop(_g.transform.position + _g.transform.forward * 0.5f + Vector3.up * 0.5f, _g.CarryItem, _g.CarryCount);
                _g.Event("fumble", $"item={_g.CarryItem}");
                _g.SetCarry("", 0);
                Enter(Phase.Fetch);
                return;
            }
            if (!Arrive(dt, task.Sink.CurrentPos(), Plugin.StationReach.Value)) return;
            AI.LookAt(task.Sink.CurrentPos());
            if (!Act(dt, "throw")) return; // toss it in

            bool ok;
            string blocked;
            Acting = true;
            try { ok = Sinks.PutOne(task.Sink, task.Item, out blocked); }
            finally { Acting = false; }
            if (!ok) { Wait(blocked); return; }

            if (task.Sink.Kind == EndpointKind.Container) ChestSound(task.Sink);
            var left = _g.CarryCount - 1;
            _g.SetCarry(task.Item, left);
            _g.Event("deposit", $"item={task.Item} to={task.Sink} carrying={left}");
            _lastWaitReason = null;
            if (left <= 0) Enter(Phase.Fetch);
        }

        // ---- mischief ----------------------------------------------------------------------

        private float _nextMealTry;

        /// <summary>Owner, every couple of seconds: go eat or go to bed when needed (never mid-lesson or mid-carry).</summary>
        internal void CheckNeeds()
        {
            if (_prank != null || _g.State == GreenlingState.Watching || _g.CarryCount > 0) return;
            if (AI.GetTargetCreature()) return;

            if (Plugin.SleepAtNight.Value && Needs.IsNight && !_g.HomelessCached)
            {
                _prank = Prank.Sleep(_g, Nest.Near(_g.transform.position, Plugin.NestRange.Value));
                _g.Event("sleep", "nest=" + _prank.Detail);
                return;
            }
            if (Needs.Hungry(_g) && Time.time > _nextMealTry)
            {
                _nextMealTry = Time.time + 60f;
                var (chest, food) = Needs.FindFood(_g.transform.position);
                if (!chest) { _g.Event("no_food"); return; }
                _prank = Prank.Meal(_g, chest, Items.PrefabOf(food));
                _g.Event("meal_seek", $"item={Items.PrefabOf(food)} chest={Utils.GetPrefabName(chest.gameObject)}");
            }
        }

        /// <summary>Called by the owner now and then while idle. Sulky greenlings misbehave more.</summary>
        internal void MaybeMisbehave()
        {
            if (_prank != null || _g.State != GreenlingState.Idle || AI.GetTargetCreature()) return;
            var chance = Plugin.MischiefChance.Value * (_g.Curiosity < 25f ? 3f : 1f) * (_g.IsStray ? 2f : 1f) * Traits.Mischief(_g);
            if (Random.value < chance) StartPrank();
        }

        internal void StartPrank(string kind = null)
        {
            if (_g.State != GreenlingState.Idle) return;
            _prank = Prank.Pick(_g, kind);
            if (_prank == null) return;
            _g.Event("mischief", $"kind={_prank.Kind} {_prank.Detail}".TrimEnd());
            if (_prank.Kind != "watch" && _prank.Kind != "chase") DrawAudience(_prank.Kind);
        }

        /// <summary>Another greenling is up to something: nearby idle ones may stop to watch, or join a chase.</summary>
        internal void DrawAudience(string what)
        {
            foreach (var other in Greenling.All.Where(o => o && o != _g && o.IsGreenling && o.IsOwner && o.State == GreenlingState.Idle
                                                         && !o.Brain.Busy && Vector3.Distance(o.transform.position, _g.transform.position) < 12f))
            {
                if (Random.value > Plugin.AudienceChance.Value) continue;
                other.Brain._prank = what == "zoomies" ? Prank.Chase(other, _g) : Prank.Watch(other, _g);
                other.Event("mischief", $"kind={other.Brain._prank.Kind} target={_g.Label} because={what}");
            }
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
            _actTimer = -1f;
        }

        /// <summary>
        /// Play an animation (synced to all clients) and report true once it has had time to land,
        /// so taking/putting items is visible instead of instant.
        /// </summary>
        private bool Act(float dt, string trigger)
        {
            if (_actTimer < 0f)
            {
                // brutes have no throw animation: they swing instead
                if (trigger == "throw" && _g.Role == Role.Brute) trigger = "attack";
                _g.Character.m_zanim.SetTrigger(trigger);
                _actTimer = Plugin.ActionTime.Value * Traits.ActionTime(_g);
                return false;
            }
            _actTimer -= dt;
            if (_actTimer > 0f) return false;
            _actTimer = -1f;
            return true;
        }

        private static void ChestSound(Endpoint e)
        {
            var c = e.Get<Container>();
            if (c) c.m_openEffects.Create(c.transform.position, c.transform.rotation);
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
