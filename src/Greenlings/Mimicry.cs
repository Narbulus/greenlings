using System.Collections;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// Idle greenlings near the player copy what the player does, badly: you swing, it swings at
    /// the air; you jump, it hops; you emote, it does a little flail. Runs on the player's client,
    /// which asks each greenling's owner to play the move.
    /// </summary>
    internal static class Mimicry
    {
        public const float Range = 10f;

        public static void PlayerDid(Player p, string action, string anim)
        {
            if (p != Player.m_localPlayer) return;
            foreach (var g in Greenling.All.Where(g => g && g.IsGreenling && g.State == GreenlingState.Idle
                                                        && Vector3.Distance(g.transform.position, p.transform.position) < Range))
                g.NView.InvokeRPC("GL_Mimic", action, anim, p.GetZDOID());
        }

        /// <summary>Owner side: maybe copy, after a beat (it has to notice first).</summary>
        public static IEnumerator Copy(Greenling g, string action, string anim)
        {
            yield return new WaitForSeconds(Random.Range(0.4f, 0.9f));
            if (!g || !g.IsOwner || g.State != GreenlingState.Idle) yield break;
            g.Character.m_zanim.SetTrigger(anim);
            g.Event("mimic", $"action={action} anim={anim}");
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class MimicAttack
    {
        private static void Postfix(Humanoid __instance, bool __result)
        {
            if (__result && __instance is Player p) Mimicry.PlayerDid(p, "attack", "attack");
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
    internal static class MimicJump
    {
        private static void Postfix(Character __instance)
        {
            if (__instance is Player p && p.IsOnGround()) Mimicry.PlayerDid(p, "jump", "jump");
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.StartEmote))]
    internal static class MimicEmote
    {
        private static void Postfix(Player __instance, string emote, bool __result)
        {
            // greylings can't sit or wave: sitting gets a floppy stumble, anything else a happy hop
            if (__result) Mimicry.PlayerDid(__instance, "emote:" + emote, emote == "sit" ? "stagger" : "jump");
        }
    }
}
