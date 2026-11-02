using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Useful things a greenling does on its own when it has nothing better to do: chores it
    /// picked up by watching you, giving a busy friend a hand (the more the merrier), and
    /// tidying loose things into the chest they belong in.
    /// </summary>
    internal static class Chores
    {
        public static IEnumerable<(string kind, LaborTask task)> Find(Greenling g)
        {
            var pos = g.transform.position;

            foreach (var h in g.Habits)
                if (Roles.CanLearn(g.Role, h, out _) && h.Source.AreaLoaded && Sources.HasWork(h, pos))
                    yield return ("habit", h);

            foreach (var other in Greenling.All.Where(o => o && o != g && o.IsGreenling && o.State == GreenlingState.Working
                                                           && Vector3.Distance(o.transform.position, pos) < Plugin.WorkRadius.Value))
            {
                var t = other.Job.FirstOrDefault(j => Roles.CanLearn(g.Role, j, out _) && j.Source.AreaLoaded && Sources.HasWork(j, pos));
                if (t != null) { yield return ("help", t); break; }
            }

            if (g.Role != Role.Brute && Tidy(g) is LaborTask tidy) yield return ("tidy", tidy);
        }

        /// <summary>
        /// Something lying about that obviously belongs in a chest nearby (that chest already holds
        /// some). Food is left alone: it may be someone's dinner.
        /// </summary>
        private static LaborTask Tidy(Greenling g)
        {
            var pos = g.transform.position;
            foreach (var d in Sources.DropsNear(pos, 15f).OrderBy(d => Vector3.Distance(d.transform.position, pos)))
            {
                var item = Utils.GetPrefabName(d.gameObject);
                if (Interaction.IsFood(item) || Sinks.InFeedingPile(d) || !ObjectDB.instance.GetItemPrefab(item)) continue;
                var chest = Sinks.Chests(d.transform.position, Plugin.WorkRadius.Value)
                    .Where(c => !c.IsInUse() && c.GetInventory().CountItems(Items.SharedName(item)) > 0
                                && c.GetInventory().CanAddItem(ObjectDB.instance.GetItemPrefab(item), 1))
                    .OrderBy(c => Vector3.Distance(c.transform.position, d.transform.position)).FirstOrDefault();
                if (chest)
                    return new LaborTask { Item = item, Source = Endpoint.Ground(d.transform.position), Sink = Endpoint.For(EndpointKind.Container, chest) };
            }
            return null;
        }

        /// <summary>
        /// Simple, can't-go-wrong chores a greenling may pick up just by watching (no coin): putting
        /// things away, foraging, keeping fires going, smashing for brutes. Not reorganizing chests
        /// or running machines: that needs a proper lesson.
        /// </summary>
        public static bool NoRegret(LaborTask t)
        {
            if (t.IsWhack) return true;
            if (t.Sink.Kind == EndpointKind.FireFuel) return true;
            if (t.Sink.Kind != EndpointKind.Container) return false;
            return t.Source.Kind == EndpointKind.Ground || t.Source.Kind == EndpointKind.PickableArea || t.Source.Kind == EndpointKind.BreakArea
                   || (t.Source.Kind == EndpointKind.Extract && (t.Source.Get<Beehive>() || t.Source.Get<SapCollector>()));
        }
    }
}
