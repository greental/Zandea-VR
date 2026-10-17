using System.Collections;
using JunkerEquipment;
using ScavengerNPC;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace KenshiVR.Bandaging
{
    [RequireComponent(typeof(XRGrabInteractable), typeof(Rigidbody))]
    public sealed class BandageRoll : MonoBehaviour
    {
        public enum WrapState { Idle, Ready, Wrapping, Complete }
        public Transform looseTip;
        public Material clothMaterial;
        [Min(.015f)] public float clothWidth = .055f;
        [Min(.001f)] public float clothThickness = .002f;
        [Min(2)] public int samplesPerTurn = 40;
        [Min(1)] public int requiredTurns = 3;
        [Range(5f, 60f)] public float directionLockDegrees = 24f;
        [Range(10f, 120f)] public float maximumStepDegrees = 65f;
        [Min(.05f)] public float maximumStepMeters = .22f;
        [Min(.05f)] public float cancellationDistance = .6f;
        public WrapState State { get; private set; }
        public float CurrentAngle => signedAngle;
        public float CompletedRotations => Mathf.Abs(signedAngle) / 360f;
        public WrapRegion ActiveRegion => zone ? zone.region : default;
        public bool SnapEligible => candidate != null;
        XRGrabInteractable grab;
        JunkerEquipmentItem equipment;
        PlayerWounds wounds;
        ScavengerPlayerHealth health;
        WoundWrapZone zone;
        PlayerWounds.Wound candidate, target;
        XRBaseInputInteractor hand;
        Transform trackingPoint;
        BandageRibbon ribbon;
        Vector3 previousPoint;
        float previousAngle, signedAngle, startAngle, axialCenter;
        int direction;
        bool haveSample;

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            equipment = GetComponent<JunkerEquipmentItem>();
            if (!looseTip) looseTip = transform;
            grab.selectEntered.AddListener(OnGrab);
            grab.selectExited.AddListener(OnDrop);
            grab.activated.AddListener(OnTrigger);
        }
        void OnDestroy()
        {
            if (grab)
            {
                grab.selectEntered.RemoveListener(OnGrab);
                grab.selectExited.RemoveListener(OnDrop);
                grab.activated.RemoveListener(OnTrigger);
            }
            if (health) health.onDirectHit -= Cancel;
            if (State == WrapState.Wrapping && ribbon) Destroy(ribbon.gameObject);
        }
        void OnGrab(SelectEnterEventArgs args)
        {
            hand = args.interactorObject as XRBaseInputInteractor;
            trackingPoint = hand ? hand.transform : args.interactorObject.transform;
            wounds = trackingPoint ? trackingPoint.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>()?.GetComponentInChildren<PlayerWounds>(true) : null;
            if (!wounds) wounds = FindFirstObjectByType<PlayerWounds>();
            health = wounds ? wounds.GetComponent<ScavengerPlayerHealth>() : null;
            if (health) health.onDirectHit += Cancel;
            State = WrapState.Ready;
        }
        void OnDrop(SelectExitEventArgs args)
        {
            Cancel();
            if (health) health.onDirectHit -= Cancel;
            health = null; hand = null; trackingPoint = null; wounds = null;
            State = WrapState.Idle;
        }
        void OnTrigger(ActivateEventArgs args)
        {
            if (State != WrapState.Ready || !grab.isSelected || candidate == null || zone == null) return;
            target = candidate;
            axialCenter = Mathf.Clamp(target.localPosition.y, -zone.length * .5f + clothWidth, zone.length * .5f - clothWidth);
            startAngle = Angle(trackingPoint.position);
            previousAngle = startAngle;
            previousPoint = trackingPoint.position;
            signedAngle = 0f; direction = 0; haveSample = true;
            var go = new GameObject("Bandage wrap");
            go.transform.SetParent(zone.bone, false);
            ribbon = go.AddComponent<BandageRibbon>();
            ribbon.Initialize(zone, clothMaterial, clothWidth, clothThickness, samplesPerTurn, requiredTurns,
                startAngle, axialCenter);
            wounds.ShowCue(null);
            candidate = null;
            State = WrapState.Wrapping;
            if (equipment) equipment.Pulse(.17f, .035f);
        }
        void LateUpdate()
        {
            if (!grab || !grab.isSelected || !wounds || !trackingPoint) return;
            if (State == WrapState.Ready)
            {
                candidate = wounds.ClosestUntreated(looseTip.position, out zone);
                wounds.ShowCue(candidate);
                return;
            }
            if (State != WrapState.Wrapping || !zone || !ribbon) return;
            Vector3 point = trackingPoint.position;
            Vector3 local = zone.ToFrame(point);
            float radial = new Vector2(local.x, local.z).magnitude;
            if (Vector3.Distance(point, zone.ToWorld(new Vector3(0f, axialCenter, 0f))) > cancellationDistance)
            { Cancel(); return; }
            bool valid = Mathf.Abs(local.y - axialCenter) <= zone.length * .5f + zone.axialTolerance &&
                Mathf.Abs(radial - zone.RadiusAt(local.y)) <= zone.radialTolerance;
            float angle = Angle(point);
            if (!valid)
            {
                haveSample = false;
                ribbon.UpdateRibbon(signedAngle, looseTip.position);
                return;
            }
            if (!haveSample)
            {
                previousAngle = angle;
                previousPoint = point;
                haveSample = true;
                ribbon.UpdateRibbon(signedAngle, looseTip.position);
                return;
            }
            float delta = Mathf.DeltaAngle(previousAngle, angle);
            float travel = Vector3.Distance(previousPoint, point);
            previousAngle = angle; previousPoint = point;
            if (Mathf.Abs(delta) > maximumStepDegrees || travel > maximumStepMeters)
            { haveSample = false; return; }
            // Rotating the wounded limb under a stationary hand is not a wrap.
            if (travel < zone.RadiusAt(local.y) * Mathf.Abs(delta) * Mathf.Deg2Rad * .35f)
            { ribbon.UpdateRibbon(signedAngle, looseTip.position); return; }
            if (direction == 0)
            {
                signedAngle += delta;
                if (Mathf.Abs(signedAngle) >= directionLockDegrees) direction = signedAngle > 0f ? 1 : -1;
            }
            else signedAngle = direction * Mathf.Max(0f, direction * signedAngle + direction * delta);
            ribbon.UpdateRibbon(signedAngle, looseTip.position);
            if (Mathf.Abs(signedAngle) >= requiredTurns * 360f - 1f) Complete();
        }
        float Angle(Vector3 point)
        {
            var p = zone.ToFrame(point);
            return Mathf.Atan2(p.z, p.x) * Mathf.Rad2Deg;
        }
        void Complete()
        {
            if (State != WrapState.Wrapping) return;
            State = WrapState.Complete;
            ribbon.UpdateRibbon(direction * requiredTurns * 360f, looseTip.position, true);
            wounds.TreatArea(zone, axialCenter, clothWidth * (.5f + .41f * requiredTurns), ribbon.transform);
            if (equipment) equipment.Pulse(.3f, .07f);
            ribbon = null;
            wounds.ShowCue(null);
            StartCoroutine(ConsumeAfterSelection());
        }
        IEnumerator ConsumeAfterSelection()
        {
            if (grab && grab.isSelected && grab.interactionManager)
                grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
            yield return null;
            Destroy(gameObject);
        }
        public void Cancel()
        {
            if (State != WrapState.Wrapping) return;
            if (ribbon) Destroy(ribbon.gameObject);
            ribbon = null; target = null; zone = null; candidate = null;
            signedAngle = 0f; direction = 0; haveSample = false;
            State = grab && grab.isSelected ? WrapState.Ready : WrapState.Idle;
            if (wounds) wounds.ShowCue(null);
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void OnGUI()
        {
            if (State == WrapState.Idle) return;
            GUI.Label(new Rect(12, 78, 450, 22), $"Bandage {State} | {ActiveRegion} | snap {SnapEligible} | {CurrentAngle:F0}° | {CompletedRotations:F2} turns");
        }
#endif
    }
}
