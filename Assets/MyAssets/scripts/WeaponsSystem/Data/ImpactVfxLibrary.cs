using System;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;
using VRKoombar.WeaponsStaging.Core;

namespace VRKoombar.WeaponsStaging.Data
{
    [CreateAssetMenu(menuName = "VRKoombar/Combat/Impact VFX Library")]
    public sealed class ImpactVfxLibrary : ScriptableObject
    {
        [Serializable]
        public struct DamageTypeDefault
        {
            public DamageType damageType;
            public GameObject prefab;
            [Min(0.01f)] public float scale;
        }

        [Serializable]
        public struct DamageSurfaceOverride
        {
            public DamageType damageType;
            public ImpactSurfaceType surfaceType;
            public GameObject prefab;
            [Min(0.01f)] public float scale;
        }

        [SerializeField] private DamageTypeDefault[] defaultsByDamageType;
        [SerializeField] private DamageSurfaceOverride[] overridesByDamageAndSurface;

        public bool TryResolve(DamageType damageType, ImpactSurfaceType surface, out GameObject prefab, out float scale)
        {
            for (int i = 0; i < overridesByDamageAndSurface.Length; i++)
            {
                DamageSurfaceOverride e = overridesByDamageAndSurface[i];
                if (e.damageType == damageType && e.surfaceType == surface && e.prefab != null)
                {
                    prefab = e.prefab;
                    scale = e.scale > 0f ? e.scale : 1f;
                    return true;
                }
            }

            for (int i = 0; i < defaultsByDamageType.Length; i++)
            {
                DamageTypeDefault d = defaultsByDamageType[i];
                if (d.damageType == damageType && d.prefab != null)
                {
                    prefab = d.prefab;
                    scale = d.scale > 0f ? d.scale : 1f;
                    return true;
                }
            }

            prefab = null;
            scale = 1f;
            return false;
        }
    }
}
