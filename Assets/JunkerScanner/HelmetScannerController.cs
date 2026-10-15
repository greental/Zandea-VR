using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace JunkerScanner
{
    [DisallowMultipleComponent]
    public sealed class HelmetScannerController : MonoBehaviour
    {
        [Header("Existing XR tracking and actions (enabled by the rig)")]
        public Transform head, leftHand, rightHand;
        public InputActionProperty leftTrigger, rightTrigger;
        public bool allowLeft = true, allowRight = true;
        [Header("Helmet gesture")]
        public Vector3 leftZoneOffset = new(-.16f, 0, 0), rightZoneOffset = new(.16f, 0, 0);
        [Min(.01f)] public float activationRadius = .13f;
        [Tooltip("Optional head-attached volume, replacing the two radius zones. No rigidbody needed.")]
        public Collider activationZone;
        [Min(0)] public float cooldown = .45f;
        [Range(.1f, 1)] public float pressThreshold = .7f;
        [Range(0, .9f)] public float releaseThreshold = .25f;
        [Header("View selection")]
        [Min(.1f)] public float maxDistance = 8;
        [Min(.001f)] public float sphereRadius = .12f;
        [Min(.02f)] public float updateInterval = .1f;
        public LayerMask scannableLayers = ~0, obstructionLayers = ~0;
        [Tooltip("Ignore the player's own colliders during line-of-sight checks.")]
        public Transform playerRoot;
        [Range(0, 90)] public float nearbyViewAngle = 50;
        public ScanVisualProfile visualProfile;
        [Header("World treatment (scanner mode only)")]
        [Range(-30, 0)] public float worldSaturation = -12f;
        [Range(-15, 0)] public float worldContrast = -4f;
        [Tooltip("A small negative value softens bright colors without darkening the scene.")]
        [Range(-.2f, 0)] public float worldPostExposure = 0f;
        public ScannerPresenter presenter;
        public AudioSource audioSource;
        public AudioClip onAudio, offAudio;
        public UnityEvent<bool> stateChanged = new();
        public bool IsScanning { get; private set; }
        public ScannableObject CurrentTarget { get; private set; }
        public event Action<ScannableObject> FocusChanged;
        bool leftHeld, rightHeld;
        float nextToggle, nextScan;
        Collider[] nearby = new Collider[64];
        RaycastHit[] hits = new RaycastHit[32], blockers = new RaycastHit[32];
        readonly Dictionary<ScannableObject, Vector3> visible = new();
        readonly Dictionary<ScanHighlightTarget, ScanHighlightKind> desired = new();
        readonly HashSet<ScanHighlightTarget> activeHighlights = new();
        readonly HashSet<AuthoredCutPath> desiredPaths = new(), activePaths = new();
        Volume worldVolume;
        VolumeProfile worldVolumeProfile;
        UniversalAdditionalCameraData cameraData;
        bool originalPostProcessing;

        void OnEnable()
        {
            // A trigger already held when enabled must first be released.
            leftHeld = Read(leftTrigger) > releaseThreshold; rightHeld = Read(rightTrigger) > releaseThreshold;
            if (presenter) presenter.Present(false, null);
        }
        void Update()
        {
            ProcessGesture(Read(leftTrigger), Read(rightTrigger), Time.unscaledTime);
            if (IsScanning && Time.unscaledTime >= nextScan)
            { nextScan = Time.unscaledTime + Mathf.Max(.02f, updateInterval); RefreshScan(); }
        }
        static float Read(InputActionProperty input) => input.action != null && input.action.enabled ? input.action.ReadValue<float>() : 0;
        public void ProcessGesture(float leftValue, float rightValue, float now)
        {
            bool leftPressed = Edge(leftValue, ref leftHeld), rightPressed = Edge(rightValue, ref rightHeld);
            if (!head || now < nextToggle) return;
            if ((allowLeft && leftPressed && InZone(leftHand)) || (allowRight && rightPressed && InZone(rightHand)))
            { nextToggle = now + cooldown; SetScanning(!IsScanning); }
        }
        bool Edge(float value, ref bool held)
        {
            if (value <= Mathf.Min(releaseThreshold, pressThreshold - .01f)) { held = false; return false; }
            if (held || value < pressThreshold) return false;
            held = true; return true;
        }
        bool InZone(Transform hand)
        {
            if (!hand || !hand.gameObject.activeInHierarchy) return false;
            if (activationZone) return activationZone.enabled && activationZone.gameObject.activeInHierarchy &&
                (activationZone.ClosestPoint(hand.position) - hand.position).sqrMagnitude < .000001f;
            return Vector3.Distance(hand.position, head.TransformPoint(leftZoneOffset)) <= activationRadius ||
                Vector3.Distance(hand.position, head.TransformPoint(rightZoneOffset)) <= activationRadius;
        }
        public void SetScanning(bool value)
        {
            if (value == IsScanning) return;
            IsScanning = value; nextScan = 0;
            SetWorldTreatment(value);
            if (!value) Clear();
            if (presenter) presenter.Present(value, null);
            if (audioSource && (value ? onAudio : offAudio)) audioSource.PlayOneShot(value ? onAudio : offAudio);
            stateChanged.Invoke(value);
        }
        public void RefreshScan()
        {
            if (!IsScanning || !head) { Clear(); return; }
            visible.Clear(); desired.Clear(); desiredPaths.Clear();
            int count;
            // Resize only on overflow; dense scenes do not silently truncate candidates.
            while ((count = Physics.OverlapSphereNonAlloc(head.position, maxDistance, nearby, scannableLayers,
                QueryTriggerInteraction.Collide)) == nearby.Length) Array.Resize(ref nearby, nearby.Length * 2);
            float viewDot = Mathf.Cos(nearbyViewAngle * Mathf.Deg2Rad);
            for (int i = 0; i < count; i++)
            {
                var collider = nearby[i]; var target = Resolve(collider);
                if (!target || visible.ContainsKey(target)) continue;
                Vector3 point = collider.ClosestPoint(head.position);
                Vector3 delta = point - head.position;
                if (delta.sqrMagnitude > maxDistance * maxDistance || Vector3.Dot(head.forward, delta.normalized) < viewDot ||
                    !HasSight(target, point)) continue;
                visible.Add(target, point);
                AddHighlights(target, target.highlightKind);
                foreach (var provider in target.providers)
                {
                    if (provider is CuttableAccessPoint cut && cut.isActiveAndEnabled && cut.NeedsCutting)
                        foreach (var path in cut.paths) if (path) desiredPaths.Add(path);
                    if (provider is IScanPathProvider work && provider.isActiveAndEnabled)
                        foreach (var path in work.ScannerPaths) if (path) desiredPaths.Add(path);
                }
            }
            while ((count = Physics.SphereCastNonAlloc(head.position, sphereRadius, head.forward, hits, maxDistance,
                scannableLayers, QueryTriggerInteraction.Collide)) == hits.Length) Array.Resize(ref hits, hits.Length * 2);
            ScannableObject best = null; float bestScore = float.NegativeInfinity;
            for (int i = 0; i < count; i++)
            {
                var target = Resolve(hits[i].collider);
                if (!target || !visible.ContainsKey(target) || !HasSight(target, hits[i].point)) continue;
                Vector3 delta = hits[i].point - head.position;
                float alignment = Vector3.Dot(head.forward, delta.normalized);
                // Directly viewed children naturally beat their parent; priority is bounded to avoid distant takeover.
                float score = alignment * 4 - hits[i].distance / Mathf.Max(.1f, maxDistance) + Mathf.Clamp(target.Priority, -10, 10) * .04f;
                if (target == CurrentTarget) score += .025f;
                if (score > bestScore) { bestScore = score; best = target; }
            }
            SetFocus(best);
            var info = best ? best.Inspect() : null;
            if (info != null)
            {
                if (ScanStatusUtility.IsFault(info.Status)) AddHighlights(best, ScanHighlightKind.Fault);
                else if (info.Status == ScanStatus.Salvageable) AddHighlights(best, ScanHighlightKind.Salvage);
                foreach (var diagnostic in info.Diagnostics)
                    if (diagnostic.Highlight && ScanStatusUtility.IsFault(diagnostic.Status))
                        AddHighlight(diagnostic.Highlight, ScanHighlightKind.Fault);
            }
            SyncVisuals();
            if (presenter) presenter.Present(true, info);
        }
        ScannableObject Resolve(Collider collider)
        {
            if (!collider || (playerRoot && collider.transform.IsChildOf(playerRoot))) return null;
            var target = collider.GetComponentInParent<ScannableObject>();
            return target && target.CanScan ? target : null;
        }
        bool HasSight(ScannableObject target, Vector3 point)
        {
            Vector3 delta = point - head.position; float distance = delta.magnitude;
            if (distance < .001f) return true;
            int count;
            while ((count = Physics.RaycastNonAlloc(head.position, delta / distance, blockers, Mathf.Max(0, distance - .015f),
                obstructionLayers, QueryTriggerInteraction.Ignore)) == blockers.Length) Array.Resize(ref blockers, blockers.Length * 2);
            for (int i = 0; i < count; i++)
            {
                var hit = blockers[i].collider;
                if (playerRoot && hit.transform.IsChildOf(playerRoot)) continue;
                if (hit.GetComponentInParent<ScannableObject>() == target) continue;
                return false;
            }
            return true;
        }
        void AddHighlights(ScannableObject target, ScanHighlightKind kind)
        { foreach (var highlight in target.highlights) if (highlight) AddHighlight(highlight, kind); }
        void AddHighlight(ScanHighlightTarget target, ScanHighlightKind kind)
        { if (!desired.TryGetValue(target, out var previous) || kind > previous) desired[target] = kind; }
        void SyncVisuals()
        {
            foreach (var target in activeHighlights) if (target && !desired.ContainsKey(target)) target.Release(this);
            activeHighlights.Clear();
            foreach (var entry in desired) { entry.Key.Request(this, entry.Value, visualProfile); activeHighlights.Add(entry.Key); }
            foreach (var path in activePaths) if (path && !desiredPaths.Contains(path)) path.RequestVisibility(this, false);
            activePaths.Clear();
            foreach (var path in desiredPaths) { path.RequestVisibility(this, true); activePaths.Add(path); }
        }
        void SetFocus(ScannableObject value)
        { if (CurrentTarget == value) return; CurrentTarget = value; FocusChanged?.Invoke(value); }
        void Clear()
        {
            SetFocus(null);
            foreach (var target in activeHighlights) if (target) target.Release(this);
            foreach (var path in activePaths) if (path) path.RequestVisibility(this, false);
            activeHighlights.Clear(); activePaths.Clear();
            if (presenter) presenter.Present(IsScanning, null);
        }
        void SetWorldTreatment(bool enabled)
        {
            if (enabled)
            {
                if (!head) return;
                var viewCamera = head.GetComponent<Camera>() ?? head.GetComponentInChildren<Camera>();
                if (!viewCamera) return;
                cameraData = viewCamera.GetUniversalAdditionalCameraData();
                if (!cameraData) return;
                originalPostProcessing = cameraData.renderPostProcessing;
                if (!worldVolume)
                {
                    var effect = new GameObject("Helmet Scanner World Treatment");
                    effect.transform.SetParent(head, false);
                    worldVolume = effect.AddComponent<Volume>();
                    worldVolume.isGlobal = true;
                    worldVolume.priority = 100f;
                    worldVolume.weight = 1f;
                    worldVolumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                    worldVolume.profile = worldVolumeProfile;
                }
                var adjustment = worldVolumeProfile.TryGet(out ColorAdjustments existing)
                    ? existing : worldVolumeProfile.Add<ColorAdjustments>();
                adjustment.saturation.Override(worldSaturation);
                adjustment.contrast.Override(worldContrast);
                adjustment.postExposure.Override(worldPostExposure);
                worldVolume.gameObject.layer = FirstLayer(cameraData.volumeLayerMask.value);
                worldVolume.enabled = true;
                cameraData.renderPostProcessing = true;
            }
            else
            {
                if (worldVolume) worldVolume.enabled = false;
                if (cameraData) cameraData.renderPostProcessing = originalPostProcessing;
                cameraData = null;
            }
        }
        static int FirstLayer(int mask)
        {
            for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) return i;
            return 0;
        }
        void OnDisable() { if (IsScanning) SetScanning(false); else Clear(); }
        void OnDestroy()
        {
            if (worldVolumeProfile) Destroy(worldVolumeProfile);
        }
        void OnDrawGizmosSelected()
        {
            if (!head) return;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(head.TransformPoint(leftZoneOffset), activationRadius);
            Gizmos.DrawWireSphere(head.TransformPoint(rightZoneOffset), activationRadius);
            Gizmos.DrawLine(head.position, head.position + head.forward * maxDistance);
            Gizmos.DrawWireSphere(head.position + head.forward * maxDistance, sphereRadius);
            if (CurrentTarget) { Gizmos.color = Color.yellow; Gizmos.DrawLine(head.position, CurrentTarget.transform.position); }
        }
    }
}
