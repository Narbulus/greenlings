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
        public const string Version = "0.7.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<string> TameItem;
        internal static ConfigEntry<string> BruteTameItems;
        internal static ConfigEntry<string> ShamanTameItems;
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
        internal static ConfigEntry<float> FeedCooldown;
        internal static ConfigEntry<float> TagAlongTime;
        internal static ConfigEntry<string> Foods;

        internal static ConfigEntry<string> TeachItem;
        internal static ConfigEntry<float> AttentionSpan;
        internal static ConfigEntry<float> DemoAttention;
        internal static ConfigEntry<float> AttentiveAfterLesson;
        internal static ConfigEntry<int> MaxJobSteps;
        internal static ConfigEntry<float> WatchFollowDistance;
        internal static ConfigEntry<float> DemoWindow;
        internal static ConfigEntry<float> NaturalCuriosity;
        internal static ConfigEntry<float> CasualAttention;
        internal static ConfigEntry<string> UnderstandingSfx;

        internal static ConfigEntry<string> SproutPrefabs;
        internal static ConfigEntry<float> SproutHeight;
        internal static ConfigEntry<float> SproutSize;
        internal static ConfigEntry<float> ActionTime;

        internal static ConfigEntry<int> CarryMax;
        internal static ConfigEntry<float> StationReach;
        internal static ConfigEntry<float> WaitRecheck;
        internal static ConfigEntry<float> StuckTimeout;
        internal static ConfigEntry<float> WorkRadius;
        internal static ConfigEntry<float> StationGroupRadius;
        internal static ConfigEntry<int> FeedPile;
        internal static ConfigEntry<float> ChoreChance;
        internal static ConfigEntry<int> ChoreTrips;
        internal static ConfigEntry<float> RepairBelow;

        internal static ConfigEntry<float> MischiefChance;
        internal static ConfigEntry<float> MealInterval;
        internal static ConfigEntry<float> FoodSearchRange;
        internal static ConfigEntry<float> NestRange;
        internal static ConfigEntry<float> NestScale;
        internal static ConfigEntry<int> NestCapacity;
        internal static ConfigEntry<bool> SleepAtNight;
        internal static ConfigEntry<float> RestChance;
        internal static ConfigEntry<int> NestDecorations;
        internal static ConfigEntry<string> NestPlants;
        internal static ConfigEntry<float> UnmetSlowdown;
        internal static ConfigEntry<float> SulkChance;

        internal static ConfigEntry<float> FearRange;
        internal static ConfigEntry<float> ScaredTime;
        internal static ConfigEntry<float> RefugeRange;
        internal static ConfigEntry<bool> OpenDoors;

        internal static ConfigEntry<float> BrutePower;
        internal static ConfigEntry<string> GreydwarfPickables;
        internal static ConfigEntry<string> GiftPickables;
        internal static ConfigEntry<float> MimicChance;
        internal static ConfigEntry<float> AudienceChance;

        internal static ConfigEntry<float> HealRange;
        internal static ConfigEntry<float> HealBelow;
        internal static ConfigEntry<float> HealPercent;
        internal static ConfigEntry<float> HealCooldown;
        internal static ConfigEntry<bool> HealPlayers;
        internal static ConfigEntry<float> SootheChance;
        internal static ConfigEntry<float> SootheBond;
        internal static ConfigEntry<float> SootheCooldown;
        internal static ConfigEntry<float> ShamanCompany;
        internal static ConfigEntry<float> FieldRadius;
        internal static ConfigEntry<int> SeedsPerTrip;
        internal static ConfigEntry<float> CropSpacing;
        internal static ConfigEntry<float> CropJitter;

        private void Awake()
        {
            Log = Logger;
            TameItem = Config.Bind("Taming", "Item", "Coins", "Item greylings and greydwarfs eat while being tamed (like boars eat food).");
            BruteTameItems = Config.Bind("Taming", "BruteItems", "AmberPearl,Amber", "Items brutes eat while being tamed (a coin is beneath them).");
            ShamanTameItems = Config.Bind("Taming", "ShamanItems", "Thistle", "Herbs shamans eat while being tamed.");
            TamingTime = Config.Bind("Taming", "TamingTime", 600f, "Seconds of being fed and calm needed to tame a greyling (boars: 1800).");
            FedDuration = Config.Bind("Taming", "FedDuration", 60f, "Seconds one eaten item keeps a greyling fed (taming only progresses while fed).");
            CoinSearchRange = Config.Bind("Taming", "SearchRange", 10f, "How far greylings notice the taming item on the ground (boars notice food at 5m).");

            SproutPrefabs = Config.Bind("Visuals", "HeadPlants",
                "Pickable_Mushroom,Pickable_Dandelion,Pickable_Thistle,shrub_2,Bush02_en,Beech_small1,FirTree_small",
                "Plants that can grow on a greenling's head: forest things (prefab names; one is picked at random per greenling, empty = none).");
            SproutHeight = Config.Bind("Visuals", "SproutHeight", 0.02f, "How far above the top of the head the plant sits (meters).");
            SproutSize = Config.Bind("Visuals", "SproutSize", 2.0f, "Size multiplier for head plants.");
            TamedTint = Config.Bind("Visuals", "TamedTint", new Color(0.55f, 1f, 0.45f), "Color multiplied onto a tamed greyling's skin.");

            StartBond = Config.Bind("Bonding", "StartBond", 0f, "Trust (0-100) a greyling has right after taming. At 100 it can be taught.");
            BondNearPerMinute = Config.Bind("Bonding", "NearbyPerMinute", 2f, "Trust gained per minute while a player is nearby.");
            BondNearRange = Config.Bind("Bonding", "NearbyRange", 15f, "How close a player must be for 'nearby' trust (meters).");
            PatBond = Config.Bind("Bonding", "PatBond", 5f, "Trust gained per pat (strays).");
            PatCooldown = Config.Bind("Bonding", "PatCooldown", 30f, "Seconds before another pat counts (any greenling).");
            FoodBond = Config.Bind("Bonding", "FoodBond", 13f, "Trust gained per food item (strays), handed over or eaten off the ground.");
            FeedCooldown = Config.Bind("Bonding", "FeedCooldown", 60f, "Seconds a stray stays full after eating.");
            TagAlongTime = Config.Bind("Bonding", "TagAlongTime", 20f, "Seconds a stray follows you after a pat or a meal (handy for leading it home).");
            Foods = Config.Bind("Bonding", "Foods", "Raspberry,Blueberries,Cloudberry,Mushroom,MushroomYellow,Honey,Carrot,Turnip,Onion,CookedMeat,QueensJam",
                "Items greenlings eat: hand them over, or leave them on the ground.");

            TeachItem = Config.Bind("Teaching", "Item", "Coins", "Item to give a trusting greenling (use it on them) to make it watch and learn.");
            AttentionSpan = Config.Bind("Teaching", "AttentionSpan", 90f, "Seconds a greenling watches before losing interest (drains 3x when you are far away).");
            DemoAttention = Config.Bind("Teaching", "DemoAttention", 45f, "Every step you demonstrate tops its attention back up to at least this many seconds.");
            AttentiveAfterLesson = Config.Bind("Teaching", "AttentiveAfterLesson", 60f, "Seconds after a lesson that a greenling still picks up corrections and extra steps (no new coin needed).");
            MaxJobSteps = Config.Bind("Teaching", "MaxJobSteps", 3, "How many steps one job can have (e.g. chest -> cooking station, cooking station -> chest).");
            WatchFollowDistance = Config.Bind("Teaching", "WatchFollowDistance", 6f, "A watching greenling follows when you are further than this.");
            DemoWindow = Config.Bind("Teaching", "DemoWindow", 90f, "Max seconds between taking an item and putting it somewhere for it to count as a demonstration.");
            NaturalCuriosity = Config.Bind("Teaching", "NaturalCuriosity", 0.012f, "Chance every 2s that an idle, content greenling near you comes over to watch what you are doing. It may pick up simple chores that way. 0 disables.");
            CasualAttention = Config.Bind("Teaching", "CasualAttention", 45f, "Seconds a naturally curious greenling watches you.");
            UnderstandingSfx = Config.Bind("Teaching", "UnderstandingSfx", "sfx_secretfound", "Sound played with the little burst of understanding above a greenling's head (empty = none).");

            CarryMax = Config.Bind("Labor", "CarryPerTrip", 1, "Items carried per trip. Greenlings are small: one at a time.");
            ActionTime = Config.Bind("Labor", "ActionTime", 0.7f, "Seconds a greenling spends reaching in / tossing an item (plays an animation).");
            StationReach = Config.Bind("Labor", "StationReach", 2.2f, "How close a greenling must get to a station to use it.");
            WaitRecheck = Config.Bind("Labor", "WaitRecheck", 5f, "Seconds a waiting greenling idles before checking its job again.");
            StuckTimeout = Config.Bind("Labor", "StuckTimeout", 30f, "Seconds without reaching a target before giving up on that trip.");
            WorkRadius = Config.Bind("Labor", "WorkRadius", 20f, "How far a job generalizes around where it was taught: every beehive, torch, berry bush or loose item of that kind.");
            StationGroupRadius = Config.Bind("Labor", "StationGroupRadius", 8f, "Machines of the same kind this close together are worked as one bank (e.g. a row of kilns).");
            FeedPile = Config.Bind("Labor", "FeedPile", 2, "Put-it-on-the-ground jobs (feeding spots) keep this many items lying there.");
            ChoreChance = Config.Bind("Labor", "ChoreChance", 0.05f, "Chance every 2s that an idle (or stuck) greenling does something useful on its own: tidying, helping, chores it picked up. 0 disables.");
            ChoreTrips = Config.Bind("Labor", "ChoreTrips", 4, "Trips a greenling makes on a chore before wandering off again.");
            RepairBelow = Config.Bind("Labor", "RepairBelow", 0.9f, "Greenlings with a hammer repair pieces below this health fraction.");

            BrutePower = Config.Bind("Roles", "BrutePower", 40f, "Chop/pickaxe damage per brute whack (splitting logs, smashing rocks).");
            GreydwarfPickables = Config.Bind("Roles", "GreydwarfPickables", "Pickable_Stone,Pickable_Flint,Pickable_Branch",
                "Pickables that count as greydwarf gathering rather than greyling foraging.");

            MealInterval = Config.Bind("Needs", "MealInterval", 900f, "Seconds after eating before a greenling is hungry again.");
            FoodSearchRange = Config.Bind("Needs", "FoodSearchRange", 20f, "How far a hungry greenling looks for food lying on the ground.");
            NestRange = Config.Bind("Needs", "NestRange", 30f, "A Greenling Nest within this range counts as home.");
            NestScale = Config.Bind("Needs", "NestScale", 0.38f, "Size of the Greenling Nest relative to the vanilla vulture straw pile (needs a restart).");
            NestCapacity = Config.Bind("Needs", "NestCapacity", 2, "Greenlings that fit in one nest (beds are shared out each time; nobody owns one).");
            SleepAtNight = Config.Bind("Needs", "SleepAtNight", true, "Greenlings with a nest go to sleep in it at night.");
            RestChance = Config.Bind("Needs", "RestChance", 0.01f, "Chance every 2s that an idle greenling with a nest goes for a little rest in it (or decorates it).");
            NestDecorations = Config.Bind("Needs", "NestDecorations", 4, "Plants greenlings may plant around a nest (0 = none).");
            NestPlants = Config.Bind("Needs", "NestPlants", "Pickable_Dandelion,Pickable_Thistle,Pickable_Mushroom", "What they plant around their nests.");
            UnmetSlowdown = Config.Bind("Needs", "UnmetSlowdown", 1.5f, "Work-speed penalty multiplier per unmet need (hungry, no nest).");
            SulkChance = Config.Bind("Needs", "SulkChance", 0.15f, "Chance per trip that a greenling with both needs unmet sulks for a while instead.");

            FearRange = Config.Bind("Danger", "FearRange", 12f, "Hostile monsters this close frighten greylings: they run to you (or a brute) for protection, or just panic.");
            ScaredTime = Config.Bind("Danger", "ScaredTime", 8f, "Seconds a greenling stays spooked after the danger is gone (a pat calms it).");
            RefugeRange = Config.Bind("Danger", "RefugeRange", 30f, "How far a frightened greenling will run to reach you or a brute.");
            OpenDoors = Config.Bind("Danger", "OpenDoors", true, "Greenlings open (and close behind them) ordinary doors, not gates.");

            MischiefChance = Config.Bind("Mischief", "Chance", 0.015f, "Chance every 2s that an idle greenling gets up to something (more when needs are unmet, x2 for strays). 0 disables.");
            MimicChance = Config.Bind("Mischief", "MimicChance", 0.2f, "Chance an idle greenling nearby copies what you just did (copycats: much more).");
            AudienceChance = Config.Bind("Mischief", "AudienceChance", 0.4f, "Chance an idle greenling stops to watch (or joins) another's antics.");
            GiftPickables = Config.Bind("Mischief", "GiftPickables", "Pickable_Stone,Pickable_Flint,Pickable_Branch,Pickable_Mushroom,Pickable_Dandelion,Pickable_Thistle,RaspberryBush,BlueberryBush",
                "Wild things a greenling may pluck to bring you as a present.");

            HealRange = Config.Bind("Shamans", "HealRange", 20f, "How far a shaman looks for hurt tamed creatures (greenlings, your animals) to heal.");
            HealBelow = Config.Bind("Shamans", "HealBelow", 0.75f, "Shamans heal creatures below this health fraction.");
            HealPercent = Config.Bind("Shamans", "HealPercent", 40f, "Percent of the patient's max health restored per healing.");
            HealCooldown = Config.Bind("Shamans", "HealCooldown", 10f, "Seconds a shaman needs between healings.");
            HealPlayers = Config.Bind("Shamans", "HealPlayers", false, "Shamans heal hurt players too.");
            SootheChance = Config.Bind("Shamans", "SootheChance", 0.05f, "Chance every 2s that an idle shaman goes to soothe a stray nearby (builds its trust). 0 disables.");
            SootheBond = Config.Bind("Shamans", "SootheBond", 6f, "Trust a stray gains when a shaman soothes it.");
            SootheCooldown = Config.Bind("Shamans", "SootheCooldown", 60f, "Seconds before the same stray can be soothed again.");
            ShamanCompany = Config.Bind("Shamans", "Company", 1f, "Strays near a trusting shaman gain this times the 'nearby' trust (on top of yours, even when you are away).");
            FieldRadius = Config.Bind("Shamans", "FieldRadius", 12f, "How far from where you planted a shaman will extend the field (only on cultivated ground).");
            SeedsPerTrip = Config.Bind("Shamans", "SeedsPerTrip", 5, "Seeds a shaman carries per trip when planting.");
            CropJitter = Config.Bind("Shamans", "CropJitter", 0.15f, "How far (meters) a crop may land off its neat spot: rows, but planted by a greydwarf. 0 = perfect rows.");
            CropSpacing = Config.Bind("Shamans", "CropSpacing", 0f, "Distance between crops. 0 = work it out from the plants already in the field (or the crop's own growing room).");

            new Harmony(Id).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void Update()
        {
            Teaching.Update();
            Doors.Update();
        }
    }
}
