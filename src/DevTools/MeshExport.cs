using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Greenlings.DevTools
{
    /// <summary>
    /// gldev_export &lt;prefab&gt;: dump a creature's skinned meshes and skeleton as JSON (Unity space,
    /// left-handed, Y up) for editing in Blender. Writes BepInEx/devtools/export/&lt;prefab&gt;.json.
    /// Per mesh: vertices, normals, uv, per-submesh triangles, 4 bone weights per vertex, bone names
    /// and bind poses. Per transform: parent and local TRS, so the rig can be rebuilt exactly.
    /// </summary>
    internal static class MeshExport
    {
        public static string Export(string prefabName)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (!prefab) return "no prefab " + prefabName;
            var root = prefab.transform;
            var sb = new StringBuilder();
            sb.Append("{\"prefab\":\"").Append(prefabName).Append("\",\"transforms\":[");
            var all = root.GetComponentsInChildren<Transform>(true);
            for (var i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(t.name).Append("\",\"parent\":\"").Append(t == root || !t.parent ? "" : t.parent.name).Append('"');
                sb.Append(",\"pos\":").Append(V(t.localPosition)).Append(",\"rot\":").Append(Q(t.localRotation)).Append(",\"scale\":").Append(V(t.localScale)).Append('}');
            }
            sb.Append("],\"meshes\":[");
            var first = true;
            var report = new StringBuilder();
            foreach (var smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = smr.sharedMesh;
                if (!m) continue;
                if (!m.isReadable) { report.Append($"{smr.name}: NOT READABLE; "); continue; }
                if (!first) sb.Append(',');
                first = false;
                sb.Append("{\"renderer\":\"").Append(smr.name).Append("\",\"mesh\":\"").Append(m.name)
                  .Append("\",\"rootBone\":\"").Append(smr.rootBone ? smr.rootBone.name : "").Append('"')
                  .Append(",\"materials\":[").Append(string.Join(",", smr.sharedMaterials.Select(x => "\"" + (x ? x.name : "") + "\""))).Append(']')
                  .Append(",\"bones\":[").Append(string.Join(",", smr.bones.Select(b => "\"" + (b ? b.name : "") + "\""))).Append(']')
                  .Append(",\"bindposes\":[").Append(string.Join(",", m.bindposes.Select(M))).Append(']')
                  .Append(",\"vertices\":").Append(Arr(m.vertices.SelectMany(v => new[] { v.x, v.y, v.z })))
                  .Append(",\"normals\":").Append(Arr(m.normals.SelectMany(v => new[] { v.x, v.y, v.z })))
                  .Append(",\"uv\":").Append(Arr(m.uv.SelectMany(v => new[] { v.x, v.y })))
                  .Append(",\"submeshes\":[");
                for (var s = 0; s < m.subMeshCount; s++)
                {
                    if (s > 0) sb.Append(',');
                    sb.Append('[').Append(string.Join(",", m.GetTriangles(s))).Append(']');
                }
                sb.Append("],\"weights\":[");
                sb.Append(string.Join(",", m.boneWeights.Select(w => string.Format(CultureInfo.InvariantCulture, "{0},{1:0.#####},{2},{3:0.#####},{4},{5:0.#####},{6},{7:0.#####}",
                    w.boneIndex0, w.weight0, w.boneIndex1, w.weight1, w.boneIndex2, w.weight2, w.boneIndex3, w.weight3))));
                sb.Append("]}");
                report.Append($"{smr.name}: {m.vertexCount}v {m.triangles.Length / 3}t {smr.bones.Length} bones");
                // extra vertex streams the shader may read (a remodel has to provide them too)
                var streams = new List<string>();
                if (m.colors32.Length > 0) streams.Add($"colors(avg {Avg(m.colors32)})");
                var uvs = new List<Vector2>();
                for (var ch = 1; ch < 8; ch++) { m.GetUVs(ch, uvs); if (uvs.Count > 0) streams.Add("uv" + ch); }
                if (m.tangents.Length > 0) streams.Add("tangents");
                if (streams.Count > 0) report.Append(" [" + string.Join(" ", streams) + "]");
                foreach (var mat in smr.sharedMaterials.Where(x => x))
                {
                    var sh = mat.shader;
                    var props = Enumerable.Range(0, sh.GetPropertyCount()).Select(i =>
                    {
                        var n = sh.GetPropertyName(i);
                        switch (sh.GetPropertyType(i))
                        {
                            case UnityEngine.Rendering.ShaderPropertyType.Color: return $"{n}={mat.GetColor(n)}";
                            case UnityEngine.Rendering.ShaderPropertyType.Float:
                            case UnityEngine.Rendering.ShaderPropertyType.Range: return $"{n}={F(mat.GetFloat(n))}";
                            case UnityEngine.Rendering.ShaderPropertyType.Texture: return $"{n}={(mat.GetTexture(n) ? mat.GetTexture(n).name : "-")}";
                            default: return n;
                        }
                    });
                    report.Append($" {{{mat.name}: {string.Join(", ", props)}; keywords {string.Join(" ", mat.shaderKeywords)}}}");
                }
                report.Append("; ");
            }
            sb.Append("]}");
            var dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "devtools", "export");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, prefabName + ".json"), sb.ToString());
            return report.ToString();
        }

        private static string Avg(Color32[] cs)
        {
            float r = 0, g = 0, b = 0, a = 0;
            foreach (var c in cs) { r += c.r; g += c.g; b += c.b; a += c.a; }
            var n = cs.Length;
            return $"{r / n:0},{g / n:0},{b / n:0},{a / n:0}";
        }

        private static string F(float f) => f.ToString("0.######", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
        private static string Q(Quaternion q) => $"[{F(q.x)},{F(q.y)},{F(q.z)},{F(q.w)}]";
        private static string M(Matrix4x4 m) => "[" + string.Join(",", Enumerable.Range(0, 16).Select(i => F(m[i % 4, i / 4]))) + "]"; // column-major
        private static string Arr(System.Collections.Generic.IEnumerable<float> xs) => "[" + string.Join(",", xs.Select(F)) + "]";

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        private static class Command
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("gldev_export", "<prefab> dump skinned meshes + skeleton to devtools/export/<prefab>.json (for Blender)", args =>
                {
                    if (args.Length < 2) return;
                    var report = Export(args[1]);
                    DevToolsPlugin.Log.LogInfo("[GLDEV] EXPORT " + args[1] + ": " + report);
                    args.Context.AddString("exported " + args[1] + ": " + report);
                });
            }
        }
    }
}
