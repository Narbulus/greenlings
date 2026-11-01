using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Lives on every greyling instance. Once the greyling is tamed (state comes from the
    /// ZDO, so all clients agree) it turns green and is called a Greenling.
    /// </summary>
    public class Greenling : MonoBehaviour
    {
        public const string DisplayName = "Greenling";
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Character _character;
        private string _wildName;
        private bool _applied;

        public static bool Is(Character c) => c && c.IsTamed() && c.GetComponent<Greenling>();

        private void Awake()
        {
            _character = GetComponent<Character>();
            _wildName = _character.m_name;
            InvokeRepeating(nameof(Refresh), 0f, 1f);
        }

        private void Refresh()
        {
            var tamed = _character.IsTamed();
            if (tamed == _applied) return;
            _applied = tamed;

            _character.m_name = tamed ? DisplayName : _wildName;
            var block = new MaterialPropertyBlock();
            foreach (var r in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.sharedMaterial == null || r.sharedMaterial.shader.name != "Custom/Creature") continue; // skip eyes
                r.GetPropertyBlock(block);
                if (tamed) block.SetColor(ColorId, Plugin.TamedTint.Value);
                else block.Clear();
                r.SetPropertyBlock(block);
            }
        }
    }
}
