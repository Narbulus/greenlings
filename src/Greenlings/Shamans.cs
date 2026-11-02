using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// What shamans do besides chores: they heal hurt tamed creatures nearby (greenlings, your
    /// animals) between trips, and they help strays settle in: their company counts like yours,
    /// and now and then they go and soothe one.
    /// </summary>
    internal static class Shamans
    {
        private static readonly List<Character> _near = new List<Character>();
        private static readonly Dictionary<Greenling, float> _nextHeal = new Dictionary<Greenling, float>();

        public static bool IsShaman(Greenling g) => g && g.IsGreenling && !g.IsStray && g.Role == Role.Shaman && !g.Character.IsDead();

        /// <summary>A trusting shaman (not this one) close by.</summary>
        public static bool Near(Greenling g, float range) =>
            Greenling.All.Any(o => o != g && IsShaman(o) && Vector3.Distance(o.transform.position, g.transform.position) < range);

        /// <summary>Whoever needs healing most within reach, if the shaman is ready to heal again.</summary>
        public static Character Patient(Greenling shaman)
        {
            if (_nextHeal.TryGetValue(shaman, out var t) && Time.time < t) return null;
            _near.Clear();
            Character.GetCharactersInRange(shaman.transform.position, Plugin.HealRange.Value, _near);
            return _near.Where(c => c && !c.IsDead() && (c.IsTamed() || (Plugin.HealPlayers.Value && c is Player))
                                    && c.GetHealthPercentage() < Plugin.HealBelow.Value && !BeingHealed(c, shaman))
                        .OrderBy(c => c.GetHealthPercentage()).FirstOrDefault();
        }

        /// <summary>Another shaman is already on its way to this one.</summary>
        private static bool BeingHealed(Character c, Greenling except) =>
            Greenling.All.Any(o => o && o != except && o.Brain.CurrentPrank is Prank p && p.Kind == "heal" && p.Patient == c);

        public static void Heal(Greenling shaman, Character patient)
        {
            _nextHeal[shaman] = Time.time + Plugin.HealCooldown.Value;
            var hp = patient.GetMaxHealth() * Plugin.HealPercent.Value / 100f;
            patient.Heal(hp);
            HealEffect(shaman)?.Create(patient.transform.position, Quaternion.identity);
            shaman.Event("heal", $"patient={Utils.GetPrefabName(patient.gameObject)} hp=+{hp:0} now={patient.GetHealthPercentage():0.00}");
        }

        /// <summary>A stray nearby that hasn't been soothed lately.</summary>
        public static Greenling StrayToSoothe(Greenling shaman) =>
            Greenling.All.Where(o => o && o != shaman && o.IsStray && !o.Character.IsDead() && o.CanBeSoothed
                                     && Vector3.Distance(o.transform.position, shaman.transform.position) < 20f)
                         .OrderBy(o => Vector3.Distance(o.transform.position, shaman.transform.position)).FirstOrDefault();

        private static EffectList _healEffect;
        private static string _healAnim;

        /// <summary>The vanilla shaman's own healing spell: its animation and effect.</summary>
        public static string HealAnimation(Greenling shaman)
        {
            Load(shaman);
            return _healAnim ?? "attack";
        }

        private static EffectList HealEffect(Greenling shaman)
        {
            Load(shaman);
            return _healEffect;
        }

        private static void Load(Greenling shaman)
        {
            if (_healEffect != null || !(shaman.Character is Humanoid h)) return;
            var spell = (h.m_defaultItems ?? new GameObject[0]).Concat(h.m_randomWeapon ?? new GameObject[0])
                .Concat((h.m_randomSets ?? new Humanoid.ItemSet[0]).SelectMany(s => s.m_items ?? new GameObject[0]))
                .Select(i => i ? i.GetComponent<ItemDrop>() : null)
                .FirstOrDefault(d => d && d.name.ToLowerInvariant().Contains("heal"));
            var attack = spell?.m_itemData.m_shared.m_attack;
            _healAnim = string.IsNullOrEmpty(attack?.m_attackAnimation) ? null : attack.m_attackAnimation;
            // the spell itself is an area heal for every greydwarf around: borrow only its look and sound
            _healEffect = new EffectList
            {
                m_effectPrefabs = new[] { "vfx_greydwarf_shaman_pray", "sfx_greydwarf_shaman_heal" }
                    .Select(n => ZNetScene.instance.GetPrefab(n)).Where(go => go)
                    .Select(go => new EffectList.EffectData { m_prefab = go, m_enabled = true }).ToArray(),
            };
            Plugin.Log.LogInfo($"[GLEVT] shaman_spell item={(spell ? spell.name : "none")} anim={_healAnim} effects={_healEffect.m_effectPrefabs.Length}");
        }
    }
}
