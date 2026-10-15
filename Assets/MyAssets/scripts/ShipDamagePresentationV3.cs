using UnityEngine;
using VRKoombar.WeaponsStaging.Core;
using System.Collections.Generic;

// Visual damage for Razara and NPCs; optionally turns a dead NPC into drifting wreckage.
[RequireComponent(typeof(CombatDamageable))]
public sealed class ShipDamagePresentationV3 : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] float smokeHullThreshold = .65f;
    [SerializeField, Range(0f, 1f)] float sparksHullThreshold = .4f;
    [SerializeField] GameObject smokePrefab;
    [SerializeField] GameObject sparksPrefab;
    [SerializeField] Vector3 effectLocalPosition = new Vector3(0f, 3f, 0f);
    [SerializeField, Min(.1f)] float smokeScale = 4f;
    [SerializeField, Min(.1f)] float sparksScale = 3f;
    [SerializeField] bool leaveFloatingWreck;
    [SerializeField] Color wreckColor = new Color(.16f, .14f, .13f, 1f);
    [SerializeField] Material destroyedMaterial;
    [SerializeField, Min(0f)] float wreckLinearDamping = .15f;
    [SerializeField, Range(0, 4)] int detachedParts = 2;
    [SerializeField, Min(0f)] float detachmentImpulse = 20f;

    CombatDamageable damageable;
    ParticleSystem[] smoke, sparks;
    bool wrecked;
    Material runtimeWreckMaterial;

    public void Configure(GameObject smoke, GameObject electricSparks, float smokeAt, float sparksAt, bool wreck)
    {
        smokePrefab = smoke;
        sparksPrefab = electricSparks;
        smokeHullThreshold = Mathf.Clamp01(smokeAt);
        sparksHullThreshold = Mathf.Clamp01(sparksAt);
        leaveFloatingWreck = wreck;
    }

    void Awake() => damageable = GetComponent<CombatDamageable>();

    void Update()
    {
        float health = damageable.HullFraction;
        SetEffect(ref smoke, smokePrefab, health <= smokeHullThreshold, smokeScale);
        SetEffect(ref sparks, sparksPrefab, health <= sparksHullThreshold, sparksScale);
        if (leaveFloatingWreck && damageable.IsDestroyed && !wrecked) MakeWreck();
    }

    void SetEffect(ref ParticleSystem[] systems, GameObject prefab, bool active, float scale)
    {
        if (prefab == null) return;
        if (systems == null && active)
        {
            GameObject instance = Instantiate(prefab, transform);
            instance.name = prefab.name + "_HullDamageV3";
            instance.transform.localPosition = effectLocalPosition;
            instance.transform.localScale = Vector3.one * scale;
            systems = instance.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem system in systems)
            {
                var main = system.main;
                main.loop = true;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                system.Play(true);
            }
        }
        if (systems == null) return;
        foreach (ParticleSystem system in systems)
        {
            if (system == null) continue;
            if (active && !system.isPlaying) system.Play(true);
            else if (!active && system.isPlaying) system.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    void MakeWreck()
    {
        wrecked = true;
        var pilot = GetComponent<NPCShipCombatV2>();
        if (pilot != null) pilot.enabled = false;
        var flight = GetComponent<FlightController>();
        if (flight != null) flight.enabled = false;
        var hotas = GetComponent<HOTASController>();
        if (hotas != null) hotas.enabled = false;
        var recovery = GetComponent<NPCShipTerrainRecoveryV3>();
        if (recovery != null) recovery.enabled = false;
        foreach (var weapon in GetComponentsInChildren<WeaponController>(true)) weapon.enabled = false;
        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null) body = GetComponentInChildren<Rigidbody>(true);
        if (body != null)
        {
            body.useGravity = false;
            body.linearDamping = wreckLinearDamping;
            if (body.GetComponent<PlanetaryWreckGravityV3>() == null) body.gameObject.AddComponent<PlanetaryWreckGravityV3>();
        }
        Material charred = GetWreckMaterial();
        if (detachedParts > 0 && pilot != null) BreakIntoParts(body, charred);
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is ParticleSystemRenderer) continue;
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++) materials[i] = charred;
            renderer.sharedMaterials = materials;
        }
    }

    Material GetWreckMaterial()
    {
        if (destroyedMaterial != null) return destroyedMaterial;
        if (runtimeWreckMaterial != null) return runtimeWreckMaterial;
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        runtimeWreckMaterial = new Material(shader) { name = "Charred Wreck V3" };
        runtimeWreckMaterial.SetColor("_BaseColor", wreckColor);
        runtimeWreckMaterial.SetColor("_Color", wreckColor);
        runtimeWreckMaterial.SetColor("_EmissionColor", Color.black);
        runtimeWreckMaterial.SetFloat("_Smoothness", .08f);
        return runtimeWreckMaterial;
    }

    void BreakIntoParts(Rigidbody parentBody, Material charred)
    {
        List<MeshRenderer> candidates = new List<MeshRenderer>();
        foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>(true))
            if (renderer.enabled && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
                candidates.Add(renderer);
        candidates.Sort((a, b) => PartScore(b).CompareTo(PartScore(a)));
        int count = Mathf.Min(detachedParts, Mathf.Max(0, candidates.Count - 1));
        for (int i = 0; i < count; i++)
        {
            MeshRenderer original = candidates[i];
            Mesh mesh = original.GetComponent<MeshFilter>().sharedMesh;
            GameObject fragment = new GameObject(name + " Debris " + (i + 1), typeof(MeshFilter), typeof(MeshRenderer));
            fragment.transform.SetPositionAndRotation(original.transform.position, original.transform.rotation);
            fragment.transform.localScale = original.transform.lossyScale;
            fragment.GetComponent<MeshFilter>().sharedMesh = mesh;
            fragment.GetComponent<MeshRenderer>().sharedMaterial = charred;
            BoxCollider collider = fragment.AddComponent<BoxCollider>();
            collider.center = mesh.bounds.center;
            collider.size = mesh.bounds.size;
            Rigidbody fragmentBody = fragment.AddComponent<Rigidbody>();
            fragmentBody.mass = Mathf.Max(1f, parentBody != null ? parentBody.mass * .02f : 15f);
            fragmentBody.useGravity = false;
            fragmentBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            fragmentBody.linearVelocity = parentBody != null ? parentBody.linearVelocity : Vector3.zero;
            Vector3 away = (original.bounds.center - transform.position).normalized;
            if (away.sqrMagnitude < .01f) away = Random.onUnitSphere;
            fragmentBody.AddForce(away * detachmentImpulse, ForceMode.VelocityChange);
            fragment.AddComponent<PlanetaryWreckGravityV3>();
            original.enabled = false;
        }
    }

    float PartScore(MeshRenderer renderer)
    {
        string lower = renderer.name.ToLowerInvariant();
        float named = lower.Contains("nose") || lower.Contains("wing") ? 1000f : 0f;
        return named + Vector3.Distance(renderer.bounds.center, transform.position) +
            Mathf.Abs(transform.InverseTransformPoint(renderer.bounds.center).x) * 2f;
    }
}
