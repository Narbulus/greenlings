using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Greenlings
{
    /// <summary>
    /// The Greenling Nest: a buildable piece (hammer, furniture tab) made from the vanilla
    /// vulture straw pile, shrunk to greenling size. Greenlings sleep in a nearby nest at night.
    /// The prefab is created at runtime and registered with ZNetScene so nests save and load
    /// like any other piece.
    /// </summary>
    internal static class Nest
    {
        public const string PrefabName = "piece_greenling_nest";
        private static GameObject _prefab;
        private static Transform _templates;

        /// <summary>Create the prefab once (needs ZNetScene for the source model), register it, add it to the hammer.</summary>
        public static void Ensure()
        {
            var scene = ZNetScene.instance;
            if (scene && !_prefab) _prefab = Create(scene);
            if (!_prefab) return;

            if (scene && !scene.m_prefabs.Contains(_prefab))
            {
                scene.m_prefabs.Add(_prefab);
                scene.m_namedPrefabs[PrefabName.GetStableHashCode()] = _prefab;
            }

            var hammer = ObjectDB.instance ? ObjectDB.instance.GetItemPrefab("Hammer") : null;
            var table = hammer ? hammer.GetComponent<ItemDrop>().m_itemData.m_shared.m_buildPieces : null;
            if (table && !table.m_pieces.Contains(_prefab)) table.m_pieces.Add(_prefab);
        }

        private static GameObject Create(ZNetScene scene)
        {
            var source = scene.GetPrefab("volture_strawpile");
            var bed = scene.GetPrefab("bed")?.GetComponent<Piece>();
            var workbench = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            if (!source || !bed)
            {
                Plugin.Log.LogError("nest: missing source prefabs (volture_strawpile / bed)");
                return null;
            }

            if (!_templates)
            {
                var root = new GameObject("GreenlingTemplates");
                root.SetActive(false); // templates never run their scripts
                Object.DontDestroyOnLoad(root);
                _templates = root.transform;
            }

            var go = Object.Instantiate(source, _templates, false);
            go.name = PrefabName;
            go.transform.localScale = Vector3.one * Plugin.NestScale.Value;
            foreach (var d in go.GetComponentsInChildren<Destructible>(true)) Object.DestroyImmediate(d);

            // something to aim the build ghost at and to hit with a hammer
            if (!go.GetComponentInChildren<Collider>(true))
            {
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.6f, 0f);
                box.size = new Vector3(5f, 1.2f, 5f); // in the unscaled model's space
            }

            var piece = go.AddComponent<Piece>();
            piece.m_name = "Greenling Nest";
            piece.m_description = "A cosy straw nest. Greenlings sleep better (and behave better) with one nearby.";
            piece.m_icon = bed.m_icon;
            piece.m_category = Piece.PieceCategory.Furniture;
            piece.m_craftingStation = workbench;
            piece.m_groundOnly = true;
            piece.m_placeEffect = bed.m_placeEffect;
            piece.m_resources = new[]
            {
                new Piece.Requirement { m_resItem = Item("Wood"), m_amount = 6, m_recover = true },
                new Piece.Requirement { m_resItem = Item("Resin"), m_amount = 3, m_recover = true },
            };

            var wear = go.AddComponent<WearNTear>();
            var bedWear = bed.GetComponent<WearNTear>();
            wear.m_health = 200f;
            wear.m_materialType = WearNTear.MaterialType.Wood;
            if (bedWear)
            {
                wear.m_destroyedEffect = bedWear.m_destroyedEffect;
                wear.m_hitEffect = bedWear.m_hitEffect;
            }

            Plugin.Log.LogInfo($"nest: created {PrefabName} (scale {Plugin.NestScale.Value})");
            return go;
        }

        private static ItemDrop Item(string name) => ObjectDB.instance?.GetItemPrefab(name)?.GetComponent<ItemDrop>()
                                                     ?? ZNetScene.instance.GetPrefab(name)?.GetComponent<ItemDrop>();

        /// <summary>The nearest nest within range of a point, or null.</summary>
        public static Piece Near(Vector3 at, float range) =>
            Object.FindObjectsByType<Piece>(FindObjectsSortMode.None)
                .Where(p => p && Utils.GetPrefabName(p.gameObject) == PrefabName && Vector3.Distance(p.transform.position, at) < range)
                .OrderBy(p => Vector3.Distance(p.transform.position, at))
                .FirstOrDefault();

        [HarmonyPatch(typeof(ZNetScene), "Awake"), HarmonyPostfix, HarmonyPriority(Priority.Low)]
        private static void OnScene()
        {
            try { Ensure(); }
            catch (System.Exception e) { Plugin.Log.LogError("nest: " + e); }
        }

        [HarmonyPatch(typeof(ObjectDB), "Awake"), HarmonyPostfix]
        private static void OnObjectDB()
        {
            try { Ensure(); }
            catch (System.Exception e) { Plugin.Log.LogError("nest: " + e); }
        }
    }
}
