using System;
using UnityEngine;
using VRKoombar.WeaponsStaging.Contracts;
using VRKoombar.WeaponsStaging.Data;

namespace VRKoombar.WeaponsStaging.Core
{
    /// <summary>How head aim builds the desired muzzle orientation.</summary>
    public enum HeadAimDirectionMode
    {
        /// <summary>Muzzle world forward matches the head camera forward (recommended for VR).</summary>
        ParallelToCameraForward,

        /// <summary>Muzzle aims at a world point on the view ray (raycast hit, or convergence distance).</summary>
        TowardRaycastHitOrConvergence
    }

    [DefaultExecutionOrder(-100)]
    public class WeaponController : MonoBehaviour, IWeaponController
    {
        [Header("Legacy asset (optional)")]
        [Tooltip("Legacy WeaponDefinition support. Leave empty to configure everything below directly in this inspector.")]
        [SerializeField] private WeaponDefinition definition;
        [Tooltip("If enabled and WeaponDefinition is assigned, legacy asset values are used. Disable to make this component fully inspector-driven.")]
        [SerializeField] private bool useLegacyWeaponDefinition;

        [Header("Weapon (inspector-driven)")]
        [SerializeField] private string weaponId = "weapon_local";
        [SerializeField] private DamageType damageType = DamageType.Kinetic;

        [Header("Projectile")]
        [SerializeField] private GameObject projectilePrefab;
        [SerializeField] private float projectileSpeed = 350f;
        [SerializeField] private float fireRate = 8f;
        [SerializeField] private float weaponHeatPerShot = 0.2f;
        [Header("Heat limits (per weapon)")]
        [SerializeField, Range(0.05f, 1f)] private float overheatThreshold = 1f;
        [SerializeField, Range(0f, 1f)] private float resumeHeatThreshold = .45f;
        [SerializeField, Min(0f)] private float heatCoolingPerSecond = .35f;
        [SerializeField] private bool useHeatLockout;
        [SerializeField] private int ammoPerMagazine;
        [SerializeField] private float reloadSeconds;
        [SerializeField] private float shieldInteraction = 1f;
        [SerializeField] private float hullInteraction = 1f;
        [SerializeField] private bool requiresDeployedHardpoint = true;

        [SerializeField] private WeaponHardpoint hardpoint;
        [SerializeField] private Transform muzzle;
        [SerializeField] private bool infiniteAmmoForDebug;
        [SerializeField] private bool verboseLogs;

        [Header("Audio")]
        [Tooltip("Base fire sound used when gatling mode is OFF.")]
        [SerializeField] private AudioClip fireSound;
        [Tooltip("Drag your **audio file** here to override the Weapon Definition for this gun only, or leave empty to use the definition’s Fire Sound.")]
        [SerializeField] private AudioClip fireSoundOverride;

        [Tooltip(
            "Optional **AudioSource component** (not an audio clip). Drag a child GameObject that has an AudioSource, or pick the AudioSource in the object picker. " +
            "Leave empty to play the clip at the muzzle with PlayClipAtPoint. Used for one-shots and gatling Start/End.")]
        [SerializeField] private AudioSource fireAudioSource;

        [Tooltip("3D blend for weapon one-shots/start/end. Lower values are clearer in cockpit (less distance/doppler feel).")]
        [Range(0f, 1f)]
        [SerializeField] private float fireAudioSpatialBlend = 0.7f;

        [Tooltip("Set to 0 to avoid doppler pitch warble/distortion on fast movement.")]
        [Range(0f, 5f)]
        [SerializeField] private float fireAudioDopplerLevel;

        [Tooltip(
            "Looping mid section for gatling-style weapons (Weapon Definition → Gatling). If empty, a child AudioSource is created when needed.")]
        [SerializeField] private AudioSource gatlingLoopAudioSource;

        [SerializeField] private float fireSoundVolume = 1f;
        [SerializeField] private bool useGatlingStyleFireAudio;
        [SerializeField] private AudioClip gatlingFireSoundStart;
        [SerializeField] private AudioClip gatlingFireSoundLoop;
        [SerializeField] private AudioClip gatlingFireSoundEnd;
        [SerializeField] private float gatlingFireTailDelay = 0.12f;
        [SerializeField] private float gatlingProjectileSpinUpSeconds;

        [Header("Impact VFX")]
        [Tooltip("Optional shared lookup: (DamageType + SurfaceType) -> impact prefab. Falls back to local default below if no match.")]
        [SerializeField] private ImpactVfxLibrary impactVfxLibrary;
        [SerializeField] private GameObject hitImpactEffectPrefab;
        [SerializeField] private float hitImpactEffectScale = 1f;

        [Header("Impact Scorch Mark")]
        [SerializeField] private GameObject hitScorchMarkPrefab;
        [SerializeField] private bool hitScorchMarkUsesUpAsNormal = true;
        [SerializeField] private float hitScorchMarkScale = 1f;
        [SerializeField] private float hitScorchMarkSurfaceOffset = 0.01f;
        [SerializeField] private float hitScorchMarkRandomRollDegrees = 180f;
        [SerializeField] private float hitScorchMarkLifetimeSeconds = 30f;
        [SerializeField] private bool hitScorchMarkParentToHitObject = true;

        [Header("VR head aim")]
        [Tooltip("Aim muzzle using the HMD view ray (see below). Off = authored muzzle + HUD calibration only.")]
        [SerializeField] private bool aimFromHead;

        [Tooltip(
            "Which camera is **your headset view**. In many VR setups that is still the object named **Main Camera** under **XR Origin** (it tracks the HMD). " +
            "Leave empty to use **Camera.main** (same thing if your eye camera has the MainCamera tag). Only assign explicitly if you have several cameras or head aim feels wrong.")]
        [SerializeField] private Camera headCamera;

        [Tooltip("If on, raycasts from the head camera along view forward; muzzle aims at hit point (skips ship). If off, uses only the convergence point below.")]
        [SerializeField] private bool headAimUseViewRaycast = true;

        [SerializeField] private float headAimRaycastMaxDistance = 8000f;

        [SerializeField] private LayerMask headAimRaycastLayers = ~0;

        [SerializeField] private QueryTriggerInteraction headAimRaycastTriggerInteraction = QueryTriggerInteraction.Ignore;

        [Tooltip("Hits on colliders under this transform are ignored (usually the ship). Defaults to weapon root.")]
        [SerializeField] private Transform headAimRaycastIgnoreRoot;

        [Tooltip("Fallback aim point along view if nothing is hit (meters).")]
        [SerializeField] private float headAimConvergenceDistance = 800f;

        [Tooltip("Ship or cockpit transform for world up when looking. Defaults to root if unset.")]
        [SerializeField] private Transform headAimUpReference;

        [Tooltip("Seconds to ease muzzle toward head aim (0 = snap).")]
        [SerializeField] private float headAimRotationSmoothTime = 0.06f;

        [Tooltip("Parallel = muzzle aligns with headset look (best default). Toward point = aim at raycast hit / convergence (older mode).")]
        [SerializeField] private HeadAimDirectionMode headAimDirectionMode = HeadAimDirectionMode.ParallelToCameraForward;

        [Tooltip("Logs every ~1.5s while playing: head aim state, camera, angles. Turn off when fixed.")]
        [SerializeField] private bool headAimDebugLogs = true;

        [Header("HUD calibration")]
        [Tooltip("When off, muzzle rotation is not overridden by HUD yaw/pitch offsets.")]
        [SerializeField] private bool applyHudCalibration = true;

        [Tooltip("Degrees added in local space: pitch (X) then yaw (Y) after the muzzle pose captured at load.")]
        [SerializeField] private float hudCalibrationPitchDeg;

        [SerializeField] private float hudCalibrationYawDeg;

        private Quaternion _baseLocalMuzzleRotation = Quaternion.identity;
        private bool _hasBaseMuzzleRotation;

        private float _nextHeadAimDebugLogTime;

        private bool _headAimLoggedMuzzleNull;
        private bool _headAimLoggedNoCamera;

        private bool _gatlingSustainActive;
        private Transform _sourceRootCached;

        /// <summary>XR trigger held — keeps gatling loop; release drives tail + end (not time-between-shots).</summary>
        private bool _gatlingTriggerHeld;

        private bool _gatlingTriggerHeldPrevious;
        private float _gatlingTriggerReleaseTime;
        private float _gatlingSpinUpEndTime;

        /// <summary>When spin-up &gt; 0, loop clip is held until <see cref="_gatlingSpinUpEndTime"/> so Start is audible alone.</summary>
        private bool _gatlingLoopDeferredUntilSpinUp;

        private bool _warnedGatlingMissingLoop;

        // Backward-compat: if a legacy definition exists and no local projectile is configured, use legacy automatically.
        private bool UseLegacy => definition != null && (useLegacyWeaponDefinition || projectilePrefab == null);
        private string WeaponIdValue => UseLegacy ? definition.weaponId : weaponId;
        private DamageType DamageTypeValue => UseLegacy ? definition.damageType : damageType;
        private GameObject ProjectilePrefabValue => UseLegacy ? definition.projectilePrefab : projectilePrefab;
        private float ProjectileSpeedValue => UseLegacy ? definition.projectileSpeed : projectileSpeed;
        private float FireRateValue => UseLegacy ? definition.fireRate : fireRate;
        private float WeaponHeatPerShotValue => UseLegacy ? definition.weaponHeatPerShot : weaponHeatPerShot;
        private int AmmoPerMagazineValue => UseLegacy ? definition.ammoPerMagazine : ammoPerMagazine;
        private float ReloadSecondsValue => UseLegacy ? definition.reloadSeconds : reloadSeconds;
        private float ShieldInteractionValue => UseLegacy ? definition.shieldInteraction : Mathf.Max(0f, shieldInteraction);
        private float HullInteractionValue => UseLegacy ? definition.hullInteraction : Mathf.Max(0.01f, hullInteraction);
        private bool RequiresDeployedHardpointValue => UseLegacy ? definition.requiresDeployedHardpoint : requiresDeployedHardpoint;
        private bool UseGatlingAudioValue => UseLegacy ? definition.useGatlingStyleFireAudio : useGatlingStyleFireAudio;
        private AudioClip GatlingStartValue => UseLegacy ? definition.gatlingFireSoundStart : gatlingFireSoundStart;
        private AudioClip GatlingLoopValue => UseLegacy ? definition.gatlingFireSoundLoop : gatlingFireSoundLoop;
        private AudioClip GatlingEndValue => UseLegacy ? definition.gatlingFireSoundEnd : gatlingFireSoundEnd;
        private float GatlingTailDelayValue => UseLegacy ? definition.gatlingFireTailDelay : gatlingFireTailDelay;
        private float GatlingSpinUpValue => UseLegacy ? definition.gatlingProjectileSpinUpSeconds : gatlingProjectileSpinUpSeconds;
        private float FireSoundVolumeValue => UseLegacy ? definition.fireSoundVolume : fireSoundVolume;
        private AudioClip FireSoundValue => fireSoundOverride != null ? fireSoundOverride : (UseLegacy ? definition.fireSound : fireSound);
        private GameObject HitImpactPrefabValue => UseLegacy ? definition.hitImpactEffectPrefab : hitImpactEffectPrefab;
        private float HitImpactScaleValue => UseLegacy ? definition.hitImpactEffectScale : hitImpactEffectScale;
        private GameObject HitScorchPrefabValue => UseLegacy ? definition.hitScorchMarkPrefab : hitScorchMarkPrefab;
        private bool HitScorchUsesUpValue => UseLegacy ? definition.hitScorchMarkUsesUpAsNormal : hitScorchMarkUsesUpAsNormal;
        private float HitScorchScaleValue => UseLegacy ? definition.hitScorchMarkScale : hitScorchMarkScale;
        private float HitScorchOffsetValue => UseLegacy ? definition.hitScorchMarkSurfaceOffset : hitScorchMarkSurfaceOffset;
        private float HitScorchRollValue => UseLegacy ? definition.hitScorchMarkRandomRollDegrees : hitScorchMarkRandomRollDegrees;
        private float HitScorchLifetimeValue => UseLegacy ? definition.hitScorchMarkLifetimeSeconds : hitScorchMarkLifetimeSeconds;
        private bool HitScorchParentValue => UseLegacy ? definition.hitScorchMarkParentToHitObject : hitScorchMarkParentToHitObject;

        /// <summary>Muzzle / bore direction for firing and HUD aim indicators.</summary>
        public Transform Muzzle => muzzle;
        public DamageType WeaponDamageType => DamageTypeValue;
        public bool UsesGatlingAudio => UseGatlingAudioValue;
        public float GatlingSpinUpSeconds => GetEffectiveGatlingSpinUpSeconds();

        /// <summary>
        /// Same origin and direction as projectile spawn and <c>linearVelocity</c> (muzzle world position and forward).
        /// Use for PIPs / aim UI so rays match shots.
        /// </summary>
        public bool TryGetMuzzleAimRay(out Ray aimRay)
        {
            if (muzzle == null)
            {
                aimRay = default;
                return false;
            }

            aimRay = new Ray(muzzle.position, muzzle.forward);
            return true;
        }

        private float _nextFireTime;
        private int _ammoInMagazine;
        private bool _reloading;
        private float _reloadEndTime;
        private float _weaponHeat;
        private bool _overheated;
        public void ConfigureHeat(bool enabled, float limit, float resume, float cooling)
        {
            useHeatLockout = enabled;
            overheatThreshold = Mathf.Clamp(limit, .05f, 1f);
            resumeHeatThreshold = Mathf.Clamp(resume, 0f, overheatThreshold - .01f);
            heatCoolingPerSecond = Mathf.Max(0f, cooling);
            _overheated = false;
        }

        private ShipPowerController _shipPowerOnRoot;

        /// <summary>
        /// When a <see cref="ShipPowerController"/> exists on the vessel, weapons only work while HUD/24V power is on.
        /// Resolves from the ship root so it works even if <see cref="ShipWeaponsOrchestrator"/> has no reference.
        /// </summary>
        private bool IsShipWeaponsPowerAllowed()
        {
            return _shipPowerOnRoot == null || _shipPowerOnRoot.hudPowered;
        }

        public bool CanFire
        {
            get
            {
                if (!IsShipWeaponsPowerAllowed())
                {
                    return false;
                }

                if (ProjectilePrefabValue == null || _reloading)
                {
                    return false;
                }

                if (muzzle == null)
                {
                    return false;
                }

                if (Time.time < _nextFireTime)
                {
                    return false;
                }

                if (RequiresDeployedHardpointValue && hardpoint != null && !hardpoint.CanFire)
                {
                    return false;
                }

                if (!infiniteAmmoForDebug && AmmoPerMagazineValue > 0 && _ammoInMagazine <= 0)
                {
                    return false;
                }

                if (useHeatLockout ? _overheated || _weaponHeat >= overheatThreshold : _weaponHeat >= 1f)
                {
                    return false;
                }

                float spinUpSec = GetEffectiveGatlingSpinUpSeconds();
                if (UseGatlingAudioValue && spinUpSec > 1e-5f && Time.time < _gatlingSpinUpEndTime)
                {
                    return false;
                }

                return true;
            }
        }

        private void Awake()
        {
            _sourceRootCached = transform.root != null ? transform.root : transform;

            if (headAimUpReference == null)
            {
                headAimUpReference = transform.root;
            }

            if (headAimRaycastIgnoreRoot == null)
            {
                headAimRaycastIgnoreRoot = transform.root;
            }

            if (fireAudioSource == null)
            {
                fireAudioSource = GetComponent<AudioSource>();
            }
            if (fireAudioSource == null)
            {
                fireAudioSource = gameObject.AddComponent<AudioSource>();
                fireAudioSource.playOnAwake = false;
            }
            ConfigureFireAudioSource();

            EnsureGatlingLoopAudioSourceIfNeeded();

            CaptureMuzzleBaseRotation();
            _ammoInMagazine = AmmoPerMagazineValue;
            if (ProjectilePrefabValue == null)
            {
                Debug.LogWarning($"[Weapon] '{name}': assign Projectile Prefab on WeaponController.", this);
            }

            if (muzzle == null)
            {
                Debug.LogWarning($"[Weapon] '{name}': Muzzle transform is not assigned — cannot spawn shots.", this);
            }

            _shipPowerOnRoot = transform.root.GetComponentInChildren<ShipPowerController>(true);

            if (headCamera != null && !aimFromHead)
            {
                Debug.LogWarning(
                    $"[Weapon][HeadAim] '{name}': Head Camera is assigned but **Aim From Head** is OFF — muzzle will NOT follow the camera. Enable Aim From Head.",
                    this);
            }

            if (aimFromHead && headCamera == null && Camera.main == null)
            {
                Debug.LogWarning(
                    $"[Weapon][HeadAim] '{name}': Aim From Head is ON but no Head Camera and no Camera.main — head aim cannot run.",
                    this);
            }
        }

        private void Update()
        {
            Tick(Time.deltaTime);
            UpdateGatlingLoopAfterSpinUp();
            UpdateGatlingFireAudioTail();
        }

        private void OnDisable()
        {
            _gatlingTriggerHeld = false;
            _gatlingTriggerHeldPrevious = false;
            StopGatlingLoop(silent: true);
        }

        private void EnsureGatlingLoopAudioSourceIfNeeded()
        {
            if (!UseGatlingAudioValue || GatlingLoopValue == null)
            {
                return;
            }

            if (gatlingLoopAudioSource != null)
            {
                return;
            }

            var go = new GameObject("GatlingLoopAudio");
            go.transform.SetParent(transform, false);
            gatlingLoopAudioSource = go.AddComponent<AudioSource>();
            gatlingLoopAudioSource.playOnAwake = false;
            gatlingLoopAudioSource.loop = true;
            gatlingLoopAudioSource.spatialBlend = 1f;
            gatlingLoopAudioSource.dopplerLevel = 0f;
        }

        private void ConfigureFireAudioSource()
        {
            if (fireAudioSource == null)
            {
                return;
            }

            fireAudioSource.playOnAwake = false;
            fireAudioSource.loop = false;
            fireAudioSource.spatialBlend = fireAudioSpatialBlend;
            fireAudioSource.dopplerLevel = fireAudioDopplerLevel;
            fireAudioSource.minDistance = Mathf.Max(0.25f, fireAudioSource.minDistance);
            fireAudioSource.maxDistance = Mathf.Max(fireAudioSource.minDistance + 1f, fireAudioSource.maxDistance);
        }

        /// <summary>
        /// Wait before loop + shots: at least the inspector spin-up, and at least start clip length when assigned
        /// (so leaving spin-up at 0 still defers the mid loop until wind-up audio can play).
        /// </summary>
        private float GetEffectiveGatlingSpinUpSeconds()
        {
            float configured = Mathf.Max(0f, GatlingSpinUpValue);
            if (GatlingStartValue != null)
            {
                return Mathf.Max(configured, GatlingStartValue.length);
            }

            return configured;
        }

        /// <summary>Begins sustained loop + projectiles only after spin-up window (Start plays alone first).</summary>
        private void UpdateGatlingLoopAfterSpinUp()
        {
            if (!UseGatlingAudioValue || !_gatlingLoopDeferredUntilSpinUp)
            {
                return;
            }

            if (!_gatlingTriggerHeld)
            {
                return;
            }

            if (Time.time < _gatlingSpinUpEndTime)
            {
                return;
            }

            _gatlingLoopDeferredUntilSpinUp = false;

            if (gatlingLoopAudioSource == null || GatlingLoopValue == null)
            {
                return;
            }

            float vol = FireSoundVolumeValue;
            gatlingLoopAudioSource.clip = GatlingLoopValue;
            gatlingLoopAudioSource.volume = vol;
            gatlingLoopAudioSource.loop = true;
            gatlingLoopAudioSource.Play();
        }

        /// <summary>After trigger release, waits <see cref="WeaponDefinition.gatlingFireTailDelay"/> then plays End and stops loop.</summary>
        private void UpdateGatlingFireAudioTail()
        {
            if (!UseGatlingAudioValue || !_gatlingSustainActive)
            {
                return;
            }

            if (_gatlingTriggerHeld)
            {
                return;
            }

            if (Time.time - _gatlingTriggerReleaseTime < GatlingTailDelayValue)
            {
                return;
            }

            StopGatlingLoop(silent: false);
        }

        /// <inheritdoc />
        public void SetGatlingTriggerHeld(bool held)
        {
            if (!UseGatlingAudioValue)
            {
                return;
            }

            if (!IsShipWeaponsPowerAllowed())
            {
                held = false;
            }

            if (held && !_gatlingTriggerHeldPrevious)
            {
                _gatlingSpinUpEndTime = Time.time + GetEffectiveGatlingSpinUpSeconds();
            }

            if (!held && _gatlingTriggerHeldPrevious)
            {
                _gatlingTriggerReleaseTime = Time.time;
            }

            _gatlingTriggerHeldPrevious = held;
            _gatlingTriggerHeld = held;

            if (held)
            {
                TryStartGatlingSustainAudio();
            }
        }

        private void TryStartGatlingSustainAudio()
        {
            if (!UseGatlingAudioValue || _gatlingSustainActive)
            {
                return;
            }

            if (GatlingLoopValue == null)
            {
                if (!_warnedGatlingMissingLoop)
                {
                    _warnedGatlingMissingLoop = true;
                    Debug.LogWarning(
                        $"[Weapon] '{name}': Gatling fire audio needs **Gatling Fire Sound Loop** assigned (per-shot Fire Sound is not used).",
                        this);
                }

                return;
            }

            EnsureGatlingLoopAudioSourceIfNeeded();
            float vol = FireSoundVolumeValue;

            bool deferLoop = GetEffectiveGatlingSpinUpSeconds() > 1e-5f;

            if (GatlingStartValue != null)
            {
                PlayOneShotOrAtPoint(GatlingStartValue, vol);
            }

            if (deferLoop)
            {
                _gatlingLoopDeferredUntilSpinUp = true;
                if (gatlingLoopAudioSource != null)
                {
                    gatlingLoopAudioSource.Stop();
                }
            }
            else
            {
                _gatlingLoopDeferredUntilSpinUp = false;
                if (gatlingLoopAudioSource != null)
                {
                    gatlingLoopAudioSource.clip = GatlingLoopValue;
                    gatlingLoopAudioSource.volume = vol;
                    gatlingLoopAudioSource.loop = true;
                    gatlingLoopAudioSource.Play();
                }
            }

            _gatlingSustainActive = true;
        }

        private void StopGatlingLoop(bool silent)
        {
            _gatlingLoopDeferredUntilSpinUp = false;

            if (gatlingLoopAudioSource != null && gatlingLoopAudioSource.isPlaying)
            {
                gatlingLoopAudioSource.Stop();
            }

            if (!silent && GatlingEndValue != null)
            {
                float vol = FireSoundVolumeValue;
                Vector3 pos = muzzle != null ? muzzle.position : transform.position;
                if (fireAudioSource != null)
                {
                    fireAudioSource.PlayOneShot(GatlingEndValue, vol);
                }
                else
                {
                    AudioSource.PlayClipAtPoint(GatlingEndValue, pos, vol);
                }
            }

            _gatlingSustainActive = false;
        }

        private void LateUpdate()
        {
            if (aimFromHead)
            {
                ApplyHeadAimingToMuzzle();
            }
            else
            {
                ApplyHudCalibrationToMuzzle();
            }
        }

        private void ApplyHeadAimingToMuzzle()
        {
            if (muzzle == null)
            {
                if (headAimDebugLogs && !_headAimLoggedMuzzleNull)
                {
                    _headAimLoggedMuzzleNull = true;
                    Debug.LogWarning($"[Weapon][HeadAim] '{name}': muzzle is null — assign Muzzle transform.", this);
                }

                return;
            }

            Camera cam = headCamera != null ? headCamera : Camera.main;
            if (cam == null)
            {
                if (headAimDebugLogs && !_headAimLoggedNoCamera)
                {
                    _headAimLoggedNoCamera = true;
                    Debug.LogWarning(
                        $"[Weapon][HeadAim] '{name}': no Camera (Head Camera empty and Camera.main is null).",
                        this);
                }

                ApplyHudCalibrationToMuzzle();
                return;
            }

            Vector3 up = headAimUpReference != null ? headAimUpReference.up : Vector3.up;
            Quaternion worldRot;

            if (headAimDirectionMode == HeadAimDirectionMode.ParallelToCameraForward)
            {
                worldRot = Quaternion.LookRotation(cam.transform.forward, up);
            }
            else
            {
                Vector3 aimPoint = GetHeadAimWorldPoint(cam);
                Vector3 dir = (aimPoint - muzzle.position).normalized;
                if (dir.sqrMagnitude < 1e-12f)
                {
                    dir = cam.transform.forward;
                }

                worldRot = Quaternion.LookRotation(dir, up);
            }

            Quaternion targetLocal;
            if (muzzle.parent != null)
            {
                targetLocal = Quaternion.Inverse(muzzle.parent.rotation) * worldRot;
            }
            else
            {
                targetLocal = worldRot;
            }

            if (applyHudCalibration)
            {
                targetLocal *= Quaternion.Euler(hudCalibrationPitchDeg, hudCalibrationYawDeg, 0f);
            }

            float t = headAimRotationSmoothTime <= 1e-5f
                ? 1f
                : 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(1e-4f, headAimRotationSmoothTime));

            if (muzzle.parent != null)
            {
                muzzle.localRotation = Quaternion.Slerp(muzzle.localRotation, targetLocal, Mathf.Clamp01(t));
            }
            else
            {
                Quaternion targetWorld = applyHudCalibration
                    ? worldRot * Quaternion.Euler(hudCalibrationPitchDeg, hudCalibrationYawDeg, 0f)
                    : worldRot;
                muzzle.rotation = Quaternion.Slerp(muzzle.rotation, targetWorld, Mathf.Clamp01(t));
            }

            MaybeLogHeadAimPeriodic(cam, worldRot);
        }

        private void MaybeLogHeadAimPeriodic(Camera cam, Quaternion worldRot)
        {
            if (!headAimDebugLogs || !aimFromHead)
            {
                return;
            }

            if (Time.time < _nextHeadAimDebugLogTime)
            {
                return;
            }

            _nextHeadAimDebugLogTime = Time.time + 1.5f;

            float angleDeg = Vector3.Angle(muzzle.forward, cam.transform.forward);
            string camSrc = headCamera != null ? "Head Camera field" : "Camera.main";
            string mode = headAimDirectionMode.ToString();
            Debug.Log(
                $"[Weapon][HeadAim] '{name}' | AimFromHead=ON | mode={mode} | cam={cam.name} ({camSrc}) enabled={cam.enabled} pos={cam.transform.position:F1} fwd={cam.transform.forward:F2} | " +
                $"angle(muzzleFwd, camFwd)={angleDeg:F1}° | muzzleFwd={muzzle.forward:F2} | ignoreRoot={headAimRaycastIgnoreRoot?.name ?? "null"}",
                this);
        }

        /// <summary>
        /// View ray from headset: raycast for impact point, else point along view at convergence distance.
        /// </summary>
        private Vector3 GetHeadAimWorldPoint(Camera cam)
        {
            Transform ct = cam.transform;
            Vector3 origin = ct.position;
            Vector3 forward = ct.forward;

            if (!headAimUseViewRaycast)
            {
                return origin + forward * headAimConvergenceDistance;
            }

            Ray ray = new Ray(origin, forward);
            RaycastHit[] hits = Physics.RaycastAll(ray, headAimRaycastMaxDistance, headAimRaycastLayers, headAimRaycastTriggerInteraction);
            if (hits == null || hits.Length == 0)
            {
                return origin + forward * headAimConvergenceDistance;
            }

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                Collider c = hits[i].collider;
                if (c == null)
                {
                    continue;
                }

                if (headAimRaycastIgnoreRoot != null && c.transform.IsChildOf(headAimRaycastIgnoreRoot))
                {
                    continue;
                }

                return hits[i].point;
            }

            return origin + forward * headAimConvergenceDistance;
        }

        private void CaptureMuzzleBaseRotation()
        {
            if (muzzle == null || _hasBaseMuzzleRotation)
            {
                return;
            }

            _baseLocalMuzzleRotation = muzzle.localRotation;
            _hasBaseMuzzleRotation = true;
        }

        private void ApplyHudCalibrationToMuzzle()
        {
            if (!applyHudCalibration || muzzle == null || !_hasBaseMuzzleRotation)
            {
                return;
            }

            muzzle.localRotation = _baseLocalMuzzleRotation * Quaternion.Euler(hudCalibrationPitchDeg, hudCalibrationYawDeg, 0f);
        }

        /// <summary>Drag deltas from HUD (typically from <see cref="WeaponAimHudIndicators"/>).</summary>
        public void AddHudCalibrationDelta(float deltaPitchDeg, float deltaYawDeg)
        {
            if (!applyHudCalibration)
            {
                return;
            }

            CaptureMuzzleBaseRotation();
            hudCalibrationPitchDeg += deltaPitchDeg;
            hudCalibrationYawDeg += deltaYawDeg;
        }

        public void ResetHudCalibration()
        {
            hudCalibrationPitchDeg = 0f;
            hudCalibrationYawDeg = 0f;
        }

        public void Tick(float deltaTime)
        {
            _weaponHeat = Mathf.Max(0f, _weaponHeat - (useHeatLockout ? heatCoolingPerSecond : .35f) * deltaTime);
            if (useHeatLockout && _overheated && _weaponHeat <= Mathf.Min(resumeHeatThreshold, overheatThreshold - .01f))
                _overheated = false;

            if (_reloading && Time.time >= _reloadEndTime)
            {
                _reloading = false;
                _ammoInMagazine = AmmoPerMagazineValue;
                if (verboseLogs) Debug.Log("[Weapon] Reload complete", this);
            }
        }

        /// <summary>Human-readable reason for <see cref="CanFire"/> being false, or a ready/OK line for diagnostics.</summary>
        public string GetShootBlockReason()
        {
            if (_shipPowerOnRoot == null)
            {
                _shipPowerOnRoot = transform.root.GetComponentInChildren<ShipPowerController>(true);
            }

            if (_shipPowerOnRoot != null && !_shipPowerOnRoot.hudPowered)
            {
                return "BLOCK: ship HUD / 24V power off";
            }

            if (_reloading)
            {
                return "BLOCK: magazine reloading";
            }

            if (ProjectilePrefabValue == null)
            {
                return "BLOCK: no Projectile Prefab assigned";
            }

            if (muzzle == null)
            {
                return "BLOCK: Muzzle transform not assigned";
            }

            if (Time.time < _nextFireTime)
            {
                return $"BLOCK: fire cooldown ({_nextFireTime - Time.time:F2}s remaining)";
            }

            if (RequiresDeployedHardpointValue && hardpoint != null && !hardpoint.CanFire)
            {
                return $"BLOCK: hardpoint not deployed (state={hardpoint.State}, need {nameof(HardpointState.Deployed)})";
            }

            if (!infiniteAmmoForDebug && AmmoPerMagazineValue > 0 && _ammoInMagazine <= 0)
            {
                return "BLOCK: magazine empty";
            }

            if (useHeatLockout ? _overheated || _weaponHeat >= overheatThreshold : _weaponHeat >= 1f)
            {
                return "BLOCK: weapon overheated";
            }

            if (UseGatlingAudioValue
                && GetEffectiveGatlingSpinUpSeconds() > 1e-5f
                && Time.time < _gatlingSpinUpEndTime)
            {
                return $"BLOCK: gatling spin-up ({_gatlingSpinUpEndTime - Time.time:F2}s left)";
            }

            return "OK — CanFire would allow a shot";
        }

        public bool TryFire()
        {
            if (!CanFire)
            {
                return false;
            }

            SpawnProjectile();
            _nextFireTime = Time.time + 1f / Mathf.Max(0.01f, FireRateValue);
            _weaponHeat = Mathf.Clamp01(_weaponHeat + WeaponHeatPerShotValue);
            if (useHeatLockout && _weaponHeat >= overheatThreshold) _overheated = true;

            if (!infiniteAmmoForDebug && AmmoPerMagazineValue > 0)
            {
                _ammoInMagazine--;
                if (_ammoInMagazine <= 0 && ReloadSecondsValue > 0f)
                {
                    _reloading = true;
                    _reloadEndTime = Time.time + ReloadSecondsValue;
                }
            }

            return true;
        }

        public void SetInfiniteAmmoForDebug(bool enabled)
        {
            infiniteAmmoForDebug = enabled;
        }

        private void SpawnProjectile()
        {
            if (ProjectilePrefabValue == null || muzzle == null)
            {
                if (verboseLogs) Debug.LogWarning("[Weapon] Missing projectile prefab or muzzle.", this);
                return;
            }

            GameObject projectile = Instantiate(ProjectilePrefabValue, muzzle.position, muzzle.rotation);
            Rigidbody body = projectile.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = projectile.GetComponentInChildren<Rigidbody>(true);
            }

            if (body == null)
            {
                body = projectile.AddComponent<Rigidbody>();
                body.mass = 0.05f;
            }

            body.useGravity = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            GameObject physicsGo = body.gameObject;
            foreach (WeaponProjectile orphan in projectile.GetComponentsInChildren<WeaponProjectile>(true))
            {
                if (orphan != null && orphan.gameObject != physicsGo)
                {
                    Destroy(orphan);
                }
            }

            WeaponProjectile wp = physicsGo.GetComponent<WeaponProjectile>();
            if (wp == null)
            {
                wp = physicsGo.AddComponent<WeaponProjectile>();
            }

            wp.Configure(
                WeaponIdValue,
                DamageTypeValue,
                ShieldInteractionValue,
                HullInteractionValue,
                HitImpactPrefabValue,
                HitImpactScaleValue,
                impactVfxLibrary,
                HitScorchPrefabValue,
                HitScorchUsesUpValue,
                HitScorchScaleValue,
                HitScorchOffsetValue,
                HitScorchRollValue,
                HitScorchLifetimeValue,
                HitScorchParentValue,
                gameObject);
            IgnoreProjectileCollisionsWithSource(body.gameObject);

            float speed = ProjectileSpeedValue;
            SnapProjectileToMuzzle(body, muzzle, speed);

            if (ShouldLogNpcSourceDebug())
            {
                Debug.Log(
                    $"[WeaponController] fired weapon='{name}' mode={DamageTypeValue} hull={HullInteractionValue:F2} shield={ShieldInteractionValue:F2} muzzle='{muzzle.name}' speed={speed:F1}",
                    this);
            }

            PlayFireSound();

            if (verboseLogs)
            {
                Debug.Log(
                    $"[Weapon] '{name}' spawned: speed={speed}, rb on '{physicsGo.name}', " +
                    $"aligned to muzzle pos/rot",
                    this);
            }
        }

        private bool ShouldLogNpcSourceDebug()
        {
            NPCShipAI npc = GetComponentInParent<NPCShipAI>();
            return npc != null && npc.ShowDebugLogs;
        }

        /// <summary>
        /// Prevents projectile colliders from colliding with the firing ship colliders.
        /// This avoids self-collisions at high projectile speeds / short muzzle offsets.
        /// </summary>
        private void IgnoreProjectileCollisionsWithSource(GameObject projectilePhysicsRoot)
        {
            if (projectilePhysicsRoot == null || _sourceRootCached == null)
            {
                return;
            }

            Collider[] projectileCols = projectilePhysicsRoot.GetComponentsInChildren<Collider>(true);
            if (projectileCols == null || projectileCols.Length == 0)
            {
                return;
            }

            Collider[] sourceCols = _sourceRootCached.GetComponentsInChildren<Collider>(true);
            if (sourceCols == null || sourceCols.Length == 0)
            {
                return;
            }

            for (int i = 0; i < projectileCols.Length; i++)
            {
                Collider pc = projectileCols[i];
                if (pc == null) continue;

                for (int j = 0; j < sourceCols.Length; j++)
                {
                    Collider sc = sourceCols[j];
                    if (sc == null || sc == pc) continue;
                    Physics.IgnoreCollision(pc, sc, true);
                }
            }
        }

        /// <summary>
        /// Places the moving rigidbody exactly on the muzzle and sets velocity along muzzle forward.
        /// Call after Awake/Configure so any prefab setup cannot drift the shot line.
        /// </summary>
        private static void SnapProjectileToMuzzle(Rigidbody body, Transform muzzle, float speed)
        {
            if (body == null || muzzle == null)
            {
                return;
            }

            body.transform.SetPositionAndRotation(muzzle.position, muzzle.rotation);
            body.linearVelocity = muzzle.forward * speed;
            body.angularVelocity = Vector3.zero;
        }

        private void PlayFireSound()
        {
            float vol = FireSoundVolumeValue;

            if (UseGatlingAudioValue)
            {
                // Sustain is started by SetGatlingTriggerHeld → TryStartGatlingSustainAudio (not per projectile).
                if (!_gatlingSustainActive)
                {
                    TryStartGatlingSustainAudio();
                }

                return;
            }

            AudioClip clip = FireSoundValue;
            if (clip == null)
            {
                return;
            }

            PlayOneShotOrAtPoint(clip, vol);
        }

        private void PlayOneShotOrAtPoint(AudioClip clip, float volume)
        {
            Vector3 pos = muzzle != null ? muzzle.position : transform.position;
            if (fireAudioSource != null)
            {
                fireAudioSource.PlayOneShot(clip, volume);
            }
            else
            {
                AudioSource.PlayClipAtPoint(clip, pos, volume);
            }
        }
    }
}
