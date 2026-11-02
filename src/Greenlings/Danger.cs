using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Greenlings are not fighters. They leave animals alone entirely; greylings (and hurt
    /// greydwarfs and shamans) run from monsters: to you or a brute for protection, or just away in a panic.
    /// Brutes and healthy greydwarfs still stand their ground against monsters.
    /// </summary>
    internal static class Danger
    {
        private static readonly List<Character> _near = new List<Character>();
        private static readonly HashSet<string> Harmless = new HashSet<string> { "Boar", "Boar_piggy", "Deer", "Hare", "Chicken", "Hen", "Lox", "Lox_Calf", "Asksvin", "Asksvin_hatchling" };

        public static bool IsHarmless(Character c) => Harmless.Contains(Utils.GetPrefabName(c.gameObject));

        /// <summary>Who runs instead of fighting.</summary>
        public static bool Flees(Greenling g) =>
            g.Role == Role.Greyling || ((g.Role == Role.Greydwarf || g.Role == Role.Shaman) && g.Character.GetHealthPercentage() < 0.5f);

        /// <summary>A monster that means harm (not an animal, not a pet, not a player).</summary>
        public static bool IsMonster(Greenling g, Character c)
        {
            if (!c || c == g.Character || c.IsDead() || c.IsTamed() || c is Player) return false;
            var f = c.GetFaction();
            if (f == Character.Faction.AnimalsVeg || f == Character.Faction.PlayerSpawned || f == Character.Faction.Players) return false;
            var ai = c.GetBaseAI();
            if (!(ai is MonsterAI monster)) return false;                   // deer, birds, fish...
            if (ai.GetTargetCreature() == g.Character) return true;           // it's coming for us
            if (Harmless.Contains(Utils.GetPrefabName(c.gameObject))) return false;
            if (monster.m_passiveAggresive && !ai.IsAggravated()) return false; // only dangerous when provoked
            return BaseAI.IsEnemy(c, g.Character);
        }

        public static Character ThreatNear(Greenling g)
        {
            _near.Clear();
            Character.GetCharactersInRange(g.transform.position, Plugin.FearRange.Value, _near);
            return _near.Where(c => IsMonster(g, c))
                        .OrderBy(c => Vector3.Distance(c.transform.position, g.transform.position))
                        .FirstOrDefault();
        }

        /// <summary>Somewhere safe to run to: the player it knows best, any player, then a brute.</summary>
        public static (Transform where, string kind) Refuge(Greenling g)
        {
            var range = Plugin.RefugeRange.Value;
            var pos = g.transform.position;
            var known = g.FollowTarget ?? ZNetScene.instance.FindInstance(g.Teacher)?.GetComponent<Player>();
            if (known && !known.IsDead() && Vector3.Distance(known.transform.position, pos) < range) return (known.transform, "player");
            var p = Player.GetClosestPlayer(pos, range);
            if (p && !p.IsDead()) return (p.transform, "player");
            var brute = Greenling.All.Where(o => o && o != g && o.IsGreenling && o.Role == Role.Brute && !o.Character.IsDead()
                                                 && Vector3.Distance(o.transform.position, pos) < range)
                                     .OrderBy(o => Vector3.Distance(o.transform.position, pos)).FirstOrDefault();
            return brute ? (brute.transform, "brute") : (null, "panic");
        }
    }

    /// <summary>
    /// Greenlings and animals leave each other alone (vanilla counts anything tamed as an enemy of
    /// every wild creature, so boars and greenlings used to go at each other). A provoked animal
    /// still fights back.
    /// </summary>
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), typeof(Character), typeof(Character))]
    internal static class AnimalTruce
    {
        private static void Postfix(Character a, Character b, ref bool __result)
        {
            if (!__result || !a || !b) return;
            if (Truce(a, b) || Truce(b, a)) __result = false;
        }

        private static bool Truce(Character greenling, Character other)
        {
            if (!greenling.IsTamed() || !greenling.GetComponent<Greenling>() || other.IsTamed() || other is Player) return false;
            var ai = other.GetBaseAI();
            if (ai && ai.IsAggravated()) return false;
            return other.GetFaction() == Character.Faction.AnimalsVeg || !(ai is MonsterAI) || Danger.IsHarmless(other);
        }
    }

    /// <summary>Greenlings never pick fights: drop targets that aren't monsters (boars, deer...), and fleers drop all.</summary>
    [HarmonyPatch(typeof(MonsterAI), "UpdateTarget")]
    internal static class PassiveTargets
    {
        private static void Postfix(MonsterAI __instance)
        {
            var target = __instance.m_targetCreature;
            if (!target) return;
            var g = __instance.GetComponent<Greenling>();
            if (!g || !g.IsGreenling) return;
            if (!Danger.IsMonster(g, target) || Danger.Flees(g)) __instance.m_targetCreature = null;
        }
    }

    /// <summary>Birds take off when a greenling comes bounding up, not just a player.</summary>
    [HarmonyPatch(typeof(RandomFlyingBird), "DangerNearby")]
    internal static class BirdsFearGreenlings
    {
        private static void Postfix(RandomFlyingBird __instance, Vector3 p, ref bool __result)
        {
            if (__result) return;
            var range = __instance.m_avoidDangerDistance;
            foreach (var g in Greenling.All)
                if (g && g.IsGreenling && Vector3.Distance(g.transform.position, p) < range) { __result = true; return; }
        }
    }
}
