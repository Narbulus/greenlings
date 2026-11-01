using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
        internal static DevToolsPlugin Instance;
        internal static readonly RecentLog Recent = new RecentLog();
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
            Instance = this;
            AutoStart = Config.Bind("AutoStart", "Enabled", true, "Skip the menus and load straight into the dev world.");
            CharacterName = Config.Bind("AutoStart", "Character", "GLDev", "Dev character (created if missing).");
            WorldName = Config.Bind("AutoStart", "World", "GLDev", "Dev world (created locally if missing).");
            WorldSeed = Config.Bind("AutoStart", "Seed", "GLDevSeed", "Seed used when the dev world is created.");

            _dir = Path.Combine(Paths.BepInExRootPath, "devtools");
            Directory.CreateDirectory(_dir);
            _cmdFile = Path.Combine(_dir, "cmd.txt");
            if (File.Exists(_cmdFile)) File.Delete(_cmdFile);

            BepInEx.Logging.Logger.Listeners.Add(Recent);
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

        /// <summary>Folder with in-game scripts (deployed from the repo's playtest/ folder).</summary>
        internal static string ScriptDir => Path.Combine(Path.GetDirectoryName(typeof(DevToolsPlugin).Assembly.Location), "scripts");

        /// <summary>Run a script file from <see cref="ScriptDir"/> (same syntax as dev.ps1 tests, !include supported).</summary>
        internal bool RunScript(string name)
        {
            var path = Path.Combine(ScriptDir, name.EndsWith(".txt") ? name : name + ".txt");
            if (!File.Exists(path)) return false;
            StartCoroutine(RunLines(Expand(path).ToArray()));
            return true;
        }

        private static IEnumerable<string> Expand(string path)
        {
            foreach (var line in File.ReadAllLines(path))
            {
                var t = line.Trim();
                if (t.StartsWith("!include ")) foreach (var l in Expand(Path.Combine(Path.GetDirectoryName(path), t.Substring(9).Trim()))) yield return l;
                else yield return line;
            }
        }

        private IEnumerator RunLines(string[] lines)
        {
            var failed = false;
            var checkpoint = Recent.Seq; // each !waitfor matches events after the previous match
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                if (failed) { Log.LogMessage("[GLDEV] skipped: " + line); continue; }
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
                        case "waitfor":
                        {
                            // !waitfor <regex> [timeoutSeconds]: an ordered expectation, matched against
                            // log lines since the previous successful !waitfor (or the start of the batch)
                            var lastSpace = arg.LastIndexOf(' ');
                            var timeout = 30f;
                            var pattern = arg;
                            if (lastSpace > 0 && float.TryParse(arg.Substring(lastSpace + 1), out var t)) { timeout = t; pattern = arg.Substring(0, lastSpace); }
                            var re = new Regex(pattern);
                            var deadline = Time.time + timeout;
                            (long seq, string text)? hit = null;
                            while (hit == null && Time.time < deadline)
                            {
                                hit = Recent.FindSince(checkpoint, re);
                                if (hit == null) yield return new WaitForSeconds(0.1f);
                            }
                            if (hit != null) checkpoint = hit.Value.seq;
                            if (hit != null) Marker($"PASS waitfor /{pattern}/ after {timeout - (deadline - Time.time):0.0}s");
                            else { Log.LogError($"[GLDEV] FAIL waitfor /{pattern}/ timed out after {timeout}s"); failed = true; }
                            break;
                        }
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
                    try { Console.instance.TryRunCommand(line, silentFail: false, skipAllowedCheck: true); }
                    catch (Exception e)
                    {
                        Log.LogError($"[GLDEV] FAIL command '{line}' threw: {e.GetBaseException()}");
                        failed = true;
                    }
                }
                else
                {
                    Log.LogWarning("[GLDEV] console not ready, dropped: " + line);
                }
            }
            Marker(failed ? "CMD DONE (FAILED)" : "CMD DONE");
        }
    }

    /// <summary>Keeps the last few thousand log lines so !waitfor can match on them.</summary>
    internal class RecentLog : ILogListener
    {
        private readonly Queue<(long seq, string text)> _lines = new Queue<(long, string)>();
        public long Seq { get; private set; }

        public void LogEvent(object sender, LogEventArgs e)
        {
            lock (_lines)
            {
                _lines.Enqueue((++Seq, e.ToString()));
                while (_lines.Count > 4000) _lines.Dequeue();
            }
        }

        public (long seq, string text)? FindSince(long seq, Regex re)
        {
            lock (_lines)
                foreach (var (s, text) in _lines)
                    if (s > seq && re.IsMatch(text) && !text.Contains("[GLDEV]")) return (s, text);
            return null;
        }

        public void Dispose() { }
    }

    [HarmonyPatch(typeof(Terminal), nameof(Terminal.AddString), typeof(string))]
    internal static class MirrorConsoleOutput
    {
        private static bool _busy;

        // Some mods (e.g. "Display BepInEx Info In-Game") echo log lines into the console: mirroring
        // those back into the log would loop forever, so skip anything that already is a log line.
        private static void Postfix(string text)
        {
            if (_busy || text == null || text.Contains("[console]") || text.StartsWith("[Info") || text.StartsWith("[Message")) return;
            _busy = true;
            try { DevToolsPlugin.Log.LogInfo("[console] " + text); }
            finally { _busy = false; }
        }
    }
}
