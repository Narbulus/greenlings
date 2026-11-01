using UnityEngine;

namespace Greenlings
{
    internal static class Effects
    {
        /// <summary>The "burst of understanding". Prefab names come from config so they can be tuned.</summary>
        public static void Understanding(Vector3 at)
        {
            foreach (var name in Plugin.UnderstandingVfx.Value.Split(','))
            {
                var prefab = ZNetScene.instance.GetPrefab(name.Trim());
                if (prefab) Object.Instantiate(prefab, at, Quaternion.identity);
                else Plugin.Log.LogWarning("missing vfx prefab " + name);
            }
        }
    }
}
