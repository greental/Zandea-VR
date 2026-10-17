using System;
using System.Collections.Generic;
using JunkerBody;
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
            public bool treated;
            public Transform bandage;
            public GameObject visual;
            public ParticleSystem blood;
            public Vector3 WorldPosition => zone ? zone.ToWorld(localPosition) : Vector3.zero;
        }

        public VRBodyController body;
        [Min(0f)] public float bleedMultiplier = .007f;
        [Min(0f)] public float postTreatmentBleedFraction = .02f;
        public bool createTestWoundOnStart;
        public WrapRegion testRegion = WrapRegion.LeftForearm;
        public List<WoundWrapZone> zones = new List<WoundWrapZone>();
        readonly List<Wound> wounds = new List<Wound>();
        public IReadOnlyList<Wound> Wounds => wounds;
        ScavengerPlayerHealth health;
        float bleedClock;
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
        void Start() { if (createTestWoundOnStart) SpawnTestWound(testRegion); }
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
        }
        void BuildZones()
        {
            Add( WrapRegion.LeftForearm, body.leftArm.lower, body.leftArm.tip, .052f);
            Add( WrapRegion.RightForearm, body.rightArm.lower, body.rightArm.tip, .052f);
            Add( WrapRegion.LeftUpperArm, body.leftArm.upper, body.leftArm.lower, .068f);
            Add( WrapRegion.RightUpperArm, body.rightArm.upper, body.rightArm.lower, .068f);
            Add( WrapRegion.LeftThigh, body.leftLeg.upper, body.leftLeg.lower, .105f);
            Add( WrapRegion.RightThigh, body.rightLeg.upper, body.rightLeg.lower, .105f);
            Add( WrapRegion.LeftCalf, body.leftLeg.lower, body.leftLeg.tip, .075f);
            Add( WrapRegion.RightCalf, body.rightLeg.lower, body.rightLeg.tip, .075f);
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
        {
            WoundWrapZone best = null;
            float distance = float.PositiveInfinity;
            foreach (var zone in zones)
            {
                if (!zone || !zone.physicalWrapping || !zone.Contains(hit, .15f)) continue;
                float d = Vector3.Distance(zone.Surface(hit), hit);
                if (d < distance) { distance = d; best = zone; }
            }
            return best ? Register(best, best.Surface(hit), severity) : null;
        }
        Wound Register(WoundWrapZone zone, Vector3 hit, float severity)
        {
            var wound = new Wound { id = nextId++, region = zone.region, zone = zone,
                localPosition = zone.ToFrame(hit), severity = severity,
                bleedingPerSecond = Mathf.Max(.005f, severity * bleedMultiplier) };
            wounds.Add(wound);
            var mark = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mark.name = "Wound " + wound.id;
            Destroy(mark.GetComponent<Collider>());
            mark.transform.SetParent(zone.bone, true);
            mark.transform.position = hit;
            mark.transform.localScale = Vector3.one * Mathf.Clamp(.025f + severity * .0005f, .025f, .045f);
            mark.GetComponent<Renderer>().sharedMaterial = WoundMaterial();
            wound.visual = mark;
            var bloodObject = new GameObject("Wound bleeding");
            bloodObject.transform.SetParent(mark.transform, false);
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
                wound.treated = true; wound.bandage = wrap; count++;
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
        static Material BloodMaterial()
        {
            if (!bloodMaterial)
            {
                var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                bloodMaterial = new Material(shader) { name = "Wound blood droplets" };
                bloodMaterial.SetColor("_BaseColor", Color.white);
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
            foreach (var wound in wounds) if (wound.visual) Destroy(wound.visual);
        }
    }
}
