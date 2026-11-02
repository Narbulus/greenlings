using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Player-facing interaction on tamed greenlings:
    ///   E          = Pat: affection for strays (they tag along for a bit), follow / stay for the rest,
    ///                calms a frightened one, "never mind" for one watching you.
    ///   Shift+E    = rename (vanilla).
    ///   use a coin = watch and learn.  use food = feed it.  use a hammer / cultivator = hand it over (a tool).
    /// </summary>
    internal static class Interaction
    {
        public static bool IsGreenling(Tameable t, out Greenling g)
        {
            g = t.GetComponent<Greenling>();
            return g && g.IsGreenling;
        }

        public static bool IsFood(string prefab) =>
            prefab != null && Plugin.Foods.Value.Split(',').Any(f => f.Trim() == prefab);

        public static IEnumerable<ItemDrop> FoodPrefabs() =>
            Plugin.Foods.Value.Split(',').Select(f => ObjectDB.instance ? ObjectDB.instance.GetItemPrefab(f.Trim()) : null)
                .Where(p => p).Select(p => p.GetComponent<ItemDrop>()).Where(d => d);

        public static void Pat(Greenling g, Player player)
        {
            var name = g.GetName();
            var fresh = PatFresh(g);

            if (g.State == GreenlingState.Watching && !g.CasualWatch && g.Teacher == player.GetZDOID())
            {
                // a pat while it watches = "never mind": forget the job and go play
                player.Message(MessageHud.MessageType.Center, $"{name} shrugs and forgets all about it");
                Plugin.Log.LogInfo($"[GLEVT] pat_dismiss gl={g.Label}");
                g.NView.InvokeRPC("GL_Dismiss");
                return;
            }

            string msg;
            if (g.Scared) msg = Danger.ThreatNear(g) ? $"{name} is too frightened to notice" : $"{name} calms down";
            else if (g.IsStray) msg = fresh ? $"{name} leans into your hand" : $"{name} has had enough fuss for now";
            else if (g.FollowTarget == player && !Following(g)) msg = $"{name} follows you"; // tagging along: now for real
            else if (Following(g)) msg = $"{name} stays here";
            else msg = $"{name} follows you";
            player.Message(MessageHud.MessageType.Center, msg);
            if (fresh && !g.Scared) g.Tameable.m_petEffect.Create(g.transform.position, g.transform.rotation);
            g.NView.InvokeRPC("GL_Pat", player.GetZDOID());
        }

        /// <summary>Following for good (not just tagging along after a treat).</summary>
        private static bool Following(Greenling g) => g.FollowTarget && g.Zdo.GetLong(Greenling.ZFollowUntil, 0L) == 0L;

        private static bool PatFresh(Greenling g) =>
            (ZNet.instance.GetTime() - new System.DateTime(g.Zdo.GetLong(Greenling.ZLastPat, 0L))).TotalSeconds
            >= Plugin.PatCooldown.Value * Traits.PatCooldown(g);

        public static bool UseItem(Greenling g, Player player, ItemDrop.ItemData item)
        {
            var prefab = Items.PrefabOf(item);
            var name = g.GetName();
            if (prefab == Plugin.TeachItem.Value)
            {
                if (g.IsStray) { player.Message(MessageHud.MessageType.Center, $"{name} doesn't trust you enough to learn from you yet"); return true; }
                if (g.State == GreenlingState.Watching && !g.CasualWatch) { player.Message(MessageHud.MessageType.Center, $"{name} is already watching you"); return true; }
                player.GetInventory().RemoveItem(item, 1);
                player.Message(MessageHud.MessageType.Center, $"{name} pockets the coin and watches you closely");
                Plugin.Log.LogInfo($"[GLEVT] coin gl={g.Label}");
                g.NView.InvokeRPC("GL_StartWatch", player.GetZDOID(), false);
                return true;
            }
            if (IsFood(prefab))
            {
                if (!g.WantsFood && !(g.IsBonded && FedLongAgo(g)))
                {
                    player.Message(MessageHud.MessageType.Center, $"{name} is full");
                    return true;
                }
                player.GetInventory().RemoveItem(item, 1);
                player.Message(MessageHud.MessageType.Center, $"{name} munches the {Items.DisplayName(prefab)} happily");
                g.Tameable.m_sootheEffect.Create(g.Character.GetCenterPoint(), Quaternion.identity);
                Plugin.Log.LogInfo($"[GLEVT] feed gl={g.Label} item={prefab}");
                g.NView.InvokeRPC("GL_Feed", prefab, player.GetZDOID());
                return true;
            }
            if (Tools.IsTool(prefab))
            {
                if (!Tools.CanUse(g, prefab, out var why))
                {
                    player.Message(MessageHud.MessageType.Center, $"{name} {why}");
                    return true;
                }
                player.UnequipItem(item);
                player.GetInventory().RemoveItem(item);
                player.Message(MessageHud.MessageType.Center, $"{name} takes the {Tools.Name(prefab)} very seriously");
                Plugin.Log.LogInfo($"[GLEVT] give_tool gl={g.Label} item={prefab}");
                g.NView.InvokeRPC("GL_Tool", prefab);
                return true;
            }
            return false;
        }

        /// <summary>A trusting greenling will take a snack between meals, just not constantly.</summary>
        private static bool FedLongAgo(Greenling g) =>
            (ZNet.instance.GetTime() - new System.DateTime(g.Zdo.GetLong(Greenling.ZLastFed, 0L))).TotalSeconds > Plugin.FeedCooldown.Value;
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Interact))]
    internal static class PatPatch
    {
        private static bool Prefix(Tameable __instance, Humanoid user, bool hold, bool alt, ref bool __result)
        {
            if (!Interaction.IsGreenling(__instance, out var g) || hold || alt || !(user is Player p)) return true; // alt = vanilla rename
            Interaction.Pat(g, p);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.UseItem))]
    internal static class GiftPatch
    {
        private static bool Prefix(Tameable __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
        {
            if (!Interaction.IsGreenling(__instance, out var g) || !(user is Player p)) return true;
            if (!Interaction.UseItem(g, p, item)) return true;
            __result = true;
            return false;
        }
    }

    /// <summary>Vanilla eating (food lying on the ground) follows our idea of hungry, not the taming timer.</summary>
    [HarmonyPatch(typeof(Tameable), nameof(Tameable.IsHungry))]
    internal static class HungerPatch
    {
        private static void Postfix(Tameable __instance, ref bool __result)
        {
            if (Interaction.IsGreenling(__instance, out var g)) __result = g.WantsFood;
        }
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
    internal static class HoverPatch
    {
        private static void Postfix(Tameable __instance, ref string __result)
        {
            if (!Interaction.IsGreenling(__instance, out var g)) return;
            var traits = g.TraitList.Length > 0
                ? "\n<color=#9ad97c>" + string.Join(", ", g.TraitList.Split(',').Select(Traits.Explain)) + "</color>"
                : "";
            string use;
            if (g.State == GreenlingState.Watching && !g.CasualWatch) use = "Never mind (forget its job)";
            else if (g.Scared) use = "Calm";
            else if (g.IsStray) use = "Pat";
            else use = g.FollowTarget && g.Zdo.GetLong(Greenling.ZFollowUntil, 0L) == 0L ? "Pat (stay)" : "Pat (follow me)";
            var hint = g.IsStray ? "\n<size=80%>Feed it and spend time with it to earn its trust</size>"
                                 : $"\n<size=80%>Give it {Items.DisplayName(Plugin.TeachItem.Value)} to teach it a job</size>";
            if (g.Tool.Length > 0) traits += $"\n<size=80%>holding a {Tools.Name(g.Tool)}</size>";
            var text = $"{__instance.GetHoverName()} ( {Roles.Name(g.Role)}: {g.Status()} ){traits}" +
                       $"\n[<color=yellow><b>$KEY_Use</b></color>] {use}" +
                       "\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $hud_rename" + hint;
            __result = Localization.instance.Localize(text);
        }
    }
}
