using JunkerScanner;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace JunkerEquipment
{
    /// <summary>Scanner and HUD rendered into the existing shared optical image, not separately per eye.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    public sealed class BinocularScannerHUD : MonoBehaviour
    {
        public bool scanWhileViewing = true;
        [Min(.1f)] public float scanDistance = 100;
        [Min(.02f)] public float scanInterval = .2f;
        public LayerMask scannableLayers = ~0, obstructionLayers = ~0;
        public ScanVisualProfile visualProfile;
        public TMP_FontAsset font;
        public Color hudColor = new(.2f, 1f, .3f, 1f);
        [Tooltip("Pixels relative to the optical image centre (512 x 512 reference).")]
        public Vector2 rangePosition = new(-70, 65);
        [Min(8)] public float rangeFontSize = 20;
        public HelmetScannerController Scanner => scanner;

        JunkerBinoculars binoculars;
        OpticalDevice optics;
        HelmetScannerController scanner;
        Canvas canvas;
        TextMeshProUGUI range, heading;
        GameObject scannerRoot;
        ScanVisualProfile ownedProfile;

        void Start()
        {
            binoculars = GetComponent<JunkerBinoculars>();
            optics = GetComponent<OpticalDevice>();
            if (!binoculars || !optics || !optics.opticsCamera) { enabled = false; return; }

            scannerRoot = new GameObject("Binocular scanner");
            scannerRoot.transform.SetParent(transform, false);
            scanner = scannerRoot.AddComponent<HelmetScannerController>();
            scanner.allowLeft = scanner.allowRight = false;
            scanner.head = optics.opticsCamera.transform;
            // Use a separate scanner owner; never toggle or hijack the helmet scanner.
            if (binoculars.hmd)
            {
                var origin = binoculars.hmd.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
                scanner.playerRoot = origin ? origin.transform : binoculars.hmd.transform.root;
                foreach (var helmet in scanner.playerRoot.GetComponentsInChildren<HelmetScannerController>(true))
                    if (helmet != scanner && helmet.visualProfile) { scanner.visualProfile = helmet.visualProfile; break; }
            }
            if (visualProfile) scanner.visualProfile = visualProfile;
            if (!scanner.visualProfile)
            {
                ownedProfile = ScriptableObject.CreateInstance<ScanVisualProfile>();
                scanner.visualProfile = ownedProfile;
            }

            var ui = new GameObject("Binocular optical HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            ui.transform.SetParent(optics.opticsCamera.transform, false);
            canvas = ui.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = optics.opticsCamera;
            canvas.planeDistance = Mathf.Max(optics.nearClip + .02f, .1f);
            canvas.sortingOrder = 100;
            var scaler = ui.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(512, 512);
            scaler.matchWidthOrHeight = .5f;

            var mount = (RectTransform)ui.transform;
            range = Label(mount, "Range", rangePosition, new Vector2(140, 30), rangeFontSize, TextAlignmentOptions.Center);
            heading = Label(mount, "Heading", new Vector2(0, 122), new Vector2(130, 22), 15, TextAlignmentOptions.Center);
            Label(mount, "Aim", Vector2.zero, new Vector2(24, 24), 20, TextAlignmentOptions.Center).text = "+";
            var label = Label(mount, "Scan information", new Vector2(0, -110), new Vector2(300, 105), 12, TextAlignmentOptions.TopLeft);
            label.overflowMode = TextOverflowModes.Ellipsis;
            var presenter = scannerRoot.AddComponent<CompactScannerPresenter>();
            presenter.label = label;
            presenter.maxDiagnosticRows = 3;
            scanner.presenter = presenter;

            // The same heading tape used by Razara, with the optical objective instead of a ship Rigidbody.
            var tapeObject = new GameObject("Optical compass controller");
            tapeObject.SetActive(false);
            tapeObject.transform.SetParent(ui.transform, false);
            var tape = tapeObject.AddComponent<HeadingTapeHUD>();
            tape.mount = mount;
            tape.headingSource = optics.opticsCamera.transform;
            tape.tapePosition = new Vector2(0, 147);
            tape.visibleDegrees = 40;
            tape.pixelsPerDegree = 3;
            tape.cardinalFontSize = 16;
            tape.degreeFontSize = 12;
            tape.tickColor = tape.cardinalColor = tape.degreeColor = hudColor;
            tapeObject.SetActive(true);
            canvas.gameObject.SetActive(false);
        }

        TextMeshProUGUI Label(RectTransform parent, string name, Vector2 position, Vector2 size, float fontSize, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            if (font) text.font = font;
            text.fontSize = fontSize;
            text.color = hudColor;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(.5f, .5f);
            text.rectTransform.anchoredPosition = position;
            text.rectTransform.sizeDelta = size;
            return text;
        }

        void LateUpdate()
        {
            if (!canvas || !scanner) return;
            bool viewing = optics.IsViewing;
            if (canvas.gameObject.activeSelf != viewing) canvas.gameObject.SetActive(viewing);
            scanner.head = optics.opticsCamera.transform;
            scanner.maxDistance = Mathf.Min(scanDistance, optics.farClip);
            scanner.updateInterval = scanInterval;
            scanner.scannableLayers = scannableLayers;
            scanner.obstructionLayers = obstructionLayers;
            scanner.nearbyViewAngle = Mathf.Clamp(optics.opticsCamera.fieldOfView * .5f, 0, 90);
            scanner.SetScanning(viewing && scanWhileViewing);
            if (!viewing) return;
            range.rectTransform.anchoredPosition = rangePosition;
            range.fontSize = rangeFontSize;
            range.text = binoculars.distance >= 0 ? $"{binoculars.distance:F1} m" : "-- m";
            if (optics.objective)
            {
                var forward = optics.opticsCamera.transform.forward;
                float degrees = Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360);
                heading.text = $"{Mathf.RoundToInt(degrees) % 360:000}°  {optics.CurrentZoom:0.#}x";
            }
        }

        void OnDisable()
        {
            if (scanner) scanner.SetScanning(false);
            if (canvas) canvas.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (scanner) scanner.SetScanning(false);
            // The canvas is owned here but parented under the optical camera.
            if (canvas) { if (Application.isPlaying) Destroy(canvas.gameObject); else DestroyImmediate(canvas.gameObject); }
            if (scannerRoot) { if (Application.isPlaying) Destroy(scannerRoot); else DestroyImmediate(scannerRoot); }
            if (ownedProfile) { if (Application.isPlaying) Destroy(ownedProfile); else DestroyImmediate(ownedProfile); }
        }
    }
}
