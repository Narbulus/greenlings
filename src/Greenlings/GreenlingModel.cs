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
    ///
    /// A body may carry decorations (trunks, mushrooms, twigs, leaf cards; Prop Studio in Blender,
    /// tools/blender/decor_export.py): static meshes already in a bone's local space, one per bone and
    /// material, shown as plain MeshRenderers parented to the bones (no extra skinning). Props use the
    /// creature material with the body's &lt;name&gt;.decor.png atlas; leaf cards borrow the game's own
    /// foliage material. The death ragdoll wears the same body and decorations (gl_rbody on its ZDO).
    /// </summary>
    public class GreenlingModel : MonoBehaviour
    {
        internal const string ZBody = "gl_body";

        private class MeshEntry
        {
            public string renderer, bone, material; // bone/material: decor entries only
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
            public readonly List<(string bone, string material, Mesh mesh)> decor = new List<(string, string, Mesh)>();
            public Texture2D decorTexture;
            public Material propMaterial; // made on first use from the creature's skin material
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
                if (word == "decor") { cur = new MeshEntry { renderer = "decor", bone = Next(), material = Next() }; list.Add(cur); continue; }
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
            _leaves.Clear();
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
                   $"; decor {body.decor.Count} parts" +
                   (body.texture ? $"; texture {body.texture.width}x{body.texture.height}" : "; vanilla texture");
        }

        private static Body LoadBody(string path)
        {
            try
            {
                var body = new Body { name = Path.GetFileNameWithoutExtension(path) };
                foreach (var e in Parse(File.ReadAllText(path)))
                {
                    if (e?.bone != null)
                    {
                        if (e.vertices != null && e.triangles != null) body.decor.Add((e.bone, e.material, BuildStaticMesh(e)));
                        continue;
                    }
                    if (e?.vertices == null || e.normals == null || e.triangles == null || e.weights == null || e.bindposesFlat == null || e.bones == null)
                    {
                        Plugin.Log.LogWarning($"model: {path}: entry {e?.renderer} is incomplete, skipping the file");
                        return null;
                    }
                    body.meshes[e.renderer] = BuildMesh(e);
                    body.bones[e.renderer] = e.bones;
                }
                body.texture = LoadTexture(Path.ChangeExtension(path, ".png"));
                body.decorTexture = LoadTexture(Path.ChangeExtension(path, ".decor.png"), mipmaps: false);
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

        private static Mesh BuildStaticMesh(MeshEntry e)
        {
            var n = e.vertices.Length / 3;
            var mesh = new Mesh { name = $"Greenling_decor_{e.bone}_{e.material}" };
            mesh.vertices = Enumerable.Range(0, n).Select(i => new Vector3(e.vertices[i * 3], e.vertices[i * 3 + 1], e.vertices[i * 3 + 2])).ToArray();
            if (e.normals != null && e.normals.Length == n * 3)
                mesh.normals = Enumerable.Range(0, n).Select(i => new Vector3(e.normals[i * 3], e.normals[i * 3 + 1], e.normals[i * 3 + 2])).ToArray();
            if (e.uv != null && e.uv.Length == n * 2)
                mesh.uv = Enumerable.Range(0, n).Select(i => new Vector2(e.uv[i * 2], e.uv[i * 2 + 1])).ToArray();
            mesh.triangles = e.triangles;
            if (e.normals == null) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            mesh.UploadMeshData(true); // never read back: free the CPU copy
            return mesh;
        }

        private static Texture2D LoadTexture(string path, bool mipmaps = true)
        {
            if (!File.Exists(path)) return null;
            // the decor atlas packs tiny tiles side by side: mipmaps would blend neighbours
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipmaps) { name = "Greenling_skin" };
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

        private readonly List<GameObject> _decor = new List<GameObject>();

        private void Apply(Body body)
        {
            ApplyLook(gameObject, body, _prefab, _original, _decor);
            _g.InvalidateTint();
            _g.Event("model", "look=greenling body=" + body.name + " decor=" + body.decor.Count);
        }

        /// <summary>
        /// Put a body (meshes, skin, eye glow, decorations) on a creature or its ragdoll. original:
        /// the vanilla meshes, for Restore; decor: the decoration objects made, for Restore.
        /// </summary>
        private static void ApplyLook(GameObject root, Body body, string prefab,
            Dictionary<SkinnedMeshRenderer, (Mesh mesh, Bounds bounds)> original, List<GameObject> decor)
        {
            Material skin = null;
            foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!body.meshes.TryGetValue(smr.name, out var mesh)) continue;
                mesh = ForBones(mesh, body.bones[smr.name], smr.bones);
                if (!mesh)
                {
                    Plugin.Log.LogError($"model: bone mismatch on {prefab}/{root.name}/{smr.name}, keeping vanilla mesh");
                    continue;
                }
                if (!original.ContainsKey(smr)) original[smr] = (smr.sharedMesh, smr.localBounds);
                smr.sharedMesh = mesh;
                var b = original[smr].bounds;
                b.Expand(b.size * 0.3f); // rounder body and leaves stick out a bit further
                smr.localBounds = b;
                if (IsSkin(smr))
                {
                    skin = skin ? skin : smr.sharedMaterial;
                    if (body.texture) SetSkin(smr, body.texture);
                }
                else SetEyeGlow(smr, true);
            }
            if (body.decor.Count == 0) return;
            var bones = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;
            foreach (var (bone, material, mesh) in body.decor)
            {
                if (!bones.TryGetValue(bone, out var parent)) continue;
                var mat = material == "props" ? PropMaterial(body, skin) : LeafMaterial(material.Substring(5));
                if (!mat) continue;
                var go = new GameObject("gl_decor_" + material);
                go.layer = parent.gameObject.layer;
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                decor.Add(go);
            }
        }

        private static readonly Dictionary<(Mesh, string), Mesh> _remapped = new Dictionary<(Mesh, string), Mesh>();

        /// <summary>
        /// The body mesh for a renderer's bone list: as is when the order matches (the creature), else a
        /// copy with its bone indices and bind poses reordered by bone name (ragdolls may list bones in
        /// their own order). Null if a bone is missing.
        /// </summary>
        private static Mesh ForBones(Mesh mesh, string[] meshBones, Transform[] target)
        {
            var names = target.Select(b => b ? b.name : "").ToArray();
            if (names.SequenceEqual(meshBones)) return mesh;
            var key = (mesh, string.Join(",", names));
            if (_remapped.TryGetValue(key, out var done)) return done;
            var index = new Dictionary<string, int>();
            for (var i = 0; i < names.Length; i++) if (!index.ContainsKey(names[i])) index[names[i]] = i;
            if (meshBones.Any(b => !index.ContainsKey(b))) return _remapped[key] = null;
            var map = meshBones.Select(b => index[b]).ToArray();
            var copy = UnityEngine.Object.Instantiate(mesh);
            copy.name = mesh.name + "_remap";
            copy.boneWeights = mesh.boneWeights.Select(w => new BoneWeight
            {
                boneIndex0 = map[w.boneIndex0], weight0 = w.weight0, boneIndex1 = map[w.boneIndex1], weight1 = w.weight1,
                boneIndex2 = map[w.boneIndex2], weight2 = w.weight2, boneIndex3 = map[w.boneIndex3], weight3 = w.weight3,
            }).ToArray();
            var bind = Enumerable.Repeat(Matrix4x4.identity, names.Length).ToArray();
            for (var i = 0; i < map.Length; i++) bind[map[i]] = mesh.bindposes[i];
            copy.bindposes = bind;
            return _remapped[key] = copy;
        }

        /// <summary>Trunks/mushrooms/twigs: the creature's own material with the decor atlas, shared by every wearer.</summary>
        private static Material PropMaterial(Body body, Material skin)
        {
            if (body.propMaterial || !skin || !body.decorTexture) return body.propMaterial;
            var m = new Material(skin) { name = "Greenling_decor_" + body.name };
            foreach (var name in m.GetTexturePropertyNames())
            {
                if (!m.GetTexture(name)) continue;
                if (name == "_MainTex") m.SetTexture(name, body.decorTexture);
                else if (name.IndexOf("Bump", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Normal", StringComparison.OrdinalIgnoreCase) >= 0)
                    m.SetTexture(name, FlatNormal());
            }
            return body.propMaterial = m;
        }

        private static readonly Dictionary<string, Material> _leaves = new Dictionary<string, Material>();

        /// <summary>The game's foliage material drawing this texture (looked up once on the vegetation prefabs).</summary>
        private static Material LeafMaterial(string texture)
        {
            if (_leaves.TryGetValue(texture, out var found)) return found;
            Material hit = null;
            if (ZNetScene.instance)
                foreach (var prefab in ZNetScene.instance.m_prefabs)
                {
                    if (!prefab) continue;
                    foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    {
                        hit = r.sharedMaterials.FirstOrDefault(m => m && m.HasProperty("_MainTex") && m.mainTexture && m.mainTexture.name == texture);
                        if (hit) break;
                    }
                    if (hit) break;
                }
            if (!hit) Plugin.Log.LogWarning($"model: no game material uses leaf texture {texture}; those leaves are not shown");
            return _leaves[texture] = hit;
        }

        private void Restore()
        {
            foreach (var go in _decor)
                if (go) Destroy(go);
            _decor.Clear();
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

        internal const string ZRagdollBody = "gl_rbody"; // "<prefab>/<body>" on a ragdoll's ZDO

        private Body Worn => _applied >= 0 ? _body : null;

        private static Body FindBody(string prefab, string name)
        {
            if (_forced.TryGetValue(prefab, out var f) && f.name == name) return f;
            return _pools.TryGetValue(prefab, out var pool) ? pool.FirstOrDefault(b => b.name == name) : null;
        }

        /// <summary>The dying greenling's look goes onto its ragdoll, and onto the ragdoll's ZDO for the other clients.</summary>
        private static void DressRagdoll(Character c, Ragdoll ragdoll)
        {
            try
            {
                var model = c ? c.GetComponent<GreenlingModel>() : null;
                var body = model ? model.Worn : null;
                if (body == null || !ragdoll || !ragdoll.m_nview || !ragdoll.m_nview.IsValid()) return;
                ragdoll.m_nview.GetZDO().Set(ZRagdollBody, model._prefab + "/" + body.name);
                RagdollLook.Dress(ragdoll.gameObject, model._prefab, body);
            }
            catch (Exception e) { Plugin.Log.LogError("model: ragdoll: " + e); }
        }

        [HarmonyPatch(typeof(Character), "OnRagdollCreated")]
        private static class RagdollCharacter
        {
            private static void Postfix(Character __instance, Ragdoll ragdoll) => DressRagdoll(__instance, ragdoll);
        }

        [HarmonyPatch(typeof(Humanoid), "OnRagdollCreated")]
        private static class RagdollHumanoid
        {
            private static void Postfix(Humanoid __instance, Ragdoll ragdoll) => DressRagdoll(__instance, ragdoll);
        }

        /// <summary>Other clients see the ragdoll appear from its ZDO: dress it from gl_rbody.</summary>
        [HarmonyPatch(typeof(Ragdoll), "Start")]
        private static class RagdollStart
        {
            private static void Postfix(Ragdoll __instance)
            {
                try
                {
                    if (!Enabled || __instance.GetComponent<RagdollLook>() || !__instance.m_nview || !__instance.m_nview.IsValid()) return;
                    var tag = __instance.m_nview.GetZDO().GetString(ZRagdollBody);
                    var slash = tag.IndexOf('/');
                    if (slash <= 0) return;
                    var prefab = tag.Substring(0, slash);
                    var body = FindBody(prefab, tag.Substring(slash + 1));
                    if (body != null) RagdollLook.Dress(__instance.gameObject, prefab, body);
                }
                catch (Exception e) { Plugin.Log.LogError("model: ragdoll: " + e); }
            }
        }

        /// <summary>Marks a dressed ragdoll (destroyed along with it, so nothing to undo).</summary>
        private class RagdollLook : MonoBehaviour
        {
            internal static void Dress(GameObject ragdoll, string prefab, Body body)
            {
                if (ragdoll.GetComponent<RagdollLook>()) return;
                ragdoll.AddComponent<RagdollLook>();
                ApplyLook(ragdoll, body, prefab, new Dictionary<SkinnedMeshRenderer, (Mesh, Bounds)>(), new List<GameObject>());
            }
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
