using System.Collections.Generic;
using System.Linq;
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
        internal const string ZState = "gl_state", ZCuriosity = "gl_curiosity", ZMoodTarget = "gl_mood",
            ZTeacher = "gl_teacher", ZAttention = "gl_attention", ZTask = "gl_task",
            ZCarryItem = "gl_carry_item", ZCarryCount = "gl_carry_n",
            ZBond = "gl_bond", ZLastPat = "gl_last_pat", ZLastFed = "gl_last_fed", ZTraits = "gl_traits";

        public Character Character { get; private set; }
        public MonsterAI AI { get; private set; }
        public Tameable Tameable { get; private set; }
        public ZNetView NView { get; private set; }
        public GreenlingBrain Brain { get; private set; }
        public Role Role { get; private set; }
        public int CarryPerTrip => Roles.CarryPerTrip(Role);

        private string _wildName;
        private float _appliedTint = -1f;
        private bool _wasStray;
        private LaborTask _taskCache;
        private string _taskCacheKey;

        public bool IsGreenling => Character && Character.IsTamed();
        public ZDO Zdo => NView && NView.IsValid() ? NView.GetZDO() : null;
        public bool IsOwner => NView && NView.IsValid() && NView.IsOwner();

        public GreenlingState State => (GreenlingState)(Zdo?.GetInt(ZState) ?? 0);
        public float Curiosity => Zdo?.GetFloat(ZCuriosity, 40f) ?? 0f;
        public ZDOID Teacher => Zdo?.GetZDOID(ZTeacher) ?? ZDOID.None;
        public float Attention => Zdo?.GetFloat(ZAttention) ?? 0f;
        /// <summary>
        /// Trust, 0-100. A freshly tamed greyling is a Stray (bond 0); at 100 it is a teachable
        /// Greenling. Missing value = 100 so greenlings tamed before bonding existed stay teachable.
        /// </summary>
        public float Bond => Zdo?.GetFloat(ZBond, 100f) ?? 0f;
        public bool IsStray => IsGreenling && Bond < 100f;
        /// <summary>Updated every couple of seconds (finding nests isn't free).</summary>
        public bool HomelessCached { get; private set; }
        public string TraitList => Zdo?.GetString(ZTraits) ?? "";
        public bool Has(string trait) => TraitList.Split(',').Contains(trait);
        public bool IsBonded => IsGreenling && Bond >= 100f;
        public string CarryItem => Zdo?.GetString(ZCarryItem) ?? "";
        public int CarryCount => Zdo?.GetInt(ZCarryCount) ?? 0;

        public LaborTask Task
        {
            get
            {
                var s = Zdo?.GetString(ZTask) ?? "";
                if (s != _taskCacheKey) { _taskCacheKey = s; _taskCache = LaborTask.Parse(s); }
                return _taskCache;
            }
        }

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
                NView.Register("GL_Gift", RPC_Gift);
                NView.Register<ZDOID>("GL_StartWatch", RPC_StartWatch);
                NView.Register<string>("GL_Learn", RPC_Learn);
                NView.Register("GL_Dismiss", RPC_Dismiss);
                NView.Register("GL_Swat", RPC_Swat);
                NView.Register("GL_Burst", RPC_Burst);
                NView.Register<ZDOID>("GL_Affection", RPC_Affection);
                NView.Register<string, ZDOID>("GL_Feed", RPC_Feed);
                NView.Register<string, string, ZDOID>("GL_Mimic", RPC_Mimic);
            }
            AI.m_onConsumedItem += item => Event("ate", $"item={Utils.GetPrefabName(item.gameObject)} tameness={Tameable.GetTameness()}%");
            InvokeRepeating(nameof(Refresh), 0f, 1f);
            InvokeRepeating(nameof(DriftMood), 2f, 2f);
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
            Brain.OnStateChanged(s);
        }

        internal void AddCuriosity(float delta, string reason)
        {
            if (!IsOwner) return;
            var v = Mathf.Clamp(Curiosity + delta, 0f, 100f);
            Zdo.Set(ZCuriosity, v);
            Event("curiosity", $"delta={delta:+0;-0} now={v:0} reason={reason}");
        }

        internal void SetCarry(string item, int n)
        {
            Zdo.Set(ZCarryItem, n > 0 ? item : "");
            Zdo.Set(ZCarryCount, n);
        }

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

        /// <summary>Curiosity wanders toward a mood target that changes on a whim.</summary>
        private void DriftMood()
        {
            if (!IsGreenling) return;
            HomelessCached = Needs.Homeless(this);
            if (!IsOwner) return;
            if (Zdo.GetLong(ZLastFed, 0L) == 0L) Zdo.Set(ZLastFed, ZNet.instance.GetTime().Ticks); // start out fed
            Brain.CheckNeeds();
            if (IsStray && Player.GetClosestPlayer(transform.position, Plugin.BondNearRange.Value))
                AddBond(Plugin.BondNearPerMinute.Value / 30f, "nearby"); // called every 2s
            var target = Zdo.GetFloat(ZMoodTarget, 40f);
            if (Random.value < 0.03f) // a new whim every ~minute on average
            {
                target = Random.Range(Plugin.MoodMin.Value, Plugin.MoodMax.Value);
                Zdo.Set(ZMoodTarget, target);
            }
            var cur = Curiosity;
            Zdo.Set(ZCuriosity, Mathf.Clamp(cur + (target - cur) * 0.04f + Random.Range(-1f, 1f), 0f, 100f));
            Brain.MaybeMisbehave();
        }

        // ---- RPC handlers (run on the owner) -----------------------------------------------

        private void RPC_Gift(long sender)
        {
            if (!IsOwner) return;
            AddCuriosity(Plugin.CoinCuriosity.Value * Traits.CoinCuriosity(this), "coin");
        }

        private float _lastMimic;

        private void RPC_Mimic(long sender, string action, string anim, ZDOID player)
        {
            if (!IsOwner || !IsGreenling || State != GreenlingState.Idle || Brain.Busy) return;
            if (Time.time - _lastMimic < 3f || Random.value > Traits.MimicChance(this)) return;
            _lastMimic = Time.time;
            StartCoroutine(Mimicry.Copy(this, action, anim));
        }

        /// <summary>A pat on a stray: affection, on a cooldown so it can't be spammed.</summary>
        private void RPC_Affection(long sender, ZDOID player)
        {
            if (!IsOwner || !IsStray) return;
            if (!CooledDown(Zdo, ZLastPat, Plugin.PatCooldown.Value * Traits.PatCooldown(this))) { Event("pat_cooldown"); return; }
            Zdo.Set(ZLastPat, ZNet.instance.GetTime().Ticks);
            AddBond(Plugin.PatBond.Value * Traits.PatBond(this), "pat");
        }

        /// <summary>Food (not coins) builds trust while it is a stray, and lifts its mood once bonded.</summary>
        private void RPC_Feed(long sender, string item, ZDOID player)
        {
            if (!IsOwner) return;
            Zdo.Set(ZLastFed, ZNet.instance.GetTime().Ticks);
            Event("fed", $"item={item}");
            if (IsStray) AddBond(Plugin.FoodBond.Value * Traits.FoodBond(this), "food");
            else AddCuriosity(Plugin.FoodCuriosity.Value, "food");
        }

        /// <summary>Whether this greenling would eat right now (feeding has a cooldown: it gets full).</summary>
        public bool Hungry => Zdo != null && CooledDown(Zdo, ZLastFed, Plugin.FeedCooldown.Value);

        private void RPC_StartWatch(long sender, ZDOID teacher)
        {
            if (!IsOwner) return;
            if (IsStray) { Event("refuse", "reason=stray"); return; }
            if (Curiosity < Plugin.LearnThreshold.Value)
            {
                Event("refuse", $"curiosity={Curiosity:0}");
                return;
            }
            Zdo.Set(ZTeacher, teacher);
            Zdo.Set(ZAttention, Plugin.AttentionSpan.Value * Traits.Attention(this));
            SetState(GreenlingState.Watching, "pat");
        }

        private void RPC_Learn(long sender, string taskString)
        {
            if (!IsOwner || State != GreenlingState.Watching) return;
            var task = LaborTask.Parse(taskString);
            Zdo.Set(ZTask, taskString);
            Event("learned", $"task=\"{task}\"");
            AddCuriosity(-Plugin.LearnCost.Value, "learned");
            SetState(GreenlingState.Working, "learned");
            NView.InvokeRPC(ZRoutedRpc.Everybody, "GL_Burst");
            Brain.DrawAudience("learned");
        }

        private void RPC_Dismiss(long sender)
        {
            if (!IsOwner || State != GreenlingState.Watching) return;
            if (Task != null) Event("forgot", $"task=\"{Task}\"");
            Zdo.Set(ZTask, "");
            SetState(GreenlingState.Idle, "dismissed");
        }

        /// <summary>Refused pat: swipe at the hand (no damage) and grumble. Sent to everybody.</summary>
        private void RPC_Swat(long sender)
        {
            if (IsOwner) Character.m_zanim.SetTrigger("attack"); // synced animation, owner drives it
            Effects.Sound("sfx_greyling_alerted", Character.GetCenterPoint());
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
            // greenlings tamed before traits existed get theirs now
            if (IsGreenling && IsOwner && TraitList.Length == 0) Zdo.Set(ZTraits, Traits.Roll());

            // tell nearby players when a stray comes to trust them (every client checks for itself)
            var stray = IsStray;
            if (_wasStray && !stray && IsGreenling && Player.m_localPlayer
                && Vector3.Distance(Player.m_localPlayer.transform.position, transform.position) < 25f)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, $"{GetName()} trusts you now. Pat it to teach it something.");
            _wasStray = stray;

            var green = Greenness;
            if (Mathf.Approximately(green, _appliedTint)) return;
            _appliedTint = green;

            Character.m_name = IsGreenling ? DisplayName : _wildName;
            var block = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMaterial == null || r.sharedMaterial.shader.name != "Custom/Creature") continue; // skip eyes
                r.GetPropertyBlock(block);
                if (green > 0f) block.SetColor(ColorId, Color.Lerp(Color.white, Plugin.TamedTint.Value, green));
                else block.Clear();
                r.SetPropertyBlock(block);
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(Tameable), nameof(Tameable.Tame))]
        private static class TamedEvent
        {
            private static void Postfix(Tameable __instance)
            {
                if (!(__instance.GetComponent<Greenling>() is Greenling g) || !g.IsGreenling || !g.IsOwner) return;
                // a new stray: no trust yet, and a name of its own
                g.Zdo.Set(ZBond, Plugin.StartBond.Value);
                g.Zdo.Set(ZTraits, Traits.Roll());
                g.Zdo.Set(ZLastFed, ZNet.instance.GetTime().Ticks);
                if (__instance.GetText().Length == 0) __instance.SetText(Names.Generate());
                g.Event("tamed", $"name={__instance.GetText()} bond={g.Bond:0} traits={g.TraitList}");
            }
        }

        public string MoodWord()
        {
            if (IsStray) return $"stray, trust {Bond:0}%";
            var needs = Needs.Describe(this);
            if (needs.Length > 0) return needs;
            var c = Curiosity;
            if (c >= 80) return "eager";
            if (c >= Plugin.LearnThreshold.Value) return "curious";
            if (c >= 25) return "bored";
            return "sulky";
        }
    }
}
