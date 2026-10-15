using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Ship audio mixer/controller:
/// - Engine idle loops with throttle-based pitch/volume
/// - Optional seamless loop crossfade (two staggered copies per clip) to reduce loop-seam clicks
/// - Landing gear movement one-shot
/// - Editor auto-assign helper for clips in Assets/MyAssets/Audio
/// </summary>
public class ShipAudioController : MonoBehaviour
{
    [Header("References")]
    public ShipPowerController powerController;
    public HOTASController hotasController;
    public FlightController flightController;
    public LandingGearController landingGearController;
    [Tooltip("Optional explicit root for audio sources. If null, uses ship rigidbody transform when available.")]
    public Transform audioRoot;

    [Header("Engine clips")]
    public AudioClip engineIdleLoopA;   // e.g. idleEngineLoop.wav
    public AudioClip engineIdleLoopB;   // e.g. idleEngineLoop2.wav (optional)

    [Header("Mechanics clips")]
    public AudioClip landingGearMoveClip; // e.g. landingGear (1).wav

    [Header("Audio sources")]
    public AudioSource engineSourceA;
    [Tooltip("Second copy of loop A for seamless crossfade. Auto-created under EngineLoopA2 when crossfade is on.")]
    public AudioSource engineSourceA2;
    public AudioSource engineSourceB;
    [Tooltip("Second copy of loop B for seamless crossfade. Auto-created under EngineLoopB2 when crossfade is on.")]
    public AudioSource engineSourceB2;
    public AudioSource mechanicsSource;

    [Header("Seamless loop crossfade")]
    [Tooltip("Uses two AudioSources per clip, offset in time, with complementary volume so the wrap point is never full level.")]
    public bool seamlessEngineLoopCrossfade = true;
    [Range(0.02f, 0.45f)]
    [Tooltip("Overlap at each loop boundary. Longer loops can use more (e.g. 0.12–0.2s). Capped automatically vs clip length.")]
    public float engineLoopCrossfadeSeconds = 0.12f;

    [Header("Engine tuning")]
    [Range(0f, 1f)] public float engineBaseVolume = 0.2f;
    [Range(0f, 1f)] public float engineThrottleVolumeBoost = 0.7f;
    [Range(0.3f, 2f)] public float engineBasePitch = 0.85f;
    [Range(0f, 2f)] public float engineThrottlePitchBoost = 0.45f;
    [Range(0.1f, 20f)] public float engineSmooth = 6f;
    [Range(0f, 1f)] public float engineOfflineFadeSpeed = 0.9f;

    [Header("Landing gear tuning")]
    [Range(0f, 1f)] public float landingGearVolume = 0.8f;

    bool _lastGearMoving;
    Transform _currentAudioRoot;
    float _smoothedEngineVol;
    float _smoothedEnginePitch;

    void Awake()
    {
        _smoothedEnginePitch = engineBasePitch;
        ResolveRefs();
        EnsureSources();
    }

    void Start()
    {
        ResolveRefs();
        EnsureSources();
        ApplyEngineClipSetup();
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        if (engineIdleLoopA == null)
            engineIdleLoopA = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MyAssets/Audio/idleEngineLoop.wav");
        if (engineIdleLoopB == null)
            engineIdleLoopB = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MyAssets/Audio/idleEngineLoop2.wav");
        if (landingGearMoveClip == null)
            landingGearMoveClip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/MyAssets/Audio/landingGear (1).wav");
#endif
    }

    void Update()
    {
        ResolveRefs();
        UpdateEngineAudio(Time.deltaTime);
        UpdateLandingGearAudio();
    }

    void ResolveRefs()
    {
        if (powerController == null)
            powerController = GetComponentInParent<ShipPowerController>();
        if (powerController == null)
            powerController = FindFirstObjectByType<ShipPowerController>();

        if (hotasController == null)
            hotasController = FindFirstObjectByType<HOTASController>();

        if (flightController == null && powerController != null)
            flightController = powerController.flightController;
        if (flightController == null)
            flightController = FindFirstObjectByType<FlightController>();

        if (landingGearController == null)
            landingGearController = FindFirstObjectByType<LandingGearController>();
    }

    void EnsureSources()
    {
        EnsureAudioRoot();

        if (engineSourceA == null)
            engineSourceA = CreateOrGetSource("EngineLoopA");
        if (engineSourceB == null)
            engineSourceB = CreateOrGetSource("EngineLoopB");
        if (seamlessEngineLoopCrossfade)
        {
            if (engineSourceA2 == null)
                engineSourceA2 = CreateOrGetSource("EngineLoopA2");
            if (engineSourceB2 == null)
                engineSourceB2 = CreateOrGetSource("EngineLoopB2");
        }
        if (mechanicsSource == null)
            mechanicsSource = CreateOrGetSource("Mechanics");
    }

    void EnsureAudioRoot()
    {
        Transform desired = audioRoot;
        if (desired == null && powerController != null && powerController.flightController != null && powerController.flightController.rb != null)
            desired = powerController.flightController.rb.transform;
        if (desired == null && powerController != null && powerController.flightController != null)
            desired = powerController.flightController.transform;
        if (desired == null)
            desired = transform;

        if (_currentAudioRoot == desired) return;
        _currentAudioRoot = desired;

        ReparentSource(engineSourceA);
        ReparentSource(engineSourceA2);
        ReparentSource(engineSourceB);
        ReparentSource(engineSourceB2);
        ReparentSource(mechanicsSource);
    }

    AudioSource CreateOrGetSource(string childName)
    {
        Transform root = _currentAudioRoot != null ? _currentAudioRoot : transform;
        Transform child = root.Find(childName);
        if (child == null)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(root, false);
            child = go.transform;
        }
        var src = child.GetComponent<AudioSource>();
        if (src == null)
            src = child.gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 1f;
        src.minDistance = 1f;
        src.maxDistance = 25f;
        src.loop = true;
        return src;
    }

    void ReparentSource(AudioSource src)
    {
        if (src == null || _currentAudioRoot == null) return;
        if (src.transform.parent == _currentAudioRoot) return;
        src.transform.SetParent(_currentAudioRoot, true);
    }

    void ApplyEngineClipSetup()
    {
        if (engineSourceA != null) engineSourceA.clip = engineIdleLoopA;
        if (engineSourceA2 != null) engineSourceA2.clip = engineIdleLoopA;
        if (engineSourceB != null) engineSourceB.clip = engineIdleLoopB;
        if (engineSourceB2 != null) engineSourceB2.clip = engineIdleLoopB;
    }

    /// <summary>
    /// Raised-cosine pair for staggered loop playback: volumes sum to 1, handoff at loop ends.
    /// </summary>
    static void SeamlessLoopPairVolumes(float timeInLoop, float loopLen, float crossfadeSeconds, out float primary, out float secondary)
    {
        primary = 1f;
        secondary = 0f;
        if (loopLen <= 0.001f)
            return;
        float cf = Mathf.Clamp(crossfadeSeconds, 0.001f, loopLen * 0.45f);
        float t = Mathf.Clamp(timeInLoop, 0f, loopLen);

        if (t < cf)
        {
            float u = t / cf;
            float s = Mathf.Sin(Mathf.PI * 0.5f * u);
            float c = Mathf.Cos(Mathf.PI * 0.5f * u);
            primary = s * s;
            secondary = c * c;
        }
        else if (t > loopLen - cf)
        {
            float u = (t - (loopLen - cf)) / cf;
            float s = Mathf.Sin(Mathf.PI * 0.5f * u);
            float c = Mathf.Cos(Mathf.PI * 0.5f * u);
            primary = c * c;
            secondary = s * s;
        }
    }

    static float EffectiveCrossfadeSeconds(float loopLen, float requested)
    {
        if (loopLen <= 0.001f) return 0f;
        return Mathf.Clamp(requested, 0.02f, loopLen * 0.45f);
    }

    void StartStaggeredLoopPair(AudioSource lead, AudioSource follow, AudioClip clip)
    {
        if (lead == null || follow == null || clip == null) return;
        float cf = EffectiveCrossfadeSeconds(clip.length, engineLoopCrossfadeSeconds);
        lead.Stop();
        follow.Stop();
        lead.clip = clip;
        follow.clip = clip;
        lead.loop = true;
        follow.loop = true;
        lead.time = 0f;
        follow.time = Mathf.Max(0f, clip.length - cf);
        lead.Play();
        follow.Play();
    }

    void StopEnginePair(AudioSource a, AudioSource b)
    {
        if (a != null && a.isPlaying) a.Stop();
        if (b != null && b.isPlaying) b.Stop();
    }

    void UpdateEngineAudio(float dt)
    {
        bool enginesOnline = powerController == null || powerController.enginesOnline;
        float throttleForAudio = hotasController != null ? hotasController.Throttle : 0f;
        // Match reduced forward thrust when landing gear is down (same authority as throttle bar).
        if (flightController != null && throttleForAudio > 0f)
            throttleForAudio *= flightController.GetForwardThrottleAuthority();
        float throttleAbs = Mathf.Abs(throttleForAudio);
        float targetVol = enginesOnline ? Mathf.Clamp01(engineBaseVolume + throttleAbs * engineThrottleVolumeBoost) : 0f;
        float targetPitch = enginesOnline ? (engineBasePitch + throttleAbs * engineThrottlePitchBoost) : engineBasePitch;

        float speedVol = enginesOnline ? engineSmooth : Mathf.Max(0.01f, engineOfflineFadeSpeed);
        _smoothedEngineVol = Mathf.Lerp(_smoothedEngineVol, targetVol, Mathf.Clamp01(speedVol * dt));
        _smoothedEnginePitch = Mathf.Lerp(_smoothedEnginePitch, targetPitch, Mathf.Clamp01(engineSmooth * dt));

        bool useXfA = seamlessEngineLoopCrossfade && engineSourceA != null && engineSourceA2 != null;
        bool engineAudible = enginesOnline || _smoothedEngineVol > 0.005f;

        // Layer A (+ optional A2 crossfade)
        if (engineSourceA != null && engineIdleLoopA != null)
        {
            engineSourceA.clip = engineIdleLoopA;
            if (engineSourceA2 != null)
                engineSourceA2.clip = engineIdleLoopA;

            if (engineAudible)
            {
                if (useXfA)
                {
                    if (!engineSourceA.isPlaying || !engineSourceA2.isPlaying)
                        StartStaggeredLoopPair(engineSourceA, engineSourceA2, engineIdleLoopA);
                    float cf = EffectiveCrossfadeSeconds(engineIdleLoopA.length, engineLoopCrossfadeSeconds);
                    SeamlessLoopPairVolumes(engineSourceA.time, engineIdleLoopA.length, cf, out float v0, out float v1);
                    engineSourceA.volume = v0 * _smoothedEngineVol;
                    engineSourceA2.volume = v1 * _smoothedEngineVol;
                    engineSourceA.pitch = _smoothedEnginePitch;
                    engineSourceA2.pitch = _smoothedEnginePitch;
                }
                else
                {
                    if (engineSourceA2 != null && engineSourceA2.isPlaying)
                        engineSourceA2.Stop();
                    if (!engineSourceA.isPlaying)
                        engineSourceA.Play();
                    engineSourceA.pitch = _smoothedEnginePitch;
                    engineSourceA.volume = _smoothedEngineVol;
                }
            }
            else
                StopEnginePair(engineSourceA, engineSourceA2);
        }

        // Layer B (+ optional B2 crossfade), throttle-weighted
        if (engineSourceB != null && engineIdleLoopB != null)
        {
            engineSourceB.clip = engineIdleLoopB;
            if (engineSourceB2 != null)
                engineSourceB2.clip = engineIdleLoopB;

            float blend = Mathf.SmoothStep(0f, 1f, throttleAbs);
            float targetVolB = _smoothedEngineVol * blend * 0.8f;
            bool useXfB = seamlessEngineLoopCrossfade && engineSourceB2 != null;
            bool layerBAudible = engineAudible && targetVolB > 0.005f;

            if (layerBAudible)
            {
                if (useXfB)
                {
                    if (!engineSourceB.isPlaying || !engineSourceB2.isPlaying)
                        StartStaggeredLoopPair(engineSourceB, engineSourceB2, engineIdleLoopB);
                    float cfB = EffectiveCrossfadeSeconds(engineIdleLoopB.length, engineLoopCrossfadeSeconds);
                    SeamlessLoopPairVolumes(engineSourceB.time, engineIdleLoopB.length, cfB, out float vb0, out float vb1);
                    float pitchB = _smoothedEnginePitch * 1.05f;
                    engineSourceB.volume = vb0 * targetVolB;
                    engineSourceB2.volume = vb1 * targetVolB;
                    engineSourceB.pitch = pitchB;
                    engineSourceB2.pitch = pitchB;
                }
                else
                {
                    if (engineSourceB2 != null && engineSourceB2.isPlaying)
                        engineSourceB2.Stop();
                    if (!engineSourceB.isPlaying)
                        engineSourceB.Play();
                    engineSourceB.pitch = _smoothedEnginePitch * 1.05f;
                    engineSourceB.volume = targetVolB;
                }
            }
            else
                StopEnginePair(engineSourceB, engineSourceB2);
        }
        else
            StopEnginePair(engineSourceB, engineSourceB2);
    }

    void UpdateLandingGearAudio()
    {
        if (mechanicsSource == null || landingGearMoveClip == null || landingGearController == null)
            return;
        if (powerController != null && !powerController.hudPowered)
        {
            _lastGearMoving = false;
            return;
        }

        bool moving = landingGearController.IsMoving;
        if (moving && !_lastGearMoving)
        {
            mechanicsSource.loop = false;
            mechanicsSource.PlayOneShot(landingGearMoveClip, landingGearVolume);
        }
        _lastGearMoving = moving;
    }
}
