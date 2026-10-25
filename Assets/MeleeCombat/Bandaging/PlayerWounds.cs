using System;
using System.Collections.Generic;
using JunkerBody;
using JunkerEquipment;
using ScavengerNPC;
using UnityEngine;

namespace KenshiVR.Bandaging
{
    [DefaultExecutionOrder(10020)]
    public sealed class PlayerWounds : MonoBehaviour
    {
        [Serializable]
        public sealed class Wound
        {
            public int id;
            public WrapRegion region;
            public WoundWrapZone zone;
            public Vector3 localPosition;
            public float severity;
            public float bleedingPerSecond;
            public float remainingRecovery;
            public bool treated;
            public float treatedAt;
            public Transform bandage;
            public GameObject visual;
            public ParticleSystem blood;
            public Vector3 WorldPosition => zone ? zone.ToWorld(localPosition) : Vector3.zero;
        }

        public VRBodyController body;
        [Min(0f)] public float bleedMultiplier = .007f;
        [Min(0f)] public float postTreatmentBleedFraction = 0f;
        public bool createTestWoundOnStart;
        [Tooltip("Seed a left forearm wound only while testing in the Unity Editor.")]
        public bool spawnEditorTestWound;
        public BandageRoll startingRollPrefab;
        [Min(0f)] public float recoveryPerSecond = .75f;
        [Min(0f)] public float recoveryDelayAfterHit = 5f;
        public WrapRegion testRegion = WrapRegion.LeftForearm;
        public List<WoundWrapZone> zones = new List<WoundWrapZone>();
        readonly List<Wound> wounds = new List<Wound>();
        public IReadOnlyList<Wound> Wounds => wounds;
        ScavengerPlayerHealth health;
        float bleedClock;
        float lastInjuryTime;
        int nextId = 1;
        static Material woundMaterial;
        static Material cueMaterial;
        GameObject cue;

        void Awake()
        {
            health = GetComponent<ScavengerPlayerHealth>();
            if (!body) body = GetComponentInChildren<VRBodyController>(true);
            if (body && zones.Count == 0) BuildZones();
        }
        void Start()
        {
            if (createTestWoundOnStart || Application.isEditor && spawnEditorTestWound)
                SpawnTestWound(testRegion);
            PlaceStartingRoll();
        }
        void PlaceStartingRoll()
        {
            if (!startingRollPrefab || !body) return;
            var rig = GetComponentInChildren<JunkerEquipmentRig>(true);
            if (!rig) return;
            foreach (var existing in FindObjectsByType<BandageRoll>(FindObjectsSortMode.None))
                if (existing && Vector3.Distance(existing.transform.position, transform.position) < 1.5f) return;
            var anchor = new GameObject("Medical bandage slot");
            anchor.transform.SetParent(rig.transform, false);
            anchor.transform.localPosition = new Vector3(.22f, .9f, .05f);
            var slot = anchor.AddComponent<JunkerEquipmentSlot>();
            slot.approvedItemIds.Add("bandage_roll");
            slot.radius = .3f;
            slot.detachMountedItem = true;
            var instance = Instantiate(startingRollPrefab, anchor.transform.position, anchor.transform.rotation);
            var item = instance.GetComponent<JunkerEquipmentItem>();
            if (item)
            {
                item.rig = rig;
                item.holsterPosition = Vector3.zero;
                item.holsterRadius = .3f;
            }
            if (!item || !slot.TryMount(item))
            {
                Destroy(instance.gameObject);
                Destroy(anchor);
                return;
            }
            var previous = rig.slots;
            var slots = new JunkerEquipmentSlot[(previous?.Length ?? 0) + 1];
            if (previous != null) previous.CopyTo(slots, 0);
            slots[slots.Length - 1] = slot;
            rig.slots = slots;
        }
        void Update()
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Input.GetKeyDown(KeyCode.F6)) SpawnTestWound(testRegion);
            if (Input.GetKeyDown(KeyCode.F7)) testRegion = (WrapRegion)(((int)testRegion + 1) % 8);
            #endif
            if (!health || health.IsDead) return;
            bleedClock += Time.deltaTime;
            if (bleedClock < .25f) return;
            float elapsed = bleedClock;
            bleedClock = 0f;
            float bleeding = 0f;
            for (int i = 0; i < wounds.Count; i++) bleeding += wounds[i].bleedingPerSecond * (wounds[i].treated ? postTreatmentBleedFraction : 1f);
            health.ApplyBleeding(bleeding * elapsed);
            float budget = recoveryPerSecond * elapsed;
            if (Time.time - lastInjuryTime >= recoveryDelayAfterHit)
                foreach (var wound in wounds)
                {
                    if (!wound.treated || wound.remainingRecovery <= 0f || budget <= 0f) continue;
                    float recovered = health.ApplyBandageRecovery(Mathf.Min(budget, wound.remainingRecovery));
                    wound.remainingRecovery -= recovered;
                    budget -= recovered;
                    if (wound.remainingRecovery <= 0f && wound.bandage)
                        wound.bandage.GetComponent<AppliedBandage>()?.MarkReady();
                }
            foreach(var wound in wounds)
                if(wound.treated&&wound.bandage&&Time.time-wound.treatedAt>=10f)
                    wound.bandage.GetComponent<AppliedBandage>()?.MarkReady();
        }
        void BuildZones()
        {
            Add(WrapRegion.LeftForearm, body.leftArm.lower, body.leftArm.tip, .075f);
            Add(WrapRegion.RightForearm, body.rightArm.lower, body.rightArm.tip, .075f);
            Add(WrapRegion.LeftUpperArm, body.leftArm.upper, body.leftArm.lower, .09f);
            Add(WrapRegion.RightUpperArm, body.rightArm.upper, body.rightArm.lower, .09f);
            Add(WrapRegion.LeftThigh, body.leftLeg.upper, body.leftLeg.lower, .14f);
            Add(WrapRegion.RightThigh, body.rightLeg.upper, body.rightLeg.lower, .14f);
            Add(WrapRegion.LeftCalf, body.leftLeg.lower, body.leftLeg.tip, .105f);
            Add(WrapRegion.RightCalf, body.rightLeg.lower, body.rightLeg.tip, .105f);
            // Head and chest intentionally have zone data but no unsafe three-turn gesture.
            if (body.head) AddStatic(WrapRegion.Head, body.head, .10f, .16f, false);
            if (body.chest) AddStatic(WrapRegion.Chest, body.chest, .18f, .25f, false);
        }
        void Add(WrapRegion region, Transform start, Transform end, float radius)
        {
            if (!start || !end) return;
            var zone = start.gameObject.AddComponent<WoundWrapZone>();
            zone.region = region; zone.bone = start; zone.radius = radius;
            Vector3 axis = end.position - start.position;
            zone.length = Mathf.Max(.08f, axis.magnitude * .78f);
            zone.localCenter = start.InverseTransformPoint((start.position + end.position) * .5f);
            zone.localFrame = Quaternion.Inverse(start.rotation) * Quaternion.FromToRotation(Vector3.up, axis.normalized);
            zones.Add(zone);
        }
        void AddStatic(WrapRegion region, Transform bone, float radius, float length, bool physical)
        {
            var zone = bone.gameObject.AddComponent<WoundWrapZone>();
            zone.region = region; zone.bone = bone; zone.radius = radius; zone.length = length; zone.physicalWrapping = physical;
            zones.Add(zone);
        }
        public Wound SpawnTestWound(WrapRegion region)
        {
            foreach (var zone in zones)
                if (zone && zone.region == region)
                    return Register(zone, zone.ToWorld(new Vector3(zone.radius + zone.surfaceOffset, 0f, 0f)), 18f);
            return null;
        }
        public Wound RegisterCut(Vector3 hit, float severity)
            => RegisterInjury(hit, severity, true);

        public Wound RegisterInjury(Vector3 hit, float severity, bool bleeding)
        {
            if (severity <= 0f || float.IsNaN(severity) || float.IsInfinity(severity)) return null;
            WoundWrapZone best = null;
            float distance = float.PositiveInfinity;
            foreach (var zone in zones)
            {
                // Combat can report a hit on the root capsule rather than a limb collider.
                // Map that coarse contact to the closest supported limb instead of discarding it.
                if (!zone || !zone.physicalWrapping || !zone.bone) continue;
                float d = Vector3.Distance(zone.Surface(hit), hit);
                if (d < distance) { distance = d; best = zone; }
            }
            return best ? Register(best, best.Surface(hit), severity, bleeding) : null;
        }
        Wound Register(WoundWrapZone zone, Vector3 hit, float severity, bool bleeding = true)
        {
            lastInjuryTime = Time.time;
            var wound = new Wound { id = nextId++, region = zone.region, zone = zone,
                localPosition = zone.ToFrame(hit), severity = severity,
                remainingRecovery = severity,
                bleedingPerSecond = bleeding ? Mathf.Max(.005f, severity * bleedMultiplier) : 0f };
            wounds.Add(wound);
            var mark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mark.name = "Wound " + wound.id;
            Destroy(mark.GetComponent<Collider>());
            mark.transform.position = hit;
            // Set world size before parenting: imported avatar bones can have 0.01 scale.
            mark.transform.localScale = Vector3.one * Mathf.Clamp(.025f + severity * .0005f, .025f, .045f);
            mark.transform.SetParent(zone.bone, true);
            mark.GetComponent<Renderer>().sharedMaterial = WoundMaterial();
            wound.visual = mark;
            if (!bleeding) return wound;
            var bloodObject = new GameObject("Wound bleeding");
            bloodObject.transform.position = hit;
            bloodObject.transform.SetParent(mark.transform, true);
            var particles = bloodObject.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.playOnAwake = false; main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = .4f; main.startSpeed = .13f;
            main.startSize = .009f; main.gravityModifier = .8f;
            main.startColor = new Color(.5f, .012f, .01f);
            var emission = particles.emission;
            emission.rateOverTime = Mathf.Clamp(severity * .35f, 2f, 12f);
            var shape = particles.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .01f;
            var renderer = particles.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = BloodMaterial();
            particles.Play();
            wound.blood = particles;
            return wound;
        }
        public Wound ClosestUntreated(Vector3 tip, out WoundWrapZone zone)
        {
            Wound best = null; zone = null; float distance = float.PositiveInfinity;
            foreach (var wound in wounds)
            {
                if (wound.treated || !wound.zone || !wound.zone.physicalWrapping) continue;
                float d = Vector3.Distance(tip, wound.WorldPosition);
                if (d > wound.zone.snapDistance || d >= distance) continue;
                best = wound; zone = wound.zone; distance = d;
            }
            return best;
        }
        public int TreatArea(WoundWrapZone zone, float axialCenter, float halfWidth, Transform wrap)
        {
            int count = 0;
            foreach (var wound in wounds)
            {
                if (wound.treated || wound.zone != zone || Mathf.Abs(wound.localPosition.y - axialCenter) > halfWidth) continue;
                wound.treated = true; wound.treatedAt=Time.time; wound.bandage = wrap; count++;
                wound.bleedingPerSecond = 0f;
                if (wound.blood) wound.blood.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (wound.visual) wound.visual.SetActive(false);
            }
            return count;
        }
        public void ShowCue(Wound wound)
        {
            if (wound == null) { if (cue) cue.SetActive(false); return; }
            if (!cue)
            {
                cue = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                cue.name = "Bandage snap cue";
                Destroy(cue.GetComponent<Collider>());
                cue.transform.localScale = Vector3.one * .019f;
                var renderer = cue.GetComponent<Renderer>();
                if (!cueMaterial)
                {
                    var shader = Shader.Find("Universal Render Pipeline/Unlit");
                    cueMaterial = new Material(shader) { name = "Bandage snap cue" };
                    cueMaterial.SetColor("_BaseColor", new Color(.85f, .83f, .68f));
                }
                renderer.sharedMaterial = cueMaterial;
            }
            cue.transform.position = wound.WorldPosition;
            cue.SetActive(true);
        }
        static Material WoundMaterial()
        {
            if (!woundMaterial)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                woundMaterial = new Material(shader) { name = "Bandage wound" };
                woundMaterial.SetColor("_BaseColor", new Color(.28f, .018f, .015f));
            }
            return woundMaterial;
        }
        static Material bloodMaterial;
        static Texture2D bloodDropTexture;
        static Material BloodMaterial()
        {
            if (!bloodMaterial)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                bloodMaterial = new Material(shader) { name = "Wound blood droplets" };
                bloodMaterial.SetColor("_BaseColor", Color.white);
                bloodDropTexture = new Texture2D(32, 32, TextureFormat.RGBA32, false)
                { name = "Soft blood droplet", wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[32 * 32];
                for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                {
                    float dx = (x + .5f) / 16f - 1f, dy = (y + .5f) / 16f - 1f;
                    float alpha = Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) * 5f);
                    pixels[y * 32 + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
                bloodDropTexture.SetPixels32(pixels); bloodDropTexture.Apply(false, true);
                bloodMaterial.SetTexture("_BaseMap", bloodDropTexture);
                bloodMaterial.SetTexture("_MainTex", bloodDropTexture);
                bloodMaterial.SetFloat("_Surface", 1f);
                bloodMaterial.SetFloat("_SrcBlend", 5f);
                bloodMaterial.SetFloat("_DstBlend", 10f);
                bloodMaterial.SetFloat("_ZWrite", 0f);
                bloodMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                bloodMaterial.SetOverrideTag("RenderType", "Transparent");
                bloodMaterial.renderQueue = 3000;
            }
            return bloodMaterial;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        void OnGUI()
        {
            float rate = 0f;
            foreach (var wound in wounds) rate += wound.bleedingPerSecond * (wound.treated ? postTreatmentBleedFraction : 1f);
            GUI.Label(new Rect(12, 56, 550, 22), $"F6 test wound: {testRegion} | F7 region | wounds {wounds.Count} | bleed {rate:F3}/s");
        }
#endif
        void OnDestroy()
        {
            if (cue) Destroy(cue);
            ClearWounds();
        }
        public void ClearWounds()
        {
            foreach (var wound in wounds)
            {
                if (wound.visual) Destroy(wound.visual);
                if (wound.bandage) Destroy(wound.bandage.gameObject);
            }
            wounds.Clear();
            ShowCue(null);
            bleedClock = 0f;
        }
    }
}
