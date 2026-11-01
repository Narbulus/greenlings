using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// gl_uninstall: make a world safe to keep playing without the mod.
    ///
    /// Nothing of ours is saved as a prefab: greenlings are vanilla creatures with a tamed flag and
    /// some gl_* ZDO keys. Without the mod a tamed greenling is an inert, friendly greyling that
    /// fights wild creatures, can't be patted or renamed and never follows anyone; whatever it was
    /// carrying exists only as a gl_* key and would vanish. This command puts carried items on the
    /// ground, strips our keys, and untames, removes or keeps the creatures, across the whole world
    /// (loaded or not). It must run where the world lives: single player, or the hosting client.
    /// </summary>
    internal static class Uninstall
    {
        /// <summary>Set once the command has run: the mod stops touching anything for the rest of the session.</summary>
        public static bool Dormant { get; private set; }

        // every kind the mod can tame (including ones only tameable in newer versions)
        private static readonly string[] Prefabs = { "Greyling", "Greydwarf", "Greydwarf_Elite" };

        private static readonly string[] Keys =
        {
            Greenling.ZState, Greenling.ZCuriosity, Greenling.ZMoodTarget, Greenling.ZAttention, Greenling.ZTask,
            Greenling.ZCarryItem, Greenling.ZCarryCount, Greenling.ZBond, Greenling.ZLastPat, Greenling.ZLastFed,
            Greenling.ZTraits, CarryVisual.ZSprout,
        };

        private static IEnumerable<int> KeyHashes()
        {
            foreach (var k in Keys) yield return k.GetStableHashCode();
            var teacher = ZDO.GetHashZDOID(Greenling.ZTeacher);
            yield return teacher.Key;
            yield return teacher.Value;
        }

        public static void Register()
        {
            new Terminal.ConsoleCommand("gl_uninstall",
                "[untame|remove|keep] Greenlings: prepare this world for removing the mod (no argument: report only)",
                args => Run(args.Context, args.Length > 1 ? args[1].ToLowerInvariant() : ""));
        }

        private static void Run(Terminal ctx, string mode)
        {
            void Say(string s)
            {
                ctx.AddString(s);
                Plugin.Log.LogInfo("[GLEVT] uninstall " + s);
            }

            if (!ZNet.instance || !ZNet.instance.IsServer() || !Player.m_localPlayer)
            {
                Say("run this in single player or on the client hosting the world (a dedicated server's world: copy it to your PC and open it locally)");
                return;
            }
            if (mode == "resume")
            {
                // changed your mind (or a test continuing): wake the mod up again; cleared data is not restored
                Dormant = false;
                Say("resumed: Greenlings is active again");
                return;
            }
            if (mode.Length > 0 && mode != "untame" && mode != "remove" && mode != "keep")
            {
                Say($"unknown mode '{mode}': use untame, remove or keep");
                return;
            }

            var prefabs = new HashSet<int>(Prefabs.Select(p => p.GetStableHashCode()));
            var ours = ZDOMan.instance.m_objectsByID.Values
                .Where(z => prefabs.Contains(z.GetPrefab()) && (z.GetBool(ZDOVars.s_tamed) || HasOurData(z)))
                .ToList();
            var tamed = ours.Count(z => z.GetBool(ZDOVars.s_tamed));
            var carrying = ours.Where(z => z.GetInt(Greenling.ZCarryCount) > 0).ToList();
            Say($"found {tamed} tamed greenling(s)/stray(s), {ours.Count - tamed} wild one(s) with leftover data, " +
                $"{carrying.Count} carrying something ({string.Join(", ", carrying.Select(z => $"{z.GetInt(Greenling.ZCarryCount)} {z.GetString(Greenling.ZCarryItem)}"))})");
            if (mode.Length == 0)
            {
                Say("nothing changed. 'gl_uninstall untame' turns them back into wild (hostile) greylings, " +
                    "'gl_uninstall remove' deletes them, 'gl_uninstall keep' leaves them tame. All three drop what they carry and clear the mod's data.");
                return;
            }

            var keys = KeyHashes().ToArray();
            var me = ZDOMan.GetSessionID();
            var player = Player.m_localPlayer;
            int dropped = 0, removed = 0, untamed = 0;
            foreach (var z in ours)
            {
                var view = ZNetScene.instance.FindInstance(z);
                if (!z.IsOwner()) z.SetOwner(me);

                // carried items exist only in our keys: put them on the ground (by you if the greenling is far away)
                var item = z.GetString(Greenling.ZCarryItem);
                var n = z.GetInt(Greenling.ZCarryCount);
                if (n > 0 && item.Length > 0)
                {
                    var at = view ? view.transform.position + Vector3.up : player.transform.position + player.transform.forward + Vector3.up;
                    Prank.Drop(at, item, n);
                    dropped += n;
                }

                if (mode == "remove")
                {
                    if (view) ZNetScene.instance.Destroy(view.gameObject);
                    else ZDOMan.instance.DestroyZDO(z);
                    removed++;
                    continue;
                }

                foreach (var h in keys) ZDOExtraData.Remove(z.m_uid, h);
                if (mode == "untame" && z.GetBool(ZDOVars.s_tamed))
                {
                    z.Set(ZDOVars.s_tamed, false);
                    z.Set(ZDOVars.s_tamedName, "");
                    var character = view ? view.GetComponent<Character>() : null;
                    if (character) character.m_tamed = false;
                    untamed++;
                }
                z.IncreaseDataRevision();
            }

            Dormant = true;
            Say($"done ({mode}): {untamed} untamed, {removed} removed, {dropped} carried item(s) dropped, data cleared on {ours.Count - removed}. " +
                "Greenlings is now dormant until restart: save and quit (logout), then remove the mod.");
        }

        private static bool HasOurData(ZDO z) =>
            z.GetString(Greenling.ZTraits).Length > 0 || z.GetString(CarryVisual.ZSprout).Length > 0 ||
            z.GetString(Greenling.ZTask).Length > 0 || z.GetInt(Greenling.ZCarryCount) > 0 ||
            z.GetFloat(Greenling.ZBond, -1f) >= 0f || z.GetFloat(Greenling.ZCuriosity, -1f) >= 0f;
    }
}
