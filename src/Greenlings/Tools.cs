using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Some work needs a tool: a hammer to repair, a cultivator to plant. A greenling holds one tool
    /// at a time (gl_tool), shown in its hand. Hand it one, or it fetches one itself from a chest
    /// near the work when a job (or a chore) needs it, putting back whatever it was holding.
    /// </summary>
    internal static class Tools
    {
        public const string Hammer = "Hammer";
        public const string Cultivator = "Cultivator";

        public static bool IsTool(string prefab) => prefab == Hammer || prefab == Cultivator;

        /// <summary>The tool a job step needs, if any.</summary>
        public static string Needed(LaborTask t) => t != null && t.Sink.Kind == EndpointKind.Field ? Cultivator : null;

        /// <summary>Whether this greenling can use the tool; when not, a short reason for the player.</summary>
        public static bool CanUse(Greenling g, string tool, out string why)
        {
            why = null;
            if (g.IsStray) { why = $"sniffs the {Name(tool)} and wanders off"; return false; }
            if (tool == Hammer && g.Role == Role.Brute) { why = "only knows one way to use a hammer"; return false; }
            if (tool == Cultivator && g.Role != Role.Shaman) { why = "pokes at the dirt with it and hands it back (planting is shaman work)"; return false; }
            return true;
        }

        public static string Name(string tool) => Items.DisplayName(tool).ToLowerInvariant();

        /// <summary>A chest near the work that has the tool: the job's own chest first, then the nearest.</summary>
        public static Container FindInChests(string tool, Vector3 near, Endpoint preferred = null)
        {
            bool Has(Container c) => c && !c.IsInUse() && c.GetInventory().CountItems(Items.SharedName(tool)) > 0;
            var own = preferred?.Kind == EndpointKind.Container ? preferred.Get<Container>() : null;
            if (Has(own)) return own;
            return Sinks.Chests(near, Plugin.WorkRadius.Value).Where(Has)
                .OrderBy(c => Vector3.Distance(c.transform.position, near)).FirstOrDefault();
        }

        /// <summary>Owner: take the tool out of the chest, putting back the one it held. False if it's gone.</summary>
        public static bool TakeFrom(Greenling g, Container chest, string tool)
        {
            var src = Endpoint.For(EndpointKind.Container, chest);
            if (Sources.TakeFromContainer(src, tool, 1) == 0) return false;
            var old = g.Tool;
            if (old.Length > 0 && !Sinks.PutOne(src, old, out _))
                Prank.Drop(g.transform.position + g.transform.forward * 0.5f + Vector3.up, old, 1);
            g.Zdo.Set(Greenling.ZTool, tool);
            chest.m_openEffects.Create(chest.transform.position, chest.transform.rotation);
            g.Event("tool", $"item={tool} from={src}{(old.Length > 0 ? " returned=" + old : "")}");
            return true;
        }
    }
}
