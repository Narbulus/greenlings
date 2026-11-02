using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Greenlings can work a door latch (not a gate: too heavy) and close it behind them.
    /// Doors are opened through the same RPC a player uses.
    /// </summary>
    internal static class Doors
    {
        private static readonly Dictionary<Door, float> _opened = new Dictionary<Door, float>();
        private static float _nextClose;

        public static bool IsOpen(Door d) => d.m_nview && d.m_nview.IsValid() && d.m_nview.GetZDO().GetInt(ZDOVars.s_state) != 0;

        /// <summary>An ordinary door: no key, closable, and not a gate or a portcullis.</summary>
        public static bool Usable(Door d)
        {
            if (!d || !d.m_nview || !d.m_nview.IsValid() || d.m_keyItem || d.m_canNotBeClosed) return false;
            var name = Utils.GetPrefabName(d.gameObject).ToLowerInvariant();
            return !name.Contains("gate") && !name.Contains("grate") && !name.Contains("portcullis");
        }

        /// <summary>The closed door that most plausibly stands between here and there.</summary>
        public static Door ClosedDoorBetween(Vector3 from, Vector3 to)
        {
            var direct = Vector3.Distance(from, to);
            return Object.FindObjectsByType<Door>(FindObjectsSortMode.None)
                .Where(d => Usable(d) && !IsOpen(d))
                .Select(d => (d, via: Vector3.Distance(from, d.transform.position) + Vector3.Distance(d.transform.position, to)))
                .Where(x => Vector3.Distance(from, x.d.transform.position) < 15f && x.via < direct + 12f)
                .OrderBy(x => x.via).Select(x => x.d).FirstOrDefault();
        }

        /// <summary>A closed door right in front of a greenling walking into it.</summary>
        public static Door ClosedDoorAt(Vector3 pos) =>
            Object.FindObjectsByType<Door>(FindObjectsSortMode.None)
                .Where(d => Usable(d) && !IsOpen(d) && Vector3.Distance(pos, d.transform.position) < 2f)
                .OrderBy(d => Vector3.Distance(pos, d.transform.position)).FirstOrDefault();

        public static void Open(Door d, Greenling g)
        {
            if (IsOpen(d)) return;
            var fromDir = (g.transform.position - d.transform.position).normalized;
            d.m_nview.InvokeRPC("UseDoor", Vector3.Dot(d.transform.forward, fromDir) < 0f);
            _opened[d] = Time.time;
            g.Event("door", $"open={Utils.GetPrefabName(d.gameObject)}");
        }

        /// <summary>Close doors greenlings opened once nobody is in the doorway.</summary>
        public static void Update()
        {
            if (Time.time < _nextClose || _opened.Count == 0) return;
            _nextClose = Time.time + 1f;
            foreach (var d in _opened.Keys.ToList())
            {
                if (!d || !IsOpen(d)) { _opened.Remove(d); continue; }
                if (Time.time - _opened[d] < 4f) continue;
                var p = d.transform.position;
                var busy = Greenling.All.Any(g => g && Vector3.Distance(g.transform.position, p) < 2.5f)
                           || Player.IsPlayerInRange(p, 2.5f);
                if (busy) continue;
                if (Time.time - _opened[d] > 60f) { _opened.Remove(d); continue; } // someone else is using it
                d.m_nview.InvokeRPC("UseDoor", true);
                _opened.Remove(d);
                Plugin.Log.LogInfo($"[GLEVT] door_close door={Utils.GetPrefabName(d.gameObject)}");
            }
        }
    }
}
