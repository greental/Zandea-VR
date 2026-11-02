using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Zandea.FriendlyNPC
{
    // One detector per player prevents nearby NPCs from all accepting the same wave.
    public sealed class FriendlyNpcWaveInitiator : MonoBehaviour
    {
        [Min(.06f)] public float minimumSwing = .12f;
        [Min(.3f)] public float waveWindowSeconds = 1.5f;
        [Min(.3f)] public float cooldownSeconds = 1.8f;
        [Tooltip("Yaw correction for the authored palm visual, clockwise when viewed from above.")]
        public float palmFacingYawDegrees = 270f;
        Transform viewer, leftPalm, rightPalm;
        XRDirectInteractor leftHand, rightHand;
        readonly WaveState left = new(), right = new();
        float nextAcceptedAt;

        sealed class WaveState
        {
            public bool initialized;
            public float lastX, swingStartX, firstReversalAt;
            public int direction, reversals;
            public void Reset() { initialized = false; direction = reversals = 0; }
        }

        public void Configure(Transform head, XRDirectInteractor leftInteractor,
            XRDirectInteractor rightInteractor, Transform leftPalmVisual = null,
            Transform rightPalmVisual = null)
        {
            viewer = head;
            leftHand = leftInteractor;
            rightHand = rightInteractor;
            leftPalm = leftPalmVisual;
            rightPalm = rightPalmVisual;
            left.Reset(); right.Reset();
        }

        void Update()
        {
            if (!viewer || Time.time < nextAcceptedAt) return;
            bool waved = Sample(leftHand, leftPalm, left) | Sample(rightHand, rightPalm, right);
            if (!waved) return;
            left.Reset(); right.Reset();
            if (FriendlyNpcInteraction.TryBeginBestWave(viewer))
                nextAcceptedAt = Time.time + cooldownSeconds;
        }

        public static bool PalmFacesTarget(Transform palm, Vector3 targetDirection,
            float yawDegrees = 270f)
        {
            return palm && Vector3.Dot(Quaternion.Euler(0f, yawDegrees, 0f) * -palm.up,
                targetDirection.normalized) > .35f;
        }

        bool Sample(XRDirectInteractor hand, Transform palm, WaveState state)
        {
            if (!hand || !hand.gameObject.activeInHierarchy || hand.hasSelection ||
                hand.isPerformingManualInteraction || FriendlyInputReservation.IsReserved(hand))
            { state.Reset(); return false; }
            Vector3 local = viewer.InverseTransformPoint(hand.transform.position);
            // A deliberate wave is in view near shoulder height, with a sideways
            // out-and-back motion. Player locomotion alone is cancelled in head space.
            if (local.z < -.12f || local.z > 1.05f || local.y > .08f || local.y < -.9f ||
                Mathf.Abs(local.x) > 1.05f)
            { state.Reset(); return false; }
            if (!state.initialized)
            {
                state.initialized = true;
                state.lastX = state.swingStartX = local.x;
                return false;
            }
            float delta = local.x - state.lastX;
            state.lastX = local.x;
            if (Mathf.Abs(delta) < .012f) return false;
            int direction = delta > 0f ? 1 : -1;
            if (state.direction == 0) { state.direction = direction; state.swingStartX = local.x; return false; }
            if (direction == state.direction) return false;
            if (Mathf.Abs(local.x - state.swingStartX) < minimumSwing) return false;
            if (state.reversals == 0) state.firstReversalAt = Time.time;
            else if (Time.time - state.firstReversalAt > waveWindowSeconds)
            { state.reversals = 0; state.firstReversalAt = Time.time; }
            state.reversals++;
            state.direction = direction;
            state.swingStartX = local.x;
            // One complete sideways change of direction is a wave. Each hand
            // has its own state, so either controller can initiate dialogue.
            return state.reversals >= 1;
        }
    }
}
