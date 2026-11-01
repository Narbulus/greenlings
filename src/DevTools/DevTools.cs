using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Greenlings.DevTools
{
    /// <summary>
    /// Dev-only harness plugin. Never shipped with the mod.
    ///  - Boots straight from the main menu into a dedicated dev character + world.
    ///  - Watches BepInEx/devtools/cmd.txt and runs each line (console commands or ! directives).
    ///  - Mirrors in-game console output into the BepInEx log.
    /// Emits "[GLDEV] ..." log markers that scripts/dev.ps1 waits on.
    /// </summary>
    [BepInPlugin(Id, "Greenlings DevTools", "0.1.0")]
    public class DevToolsPlugin : BaseUnityPlugin
    {
        public const string Id = "greenlings.devtools";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> AutoStart;
        internal static ConfigEntry<string> CharacterName;
        internal static ConfigEntry<string> WorldName;
        internal static ConfigEntry<string> WorldSeed;

        private string _dir;
        private string _cmdFile;
        private bool _announcedInGame;

        private void Awake()
        {
            Log = Logger;
            AutoStart = Config.Bind("AutoStart", "Enabled", true, "Skip the menus and load straight into the dev world.");
            CharacterName = Config.Bind("AutoStart", "Character", "GLDev", "Dev character (created if missing).");
            WorldName = Config.Bind("AutoStart", "World", "GLDev", "Dev world (created locally if missing).");
            WorldSeed = Config.Bind("AutoStart", "Seed", "GLDevSeed", "Seed used when the dev world is created.");

            _dir = Path.Combine(Paths.BepInExRootPath, "devtools");
            Directory.CreateDirectory(_dir);
            _cmdFile = Path.Combine(_dir, "cmd.txt");
            if (File.Exists(_cmdFile)) File.Delete(_cmdFile);

            new Harmony(Id).PatchAll(typeof(DevToolsPlugin).Assembly);
            Marker("LOADED");
            InvokeRepeating(nameof(PollCommands), 1f, 0.25f);
        }

        internal static void Marker(string what) => Log.LogMessage("[GLDEV] " + what);

        private void Update()
        {
            var p = Player.m_localPlayer;
            if (!_announcedInGame && p != null && !p.IsTeleporting() && p.IsOnGround()
                && Game.instance != null && !Game.instance.InIntro(includeQueued: true))
            {
                _announcedInGame = true;
                Terminal.m_cheat = true; // local server, so devcommands-gated commands now work
                Marker("INGAME " + Player.m_localPlayer.transform.position);
            }
            else if (_announcedInGame && Player.m_localPlayer == null)
            {
                _announcedInGame = false;
            }
        }

        private void PollCommands()
        {
            if (!File.Exists(_cmdFile)) return;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(_cmdFile);
                File.Delete(_cmdFile);
            }
            catch (IOException) { return; } // writer still has it open; try next tick

            StartCoroutine(RunLines(lines));
        }

        private IEnumerator RunLines(string[] lines)
        {
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                Log.LogMessage("[GLDEV] > " + line);
                if (line.StartsWith("!"))
                {
                    var parts = line.Substring(1).Split(new[] { ' ' }, 2);
                    var arg = parts.Length > 1 ? parts[1].Trim() : "";
                    switch (parts[0].ToLowerInvariant())
                    {
                        case "wait":
                            yield return new WaitForSeconds(float.TryParse(arg, out var s) ? s : 1f);
                            break;
                        case "screenshot":
                            var name = arg.Length > 0 ? arg : DateTime.Now.ToString("HHmmss");
                            var path = Path.Combine(_dir, name + ".png");
                            yield return new WaitForEndOfFrame();
                            ScreenCapture.CaptureScreenshot(path);
                            yield return null;
                            yield return null;
                            Marker("SCREENSHOT " + path);
                            break;
                        case "pos":
                            Marker(Player.m_localPlayer ? "POS " + Player.m_localPlayer.transform.position : "POS none");
                            break;
                        case "quit":
                            Marker("QUIT");
                            Application.Quit();
                            break;
                        default:
                            Log.LogWarning("[GLDEV] unknown directive " + parts[0]);
                            break;
                    }
                }
                else if (Console.instance != null)
                {
                    Console.instance.TryRunCommand(line, silentFail: false, skipAllowedCheck: true);
                }
                else
                {
                    Log.LogWarning("[GLDEV] console not ready, dropped: " + line);
                }
            }
            Marker("CMD DONE");
        }
    }

    [HarmonyPatch(typeof(Terminal), nameof(Terminal.AddString), typeof(string))]
    internal static class MirrorConsoleOutput
    {
        private static void Postfix(string text) => DevToolsPlugin.Log.LogInfo("[console] " + text);
    }
}
