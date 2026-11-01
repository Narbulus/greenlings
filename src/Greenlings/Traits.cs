using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Personality quirks rolled at taming (one, sometimes two). Each nudges a few numbers so
    /// greenlings feel different without any one being strictly better.
    /// </summary>
    internal static class Traits
    {
        public const string Greedy = "greedy";   // coins excite it more, food less; loves pilfering chests
        public const string Lazy = "lazy";       // slower at every action, longer breaks
        public const string Clumsy = "clumsy";   // sometimes fumbles what it carries
        public const string Curious = "curious"; // keener to learn, longer attention span
        public const string Cuddly = "cuddly";   // pats mean more and come round sooner; stares a lot
        public const string Magpie = "magpie";   // forever bringing you presents
        public const string Zoomy = "zoomy";     // runs everywhere, zoomies galore
        public const string Copycat = "copycat"; // mimics you constantly
        public const string Sleepy = "sleepy";   // misbehaves less, just sort of... sits

        public static readonly string[] All = { Greedy, Lazy, Clumsy, Curious, Cuddly, Magpie, Zoomy, Copycat, Sleepy };

        public static string Roll()
        {
            var n = Random.value < 0.3f ? 2 : 1;
            return string.Join(",", All.OrderBy(_ => Random.value).Take(n));
        }

        // ---- the numbers ------------------------------------------------------------------

        public static float ActionTime(Greenling g) => (g.Has(Lazy) ? 1.7f : 1f) * Needs.Slowdown(g);
        public static float WaitRecheck(Greenling g) => g.Has(Lazy) ? 2f : 1f;
        public static float Attention(Greenling g) => g.Has(Curious) ? 1.5f : 1f;
        public static float CoinCuriosity(Greenling g) => g.Has(Greedy) ? 2f : g.Has(Curious) ? 1.3f : 1f;
        public static float FoodBond(Greenling g) => g.Has(Greedy) ? 0.7f : 1f;
        public static float PatBond(Greenling g) => g.Has(Cuddly) ? 2f : 1f;
        public static float PatCooldown(Greenling g) => g.Has(Cuddly) ? 0.5f : 1f;
        public static float Mischief(Greenling g) => (g.Has(Sleepy) ? 0.4f : g.Has(Zoomy) ? 1.5f : 1f) * (1f + Needs.Unmet(g));
        public static float FumbleChance(Greenling g) => g.Has(Clumsy) ? 0.12f : 0f;
        public static float MimicChance(Greenling g) => Mathf.Min(1f, Plugin.MimicChance.Value * (g.Has(Copycat) ? 2.5f : 1f));

        /// <summary>Extra weight for a prank kind, by personality.</summary>
        public static int Weight(Greenling g, string kind)
        {
            switch (kind)
            {
                case "gift": return g.Has(Magpie) ? 4 : 1;
                case "zoomies": return g.Has(Zoomy) ? 4 : 1;
                case "misplace": return g.Has(Greedy) ? 3 : 1;
                case "stare": return g.Has(Cuddly) ? 4 : 2;
                default: return 1;
            }
        }
    }
}
