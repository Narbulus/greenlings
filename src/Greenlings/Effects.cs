using UnityEngine;

namespace Greenlings
{
    internal static class Effects
    {
        private static Material _sparkMaterial;

        /// <summary>
        /// The "burst of understanding": a small puff of green-gold sparkles rising from the
        /// greenling's head, with a soft chime. Built here rather than borrowed, so it stays small.
        /// </summary>
        public static void Understanding(Vector3 head)
        {
            var go = new GameObject("GreenlingUnderstanding");
            go.transform.position = head + Vector3.up * 0.15f;

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 0.5f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 1f, 0.35f), new Color(1f, 0.95f, 0.5f));
            main.gravityModifier = -0.12f; // drift upward
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 32;

            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 14), new ParticleSystem.Burst(0.15f, 8) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.12f;

            var fade = new Gradient();
            fade.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = fade;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = SparkMaterial();

            ps.Play();
            Object.Destroy(go, 2.5f);

            var sfx = Plugin.UnderstandingSfx.Value;
            if (sfx.Length > 0 && ZNetScene.instance.GetPrefab(sfx) is GameObject sound)
                Object.Destroy(Object.Instantiate(sound, go.transform.position, Quaternion.identity), 5f);
        }

        /// <summary>An additive glow material borrowed from a vanilla effect, so it matches the game's look.</summary>
        private static Material SparkMaterial()
        {
            if (_sparkMaterial) return _sparkMaterial;
            foreach (var name in new[] { "vfx_HitSparks", "fx_guardstone_permitted_add", "vfx_Potion_eitr_minor" })
            {
                var prefab = ZNetScene.instance.GetPrefab(name);
                var r = prefab ? prefab.GetComponentInChildren<ParticleSystemRenderer>(true) : null;
                if (r && r.sharedMaterial)
                {
                    _sparkMaterial = r.sharedMaterial;
                    Plugin.Log.LogInfo($"[GLEVT] spark_material from={name} mat={_sparkMaterial.name} shader={_sparkMaterial.shader.name}");
                    break;
                }
            }
            return _sparkMaterial ?? (_sparkMaterial = new Material(Shader.Find("Sprites/Default")));
        }

        /// <summary>Play a creature sound/effect prefab at a point (local only; used for short reactions).</summary>
        public static void Sound(string prefab, Vector3 at)
        {
            if (ZNetScene.instance.GetPrefab(prefab) is GameObject sound)
                Object.Destroy(Object.Instantiate(sound, at, Quaternion.identity), 5f);
        }
    }
}
