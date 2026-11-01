using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Greenlings.DevTools
{
    /// <summary>Console commands for poking at game data from dev.ps1.</summary>
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class InspectCommands
    {
        private static void Postfix()
        {
            new Terminal.ConsoleCommand("gldev_inspect", "[prefab] dump components, AI settings and materials", args =>
            {
                if (args.Length < 2) return;
                var prefab = ZNetScene.instance?.GetPrefab(args[1]);
                if (prefab == null) { args.Context.AddString("no prefab " + args[1]); return; }

                var sb = new StringBuilder($"== {prefab.name}\ncomponents: ");
                sb.AppendLine(string.Join(", ", prefab.GetComponents<Component>().Select(c => c.GetType().Name)));
                var ch = prefab.GetComponent<Character>();
                if (ch) sb.AppendLine($"character: name={ch.m_name} faction={ch.m_faction} group='{ch.m_group}' hp={ch.m_health}");
                var ai = prefab.GetComponent<MonsterAI>();
                if (ai) sb.AppendLine($"monsterAI: agent={ai.m_pathAgentType} consume=[{string.Join(",", (ai.m_consumeItems ?? new System.Collections.Generic.List<ItemDrop>()).Select(i => i ? i.name : "null"))}] " +
                                      $"view={ai.m_viewRange} flee<{ai.m_fleeIfLowHealth} randomMove={ai.m_randomMoveRange}");
                var hum = prefab.GetComponent<Humanoid>();
                if (hum) sb.AppendLine("defaultItems: " + string.Join(", ", (hum.m_defaultItems ?? new GameObject[0]).Select(g => g ? g.name : "null")) +
                                       " | randomWeapon: " + string.Join(", ", (hum.m_randomWeapon ?? new GameObject[0]).Select(g => g ? g.name : "null")));
                if (prefab.GetComponent<VisEquipment>() is VisEquipment ve)
                    sb.AppendLine($"visEquipment: rightHand={(ve.m_rightHand ? ve.m_rightHand.name : "null")} leftHand={(ve.m_leftHand ? ve.m_leftHand.name : "null")}");
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach (var m in r.sharedMaterials.Where(m => m))
                    {
                        var s = m.shader;
                        var props = Enumerable.Range(0, s.GetPropertyCount())
                            .Where(i => s.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
                            .Select(i => $"{s.GetPropertyName(i)}={m.GetColor(s.GetPropertyName(i))}");
                        sb.AppendLine($"renderer {r.name} ({r.GetType().Name}) mat={m.name} shader={s.name} colors: {string.Join(" ", props)}");
                    }
                foreach (var line in sb.ToString().Split('\n')) DevToolsPlugin.Log.LogInfo("[inspect] " + line);
                args.Context.AddString("inspected " + prefab.name + " (see log)");
            });
        }
    }
}
