using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace JunkerEquipment
{
    public enum OpticalVisionMode { Normal, Thermal, Loot }

    /// <summary>Physical eyepiece camera; never modifies the XR/HMD camera.</summary>
    public sealed class OpticalDevice : MonoBehaviour
    {
        public Transform eye, eyepiece, objective;
        public Camera opticsCamera;
        public XRGrabInteractable requiredGrab;
        public bool requireHeld = true;
        public Renderer opticalDisplayRenderer;
        public int displayMaterialSlot;
        public RawImage opticalDisplayImage;
        public bool hideDedicatedDisplayWhenInactive = true;
        [Header("Viewing window (binoculars and scopes)")]
        [Tooltip("Show the optical image in both eyes with black outside the viewing circle.")]
        public bool immersiveViewing = true;
        public Shader viewingShader;
        [Range(.1f, .49f)] public float viewingRadius = .43f;
        [Range(.001f, .05f)] public float viewingEdgeSoftness = .01f;
        [Tooltip("Distance in metres to the shared virtual image plane. Both eyes converge on this plane.")]
        [Range(.5f, 10f)] public float viewingFocusDistance = 2f;
        [Tooltip("Correct the UV orientation of a physical quad rotated 180 degrees towards the eye.")]
        public bool flipPhysicalLensHorizontally = true;
        [Header("Scope reticle")]
        public bool showReticle;
        public Color reticleColor = new Color(1f, .35f, .12f, .9f);
        public float activationDistance = .22f, activationAngle = 25f;
        public float baseFieldOfView = 60f, nearClip = .04f, farClip = 500f;
        [Range(512, 2048)] public int textureResolution = 1536;
        public float[] zoomLevels = { 2f, 4f, 8f };
        public float zoomSmoothTime = .12f;
        [Header("Optical stabilisation (headset tracking stays unsmoothed)")]
        public bool stabiliseView;
        [Min(.001f)] public float rotationSmoothTime = .18f;
        [Min(.001f)] public float positionSmoothTime = .10f;
        [Range(0, 1)] public float angularDeadZone = .12f;
        [Min(.001f)] public float fastTurnSmoothTime = .035f;
        public OpticalVisionMode visionMode = OpticalVisionMode.Normal;
        public bool debugTransitions;
        [SerializeField] string activationStatus = "Not playing";

        public bool IsViewing { get; private set; }
        public float CurrentZoom => zoomLevels != null && zoomLevels.Length > 0 ? zoomLevels[Mathf.Clamp(zoomIndex, 0, zoomLevels.Length - 1)] : 1f;
        public RenderTexture OutputTexture => output;

        RenderTexture output;
        MaterialPropertyBlock propertyBlock;
        int zoomIndex;
        float fovVelocity;
        int textureProperty;
        bool originalRendererEnabled;
        Transform displayTransform;
        Vector3 originalDisplayLocalPosition;
        Camera eyeCamera;
        GameObject viewingOverlay;
        Renderer viewingRenderer;
        Material viewingMaterial;
        Renderer[] ownRenderers;
        bool[] ownRendererForceOff;
        bool ownRenderersHidden;
        float viewingReferenceTangent;
        Vector3 stablePosition;
        Quaternion stableRotation;
        static readonly int ImmersiveId = Shader.PropertyToID("_ImmersiveView");
        static readonly int RadiusId = Shader.PropertyToID("_ViewingRadius");
        static readonly int EdgeId = Shader.PropertyToID("_EdgeSoftness");
        static readonly int FlipId = Shader.PropertyToID("_FlipHorizontal");
        static readonly int FocusId = Shader.PropertyToID("_ViewingFocusDistance");
        static readonly int TangentRadiusId = Shader.PropertyToID("_ViewingTangentRadius");
        static readonly int WorldToViewingId = Shader.PropertyToID("_ViewingWorldToLocal");
        static readonly int ReticleEnabledId = Shader.PropertyToID("_ReticleEnabled");
        static readonly int ReticleColorId = Shader.PropertyToID("_ReticleColor");

        void Awake()
        {
            if (!eye && Camera.main) eye = Camera.main.transform;
            if (!requiredGrab) requiredGrab = GetComponent<XRGrabInteractable>();
            if (opticalDisplayRenderer) originalRendererEnabled = opticalDisplayRenderer.enabled;
            if (opticalDisplayRenderer)
            {
                displayTransform = opticalDisplayRenderer.transform;
                originalDisplayLocalPosition = displayTransform.localPosition;
            }
            if (eye) eyeCamera = eye.GetComponent<Camera>();
            if (!eyeCamera) eyeCamera = Camera.main;
            if (opticsCamera && opticsCamera == eyeCamera)
            {
                Debug.LogError("Assign a separate camera to Optics Camera; the player's camera cannot be used as the optical source.", this);
                opticsCamera = null;
            }
            if (opticsCamera)
            {
                opticsCamera.enabled = false;
                opticsCamera.aspect = 1f;
                opticsCamera.allowMSAA = true;
                if (eyeCamera)
                {
                    opticsCamera.allowHDR = eyeCamera.allowHDR;
                    opticsCamera.cullingMask = eyeCamera.cullingMask;
                    opticsCamera.clearFlags = eyeCamera.clearFlags;
                    opticsCamera.backgroundColor = eyeCamera.backgroundColor;
                    opticsCamera.GetUniversalAdditionalCameraData().renderPostProcessing =
                        eyeCamera.GetUniversalAdditionalCameraData().renderPostProcessing;
                }
                // The source is one ordinary 2D image, shared by the two eye displays.
                opticsCamera.GetUniversalAdditionalCameraData().allowXRRendering = false;
            }
            ownRenderers = GetComponentsInChildren<Renderer>(true);
            ownRendererForceOff = new bool[ownRenderers.Length];
            if (hideDedicatedDisplayWhenInactive && opticalDisplayRenderer) opticalDisplayRenderer.enabled = false;
            if (opticalDisplayImage) opticalDisplayImage.enabled = false;
        }

        void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
        }

        void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera == opticsCamera && IsViewing)
            {
                for (int i = 0; i < ownRenderers.Length; i++)
                    if (ownRenderers[i])
                    {
                        ownRendererForceOff[i] = ownRenderers[i].forceRenderingOff;
                        ownRenderers[i].forceRenderingOff = true;
                    }
                ownRenderersHidden = true;
            }
            // XR can update the hand/parent before rendering; restore the filtered
            // world pose without introducing latency into headset tracking.
            if (camera == opticsCamera && IsViewing && stabiliseView)
                opticsCamera.transform.SetPositionAndRotation(stablePosition, stableRotation);
            // An optical camera must never capture the viewing overlay recursively.
            if (viewingRenderer) viewingRenderer.forceRenderingOff = camera != eyeCamera;
            // Capture the latest head pose immediately before its eyes render.
            if (camera == eyeCamera && IsViewing) UpdateViewingMapping();
        }

        void EndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != opticsCamera || ownRenderers == null || !ownRenderersHidden) return;
            for (int i = 0; i < ownRenderers.Length; i++)
                if (ownRenderers[i]) ownRenderers[i].forceRenderingOff = ownRendererForceOff[i];
            ownRenderersHidden = false;
        }

        void Update()
        {
            if (!eye && Camera.main) eye = Camera.main.transform;
            if (!eyeCamera && eye) eyeCamera = eye.GetComponent<Camera>();
            bool held = !requireHeld || (requiredGrab && requiredGrab.isSelected);
            bool close = eye && eyepiece && (eyepiece.position - eye.position).sqrMagnitude <= activationDistance * activationDistance;
            bool inFront = eye && eyepiece && Vector3.Dot(eye.forward, eyepiece.position - eye.position) > 0f;
            bool aligned = eye && eyepiece && objective &&
                Vector3.Angle(objective.forward, eye.forward) <= activationAngle &&
                Vector3.Angle(eyepiece.forward, eye.forward) <= activationAngle;
            bool active = eye && eyepiece && objective && opticsCamera && held && close && inFront && aligned;
            string status = !eye ? "No XR camera" : !opticsCamera || !eyepiece || !objective ? "Missing optics reference" :
                !held ? "Grab binoculars" : !close ? "Move eyepiece closer" : !inFront ? "Eyepiece behind head" :
                !aligned ? "Align binoculars with gaze" : "Viewing";
            if (status != activationStatus) { activationStatus = status; if (debugTransitions) Debug.Log($"{name}: {status}", this); }
            if (active != IsViewing) SetViewing(active);
            if (!IsViewing) return;
            // Keep a physical display visible beyond the XR camera's near clip plane.
            // The overlay material prevents the binocular shell from occluding it.
            if (!immersiveViewing && displayTransform && eyeCamera)
            {
                float depth = Vector3.Dot(eye.forward, displayTransform.parent.TransformPoint(originalDisplayLocalPosition) - eye.position);
                float shift = Mathf.Clamp(eyeCamera.nearClipPlane + .025f - depth, 0f, .22f);
                displayTransform.localPosition = originalDisplayLocalPosition + Vector3.forward * shift;
            }
            float targetFov = ZoomFieldOfView();
            opticsCamera.fieldOfView = Mathf.SmoothDamp(opticsCamera.fieldOfView, targetFov, ref fovVelocity, Mathf.Max(.01f, zoomSmoothTime));
        }

        float ZoomFieldOfView()
        {
            float halfAngle = Mathf.Clamp(baseFieldOfView, 1f, 179f) * Mathf.Deg2Rad * .5f;
            return Mathf.Clamp(2f * Mathf.Atan(Mathf.Tan(halfAngle) / Mathf.Max(.01f, CurrentZoom)) * Mathf.Rad2Deg, 1f, 179f);
        }

        void LateUpdate()
        {
            if (!IsViewing) return;
            if (stabiliseView)
            {
                FilterPose(objective.position, objective.rotation, Time.unscaledDeltaTime);
                opticsCamera.transform.SetPositionAndRotation(stablePosition, stableRotation);
            }
            else opticsCamera.transform.SetPositionAndRotation(objective.position, objective.rotation);
            if (viewingOverlay)
            {
                // Bounds stay in front of the eye so normal renderer culling cannot
                // remove the full-screen shader when the held item is off-center.
                var t = viewingOverlay.transform;
                t.localPosition = new Vector3(0, 0, Mathf.Max(.3f, eyeCamera ? eyeCamera.nearClipPlane + .1f : .3f));
                t.localRotation = Quaternion.identity;
                viewingMaterial.SetFloat(RadiusId, viewingRadius);
                viewingMaterial.SetFloat(EdgeId, viewingEdgeSoftness);
                viewingMaterial.SetFloat(ReticleEnabledId, showReticle ? 1f : 0f);
                viewingMaterial.SetColor(ReticleColorId, reticleColor);
                UpdateViewingMapping();
            }
        }

        void FilterPose(Vector3 position, Quaternion rotation, float deltaTime)
        {
            float dt = Mathf.Max(0, deltaTime);
            float zoomWeight = Mathf.Clamp(CurrentZoom / 2f, 1f, 2f);
            float angle = Quaternion.Angle(stableRotation, rotation);
            float deadZone = Mathf.Max(0, angularDeadZone);
            if (angle > deadZone)
            {
                var target = Quaternion.Slerp(stableRotation, rotation, (angle - deadZone) / angle);
                float turning = Mathf.InverseLerp(1f, 8f, angle);
                float time = Mathf.Lerp(rotationSmoothTime * zoomWeight, fastTurnSmoothTime, turning);
                stableRotation = Quaternion.Slerp(stableRotation, target, 1f - Mathf.Exp(-dt / Mathf.Max(.001f, time)));
            }
            stablePosition = Vector3.Lerp(stablePosition, position,
                1f - Mathf.Exp(-dt / Mathf.Max(.001f, positionSmoothTime * zoomWeight)));
        }

        static float SmallestTangent(Matrix4x4 projection)
        {
            return Mathf.Min(1f / Mathf.Max(.001f, Mathf.Abs(projection.m00)),
                1f / Mathf.Max(.001f, Mathf.Abs(projection.m11)));
        }

        void UpdateViewingMapping()
        {
            if (!viewingMaterial || !eyeCamera) return;
            if (eyeCamera.stereoEnabled)
            {
                viewingReferenceTangent = Mathf.Min(
                    SmallestTangent(eyeCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)),
                    SmallestTangent(eyeCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right)));
            }
            else if (viewingReferenceTangent <= 0)
                viewingReferenceTangent = SmallestTangent(eyeCamera.projectionMatrix);
            // The two projections determine a single angular size, rather than
            // separately stretching the source to fill each eye's viewport.
            viewingMaterial.SetFloat(TangentRadiusId, 2f * viewingRadius * viewingReferenceTangent);
            viewingMaterial.SetFloat(FocusId, Mathf.Max(.5f, viewingFocusDistance));
            viewingMaterial.SetMatrix(WorldToViewingId, Matrix4x4.TRS(eyeCamera.transform.position,
                eyeCamera.transform.rotation, Vector3.one).inverse);
        }

        bool EnsureViewingOverlay()
        {
            if (viewingOverlay) return true;
            if (!viewingShader) viewingShader = Shader.Find("JunkerEquipment/OpticalEyepiece");
            if (!viewingShader) { Debug.LogError("Optical viewing shader is missing. Assign Viewing Shader on OpticalDevice.", this); return false; }
            viewingOverlay = GameObject.CreatePrimitive(PrimitiveType.Quad);
            viewingOverlay.name = name + " Viewing Window";
            var collider = viewingOverlay.GetComponent<Collider>();
            collider.enabled = false;
            ReleaseOwned(collider);
            viewingOverlay.transform.SetParent(eye, false);
            viewingOverlay.transform.localPosition = new Vector3(0, 0, Mathf.Max(.3f, eyeCamera ? eyeCamera.nearClipPlane + .1f : .3f));
            // Choose a layer the XR camera renders; no project layers are renamed.
            if (eyeCamera)
                for (int layer = 0; layer < 32; layer++)
                    if ((eyeCamera.cullingMask & (1 << layer)) != 0) { viewingOverlay.layer = layer; break; }
            viewingRenderer = viewingOverlay.GetComponent<Renderer>();
            viewingRenderer.shadowCastingMode = ShadowCastingMode.Off;
            viewingRenderer.receiveShadows = false;
            viewingRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            viewingMaterial = new Material(viewingShader) { name = name + " Viewing Window", enableInstancing = true };
            viewingMaterial.SetFloat(ImmersiveId, 1f);
            viewingMaterial.SetFloat(RadiusId, viewingRadius);
            viewingMaterial.SetFloat(EdgeId, viewingEdgeSoftness);
            viewingMaterial.SetFloat(ReticleEnabledId, showReticle ? 1f : 0f);
            viewingMaterial.SetColor(ReticleColorId, reticleColor);
            viewingRenderer.sharedMaterial = viewingMaterial;
            UpdateViewingMapping();
            return true;
        }

        void SetViewing(bool active)
        {
            IsViewing = active;
            if (active)
            {
                EnsureOutput();
                opticsCamera.fieldOfView = ZoomFieldOfView();
                opticsCamera.aspect = 1f;
                opticsCamera.nearClipPlane = Mathf.Max(.001f, nearClip);
                opticsCamera.farClipPlane = Mathf.Max(opticsCamera.nearClipPlane + .01f, farClip);
                opticsCamera.targetTexture = output;
                if (immersiveViewing && EnsureViewingOverlay())
                {
                    viewingMaterial.SetTexture("_BaseMap", output);
                    viewingRenderer.enabled = true;
                    if (hideDedicatedDisplayWhenInactive && opticalDisplayRenderer) opticalDisplayRenderer.enabled = false;
                }
                else if (opticalDisplayRenderer)
                {
                    propertyBlock ??= new MaterialPropertyBlock();
                    opticalDisplayRenderer.GetPropertyBlock(propertyBlock, displayMaterialSlot);
                    propertyBlock.SetTexture(textureProperty, output);
                    propertyBlock.SetFloat(ImmersiveId, 0f);
                    propertyBlock.SetFloat(FlipId, flipPhysicalLensHorizontally ? 1f : 0f);
                    opticalDisplayRenderer.SetPropertyBlock(propertyBlock, displayMaterialSlot);
                    if (hideDedicatedDisplayWhenInactive) opticalDisplayRenderer.enabled = true;
                }
                if (opticalDisplayImage) { opticalDisplayImage.texture = output; opticalDisplayImage.enabled = !immersiveViewing; }
                opticsCamera.transform.SetPositionAndRotation(objective.position, objective.rotation);
                stablePosition = objective.position;
                stableRotation = objective.rotation;
                opticsCamera.enabled = true;
            }
            else
            {
                if (opticsCamera) opticsCamera.enabled = false;
                if (viewingRenderer) viewingRenderer.enabled = false;
                if (displayTransform) displayTransform.localPosition = originalDisplayLocalPosition;
                if (hideDedicatedDisplayWhenInactive && opticalDisplayRenderer) opticalDisplayRenderer.enabled = false;
                if (opticalDisplayImage) opticalDisplayImage.enabled = false;
            }
            if (debugTransitions) Debug.Log($"{name}: optics {(active ? "activated" : "deactivated")}", this);
        }

        void EnsureOutput()
        {
            if (!output)
            {
                int size = Mathf.Clamp(textureResolution, 64, 2048);
                output = new RenderTexture(size, size, 24, RenderTextureFormat.ARGBHalf)
                {
                    name = name + " Optics", useMipMap = false, autoGenerateMips = false,
                    // The higher source resolution smooths the image without MSAA interfering
                    // with camera-space HUD compositing on URP/XR render targets.
                    antiAliasing = 1, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp
                };
                output.Create();
            }
            textureProperty = Shader.PropertyToID("_BaseMap");
            if (opticalDisplayRenderer && displayMaterialSlot >= 0 && displayMaterialSlot < opticalDisplayRenderer.sharedMaterials.Length)
            {
                var material = opticalDisplayRenderer.sharedMaterials[displayMaterialSlot];
                if (material && !material.HasProperty(textureProperty)) textureProperty = Shader.PropertyToID("_MainTex");
            }
        }

        public void CycleZoom()
        {
            if (!IsViewing || zoomLevels == null || zoomLevels.Length == 0) return;
            zoomIndex = (zoomIndex + 1) % zoomLevels.Length;
            if (debugTransitions) Debug.Log($"{name}: optics zoom {CurrentZoom:0.#}x", this);
        }

        // Placeholder: later vision modes can replace the camera shader/overlay here.
        public void CycleVisionMode() { }

        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            EndCameraRendering(default, opticsCamera);
            if (IsViewing) SetViewing(false);
            if (opticsCamera) opticsCamera.enabled = false;
        }

        void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            EndCameraRendering(default, opticsCamera);
            if (opticsCamera) opticsCamera.targetTexture = null;
            if (opticalDisplayRenderer && propertyBlock != null)
            {
                opticalDisplayRenderer.GetPropertyBlock(propertyBlock, displayMaterialSlot);
                propertyBlock.SetTexture(textureProperty, null);
                opticalDisplayRenderer.SetPropertyBlock(propertyBlock, displayMaterialSlot);
                opticalDisplayRenderer.enabled = originalRendererEnabled;
            }
            if (output) { output.Release(); ReleaseOwned(output); }
            if (viewingOverlay) ReleaseOwned(viewingOverlay);
            if (viewingMaterial) ReleaseOwned(viewingMaterial);
        }

        static void ReleaseOwned(Object value)
        {
            if (Application.isPlaying) Destroy(value);
            else DestroyImmediate(value);
        }

        void OnDrawGizmosSelected()
        {
            if (eyepiece)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(eyepiece.position, activationDistance);
                Gizmos.DrawRay(eyepiece.position, eyepiece.forward * .2f);
            }
            if (objective)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawRay(objective.position, objective.forward * .5f);
                Quaternion left = Quaternion.AngleAxis(-activationAngle, objective.up);
                Quaternion right = Quaternion.AngleAxis(activationAngle, objective.up);
                Gizmos.DrawRay(objective.position, left * objective.forward * .3f);
                Gizmos.DrawRay(objective.position, right * objective.forward * .3f);
            }
        }
    }
}
