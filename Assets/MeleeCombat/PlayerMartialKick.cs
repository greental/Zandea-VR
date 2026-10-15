using System.Collections.Generic;
using JunkerBody;
using ScavengerNPC;
using UnityEngine;
using UnityEngine.InputSystem;
using VRKoombar.WeaponsStaging.Contracts;

namespace KenshiVR.MeleeCombat
{
    // The controller's masked kick layer animates legs only. VRBodyController keeps
    // HMD and tracked hands authoritative and continues to own the XR origin.
    [DefaultExecutionOrder(10800)]
    public sealed class PlayerMartialKick : MonoBehaviour
    {
        public VRBodyController body;
        public AnimationClip frontClip, roundhouseClip;
        public InputActionProperty frontInput = new InputActionProperty(
            new InputAction("Front Kick", InputActionType.Button, "<XRController>{LeftHand}/primaryButton"));
        public InputActionProperty roundhouseInput = new InputActionProperty(
            new InputAction("Roundhouse Kick", InputActionType.Button, "<XRController>{RightHand}/primaryButton"));
        [Min(.1f)] public float cooldown = .85f;
        [Min(.03f)] public float footRadius = .12f;
        public bool IsKicking => activeClip && Time.time < endTime;
        readonly Collider[] contacts = new Collider[24];
        readonly HashSet<ScavengerHealth> scored = new HashSet<ScavengerHealth>();
        AnimationClip activeClip;
        Transform foot;
        Vector3 previousFoot;
        float startTime, endTime, nextKick;
        int layer = -1;
        bool ownFront, ownRoundhouse;

        void OnEnable()
        {
            if (!body) body = GetComponent<VRBodyController>();
            if (frontInput.action == null)
                frontInput = new InputActionProperty(new InputAction("Front Kick", InputActionType.Button,
                    "<XRController>{LeftHand}/primaryButton"));
            if (roundhouseInput.action == null)
                roundhouseInput = new InputActionProperty(new InputAction("Roundhouse Kick", InputActionType.Button,
                    "<XRController>{RightHand}/primaryButton"));
            var front = frontInput.action;
            var roundhouse = roundhouseInput.action;
            ownFront = front != null && !front.enabled;
            ownRoundhouse = roundhouse != null && !roundhouse.enabled;
            if (ownFront) front.Enable();
            if (ownRoundhouse) roundhouse.Enable();
        }

        void OnDisable()
        {
            if (body && body.animator && layer >= 0) body.animator.SetLayerWeight(layer, 0f);
            if (ownFront) frontInput.action?.Disable();
            if (ownRoundhouse) roundhouseInput.action?.Disable();
        }

        void Update()
        {
            if (!body || !body.animator) return;
            if (layer < 0) layer = body.animator.GetLayerIndex("Martial Kicks");
            if (layer < 0) return;
            if (activeClip && Time.time >= endTime)
            {
                activeClip = null;
                scored.Clear();
                body.animator.SetLayerWeight(layer, 0f);
            }
            if (activeClip || Time.time < nextKick) return;
            if (frontClip && (frontInput.action?.WasPressedThisFrame() ?? false)) StartKick(frontClip, "FrontKick");
            else if (roundhouseClip && (roundhouseInput.action?.WasPressedThisFrame() ?? false))
                StartKick(roundhouseClip, "RoundhouseKick");
        }

        void StartKick(AnimationClip clip, string state)
        {
            activeClip = clip;
            startTime = Time.time;
            endTime = Time.time + clip.length;
            nextKick = endTime + cooldown;
            scored.Clear();
            foot = body.animator.GetBoneTransform(HumanBodyBones.RightFoot);
            if (!foot) { activeClip = null; return; }
            previousFoot = foot.position;
            body.animator.SetLayerWeight(layer, 1f);
            body.animator.CrossFadeInFixedTime("Martial Kicks." + state, .07f, layer, 0f);
        }

        void LateUpdate()
        {
            if (!IsKicking || !foot || !body) return;
            Vector3 current = foot.position;
            float progress = Mathf.InverseLerp(startTime, endTime, Time.time);
            if (progress >= .3f && progress <= .68f)
            {
                float speed = Vector3.Distance(previousFoot, current) / Mathf.Max(Time.deltaTime, .0001f);
                if (speed >= .65f) Score(previousFoot, current, speed);
            }
            previousFoot = current;
        }

        void Score(Vector3 from, Vector3 to, float speed)
        {
            int count = Physics.OverlapCapsuleNonAlloc(from, to, footRadius, contacts, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var shape = contacts[i];
                if (!shape || shape.transform.IsChildOf(body.xrOrigin)) continue;
                var target = shape.GetComponentInParent<ScavengerHealth>();
                if (!target || target.IsDead || scored.Contains(target)) continue;
                Vector3 travel = to - from;
                Vector3 center = from + travel * Mathf.Clamp01(Vector3.Dot(shape.bounds.center - from, travel) /
                    Mathf.Max(travel.sqrMagnitude, .000001f));
                Vector3 point = shape.ClosestPoint(center);
                if (Vector3.Distance(point, center) > footRadius + .02f) continue;
                scored.Add(target);
                target.ApplyDamage(new DamagePacket {
                    BaseDamage = Mathf.Clamp(8f + speed * 2f, 8f, 22f) *
                        CharacterCombatMath.DamageDealt(body.GetComponent<CharacterAttributes>(),
                            PhysicalMeleeWeapon.WeaponProficiencyClass.None),
                    DamageType = DamageType.Kinetic, HitPointWorld = point,
                    HitNormalWorld = (point - from).normalized, Source = body.gameObject,
                    WeaponId = activeClip.name, Timestamp = Time.time
                });
                var martial = target.GetComponent<NpcMartialArts>();
                if (martial) martial.ReceiveStrikeStagger(1.4f);
            }
        }
    }
}
