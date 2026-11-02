using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Where the top of a greyling's skull is, read from its skinned mesh: the highest vertices
    /// skinned to the head (in the bind pose, so it doesn't matter what animation is playing),
    /// averaged so a hollow stump gives the middle of the hollow. Returned in the head bone's
    /// local space, so anything parented there stays glued to the skull.
    /// </summary>
    internal static class Crown
    {
        public static bool Find(SkinnedMeshRenderer smr, Transform head, out Transform bone, out Vector3 local, out Vector3 up)
        {
            bone = head;
            local = Vector3.zero;
            up = Vector3.up;
            if (!smr || !head) return false;
            var mesh = smr.sharedMesh;
            if (!mesh || !mesh.isReadable) return false;

            var bones = smr.bones;
            var headIdx = System.Array.IndexOf(bones, head);
            var bind = mesh.bindposes;
            var weights = mesh.boneWeights;
            var verts = mesh.vertices;
            if (headIdx < 0 || headIdx >= bind.Length || weights.Length != verts.Length) return false;

            var skull = new HashSet<int>();
            for (var i = 0; i < bones.Length; i++)
                if (bones[i] && (bones[i] == head || bones[i].IsChildOf(head))) skull.Add(i);

            // the character's up, in the head bone's (bind-pose) space
            var root = smr.GetComponentInParent<Character>() ? smr.GetComponentInParent<Character>().transform : smr.transform.root;
            var upMesh = smr.transform.InverseTransformDirection(root.up);
            up = bind[headIdx].MultiplyVector(upMesh).normalized;

            var pts = new List<Vector3>();
            for (var v = 0; v < verts.Length; v++)
            {
                var w = weights[v];
                if (w.weight0 >= 0.6f && skull.Contains(w.boneIndex0)) pts.Add(bind[headIdx].MultiplyPoint3x4(verts[v]));
            }
            if (pts.Count < 10) return false;

            var u = up;
            var heights = pts.Select(p => Vector3.Dot(p, u)).ToList();
            float top = heights.Max(), bottom = heights.Min();
            var band = (top - bottom) * 0.06f;
            var crown = pts.Where((p, i) => heights[i] >= top - band).ToList();
            var center = crown.Aggregate(Vector3.zero, (s, p) => s + p) / crown.Count;
            // sit on the rim height, centered over the hollow
            local = center + up * (top - Vector3.Dot(center, up));
            return true;
        }
    }
}
