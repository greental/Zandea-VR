using System;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;
using KenshiVR.MeleeCombat;

namespace Zandea.Props
{
    [RequireComponent(typeof(Rigidbody))]
    public sealed class BreakableProp : MonoBehaviour, IDamageable
    {
        [Min(.1f)] public float minimumImpactSpeed = 3f;
        [Min(.1f)] public float collisionEnergyThreshold = 9f;
        [Min(.1f)] public float meleeDamageThreshold = 5f;
        [Tooltip("Only a scored weapon hit can break this prop; body and terrain collisions are ignored.")]
        public bool requireWeaponDamage;
        public GameObject fracturedPrefab;
        public bool IsBroken { get; private set; }
        public event Action<BreakableProp> Broken;
        Rigidbody body;
        void Awake() => body = GetComponent<Rigidbody>();
        void OnCollisionEnter(Collision collision)
        {
            if (IsBroken || requireWeaponDamage || collision.collider.GetComponentInParent<PhysicalMeleeWeapon>()) return;
            float speed = collision.relativeVelocity.magnitude;
            if (speed < minimumImpactSpeed) return;
            float energy = .5f * body.mass * speed * speed;
            if (energy >= collisionEnergyThreshold)
                Break(collision.contactCount > 0 ? collision.GetContact(0).point : transform.position,
                    collision.relativeVelocity.normalized);
        }
        public void ApplyDamage(in DamagePacket packet)
        {
            if (!IsBroken && packet.BaseDamage >= meleeDamageThreshold &&
                (!requireWeaponDamage || !string.IsNullOrEmpty(packet.WeaponId)))
                Break(packet.HitPointWorld, packet.HitNormalWorld);
        }
        public ShipCombatState GetCombatState() => IsBroken ? ShipCombatState.Destroyed : ShipCombatState.CombatReady;
        public void Break(Vector3 point, Vector3 direction)
        {
            if (IsBroken) return;
            IsBroken = true;
            Broken?.Invoke(this);
            if (fracturedPrefab)
            {
                var debris = Instantiate(fracturedPrefab, transform.position, transform.rotation);
                debris.transform.localScale = transform.lossyScale;
                foreach (var shard in debris.GetComponentsInChildren<Rigidbody>())
                {
                    shard.linearVelocity = body.linearVelocity;
                    shard.angularVelocity = body.angularVelocity;
                    shard.AddForceAtPosition(direction * Mathf.Min(2f, body.mass), point, ForceMode.Impulse);
                }
                var lifetime = debris.GetComponent<PropDebrisLifetime>();
                if (!lifetime) lifetime = debris.AddComponent<PropDebrisLifetime>();
                lifetime.BeginRuntimeBurst();
            }
            foreach (var renderer in GetComponentsInChildren<Renderer>()) renderer.enabled = false;
            foreach (var collider in GetComponentsInChildren<Collider>()) collider.enabled = false;
            var grab = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            if (grab) grab.enabled = false;
            body.isKinematic = true;
        }
    }

}
