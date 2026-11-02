using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// The domesticated look: tamed greenlings swap their meshes for a remodelled version
    /// (rounder body, thicker limbs, leaves) built in Blender from the game's own greyling.
    /// The new meshes keep the original bone list and bind poses, so the vanilla skeleton and
    /// animations drive them unchanged. Wild greylings keep the vanilla meshes.
    ///
    /// File: plugins/Greenlings/models/greenling.glmesh (written by tools/blender/greenling_io.py),
    /// plus an optional greenling.png beside it: the remodel's own skin texture, used point-filtered.
    /// </summary>
    public class GreenlingModel : MonoBehaviour
    {
        private class MeshEntry
        {
            public string renderer;
            public string[] bones;
            public float[] bindposesFlat, vertices, normals, uv, weights;
            public int[] triangles;
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

        private static Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>();
        private static Dictionary<string, string[]> _boneNames = new Dictionary<string, string[]>();
        private static Texture2D _texture;
        internal static bool Enabled = true;
        private static int _version;

        private Greenling _g;
        private readonly Dictionary<SkinnedMeshRenderer, (Mesh mesh, Bounds bounds)> _original = new Dictionary<SkinnedMeshRenderer, (Mesh, Bounds)>();
        private int _applied = -1; // model version currently applied, -1 = vanilla

        /// <summary>True while this creature wears the remodel (the tint stays off: it is already green).</summary>
        internal bool Applied => _applied >= 0;

        public static string DefaultPath =>
            Path.Combine(Path.GetDirectoryName(typeof(GreenlingModel).Assembly.Location), "models", "greenling.glmesh");

        /// <summary>Load (or reload) the model file. Returns a one-line report.</summary>
        public static string Load(string path = null)
        {
            path = path ?? DefaultPath;
            if (!File.Exists(path)) { _meshes = new Dictionary<string, Mesh>(); _version++; return "no model file at " + path; }
            var entries = Parse(File.ReadAllText(path));
            if (entries.Count == 0) { _meshes = new Dictionary<string, Mesh>(); _version++; return "model file has no meshes: " + path; }
            var meshes = new Dictionary<string, Mesh>();
            var bones = new Dictionary<string, string[]>();
            foreach (var e in entries)
            {
                if (e?.vertices == null || e.normals == null || e.triangles == null || e.weights == null || e.bindposesFlat == null || e.bones == null)
                    return $"model file entry {e?.renderer} is incomplete, keeping vanilla look";
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
                meshes[e.renderer] = mesh;
                bones[e.renderer] = e.bones;
            }
            _meshes = meshes;
            _boneNames = bones;
            _texture = LoadTexture(Path.ChangeExtension(path, ".png"));
            _version++;
            return $"loaded {meshes.Count} meshes from {Path.GetFileName(path)}: " +
                   string.Join(", ", meshes.Select(kv => $"{kv.Key} {kv.Value.vertexCount}v")) +
                   (_texture ? $"; texture {_texture.width}x{_texture.height}" : "; vanilla texture");
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
            InvokeRepeating(nameof(Refresh), 0.3f, 1f);
        }

        private void Refresh()
        {
            try { RefreshInner(); }
            catch (Exception e) { Plugin.Log.LogError("model: " + e); Enabled = false; }
        }

        private void RefreshInner()
        {
            var want = Enabled && _g && _g.IsGreenling && _meshes.Count > 0 ? _version : -1;
            if (want == _applied) return;
            if (want < 0) Restore();
            else Apply();
            _applied = want;
        }

        private void Apply()
        {
            foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!_meshes.TryGetValue(smr.name, out var mesh)) continue;
                var expected = _boneNames[smr.name];
                if (!smr.bones.Select(b => b ? b.name : "").SequenceEqual(expected))
                {
                    Plugin.Log.LogError($"model: bone mismatch on {smr.name}, keeping vanilla mesh");
                    continue;
                }
                if (!_original.ContainsKey(smr)) _original[smr] = (smr.sharedMesh, smr.localBounds);
                smr.sharedMesh = mesh;
                var b = _original[smr].bounds;
                b.Expand(b.size * 0.3f); // rounder body and leaves stick out a bit further
                smr.localBounds = b;
                if (IsSkin(smr) && _texture) SetSkin(smr, true);
            }
            _g.InvalidateTint();
            _g.Event("model", "look=greenling");
        }

        private void Restore()
        {
            foreach (var kv in _original)
                if (kv.Key)
                {
                    kv.Key.sharedMesh = kv.Value.mesh;
                    kv.Key.localBounds = kv.Value.bounds;
                    if (IsSkin(kv.Key) && _texture) SetSkin(kv.Key, false);
                }
            _g.InvalidateTint();
            if (_applied >= 0) _g.Event("model", "look=vanilla");
        }

        private static bool IsSkin(Renderer r) => r.sharedMaterial && r.sharedMaterial.shader.name == "Custom/Creature"; // not the eyes

        private static Texture2D _flatNormal;
        private static bool _loggedProps;

        /// <summary>
        /// Swap in the remodel's skin (or put the vanilla one back). The vanilla maps besides _MainTex
        /// (normal map etc.) are laid out for the vanilla UVs, so the remodel gets neutral ones instead.
        /// </summary>
        private static void SetSkin(Renderer r, bool remodel)
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
                if (remodel)
                {
                    if (name == "_MainTex") tex = _texture;
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
                    var greyling = __instance.GetPrefab("Greyling");
                    if (greyling && !greyling.GetComponent<GreenlingModel>()) greyling.AddComponent<GreenlingModel>();
                    Plugin.Log.LogInfo("[GLEVT] model " + Load());
                }
                catch (Exception e)
                {
                    _meshes = new Dictionary<string, Mesh>();
                    Plugin.Log.LogError("model: failed to load, keeping vanilla look: " + e);
                }
            }
        }

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        private static class Command
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("gl_model", "[reload|on|off|<path>] greenling remodel: reload the model file, toggle it, or load another file", args =>
                {
                    var arg = args.Length > 1 ? args.FullLine.Substring(args.FullLine.IndexOf(' ') + 1).Trim() : "reload";
                    string report;
                    if (arg == "on") { Enabled = true; _version++; report = "greenling model on"; }
                    else if (arg == "off") { Enabled = false; report = "greenling model off (vanilla look)"; }
                    else
                    {
                        try { report = Load(arg == "reload" ? null : arg); }
                        catch (Exception e) { report = "model load failed: " + e.Message; Plugin.Log.LogError(e); }
                    }
                    Plugin.Log.LogInfo("[GLEVT] model " + report);
                    args.Context.AddString(report);
                }, isCheat: true);
            }
        }
    }
}
