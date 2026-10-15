using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;
using VRKoombar.WeaponsStaging.Data;

namespace VRKoombar.WeaponsStaging.Core
{
    public class WeaponProjectile : MonoBehaviour
    {
        [SerializeField] private string weaponId;
        [SerializeField] private DamageType damageType = DamageType.Kinetic;
        [SerializeField] private float shieldInteraction = 1f;
        [SerializeField] private float hullInteraction = 1f;
        [SerializeField] private GameObject hitImpactEffectPrefab;
        [SerializeField] private float hitImpactEffectScale = 1f;
        [SerializeField] private ImpactVfxLibrary impactVfxLibrary;
        [SerializeField] private GameObject hitScorchMarkPrefab;
        [SerializeField] private bool hitScorchMarkUsesUpAsNormal = true;
        [SerializeField] private float hitScorchMarkScale = 1f;
        [SerializeField] private float hitScorchMarkSurfaceOffset = 0.01f;
        [SerializeField] private float hitScorchMarkRandomRollDegrees = 180f;
        [SerializeField] private float hitScorchMarkLifetimeSeconds = 30f;
        [SerializeField] private bool hitScorchMarkParentToHitObject = true;
        [SerializeField] private float lifetimeSeconds = 6f;
        [SerializeField, Min(0f)] private float sweptHitRadius = .45f;

        private float _spawnTime;
        private GameObject _source;
        private GameObject _instanceRoot;
        private bool _hitProcessed;
        private Vector3 _previousPosition;
        private readonly RaycastHit[] _sweepHits = new RaycastHit[32];

        private void Awake()
        {
            _spawnTime = Time.time;
            _instanceRoot = transform.root.gameObject;
            _previousPosition = transform.position;
        }

        public void SetSource(GameObject sourceObject)
        {
            _source = sourceObject;
            _previousPosition = transform.position;
        }

        private void FixedUpdate()
        {
            if (_hitProcessed) return;
            Vector3 travel = transform.position - _previousPosition;
            float distance = travel.magnitude;
            if (distance > .001f && sweptHitRadius > 0f)
            {
                int count = Physics.SphereCastNonAlloc(_previousPosition, sweptHitRadius,
                    travel / distance, _sweepHits, distance, ~0, QueryTriggerInteraction.Ignore);
                int nearest = -1;
                float nearestDistance = float.PositiveInfinity;
                for (int i = 0; i < count; i++)
                {
                    Collider candidate = _sweepHits[i].collider;
                    if (candidate == null || candidate.transform.IsChildOf(_instanceRoot.transform) ||
                        _source != null && candidate.transform.root == _source.transform.root) continue;
                    if (_sweepHits[i].distance >= nearestDistance) continue;
                    nearest = i;
                    nearestDistance = _sweepHits[i].distance;
                }
                if (nearest >= 0)
                {
                    RaycastHit hit = _sweepHits[nearest];
                    TryProcessHit(hit.collider.gameObject, hit.point, hit.normal);
                }
            }
            _previousPosition = transform.position;
        }

        public void Configure(
            string weaponIdValue,
            DamageType damageTypeValue,
            float shieldInteractionValue,
            float hullInteractionValue,
            GameObject hitImpactEffectPrefabValue,
            float hitImpactEffectScaleValue,
            ImpactVfxLibrary impactVfxLibraryValue,
            GameObject hitScorchMarkPrefabValue,
            bool hitScorchMarkUsesUpAsNormalValue,
            float hitScorchMarkScaleValue,
            float hitScorchMarkSurfaceOffsetValue,
            float hitScorchMarkRandomRollDegreesValue,
            float hitScorchMarkLifetimeSecondsValue,
            bool hitScorchMarkParentToHitObjectValue,
            GameObject sourceObject)
        {
            weaponId = weaponIdValue;
            damageType = damageTypeValue;
            shieldInteraction = shieldInteractionValue;
            hullInteraction = hullInteractionValue;
            hitImpactEffectPrefab = hitImpactEffectPrefabValue;
            hitImpactEffectScale = hitImpactEffectScaleValue;
            impactVfxLibrary = impactVfxLibraryValue;
            hitScorchMarkPrefab = hitScorchMarkPrefabValue;
            hitScorchMarkUsesUpAsNormal = hitScorchMarkUsesUpAsNormalValue;
            hitScorchMarkScale = hitScorchMarkScaleValue;
            hitScorchMarkSurfaceOffset = hitScorchMarkSurfaceOffsetValue;
            hitScorchMarkRandomRollDegrees = hitScorchMarkRandomRollDegreesValue;
            hitScorchMarkLifetimeSeconds = hitScorchMarkLifetimeSecondsValue;
            hitScorchMarkParentToHitObject = hitScorchMarkParentToHitObjectValue;
            _source = sourceObject;
        }

        private void Update()
        {
            if (_hitProcessed)
            {
                return;
            }

            if (Time.time - _spawnTime >= lifetimeSeconds)
            {
                DestroyProjectileInstance();
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null || collision.gameObject == null)
            {
                return;
            }

            ContactPoint cp = collision.GetContact(0);
            TryProcessHit(collision.gameObject, cp.point, cp.normal);
        }

        /// <summary>
        /// Physics triggers do not invoke <see cref="OnCollisionEnter"/>; many environments use trigger volumes.
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            if (other == null)
            {
                return;
            }

            Vector3 hitPoint = other.ClosestPoint(transform.position);
            Vector3 n = hitPoint - transform.position;
            if (n.sqrMagnitude < 1e-8f)
            {
                n = -transform.forward;
            }
            else
            {
                n.Normalize();
            }

            TryProcessHit(other.gameObject, hitPoint, n);
        }

        private void TryProcessHit(GameObject hitObject, Vector3 hitPointWorld, Vector3 hitNormalWorld)
        {
            if (_hitProcessed || hitObject == null)
            {
                return;
            }

            _hitProcessed = true;

            bool sameShipRoot = _source != null && hitObject.transform.root == _source.transform.root;

            if (!sameShipRoot)
            {
                ImpactSurfaceType surface = ResolveSurfaceType(hitObject);
                if (TryResolveImpactPrefab(surface, out GameObject impactPrefab, out float impactScale))
                {
                    WeaponImpactVfx.Spawn(impactPrefab, hitPointWorld, hitNormalWorld, impactScale);
                }
            }

            if (hitScorchMarkPrefab != null
                && !sameShipRoot)
            {
                WeaponImpactVfx.SpawnScorchMark(
                    hitScorchMarkPrefab,
                    hitPointWorld,
                    hitNormalWorld,
                    hitObject.transform,
                    hitScorchMarkUsesUpAsNormal,
                    hitScorchMarkScale,
                    hitScorchMarkSurfaceOffset,
                    hitScorchMarkRandomRollDegrees,
                    hitScorchMarkLifetimeSeconds,
                    hitScorchMarkParentToHitObject);
            }

            if (!sameShipRoot)
            {
                IDamageable damageable = ResolveDamageable(hitObject);
                if (damageable != null)
                {
                    DamagePacket packet = new DamagePacket
                    {
                        DamageType = damageType,
                        BaseDamage = hullInteraction,
                        HeatAmount = shieldInteraction,
                        HitPointWorld = hitPointWorld,
                        HitNormalWorld = hitNormalWorld,
                        Source = _source,
                        WeaponId = weaponId,
                        Timestamp = Time.time
                    };

                    if (ShouldLogSourceDebug())
                    {
                        string resolvedType = damageable.GetType().Name;
                        Debug.Log($"[WeaponProjectile] hit='{hitObject.name}' root='{hitObject.transform.root.name}' damageable='{((Component)damageable).name}' type={damageType} shield={shieldInteraction:F2} hull={hullInteraction:F2} resolvedType={resolvedType}", this);
                    }

                    damageable.ApplyDamage(packet);
                }
                else if (ShouldLogSourceDebug())
                {
                    Debug.Log($"[WeaponProjectile] hit='{hitObject.name}' but no IDamageable resolved in hierarchy. Damage skipped.", this);
                }
            }

            DestroyProjectileInstance();
        }

        private IDamageable ResolveDamageable(GameObject hitObject)
        {
            if (hitObject == null)
            {
                return null;
            }

            Transform root = hitObject.transform.root;

            // Prefer ship-level combat receiver when available to avoid child-collider IDamageable traps.
            CombatDamageable rootCombat = root != null ? root.GetComponentInChildren<CombatDamageable>(true) : null;

            CombatDamageable combat = hitObject.GetComponentInParent<CombatDamageable>(true);
            if (combat != null)
            {
                return combat;
            }

            if (rootCombat != null)
            {
                return rootCombat;
            }

            IDamageable d = hitObject.GetComponentInParent<IDamageable>();
            if (d != null)
            {
                return d;
            }

            Collider col = hitObject.GetComponent<Collider>();
            if (col != null && col.attachedRigidbody != null)
            {
                combat = col.attachedRigidbody.GetComponentInParent<CombatDamageable>(true);
                if (combat != null)
                {
                    return combat;
                }

                if (rootCombat != null)
                {
                    return rootCombat;
                }

                d = col.attachedRigidbody.GetComponentInParent<IDamageable>();
                if (d != null)
                {
                    return d;
                }
            }

            if (root != null)
            {
                d = root.GetComponentInChildren<IDamageable>(true);
                if (d != null)
                {
                    return d;
                }
            }

            return null;
        }

        private bool ShouldLogSourceDebug()
        {
            if (_source == null)
            {
                return false;
            }

            NPCShipAI npc = _source.GetComponentInParent<NPCShipAI>();
            return npc != null && npc.ShowDebugLogs;
        }

        private ImpactSurfaceType ResolveSurfaceType(GameObject hitObject)
        {
            if (hitObject == null)
            {
                return ImpactSurfaceType.Default;
            }

            ImpactSurfaceTag tag = hitObject.GetComponentInParent<ImpactSurfaceTag>();
            return tag != null ? tag.SurfaceType : ImpactSurfaceType.Default;
        }

        private bool TryResolveImpactPrefab(ImpactSurfaceType surface, out GameObject prefab, out float scale)
        {
            if (impactVfxLibrary != null && impactVfxLibrary.TryResolve(damageType, surface, out prefab, out scale))
            {
                return true;
            }

            prefab = hitImpactEffectPrefab;
            scale = hitImpactEffectScale > 0f ? hitImpactEffectScale : 1f;
            return prefab != null;
        }

        private void DestroyProjectileInstance()
        {
            if (_instanceRoot != null)
            {
                Destroy(_instanceRoot);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}
