using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace VRKoombar.WeaponsStaging.Core
{
    /// <summary>
    /// Star Citizen–style PIPs: HUD markers where each weapon bore intersects the world (raycast) or max range.
    /// Optional <see cref="hudCalibrationMode"/>: drag dots to adjust <see cref="WeaponController"/> muzzle yaw/pitch offsets.
    /// Requires a Canvas <see cref="UnityEngine.UI.GraphicRaycaster"/> and an <see cref="UnityEngine.EventSystems.EventSystem"/> in the scene.
    /// Runtime UI dots are children of <see cref="dotsParent"/> named <c>AimDot_0</c>, <c>AimDot_1</c>, … (search the Hierarchy under your HUD panel).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("VR Koombar/Combat/Weapon Aim HUD Indicators")]
    [DefaultExecutionOrder(200)]
    public class WeaponAimHudIndicators : MonoBehaviour
    {
        [Header("Ship & view")]
        [Tooltip("Weapons are found under this root (colliders on this hierarchy are skipped by the aim ray).")]
        [SerializeField] private Transform shipRoot;

        [Tooltip("If set, only weapons in the orchestrator’s selected group (Primary/Secondary/Utility) get PIPs, and PIPs hide when the pilot seat gate is not satisfied. Auto-found from parents if empty.")]
        [SerializeField] private ShipWeaponsOrchestrator shipWeaponsOrchestrator;

        [Tooltip("Camera used for WorldToScreenPoint (use center-eye or the camera drawing this HUD in VR).")]
        [SerializeField] private Camera hudViewCamera;

        [Tooltip("Parent for aim dots (usually this object’s RectTransform).")]
        [SerializeField] private RectTransform dotsParent;

        [Header("Ray")]
        [SerializeField] private float maxRayDistance = 8000f;

        [SerializeField] private LayerMask raycastLayers = ~0;

        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;

        [Header("Dots")]
        [SerializeField] private float dotSizePixels = 16f;

        [SerializeField] private Color dotColor = new Color(1f, 0.88f, 0.15f, 0.92f);

        [SerializeField] private Color dotOutlineColor = new Color(0f, 0f, 0f, 0.9f);

        [SerializeField] private Vector2 dotOutlineDistance = new Vector2(2f, -2f);

        [Tooltip("If set, all aim dots use this sprite (including newly created dots).")]
        [SerializeField] private Sprite dotSprite;

        [Tooltip("Optional: RectTransform with an Image; if null, a simple square dot is created when no sprite is set.")]
        [SerializeField] private RectTransform dotPrefab;

        [SerializeField] private bool aimIndicatorsEnabled = true;

        [Header("HUD calibration (drag dots)")]
        [Tooltip("When on, you can drag aim dots to tweak each weapon’s muzzle yaw/pitch (see Weapon Controller HUD calibration).")]
        [SerializeField] private bool hudCalibrationMode;

        [Tooltip("Pointer delta → degrees on muzzle (tune to taste).")]
        [SerializeField] private float calibrationDragSensitivity = 0.06f;

        [Header("Stability")]
        [Tooltip("Smooth damp time for dot anchored positions (seconds). 0 = no smoothing.")]
        [SerializeField] private float dotPositionSmoothTime = 0.1f;

        [Header("Edit-mode PIP placeholders")]
        [Tooltip("When enabled, creates AimDot_0.. under Dots Parent in Edit Mode so they appear in the Hierarchy (layout/reference). Safe to leave on; runtime reuses them.")]
        [SerializeField] private bool createDotPlaceholdersInEditMode;

        [Tooltip("How many AimDot_N objects to keep when placeholders are enabled.")]
        [SerializeField] [Min(0)] private int editModeDotPlaceholderCount = 2;

        [HideInInspector] [SerializeField] private int _inspectorPreviewDotCount;

        [HideInInspector] [SerializeField] private Vector2[] _inspectorPreviewAnchored;

        [HideInInspector] [SerializeField] private Vector2[] _inspectorPreviewNormalized;

        [HideInInspector] [SerializeField] private Vector2 _inspectorPreviewParentHalf;

        private readonly List<Image> _dotImages = new List<Image>();
        private readonly List<WeaponController> _weaponOrder = new List<WeaponController>();
        private readonly List<Vector2> _dotSmoothPos = new List<Vector2>();
        private readonly List<Vector2> _dotSmoothVel = new List<Vector2>();

        private Canvas _canvas;
        private int _draggingDotIndex = -1;

        public bool HudCalibrationMode => hudCalibrationMode;

        private void Awake()
        {
            if (dotsParent == null)
            {
                dotsParent = transform as RectTransform;
            }

            _canvas = dotsParent != null ? dotsParent.GetComponentInParent<Canvas>() : GetComponentInParent<Canvas>();

            if (shipRoot == null)
            {
                var orch = GetComponentInParent<ShipWeaponsOrchestrator>();
                if (orch != null)
                {
                    shipRoot = orch.transform;
                }
            }

            if (shipWeaponsOrchestrator == null)
            {
                shipWeaponsOrchestrator = GetComponentInParent<ShipWeaponsOrchestrator>();
                if (shipWeaponsOrchestrator == null && shipRoot != null)
                {
                    shipWeaponsOrchestrator = shipRoot.GetComponentInChildren<ShipWeaponsOrchestrator>(true);
                }
            }

            if (hudViewCamera == null)
            {
                hudViewCamera = Camera.main;
            }

            CollectExistingDotImagesFromParent();
        }

        private void Start()
        {
            BuildWeaponOrder();
            SyncDotOutlineStyles();
        }

        private void LateUpdate()
        {
            if (!aimIndicatorsEnabled || dotsParent == null || shipRoot == null)
            {
                ClearInspectorPreview();
                SetDotCount(0);
                return;
            }

            if (shipWeaponsOrchestrator != null && !shipWeaponsOrchestrator.IsCockpitSeatGateSatisfied())
            {
                ClearInspectorPreview();
                SetDotCount(0);
                return;
            }

            Camera projCam = hudViewCamera != null ? hudViewCamera : Camera.main;
            if (projCam == null)
            {
                ClearInspectorPreview();
                SetDotCount(0);
                return;
            }

            for (int di = 0; di < _dotImages.Count; di++)
            {
                if (_dotImages[di] != null)
                {
                    _dotImages[di].raycastTarget = hudCalibrationMode;
                }
            }

            BuildWeaponOrder();

            int count = _weaponOrder.Count;
            EnsureDotCount(count);
            ResizeSmoothLists(count);

            Vector2 halfParent = new Vector2(
                Mathf.Max(1f, dotsParent.rect.width * 0.5f),
                Mathf.Max(1f, dotsParent.rect.height * 0.5f));

            for (int i = 0; i < count; i++)
            {
                WeaponController w = _weaponOrder[i];
                Image img = _dotImages[i];
                if (w == null || img == null)
                {
                    continue;
                }

                if (_draggingDotIndex == i)
                {
                    continue;
                }

                if (!w.TryGetMuzzleAimRay(out Ray aimRay))
                {
                    img.enabled = false;
                    continue;
                }

                if (!TryGetAimWorldPoint(aimRay, out Vector3 worldAim))
                {
                    img.enabled = false;
                    continue;
                }

                img.enabled = true;

                Vector3 screen = projCam.WorldToScreenPoint(worldAim);
                if (screen.z <= 0.01f)
                {
                    img.enabled = false;
                }
                else
                {
                    Camera eventCam = GetEventCameraForCanvas();
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                            dotsParent,
                            new Vector2(screen.x, screen.y),
                            eventCam,
                            out Vector2 local))
                    {
                        RectTransform rt = img.rectTransform;
                        Vector2 shown = local;
                        if (dotPositionSmoothTime > 1e-5f)
                        {
                            if (Vector2.Distance(_dotSmoothPos[i], local) > 400f)
                            {
                                _dotSmoothPos[i] = local;
                                _dotSmoothVel[i] = Vector2.zero;
                                shown = local;
                            }
                            else
                            {
                                Vector2 vel = _dotSmoothVel[i];
                                shown = Vector2.SmoothDamp(
                                    _dotSmoothPos[i],
                                    local,
                                    ref vel,
                                    dotPositionSmoothTime,
                                    Mathf.Infinity,
                                    Time.deltaTime);
                                _dotSmoothVel[i] = vel;
                                _dotSmoothPos[i] = shown;
                            }
                        }
                        else
                        {
                            _dotSmoothPos[i] = local;
                            _dotSmoothVel[i] = Vector2.zero;
                        }

                        rt.anchoredPosition = shown;
                        rt.sizeDelta = new Vector2(dotSizePixels, dotSizePixels);
                    }
                    else
                    {
                        img.enabled = false;
                    }
                }
            }

            SetDotCount(count);
            CopyDotPositionsForInspector(count, halfParent);
        }

        private void ResizeSmoothLists(int count)
        {
            while (_dotSmoothPos.Count < count)
            {
                _dotSmoothPos.Add(Vector2.zero);
                _dotSmoothVel.Add(Vector2.zero);
            }

            while (_dotSmoothPos.Count > count)
            {
                _dotSmoothPos.RemoveAt(_dotSmoothPos.Count - 1);
                _dotSmoothVel.RemoveAt(_dotSmoothVel.Count - 1);
            }
        }

        private void ClearInspectorPreview()
        {
            _inspectorPreviewDotCount = 0;
            _inspectorPreviewAnchored = null;
            _inspectorPreviewNormalized = null;
        }

        private void CopyDotPositionsForInspector(int count, Vector2 dotsParentHalfSize)
        {
            _inspectorPreviewDotCount = count;
            _inspectorPreviewParentHalf = dotsParentHalfSize;

            if (_inspectorPreviewAnchored == null || _inspectorPreviewAnchored.Length != count)
            {
                _inspectorPreviewAnchored = new Vector2[count];
            }

            if (_inspectorPreviewNormalized == null || _inspectorPreviewNormalized.Length != count)
            {
                _inspectorPreviewNormalized = new Vector2[count];
            }

            for (int i = 0; i < count; i++)
            {
                if (i < _dotImages.Count && _dotImages[i] != null)
                {
                    Vector2 ap = _dotImages[i].rectTransform.anchoredPosition;
                    _inspectorPreviewAnchored[i] = ap;
                    _inspectorPreviewNormalized[i] = new Vector2(
                        ap.x / Mathf.Max(1f, dotsParentHalfSize.x),
                        ap.y / Mathf.Max(1f, dotsParentHalfSize.y));
                }
            }

#if UNITY_EDITOR
            if (count > 0)
            {
                UnityEditor.EditorUtility.SetDirty(this);
            }
#endif
        }

        private void BuildWeaponOrder()
        {
            _weaponOrder.Clear();
            if (shipWeaponsOrchestrator != null &&
                shipWeaponsOrchestrator.TryGetSelectedGroupWeapons(out WeaponController[] groupWeapons) &&
                groupWeapons != null)
            {
                for (int i = 0; i < groupWeapons.Length; i++)
                {
                    WeaponController w = groupWeapons[i];
                    if (w == null || w.Muzzle == null)
                    {
                        continue;
                    }

                    _weaponOrder.Add(w);
                }

                return;
            }

            WeaponController[] weapons = shipRoot.GetComponentsInChildren<WeaponController>(true);
            for (int i = 0; i < weapons.Length; i++)
            {
                WeaponController w = weapons[i];
                if (w == null || w.Muzzle == null)
                {
                    continue;
                }

                _weaponOrder.Add(w);
            }
        }

        internal void NotifyDotBeginDrag(int dotIndex)
        {
            if (!hudCalibrationMode)
            {
                return;
            }

            _draggingDotIndex = dotIndex;
        }

        internal void NotifyDotDrag(int dotIndex, PointerEventData eventData)
        {
            if (!hudCalibrationMode || eventData == null)
            {
                return;
            }

            if (_weaponOrder.Count == 0)
            {
                BuildWeaponOrder();
            }

            Camera eventCam = GetEventCameraForCanvas();
            if (dotsParent == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    dotsParent,
                    eventData.position,
                    eventCam,
                    out Vector2 local))
            {
                return;
            }

            if (dotIndex >= 0 && dotIndex < _dotImages.Count && _dotImages[dotIndex] != null)
            {
                _dotImages[dotIndex].rectTransform.anchoredPosition = local;
                EnsureSmoothListIndex(dotIndex);
                _dotSmoothPos[dotIndex] = local;
                _dotSmoothVel[dotIndex] = Vector2.zero;
            }

            if (dotIndex >= 0 && dotIndex < _weaponOrder.Count)
            {
                WeaponController w = _weaponOrder[dotIndex];
                if (w != null)
                {
                    w.AddHudCalibrationDelta(
                        -eventData.delta.y * calibrationDragSensitivity,
                        eventData.delta.x * calibrationDragSensitivity);
                }
            }
        }

        internal void NotifyDotEndDrag(int dotIndex)
        {
            if (_draggingDotIndex == dotIndex)
            {
                _draggingDotIndex = -1;
            }

            if (dotIndex >= 0 && dotIndex < _dotImages.Count && _dotImages[dotIndex] != null)
            {
                EnsureSmoothListIndex(dotIndex);
                Vector2 ap = _dotImages[dotIndex].rectTransform.anchoredPosition;
                _dotSmoothPos[dotIndex] = ap;
                _dotSmoothVel[dotIndex] = Vector2.zero;
            }
        }

        private void EnsureSmoothListIndex(int index)
        {
            while (_dotSmoothPos.Count <= index)
            {
                _dotSmoothPos.Add(Vector2.zero);
                _dotSmoothVel.Add(Vector2.zero);
            }
        }

        private Camera GetEventCameraForCanvas()
        {
            if (_canvas == null)
            {
                return hudViewCamera != null ? hudViewCamera : Camera.main;
            }

            if (_canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            return _canvas.worldCamera != null ? _canvas.worldCamera : hudViewCamera;
        }

        private bool TryGetAimWorldPoint(Ray ray, out Vector3 worldPoint)
        {
            RaycastHit[] hits = Physics.RaycastAll(ray, maxRayDistance, raycastLayers, triggerInteraction);
            if (hits == null || hits.Length == 0)
            {
                worldPoint = ray.GetPoint(maxRayDistance);
                return true;
            }

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                Transform t = hits[i].collider != null ? hits[i].collider.transform : null;
                if (t != null && shipRoot != null && t.IsChildOf(shipRoot))
                {
                    continue;
                }

                worldPoint = hits[i].point;
                return true;
            }

            worldPoint = ray.GetPoint(maxRayDistance);
            return true;
        }

        /// <summary>
        /// Picks up existing <c>AimDot_N</c> children (e.g. created in Edit Mode) so Play Mode does not duplicate them.
        /// </summary>
        private void CollectExistingDotImagesFromParent()
        {
            _dotImages.Clear();
            if (dotsParent == null)
            {
                return;
            }

            for (int i = 0; ; i++)
            {
                Transform t = dotsParent.Find($"AimDot_{i}");
                if (t == null)
                {
                    break;
                }

                Image img = t.GetComponent<Image>();
                if (img == null)
                {
                    break;
                }

                _dotImages.Add(img);
            }
        }

        private void EnsureDotCount(int count)
        {
            while (_dotImages.Count < count)
            {
                _dotImages.Add(CreateDotImage(_dotImages.Count));
            }
        }

        private void SetDotCount(int count)
        {
            EnsureDotCount(count);
            for (int i = 0; i < _dotImages.Count; i++)
            {
                if (_dotImages[i] != null)
                {
                    _dotImages[i].enabled = i < count;
                }
            }
        }

        private Image CreateDotImage(int index)
        {
            GameObject go;
            if (dotPrefab != null)
            {
                go = Instantiate(dotPrefab.gameObject, dotsParent);
                go.name = $"AimDot_{index}";
            }
            else
            {
                go = new GameObject($"AimDot_{index}", typeof(RectTransform));
                go.transform.SetParent(dotsParent, false);
                var img = go.AddComponent<Image>();
                ApplySpriteToImage(img);
                img.color = dotColor;
                var rt = go.GetComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                FinishDotSetup(go, img, index);
                return img;
            }

            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                image = go.AddComponent<Image>();
            }

            ApplySpriteToImage(image);
            image.color = dotColor;
            RectTransform r = go.GetComponent<RectTransform>();
            if (r != null)
            {
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
            }

            FinishDotSetup(go, image, index);
            return image;
        }

        private void FinishDotSetup(GameObject go, Image image, int index)
        {
            image.raycastTarget = hudCalibrationMode;
            ApplyDotOutline(go);
            var drag = go.GetComponent<WeaponAimDotDrag>();
            if (drag == null)
            {
                drag = go.AddComponent<WeaponAimDotDrag>();
            }

            drag.Configure(this, index);
        }

        private void ApplyDotOutline(GameObject go)
        {
            if (go == null)
            {
                return;
            }

            Outline outline = go.GetComponent<Outline>();
            if (outline == null)
            {
                outline = go.AddComponent<Outline>();
            }

            outline.effectColor = dotOutlineColor;
            outline.effectDistance = dotOutlineDistance;
            outline.useGraphicAlpha = true;
        }

        private void SyncDotOutlineStyles()
        {
            for (int i = 0; i < _dotImages.Count; i++)
            {
                if (_dotImages[i] != null)
                {
                    ApplyDotOutline(_dotImages[i].gameObject);
                }
            }
        }

        private void ApplySpriteToImage(Image image)
        {
            if (image == null)
            {
                return;
            }

            if (dotSprite != null)
            {
                image.sprite = dotSprite;
                image.type = Image.Type.Simple;
                image.preserveAspect = true;
            }
            else if (image.sprite == null)
            {
                image.sprite = Sprite.Create(
                    Texture2D.whiteTexture,
                    new Rect(0f, 0f, 4f, 4f),
                    new Vector2(0.5f, 0.5f),
                    100f);
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
            }
        }

        private void OnValidate()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (dotsParent == null)
                {
                    dotsParent = transform as RectTransform;
                }

                if (createDotPlaceholdersInEditMode && dotsParent != null)
                {
                    SyncEditModeDotPlaceholders();
                }

                CollectExistingDotImagesFromParent();
            }
#endif
            for (int i = 0; i < _dotImages.Count; i++)
            {
                if (_dotImages[i] == null)
                {
                    continue;
                }

                ApplySpriteToImage(_dotImages[i]);
                _dotImages[i].color = dotColor;
                _dotImages[i].raycastTarget = hudCalibrationMode;
                ApplyDotOutline(_dotImages[i].gameObject);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Creates/removes <c>AimDot_N</c> under <see cref="dotsParent"/> in the editor so you can see them in the Hierarchy without Play Mode.
        /// </summary>
        private void SyncEditModeDotPlaceholders()
        {
            if (dotsParent == null)
            {
                return;
            }

            int n = Mathf.Max(0, editModeDotPlaceholderCount);
            for (int i = 0; i < n; i++)
            {
                if (dotsParent.Find($"AimDot_{i}") != null)
                {
                    continue;
                }

                Image img = CreateDotImage(i);
                Undo.RegisterCreatedObjectUndo(img.gameObject, "Create PIP dot placeholder");
            }

            var toDestroy = new List<Transform>();
            foreach (Transform ch in dotsParent)
            {
                if (!ch.name.StartsWith("AimDot_", StringComparison.Ordinal))
                {
                    continue;
                }

                string suffix = ch.name.Length > 7 ? ch.name.Substring(7) : string.Empty;
                if (!int.TryParse(suffix, out int idx) || idx < n)
                {
                    continue;
                }

                toDestroy.Add(ch);
            }

            for (int i = 0; i < toDestroy.Count; i++)
            {
                Undo.DestroyObjectImmediate(toDestroy[i].gameObject);
            }

            EditorUtility.SetDirty(dotsParent.gameObject);
        }

        [ContextMenu("VR Koombar/Sync PIP dot placeholders (edit mode)")]
        private void ContextMenuSyncEditModeDots()
        {
            if (Application.isPlaying)
            {
                return;
            }

            SyncEditModeDotPlaceholders();
            CollectExistingDotImagesFromParent();
        }
#endif
    }
}
