using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>Look-only copies of game objects (carried items and tools).</summary>
    internal static class Models
    {
        private static Transform _inactiveRoot;

        /// <summary>
        /// Copy a prefab's visuals: instantiated under an inactive parent so none of its scripts
        /// (ZNetView, Pickable, ItemDrop...) ever wake up, then stripped to transforms + renderers.
        /// </summary>
        public static GameObject Visual(GameObject prefab, string name)
        {
            if (!_inactiveRoot)
            {
                var root = new GameObject("GreenlingModelStaging");
                root.SetActive(false);
                Object.DontDestroyOnLoad(root);
                _inactiveRoot = root.transform;
            }

            var go = Object.Instantiate(prefab, _inactiveRoot, false);
            go.name = name;
            foreach (var c in go.GetComponentsInChildren<Component>(true).Reverse())
            {
                if (c is Transform || c is Renderer || c is MeshFilter || c is LODGroup) continue;
                Object.DestroyImmediate(c);
            }
            // drop/pickable prefabs often keep alternate looks (picked/unpicked) as inactive children: leave as authored
            return go;
        }

        /// <summary>Parent a visual to a bone, keeping a given world-space size regardless of the bone's scale.</summary>
        public static void Attach(GameObject visual, Transform parent, Vector3 localPos, Quaternion localRot, float worldScale)
        {
            visual.transform.SetParent(parent, false);
            visual.transform.localPosition = localPos;
            visual.transform.localRotation = localRot;
            SetWorldScale(visual.transform, worldScale);
            visual.SetActive(true);
        }

        /// <summary>
        /// Parent a visual to a bone but place it in world space (bones in imported rigs can be
        /// scaled 40x and point any which way). It then follows the bone as it animates.
        /// </summary>
        public static void AttachWorld(GameObject visual, Transform parent, Vector3 worldPos, Quaternion worldRot, float worldScale)
        {
            visual.transform.SetParent(parent, true);
            visual.transform.SetPositionAndRotation(worldPos, worldRot);
            SetWorldScale(visual.transform, worldScale);
            visual.SetActive(true);
        }

        private static void SetWorldScale(Transform t, float worldScale)
        {
            var s = t.parent ? t.parent.lossyScale : Vector3.one;
            t.localScale = new Vector3(worldScale / Mathf.Max(s.x, 0.0001f), worldScale / Mathf.Max(s.y, 0.0001f), worldScale / Mathf.Max(s.z, 0.0001f));
        }

        public static Transform Find(Transform root, string bone) =>
            root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == bone);
    }
}
