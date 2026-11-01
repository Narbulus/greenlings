using System.Collections;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings.DevTools
{
    /// <summary>
    /// gldev_photo: reference renders of a creature with the game's own shaders and textures.
    /// A look-only copy (renderers + animator, idle pose) is shot high above the world with an
    /// orthographic camera and its own lights, on a transparent background:
    ///   front, threequarter, side, back, head_front, head_threequarter, plus albedo textures.
    /// Output: BepInEx/devtools/photos/&lt;prefab&gt;/
    /// </summary>
    internal static class PhotoStudio
    {
        private static readonly (string name, float yaw, bool head)[] Shots =
        {
            ("front", 180f, false), ("threequarter", 135f, false), ("side", 90f, false), ("back", 0f, false),
            ("head_front", 180f, true), ("head_threequarter", 135f, true),
        };

        public static IEnumerator Shoot(string prefabName, int size)
        {
            var prefab = ZNetScene.instance.GetPrefab(prefabName);
            if (!prefab) { DevToolsPlugin.Log.LogWarning("[GLDEV] photo: no prefab " + prefabName); yield break; }
            var dir = Path.Combine(BepInEx.Paths.BepInExRootPath, "devtools", "photos", prefabName);
            Directory.CreateDirectory(dir);

            // look-only copy: instantiated inactive so no game scripts run, keep renderers + animator
            var staging = new GameObject("PhotoStaging");
            staging.SetActive(false);
            var model = Object.Instantiate(prefab, staging.transform, false);
            foreach (var c in model.GetComponentsInChildren<Component>(true).Reverse())
                if (!(c is Transform || c is Renderer || c is MeshFilter || c is LODGroup || c is Animator)) Object.DestroyImmediate(c);
            var studio = Player.m_localPlayer.transform.position + Vector3.up * 600f;
            model.transform.SetParent(null, false);
            model.transform.SetPositionAndRotation(studio, Quaternion.identity);
            Object.Destroy(staging);
            foreach (var smr in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            // our camera renders manually, so the animator would think nobody is watching and fall back to the bind pose
            var animators = model.GetComponentsInChildren<Animator>(true);
            foreach (var a in animators) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            // lights that only matter up here
            var key = new GameObject("PhotoKey").AddComponent<Light>();
            key.type = LightType.Directional; key.intensity = 1.25f; key.color = new Color(1f, 0.96f, 0.9f);
            key.shadows = LightShadows.None;
            var fill = new GameObject("PhotoFill").AddComponent<Light>();
            fill.type = LightType.Directional; fill.intensity = 0.45f; fill.color = new Color(0.85f, 0.9f, 1f);
            fill.shadows = LightShadows.None;

            var ambientMode = RenderSettings.ambientMode;
            var ambient = RenderSettings.ambientLight;
            var fog = RenderSettings.fog;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.42f, 0.42f, 0.45f);
            RenderSettings.fog = false;

            var rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var cam = new GameObject("PhotoCam").AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.targetTexture = rt;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 60f;

            // let the animator settle into its idle pose
            for (var i = 0; i < 10; i++) yield return null;

            var head = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "head");
            var headEnd = model.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "head_end");
            try
            {
                foreach (var (shot, yaw, isHead) in Shots)
                {
                    if (isHead && !head) continue;
                    model.transform.rotation = Quaternion.Euler(0f, yaw, 0f); // camera stays on +Z looking -Z
                    yield return null;
                    foreach (var a in animators) a.Update(0f); // make sure this frame shows the idle pose
                    var b = Bounds(model);
                    Vector3 center; float half;
                    if (isHead)
                    {
                        // frame head + horns/antlers: a slice of the body height around the head bone
                        half = Mathf.Max(0.3f, b.size.y * 0.24f);
                        center = head.position + Vector3.up * half * 0.35f;
                    }
                    else
                    {
                        center = b.center;
                        half = Mathf.Max(b.extents.y, b.extents.x, b.extents.z) * 1.08f;
                    }
                    cam.orthographicSize = half;
                    cam.transform.SetPositionAndRotation(center + Vector3.back * 20f, Quaternion.LookRotation(Vector3.forward));
                    key.transform.rotation = Quaternion.Euler(35f, 25f, 0f);
                    fill.transform.rotation = Quaternion.Euler(10f, -140f, 0f);
                    cam.Render();
                    Save(rt, Path.Combine(dir, shot + ".png"));
                }

                // the actual textures the model uses
                foreach (var mat in model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct())
                {
                    var tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                    if (!tex) continue;
                    var trt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(tex, trt);
                    Save(trt, Path.Combine(dir, "texture_" + Safe(mat.name) + ".png"));
                    RenderTexture.ReleaseTemporary(trt);
                }

                var bounds = Bounds(model);
                File.WriteAllText(Path.Combine(dir, "info.txt"),
                    $"prefab={prefabName}\nheight_m={bounds.size.y:0.00}\nwidth_m={bounds.size.x:0.00}\ndepth_m={bounds.size.z:0.00}\n" +
                    "materials=" + string.Join(", ", model.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => $"{m.name} ({m.shader.name})").Distinct()) + "\n" +
                    "meshes=" + string.Join(", ", model.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.sharedMesh).Select(r => $"{r.name}:{r.sharedMesh.vertexCount}v/{r.sharedMesh.triangles.Length / 3}t")) + "\n");
                DevToolsPlugin.Marker($"PHOTO {prefabName} -> {dir}");
            }
            finally
            {
                RenderSettings.ambientMode = ambientMode;
                RenderSettings.ambientLight = ambient;
                RenderSettings.fog = fog;
                Object.Destroy(cam.gameObject);
                Object.Destroy(key.gameObject);
                Object.Destroy(fill.gameObject);
                Object.Destroy(model);
                rt.Release();
            }
        }

        private static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(false);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private static void Save(RenderTexture rt, string path)
        {
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        private static string Safe(string s) => new string(s.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        private static class Command
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("gldev_photo", "<prefab> [size=1024] reference renders (turnaround, head, textures) into devtools/photos", args =>
                {
                    if (args.Length < 2 || !Player.m_localPlayer) return;
                    DevToolsPlugin.Instance.StartCoroutine(Shoot(args[1], args.TryParameterInt(2, 1024)));
                    args.Context.AddString("photographing " + args[1]);
                });
            }
        }
    }
}
