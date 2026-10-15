using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

namespace VRKoombar.WeaponsStaging.Core
{
    /// <summary>
    /// How multiple <see cref="ExplodableDamageable.explosionSounds"/> clips are combined on death.
    /// </summary>
    public enum ExplosionAudioMixMode
    {
        /// <summary>Pick one clip at random.</summary>
        RandomSingle,

        /// <summary>Pick <see cref="ExplodableDamageable.randomExplosionSoundCount"/> distinct clips at random, then play (see stagger).</summary>
        RandomSubset,

        /// <summary>Play every non-null clip in array order; each start is delayed by <see cref="ExplodableDamageable.explosionSoundStaggerSeconds"/> after the previous.</summary>
        AllStaggered
    }

    /// <summary>
    /// Simple damage receiver for props/terrain objects.
    /// Add this to any collider root you want projectiles to damage.
    /// </summary>
    [DisallowMultipleComponent]
    public class ExplodableDamageable : MonoBehaviour, IDamageable
    {
        [Header("Health")]
        [SerializeField] private float maxHealth = 40f;
        [SerializeField] private bool invulnerable;

        [Header("Explosion on death")]
        [SerializeField] private bool explodable = true;
        [Tooltip("One or more prefabs to spawn on death. Leave empty to disable explosion spawn.")]
        [SerializeField] private GameObject[] deathExplosionPrefabs;
        [Tooltip("If on, spawn every prefab in the list. If off, spawn one random prefab.")]
        [SerializeField] private bool spawnAllExplosionPrefabs;
        [Tooltip("Uniform scale multiplier for spawned death explosions.")]
        [SerializeField] [Min(0.01f)] private float deathExplosionScale = 1f;

        [Header("Explosion audio")]
        [Tooltip("One or more explosion sounds. How they mix is controlled by Explosion Audio Mix Mode.")]
        [SerializeField] private AudioClip[] explosionSounds;

        [SerializeField] private ExplosionAudioMixMode explosionAudioMixMode = ExplosionAudioMixMode.RandomSubset;

        [Tooltip("For Random Subset: how many clips to pick (e.g. 2). Clamped to available clips.")]
        [SerializeField] [Min(1)] private int randomExplosionSoundCount = 2;

        [Tooltip("Seconds between the start of each clip when using All Staggered, or between subset picks when Stagger Subset is on.")]
        [SerializeField] [Min(0f)] private float explosionSoundStaggerSeconds = 0.05f;

        [Tooltip("If on, Random Subset plays chosen clips one after another with stagger. If off, they all start together (layered).")]
        [SerializeField] private bool staggerRandomSubset = true;

        [SerializeField] [Range(0f, 2f)] private float explosionSoundVolume = 1f;

        [Tooltip("0 = 2D (full volume at listener — reliable in VR). 1 = 3D at explosion position (uses min/max distance).")]
        [SerializeField] [Range(0f, 1f)] private float explosionAudioSpatialBlend;

        [SerializeField] [Min(0.1f)] private float explosionAudioMinDistance = 3f;

        [SerializeField] [Min(0.1f)] private float explosionAudioMaxDistance = 250f;

        [Header("After death")]
        [Tooltip("If on, destroys this GameObject after death. If off, disables renderers/colliders only.")]
        [SerializeField] private bool destroyGameObjectOnDeath = true;
        [SerializeField] [Min(0f)] private float destroyDelaySeconds = 0f;

        private float _currentHealth;
        private bool _isDead;
        private Vector3 _lastHitPoint;
        private Vector3 _lastHitNormal = Vector3.up;

        private void Awake()
        {
            _currentHealth = Mathf.Max(1f, maxHealth);
        }

        public void ApplyDamage(in DamagePacket packet)
        {
            if (_isDead || invulnerable)
            {
                return;
            }

            _lastHitPoint = packet.HitPointWorld;
            _lastHitNormal = packet.HitNormalWorld.sqrMagnitude > 1e-8f ? packet.HitNormalWorld.normalized : Vector3.up;

            _currentHealth = Mathf.Max(0f, _currentHealth - Mathf.Max(0f, packet.BaseDamage));
            if (_currentHealth > 0f)
            {
                return;
            }

            _isDead = true;
            HandleDeath();
        }

        /// <summary>Runtime access to configured explosion clips (read-only use).</summary>
        public AudioClip[] ExplosionSounds => explosionSounds;

        public ShipCombatState GetCombatState()
        {
            return _isDead ? ShipCombatState.Destroyed : ShipCombatState.CombatReady;
        }

        public float HullFraction => maxHealth > 1e-5f ? Mathf.Clamp01(_currentHealth / maxHealth) : 0f;

        public bool IsDestroyed => _isDead;

        private void HandleDeath()
        {
            if (explodable)
            {
                SpawnDeathExplosionPrefabs();
            }

            PlayExplosionAudioIfConfigured();

            if (destroyGameObjectOnDeath)
            {
                Destroy(gameObject, destroyDelaySeconds);
                return;
            }

            DisableVisualsAndColliders();
        }

        private void SpawnDeathExplosionPrefabs()
        {
            if (deathExplosionPrefabs == null || deathExplosionPrefabs.Length == 0)
            {
                return;
            }

            Vector3 p = _lastHitPoint.sqrMagnitude > 1e-8f ? _lastHitPoint : transform.position;
            Vector3 n = _lastHitNormal.sqrMagnitude > 1e-8f ? _lastHitNormal : transform.up;

            if (spawnAllExplosionPrefabs)
            {
                for (int i = 0; i < deathExplosionPrefabs.Length; i++)
                {
                    GameObject prefab = deathExplosionPrefabs[i];
                    if (prefab == null) continue;
                    WeaponImpactVfx.Spawn(prefab, p, n, deathExplosionScale);
                }

                return;
            }

            int idx = Random.Range(0, deathExplosionPrefabs.Length);
            GameObject selected = deathExplosionPrefabs[idx];
            if (selected != null)
            {
                WeaponImpactVfx.Spawn(selected, p, n, deathExplosionScale);
            }
        }

        private void PlayExplosionAudioIfConfigured()
        {
            if (explosionSounds == null || explosionSounds.Length == 0)
            {
                return;
            }

            var valid = new List<AudioClip>(explosionSounds.Length);
            for (int i = 0; i < explosionSounds.Length; i++)
            {
                if (explosionSounds[i] != null)
                {
                    valid.Add(explosionSounds[i]);
                }
            }

            if (valid.Count == 0)
            {
                return;
            }

            Vector3 p = _lastHitPoint.sqrMagnitude > 1e-8f ? _lastHitPoint : transform.position;
            ExplosionAudioBurstHost.Play(
                p,
                valid,
                explosionAudioMixMode,
                Mathf.Max(1, randomExplosionSoundCount),
                Mathf.Max(0f, explosionSoundStaggerSeconds),
                staggerRandomSubset,
                explosionSoundVolume,
                explosionAudioSpatialBlend,
                explosionAudioMinDistance,
                explosionAudioMaxDistance);
        }

        private void DisableVisualsAndColliders()
        {
            Collider[] cols = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                cols[i].enabled = false;
            }

            Renderer[] rends = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < rends.Length; i++)
            {
                rends[i].enabled = false;
            }
        }

        /// <summary>
        /// Temporary host so staggered explosion one-shots keep playing after <see cref="ExplodableDamageable"/> is destroyed.
        /// </summary>
        private sealed class ExplosionAudioBurstHost : MonoBehaviour
        {
            internal static void Play(
                Vector3 worldPosition,
                List<AudioClip> validClips,
                ExplosionAudioMixMode mode,
                int subsetCount,
                float staggerSeconds,
                bool staggerRandomSubset,
                float volume,
                float spatialBlend,
                float minDistance,
                float maxDistance)
            {
                var go = new GameObject("ExplosionAudioBurst");
                go.transform.position = worldPosition;
                var host = go.AddComponent<ExplosionAudioBurstHost>();
                host.Configure(
                    validClips,
                    mode,
                    subsetCount,
                    staggerSeconds,
                    staggerRandomSubset,
                    volume,
                    spatialBlend,
                    minDistance,
                    maxDistance);
            }

            private List<AudioClip> _clips;
            private ExplosionAudioMixMode _mode;
            private int _subsetCount;
            private float _stagger;
            private bool _staggerRandomSubset;
            private float _volume;
            private float _spatialBlend;
            private float _minDistance;
            private float _maxDistance;
            private AudioSource _oneShotSource;

            private void Configure(
                List<AudioClip> clips,
                ExplosionAudioMixMode mode,
                int subsetCount,
                float staggerSeconds,
                bool staggerRandomSubset,
                float volume,
                float spatialBlend,
                float minDistance,
                float maxDistance)
            {
                _clips = clips;
                _mode = mode;
                _subsetCount = subsetCount;
                _stagger = staggerSeconds;
                _staggerRandomSubset = staggerRandomSubset;
                _volume = volume;
                _spatialBlend = spatialBlend;
                _minDistance = minDistance;
                _maxDistance = maxDistance;

                _oneShotSource = gameObject.AddComponent<AudioSource>();
                _oneShotSource.playOnAwake = false;
                _oneShotSource.loop = false;
                _oneShotSource.spatialBlend = _spatialBlend;
                _oneShotSource.minDistance = _minDistance;
                _oneShotSource.maxDistance = _maxDistance;
                _oneShotSource.rolloffMode = AudioRolloffMode.Logarithmic;
                _oneShotSource.dopplerLevel = 0f;
                _oneShotSource.priority = 16;
                StartCoroutine(Run());
            }

            private IEnumerator Run()
            {
                List<AudioClip> playOrder = BuildPlayOrder();
                if (playOrder == null || playOrder.Count == 0)
                {
                    Destroy(gameObject);
                    yield break;
                }

                for (int i = 0; i < playOrder.Count; i++)
                {
                    AudioClip c = playOrder[i];
                    if (c != null && _oneShotSource != null)
                    {
                        _oneShotSource.PlayOneShot(c, _volume);
                    }

                    if (i < playOrder.Count - 1)
                    {
                        bool needDelay = _mode == ExplosionAudioMixMode.AllStaggered
                            || (_mode == ExplosionAudioMixMode.RandomSubset && _staggerRandomSubset);
                        if (needDelay && _stagger > 0f)
                        {
                            yield return new WaitForSeconds(_stagger);
                        }
                    }
                }

                float tail = 0.5f;
                if (playOrder.Count > 0 && playOrder[playOrder.Count - 1] != null)
                {
                    tail = Mathf.Max(tail, playOrder[playOrder.Count - 1].length + 0.15f);
                }

                Destroy(gameObject, tail);
            }

            private List<AudioClip> BuildPlayOrder()
            {
                int n = _clips.Count;
                switch (_mode)
                {
                    case ExplosionAudioMixMode.RandomSingle:
                        return new List<AudioClip> { _clips[Random.Range(0, n)] };

                    case ExplosionAudioMixMode.RandomSubset:
                    {
                        int want = Mathf.Min(_subsetCount, n);
                        var pool = new List<AudioClip>(_clips);
                        var picked = new List<AudioClip>(want);
                        for (int i = 0; i < want; i++)
                        {
                            int idx = Random.Range(0, pool.Count);
                            picked.Add(pool[idx]);
                            pool.RemoveAt(idx);
                        }

                        return picked;
                    }

                    case ExplosionAudioMixMode.AllStaggered:
                    default:
                        return new List<AudioClip>(_clips);
                }
            }
        }
    }
}
