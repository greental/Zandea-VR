using UnityEngine;

namespace VRKoombar.WeaponsStaging.Core
{
    /// <summary>
    /// Spawns a user-authored impact prefab (Particle System, lights, audio) at hit position.
    /// Attach nothing to the prefab root — this is invoked from <see cref="WeaponProjectile"/>.
    /// </summary>
    public static class WeaponImpactVfx
    {
        /// <summary>Spawns <paramref name="prefab"/> at <paramref name="position"/> with +Z along <paramref name="normalWorld"/>.</summary>
        public static GameObject Spawn(GameObject prefab, Vector3 position, Vector3 normalWorld, float uniformScale = 1f)
        {
            if (prefab == null)
            {
                return null;
            }

            Vector3 n = normalWorld.sqrMagnitude > 1e-8f ? normalWorld.normalized : Vector3.up;
            Quaternion rot = Quaternion.LookRotation(n);

            GameObject instance = Object.Instantiate(prefab, position, rot);
            if (uniformScale > 0f && !Mathf.Approximately(uniformScale, 1f))
            {
                instance.transform.localScale = instance.transform.localScale * uniformScale;
            }

            ParticleSystem[] particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem ps in particleSystems)
            {
                ps.Clear(true);
                ps.Play(true);
            }

            float destroyAfter = Mathf.Max(0.5f, EstimateDestroyDelay(particleSystems));
            Object.Destroy(instance, destroyAfter);

            return instance;
        }

        /// <summary>
        /// Spawns a scorch-mark prefab flush to the hit plane.
        /// By default the prefab's +Y is treated as surface normal (typical quad lying in XZ).
        /// </summary>
        public static GameObject SpawnScorchMark(
            GameObject prefab,
            Vector3 position,
            Vector3 normalWorld,
            Transform parent,
            bool prefabUpIsSurfaceNormal,
            float uniformScale,
            float offsetAlongNormal,
            float randomRollDegrees,
            float lifetimeSeconds,
            bool parentToHitTransform)
        {
            if (prefab == null)
            {
                return null;
            }

            Vector3 n = normalWorld.sqrMagnitude > 1e-8f ? normalWorld.normalized : Vector3.up;
            Quaternion align = prefabUpIsSurfaceNormal
                ? Quaternion.FromToRotation(Vector3.up, n)
                : Quaternion.LookRotation(n);

            float roll = randomRollDegrees > 0f ? Random.Range(-randomRollDegrees, randomRollDegrees) : 0f;
            Quaternion rollQ = Quaternion.AngleAxis(roll, n);
            Quaternion rot = rollQ * align;

            Vector3 spawnPos = position + n * Mathf.Max(0f, offsetAlongNormal);
            GameObject mark = Object.Instantiate(prefab, spawnPos, rot);
            if (uniformScale > 0f && !Mathf.Approximately(uniformScale, 1f))
            {
                mark.transform.localScale = mark.transform.localScale * uniformScale;
            }

            if (parentToHitTransform && parent != null)
            {
                mark.transform.SetParent(parent, true);
            }

            if (lifetimeSeconds > 0f)
            {
                Object.Destroy(mark, lifetimeSeconds);
            }

            return mark;
        }

        static float EstimateDestroyDelay(ParticleSystem[] systems)
        {
            if (systems == null || systems.Length == 0)
            {
                return 5f;
            }

            float maxT = 0f;
            foreach (ParticleSystem ps in systems)
            {
                if (ps == null)
                {
                    continue;
                }

                ParticleSystem.MainModule m = ps.main;
                float startMax = GetStartLifetimeMax(m.startLifetime);
                maxT = Mathf.Max(maxT, m.duration + startMax);
            }

            return maxT + 0.35f;
        }

        static float GetStartLifetimeMax(ParticleSystem.MinMaxCurve curve)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return curve.constant;
                case ParticleSystemCurveMode.TwoConstants:
                    return Mathf.Max(curve.constantMin, curve.constantMax);
                case ParticleSystemCurveMode.Curve:
                case ParticleSystemCurveMode.TwoCurves:
                    return curve.constantMax;
                default:
                    return curve.constantMax > 1e-5f ? curve.constantMax : curve.constant;
            }
        }
    }
}
