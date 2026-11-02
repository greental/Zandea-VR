using System;
using JunkerBody;
using KenshiVR.MeleeCombat;
using UnityEngine;

namespace Zandea.Opening.Capture
{
    /// <summary>Interruptible, reach-checked rope action with a temporary visible prop.</summary>
    public sealed class OpeningBindingAction : MonoBehaviour
    {
        public OpeningCaptureGroup capture;
        public PlayerWristRestraints restraints;
        public VRBodyController prisoner;
        public AnimationClip bindingClip;
        public string bindingTrigger = "Bind";
        [Min(.1f)] public float bindSeconds = 2f;
        [Min(.2f)] public float maximumHandDistance = 1.1f;
        public Material ropeMaterial;
        public bool IsBinding { get; private set; }
        public event Action Started, Restrained;
        public event Action<string> Interrupted;

        StarvingBandit captor;
        float completedAt;
        LineRenderer looseRope;
        Material ownedMaterial;
        bool subscribed;

        void OnEnable()
        {
            Subscribe();
        }

        void Start() => Subscribe();

        void Subscribe()
        {
            if (capture && !subscribed)
            {
                capture.CaptorChanged += OnCaptorChanged;
                capture.CaptureInterrupted += Interrupt;
                subscribed = true;
            }
        }

        void OnDisable()
        {
            if (capture && subscribed)
            {
                capture.CaptorChanged -= OnCaptorChanged;
                capture.CaptureInterrupted -= Interrupt;
                subscribed = false;
            }
            Interrupt("binding component disabled");
            if (ownedMaterial) Destroy(ownedMaterial);
        }

        public bool Begin()
        {
            if (IsBinding || !capture || !restraints || !prisoner || restraints.IsRestrained ||
                capture.State != CaptureGroupState.CaptorApproach || !capture.ComplianceHeld) return false;
            captor = capture.Captor;
            if (!CaptorReady() || !WithinReach()) return false;
            IsBinding = true;
            completedAt = Time.time + bindSeconds;
            captor.health?.onDamaged.AddListener(OnCaptorDamaged);
            capture.MarkBindingStarted();
            CreateLooseRope();
            var animator = captor.GetComponentInChildren<Animator>();
            if (animator && bindingClip && !string.IsNullOrEmpty(bindingTrigger)) animator.SetTrigger(bindingTrigger);
            else Debug.LogWarning("Opening binder has no authored binding animation; temporary rope/reach pose is active.", this);
            Started?.Invoke();
            return true;
        }

        public void Interrupt(string reason)
        {
            if (!IsBinding) return;
            IsBinding = false;
            if (captor && captor.health) captor.health.onDamaged.RemoveListener(OnCaptorDamaged);
            if (looseRope) Destroy(looseRope.gameObject);
            looseRope = null;
            Interrupted?.Invoke(reason);
            if (!capture || capture.State != CaptureGroupState.Binding) return;
            if (reason == "captor hit" || reason == "binding conditions lost")
                capture.NotifyAggression(prisoner ? prisoner.transform.position : transform.position);
            else if (reason == "restraint application failed" || reason == "binding component disabled")
                capture.Abort(reason);
        }

        void OnCaptorChanged(StarvingBandit next)
        {
            if (IsBinding && next != captor) Interrupt("captor unavailable");
        }

        void OnCaptorDamaged() => Interrupt("captor hit");

        void Update()
        {
            if (!IsBinding) return;
            if (!capture || capture.State != CaptureGroupState.Binding || !capture.ComplianceHeld ||
                !CaptorReady() || !WithinReach() || restraints.IsRestrained)
            { Interrupt("binding conditions lost"); return; }
            if (Time.time < completedAt) return;
            if (!restraints.ApplyRestraints()) { Interrupt("restraint application failed"); return; }
            IsBinding = false;
            if (captor && captor.health) captor.health.onDamaged.RemoveListener(OnCaptorDamaged);
            if (looseRope) Destroy(looseRope.gameObject);
            looseRope = null;
            Restrained?.Invoke();
        }

        void LateUpdate()
        {
            if (!IsBinding || !looseRope || !captor || !prisoner) return;
            Vector3 wrist = WristMidpoint();
            Vector3 hand = captor.transform.position + captor.transform.up * 1.25f + captor.transform.forward * .35f;
            looseRope.SetPosition(0, hand);
            looseRope.SetPosition(1, (hand + wrist) * .5f + Vector3.down * .12f);
            looseRope.SetPosition(2, wrist);
        }

        bool CaptorReady() => captor && captor.health &&
            captor.health.State == ScavengerNPC.NpcConsciousness.Conscious;

        bool WithinReach()
        {
            Vector3 hand = captor.transform.position + captor.transform.up * 1.25f + captor.transform.forward * .35f;
            return Vector3.Distance(hand, WristMidpoint()) <= maximumHandDistance;
        }

        Vector3 WristMidpoint()
        {
            var left = prisoner.leftOpticalWrist ? prisoner.leftOpticalWrist.position : prisoner.leftController.position;
            var right = prisoner.rightOpticalWrist ? prisoner.rightOpticalWrist.position : prisoner.rightController.position;
            return (left + right) * .5f;
        }

        void CreateLooseRope()
        {
            var prop = new GameObject("Binding rope (temporary)");
            prop.transform.SetParent(transform, false);
            looseRope = prop.AddComponent<LineRenderer>();
            looseRope.useWorldSpace = true;
            looseRope.positionCount = 3;
            looseRope.startWidth = looseRope.endWidth = .012f;
            looseRope.numCapVertices = 4;
            looseRope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            if (!ropeMaterial)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader) ownedMaterial = new Material(shader) { color = new Color(.42f, .28f, .13f) };
            }
            looseRope.sharedMaterial = ropeMaterial ? ropeMaterial : ownedMaterial;
            if (!looseRope.sharedMaterial) Debug.LogWarning("Binding rope material is missing.", this);
        }
    }
}
