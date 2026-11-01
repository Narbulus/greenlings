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
    /// File: plugins/Greenlings/models/greenling.json (written by tools/blender/greenling_io.py).
    /// </summary>
    public class GreenlingModel : MonoBehaviour
    {
        [Serializable]
        private class ModelFile
        {
            public string prefab;
            public MeshEntry[] meshes;
        }

        [Serializable]
        private class MeshEntry
        {
            public string renderer;
            public string[] bones;
            public float[] bindposesFlat;
            public float[] vertices, normals, uv, weights;
            public int[] triangles;
        }

        private static Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>();
        private static Dictionary<string, string[]> _boneNames = new Dictionary<string, string[]>();
        internal static bool Enabled = true;
        private static int _version;

        private Greenling _g;
        private readonly Dictionary<SkinnedMeshRenderer, (Mesh mesh, Bounds bounds)> _original = new Dictionary<SkinnedMeshRenderer, (Mesh, Bounds)>();
        private int _applied = -1; // model version currently applied, -1 = vanilla

        public static string DefaultPath =>
            Path.Combine(Path.GetDirectoryName(typeof(GreenlingModel).Assembly.Location), "models", "greenling.json");

        /// <summary>Load (or reload) the model file. Returns a one-line report.</summary>
        public static string Load(string path = null)
        {
            path = path ?? DefaultPath;
            if (!File.Exists(path)) { _meshes = new Dictionary<string, Mesh>(); _version++; return "no model file at " + path; }
            var file = JsonUtility.FromJson<ModelFile>(File.ReadAllText(path));
            if (file?.meshes == null || file.meshes.Length == 0) { _meshes = new Dictionary<string, Mesh>(); _version++; return "model file has no meshes (wrong format?): " + path; }
            var meshes = new Dictionary<string, Mesh>();
            var bones = new Dictionary<string, string[]>();
            foreach (var e in file.meshes)
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
                meshes[e.renderer] = mesh;
                bones[e.renderer] = e.bones;
            }
            _meshes = meshes;
            _boneNames = bones;
            _version++;
            return $"loaded {meshes.Count} meshes from {Path.GetFileName(path)}: " +
                   string.Join(", ", meshes.Select(kv => $"{kv.Key} {kv.Value.vertexCount}v"));
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
            }
            _g.Event("model", "look=greenling");
        }

        private void Restore()
        {
            foreach (var kv in _original)
                if (kv.Key)
                {
                    kv.Key.sharedMesh = kv.Value.mesh;
                    kv.Key.localBounds = kv.Value.bounds;
                }
            if (_applied >= 0) _g.Event("model", "look=vanilla");
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
