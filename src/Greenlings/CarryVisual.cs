using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Greenling looks that depend on synced state, rendered the same on every client:
    /// the tool it holds (right hand), the item it is carrying (right hand, or left if it holds a
    /// tool). Head decoration is authored in the model asset pipeline.
    /// </summary>
    public class CarryVisual : MonoBehaviour
    {
        private Greenling _g;
        private Transform _hand, _leftHand;
        private GameObject _carryModel, _toolModel;
        private string _shownCarry = "", _shownTool = "";

        private void Awake()
        {
            _g = GetComponent<Greenling>();
            _hand = Models.Find(transform, "r_hand") ?? Models.Find(transform, "l_hand");
            _leftHand = Models.Find(transform, "l_hand") ?? _hand;
            InvokeRepeating(nameof(Refresh), 0.5f, 0.25f);
        }

        private void Refresh()
        {
            if (_g.Zdo == null) return;
            RefreshTool();
            RefreshCarry();
        }

        private void RefreshCarry()
        {
            var item = _g.CarryCount > 0 ? _g.CarryItem : "";
            if (item.Length > 0 && _toolModel) item += "#left"; // the tool has the right hand
            if (item == _shownCarry) return;
            _shownCarry = item;
            if (_carryModel) Destroy(_carryModel);
            _carryModel = null;
            var left = item.EndsWith("#left");
            if (left) item = item.Substring(0, item.Length - 5);
            var prefab = item.Length > 0 ? ObjectDB.instance.GetItemPrefab(item) : null;
            if (!prefab) return;
            _carryModel = Models.Visual(prefab, "GreenlingCarry_" + item);
            var hand = left ? _leftHand : _hand;
            Models.Attach(_carryModel, hand ? hand : transform, hand ? Vector3.zero : new Vector3(0f, 0.9f, 0.35f), Quaternion.identity, 0.75f);
        }

        /// <summary>
        /// The tool in its right hand, held like a player holds it: the item's own "attach" model
        /// (the in-hand version the player uses), gripped in the fist with the handle running across
        /// the knuckles and the head lined up with the forearm, so it reads right on any of the rigs.
        /// </summary>
        private void RefreshTool()
        {
            var tool = _g.IsGreenling ? _g.Tool : "";
            if (tool == _shownTool) return;
            _shownTool = tool;
            if (_toolModel) Destroy(_toolModel);
            _toolModel = null;
            var prefab = tool.Length > 0 ? ObjectDB.instance.GetItemPrefab(tool) : null;
            var attach = prefab ? prefab.transform.Find("attach") : null;
            if (!attach || !_hand) return;
            _toolModel = Models.Visual(attach.gameObject, "GreenlingTool_" + tool);
            if (!Grip.Fit(_toolModel, _hand, transform, _g.Character, out var pos, out var rot, out var scale)) { Destroy(_toolModel); _toolModel = null; return; }
            Models.AttachWorld(_toolModel, _hand, pos, rot, scale);
            _g.Event("tool_model", $"item={tool} scale={scale:0.00}");
        }
    }

    /// <summary>
    /// How to hold a tool in a hand on the greyling-family rig (greylings, greydwarfs, brutes and
    /// shamans share it). Worked out from the hand itself rather than hand-tuned numbers:
    ///  - the fist is just past the knuckles (index, middle and pinky bones);
    ///  - the handle runs through the fist across the knuckles, pinky side to index side;
    ///  - the tool's head (its second-longest side) lines up with the forearm, like a hammer at rest.
    /// The tool's own frame comes from its "attach" model: the origin is where a player's hand grips
    /// it, and the handle points from there towards the middle of the model.
    /// </summary>
    internal static class Grip
    {
        public static bool Fit(GameObject tool, Transform hand, Transform root, Character c, out Vector3 pos, out Quaternion rot, out float scale)
        {
            pos = Vector3.zero;
            rot = Quaternion.identity;
            scale = Mathf.Clamp(c.GetHeight() / 1.85f, 0.45f, 1f); // never bigger than in a player's hand
            var side = hand.name.StartsWith("l_") ? "l_" : "r_";
            var index = Models.Find(root, side + "index1");
            var middle = Models.Find(root, side + "middle1");
            var pinky = Models.Find(root, side + "pinky1");
            if (!index || !middle || !pinky) return false;

            var along = (middle.position - hand.position).normalized;     // wrist to knuckles
            var across = Vector3.ProjectOnPlane(index.position - pinky.position, along).normalized; // through the fist
            if (across.sqrMagnitude < 0.01f) return false;
            var handLength = Vector3.Distance(middle.position, hand.position);
            pos = (index.position + middle.position + pinky.position) / 3f + along * handLength * 0.35f;

            // the tool in its own (attach) space: handle direction, and the head's direction across it
            var b = LocalBounds(tool);
            var handle = b.center.sqrMagnitude > 0.0001f ? b.center.normalized : Vector3.forward;
            var ext = b.size;
            var axes = new[] { Vector3.right * ext.x, Vector3.up * ext.y, Vector3.forward * ext.z };
            var head = axes.Select(a => Vector3.ProjectOnPlane(a, handle)).OrderByDescending(a => a.sqrMagnitude).First();
            if (head.sqrMagnitude < 1e-6f) head = Vector3.ProjectOnPlane(Vector3.up, handle);

            rot = Quaternion.LookRotation(across, along) * Quaternion.Inverse(Quaternion.LookRotation(handle, head.normalized));

            // long-handled tools (a cultivator) are held a third of the way up, like a staff, not by the very end
            float lo = float.MaxValue, hi = float.MinValue;
            for (var i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var d = Vector3.Dot(corner, handle);
                lo = Mathf.Min(lo, d);
                hi = Mathf.Max(hi, d);
            }
            if (hi - lo > 1f) pos -= rot * handle * (lo + (hi - lo) * 0.33f) * scale;
            return true;
        }

        /// <summary>Bounds of a visual's meshes in its own local space (ignoring where it sits in the world).</summary>
        private static Bounds LocalBounds(GameObject go)
        {
            var root = go.transform;
            var first = true;
            var bounds = new Bounds();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                var mb = mf.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = m.MultiplyPoint3x4(corner);
                    if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                    else bounds.Encapsulate(p);
                }
            }
            return bounds;
        }
    }
}
