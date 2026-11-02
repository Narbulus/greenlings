using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// How a shaman plants neatly. You plant one seed (or a few) in front of it; it works out a
    /// grid from that and fills the cultivated patch, row by row, and keeps it filled as you harvest.
    ///
    /// The grid ("lattice"):
    ///  - spacing: the distance between crops already in the field (what you did), or failing that
    ///    just enough for the crop to grow (Plant.m_growRadius), or the CropSpacing setting;
    ///  - rows: lined up with the crops already there, or failing that with the long side of the
    ///    cultivated patch, so a strip of tilled ground gets rows along it;
    ///  - origin: one of your plants, so its rows continue yours exactly.
    /// Each crop goes in a little off its mark (CropJitter): rows, but planted by hand.
    /// The field is the cultivated ground reachable from where you planted, cell by cell, stopping at
    /// untilled ground and at other crops (a turnip bed next door is somebody else's field).
    /// </summary>
    internal static class Fields
    {
        private class Lattice
        {
            public Vector3 Origin, U, V;
            public float Spacing;
            public float Made;
        }

        private static readonly Dictionary<string, Lattice> _lattices = new Dictionary<string, Lattice>();
        private static readonly Collider[] _hits = new Collider[32];
        private static int _spaceMask;

        public static Plant PlantOf(string piecePrefab) => ZNetScene.instance.GetPrefab(piecePrefab)?.GetComponent<Plant>();

        /// <summary>The item a crop piece is planted from (carrot seeds for a carrot sapling).</summary>
        public static string SeedOf(Piece piece) =>
            piece && piece.GetComponent<Plant>() && piece.m_resources.Length > 0 && piece.m_resources[0].m_resItem
                ? piece.m_resources[0].m_resItem.name : null;

        public static string SeedOf(string piecePrefab) => SeedOf(ZNetScene.instance.GetPrefab(piecePrefab)?.GetComponent<Piece>());

        private static bool Cultivated(Vector3 p) => Heightmap.FindHeightmap(p) is Heightmap h && h.IsCultivated(p);

        /// <summary>This crop, at any stage: the sapling or what it grows into.</summary>
        private static bool SameCrop(GameObject go, Plant crop, string piecePrefab)
        {
            var name = Utils.GetPrefabName(go);
            return name == piecePrefab || crop.m_grownPrefabs.Any(g => g && g.name == name);
        }

        /// <summary>Crops (growing or grown) near a point.</summary>
        private static IEnumerable<GameObject> CropsNear(Vector3 at, float radius) =>
            Object.FindObjectsByType<Plant>(FindObjectsSortMode.None).Where(p => p.m_nview && p.m_nview.IsValid()).Select(p => p.gameObject)
                .Concat(Object.FindObjectsByType<Pickable>(FindObjectsSortMode.None).Where(p => p.m_nview && p.m_nview.IsValid()).Select(p => p.gameObject))
                .Where(go => Utils.DistanceXZ(go.transform.position, at) < radius && (go.GetComponent<Plant>() || Cultivated(go.transform.position)));

        /// <summary>The next spot to plant in this field, or null when the field is full.</summary>
        public static Vector3? NextSpot(Endpoint field, Vector3 from)
        {
            var crop = PlantOf(field.Prefab);
            if (!crop) return null;
            var lat = LatticeFor(field, crop);
            var crops = CropsNear(field.Pos, Plugin.FieldRadius.Value + lat.Spacing * 2f).ToList();
            var others = crops.Where(c => !SameCrop(c, crop, field.Prefab)).Select(c => c.transform.position).ToList();
            var mine = crops.Where(c => SameCrop(c, crop, field.Prefab)).Select(c => c.transform.position).ToList();

            // walk the field cell by cell from where it was planted
            var start = Cell(lat, field.Pos);
            var seen = new HashSet<Vector2Int> { start };
            var queue = new Queue<Vector2Int>();
            queue.Enqueue(start);
            var free = new List<(Vector2Int cell, Vector3 pos)>();
            var maxCells = Mathf.CeilToInt(Plugin.FieldRadius.Value / lat.Spacing);
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                var pos = Pos(lat, c);
                if (!Cultivated(pos)) continue;
                if (others.Any(o => Utils.DistanceXZ(o, pos) < lat.Spacing * 0.6f)) continue; // someone else's bed
                if (!mine.Any(m => Utils.DistanceXZ(m, pos) < lat.Spacing * 0.6f) && Plantable(pos, crop, lat.Spacing, mine)) free.Add((c, pos));
                foreach (var d in new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down })
                {
                    var n = c + d;
                    if (Mathf.Abs(n.x - start.x) > maxCells || Mathf.Abs(n.y - start.y) > maxCells || !seen.Add(n)) continue;
                    queue.Enqueue(n);
                }
            }
            if (free.Count == 0) return null;

            // finish the row you started before the next (rows nearest yours first); within a row, the free
            // spot nearest the shaman, so the row grows on from what is already planted
            int RowRank(int y) => Mathf.Abs(y - start.y) * 2 + (y < start.y ? 1 : 0);
            var best = free.OrderBy(f => RowRank(f.cell.y))
                           .ThenBy(f => Utils.DistanceXZ(f.pos, from))
                           .First();
            return best.pos;
        }

        /// <summary>Would a crop grow here (room around it, nothing in the way, right climate)?</summary>
        private static bool Plantable(Vector3 pos, Plant crop, float spacing, List<Vector3> mine)
        {
            if (mine.Any(m => Utils.DistanceXZ(m, pos) < Mathf.Max(crop.m_growRadius, spacing * 0.8f))) return false;
            if (crop.m_biome != 0 && (Heightmap.FindBiome(pos) & crop.m_biome) == 0) return false;
            if (_spaceMask == 0) _spaceMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid");
            var n = Physics.OverlapSphereNonAlloc(pos + Vector3.up * 0.3f, crop.m_growRadius, _hits, _spaceMask);
            for (var i = 0; i < n; i++)
                if (!_hits[i].GetComponentInParent<Character>()) return false; // a fence, a rock, another plant's leaves
            return !Physics.Raycast(pos + Vector3.up * 0.5f, Vector3.up, 50f, LayerMask.GetMask("Default", "static_solid", "piece")); // needs sun
        }

        // ---- the grid ---------------------------------------------------------------------------

        private static Vector2Int Cell(Lattice l, Vector3 p)
        {
            var d = p - l.Origin;
            return new Vector2Int(Mathf.RoundToInt(Vector3.Dot(d, l.U) / l.Spacing), Mathf.RoundToInt(Vector3.Dot(d, l.V) / l.Spacing));
        }

        private static Vector3 Pos(Lattice l, Vector2Int c)
        {
            var p = l.Origin + (l.U * c.x + l.V * c.y) * l.Spacing;
            p.y = ZoneSystem.instance.GetGroundHeight(p);
            return p;
        }

        private static Lattice LatticeFor(Endpoint field, Plant crop)
        {
            var key = field.Prefab + "@" + Mathf.RoundToInt(field.Pos.x) + "," + Mathf.RoundToInt(field.Pos.z);
            // a field keeps its grid once worked out (its own jittered crops would make it creep)
            if (_lattices.TryGetValue(key, out var cached)) return cached;

            var mine = CropsNear(field.Pos, Plugin.FieldRadius.Value).Where(c => SameCrop(c, crop, field.Prefab))
                .Select(c => c.transform.position).OrderBy(p => Utils.DistanceXZ(p, field.Pos)).ToList();
            var minSpacing = crop.m_growRadius + 0.15f;
            var l = new Lattice { Made = Time.time, Origin = mine.Count > 0 ? mine[0] : field.Pos };

            // what you did: nearest-neighbour steps between your plants
            var steps = mine.Select(a => mine.Where(b => b != a).OrderBy(b => Utils.DistanceXZ(a, b)).Select(b => b - a).FirstOrDefault())
                            .Where(s => s.sqrMagnitude > 0.01f).Select(s => new Vector3(s.x, 0f, s.z)).ToList();
            if (Plugin.CropSpacing.Value > 0f) l.Spacing = Plugin.CropSpacing.Value;
            else if (steps.Count > 0) l.Spacing = Mathf.Max(minSpacing, Upper(steps.Select(s => s.magnitude)));
            else l.Spacing = Mathf.Max(minSpacing, crop.m_growRadius * 2f);

            var dir = steps.Count > 0 ? MainDirection(steps) : PatchDirection(l.Origin, l.Spacing);
            l.U = dir;
            l.V = Vector3.Cross(Vector3.up, dir).normalized;
            _lattices[key] = l;
            Plugin.Log.LogInfo($"[GLEVT] field crop={field.Prefab} spacing={l.Spacing:0.00} dir={l.U.x:0.00},{l.U.z:0.00} from={(steps.Count > 0 ? "plants" : "patch")} plants={mine.Count}");
            return l;
        }

        /// <summary>
        /// The usual spacing: the upper quartile of nearest-neighbour distances. On a hand-planted
        /// (jittered) field the nearest neighbour is usually a bit closer than the real spacing, so the
        /// median would shrink the grid each time it is worked out again.
        /// </summary>
        private static float Upper(IEnumerable<float> xs)
        {
            var s = xs.OrderBy(x => x).ToList();
            return s[Mathf.Min(s.Count - 1, s.Count * 3 / 4)];
        }

        /// <summary>The direction most steps agree on (a row direction and its reverse count as one).</summary>
        private static Vector3 MainDirection(List<Vector3> steps)
        {
            // average on the doubled angle, so +d and -d (and a square grid's two axes, folded) agree
            float sx = 0f, sz = 0f;
            foreach (var s in steps)
            {
                var a = Mathf.Atan2(s.z, s.x) * 4f; // x4: a square grid's 90 degree turn is the same row system
                sx += Mathf.Cos(a);
                sz += Mathf.Sin(a);
            }
            var angle = Mathf.Atan2(sz, sx) / 4f;
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        /// <summary>The long side of the cultivated patch (principal axis of a sample of its ground).</summary>
        private static Vector3 PatchDirection(Vector3 at, float step)
        {
            var pts = new List<Vector3>();
            var r = Plugin.FieldRadius.Value;
            var s = Mathf.Max(0.5f, step);
            for (var x = -r; x <= r; x += s)
            for (var z = -r; z <= r; z += s)
            {
                var p = at + new Vector3(x, 0f, z);
                if (Cultivated(p)) pts.Add(p);
            }
            if (pts.Count < 3) return Vector3.right;
            var mean = pts.Aggregate(Vector3.zero, (a, b) => a + b) / pts.Count;
            float xx = 0, xz = 0, zz = 0;
            foreach (var p in pts)
            {
                var d = p - mean;
                xx += d.x * d.x; xz += d.x * d.z; zz += d.z * d.z;
            }
            var angle = 0.5f * Mathf.Atan2(2f * xz, xx - zz);
            return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        }

        /// <summary>Plant one: the same piece a player would place, with its placing effect.</summary>
        public static bool Plant(Endpoint field, Vector3 spot)
        {
            var prefab = ZNetScene.instance.GetPrefab(field.Prefab);
            if (!prefab) return false;
            // a greydwarf planted it: roughly on the mark, never quite (but always leaving the crop its room)
            var lat = _lattices.Values.OrderBy(l => Utils.DistanceXZ(l.Origin, spot)).FirstOrDefault();
            var crop0 = prefab.GetComponent<Plant>();
            var slack = lat != null && crop0 ? Mathf.Max(0f, (lat.Spacing - crop0.m_growRadius - 0.1f) / 2f) : 0f;
            var jitter = Random.insideUnitCircle * Mathf.Min(Plugin.CropJitter.Value, slack);
            spot += new Vector3(jitter.x, 0f, jitter.y);
            spot.y = ZoneSystem.instance.GetGroundHeight(spot);
            var rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var go = Object.Instantiate(prefab, spot, rot);
            foreach (var placed in go.GetComponents<IPlaced>()) placed.OnPlaced();
            go.GetComponent<Piece>()?.m_placeEffect.Create(spot, rot, go.transform);
            if (lat != null)
            {
                var c = Cell(lat, spot);
                Plugin.Log.LogInfo($"[GLEVT] planted crop={field.Prefab} cell={c.x},{c.y} at={spot.x:0.0},{spot.z:0.0}");
            }
            return true;
        }

        /// <summary>A field endpoint for teaching: where the crop was planted, and which crop.</summary>
        public static Endpoint FieldAt(Vector3 pos, string piecePrefab) => new Endpoint { Kind = EndpointKind.Field, Pos = pos, Prefab = piecePrefab };
    }

    /// <summary>The player planting a crop (cultivator) is a place to put seeds.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
    internal static class PlantHook
    {
        private static void Postfix(Player __instance, Piece piece, Vector3 pos)
        {
            if (__instance != Player.m_localPlayer || !Teaching.Active) return;
            var seed = Fields.SeedOf(piece);
            if (seed != null) Teaching.Sink(Fields.FieldAt(pos, Utils.GetPrefabName(piece.gameObject)), seed);
        }
    }
}
