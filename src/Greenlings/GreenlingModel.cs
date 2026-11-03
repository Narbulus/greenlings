using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// The domesticated look: tamed greenlings swap their meshes for a remodelled body built in
    /// Blender (tools/tripo + tools/blender). The new meshes keep the original bone list and bind
    /// poses, so the vanilla skeleton and animations drive them unchanged. Wild ones keep the vanilla meshes.
    ///
    /// Bodies come in pools per creature prefab: plugins/Greenlings/models/&lt;Prefab&gt;/*.glmesh, each with
    /// an optional .png beside it (its own skin, used point-filtered). The old single file
    /// models/greenling.glmesh is the Greyling pool when there is no models/Greyling folder.
    /// Each greenling picks a body from its pool by a seed stored in its ZDO (gl_body), so every
    /// client shows the same one.
    /// </summary>
    public class GreenlingModel : MonoBehaviour
    {
        internal const string ZBody = "gl_body";

        private class MeshEntry
        {
            public string renderer;
            public string[] bones;
            public float[] bindposesFlat, vertices, normals, uv, weights;
            public int[] triangles;
        }

        /// <summary>One loaded body: meshes by renderer name, their bone lists, and its skin.</summary>
        private class Body
        {
            public string name;
            public readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
            public readonly Dictionary<string, string[]> bones = new Dictionary<string, string[]>();
            public Texture2D texture;
        }

        /// <summary>
        /// Parse the .glmesh text format written by tools/blender/greenling_io.py:
        /// "glmesh 1", then per mesh "mesh &lt;renderer&gt;", sections "&lt;name&gt; &lt;count&gt; &lt;values...&gt;", "end".
        /// </summary>
        private static List<MeshEntry> Parse(string text)
        {
            var tok = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries); // any whitespace
            var i = 0;
            string Next() => i < tok.Length ? tok[i++] : throw new FormatException("unexpected end of model file");
            float F(string t) => float.Parse(t, System.Globalization.CultureInfo.InvariantCulture);
            if (Next() != "glmesh" || Next() != "1") throw new FormatException("not a glmesh v1 file");
            var list = new List<MeshEntry>();
            MeshEntry cur = null;
            while (i < tok.Length)
            {
                var word = Next();
                if (word == "mesh") { cur = new MeshEntry { renderer = Next() }; list.Add(cur); continue; }
                if (word == "end") { cur = null; continue; }
                if (cur == null) throw new FormatException("section outside a mesh: " + word);
                var count = int.Parse(Next());
                switch (word)
                {
                    case "bones": cur.bones = Enumerable.Range(0, count).Select(_ => Next()).ToArray(); break;
                    case "triangles": cur.triangles = Enumerable.Range(0, count).Select(_ => int.Parse(Next())).ToArray(); break;
                    default:
                        var vals = Enumerable.Range(0, count).Select(_ => F(Next())).ToArray();
                        if (word == "bindposes") cur.bindposesFlat = vals;
                        else if (word == "vertices") cur.vertices = vals;
                        else if (word == "normals") cur.normals = vals;
                        else if (word == "uv") cur.uv = vals;
                        else if (word == "weights") cur.weights = vals;
                        break; // unknown sections are skipped (values already consumed)
                }
            }
            return list;
        }

        private static Dictionary<string, List<Body>> _pools = new Dictionary<string, List<Body>>();
        private static readonly Dictionary<string, Body> _forced = new Dictionary<string, Body>(); // gl_model <prefab>/<name>
        internal static bool Enabled = true;
        private static int _version;

        private Greenling _g;
        private string _prefab;
        private readonly Dictionary<SkinnedMeshRenderer, (Mesh mesh, Bounds bounds)> _original = new Dictionary<SkinnedMeshRenderer, (Mesh, Bounds)>();
        private Body _body; // body currently worn, null = vanilla
        private int _applied = -1; // model version currently applied, -1 = vanilla

        /// <summary>True while this creature wears the remodel (the tint stays off: it is already green).</summary>
        internal bool Applied => _applied >= 0;

        public static string ModelDir => Path.Combine(Path.GetDirectoryName(typeof(GreenlingModel).Assembly.Location), "models");

        /// <summary>(Re)load every pool. Returns a one-line report.</summary>
        public static string LoadAll()
        {
            var pools = new Dictionary<string, List<Body>>();
            if (Directory.Exists(ModelDir))
                foreach (var dir in Directory.GetDirectories(ModelDir))
                {
                    var bodies = Directory.GetFiles(dir, "*.glmesh").OrderBy(f => f).Select(LoadBody).Where(b => b != null).ToList();
                    if (bodies.Count > 0) pools[Path.GetFileName(dir)] = bodies;
                }
            var legacy = Path.Combine(ModelDir, "greenling.glmesh");
            if (!pools.ContainsKey("Greyling") && File.Exists(legacy) && LoadBody(legacy) is Body b0) pools["Greyling"] = new List<Body> { b0 };
            _pools = pools;
            _forced.Clear();
            _version++;
            return pools.Count == 0 ? "no models in " + ModelDir
                : string.Join("; ", pools.Select(kv => kv.Key + ": " + string.Join(", ", kv.Value.Select(b => b.name))));
        }

        /// <summary>
        /// Show one body on every greenling of a prefab (testing): "Greydwarf_Elite/brute2", or a bare
        /// name for Greyling (looked up in models/Greyling, then loose in models/).
        /// </summary>
        public static string Force(string arg)
        {
            var slash = arg.IndexOf('/');
            var prefab = slash > 0 ? arg.Substring(0, slash) : "Greyling";
            var name = slash > 0 ? arg.Substring(slash + 1) : arg;
            var path = Path.Combine(ModelDir, prefab, name + ".glmesh");
            if (!File.Exists(path)) path = Path.Combine(ModelDir, name + ".glmesh");
            var body = File.Exists(path) ? LoadBody(path) : null;
            if (body == null) return "no model " + arg;
            _forced[prefab] = body;
            _version++;
            return $"{prefab} forced to {body.name}: " + string.Join(", ", body.meshes.Select(kv => $"{kv.Key} {kv.Value.vertexCount}v")) +
                   (body.texture ? $"; texture {body.texture.width}x{body.texture.height}" : "; vanilla texture");
        }

        private static Body LoadBody(string path)
        {
            try
            {
                var body = new Body { name = Path.GetFileNameWithoutExtension(path) };
                foreach (var e in Parse(File.ReadAllText(path)))
                {
                    if (e?.vertices == null || e.normals == null || e.triangles == null || e.weights == null || e.bindposesFlat == null || e.bones == null)
                    {
                        Plugin.Log.LogWarning($"model: {path}: entry {e?.renderer} is incomplete, skipping the file");
                        return null;
                    }
                    body.meshes[e.renderer] = BuildMesh(e);
                    body.bones[e.renderer] = e.bones;
                }
                body.texture = LoadTexture(Path.ChangeExtension(path, ".png"));
                return body.meshes.Count > 0 ? body : null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"model: cannot load {path}: {ex.Message}");
                return null;
            }
        }

        private static Mesh BuildMesh(MeshEntry e)
        {
            var n = e.vertices.Length / 3;
            var mesh = new Mesh { name = "Greenling_" + e.renderer };
            if (n > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = Enumerable.Range(0, n).Select(i => new Vector3(e.vertices[i * 3], e.vertices[i * 3 + 1], e.vertices[i * 3 + 2])).ToArray();
            mesh.normals = Enumerable.Range(0, n).Select(i => new Vector3(e.normals[i * 3], e.normals[i * 3 + 1], e.normals[i * 3 + 2])).ToArray();
            if (e.uv != null && e.uv.Length == n * 2)
                mesh.uv = Enumerable.Range(0, n).Select(i => new Vector2(e.uv[i * 2], e.uv[i * 2 + 1])).ToArray();
            mesh.triangles = e.triangles;
            mesh.boneWeights = Enumerable.Range(0, n).Select(i =>
            {
                var w = e.weights;
                var o = i * 8;
                return new BoneWeight
                {
                    boneIndex0 = (int)w[o], weight0 = w[o + 1], boneIndex1 = (int)w[o + 2], weight1 = w[o + 3],
                    boneIndex2 = (int)w[o + 4], weight2 = w[o + 5], boneIndex3 = (int)w[o + 6], weight3 = w[o + 7],
                };
            }).ToArray();
            mesh.bindposes = Enumerable.Range(0, e.bindposesFlat.Length / 16).Select(b =>
            {
                var m = new Matrix4x4();
                for (var i = 0; i < 16; i++) m[i % 4, i / 4] = e.bindposesFlat[b * 16 + i]; // column-major
                return m;
            }).ToArray();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents(); // the creature shader is normal mapped
            return mesh;
        }

        private static Texture2D LoadTexture(string path)
        {
            if (!File.Exists(path)) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "Greenling_skin" };
            // called by reflection: the compiler otherwise trips over Unity 6's ReadOnlySpan overload on net472
            var load = typeof(ImageConversion).GetMethod(nameof(ImageConversion.LoadImage), new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
            if (!(bool)load.Invoke(null, new object[] { tex, File.ReadAllBytes(path), false })) return null;
            tex.filterMode = FilterMode.Point; // the vanilla creatures' crisp low-res look
            tex.wrapMode = TextureWrapMode.Clamp;
            return tex;
        }

        private void Awake()
        {
            _g = GetComponent<Greenling>();
            _prefab = Utils.GetPrefabName(gameObject);
            InvokeRepeating(nameof(Refresh), 0.3f, 1f);
        }

        private void Refresh()
        {
            try { RefreshInner(); }
            catch (Exception e) { Plugin.Log.LogError("model: " + e); Enabled = false; }
        }

        private void RefreshInner()
        {
            var want = Enabled && _g && _g.IsGreenling ? Pick() : null;
            var version = want != null ? _version : -1;
            if (want == _body && version == _applied) return;
            if (_body != null) Restore();
            if (want != null) Apply(want);
            _body = want;
            _applied = version;
        }

        /// <summary>This greenling's body: the forced one, else its seeded pick from the prefab's pool.</summary>
        private Body Pick()
        {
            if (_forced.TryGetValue(_prefab, out var forced)) return forced;
            if (!_pools.TryGetValue(_prefab, out var pool) || pool.Count == 0) return null;
            var zdo = _g.Zdo;
            if (zdo == null) return null;
            var seed = zdo.GetInt(ZBody, -1);
            if (seed < 0)
            {
                if (!_g.IsOwner) return null; // the owner rolls it; the others pick it up from the ZDO
                seed = UnityEngine.Random.Range(0, int.MaxValue);
                zdo.Set(ZBody, seed);
            }
            return pool[seed % pool.Count];
        }

        private void Apply(Body body)
        {
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!body.meshes.TryGetValue(smr.name, out var mesh)) continue;
                if (!smr.bones.Select(b => b ? b.name : "").SequenceEqual(body.bones[smr.name]))
                {
                    Plugin.Log.LogError($"model: bone mismatch on {_prefab}/{smr.name}, keeping vanilla mesh");
                    continue;
                }
                if (!_original.ContainsKey(smr)) _original[smr] = (smr.sharedMesh, smr.localBounds);
                smr.sharedMesh = mesh;
                var b = _original[smr].bounds;
                b.Expand(b.size * 0.3f); // rounder body and leaves stick out a bit further
                smr.localBounds = b;
                if (IsSkin(smr) && body.texture) SetSkin(smr, body.texture);
                else if (!IsSkin(smr)) SetEyeGlow(smr, true);
            }
            _g.InvalidateTint();
            _g.Event("model", "look=greenling body=" + body.name);
        }

        private void Restore()
        {
            foreach (var kv in _original)
                if (kv.Key)
                {
                    kv.Key.sharedMesh = kv.Value.mesh;
                    kv.Key.localBounds = kv.Value.bounds;
                    if (IsSkin(kv.Key)) SetSkin(kv.Key, null);
                    else SetEyeGlow(kv.Key, false);
                }
            _g.InvalidateTint();
            if (_applied >= 0) _g.Event("model", "look=vanilla");
        }

        private static bool IsSkin(Renderer r) => r.sharedMaterial && r.sharedMaterial.shader.name == "Custom/Creature"; // not the eyes

        /// <summary>Remodelled bodies glow soft pale yellow from the eyes, whatever the creature's vanilla eye colour.</summary>
        private static readonly Color EyeColor = new Color(1f, 0.85f, 0.45f), EyeGlow = new Color(2.6f, 2.0f, 0.8f);

        private static void SetEyeGlow(Renderer r, bool remodel)
        {
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block);
            if (remodel)
            {
                block.SetColor("_Color", EyeColor);
                block.SetColor("_EmissionColor", EyeGlow);
            }
            else block.Clear();
            r.SetPropertyBlock(block);
        }

        private static Texture2D _flatNormal;
        private static bool _loggedProps;

        /// <summary>
        /// Swap in a remodel's skin (or, with null, put the vanilla one back). The vanilla maps besides
        /// _MainTex (normal map etc.) are laid out for the vanilla UVs, so the remodel gets neutral ones instead.
        /// </summary>
        private static void SetSkin(Renderer r, Texture2D skin)
        {
            var mat = r.sharedMaterial;
            var block = new MaterialPropertyBlock();
            r.GetPropertyBlock(block); // keeps the tint
            foreach (var name in mat.GetTexturePropertyNames())
            {
                var vanilla = mat.GetTexture(name);
                if (!vanilla) continue;
                if (!_loggedProps) Plugin.Log.LogInfo($"[GLEVT] model skin_prop name={name} tex={vanilla.name}");
                Texture tex = vanilla;
                if (skin)
                {
                    if (name == "_MainTex") tex = skin;
                    else if (name.IndexOf("Bump", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0) tex = FlatNormal();
                }
                block.SetTexture(name, tex);
            }
            _loggedProps = true;
            r.SetPropertyBlock(block);
        }

        private static Texture2D FlatNormal()
        {
            if (_flatNormal) return _flatNormal;
            // (0.5, 0.5, 1) unpacks flat as RGB; alpha 0.5 keeps it flat for the AG (DXT5nm) unpack too
            _flatNormal = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "Greenling_flat_normal" };
            _flatNormal.SetPixels32(Enumerable.Repeat(new Color32(128, 128, 255, 128), 16).ToArray());
            _flatNormal.Apply();
            return _flatNormal;
        }

        [HarmonyPatch(typeof(ZNetScene), "Awake")]
        private static class Setup
        {
            private static void Postfix(ZNetScene __instance)
            {
                // never let a missing/bad model file break world loading
                try
                {
                    Plugin.Log.LogInfo("[GLEVT] model " + LoadAll());
                    foreach (var prefab in Roles.TameablePrefabs)
                    {
                        var go = __instance.GetPrefab(prefab);
                        if (go && !go.GetComponent<GreenlingModel>()) go.AddComponent<GreenlingModel>();
                    }
                }
                catch (Exception e)
                {
                    _pools = new Dictionary<string, List<Body>>();
                    Plugin.Log.LogError("model: failed to load, keeping vanilla look: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        private static class Command
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("gl_model", "[reload|on|off|pool|<prefab>/<name>|<name>] greenling bodies: reload the pools, toggle, " +
                    "drop forced bodies (pool), or force one body on every greenling of a prefab (a bare name means Greyling)", args =>
                {
                    var arg = args.Length > 1 ? args.FullLine.Substring(args.FullLine.IndexOf(' ') + 1).Trim() : "reload";
                    string report;
                    if (arg == "on") { Enabled = true; _version++; report = "greenling model on"; }
                    else if (arg == "off") { Enabled = false; report = "greenling model off (vanilla look)"; }
                    else if (arg == "pool") { _forced.Clear(); _version++; report = "bodies from the pools again"; }
                    else
                    {
                        try { report = arg == "reload" ? "reloaded " + LoadAll() : Force(arg); }
                        catch (Exception e) { report = "model load failed: " + e.Message; Plugin.Log.LogError(e); }
                    }
                    Plugin.Log.LogInfo("[GLEVT] model " + report);
                    args.Context.AddString(report);
                }, isCheat: true);
            }
        }
    }
}
