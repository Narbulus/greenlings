using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Shows what a greenling is carrying in its hand. Purely visual and driven by the ZDO, so
    /// every client renders the same thing.
    /// </summary>
    public class CarryVisual : MonoBehaviour
    {
        private Greenling _g;
        private Transform _hand;
        private GameObject _model;
        private string _shown = "";
        private static bool _loggedBone;

        private void Awake()
        {
            _g = GetComponent<Greenling>();
            _hand = FindHand(transform);
            if (!_loggedBone)
            {
                _loggedBone = true;
                Plugin.Log.LogInfo($"[GLEVT] carry_bone name={(_hand ? _hand.name : "none (using body)")}");
            }
            InvokeRepeating(nameof(Refresh), 0.5f, 0.25f);
        }

        private static Transform FindHand(Transform root)
        {
            var all = root.GetComponentsInChildren<Transform>(true);
            bool IsRight(string n) => n.StartsWith("r_") || n.Contains(".r") || n.Contains("right") || n.EndsWith("_r");
            return all.FirstOrDefault(t => t.name.ToLower().Contains("hand") && IsRight(t.name.ToLower()))
                ?? all.FirstOrDefault(t => t.name.ToLower().Contains("hand"));
        }

        private void Refresh()
        {
            var item = _g.CarryCount > 0 ? _g.CarryItem : "";
            if (item == _shown) return;
            _shown = item;
            if (_model) Destroy(_model);
            _model = item.Length > 0 ? CreateModel(item) : null;
        }

        private GameObject CreateModel(string item)
        {
            var prefab = ObjectDB.instance.GetItemPrefab(item);
            if (!prefab) return null;

            ZNetView.m_forceDisableInit = true;
            GameObject go;
            try { go = Instantiate(prefab); }
            finally { ZNetView.m_forceDisableInit = false; }

            // keep only the looks
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(c);
            go.name = "GreenlingCarry_" + item;

            var parent = _hand ? _hand : transform;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = _hand ? Vector3.zero : new Vector3(0f, 0.9f, 0.35f);
            go.transform.localRotation = Quaternion.identity;
            // undo the bone's scale so items keep their world size (slightly shrunk to fit small hands)
            var s = parent.lossyScale;
            go.transform.localScale = new Vector3(0.75f / Mathf.Max(s.x, 0.01f), 0.75f / Mathf.Max(s.y, 0.01f), 0.75f / Mathf.Max(s.z, 0.01f));
            return go;
        }
    }
}
