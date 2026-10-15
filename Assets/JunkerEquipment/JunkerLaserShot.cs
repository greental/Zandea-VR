using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;

namespace JunkerEquipment
{
    // Swept projectile: damage and impact effects occur only when the visible bolt arrives.
    [RequireComponent(typeof(LineRenderer))]
    public sealed class JunkerLaserShot : MonoBehaviour
    {
        public Vector3 StartPoint { get; private set; }
        public Vector3 EndPoint { get; private set; }
        public bool HasImpacted { get; private set; }
        public float Speed { get; private set; }
        public float Length { get; private set; }
        LineRenderer line;
        Vector3 direction;
        float traveled, maxRange, damage, impactImpulse, scorchSize;
        LayerMask hitMask;
        Transform ignoredRig, weapon;
        GameObject source;
        JunkerPistol pistol;
        static Mesh scorchMesh;
        static Material scorchMaterial;

        public static JunkerLaserShot Spawn(Vector3 start, Vector3 forward, float range, Material material,
            float speed, float length, float width, LayerMask mask, Transform ignoredRig,
            Transform weapon, GameObject source, JunkerPistol pistol, float damage, float impulse, float scorchSize)
        {
            if (forward.sqrMagnitude < .0001f) return null;
            var obj = new GameObject("JKR laser shot");
            var shot = obj.AddComponent<JunkerLaserShot>();
            shot.StartPoint = start;
            shot.direction = forward.normalized;
            shot.maxRange = Mathf.Max(.01f, range);
            shot.EndPoint = start + shot.direction * shot.maxRange;
            shot.Speed = Mathf.Max(1f, speed);
            shot.Length = Mathf.Max(.02f, length);
            shot.damage = damage;
            shot.impactImpulse = impulse;
            shot.scorchSize = scorchSize;
            shot.hitMask = mask;
            shot.ignoredRig = ignoredRig;
            shot.weapon = weapon;
            shot.source = source;
            shot.pistol = pistol;
            shot.line = obj.GetComponent<LineRenderer>();
            shot.line.enabled = material;
            if (material) shot.line.sharedMaterial = material;
            shot.line.useWorldSpace = true;
            shot.line.positionCount = 2;
            shot.line.startWidth = shot.line.endWidth = Mathf.Max(.001f, width);
            shot.line.numCapVertices = 4;
            shot.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shot.line.receiveShadows = false;
            shot.line.SetPosition(0, start);
            shot.line.SetPosition(1, start + shot.direction * .025f);
            return shot;
        }

        void Update()
        {
            if (HasImpacted) return;
            float step = Mathf.Min(Speed * Time.deltaTime, maxRange - traveled);
            if (step <= 0f) { Destroy(gameObject); return; }
            Vector3 from = StartPoint + direction * traveled;
            RaycastHit closest = default;
            float nearest = step;
            foreach (var candidate in Physics.RaycastAll(from, direction, step, hitMask, QueryTriggerInteraction.Ignore))
            {
                if ((weapon && candidate.transform.IsChildOf(weapon)) ||
                    (ignoredRig && candidate.transform.IsChildOf(ignoredRig)) || candidate.distance > nearest) continue;
                closest = candidate;
                nearest = candidate.distance;
            }
            traveled += closest.collider ? nearest : step;
            float tail = Mathf.Max(0f, traveled - Length);
            line.SetPosition(0, StartPoint + direction * tail);
            line.SetPosition(1, StartPoint + direction * traveled);
            if (closest.collider)
            {
                HasImpacted = true;
                EndPoint = closest.point;
                Impact(closest);
                Destroy(gameObject, .07f);
            }
            else if (traveled >= maxRange) Destroy(gameObject, Length / Speed);
        }

        void Impact(RaycastHit hit)
        {
            if (pistol) pistol.NotifyProjectileImpact();
            var receiver = hit.collider.GetComponentInParent<IDamageable>();
            if (receiver != null)
                receiver.ApplyDamage(new DamagePacket { BaseDamage = damage, DamageType = DamageType.Kinetic,
                    HitPointWorld = hit.point, HitNormalWorld = hit.normal, Source = source,
                    WeaponId = pistol ? pistol.name : "Laser", Timestamp = Time.time });
            else
            {
                var target = hit.collider.GetComponentInParent<JunkerTarget>();
                if (target) target.ApplyDamage(damage);
            }
            if (hit.rigidbody && !hit.rigidbody.isKinematic)
                hit.rigidbody.AddForceAtPosition(direction * impactImpulse, hit.point, ForceMode.Impulse);
            SpawnScorch(hit.point, hit.normal, hit.transform, Mathf.Max(.005f, scorchSize));
        }

        static void SpawnScorch(Vector3 point, Vector3 normal, Transform surface, float size)
        {
            if (!scorchMesh)
            {
                const int sides = 12;
                var vertices = new Vector3[sides + 1];
                var triangles = new int[sides * 3];
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2f / sides;
                    vertices[i + 1] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                    triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = (i + 1) % sides + 1;
                }
                scorchMesh = new Mesh { vertices = vertices, triangles = triangles };
                scorchMesh.RecalculateNormals();
            }
            if (!scorchMaterial)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (!shader) shader = Shader.Find("Unlit/Color");
                if (!shader) return;
                scorchMaterial = new Material(shader);
                if (scorchMaterial.HasProperty("_BaseColor")) scorchMaterial.SetColor("_BaseColor", new Color(.065f, .025f, .018f));
                else scorchMaterial.color = new Color(.065f, .025f, .018f);
            }
            var mark = new GameObject("Laser scorch");
            mark.transform.SetPositionAndRotation(point + normal * .003f, Quaternion.FromToRotation(Vector3.forward, normal));
            mark.transform.localScale = Vector3.one * size;
            mark.AddComponent<MeshFilter>().sharedMesh = scorchMesh;
            mark.AddComponent<MeshRenderer>().sharedMaterial = scorchMaterial;
            if (surface) mark.transform.SetParent(surface, true);
            Destroy(mark, 90f);
        }
    }
}
