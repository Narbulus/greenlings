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
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<string> TameItem;
        internal static ConfigEntry<float> TamingTime;
        internal static ConfigEntry<float> FedDuration;
        internal static ConfigEntry<Color> TamedTint;

        internal static ConfigEntry<float> LearnThreshold;
        internal static ConfigEntry<float> CoinCuriosity;
        internal static ConfigEntry<float> MoodMin;
        internal static ConfigEntry<float> MoodMax;
        internal static ConfigEntry<float> LearnCost;
        internal static ConfigEntry<float> LoseInterestCost;
        internal static ConfigEntry<float> AttentionSpan;
        internal static ConfigEntry<float> WatchFollowDistance;
        internal static ConfigEntry<float> DemoWindow;
        internal static ConfigEntry<string> UnderstandingVfx;

        internal static ConfigEntry<int> CarryMax;
        internal static ConfigEntry<float> StationReach;
        internal static ConfigEntry<float> DepositInterval;
        internal static ConfigEntry<float> WaitRecheck;
        internal static ConfigEntry<float> StuckTimeout;

        private void Awake()
        {
            Log = Logger;
            TameItem = Config.Bind("Taming", "Item", "Coins", "Item prefab greylings eat while being tamed, and that raises a greenling's curiosity when given.");
            TamingTime = Config.Bind("Taming", "TamingTime", 600f, "Seconds of being fed and calm needed to tame a greyling (boars: 1800).");
            FedDuration = Config.Bind("Taming", "FedDuration", 60f, "Seconds one eaten item keeps a greyling fed (taming only progresses while fed).");
            TamedTint = Config.Bind("Visuals", "TamedTint", new Color(0.55f, 1f, 0.45f), "Color multiplied onto a tamed greyling's skin.");

            LearnThreshold = Config.Bind("Teaching", "LearnThreshold", 50f, "Curiosity (0-100) needed for a pat to start a lesson.");
            CoinCuriosity = Config.Bind("Teaching", "CoinCuriosity", 25f, "Curiosity gained per coin given.");
            MoodMin = Config.Bind("Teaching", "MoodMin", 10f, "Lowest curiosity a greenling's whims drift toward.");
            MoodMax = Config.Bind("Teaching", "MoodMax", 70f, "Highest curiosity a greenling's whims drift toward.");
            LearnCost = Config.Bind("Teaching", "LearnCost", 20f, "Curiosity spent when a lesson is understood.");
            LoseInterestCost = Config.Bind("Teaching", "LoseInterestCost", 30f, "Curiosity lost when a lesson times out.");
            AttentionSpan = Config.Bind("Teaching", "AttentionSpan", 60f, "Seconds a greenling watches before losing interest (drains 3x when you are far away).");
            WatchFollowDistance = Config.Bind("Teaching", "WatchFollowDistance", 6f, "A watching greenling follows when you are further than this.");
            DemoWindow = Config.Bind("Teaching", "DemoWindow", 90f, "Max seconds between taking an item and putting it somewhere for it to count as a demonstration.");
            UnderstandingVfx = Config.Bind("Teaching", "UnderstandingVfx", "vfx_HealthUpgrade", "Comma-separated effect prefabs for the burst of understanding.");

            CarryMax = Config.Bind("Labor", "CarryMax", 10, "Items carried per trip.");
            StationReach = Config.Bind("Labor", "StationReach", 2.2f, "How close a greenling must get to a station to use it.");
            DepositInterval = Config.Bind("Labor", "DepositInterval", 0.5f, "Seconds between putting single items into a station.");
            WaitRecheck = Config.Bind("Labor", "WaitRecheck", 5f, "Seconds a waiting greenling idles before checking its task again.");
            StuckTimeout = Config.Bind("Labor", "StuckTimeout", 30f, "Seconds without reaching a target before giving up on that trip.");

            new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void Update() => Teaching.Update();
    }
}
