using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings.DevTools
{
    /// <summary>
    /// gldev_cam: point the game camera at an object (for close-up screenshots of visuals).
    /// Overrides the camera after vanilla positions it each frame; "gldev_cam off" releases it.
    /// </summary>
    [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
    internal static class DevCamera
    {
        private static Transform _target;
        private static float _distance = 1.6f;
        private static float _yaw;

        private static void Postfix(GameCamera __instance)
        {
            if (!_target) return;
            var focus = _target.position + Vector3.up * (FocusHeight(_target) - 0.1f);
            var dir = Quaternion.Euler(0f, _target.eulerAngles.y + _yaw, 0f) * Vector3.forward;
            var pos = focus + dir * _distance + Vector3.up * 0.35f;
            __instance.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(focus - pos));
        }

        /// <summary>Look at the head if the target has one, else its middle.</summary>
        private static float FocusHeight(Transform t)
        {
            var head = t.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == "head_end" || x.name == "head");
            return head ? head.position.y - t.position.y : 0.8f;
        }

        private static Renderer[] _hidden = new Renderer[0];

        /// <summary>Hide the player's visible renderers while framing, and bring back exactly those afterwards.</summary>
        private static void ShowPlayer(Player p, bool show)
        {
            foreach (var r in _hidden) if (r) r.enabled = true;
            _hidden = new Renderer[0];
            if (show || !p) return;
            _hidden = p.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled).ToArray();
            foreach (var r in _hidden) r.enabled = false;
        }

        [HarmonyPatch(typeof(Terminal), "InitTerminal")]
        private static class Command
        {
            private static void Postfix()
            {
                new Terminal.ConsoleCommand("gldev_cam", "<prefab|off> [distance=1.6] [yaw=0] frame the nearest object of that prefab (front = yaw 0)", args =>
                {
                    var p = Player.m_localPlayer;
                    if (args.Length < 2 || args[1] == "off") { _target = null; ShowPlayer(p, true); args.Context.AddString("camera released"); return; }
                    var go = Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None)
                        .Where(v => v.IsValid() && Utils.GetPrefabName(v.gameObject) == args[1])
                        .OrderBy(v => Vector3.Distance(v.transform.position, p.transform.position))
                        .Select(v => v.transform).FirstOrDefault();
                    if (!go) { args.Context.AddString("no " + args[1]); return; }
                    _target = go;
                    _distance = args.TryParameterFloat(2, 1.6f);
                    _yaw = args.TryParameterFloat(3, 0f);
                    ShowPlayer(p, false); // the player tends to stand between the camera and a following creature
                    args.Context.AddString($"camera on {args[1]}");
                });
            }
        }
    }
}
