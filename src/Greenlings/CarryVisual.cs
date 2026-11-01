using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Greenling looks that depend on synced state, rendered the same on every client:
    /// the item it is carrying (right hand) and the plant sprouting from its head.
    /// </summary>
    public class CarryVisual : MonoBehaviour
    {
        internal const string ZSprout = "gl_sprout"; // "prefab|scale|yaw"

        private Greenling _g;
        private Transform _hand, _head, _headTip;
        private GameObject _carryModel, _sproutModel;
        private string _shownCarry = "", _shownSprout = "";

        private void Awake()
        {
            _g = GetComponent<Greenling>();
            _hand = Models.Find(transform, "r_hand") ?? Models.Find(transform, "l_hand");
            _head = Models.Find(transform, "head");
            _headTip = Models.Find(transform, "head_end") ?? _head;
            InvokeRepeating(nameof(Refresh), 0.5f, 0.25f);
        }

        private void Refresh()
        {
            if (_g.Zdo == null) return;
            RefreshCarry();
            RefreshSprout();
        }

        private void RefreshCarry()
        {
            var item = _g.CarryCount > 0 ? _g.CarryItem : "";
            if (item == _shownCarry) return;
            _shownCarry = item;
            if (_carryModel) Destroy(_carryModel);
            _carryModel = null;
            var prefab = item.Length > 0 ? ObjectDB.instance.GetItemPrefab(item) : null;
            if (!prefab) return;
            _carryModel = Models.Visual(prefab, "GreenlingCarry_" + item);
            Models.Attach(_carryModel, _hand ? _hand : transform, _hand ? Vector3.zero : new Vector3(0f, 0.9f, 0.35f), Quaternion.identity, 0.75f);
        }

        private void RefreshSprout()
        {
            if (_g.IsGreenling && _g.IsOwner && _g.Zdo.GetString(ZSprout).Length == 0) PickSprout();

            var want = _g.IsGreenling ? _g.Zdo.GetString(ZSprout) : "";
            // config changes (height/size) rebuild the sprout, so it can be tuned live with gldev_cfg
            want += string.Format(CultureInfo.InvariantCulture, "#{0}#{1}#{2}", Plugin.SproutHeight.Value, Plugin.SproutSize.Value, Growth());
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
            // head_end points up out of the skull; plant "up" follows it, spun randomly around it
            // Greylings are hunched: head_end is the tip of the face, the crown is above the middle of the skull.
            var mid = Vector3.Lerp(_head.position, _headTip.position, 0.45f);
            var crown = new Vector3(mid.x, _headTip.position.y + Plugin.SproutHeight.Value, mid.z);
            // upright in the world, spun randomly; follows the head from then on
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

        /// <summary>Owner rolls a sprout once; it is stored in the ZDO so it never changes.</summary>
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
            return 0.06f; // bushes and shrubs
        }
    }
}
