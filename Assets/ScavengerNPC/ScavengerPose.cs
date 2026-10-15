using UnityEngine;
using JunkerEquipment;

namespace ScavengerNPC
{
    [ExecuteAlways, DefaultExecutionOrder(100)]
    public sealed class ScavengerPose : MonoBehaviour
    {
        public ScavengerEnemy enemy;
        public Transform hips, head, rightUpper, rightLower, rightHand, leftUpper, leftLower, leftHand;
        public Transform rightThigh, rightShin, rightFoot, leftThigh, leftShin, leftFoot;
        public Transform gun;
        [Header("Hit reaction")]
        [Tooltip("Upper chest bone. Found by name at runtime for older prefabs if left empty.")]
        public Transform hitSpine;
        [Min(.05f)] public float hitDuration = .38f;
        [Range(0, 30)] public float hitBendDegrees = 14f;
        [Range(0, 30)] public float hitTwistDegrees = 10f;
        public float HitReactionWeight { get; private set; }
        float hitStarted = -100f, hitStrength = 1f, hitSide;
        [Header("Gun grip on right hand")]
        [Tooltip("Move the gun in the right hand's local coordinates, including in Edit mode.")]
        public Vector3 gunGripPosition = new Vector3(0, .04f, -.03f);
        [Tooltip("Rotate the gun in the right hand's local coordinates, including in Edit mode.")]
        public Vector3 gunGripEuler = new Vector3(-90, 0, 0);
        [Header("Edit mode aim preview")]
        public bool previewAimInEditor;
        public Vector3 previewAimPointLocal = new Vector3(0, 1.3f, 5);
        public Vector3 gripOffset = new Vector3(0, .03f, .04f);
        public Quaternion handGripRotation = Quaternion.identity;
        public Quaternion leftGripRotation = Quaternion.identity;
        public Vector3 gunRestOffset;
        public Quaternion gunRestRotation;
        public float CrouchAmount { get; private set; }
        bool wasPreviewing;
        void LateUpdate()
        {
            if (!enemy || !rightHand) return;
            ResolveGun();
            if (!Application.isPlaying)
            {
                if (previewAimInEditor)
                {
                    var clips = enemy.animator && enemy.animator.runtimeAnimatorController ? enemy.animator.runtimeAnimatorController.animationClips : null;
                    if (clips != null)
                        foreach (var clip in clips) if (clip && clip.name == "Idle") { clip.SampleAnimation(enemy.animator.gameObject, 0); break; }
                    ApplyAim(transform.TransformPoint(previewAimPointLocal));
                    wasPreviewing = true;
                }
                else if (wasPreviewing)
                {
                    if (enemy.animator) enemy.animator.Rebind();
                    wasPreviewing = false;
                }
                PlaceGun();
                return;
            }
            if (enemy.CurrentAction == ScavengerEnemy.Action.Dead) return;
            CrouchAmount = Mathf.MoveTowards(CrouchAmount, enemy.IsCrouching ? 1 : 0, Time.deltaTime * 4);
            if (CrouchAmount > 0 && (enemy.IsCombat || enemy.CurrentAction == ScavengerEnemy.Action.Squatting))
                Crouch(CrouchAmount * enemy.CrouchDepth);
            ApplyHitReaction();
            if (enemy.CurrentAction == ScavengerEnemy.Action.Sitting &&
                (!enemy.seat || Vector3.Distance(enemy.transform.position, enemy.seat.position) < .35f))
            {
                hips.position -= enemy.transform.up * .43f;
                SitLeg(rightThigh, rightShin, rightFoot); SitLeg(leftThigh, leftShin, leftFoot);
            }
            if (!enemy.IsCombat)
            {
                PlaceGun();
                return;
            }
            ApplyAim(enemy.AimPoint);
            PlaceGun();
        }
        void ResolveGun()
        {
            var pistol = GetComponentInChildren<JunkerPistol>(true);
            if (pistol && gun != pistol.transform) gun = pistol.transform;
        }
        void PlaceGun()
        {
            if (!gun || !rightHand) return;
            gun.SetPositionAndRotation(rightHand.TransformPoint(gunGripPosition), rightHand.rotation * Quaternion.Euler(gunGripEuler));
        }
        public void PlayHitReaction(Vector3 hitNormal, float damage)
        {
            if (!Application.isPlaying || !enemy || enemy.CurrentAction == ScavengerEnemy.Action.Dead) return;
            if (!hitSpine && hips)
                foreach (var bone in hips.GetComponentsInChildren<Transform>(true))
                    if (bone.name == "mixamorig:Spine2") { hitSpine = bone; break; }
            if (!hitSpine) return;
            // The contact normal points out of the surface; use it to turn away from the hit.
            Vector3 local = transform.InverseTransformDirection(hitNormal.sqrMagnitude > .001f ? hitNormal.normalized : transform.forward);
            hitSide = Mathf.Abs(local.x) > .1f ? Mathf.Sign(local.x) : (Random.value < .5f ? -1f : 1f);
            hitStrength = Mathf.Clamp(damage / 25f, .5f, 1.5f);
            hitStarted = Time.time;
            HitReactionWeight = hitStrength;
        }
        void ApplyHitReaction()
        {
            if (!hitSpine) return;
            float progress = (Time.time - hitStarted) / Mathf.Max(.05f, hitDuration);
            HitReactionWeight = progress >= 0 && progress < 1 ? Mathf.Sin(progress * Mathf.PI) * hitStrength : 0;
            if (HitReactionWeight <= 0) return;
            // This runs after the Animator and before the arm IK, so locomotion and aiming continue.
            hitSpine.localRotation *= Quaternion.Euler(-hitBendDegrees * HitReactionWeight,
                hitSide * hitTwistDegrees * HitReactionWeight, hitSide * hitBendDegrees * .5f * HitReactionWeight);
        }
        void ApplyAim(Vector3 point)
        {
            if (!rightUpper || (point - rightUpper.position).sqrMagnitude < .001f) return;
            Vector3 direction = (point - rightUpper.position).normalized;
            Quaternion aim = Quaternion.LookRotation(direction, enemy.transform.up);
            Vector3 handTarget = rightUpper.position + direction * .40f + enemy.transform.right * .06f;
            Solve(rightUpper, rightLower, rightHand, handTarget, -enemy.transform.up + enemy.transform.right * .5f);
            rightHand.rotation = aim * handGripRotation;
            if (gun)
            {
                Solve(leftUpper, leftLower, leftHand, rightHand.position - aim * Vector3.right * .07f, -enemy.transform.up - enemy.transform.right);
                leftHand.rotation = aim * leftGripRotation;
            }
            if (head) head.rotation = Quaternion.RotateTowards(head.rotation, aim, 35);
        }
        void Crouch(float depth)
        {
            if (!hips) return;
            Vector3 rightFootTarget = rightFoot ? rightFoot.position : Vector3.zero;
            Vector3 leftFootTarget = leftFoot ? leftFoot.position : Vector3.zero;
            hips.position -= enemy.transform.up * depth;
            Solve(rightThigh, rightShin, rightFoot, rightFootTarget, enemy.transform.forward);
            Solve(leftThigh, leftShin, leftFoot, leftFootTarget, enemy.transform.forward);
        }
        void SitLeg(Transform upper, Transform lower, Transform tip)
        {
            if (!upper || !lower || !tip) return;
            upper.rotation = Quaternion.FromToRotation(lower.position - upper.position, enemy.transform.forward) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(tip.position - lower.position, -enemy.transform.up) * lower.rotation;
        }
        public static void Solve(Transform upper, Transform lower, Transform tip, Vector3 target, Vector3 bend)
        {
            if (!upper || !lower || !tip) return;
            Vector3 origin = upper.position, delta = target - origin;
            float a = Vector3.Distance(origin, lower.position), b = Vector3.Distance(lower.position, tip.position);
            if (a < .001f || b < .001f || delta.sqrMagnitude < .000001f) return;
            float distance = Mathf.Clamp(delta.magnitude, Mathf.Abs(a - b) + .001f, a + b - .001f);
            Vector3 direction = delta.normalized;
            Vector3 pole = Vector3.ProjectOnPlane(bend, direction).normalized;
            if (pole.sqrMagnitude < .01f) pole = Vector3.Cross(direction, Vector3.right).normalized;
            float along = (a * a - b * b + distance * distance) / (2 * distance);
            Vector3 elbow = origin + direction * along + pole * Mathf.Sqrt(Mathf.Max(0, a * a - along * along));
            upper.rotation = Quaternion.FromToRotation(lower.position - origin, elbow - origin) * upper.rotation;
            lower.rotation = Quaternion.FromToRotation(tip.position - lower.position, origin + direction * distance - lower.position) * lower.rotation;
        }
    }
}
