using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    [BepInPlugin(Id, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Id = "greenlings";
        public const string Name = "Greenlings";
        public const string Version = "0.1.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<string> TameItem;
        internal static ConfigEntry<float> TamingTime;
        internal static ConfigEntry<float> FedDuration;
        internal static ConfigEntry<Color> TamedTint;

        private void Awake()
        {
            Log = Logger;
            TameItem = Config.Bind("Taming", "Item", "Coins", "Item prefab greylings eat while being tamed. Drop it on the ground near them.");
            TamingTime = Config.Bind("Taming", "TamingTime", 600f, "Seconds of being fed and calm needed to tame a greyling (boars: 1800).");
            FedDuration = Config.Bind("Taming", "FedDuration", 60f, "Seconds one eaten item keeps a greyling fed (taming only progresses while fed).");
            TamedTint = Config.Bind("Visuals", "TamedTint", new Color(0.55f, 1f, 0.45f), "Color multiplied onto a tamed greyling's skin.");

            new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }
    }
}
