using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Greenling looks that depend on synced state, rendered the same on every client:
    /// the tool it holds (right hand), the item it is carrying (right hand, or left if it holds a
    /// tool) and the plant sprouting from its head.
    /// </summary>
    public class CarryVisual : MonoBehaviour
    {
        internal const string ZSprout = "gl_sprout"; // "prefab|scale|yaw"

        private Greenling _g;
        private Transform _hand, _leftHand, _head, _headTip;
        private GameObject _carryModel, _sproutModel, _toolModel;
        private string _shownCarry = "", _shownSprout = "", _shownTool = "";

        private void Awake()
        {
            _g = GetComponent<Greenling>();
            _hand = Models.Find(transform, "r_hand") ?? Models.Find(transform, "l_hand");
            _leftHand = Models.Find(transform, "l_hand") ?? _hand;
            _head = Models.Find(transform, "head");
            _headTip = Models.Find(transform, "head_end") ?? _head;
            InvokeRepeating(nameof(Refresh), 0.5f, 0.25f);
        }

        private void Refresh()
        {
            if (_g.Zdo == null) return;
            RefreshTool();
            RefreshCarry();
            RefreshSprout();
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

        private void RefreshSprout()
        {

            // plants that are no longer on the list (e.g. the glowing mushrooms) get re-rolled
            if (_g.IsGreenling && _g.IsOwner && !Allowed(_g.Zdo.GetString(ZSprout))) PickSprout();

            var want = _g.IsGreenling ? _g.Zdo.GetString(ZSprout) : "";
            // config changes (height/size) and model swaps rebuild the sprout, so it can be tuned live with gldev_cfg
            var skin = HeadSkin();
            want += string.Format(CultureInfo.InvariantCulture, "#{0}#{1}#{2}#{3}", Plugin.SproutHeight.Value, Plugin.SproutSize.Value, Growth(),
                                  skin ? skin.sharedMesh.GetInstanceID() : 0);
            if (want == _shownSprout) return;
            _shownSprout = want;
            if (_sproutModel) Destroy(_sproutModel);
            _sproutModel = null;
            var stored = want.Substring(0, want.IndexOf('#'));
            if (stored.Length == 0 || stored == "none" || !_head) return;

            var p = stored.Split('|');
            var prefab = ZNetScene.instance.GetPrefab(p[0]);
            if (!prefab) return;
            var growth = Growth();
            if (growth <= 0f) return; // not sprouted yet
            var scale = float.Parse(p[1], CultureInfo.InvariantCulture) * Plugin.SproutSize.Value * growth;
            var yaw = float.Parse(p[2], CultureInfo.InvariantCulture);
            _sproutModel = Models.Visual(prefab, "GreenlingSprout_" + p[0]);
            // Plant it in the hollow on top of the skull: found from the mesh itself (the top of the
            // vertices skinned to the head), and parented to the bone that moves them, so it stays put.
            if (Crown.Find(skin, _head, out var bone, out var local, out var up))
            {
                _sproutModel.transform.SetParent(bone, false);
                _sproutModel.transform.localPosition = local + up * (Plugin.SproutHeight.Value / Mathf.Max(bone.lossyScale.y, 0.0001f));
                _sproutModel.transform.localRotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, yaw, 0f);
                var s = bone.lossyScale;
                _sproutModel.transform.localScale = new Vector3(scale / s.x, scale / s.y, scale / s.z);
                _sproutModel.SetActive(true);
                LogModel("sprout_model", _sproutModel, bone);
                return;
            }
            // no skinned head to read (shouldn't happen): fall back to the old estimate
            var mid = Vector3.Lerp(_head.position, _headTip.position, 0.45f);
            var crown = new Vector3(mid.x, _headTip.position.y + Plugin.SproutHeight.Value, mid.z);
            Models.AttachWorld(_sproutModel, _head, crown, Quaternion.Euler(0f, transform.eulerAngles.y + yaw, 0f), scale);
            LogModel("sprout_model", _sproutModel, _head);
        }

        /// <summary>Where a visual actually ended up (renderers, world bounds vs. the bone) — for tuning without screenshots.</summary>
        private void LogModel(string kind, GameObject model, Transform bone)
        {
            var rs = model.GetComponentsInChildren<Renderer>(true);
            var active = rs.Count(r => r.enabled && r.gameObject.activeInHierarchy);
            var b = rs.Length > 0 ? rs[0].bounds : new Bounds(model.transform.position, Vector3.zero);
            foreach (var r in rs) b.Encapsulate(r.bounds);
            _g.Event(kind, string.Format(CultureInfo.InvariantCulture,
                "name={0} renderers={1}/{2} center={3:0.00},{4:0.00},{5:0.00} size={6:0.00},{7:0.00},{8:0.00} bone={9}@{10:0.00},{11:0.00},{12:0.00} boneUp={13:0.0},{14:0.0},{15:0.0} boneScale={16:0.000}",
                model.name, active, rs.Length, b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z,
                bone.name, bone.position.x, bone.position.y, bone.position.z, bone.up.x, bone.up.y, bone.up.z, bone.lossyScale.x));
        }

        /// <summary>
        /// How grown the sprout is: nothing for a brand-new stray, a seedling that grows with trust,
        /// full size once it is a Greenling. Stepped so the model is not rebuilt constantly.
        /// </summary>
        private float Growth()
        {
            if (!_g.IsStray) return 1f;
            var bond = _g.Bond;
            return bond < 8f ? 0f : Mathf.Round(Mathf.Lerp(0.3f, 0.9f, bond / 100f) * 10f) / 10f;
        }

        private SkinnedMeshRenderer HeadSkin() =>
            GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(r => r.sharedMesh && r.bones.Contains(_head))
                .OrderByDescending(r => r.sharedMesh.vertexCount).FirstOrDefault();

        private static bool Allowed(string stored)
        {
            if (stored.Length == 0) return false;
            var plant = stored.Split('|')[0];
            return plant == "none" || Plugin.SproutPrefabs.Value.Split(',').Any(x => x.Trim() == plant);
        }

        /// <summary>Owner rolls a sprout once; it is stored in the ZDO so it only changes if the plant list does.</summary>
        private void PickSprout()
        {
            var options = Plugin.SproutPrefabs.Value.Split(',');
            var choice = options[Random.Range(0, options.Length)].Trim();
            if (choice.Length == 0 || !ZNetScene.instance.GetPrefab(choice)) choice = "none";
            var scale = SproutScale(choice) * Random.Range(0.8f, 1.2f);
            var value = string.Format(CultureInfo.InvariantCulture, "{0}|{1:0.###}|{2:0}", choice, scale, Random.Range(0f, 360f));
            _g.Zdo.Set(ZSprout, value);
            _g.Event("sprout", "plant=" + choice);
        }

        /// <summary>Plants come in very different sizes; shrink the big ones more.</summary>
        private static float SproutScale(string prefab)
        {
            if (prefab.StartsWith("Pickable_Mushroom")) return 0.35f;
            if (prefab.StartsWith("Pickable_")) return 0.25f;
            if (prefab.Contains("sapling")) return 0.2f;
            if (prefab.StartsWith("Beech_small") || prefab.StartsWith("FirTree_small")) return 0.05f;
            return 0.06f; // bushes and shrubs
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
