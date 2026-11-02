using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    public enum GreenlingState { Idle = 0, Watching = 1, Working = 2 }

    /// <summary>
    /// Lives on every greyling instance. Once tamed it is a Greenling: green, renamed, and
    /// driven by <see cref="GreenlingBrain"/> on the owning client.
    ///
    /// All state lives in the ZDO so every client (and a relog) sees the same thing. Only the
    /// owner writes it; other clients ask the owner through RPCs.
    /// </summary>
    public class Greenling : MonoBehaviour
    {
        public const string DisplayName = "Greenling";
        public static readonly List<Greenling> All = new List<Greenling>();

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        internal const string ZState = "gl_state", ZTeacher = "gl_teacher", ZAttention = "gl_attention", ZTask = "gl_task",
            ZCasual = "gl_casual", ZAttentiveUntil = "gl_attentive", ZHabits = "gl_habits",
            ZCarryItem = "gl_carry_item", ZCarryCount = "gl_carry_n",
            ZBond = "gl_bond", ZLastPat = "gl_last_pat", ZLastFed = "gl_last_fed", ZTraits = "gl_traits",
            ZFollow = "gl_follow", ZFollowUntil = "gl_follow_until", ZScaredUntil = "gl_scared", ZTool = "gl_tool",
            ZLastSoothe = "gl_last_soothe";

        public Character Character { get; private set; }
        public MonsterAI AI { get; private set; }
        public Tameable Tameable { get; private set; }
        public ZNetView NView { get; private set; }
        public GreenlingBrain Brain { get; private set; }
        public Role Role { get; private set; }
        public int CarryPerTrip => Roles.CarryPerTrip(Role);

        private string _wildName;
        private float _appliedTint = -1f;

        /// <summary>Re-apply the skin tint on the next refresh (the remodel swapped meshes or textures).</summary>
        internal void InvalidateTint() => _appliedTint = -1f;
        private bool _wasStray;
        private List<LaborTask> _jobCache = new List<LaborTask>();
        private string _jobCacheKey;

        public bool IsGreenling => Character && Character.IsTamed();
        public ZDO Zdo => NView && NView.IsValid() ? NView.GetZDO() : null;
        public bool IsOwner => NView && NView.IsValid() && NView.IsOwner();

        public GreenlingState State => (GreenlingState)(Zdo?.GetInt(ZState) ?? 0);
        public ZDOID Teacher => Zdo?.GetZDOID(ZTeacher) ?? ZDOID.None;
        public float Attention => Zdo?.GetFloat(ZAttention) ?? 0f;
        /// <summary>Watching because it felt like it, not because it was paid: it only picks up simple chores.</summary>
        public bool CasualWatch => State == GreenlingState.Watching && (Zdo?.GetBool(ZCasual) ?? false);
        /// <summary>Just learned something and still paying attention to its teacher (corrections, extra steps).</summary>
        public bool Attentive => State == GreenlingState.Working && Now < (Zdo?.GetLong(ZAttentiveUntil, 0L) ?? 0L);
        /// <summary>
        /// Trust, 0-100. A freshly tamed greyling is a Stray (bond 0); at 100 it is a teachable
        /// Greenling. Missing value = 100 so greenlings tamed before bonding existed stay teachable.
        /// </summary>
        public float Bond => Zdo?.GetFloat(ZBond, 100f) ?? 0f;
        public bool IsStray => IsGreenling && Bond < 100f;
        public bool IsBonded => IsGreenling && Bond >= 100f;
        /// <summary>Updated every couple of seconds (finding nests isn't free).</summary>
        public bool HomelessCached { get; private set; }
        public string TraitList => Zdo?.GetString(ZTraits) ?? "";
        public bool Has(string trait) => TraitList.Split(',').Contains(trait);
        public string CarryItem => Zdo?.GetString(ZCarryItem) ?? "";
        public int CarryCount => Zdo?.GetInt(ZCarryCount) ?? 0;
        public string Tool => Zdo?.GetString(ZTool) ?? "";
        public bool Scared => Now < (Zdo?.GetLong(ZScaredUntil, 0L) ?? 0L);

        /// <summary>The player it is following, or null (following ends by itself when it was only tagging along).</summary>
        public Player FollowTarget
        {
            get
            {
                var z = Zdo;
                if (z == null) return null;
                var id = z.GetZDOID(ZFollow);
                if (id.IsNone()) return null;
                var until = z.GetLong(ZFollowUntil, 0L);
                if (until != 0L && Now > until) return null;
                return ZNetScene.instance.FindInstance(id)?.GetComponent<Player>();
            }
        }

        internal static long Now => ZNet.instance ? ZNet.instance.GetTime().Ticks : 0L;
        internal static long Ticks(float seconds) => (long)(seconds * System.TimeSpan.TicksPerSecond);

        /// <summary>Its job: one or more steps ("take X from here, put it there"), done in turn.</summary>
        public List<LaborTask> Job
        {
            get
            {
                var s = Zdo?.GetString(ZTask) ?? "";
                if (s != _jobCacheKey) { _jobCacheKey = s; _jobCache = LaborTask.ParseJob(s); }
                return _jobCache;
            }
        }

        public List<LaborTask> Habits => LaborTask.ParseJob(Zdo?.GetString(ZHabits) ?? "");

        public string Label => $"{GetName()}#{(Zdo != null ? Zdo.m_uid.ID : 0)}";
        public string GetName() =>
            !IsGreenling ? Localization.instance.Localize(_wildName ?? Character.m_name)
            : Tameable && Tameable.GetText().Length > 0 ? Tameable.GetText() : DisplayName;

        private void Awake()
        {
            Character = GetComponent<Character>();
            AI = GetComponent<MonsterAI>();
            Tameable = GetComponent<Tameable>();
            NView = GetComponent<ZNetView>();
            Role = Roles.Of(gameObject);
            Brain = new GreenlingBrain(this);
            _wildName = Character.m_name;
        }

        private void Start()
        {
            if (NView.IsValid())
            {
                NView.Register<ZDOID, bool>("GL_StartWatch", RPC_StartWatch);
                NView.Register<string>("GL_Learn", RPC_Learn);
                NView.Register<string>("GL_Habit", RPC_Habit);
                NView.Register("GL_Attend", RPC_Attend);
                NView.Register("GL_Dismiss", RPC_Dismiss);
                NView.Register("GL_Burst", RPC_Burst);
                NView.Register<ZDOID>("GL_Pat", RPC_Pat);
                NView.Register<string, ZDOID>("GL_Feed", RPC_Feed);
                NView.Register<string, string, ZDOID>("GL_Mimic", RPC_Mimic);
                NView.Register<string>("GL_Tool", RPC_Tool);
                NView.Register<ZDOID>("GL_Soothe", RPC_Soothe);
            }
            AI.m_onConsumedItem += OnAte;
            InvokeRepeating(nameof(Refresh), 0f, 1f);
            InvokeRepeating(nameof(Think), 2f, 2f);
        }

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        // ---- events (structured log lines for tests and debugging) -------------------------

        internal void Event(string kind, string details = "") =>
            Plugin.Log.LogInfo($"[GLEVT] {kind} gl={Label} {details}".TrimEnd());

        // ---- owner-side state changes ------------------------------------------------------

        internal void SetState(GreenlingState s, string reason)
        {
            if (!IsOwner || State == s) return;
            Event("state", $"from={State} to={s} reason={reason}");
            Zdo.Set(ZState, (int)s);
            if (s != GreenlingState.Watching) Zdo.Set(ZCasual, false);
            Brain.OnStateChanged(s);
        }

        internal void SetCarry(string item, int n)
        {
            Zdo.Set(ZCarryItem, n > 0 ? item : "");
            Zdo.Set(ZCarryCount, n);
        }

        internal void SetJob(List<LaborTask> job) => Zdo.Set(ZTask, LaborTask.SerializeJob(job));

        internal void Follow(Player p, float seconds)
        {
            if (!IsOwner) return;
            Zdo.Set(ZFollow, p ? p.GetZDOID() : ZDOID.None);
            Zdo.Set(ZFollowUntil, p && seconds > 0f ? Now + Ticks(seconds) : 0L);
        }

        internal void SetScared(float seconds) => Zdo.Set(ZScaredUntil, seconds > 0f ? Now + Ticks(seconds) : 0L);

        internal void AddBond(float delta, string reason)
        {
            if (!IsOwner || !IsStray || delta <= 0f) return;
            var v = Mathf.Min(100f, Bond + delta);
            Zdo.Set(ZBond, v);
            if (reason != "nearby") Event("bond", $"delta=+{delta:0.#} now={v:0} reason={reason}");
            if (v >= 100f)
            {
                Event("bonded");
                NView.InvokeRPC(ZRoutedRpc.Everybody, "GL_Burst");
            }
        }

        private static bool CooledDown(ZDO zdo, string key, float seconds) =>
            (ZNet.instance.GetTime() - new System.DateTime(zdo.GetLong(key, 0L))).TotalSeconds >= seconds;

        /// <summary>Every couple of seconds: needs, trust from company, and what to do with itself.</summary>
        private void Think()
        {
            if (!IsGreenling) return;
            HomelessCached = Needs.Homeless(this);
            UpdateDiet();
            if (!IsOwner) return;
            if (!IsStray && Zdo.GetLong(ZLastFed, 0L) == 0L) Zdo.Set(ZLastFed, Now); // start out fed
            if (IsStray)
            {
                // company: yours, and a trusting shaman's (they help strays settle in)
                var company = (Player.GetClosestPlayer(transform.position, Plugin.BondNearRange.Value) ? 1f : 0f)
                              + (Shamans.Near(this, Plugin.BondNearRange.Value) ? Plugin.ShamanCompany.Value : 0f);
                if (company > 0f) AddBond(Plugin.BondNearPerMinute.Value / 30f * company, "nearby"); // called every 2s
            }
            Brain.Think();
        }

        // ---- eating ------------------------------------------------------------------------

        private bool _dietTamed;

        /// <summary>
        /// Wild ones eat the taming item off the ground (vanilla consume AI); greenlings eat real food
        /// lying around instead. The list is per instance, so switch it when tamed.
        /// </summary>
        private void UpdateDiet()
        {
            if (_dietTamed) return;
            _dietTamed = true;
            AI.m_consumeItems = Interaction.FoodPrefabs().ToList();
        }

        /// <summary>Whether it would eat right now: strays have a short "full" timer, greenlings eat at mealtimes.</summary>
        public bool WantsFood => Zdo != null && (IsStray ? CooledDown(Zdo, ZLastFed, Plugin.FeedCooldown.Value) : Needs.Hungry(this));

        /// <summary>Vanilla consume AI ate something off the ground (owner only).</summary>
        private void OnAte(ItemDrop item)
        {
            var prefab = Utils.GetPrefabName(item.gameObject);
            if (!IsGreenling) { Event("ate", $"item={prefab} tameness={Tameable.GetTameness()}%"); return; }
            Fed(prefab, null, "ground");
        }

        /// <summary>Owner: it ate something (handed over, off the ground, or on its own).</summary>
        internal void Fed(string item, Player by, string how)
        {
            Zdo.Set(ZLastFed, Now);
            Event("fed", $"item={item} how={how}");
            if (!IsStray) return;
            AddBond(Plugin.FoodBond.Value * Traits.FoodBond(this), "food");
            var p = by ? by : Player.GetClosestPlayer(transform.position, 15f);
            if (p && IsStray) TagAlong(p);
        }

        /// <summary>A stray tags along for a bit after a treat or a pat (easy to lead home).</summary>
        private void TagAlong(Player p)
        {
            if (Plugin.TagAlongTime.Value <= 0f || (FollowTarget && Zdo.GetLong(ZFollowUntil, 0L) == 0L)) return; // already following for good
            Follow(p, Plugin.TagAlongTime.Value);
            Event("tag_along", $"player={p.GetPlayerName()} for={Plugin.TagAlongTime.Value:0}s");
        }

        // ---- RPC handlers (run on the owner) -----------------------------------------------

        private float _lastMimic;

        private void RPC_Mimic(long sender, string action, string anim, ZDOID player)
        {
            if (!IsOwner || !IsGreenling || State != GreenlingState.Idle || Brain.Busy) return;
            if (Time.time - _lastMimic < 3f || Random.value > Traits.MimicChance(this)) return;
            _lastMimic = Time.time;
            StartCoroutine(Mimicry.Copy(this, action, anim));
        }

        /// <summary>
        /// E on a greenling. Calms a frightened one; on a stray it is affection (trust, on a cooldown)
        /// and it tags along; on a trusting one it toggles follow / stay.
        /// </summary>
        private void RPC_Pat(long sender, ZDOID playerId)
        {
            if (!IsOwner) return;
            var player = ZNetScene.instance.FindInstance(playerId)?.GetComponent<Player>();
            var fresh = CooledDown(Zdo, ZLastPat, Plugin.PatCooldown.Value * Traits.PatCooldown(this));
            if (fresh) Zdo.Set(ZLastPat, Now);
            Event(fresh ? "pat" : "pat_cooldown", $"stray={IsStray}");

            if (Scared && !Danger.ThreatNear(this))
            {
                SetScared(0f);
                Event("calm", "reason=pat");
                return;
            }
            if (IsStray)
            {
                if (fresh) AddBond(Plugin.PatBond.Value * Traits.PatBond(this), "pat");
                if (player) TagAlong(player);
                return;
            }
            if (CasualWatch) SetState(Job.Count > 0 ? GreenlingState.Working : GreenlingState.Idle, "pat");
            if (FollowTarget && Zdo.GetLong(ZFollowUntil, 0L) == 0L)
            {
                Follow(null, 0f);
                Event("stay", "reason=pat");
            }
            else if (player)
            {
                Follow(player, 0f);
                Event("follow", $"player={player.GetPlayerName()}");
            }
        }

        /// <summary>Whether a shaman may soothe this stray now (not soothed lately).</summary>
        public bool CanBeSoothed => IsStray && Zdo != null && CooledDown(Zdo, ZLastSoothe, Plugin.SootheCooldown.Value);

        /// <summary>A shaman soothes this stray: a little trust, and it calms down.</summary>
        private void RPC_Soothe(long sender, ZDOID shaman)
        {
            if (!IsOwner || !CanBeSoothed) return;
            Zdo.Set(ZLastSoothe, Now);
            Tameable.m_sootheEffect.Create(transform.position, transform.rotation);
            var by = ZNetScene.instance.FindInstance(shaman)?.GetComponent<Greenling>();
            Event("soothed", "by=" + (by ? by.Label : "?"));
            if (Scared && !Danger.ThreatNear(this)) SetScared(0f);
            AddBond(Plugin.SootheBond.Value, "shaman");
        }

        private void RPC_Feed(long sender, string item, ZDOID playerId) =>
            Fed(item, ZNetScene.instance.FindInstance(playerId)?.GetComponent<Player>(), "hand");

        /// <summary>Paid to pay attention (casual = it wandered over by itself).</summary>
        private void RPC_StartWatch(long sender, ZDOID teacher, bool casual)
        {
            if (!IsOwner) return;
            if (IsStray) { Event("refuse", "reason=stray"); return; }
            if (casual && State != GreenlingState.Idle) return;
            Zdo.Set(ZTeacher, teacher);
            Zdo.Set(ZAttention, casual ? Plugin.CasualAttention.Value : Plugin.AttentionSpan.Value * Traits.Attention(this));
            Zdo.Set(ZAttentiveUntil, 0L);
            Follow(null, 0f);
            SetState(GreenlingState.Watching, casual ? "curious" : "coin");
            Zdo.Set(ZCasual, casual);
        }

        /// <summary>The teacher just demonstrated something: worth paying attention a little longer.</summary>
        private void RPC_Attend(long sender)
        {
            if (!IsOwner || State != GreenlingState.Watching) return;
            var top = CasualWatch ? Plugin.CasualAttention.Value * 0.5f : Plugin.DemoAttention.Value;
            if (Attention < top) Zdo.Set(ZAttention, top);
        }

        /// <summary>
        /// A lesson. While it watches (or just after a lesson) every demonstration adds a step to its
        /// job, or replaces the step it corrects (same place to take from, or to put into).
        /// </summary>
        private void RPC_Learn(long sender, string taskString)
        {
            if (!IsOwner || !(State == GreenlingState.Watching || Attentive)) return;
            var task = LaborTask.Parse(taskString);
            // the first demonstration after a coin starts a new job; later ones (while attentive) build on it
            var job = State == GreenlingState.Watching ? new List<LaborTask>() : Job.ToList();
            var replaced = job.FindIndex(t => t.Corrects(task));
            if (replaced >= 0) job[replaced] = task;
            else
            {
                job.Add(task);
                while (job.Count > Mathf.Max(1, Plugin.MaxJobSteps.Value)) job.RemoveAt(0);
            }
            SetJob(job);
            Event("learned", $"task=\"{task}\" steps={job.Count}{(replaced >= 0 ? " corrected=" + replaced : "")}");
            Zdo.Set(ZAttentiveUntil, Now + Ticks(Plugin.AttentiveAfterLesson.Value));
            if (State != GreenlingState.Working) SetState(GreenlingState.Working, "learned");
            else Brain.OnJobChanged();
            NView.InvokeRPC(ZRoutedRpc.Everybody, "GL_Burst");
            Brain.DrawAudience("learned");
        }

        /// <summary>Picked up a simple chore by watching (no coin): keeps it as a habit and jumps in to help.</summary>
        private void RPC_Habit(long sender, string taskString)
        {
            if (!IsOwner || !CasualWatch) return;
            var task = LaborTask.Parse(taskString);
            if (task.Sink.Kind != EndpointKind.ToPlayer) // helping you forage is a one-off, not a habit
            {
                var habits = Habits;
                habits.RemoveAll(t => t.Corrects(task));
                habits.Add(task);
                while (habits.Count > 3) habits.RemoveAt(0);
                Zdo.Set(ZHabits, LaborTask.SerializeJob(habits));
            }
            Event("habit", $"task=\"{task}\"");
            NView.InvokeRPC(ZRoutedRpc.Everybody, "GL_Burst");
            SetState(Job.Count > 0 ? GreenlingState.Working : GreenlingState.Idle, "picked_up_chore");
            Brain.StartChore("habit", task);
        }

        private void RPC_Dismiss(long sender)
        {
            if (!IsOwner || State != GreenlingState.Watching) return;
            if (Job.Count > 0) Event("forgot", $"job=\"{string.Join(" ; ", Job)}\"");
            Zdo.Set(ZTask, "");
            SetState(GreenlingState.Idle, "dismissed");
        }

        /// <summary>Handed a tool (a hammer to repair with, a cultivator to plant with).</summary>
        private void RPC_Tool(long sender, string item)
        {
            if (!IsOwner) return;
            if (Tool.Length > 0) Prank.Drop(transform.position + transform.forward * 0.5f + Vector3.up, Tool, 1);
            Zdo.Set(ZTool, item);
            Event("tool", "item=" + item);
        }

        /// <summary>Burst of understanding, played on every client.</summary>
        private void RPC_Burst(long sender)
        {
            var head = Models.Find(transform, "head_end");
            Effects.Understanding(head ? head.position : Character.GetTopPoint());
        }

        // ---- visuals -----------------------------------------------------------------------

        /// <summary>How green it is, 0 (wild grey) to 1 (full Greenling): it greens up as trust grows.</summary>
        public float Greenness => !IsGreenling ? 0f : Mathf.Round(Mathf.Lerp(0.15f, 1f, Bond / 100f) * 20f) / 20f;

        private void Refresh()
        {
            if (!IsGreenling && Tameable) Tameable.m_tamingTime = Plugin.TamingTime.Value; // config changes apply to wild ones live
            // greenlings tamed before traits existed get theirs now
            if (IsGreenling && IsOwner && TraitList.Length == 0) Zdo.Set(ZTraits, Traits.Roll());

            // tell nearby players when a stray comes to trust them (every client checks for itself)
            var stray = IsStray;
            if (_wasStray && !stray && IsGreenling && Player.m_localPlayer
                && Vector3.Distance(Player.m_localPlayer.transform.position, transform.position) < 25f)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, $"{GetName()} trusts you now. Give it a coin to teach it something.");
            _wasStray = stray;

            var green = Greenness;
            if (Mathf.Approximately(green, _appliedTint)) return;
            _appliedTint = green;

            Character.m_name = IsGreenling ? DisplayName : _wildName;
            var block = new MaterialPropertyBlock();
            var remodelled = GetComponent<GreenlingModel>() is GreenlingModel model && model.Applied; // already painted green
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMaterial == null || r.sharedMaterial.shader.name != "Custom/Creature") continue; // skip eyes
                r.GetPropertyBlock(block); // keeps the remodel's texture
                block.SetColor(ColorId, green > 0f && !remodelled ? Color.Lerp(Color.white, Plugin.TamedTint.Value, green) : Color.white);
                r.SetPropertyBlock(block);
            }
        }

        [HarmonyPatch(typeof(Tameable), nameof(Tameable.Tame))]
        private static class TamedEvent
        {
            private static void Postfix(Tameable __instance)
            {
                if (!(__instance.GetComponent<Greenling>() is Greenling g) || !g.IsGreenling || !g.IsOwner) return;
                // a new stray: no trust yet, and a name of its own
                g.Zdo.Set(ZBond, Plugin.StartBond.Value);
                g.Zdo.Set(ZTraits, Traits.Roll());
                if (__instance.GetText().Length == 0) __instance.SetText(Names.Generate());
                g.Event("tamed", $"name={__instance.GetText()} bond={g.Bond:0} traits={g.TraitList}");
            }
        }

        /// <summary>Whatever it was carrying (and its hammer) falls to the ground when it dies.</summary>
        [HarmonyPatch(typeof(Character), "OnDeath")]
        private static class DropOnDeath
        {
            private static void Prefix(Character __instance)
            {
                if (!(__instance.GetComponent<Greenling>() is Greenling g) || !g.IsGreenling || !g.IsOwner) return;
                var at = g.transform.position + Vector3.up;
                if (g.CarryCount > 0) Prank.Drop(at, g.CarryItem, g.CarryCount);
                if (g.Tool.Length > 0) Prank.Drop(at, g.Tool, 1);
                g.Event("died", $"dropped={g.CarryItem}x{g.CarryCount} tool={g.Tool}");
            }
        }

        /// <summary>A short "how is it doing" for the hover text.</summary>
        public string Status()
        {
            if (Scared) return "frightened";
            if (IsStray) return $"stray, trust {Bond:0}%";
            if (State == GreenlingState.Watching) return CasualWatch ? "watching you curiously" : "watching you closely";
            var follow = FollowTarget;
            if (follow) return "following you";
            var doing = Brain.Doing();
            var needs = Needs.Describe(this);
            return needs.Length > 0 ? $"{doing}, {needs}" : doing;
        }
    }
}
