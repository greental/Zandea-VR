using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

namespace VRKoombar.WeaponsStaging.Data
{
    [CreateAssetMenu(menuName = "VRKoombar/Combat/Weapon Definition")]
    public class WeaponDefinition : ScriptableObject
    {
        public string weaponId;
        public DamageType damageType;

        [Header("Projectile")]
        [Tooltip("Prefab in the Project (not a scene object). Needs Rigidbody + Collider + WeaponProjectile.")]
        public GameObject projectilePrefab;
        public float projectileSpeed = 350f;
        public float fireRate = 8f;
        public float weaponHeatPerShot = 0.2f;
        public int ammoPerMagazine = 0;
        public float reloadSeconds = 0f;
        public float shieldInteraction = 1f;
        public float hullInteraction = 1f;
        public WeaponSubsystemEffect[] subsystemEffects;
        public bool requiresDeployedHardpoint = true;

        [Header("Audio")]
        [Tooltip("Drag your **audio clip asset** here (the sound file). Per-weapon: laser vs ballistic. Optional override on Weapon Controller.")]
        public AudioClip fireSound;

        [Range(0f, 2f)]
        [Tooltip("Volume multiplier for Fire Sound.")]
        public float fireSoundVolume = 1f;

        [Header("Audio — Gatling / sustained burst")]
        [Tooltip("If on, uses Start / Loop / End instead of a single Fire Sound per shot. Loop plays while firing; End plays after you stop (see Tail Delay).")]
        public bool useGatlingStyleFireAudio;

        [Tooltip("One-shot when a burst begins (spin-up).")]
        public AudioClip gatlingFireSoundStart;

        [Tooltip("Seamless loop while the weapon keeps firing. Must loop cleanly in an audio editor.")]
        public AudioClip gatlingFireSoundLoop;

        [Tooltip("One-shot when the burst ends (spin-down).")]
        public AudioClip gatlingFireSoundEnd;

        [Tooltip(
            "Seconds after trigger **release** before End plays and the loop stops. " +
            "Not the gap between shots — use a short value (e.g. 0.08–0.15).")]
        [Min(0.02f)]
        public float gatlingFireTailDelay = 0.12f;

        [Tooltip(
            "Extra seconds after trigger press before **projectiles** and **loop** begin. " +
            "If **Gatling Fire Sound Start** is set, the effective wait is at least that clip’s length (or max with this value). " +
            "Use 0 to rely on start-clip length only.")]
        [Min(0f)]
        public float gatlingProjectileSpinUpSeconds;

        [Header("Impact VFX")]
        [Tooltip(
            "Optional prefab spawned at the hit point when the projectile strikes a surface that is **not** your own ship. " +
            "Use a root with **Particle System** children (or a single PS on the root). +Z aligns with the hit **normal**.")]
        public GameObject hitImpactEffectPrefab;

        [Tooltip("Uniform scale for the spawned instance (1 = keep prefab scale).")]
        [Min(0.01f)]
        public float hitImpactEffectScale = 1f;

        [Header("Impact Scorch Mark")]
        [Tooltip(
            "Optional scorch/decal prefab spawned on hit surfaces (terrain or objects). " +
            "Aligns to hit normal so it lies on the impacted plane.")]
        public GameObject hitScorchMarkPrefab;

        [Tooltip("If on, prefab +Y is treated as the surface normal. If off, prefab +Z is used.")]
        public bool hitScorchMarkUsesUpAsNormal = true;

        [Tooltip("Uniform scale for scorch mark instances (1 = keep prefab scale).")]
        [Min(0.01f)]
        public float hitScorchMarkScale = 1f;

        [Tooltip("Small offset along surface normal to avoid z-fighting.")]
        [Min(0f)]
        public float hitScorchMarkSurfaceOffset = 0.01f;

        [Tooltip("Random in-plane roll (+/- degrees) around the hit normal for variation.")]
        [Range(0f, 180f)]
        public float hitScorchMarkRandomRollDegrees = 180f;

        [Tooltip("Auto-destroy scorch marks after this many seconds. Set <= 0 to keep forever.")]
        public float hitScorchMarkLifetimeSeconds = 30f;

        [Tooltip("Parent scorch marks to the hit transform so they follow moving objects.")]
        public bool hitScorchMarkParentToHitObject = true;
    }
}
