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
        public const string Version = "0.5.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<string> TameItem;
        internal static ConfigEntry<float> TamingTime;
        internal static ConfigEntry<float> FedDuration;
        internal static ConfigEntry<float> CoinSearchRange;
        internal static ConfigEntry<Color> TamedTint;

        internal static ConfigEntry<float> StartBond;
        internal static ConfigEntry<float> BondNearPerMinute;
        internal static ConfigEntry<float> BondNearRange;
        internal static ConfigEntry<float> PatBond;
        internal static ConfigEntry<float> PatCooldown;
        internal static ConfigEntry<float> FoodBond;
        internal static ConfigEntry<float> FoodCuriosity;
        internal static ConfigEntry<float> FeedCooldown;
        internal static ConfigEntry<string> Foods;
        internal static ConfigEntry<float> LearnThreshold;
        internal static ConfigEntry<float> CoinCuriosity;
        internal static ConfigEntry<float> MoodMin;
        internal static ConfigEntry<float> MoodMax;
        internal static ConfigEntry<float> LearnCost;
        internal static ConfigEntry<float> LoseInterestCost;
        internal static ConfigEntry<float> AttentionSpan;
        internal static ConfigEntry<float> WatchFollowDistance;
        internal static ConfigEntry<float> DemoWindow;
        internal static ConfigEntry<string> UnderstandingSfx;
        internal static ConfigEntry<string> SproutPrefabs;
        internal static ConfigEntry<float> SproutHeight;
        internal static ConfigEntry<float> SproutSize;
        internal static ConfigEntry<float> ActionTime;

        internal static ConfigEntry<int> CarryMax;
        internal static ConfigEntry<float> StationReach;
        internal static ConfigEntry<float> WaitRecheck;
        internal static ConfigEntry<float> StuckTimeout;
        internal static ConfigEntry<float> MischiefChance;
        internal static ConfigEntry<string> GiftPickables;
        internal static ConfigEntry<float> MimicChance;
        internal static ConfigEntry<float> AudienceChance;

        private void Awake()
        {
            Log = Logger;
            TameItem = Config.Bind("Taming", "Item", "Coins", "Item prefab greylings eat while being tamed, and that raises a greenling's curiosity when given.");
            TamingTime = Config.Bind("Taming", "TamingTime", 600f, "Seconds of being fed and calm needed to tame a greyling (boars: 1800).");
            FedDuration = Config.Bind("Taming", "FedDuration", 60f, "Seconds one eaten item keeps a greyling fed (taming only progresses while fed).");
            CoinSearchRange = Config.Bind("Taming", "SearchRange", 10f, "How far greylings notice the taming item on the ground (boars notice food at 5m).");
            SproutPrefabs = Config.Bind("Visuals", "Sprouts",
                "Pickable_Dandelion,Pickable_Thistle,Pickable_Mushroom,Pickable_Mushroom_yellow,Pickable_Mushroom_blue,Pickable_Flax_Wild,Pickable_Barley_Wild,shrub_2,Bush02_en,VineGreen_sapling",
                "Plants that can grow on a greenling's head (prefab names; one is picked at random per greenling, empty = none).");
            SproutHeight = Config.Bind("Visuals", "SproutHeight", 0.24f, "How far above the face the sprout sits (meters).");
            SproutSize = Config.Bind("Visuals", "SproutSize", 2.0f, "Size multiplier for head sprouts.");
            TamedTint = Config.Bind("Visuals", "TamedTint", new Color(0.55f, 1f, 0.45f), "Color multiplied onto a tamed greyling's skin.");

            StartBond = Config.Bind("Bonding", "StartBond", 0f, "Trust (0-100) a greyling has right after taming. At 100 it can be taught.");
            BondNearPerMinute = Config.Bind("Bonding", "NearbyPerMinute", 1.5f, "Trust gained per minute while a player is nearby.");
            BondNearRange = Config.Bind("Bonding", "NearbyRange", 15f, "How close a player must be for 'nearby' trust (meters).");
            PatBond = Config.Bind("Bonding", "PatBond", 4f, "Trust gained per pat (strays).");
            PatCooldown = Config.Bind("Bonding", "PatCooldown", 45f, "Seconds before another pat counts.");
            FoodBond = Config.Bind("Bonding", "FoodBond", 10f, "Trust gained per food item (strays).");
            FoodCuriosity = Config.Bind("Bonding", "FoodCuriosity", 10f, "Curiosity gained per food item once bonded.");
            FeedCooldown = Config.Bind("Bonding", "FeedCooldown", 60f, "Seconds a greenling stays full after eating.");
            Foods = Config.Bind("Bonding", "Foods", "Raspberry,Blueberries,Cloudberry,Mushroom,MushroomYellow,Honey,Carrot,Turnip,Onion,CookedMeat,QueensJam",
                "Items greenlings accept as food (use the item on them). Coins only buy interest.");
            LearnThreshold = Config.Bind("Teaching", "LearnThreshold", 50f, "Curiosity (0-100) needed for a pat to start a lesson.");
            CoinCuriosity = Config.Bind("Teaching", "CoinCuriosity", 25f, "Curiosity gained per coin given.");
            MoodMin = Config.Bind("Teaching", "MoodMin", 10f, "Lowest curiosity a greenling's whims drift toward.");
            MoodMax = Config.Bind("Teaching", "MoodMax", 70f, "Highest curiosity a greenling's whims drift toward.");
            LearnCost = Config.Bind("Teaching", "LearnCost", 20f, "Curiosity spent when a lesson is understood.");
            LoseInterestCost = Config.Bind("Teaching", "LoseInterestCost", 30f, "Curiosity lost when a lesson times out.");
            AttentionSpan = Config.Bind("Teaching", "AttentionSpan", 60f, "Seconds a greenling watches before losing interest (drains 3x when you are far away).");
            WatchFollowDistance = Config.Bind("Teaching", "WatchFollowDistance", 6f, "A watching greenling follows when you are further than this.");
            DemoWindow = Config.Bind("Teaching", "DemoWindow", 90f, "Max seconds between taking an item and putting it somewhere for it to count as a demonstration.");
            UnderstandingSfx = Config.Bind("Teaching", "UnderstandingSfx", "sfx_secretfound", "Sound played with the little burst of understanding above a greenling's head (empty = none).");

            CarryMax = Config.Bind("Labor", "CarryPerTrip", 1, "Items carried per trip. Greenlings are small: one at a time.");
            ActionTime = Config.Bind("Labor", "ActionTime", 0.7f, "Seconds a greenling spends reaching in / tossing an item (plays an animation).");
            StationReach = Config.Bind("Labor", "StationReach", 2.2f, "How close a greenling must get to a station to use it.");
            WaitRecheck = Config.Bind("Labor", "WaitRecheck", 5f, "Seconds a waiting greenling idles before checking its task again.");
            StuckTimeout = Config.Bind("Labor", "StuckTimeout", 30f, "Seconds without reaching a target before giving up on that trip.");

            MischiefChance = Config.Bind("Mischief", "Chance", 0.015f, "Chance every 2s that an idle greenling starts a prank or a cute moment (x3 when sulky, x2 for strays). 0 disables.");
            MimicChance = Config.Bind("Mischief", "MimicChance", 0.35f, "Chance an idle greenling nearby copies what you just did (copycats: much more).");
            AudienceChance = Config.Bind("Mischief", "AudienceChance", 0.4f, "Chance an idle greenling stops to watch (or joins) another's antics.");
            GiftPickables = Config.Bind("Mischief", "GiftPickables", "Pickable_Stone,Pickable_Flint,Pickable_Branch,Pickable_Mushroom,Pickable_Mushroom_yellow,Pickable_Dandelion,Pickable_Thistle,RaspberryBush,BlueberryBush",
                "Wild things a greenling may pluck to bring you as a present.");

            new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void Update() => Teaching.Update();
    }
}
