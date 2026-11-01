using System.Linq;
using UnityEngine;

namespace Greenlings
{
    public enum Role { Greyling, Greydwarf, Brute }

    /// <summary>
    /// What each kind of greenling can learn.
    ///   Greyling:  light chores: chests, loose items, foraging, beehives, keeping fires lit.
    ///   Greydwarf: + operating machines (smelters, kilns, cooking, fermenters), gathering stones/flint, breaking bushes.
    ///   Brute:     only whacks: splits logs, smashes rocks and stumps, leaves the pieces. Never hauls, never fells standing trees.
    /// </summary>
    internal static class Roles
    {
        public static readonly string[] TameablePrefabs = { "Greyling", "Greydwarf", "Greydwarf_Elite" };

        public static Role Of(GameObject go)
        {
            switch (Utils.GetPrefabName(go))
            {
                case "Greydwarf": return Role.Greydwarf;
                case "Greydwarf_Elite": return Role.Brute;
                default: return Role.Greyling;
            }
        }

        public static string Name(Role r) => r == Role.Brute ? "brute" : r == Role.Greydwarf ? "greydwarf" : "greyling";

        public static int CarryPerTrip(Role r) => Plugin.CarryMax.Value;

        /// <summary>Stones, flint and branches are greydwarf gathering, not greyling foraging.</summary>
        private static bool IsRockyPickable(string prefab) => Plugin.GreydwarfPickables.Value.Split(',').Any(x => x.Trim() == prefab);

        /// <summary>Whether this role can do the task; when not, a short reason for the player.</summary>
        public static bool CanLearn(Role r, LaborTask t, out string why)
        {
            why = null;
            // brutes only whack; nobody else does whack-only jobs
            if (r == Role.Brute && !t.IsWhack) { why = "brutes only smash things; let the little ones carry"; return false; }
            if (r != Role.Brute && t.IsWhack) { why = "too small to smash that"; return false; }
            if (r == Role.Brute) return true;
            if (r == Role.Greyling)
            {
                if (t.Sink.Kind != EndpointKind.Container && t.Sink.Kind != EndpointKind.FireFuel) { why = "machines are greydwarf work"; return false; }
                if (t.Source.Kind == EndpointKind.Extract && !(t.Source.Get<Beehive>() || t.Source.Get<SapCollector>())) { why = "machines are greydwarf work"; return false; }
                if (t.Source.Kind == EndpointKind.PickableArea && IsRockyPickable(t.Source.Prefab)) { why = "rocks and flint are greydwarf work"; return false; }
                if (t.Source.Kind == EndpointKind.BreakArea) { why = "breaking things is greydwarf work"; return false; }
            }
            if (r != Role.Brute && t.Source.Kind == EndpointKind.BreakArea && t.Source.Prefab != Breakables.Bush)
            {
                why = "splitting logs and smashing rocks is brute work";
                return false;
            }
            return true;
        }
    }
}
