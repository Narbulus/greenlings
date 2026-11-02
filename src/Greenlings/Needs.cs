using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Upkeep. A greenling wants a meal now and then (food left on the ground, never from chests)
    /// and a nest to sleep in at night. Unmet needs never hurt or untame it: it just works
    /// slower, misbehaves more and sometimes sulks.
    /// </summary>
    internal static class Needs
    {
        public static bool Hungry(Greenling g) =>
            g.Zdo != null && (ZNet.instance.GetTime() - new System.DateTime(g.Zdo.GetLong(Greenling.ZLastFed, 0L))).TotalSeconds > Plugin.MealInterval.Value;

        public static bool Homeless(Greenling g) => !Nest.HasRoom(g);

        public static int Unmet(Greenling g) => (Hungry(g) ? 1 : 0) + (g.HomelessCached ? 1 : 0);

        /// <summary>Action time multiplier from unmet needs.</summary>
        public static float Slowdown(Greenling g) => Mathf.Pow(Plugin.UnmetSlowdown.Value, Unmet(g));

        public static string Describe(Greenling g)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (Hungry(g)) parts.Add("hungry");
            if (g.HomelessCached) parts.Add("no nest");
            return string.Join(", ", parts);
        }

        /// <summary>
        /// Nearest food lying on the ground. Greenlings never help themselves from chests: you feed
        /// them by leaving food out (or by teaching one of them to keep a feeding spot stocked).
        /// </summary>
        public static ItemDrop FindFood(Vector3 at) =>
            Sources.DropsNear(at, Plugin.FoodSearchRange.Value)
                .Where(d => Interaction.IsFood(Utils.GetPrefabName(d.gameObject)))
                .OrderBy(d => Vector3.Distance(d.transform.position, at)).FirstOrDefault();

        public static bool IsNight => EnvMan.instance && EnvMan.IsNight();
    }
}
