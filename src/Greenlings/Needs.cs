using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Upkeep. A greenling wants a meal now and then (it will help itself from a nearby chest)
    /// and a nest to sleep in at night. Unmet needs never hurt or untame it: it just works
    /// slower, misbehaves more and sometimes sulks.
    /// </summary>
    internal static class Needs
    {
        public static bool Hungry(Greenling g) =>
            g.Zdo != null && (ZNet.instance.GetTime() - new System.DateTime(g.Zdo.GetLong(Greenling.ZLastFed, 0L))).TotalSeconds > Plugin.MealInterval.Value;

        public static bool Homeless(Greenling g) => !Nest.Near(g.transform.position, Plugin.NestRange.Value);

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

        /// <summary>Nearest container within range holding something it will eat.</summary>
        public static (Container chest, ItemDrop.ItemData food) FindFood(Vector3 at)
        {
            foreach (var c in Object.FindObjectsByType<Container>(FindObjectsSortMode.None)
                         .Where(c => c.m_nview && c.m_nview.IsValid() && !c.IsInUse() && c.GetComponent<Piece>()
                                     && Vector3.Distance(c.transform.position, at) < Plugin.FoodSearchRange.Value)
                         .OrderBy(c => Vector3.Distance(c.transform.position, at)))
            {
                var food = c.GetInventory().GetAllItems().FirstOrDefault(i => Interaction.IsFood(Items.PrefabOf(i)));
                if (food != null) return (c, food);
            }
            return (null, null);
        }

        public static bool IsNight => EnvMan.instance && EnvMan.IsNight();
    }
}
