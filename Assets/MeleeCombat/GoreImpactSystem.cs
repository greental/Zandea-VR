using System.Collections.Generic;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;
using ScavengerNPC;

namespace KenshiVR.MeleeCombat
{
    // Lives on the NPC root. All damage still goes through ScavengerHealth.
    public sealed class GoreImpactSystem : MonoBehaviour
    {
        [Header("Blood")]
        public bool enableGore = true;
        [Min(0f)] public float bloodIntensityMultiplier = 1f;
        [Min(0)] public int maximumSurfaceMarks = 80;
        [Min(.01f)] public float markLifetime = 180f;
        [Min(0f)] public float minorBleedDamagePerSecond = .025f;
        [Min(0f)] public float severeBleedDamagePerSecond = .5f;
        [Min(0f)] public float minorBleedSeconds = 8f;
        [Min(0f)] public float severeBleedSeconds = 35f;
        [Min(0f)] public float severeHitDamage = 18f;
        [Min(0f)] public float trailDistance = .25f;
        [Min(0f)] public float bloodPoolRadius = .7f;
        [Min(.1f)] public float bloodPoolGrowSeconds = 9f;
        public ParticleSystem sprayPrefab;
        public Material bloodMarkMaterial;
        public LayerMask surfaceLayers = ~0;

        [Min(1)] public int maximumActiveSprays = 6;
        [Min(0)] public int maximumDropletsPerHit = 36;
        readonly List<GameObject> marks = new List<GameObject>();
        readonly List<float> markExpiry = new List<float>();
        readonly List<ParticleSystem> sprays = new List<ParticleSystem>();
        int nextMark, nextSpray;
        static Material defaultBloodMaterial;
        static Material defaultMarkMaterial;
        static Texture2D splatterTexture;
        static readonly RaycastHit[] surfaceHits = new RaycastHit[32];
        const int GlobalSurfaceMarkLimit = 256;
        const int GlobalSprayLimit = 24;
        const int GlobalPoolLimit = 32;
        static int globalSurfaceMarks, globalSprays, globalPools;
        ScavengerHealth health;
        float bleedPerSecond, bleedUntil, nextBleedTick;
        float nextMarkExpiryCheck;
        int bleedCount;
        Vector3 lastTrailPosition;
        GameObject bloodPool;
        bool poolAllocated;

        void Awake()
        {
            health = GetComponent<ScavengerHealth>();
            if (health) health.onDeath.AddListener(StartBloodPool);
            lastTrailPosition = transform.position;
        }

        public void RegisterImpact(in DamagePacket packet, Vector3 slashDirection, float effectiveDamage)
        {
            if (!enableGore || effectiveDamage <= 0f) return;
            bool severe = effectiveDamage >= severeHitDamage;
            bleedPerSecond = Mathf.Max(bleedPerSecond, severe ? severeBleedDamagePerSecond : minorBleedDamagePerSecond);
            bleedUntil = Mathf.Max(bleedUntil, Time.time + (severe ? severeBleedSeconds : minorBleedSeconds));
            Vector3 direction = slashDirection.sqrMagnitude > .01f ? slashDirection.normalized : -packet.HitNormalWorld;
            if (sprayPrefab)
            {
                var spray = AcquireSpray(packet.HitPointWorld, direction);
                if (spray) { spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); spray.Play(); }
            }
            else
            {
                var spray = AcquireSpray(packet.HitPointWorld, direction);
                if (spray) spray.Emit(Mathf.Clamp(Mathf.RoundToInt(effectiveDamage * .8f * bloodIntensityMultiplier),
                    4, maximumDropletsPerHit));
            }
            if (maximumSurfaceMarks > 0)
            {
                if (TrySurface(packet.HitPointWorld + direction * .03f, direction, 2.5f, out var hit))
                    AddMark(hit.point, hit.normal);
                else if (TrySurface(packet.HitPointWorld + Vector3.up * .05f, Vector3.down, 2.5f, out hit))
                    AddMark(hit.point, hit.normal);
            }
        }

        public void RegisterAmputation(float damagePerSecond, float seconds, Vector3 joint, Vector3 direction,
            bool decapitation)
        {
            if (!enableGore) return;
            bleedPerSecond = Mathf.Max(bleedPerSecond, Mathf.Max(0f, damagePerSecond));
            bleedUntil = Mathf.Max(bleedUntil, Time.time + Mathf.Max(0f, seconds));
            var spray = AcquireSpray(joint, direction.sqrMagnitude > .01f ? direction.normalized : transform.forward);
            if (spray)
            {
                spray.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                spray.Emit(decapitation ? 55 : 36);
            }
            StartCoroutine(SecondarySpray(joint, direction, decapitation));
        }

        System.Collections.IEnumerator SecondarySpray(Vector3 joint, Vector3 direction, bool decapitation)
        {
            int pulses = decapitation ? 5 : 3;
            for (int i = 0; i < pulses; i++)
            {
                yield return new WaitForSeconds(.3f);
                var spray = AcquireSpray(joint, direction.sqrMagnitude > .01f ? direction.normalized : Vector3.up);
                if (spray) spray.Emit(Mathf.Max(3, 15 - i * 3));
            }
        }

        void Update()
        {
            if (Time.time >= nextMarkExpiryCheck)
            {
                nextMarkExpiryCheck = Time.time + 1f;
                for (int i = 0; i < marks.Count; i++)
                    if (marks[i] && marks[i].activeSelf && Time.time >= markExpiry[i]) marks[i].SetActive(false);
            }
            if (!health || health.IsDead || Time.time >= bleedUntil || bleedPerSecond <= 0f) return;
            if (Time.time < nextBleedTick) return;
            nextBleedTick = Time.time + 1f;
            health.ApplyBleeding(bleedPerSecond);
            if (maximumSurfaceMarks > 0 && ++bleedCount % 2 == 0 &&
                Vector3.Distance(transform.position, lastTrailPosition) >= trailDistance &&
                TrySurface(transform.position + Vector3.up * .4f, Vector3.down, 3f, out var hit))
            { AddMark(hit.point, hit.normal); lastTrailPosition = transform.position; }
        }


        void StartBloodPool()
        {
            if (!enableGore || bloodPoolRadius <= 0f || bloodPool || globalPools >= GlobalPoolLimit) return;
            if (!TrySurface(transform.position + Vector3.up, Vector3.down, 4f, out var hit)) return;
            bloodPool = CreateSurfacePatch("Corpse blood pool");
            globalPools++;
            poolAllocated = true;
            bloodPool.transform.position = hit.point;
            bloodPool.GetComponent<Renderer>().sharedMaterial = bloodMarkMaterial ? bloodMarkMaterial : DefaultMarkMaterial();
            UpdateSurfacePatch(bloodPool, .08f);
            StartCoroutine(GrowPool());
        }

        System.Collections.IEnumerator GrowPool()
        {
            float started = Time.time;
            while (bloodPool && Time.time - started < bloodPoolGrowSeconds)
            {
                float radius = Mathf.Lerp(.08f, bloodPoolRadius, (Time.time - started) / bloodPoolGrowSeconds);
                UpdateSurfacePatch(bloodPool, radius);
                yield return new WaitForSeconds(.2f);
            }
        }

        bool TrySurface(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, surfaceHits, distance,
                surfaceLayers, QueryTriggerInteraction.Ignore);
            nearest = default;
            float closest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = surfaceHits[i];
                if (!hit.collider || hit.collider.transform.IsChildOf(transform) || hit.distance >= closest ||
                    hit.normal.y <= .1f) continue;
                nearest = hit;
                closest = hit.distance;
            }
            return closest < float.PositiveInfinity;
        }

        ParticleSystem AcquireSpray(Vector3 position, Vector3 direction)
        {
            if (maximumActiveSprays <= 0 || maximumDropletsPerHit <= 0) return null;
            ParticleSystem spray;
            if (sprays.Count < maximumActiveSprays && globalSprays < GlobalSprayLimit)
            {
                spray = sprayPrefab ? Instantiate(sprayPrefab) : CreateDefaultSpray();
                sprays.Add(spray);
                globalSprays++;
            }
            else
            {
                if (sprays.Count == 0) return null;
                spray = sprays[nextSpray];
                nextSpray = (nextSpray + 1) % sprays.Count;
            }
            spray.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
            return spray;
        }

        static ParticleSystem CreateDefaultSpray()
        {
            var go = new GameObject("Pooled blood droplets");
            var particles = go.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.25f, .7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(.008f, .032f);
            main.startColor = new Color(.32f, .015f, .01f, .95f);
            main.gravityModifier = 1.2f;
            main.maxParticles = 80;
            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = .015f;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = DefaultMaterial();
            return particles;
        }

        static Material DefaultMaterial()
        {
            if (defaultBloodMaterial) return defaultBloodMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!shader) shader = Shader.Find("Universal Render Pipeline/Unlit");
            defaultBloodMaterial = new Material(shader) { name = "Runtime blood" };
            defaultBloodMaterial.SetColor("_BaseColor", new Color(.35f, .012f, .008f, .8f));
            defaultBloodMaterial.SetFloat("_Surface", 1f);
            defaultBloodMaterial.SetFloat("_SrcBlend", 5f);
            defaultBloodMaterial.SetFloat("_DstBlend", 10f);
            defaultBloodMaterial.SetFloat("_ZWrite", 0f);
            defaultBloodMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            defaultBloodMaterial.SetOverrideTag("RenderType", "Transparent");
            defaultBloodMaterial.renderQueue = 3000;
            return defaultBloodMaterial;
        }

        static Material DefaultMarkMaterial()
        {
            if (defaultMarkMaterial) return defaultMarkMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader) shader = Shader.Find("Unlit/Color");
            defaultMarkMaterial = new Material(shader) { name = "Runtime blood mark" };
            defaultMarkMaterial.SetColor("_BaseColor", new Color(.27f, .008f, .005f, .82f));
            defaultMarkMaterial.SetTexture("_BaseMap", SplatterTexture());
            defaultMarkMaterial.SetFloat("_Surface", 1f);
            defaultMarkMaterial.SetFloat("_SrcBlend", 5f);
            defaultMarkMaterial.SetFloat("_DstBlend", 10f);
            defaultMarkMaterial.SetFloat("_ZWrite", 0f);
            defaultMarkMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            defaultMarkMaterial.SetOverrideTag("RenderType", "Transparent");
            defaultMarkMaterial.renderQueue = 3000;
            return defaultMarkMaterial;
        }

        static Texture2D SplatterTexture()
        {
            if (splatterTexture) return splatterTexture;
            const int size = 64;
            splatterTexture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            { name = "Runtime blood splatter", wrapMode = TextureWrapMode.Clamp };
            var colors = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float px = (x + .5f) / size * 2f - 1f;
                    float py = (y + .5f) / size * 2f - 1f;
                    float angle = Mathf.Atan2(py, px);
                    float radius = Mathf.Sqrt(px * px + py * py);
                    float edge = .67f + .09f * Mathf.Sin(angle * 5f) + .06f * Mathf.Sin(angle * 9f + 1.2f);
                    float alpha = Mathf.Clamp01((edge - radius) * 30f);
                    alpha = Mathf.Max(alpha, Mathf.Clamp01((.095f - Vector2.Distance(new Vector2(px, py),
                        new Vector2(.78f, .22f))) * 40f));
                    alpha = Mathf.Max(alpha, Mathf.Clamp01((.065f - Vector2.Distance(new Vector2(px, py),
                        new Vector2(-.72f, -.48f))) * 40f));
                    colors[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            splatterTexture.SetPixels32(colors);
            splatterTexture.Apply(true, true);
            return splatterTexture;
        }

        static GameObject CreateSurfacePatch(string patchName)
        {
            var patch = new GameObject(patchName);
            var filter = patch.AddComponent<MeshFilter>();
            filter.sharedMesh = new Mesh { name = patchName + " mesh" };
            patch.AddComponent<MeshRenderer>();
            return patch;
        }

        void UpdateSurfacePatch(GameObject patch, float radius)
        {
            if (!patch) return;
            const int segments = 4;
            const int side = segments + 1;
            var vertices = new Vector3[side * side];
            var uv = new Vector2[vertices.Length];
            var normals = new Vector3[vertices.Length];
            var triangles = new int[segments * segments * 6];
            var center = patch.transform.position;
            for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                {
                    int index = z * side + x;
                    float u = (float)x / segments, v = (float)z / segments;
                    var sample = center + new Vector3((u * 2f - 1f) * radius, .45f, (v * 2f - 1f) * radius);
                    if (TrySurface(sample, Vector3.down, 1.2f, out var hit))
                    {
                        vertices[index] = hit.point + hit.normal * .004f - center;
                        normals[index] = hit.normal;
                    }
                    else
                    {
                        vertices[index] = sample - Vector3.up * .55f - center;
                        normals[index] = Vector3.up;
                    }
                    uv[index] = new Vector2(u, v);
                }
            int triangle = 0;
            for (int z = 0; z < segments; z++)
                for (int x = 0; x < segments; x++)
                {
                    int a = z * side + x, b = a + 1, c = a + side, d = c + 1;
                    triangles[triangle++] = a; triangles[triangle++] = c; triangles[triangle++] = b;
                    triangles[triangle++] = b; triangles[triangle++] = c; triangles[triangle++] = d;
                }
            var mesh = patch.GetComponent<MeshFilter>().sharedMesh;
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.normals = normals;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }

        static void DestroySurfacePatch(GameObject patch)
        {
            if (!patch) return;
            var filter = patch.GetComponent<MeshFilter>();
            if (filter && filter.sharedMesh) Destroy(filter.sharedMesh);
            Destroy(patch);
        }

        void AddMark(Vector3 point, Vector3 normal)
        {
            GameObject mark;
            int index;
            if (marks.Count < maximumSurfaceMarks && globalSurfaceMarks < GlobalSurfaceMarkLimit)
            {
                mark = CreateSurfacePatch("Blood surface mark");
                marks.Add(mark);
                globalSurfaceMarks++;
                markExpiry.Add(0f);
                index = marks.Count - 1;
            }
            else
            {
                if (marks.Count == 0) return;
                index = nextMark;
                mark = marks[index];
                nextMark = (nextMark + 1) % marks.Count;
            }
            mark.transform.position = point;
            UpdateSurfacePatch(mark, Random.Range(.04f, .08f) * bloodIntensityMultiplier);
            mark.GetComponent<MeshRenderer>().sharedMaterial = bloodMarkMaterial ? bloodMarkMaterial : DefaultMarkMaterial();
            mark.SetActive(true);
            markExpiry[index] = Time.time + markLifetime;
        }

        void OnDestroy()
        {
            if (health) health.onDeath.RemoveListener(StartBloodPool);
            foreach (var mark in marks) DestroySurfacePatch(mark);
            globalSurfaceMarks = Mathf.Max(0, globalSurfaceMarks - marks.Count);
            foreach (var spray in sprays) if (spray) Destroy(spray.gameObject);
            globalSprays = Mathf.Max(0, globalSprays - sprays.Count);
            DestroySurfacePatch(bloodPool);
            if (poolAllocated) globalPools = Mathf.Max(0, globalPools - 1);
        }
    }
}
