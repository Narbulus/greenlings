using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Things a greenling can smash to get at what's inside: bushes (greydwarfs), logs and rocks
    /// (brutes). Standing trees (TreeBase) are never included: they don't have axes.
    /// A BreakArea source is a category, not one object: "logs around here".
    /// </summary>
    internal static class Breakables
    {
        public const string Bush = "bush", Log = "log", Rock = "rock";

        /// <summary>Category of a hittable object, or null if greenlings shouldn't touch it.</summary>
        public static string Category(GameObject go)
        {
            if (!go || go.GetComponent<TreeBase>() || go.GetComponent<Piece>()) return null;
            if (go.GetComponent<TreeLog>()) return Log;
            if (go.GetComponent<MineRock>() || go.GetComponent<MineRock5>()) return Rock;
            if (go.GetComponent<Destructible>() is Destructible d)
            {
                var name = Utils.GetPrefabName(go).ToLower();
                if (name.Contains("stub") || name.Contains("stump")) return Log;       // stumps: brute work
                if (d.m_minToolTier > 0 || name.Contains("rock")) return Rock;
                return Bush; // bushes and little saplings: greydwarf gathering
            }
            return null;
        }

        public static GameObject Nearest(Endpoint area, Vector3 from) =>
            Physics.OverlapSphere(area.Pos, area.CollectRadius)
                .Select(c => c.GetComponentInParent<ZNetView>())
                .Where(v => v && v.IsValid() && Category(v.gameObject) == area.Prefab)
                .Select(v => v.gameObject).Distinct()
                .OrderBy(go => Vector3.Distance(go.transform.position, from))
                .FirstOrDefault();

        /// <summary>One whack, through the object's own damage code (it RPCs to its owner, like a player hit).</summary>
        public static void Hit(GameObject target, Character attacker, Role role)
        {
            var col = target.GetComponentInChildren<Collider>();
            var point = col ? col.ClosestPoint(attacker.GetCenterPoint()) : target.transform.position;
            var hit = new HitData
            {
                m_point = point,
                m_dir = (point - attacker.GetCenterPoint()).normalized,
                m_toolTier = (short)(role == Role.Brute ? 2 : 0),
                m_hitCollider = col,
            };
            var power = role == Role.Brute ? Plugin.BrutePower.Value : 8f;
            hit.m_damage.m_chop = power;
            hit.m_damage.m_pickaxe = power;
            hit.m_damage.m_blunt = power * 0.5f;
            hit.SetAttacker(attacker);

            if (target.GetComponent<TreeLog>() is TreeLog log) log.Damage(hit);
            else if (target.GetComponent<MineRock5>() is MineRock5 m5) m5.Damage(hit);
            else if (target.GetComponent<MineRock>() is MineRock m) m.Damage(hit);
            else if (target.GetComponent<Destructible>() is Destructible d) d.Damage(hit);
        }

        // ---- teaching: remember what the player just broke ------------------------------------

        private static string _lastCategory;
        private static Vector3 _lastPos;
        private static float _lastTime = -999f;

        public static void PlayerHit(GameObject target, HitData hit)
        {
            if (!Teaching.Active || hit.GetAttacker() != Player.m_localPlayer) return;
            var cat = Category(target);
            if (cat == null) return;
            _lastCategory = cat;
            _lastPos = target.transform.position;
            _lastTime = Time.time;
            Teaching.Whack(Endpoint.BreakArea(_lastPos, cat)); // brutes learn from the hit itself
        }

        /// <summary>An item picked up right after the player broke something nearby came from breaking it.</summary>
        public static Endpoint SourceFor(Vector3 pickupAt)
        {
            if (_lastCategory == null || Time.time - _lastTime > 45f || Vector3.Distance(pickupAt, _lastPos) > 15f) return null;
            return Endpoint.BreakArea(_lastPos, _lastCategory);
        }
    }

    [HarmonyPatch]
    internal static class BreakHooks
    {
        [HarmonyPatch(typeof(global::Destructible), nameof(global::Destructible.Damage)), HarmonyPrefix]
        private static void HitBush(global::Destructible __instance, HitData hit) => Breakables.PlayerHit(__instance.gameObject, hit);

        [HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Damage)), HarmonyPrefix]
        private static void HitLog(TreeLog __instance, HitData hit) => Breakables.PlayerHit(__instance.gameObject, hit);

        [HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage)), HarmonyPrefix]
        private static void HitRock(MineRock __instance, HitData hit) => Breakables.PlayerHit(__instance.gameObject, hit);

        [HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage)), HarmonyPrefix]
        private static void HitRock5(MineRock5 __instance, HitData hit) => Breakables.PlayerHit(__instance.gameObject, hit);
    }
}
