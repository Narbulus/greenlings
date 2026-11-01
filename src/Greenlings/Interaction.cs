using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Player-facing interaction on tamed greenlings: E = Pat (start teaching if curious),
    /// Shift+E = rename (vanilla), use a coin on it = raise curiosity.
    /// </summary>
    internal static class Interaction
    {
        public static bool IsGreenling(Tameable t, out Greenling g)
        {
            g = t.GetComponent<Greenling>();
            return g && g.IsGreenling;
        }

        public static void Pat(Greenling g, Humanoid user)
        {
            var player = user as Player;
            if (!player) return;
            var name = g.GetName();

            if (g.State == GreenlingState.Watching && g.Teacher == player.GetZDOID())
            {
                // a second pat while it watches = "never mind": forget the job and go play
                player.Message(MessageHud.MessageType.Center, $"{name} shrugs and wanders off");
                Plugin.Log.LogInfo($"[GLEVT] pat_dismiss gl={g.Label}");
                g.NView.InvokeRPC("GL_Dismiss");
                return;
            }
            if (g.Curiosity < Plugin.LearnThreshold.Value)
            {
                player.Message(MessageHud.MessageType.Center, $"{name} swats your hand away. Maybe a coin would help?");
                g.NView.InvokeRPC(ZRoutedRpc.Everybody, "GL_Swat");
                Plugin.Log.LogInfo($"[GLEVT] pat_refused gl={g.Label} curiosity={g.Curiosity:0}");
                return;
            }
            g.Tameable.m_petEffect.Create(g.transform.position, g.transform.rotation); // a happy pat
            player.Message(MessageHud.MessageType.Center, $"{name} perks up and watches you closely");
            Plugin.Log.LogInfo($"[GLEVT] pat gl={g.Label} curiosity={g.Curiosity:0}");
            g.NView.InvokeRPC("GL_StartWatch", player.GetZDOID());
        }
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.Interact))]
    internal static class PatPatch
    {
        private static bool Prefix(Tameable __instance, Humanoid user, bool hold, bool alt, ref bool __result)
        {
            if (!Interaction.IsGreenling(__instance, out var g) || hold || alt) return true; // alt = vanilla rename
            Interaction.Pat(g, user);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.UseItem))]
    internal static class GiftPatch
    {
        private static bool Prefix(Tameable __instance, Humanoid user, ItemDrop.ItemData item, ref bool __result)
        {
            if (!Interaction.IsGreenling(__instance, out var g) || Items.PrefabOf(item) != Plugin.TameItem.Value) return true;
            user.GetInventory().RemoveItem(item, 1);
            user.Message(MessageHud.MessageType.Center, $"{g.GetName()} snatches the coin and looks at you with interest");
            Plugin.Log.LogInfo($"[GLEVT] gift gl={g.Label} item={Plugin.TameItem.Value}");
            g.NView.InvokeRPC("GL_Gift");
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Tameable), nameof(Tameable.GetHoverText))]
    internal static class HoverPatch
    {
        private static void Postfix(Tameable __instance, ref string __result)
        {
            if (!Interaction.IsGreenling(__instance, out var g)) return;
            var status = g.State switch
            {
                GreenlingState.Watching => "watching you",
                GreenlingState.Working => g.Task != null ? $"hauling {g.Task.Describe()}" : "working",
                _ => g.MoodWord(),
            };
            if (g.State == GreenlingState.Working && g.CarryCount > 0) status += $", carrying {g.CarryCount} {Items.DisplayName(g.CarryItem)}";
            var text = $"{__instance.GetHoverName()} ( {status} )" +
                       (g.State == GreenlingState.Watching ? "\n[<color=yellow><b>$KEY_Use</b></color>] Never mind (forget its job)"
                                                           : "\n[<color=yellow><b>$KEY_Use</b></color>] Pat") +
                       "\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] $hud_rename";
            __result = Localization.instance.Localize(text);
        }
    }
}
