using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class Commands
    {
        private static void Postfix()
        {
            new Terminal.ConsoleCommand("gl_info", "Greenlings: list creatures near you", args =>
            {
                var player = Player.m_localPlayer;
                if (player == null) { args.Context.AddString("gl_info: no local player"); return; }

                var nearby = new List<Character>();
                Character.GetCharactersInRange(player.transform.position, 30f, nearby);
                var lines = nearby.Where(c => c != player).Select(Describe).ToList();
                args.Context.AddString($"gl_info v{Plugin.Version}: " + (lines.Any() ? string.Join(" | ", lines) : "nothing within 30m"));
            });
        }

        private static string Describe(Character c)
        {
            var s = $"{Localization.instance.Localize(c.m_name)}";
            var tame = c.GetComponent<Tameable>();
            if (tame)
            {
                var name = tame.GetText();
                if (name.Length > 0) s += $" '{name}'";
                s += c.IsTamed() ? " tamed" : $" tameness={tame.GetTameness()}% hungry={tame.IsHungry()}";
            }
            var player = Player.m_localPlayer;
            s += $" d={Vector3.Distance(player.transform.position, c.transform.position):0.0}m";
            var ai = c.GetBaseAI() as MonsterAI;
            if (ai)
            {
                if (ai.GetTargetCreature()) s += $" -> attacking {Localization.instance.Localize(ai.GetTargetCreature().m_name)}";
                if (ai.IsAlerted()) s += " alerted";
                if (ai.IsSleeping()) s += " asleep";
                if (ai.DespawnInDay()) s += " despawnInDay";
                if (ai.IsEventCreature()) s += " event";
                if (ai.m_consumeTarget) s += $" eating->{ai.m_consumeTarget.name}@{Vector3.Distance(ai.m_consumeTarget.transform.position, c.transform.position):0.0}m";
            }
            return s;
        }
    }
}
