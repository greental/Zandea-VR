using System;
using System.Collections.Generic;
using JunkerBody;
using KenshiVR.MeleeCombat;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Zandea.Opening.Capture
{
    /// <summary>Front wrist restraint. Only effective actions are gated; tracked poses are never written.</summary>
    public sealed class PlayerWristRestraints : MonoBehaviour
    {
        public VRBodyController body;
        public Material ropeMaterial;
        [Min(.005f)] public float ropeWidth = .012f;
        [Min(.1f)] public float maximumVisualWristSeparation = .65f;
        public bool IsRestrained { get; private set; }
        public event Action Applied;
        public event Action Removed;

        readonly Dictionary<Behaviour, bool> prior = new Dictionary<Behaviour, bool>();
        LineRenderer rope;
        Material ownedMaterial;
        PlayerKnockout deferredKnockout;

        void Awake()
        {
            if (!body) body = GetComponentInChildren<VRBodyController>(true);
            if (!body) body = GetComponentInParent<VRBodyController>();
        }

        public bool ApplyRestraints()
        {
            if (IsRestrained || deferredKnockout || !body || !body.xrOrigin ||
                !body.leftController || !body.rightController) return false;
            IsRestrained = true;
            prior.Clear();
            foreach (var hand in body.xrOrigin.GetComponentsInChildren<XRDirectInteractor>(true)) Gate(hand);
            foreach (var hand in body.GetComponentsInChildren<PlayerMartialHand>(true)) Gate(hand);
            CreateRope();
            Applied?.Invoke();
            return true;
        }

        public bool RemoveRestraints()
        {
            if (!IsRestrained) return false;
            IsRestrained = false;
            var knockout = GetComponentInParent<PlayerKnockout>();
            if (!knockout) knockout = GetComponentInChildren<PlayerKnockout>(true);
            if (knockout && knockout.State != PlayerConsciousness.Conscious)
            {
                deferredKnockout = knockout;
                deferredKnockout.Recovered += RestoreActions;
            }
            else RestoreActions();
            if (rope) Destroy(rope.gameObject);
            rope = null;
            Removed?.Invoke();
            return true;
        }

        void Gate(Behaviour component)
        {
            if (!component || prior.ContainsKey(component)) return;
            prior.Add(component, component.enabled);
            if (component.enabled) component.enabled = false;
        }

        void RestoreActions()
        {
            if (deferredKnockout) deferredKnockout.Recovered -= RestoreActions;
            deferredKnockout = null;
            foreach (var item in prior) if (item.Key && item.Value) item.Key.enabled = true;
            prior.Clear();
        }

        void CreateRope()
        {
            var visual = new GameObject("Front wrist rope (visual only)");
            visual.transform.SetParent(transform, false);
            rope = visual.AddComponent<LineRenderer>();
            rope.useWorldSpace = true;
            rope.positionCount = 3;
            rope.startWidth = rope.endWidth = ropeWidth;
            rope.numCapVertices = 4;
            rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rope.receiveShadows = false;
            if (!ropeMaterial)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader) ownedMaterial = new Material(shader) { color = new Color(.42f, .28f, .13f) };
            }
            rope.sharedMaterial = ropeMaterial ? ropeMaterial : ownedMaterial;
            if (!rope.sharedMaterial) Debug.LogWarning("Wrist rope material is missing.", this);
        }

        void LateUpdate()
        {
            if (!IsRestrained || !rope || !body) return;
            var left = body.leftOpticalWrist ? body.leftOpticalWrist.position : body.leftController.position;
            var right = body.rightOpticalWrist ? body.rightOpticalWrist.position : body.rightController.position;
            Vector3 midpoint = (left + right) * .5f;
            Vector3 half = Vector3.ClampMagnitude((right - left) * .5f, maximumVisualWristSeparation * .5f);
            rope.SetPosition(0, midpoint - half);
            rope.SetPosition(1, midpoint + Vector3.down * Mathf.Min(.08f, half.magnitude * .3f));
            rope.SetPosition(2, midpoint + half);
        }

        void OnDisable() => RemoveRestraints();
        void OnDestroy()
        {
            if (deferredKnockout) deferredKnockout.Recovered -= RestoreActions;
            if (ownedMaterial) Destroy(ownedMaterial);
        }
    }
}
