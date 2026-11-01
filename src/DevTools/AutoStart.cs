using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace SlaveGreylings.DevTools
{
    /// <summary>
    /// Drives FejdStartup the same way the menu buttons do: Start Game -> pick/create
    /// character -> pick/create world -> Start (local, closed server).
    /// </summary>
    [HarmonyPatch(typeof(FejdStartup), "Start")]
    internal static class AutoStart
    {
        private static bool _done; // only once per game process; returning to menu stays in menu

        private static void Postfix(FejdStartup __instance)
        {
            if (_done || !DevToolsPlugin.AutoStart.Value) return;
            _done = true;
            __instance.StartCoroutine(Run(__instance));
        }

        private static IEnumerator Run(FejdStartup fs)
        {
            yield return new WaitForSeconds(0.5f);
            DevToolsPlugin.Marker("MENU");

            var charName = DevToolsPlugin.CharacterName.Value;
            fs.OnStartGame();
            yield return null;

            if (!PlayerProfile.HaveProfile(charName.ToLower()))
            {
                DevToolsPlugin.Marker("creating character " + charName);
                fs.OnCharacterNew();
                yield return null;
                fs.m_csNewCharacterName.text = charName;
                fs.OnNewCharacterDone(forceLocal: true);
                yield return null;
            }
            fs.m_profiles = null;
            fs.SetSelectedProfile(charName.ToLower());
            fs.OnCharacterStart();
            yield return null;

            var worldName = DevToolsPlugin.WorldName.Value;
            var world = fs.FindWorld(worldName);
            if (world == null)
            {
                DevToolsPlugin.Marker("creating world " + worldName);
                fs.OnWorldNew();
                fs.m_newWorldName.text = worldName;
                fs.m_newWorldSeed.text = DevToolsPlugin.WorldSeed.Value;
                fs.OnNewWorldDone(forceLocal: true);
                yield return null;
                world = fs.FindWorld(worldName);
            }
            if (world == null)
            {
                DevToolsPlugin.Log.LogError("[SGDEV] AUTOSTART FAILED: no world " + worldName);
                yield break;
            }

            fs.m_world = world;
            fs.m_openServerToggle.isOn = false;
            fs.m_publicServerToggle.isOn = false;
            DevToolsPlugin.Marker("STARTING " + charName + " @ " + worldName);
            fs.OnWorldStart();
        }
    }

    /// <summary>No Valkyrie ride for a freshly created dev character.</summary>
    [HarmonyPatch(typeof(Game), "Start")]
    internal static class SkipValkyrie
    {
        private static void Prefix(Game __instance)
        {
            if (DevToolsPlugin.AutoStart.Value && __instance.m_playerProfile != null)
                __instance.m_playerProfile.m_firstSpawn = false;
        }
    }

    /// <summary>Skip the intro cinematic without touching the player's saved prefs.</summary>
    [HarmonyPatch(typeof(FejdStartup), "TryPlayIntroCinematic")]
    internal static class SkipIntro
    {
        private static bool Prefix(FejdStartup __instance, ref IEnumerator __result)
        {
            if (!DevToolsPlugin.AutoStart.Value) return true;
            __result = FadeIn(__instance);
            return false;
        }

        private static IEnumerator FadeIn(FejdStartup fs)
        {
            fs.m_menuAnimator.SetTrigger("FadeIn");
            yield break;
        }
    }
}
