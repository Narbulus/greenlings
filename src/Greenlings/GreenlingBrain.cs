using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Owner-side behaviour. Takes over from vanilla MonsterAI whenever a greenling has something
    /// to do (work, watch, follow, a chore, a prank, being scared); vanilla runs the rest of the
    /// time and for fighting (see <see cref="BrainHook"/>).
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
        private bool _phaseStart;     // first tick of a phase: once-per-trip decisions happen here
        private int _step;            // which step of the job this trip is for
        private Endpoint _tripSink;   // where this trip's item goes (jobs generalize to nearby sinks)
        private LaborTask _chore;     // something useful it does on its own for a few trips
        private string _choreKind = "";
        private int _choreTrips;

        /// <summary>Set while a greenling calls station RPCs, so Teaching ignores them.</summary>
        internal static bool Acting;

        public GreenlingBrain(Greenling g) { _g = g; }

        private MonsterAI AI => _g.AI;
        public bool WantsControl => _g.Scared || _g.FollowTarget || _g.State != GreenlingState.Idle || _prank != null || _chore != null;
        public bool Busy => _prank != null || _chore != null;
        internal Prank CurrentPrank => _prank;

        /// <summary>What it is doing, for the hover text and gl_dump.</summary>
        public string Doing()
        {
            if (_prank != null) return _prank.Describe();
            if (_chore != null) return _choreKind == "help" ? $"helping out ({_chore.Describe()})" : $"{_choreKind} ({_chore.Describe()})";
            if (_g.State == GreenlingState.Working)
            {
                var job = _g.Job;
                if (job.Count == 0) return "working";
                var what = job.Count > 1 ? LaborTask.Describe(job) : (job[0].IsWhack ? job[0].Describe() : "hauling " + job[0].Describe());
                if (_phase == Phase.Wait && _lastWaitReason != null) what += $" (waiting: {_lastWaitReason.Replace('_', ' ')})";
                if (_g.CarryCount > 0) what += $", carrying {Items.DisplayName(_g.CarryItem)}";
                return what;
            }
            return "pottering about";
        }

        internal void OnStateChanged(GreenlingState s)
        {
            Enter(Phase.Fetch);
            _lastWaitReason = null;
            _prank = null;
            _chore = null;
            _step = 0;
            _tripSink = null;
            AI.StopMoving();
            if (s != GreenlingState.Working && _g.CarryCount > 0) DropCarry("state change");
        }

        internal void OnJobChanged()
        {
            _step = 0;
            _tripSink = null;
            if (_phase == Phase.Wait) Enter(Phase.Fetch);
        }

        /// <summary>Returns the value MonsterAI.UpdateAI would return (true = AI ran).</summary>
        public bool Tick(float dt)
        {
            var follow = _g.FollowTarget;
            if (follow && _prank != null) StopPrank("following"); // drop everything and come along
            if (_prank != null)
            {
                if (!_prank.Tick(dt)) { _prank = null; Enter(Phase.Fetch); }
                return true;
            }
            if (follow) { TickFollow(dt, follow); return true; }
            if (_g.Zdo.GetZDOID(Greenling.ZFollow) != ZDOID.None) EndFollow();

            if (_g.State == GreenlingState.Watching) { TickWatching(dt); return true; }
            var task = CurrentTask;
            if (task != null) TickWorking(task, dt);
            else if (_g.State == GreenlingState.Working) _g.SetState(GreenlingState.Idle, "no_job");
            return true;
        }

        private LaborTask CurrentTask
        {
            get
            {
                if (_chore != null) return _chore;
                if (_g.State != GreenlingState.Working) return null;
                var job = _g.Job;
                return job.Count == 0 ? null : job[_step % job.Count];
            }
        }

        // ---- following -----------------------------------------------------------------------

        private void TickFollow(float dt, Player p)
        {
            var dist = Vector3.Distance(p.transform.position, _g.transform.position);
            if (dist > 3f) AI.MoveTo(dt, p.transform.position, 2.5f, dist > 8f);
            else
            {
                AI.StopMoving();
                AI.LookAt(p.GetHeadPoint());
            }
        }

        private void EndFollow()
        {
            _g.Zdo.Set(Greenling.ZFollow, ZDOID.None);
            _g.Event("follow_end");
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

            var keep = _g.CasualWatch ? Plugin.WatchFollowDistance.Value * 1.5f : Plugin.WatchFollowDistance.Value;
            var dist = Vector3.Distance(teacher.transform.position, _g.transform.position);
            if (dist > keep)
                AI.MoveTo(dt, teacher.transform.position, keep * 0.6f, dist > 12f);
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
            _g.SetState(_g.Job.Count > 0 ? GreenlingState.Working : GreenlingState.Idle, reason);
        }

        // ---- working -----------------------------------------------------------------------

        private void TickWorking(LaborTask task, float dt)
        {
            if (!task.Sink.Exists() || !task.Source.Exists() || (task.Sink.IsStation && task.Sink.Resolve() && !Sinks.Accepts(task.Sink, task.Item)))
            {
                if (task == _chore) { EndChore("broken"); return; }
                _g.Event("task_broken", $"task=\"{task}\"");
                var job = _g.Job.Where(t => t.Serialize() != task.Serialize()).ToList();
                _g.SetJob(job);
                if (job.Count == 0) _g.SetState(GreenlingState.Idle, "task_broken");
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
                case Phase.Fetch: Fetch(dt); break;
                case Phase.Deliver: Deliver(task, dt); break;
                case Phase.Wait:
                    // idle near where the work is, re-check now and then (Think may find it something else to do)
                    var near = _g.CarryCount > 0 ? task.Sink.CurrentPos() : task.Source.CurrentPos();
                    if (Vector3.Distance(_g.transform.position, near) > 5f) AI.MoveTo(dt, near, 4f, false);
                    else AI.StopMoving();
                    if (_phaseTime > Plugin.WaitRecheck.Value * Traits.WaitRecheck(_g)) Enter(_g.CarryCount > 0 ? Phase.Deliver : Phase.Fetch);
                    break;
            }
        }

        /// <summary>Is there something to do for this step right now: something to fetch, and somewhere to put it?</summary>
        private bool Workable(LaborTask t)
        {
            if (!t.Source.AreaLoaded) return false;
            if (!Sources.HasWork(t, _g.transform.position)) return false;
            if (t.IsWhack) return true;
            var item = t.Source.Kind == EndpointKind.Container ? Sources.ContainerItem(t) : t.Item;
            return item != null && Sinks.Choose(t, item, _g.transform.position) != null;
        }

        /// <summary>Start of a trip: pick the job step that has work (cooking: collect what's done before adding more).</summary>
        private bool PickStep()
        {
            if (_chore != null) return Workable(_chore);
            var job = _g.Job;
            for (var i = 0; i < job.Count; i++)
            {
                var idx = (_step + i) % job.Count;
                if (!Workable(job[idx])) continue;
                if (idx != _step % job.Count) _g.Event("step", $"step={idx} task=\"{job[idx]}\"");
                _step = idx;
                return true;
            }
            return false;
        }

        /// <summary>How many it carries per trip: one, or a handful of seeds when planting.</summary>
        private int TripSize(LaborTask t) => t != null && t.Sink.Kind == EndpointKind.Field ? Mathf.Max(1, Plugin.SeedsPerTrip.Value) : _g.CarryPerTrip;

        private void Fetch(float dt)
        {
            if (_phaseStart && _g.CarryCount == 0)
            {
                _phaseStart = false;
                // neglected greenlings sometimes just... don't (checked once per trip)
                if (Needs.Unmet(_g) >= 2 && Random.value < Plugin.SulkChance.Value) { Wait("sulking"); return; }
                if (!PickStep())
                {
                    if (_chore != null) { EndChore("done"); return; }
                    Wait("no_work");
                    return;
                }
                if (!HaveTool(CurrentTask)) return;
            }
            var task = CurrentTask;
            var src = task.Source;
            var room = TripSize(task) - _g.CarryCount;
            if (room <= 0) { Enter(Phase.Deliver); return; }

            if (src.Kind == EndpointKind.Container)
            {
                var item = Sources.ContainerItem(task);
                if (item == null) { DeliverOrWait("source_empty"); return; }
                if (!Arrive(dt, src.CurrentPos(), Plugin.StationReach.Value)) return;
                AI.LookAt(src.CurrentPos());
                if (!Act(dt, "attack")) return; // reach in
                var got = Sources.TakeFromContainer(src, item, room);
                if (got > 0) { ChestSound(src); Took(task, item, got); Enter(Phase.Deliver); }
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
                if (!drop) { Enter(Phase.Fetch); return; } // someone else got it
                var got = Utils.GetPrefabName(drop.gameObject);
                var n = Sources.PickUp(drop, room);
                if (n > 0) Took(task, got, n);
                Enter(_g.CarryCount >= TripSize(task) ? Phase.Deliver : Phase.Fetch); // keep collecting until hands are full
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
            if (!target)
            {
                if (task == _chore) { EndChore("done"); return; }
                Wait("nothing_to_smash");
                return;
            }
            if (!Arrive(dt, target.transform.position, Plugin.StationReach.Value + 0.5f)) return;
            AI.LookAt(target.transform.position);
            if (!Act(dt, "attack")) return;
            var label = $"{Utils.GetPrefabName(target)}#{target.GetComponent<ZNetView>().GetZDO().m_uid.ID}";
            Breakables.Hit(target, _g.Character, _g.Role);
            _g.Event("smash", $"target={label} category={task.Source.Prefab}");
            _lastWaitReason = null;
            _actionCooldown = 0.3f;
            if (task == _chore && --_choreTrips <= 0) { EndChore("done"); return; }
            Enter(Phase.Fetch);
        }

        private void DeliverOrWait(string reason)
        {
            if (_g.CarryCount > 0) Enter(Phase.Deliver);
            else Wait(reason);
        }

        private void Took(LaborTask task, string item, int n)
        {
            _g.SetCarry(item, _g.CarryCount + n);
            _g.Event("take", $"item={item} n={n} from={task.Source} carrying={_g.CarryCount}");
            _lastWaitReason = null;
            _tripSink = null;
        }

        private void Deliver(LaborTask task, float dt)
        {
            if (_g.CarryCount <= 0) { Enter(Phase.Fetch); return; }
            var item = _g.CarryItem;
            if (_phaseStart)
            {
                _phaseStart = false;
                // clumsy ones sometimes drop it on the way (checked once per trip, as it sets off)
                if (Random.value < Traits.FumbleChance(_g))
                {
                    Prank.Drop(_g.transform.position + _g.transform.forward * 0.5f + Vector3.up * 0.5f, item, _g.CarryCount);
                    _g.Event("fumble", $"item={item}");
                    _g.SetCarry("", 0);
                    Enter(Phase.Fetch);
                    return;
                }
                _tripSink = ChooseSink(task, item);
                if (_tripSink == null) { Wait("nowhere_to_put_it"); return; }
            }
            if (_tripSink == null) { Enter(Phase.Deliver); return; }

            var field = _tripSink.Kind == EndpointKind.Field;
            if (!Arrive(dt, _tripSink.CurrentPos(), _tripSink.Kind == EndpointKind.GroundDrop ? 1.2f : field ? 0.9f : Plugin.StationReach.Value)) return;
            AI.LookAt(_tripSink.CurrentPos());
            if (!Act(dt, field ? "attack" : "throw")) return; // toss it in (or tuck the seed in)

            bool ok;
            string blocked;
            Acting = true;
            try { ok = Sinks.PutOne(_tripSink, item, out blocked); }
            finally { Acting = false; }
            if (!ok) { Wait(blocked); return; }

            if (_tripSink.Kind == EndpointKind.Container) ChestSound(_tripSink);
            var left = _g.CarryCount - 1;
            _g.SetCarry(item, left);
            _g.Event("deposit", $"item={item} to={_tripSink} carrying={left}");
            _lastWaitReason = null;
            if (left > 0)
            {
                // seeds: on to the next spot in the row
                if (field && (_tripSink = ChooseSink(task, item)) == null) Wait("nowhere_to_put_it");
                return;
            }
            _tripSink = null;
            if (task == _chore && --_choreTrips <= 0) { EndChore("done"); return; }
            Enter(Phase.Fetch);
        }

        /// <summary>
        /// Does it have the tool this step needs? If not, go and fetch one from a chest near the work
        /// (the job's own chest first), or wait for someone to hand it one.
        /// </summary>
        private bool HaveTool(LaborTask task)
        {
            var need = Tools.Needed(task);
            if (need == null || _g.Tool == need) return true;
            var chest = Tools.FindInChests(need, task.Source.CurrentPos(), task.Source);
            if (chest && StartToolFetch(chest, need)) return false;
            Wait("needs_" + need.ToLowerInvariant());
            return false;
        }

        private bool StartToolFetch(Container chest, string tool)
        {
            _prank = Prank.FetchTool(_g, chest, tool);
            _g.Event("mischief", $"kind={_prank.Kind} {_prank.Detail}");
            return true;
        }

        /// <summary>Where this trip's item goes. Seeds left over when the field is full go back where they came from.</summary>
        private Endpoint ChooseSink(LaborTask task, string item)
        {
            var sink = Sinks.Choose(task, item, _g.transform.position);
            if (sink == null && task.Sink.Kind == EndpointKind.Field && task.Source.Kind == EndpointKind.Container && Sinks.HasRoom(task.Source, item))
            {
                _g.Event("field_full", $"returning={_g.CarryCount}");
                return task.Source;
            }
            return sink;
        }

        // ---- chores: useful things it does by itself -----------------------------------------

        internal void StartChore(string kind, LaborTask task)
        {
            if (!Roles.CanLearn(_g.Role, task, out _)) return;
            _chore = task;
            _choreKind = kind;
            _choreTrips = Mathf.Max(1, Plugin.ChoreTrips.Value);
            _tripSink = null;
            Enter(Phase.Fetch);
            _g.Event("chore", $"kind={kind} task=\"{task}\"");
        }

        private void EndChore(string why)
        {
            _g.Event("chore_done", $"kind={_choreKind} reason={why}");
            _chore = null;
            _tripSink = null;
            if (_g.CarryCount > 0) DropCarry("chore_over");
            Enter(Phase.Fetch);
        }

        /// <summary>Find something useful to do: a habit it picked up, helping a busy friend, or tidying up.</summary>
        private bool TryChore()
        {
            var options = Chores.Find(_g).ToList();
            if (options.Count == 0) return false;
            var (kind, task) = options[Random.Range(0, options.Count)];
            StartChore(kind, task);
            return true;
        }

        // ---- every couple of seconds: needs, mischief, chores --------------------------------

        private float _nextMealTry;

        /// <summary>Is it free to go and do something else (idle, or stuck waiting with empty hands)?</summary>
        private bool Free =>
            _prank == null && _chore == null && !_g.FollowTarget && !_g.Scared && !AI.GetTargetCreature()
            && (_g.State == GreenlingState.Idle || (_g.State == GreenlingState.Working && _phase == Phase.Wait && _g.CarryCount == 0 && _phaseTime > 8f));

        internal void Think()
        {
            if (_g.Scared || AI.GetTargetCreature()) return;
            if (CheckNeeds()) return;
            if (_g.Role == Role.Shaman && !_g.IsStray && ShamanDuty()) return;
            if (!Free) return;

            if (_g.IsStray) { MaybeMisbehave(); return; }

            var player = Player.GetClosestPlayer(_g.transform.position, 12f);
            if (_g.State == GreenlingState.Idle && player && !Needs.Hungry(_g) && Random.value < Plugin.NaturalCuriosity.Value * Traits.Curiosity(_g))
            {
                _g.NView.InvokeRPC("GL_StartWatch", player.GetZDOID(), true);
                return;
            }
            if (Random.value < Plugin.ChoreChance.Value * Traits.Diligence(_g))
            {
                if (Random.value < 0.5f && Tools.CanUse(_g, Tools.Hammer, out _))
                {
                    if (_g.Tool == Tools.Hammer && (_prank = Prank.Repair(_g)) != null)
                    {
                        _g.Event("mischief", $"kind=repair {_prank.Detail}");
                        return;
                    }
                    // something needs fixing and there's a hammer in a chest nearby
                    if (_g.Tool != Tools.Hammer && Prank.AnythingToRepair(_g.transform.position)
                        && Tools.FindInChests(Tools.Hammer, _g.transform.position) is Container chest && StartToolFetch(chest, Tools.Hammer))
                        return;
                }
                if (TryChore()) return;
            }
            if (Random.value < Plugin.RestChance.Value && !_g.HomelessCached)
            {
                _prank = Prank.Rest(_g);
                if (_prank != null) { _g.Event("mischief", $"kind={_prank.Kind} {_prank.Detail}".TrimEnd()); return; }
            }
            MaybeMisbehave();
        }

        /// <summary>Shamans heal whoever is hurt (between trips, even mid-job), and soothe strays when idle.</summary>
        private bool ShamanDuty()
        {
            if (_prank != null || _chore != null || _g.CarryCount > 0 || _g.State == GreenlingState.Watching || _g.FollowTarget) return false;
            var p = Prank.Heal(_g);
            if (p == null && Free && Random.value < Plugin.SootheChance.Value) p = Prank.Soothe(_g);
            if (p == null) return false;
            _prank = p;
            _g.Event("mischief", $"kind={p.Kind} {p.Detail}");
            return true;
        }

        /// <summary>Go eat or go to bed when needed (never mid-lesson or mid-carry). True if it went.</summary>
        private bool CheckNeeds()
        {
            if (_prank != null || _g.State == GreenlingState.Watching || _g.CarryCount > 0 || _g.FollowTarget) return false;

            if (Plugin.SleepAtNight.Value && Needs.IsNight && !_g.HomelessCached)
            {
                var nest = Nest.Assign(_g);
                if (!nest) return false;
                _chore = null;
                _prank = Prank.Sleep(_g, nest);
                _g.Event("sleep", "nest=" + _prank.Detail);
                return true;
            }
            if (_g.WantsFood && !_g.IsStray && Time.time > _nextMealTry)
            {
                _nextMealTry = Time.time + 20f;
                var food = Needs.FindFood(_g.transform.position);
                if (!food) { _g.Event("no_food"); return false; }
                _chore = null;
                _prank = Prank.Meal(_g, food);
                _g.Event("meal_seek", $"item={Utils.GetPrefabName(food.gameObject)}");
                return true;
            }
            return false;
        }

        internal void MaybeMisbehave()
        {
            var chance = Plugin.MischiefChance.Value * (_g.IsStray ? 2f : 1f) * Traits.Mischief(_g);
            if (Random.value < chance) StartPrank();
        }

        internal void StartPrank(string kind = null, bool force = false)
        {
            if (_prank != null && !force) return;
            if (_prank != null) StopPrank("replaced");
            _prank = Prank.Pick(_g, kind);
            if (_prank == null) return;
            _g.Event("mischief", $"kind={_prank.Kind} {_prank.Detail}".TrimEnd());
            if (_prank.Kind == "huddle") Prank.InviteToHuddle(_g, _prank);
            else if (_prank.Kind == "tag") _prank.Target?.Brain.Join(Prank.RunFrom(_prank.Target, _g), "tag");
            else if (_prank.Kind != "watch" && _prank.Kind != "chase") DrawAudience(_prank.Kind);
        }

        private void StopPrank(string why)
        {
            _g.Event("mischief_done", $"kind={_prank.Kind} reason={why}");
            _prank.Stop();
            _prank = null;
            if (_g.CarryCount > 0) DropCarry(why);
        }

        /// <summary>Another greenling pulls this one into something (a game of tag, a huddle).</summary>
        internal bool Join(Prank p, string why)
        {
            if (p == null || !_g.IsOwner || _prank != null || _g.State == GreenlingState.Watching || _g.CarryCount > 0 || _g.Scared) return false;
            _prank = p;
            _g.Event("mischief", $"kind={p.Kind} {p.Detail} because={why}".Replace("  ", " "));
            return true;
        }

        /// <summary>Another greenling is up to something: nearby idle ones may stop to watch, or join a chase.</summary>
        internal void DrawAudience(string what)
        {
            foreach (var other in Greenling.All.Where(o => o && o != _g && o.IsGreenling && o.IsOwner && o.State == GreenlingState.Idle
                                                         && !o.Brain.Busy && Vector3.Distance(o.transform.position, _g.transform.position) < 12f))
            {
                if (Random.value > Plugin.AudienceChance.Value) continue;
                other.Brain.Join(what == "zoomies" ? Prank.Chase(other, _g) : Prank.Watch(other, _g), what);
            }
        }

        // ---- danger ----------------------------------------------------------------------------

        private Transform _refuge;
        private string _fleeMode;
        private Vector3 _threatPos;
        private float _panicRetarget;
        private Vector3 _panicTo;

        /// <summary>Owner, every frame: true when it is scared and has handled moving this frame.</summary>
        internal bool TickDanger(float dt)
        {
            var flees = Danger.Flees(_g);
            var threat = flees ? Danger.ThreatNear(_g) : null;
            if (threat || (flees && AI.m_timeSinceHurt < 0.5f))
            {
                _threatPos = threat ? threat.transform.position : _g.transform.position - _g.transform.forward * 3f;
                if (!_g.Scared || _fleeMode == null) StartFleeing(threat);
                if (Time.frameCount % 30 == 0) _g.SetScared(Plugin.ScaredTime.Value);
            }
            if (!_g.Scared)
            {
                if (_fleeMode != null) { _g.Event("calm", "reason=timeout"); _fleeMode = null; }
                return false;
            }
            if (_fleeMode == null) StartFleeing(null); // e.g. scared on another client, then handed over

            if (_fleeMode == "panic")
            {
                if (threat) AI.Flee(dt, threat.transform.position);
                else
                {
                    // dart about: no danger in sight, but it hasn't calmed down yet
                    if (Time.time > _panicRetarget) { _panicRetarget = Time.time + Random.Range(1f, 2.5f); _panicTo = Prank.RandomPointNear(_g.transform.position, 3f, 7f); }
                    AI.MoveTo(dt, _panicTo, 1f, true);
                }
                return true;
            }
            if (!_refuge) { _fleeMode = "panic"; return true; }
            // hide behind the protector, away from the danger
            var away = _refuge.position - _threatPos;
            away.y = 0f;
            var spot = _refuge.position + (away.sqrMagnitude > 0.01f ? away.normalized : -_refuge.forward) * 1.6f;
            if (AI.MoveTo(dt, spot, 0.8f, true)) AI.LookAt(threat ? threat.transform.position : _refuge.position);
            return true;
        }

        private void StartFleeing(Character threat)
        {
            if (_prank != null) StopPrank("scared");
            if (_g.CarryCount > 0) DropCarry("scared");
            _g.SetScared(Plugin.ScaredTime.Value);
            var (refuge, kind) = Danger.Refuge(_g);
            // some just lose their heads (cuddly ones always run to you)
            if (refuge && !_g.Has(Traits.Cuddly) && Random.value < 0.25f) { refuge = null; kind = "panic"; }
            _refuge = refuge;
            _fleeMode = kind;
            _g.Event("scared", $"to={kind} threat={(threat ? Utils.GetPrefabName(threat.gameObject) : "hurt")}");
        }

        // ---- helpers -----------------------------------------------------------------------

        private Door _door;
        private float _nextDoorLook;
        private float _slowTime;

        /// <summary>
        /// Walk toward a target; true once there. MoveTo also says "done" when there is no path at
        /// all, so check the distance: if it can't get there, look for a door to open.
        /// </summary>
        private bool Arrive(float dt, Vector3 target, float reach)
        {
            if (_door)
            {
                if (Doors.IsOpen(_door)) { _door = null; return false; }
                var at = _door.transform.position + (_g.transform.position - _door.transform.position).normalized * 0.9f;
                if (AI.MoveTo(dt, at, 1.3f, false) || Vector3.Distance(_g.transform.position, _door.transform.position) < 2f)
                {
                    Doors.Open(_door, _g);
                    _door = null;
                }
                else if (_phaseTime > Plugin.StuckTimeout.Value) _door = null;
                return false;
            }

            var done = AI.MoveTo(dt, target, reach, false);
            var dist = Utils.DistanceXZ(target, _g.transform.position);
            if (done && dist <= reach + 1f) return true;

            if (Plugin.OpenDoors.Value)
            {
                // walking into a closed door, or no path at all: find the door in the way
                var slow = AI.m_character.GetVelocity().magnitude < 0.3f;
                _slowTime = slow ? _slowTime + dt : 0f;
                if ((done || _slowTime > 1f) && Time.time > _nextDoorLook)
                {
                    _nextDoorLook = Time.time + 2f;
                    var door = Doors.ClosedDoorAt(_g.transform.position) ?? (done ? Doors.ClosedDoorBetween(_g.transform.position, target) : null);
                    if (door) { _door = door; return false; }
                }
            }

            // Stations carve holes in the navmesh; accept "close enough" when progress stalls.
            if (_phaseTime > 4f && dist < reach + 1.5f && AI.m_character.GetVelocity().magnitude < 0.2f) return true;
            if (_phaseTime > Plugin.StuckTimeout.Value) Wait(done ? "no_path" : "stuck");
            return false;
        }

        private void Enter(Phase p)
        {
            _phase = p;
            _phaseTime = 0f;
            _phaseStart = true;
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
    /// Takes over MonsterAI for greenlings that have something to do. Fear comes first; fights
    /// (brutes and healthy greydwarfs against monsters) fall through to vanilla.
    /// </summary>
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    internal static class BrainHook
    {
        private static bool Prefix(MonsterAI __instance, float dt, ref bool __result)
        {
            var g = __instance.GetComponent<Greenling>();
            if (!g || !g.IsGreenling || !g.IsOwner) return true;

            __instance.UpdateTarget((Humanoid)__instance.m_character, dt, out _, out _);
            var scared = g.Brain.TickDanger(dt);
            if (!scared && (__instance.m_targetCreature != null || __instance.m_timeSinceHurt < 5f || !g.Brain.WantsControl)) return true;

            // what BaseAI.UpdateAI does for the owner, minus the bits only vanilla movement needs
            __instance.UpdateRegeneration(dt);
            __instance.m_timeSinceHurt += dt;
            __result = scared || g.Brain.Tick(dt);
            return false;
        }
    }
}
