using System.Collections.Generic;
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
            ZCarryItem = "gl_carry_item", ZCarryCount = "gl_carry_n";

        public Character Character { get; private set; }
        public MonsterAI AI { get; private set; }
        public Tameable Tameable { get; private set; }
        public ZNetView NView { get; private set; }
        public GreenlingBrain Brain { get; private set; }

        private string _wildName;
        private bool _appliedTamed;
        private LaborTask _taskCache;
        private string _taskCacheKey;

        public bool IsGreenling => Character && Character.IsTamed();
        public ZDO Zdo => NView && NView.IsValid() ? NView.GetZDO() : null;
        public bool IsOwner => NView && NView.IsValid() && NView.IsOwner();

        public GreenlingState State => (GreenlingState)(Zdo?.GetInt(ZState) ?? 0);
        public float Curiosity => Zdo?.GetFloat(ZCuriosity, 40f) ?? 0f;
        public ZDOID Teacher => Zdo?.GetZDOID(ZTeacher) ?? ZDOID.None;
        public float Attention => Zdo?.GetFloat(ZAttention) ?? 0f;
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

        /// <summary>Curiosity wanders toward a mood target that changes on a whim.</summary>
        private void DriftMood()
        {
            if (!IsOwner || !IsGreenling) return;
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
            AddCuriosity(Plugin.CoinCuriosity.Value, "coin");
        }

        private void RPC_StartWatch(long sender, ZDOID teacher)
        {
            if (!IsOwner) return;
            if (Curiosity < Plugin.LearnThreshold.Value)
            {
                Event("refuse", $"curiosity={Curiosity:0}");
                return;
            }
            Zdo.Set(ZTeacher, teacher);
            Zdo.Set(ZAttention, Plugin.AttentionSpan.Value);
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

        private void Refresh()
        {
            var tamed = IsGreenling;
            if (tamed == _appliedTamed) return;
            _appliedTamed = tamed;

            Character.m_name = tamed ? DisplayName : _wildName;
            var block = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMaterial == null || r.sharedMaterial.shader.name != "Custom/Creature") continue; // skip eyes
                r.GetPropertyBlock(block);
                if (tamed) block.SetColor(ColorId, Plugin.TamedTint.Value);
                else block.Clear();
                r.SetPropertyBlock(block);
            }
        }

        [HarmonyLib.HarmonyPatch(typeof(Tameable), nameof(Tameable.Tame))]
        private static class TamedEvent
        {
            private static void Postfix(Tameable __instance)
            {
                if (__instance.GetComponent<Greenling>() is Greenling g && g.IsGreenling) g.Event("tamed");
            }
        }

        public string MoodWord()
        {
            var c = Curiosity;
            if (c >= 80) return "eager";
            if (c >= Plugin.LearnThreshold.Value) return "curious";
            if (c >= 25) return "bored";
            return "sulky";
        }
    }
}
