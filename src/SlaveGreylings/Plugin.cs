using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace SlaveGreylings
{
    [BepInPlugin(Id, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Id = "slavegreylings";
        public const string Name = "SlaveGreylings";
        public const string Version = "0.0.2";

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }
    }

    [HarmonyPatch(typeof(Terminal), "InitTerminal")]
    internal static class Commands
    {
        private static void Postfix()
        {
            new Terminal.ConsoleCommand("sg_info", "SlaveGreylings: list creatures near you", args =>
            {
                var player = Player.m_localPlayer;
                if (player == null) { args.Context.AddString("sg_info: no local player"); return; }

                var nearby = new List<Character>();
                Character.GetCharactersInRange(player.transform.position, 30f, nearby);
                var summary = nearby
                    .Where(c => c != player)
                    .GroupBy(c => Utils.GetPrefabName(c.gameObject))
                    .Select(g => $"{g.Key} x{g.Count()} (tamed {g.Count(c => c.IsTamed())})");
                args.Context.AddString($"sg_info v{Plugin.Version}: " + (summary.Any() ? string.Join(", ", summary) : "nothing within 30m"));
            });
        }
    }
}
