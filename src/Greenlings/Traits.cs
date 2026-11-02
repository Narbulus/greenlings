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
        public const string Greedy = "greedy";   // food means less to it; hoards shiny things; keen on chores
        public const string Lazy = "lazy";       // slower at every action, longer breaks, rarely does chores
        public const string Clumsy = "clumsy";   // sometimes fumbles what it carries
        public const string Curious = "curious"; // comes to watch you often, longer attention span
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

        /// <summary>The trait with a few words on what it means, for the hover text.</summary>
        public static string Explain(string trait)
        {
            switch (trait)
            {
                case Greedy: return "greedy (eager for chores, steals your stuff)";
                case Lazy: return "lazy (slow, takes long breaks)";
                case Clumsy: return "clumsy (drops things)";
                case Curious: return "curious (watches you, quick to learn)";
                case Cuddly: return "cuddly (loves pats, stares at you)";
                case Magpie: return "magpie (brings you presents)";
                case Zoomy: return "zoomy (runs about, loves a chase)";
                case Copycat: return "copycat (copies what you do)";
                case Sleepy: return "sleepy (calm, rarely naughty)";
                default: return trait;
            }
        }

        // ---- the numbers ------------------------------------------------------------------

        public static float ActionTime(Greenling g) => (g.Has(Lazy) ? 1.7f : 1f) * Needs.Slowdown(g);
        public static float WaitRecheck(Greenling g) => g.Has(Lazy) ? 2f : 1f;
        public static float Attention(Greenling g) => g.Has(Curious) ? 1.5f : 1f;
        /// <summary>How often it wanders over to see what you're up to.</summary>
        public static float Curiosity(Greenling g) => g.Has(Curious) ? 3f : g.Has(Copycat) ? 2f : g.Has(Sleepy) ? 0.5f : 1f;
        /// <summary>How often it finds itself a useful chore.</summary>
        public static float Diligence(Greenling g) => g.Has(Lazy) ? 0.3f : g.Has(Greedy) ? 1.5f : 1f;
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
                case "scatter": return g.Has(Greedy) ? 3 : 1;
                case "stare": return g.Has(Cuddly) ? 4 : 2;
                case "tag": return g.Has(Zoomy) ? 4 : 2;
                case "huddle": return g.Has(Cuddly) ? 3 : 2;
                case "herd": case "birds": return g.Has(Zoomy) ? 3 : 2;
                default: return 1;
            }
        }
    }
}
