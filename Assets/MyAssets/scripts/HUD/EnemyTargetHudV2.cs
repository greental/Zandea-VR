using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VRKoombar.WeaponsStaging.Contracts;
using VRKoombar.WeaponsStaging.Core;

// POC scene-only HUD bridge. All UI is attached to Razara's existing world-space cockpit anchors.
public sealed class EnemyTargetHudV2 : MonoBehaviour
{
    [SerializeField] NPCShipCombatV2 enemy;
    [SerializeField] ShipHUDManager shipHud;
    [SerializeField] Camera viewCamera;
    [SerializeField] Color enemyColor = new Color(1f, .18f, .16f, 1f);
    [SerializeField] Color friendlyColor = new Color(.2f, .92f, 1f, 1f);
    [SerializeField] bool targetIsFriendly;
    [SerializeField] Color hullColor = new Color(1f, .56f, .3f, .95f);
    [Header("Razara main screen placement (local to screenFlat)")]
    [SerializeField] Vector3 screenLocalPosition = new Vector3(.003f, -.0005f, .0015f);
    [SerializeField] Vector3 screenLocalEulerAngles = new Vector3(-90f, 0f, 180f);
    [SerializeField] Vector3 screenLocalScale = Vector3.one * .00003f;
    [Header("Wide VR reticle")]
    [SerializeField, Range(15f, 80f)] float reticleHalfAngle = 45f;
    [SerializeField, Min(.5f)] float reticleDistance = 2f;
    [SerializeField, Min(0f)] float destroyedMessageSeconds = 2f;

    RectTransform canopyRect, markerRect, arrowRect, screenRect, bracketRect, targetContentRect, outlineRect;
    TextMeshProUGUI markerText, distanceText, shieldText, hullText, targetNameText, noTargetsText;
    readonly List<NPCShipCombatV2> targets = new List<NPCShipCombatV2>();
    readonly Image[] shieldFills = new Image[4];
    readonly TextMeshProUGUI[] shieldTexts = new TextMeshProUGUI[4];
    static readonly ShieldArc[] shieldArcs = { ShieldArc.Front, ShieldArc.Rear, ShieldArc.Left, ShieldArc.Right };
    Image arrowImage, hullFill;
    static Sprite arrowSprite;
    CombatDamageable health;
    ShieldSystem shield;
    Renderer[] renderers;
    float nextReadout, nextTargetRefresh;
    float destroyedMessageUntil;
    ShipPowerController shipPower;
    int selectedIndex = -1;
    Color Accent => targetIsFriendly ? friendlyColor : enemyColor;

    void Start()
    {
        NPCShipCombatV2 initialTarget = enemy;
        enemy = null;
        if (shipHud == null) shipHud = FindFirstObjectByType<ShipHUDManager>();
        if (shipHud == null) { enabled = false; return; }
        if (viewCamera == null) viewCamera = shipHud.GetComponentInChildren<Camera>(true);
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) { enabled = false; return; }
        shipPower = shipHud.GetComponentInParent<ShipPowerController>();
        if (shipPower == null) shipPower = shipHud.transform.root.GetComponentInChildren<ShipPowerController>(true);
        RefreshTargets();
        BuildCanopy();
        Transform screenAnchor = FindDescendant(shipHud.transform.root, "screenFlat");
        if (screenAnchor == null) screenAnchor = shipHud.rightScreen;
        if (screenAnchor != null) BuildScreen(screenAnchor);
        SelectTarget(IsValidTarget(initialTarget) ? initialTarget :
            targets.Count > 0 ? targets[0] : null);
    }

    void BuildCanopy()
    {
        var root = new GameObject("WideTargetReticleV3", typeof(RectTransform), typeof(Canvas));
        canopyRect = root.GetComponent<RectTransform>();
        canopyRect.SetParent(viewCamera.transform, false);
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = viewCamera;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 80;
        canopyRect.sizeDelta = new Vector2(1200f, 900f);
        float width = 2f * reticleDistance * Mathf.Tan(reticleHalfAngle * Mathf.Deg2Rad);
        canopyRect.localScale = Vector3.one * (width / canopyRect.sizeDelta.x);
        canopyRect.localPosition = Vector3.forward * reticleDistance;
        canopyRect.localRotation = Quaternion.identity;
        markerRect = NewRect("EnemyTargetMarkerV2", canopyRect, new Vector2(110f, 85f), Vector2.zero);
        bracketRect = NewRect("TargetCorners", markerRect, markerRect.sizeDelta, Vector2.zero);
        CornerOutline(bracketRect, bracketRect.sizeDelta, Accent, 1.25f);
        markerText = NewText("TargetLabel", markerRect, "", 15, Accent, new Vector2(0f, 58f), new Vector2(180f, 24f));
        arrowRect = NewRect("EnemyDirectionArrowV2", canopyRect, new Vector2(30f, 30f), Vector2.zero);
        arrowImage = NewImage("Arrow", arrowRect, new Vector2(26f, 26f), Vector2.zero, Accent);
        arrowImage.sprite = GetArrowSprite();
    }

    void BuildScreen(Transform anchor)
    {
        GameObject root = new GameObject("EnemyTargetMainScreenV2", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        screenRect = root.GetComponent<RectTransform>();
        screenRect.SetParent(anchor, false);
        screenRect.sizeDelta = new Vector2(340f, 260f);
        ApplyScreenPlacement();
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = viewCamera;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 50;
        NewImage("DarkReadout", screenRect, screenRect.sizeDelta, Vector2.zero, new Color(.015f, .02f, .035f, .94f));
        targetContentRect = NewRect("TargetContent", screenRect, screenRect.sizeDelta, Vector2.zero);
        noTargetsText = NewText("NoAvailableTargets", screenRect, "NO AVAILABLE TARGETS", 20,
            Accent, Vector2.zero, new Vector2(310f, 50f));
        targetNameText = NewText("TargetName", targetContentRect, "", 22, Accent, new Vector2(0f, 105f), new Vector2(310f, 32f));
        outlineRect = NewRect("TargetOutline", targetContentRect, new Vector2(135f, 82f), new Vector2(0f, 37f));
        distanceText = NewText("Range", targetContentRect, "RANGE --", 16, Accent, new Vector2(0f, -17f), new Vector2(310f, 24f));
        NewText("ShieldLabel", targetContentRect, "SHIELDS", 14, Accent, new Vector2(0f, -40f), new Vector2(110f, 20f));
        BuildShieldArc(0, "FRONT", new Vector2(-83f, -62f));
        BuildShieldArc(1, "REAR", new Vector2(83f, -62f));
        BuildShieldArc(2, "LEFT", new Vector2(-83f, -85f));
        BuildShieldArc(3, "RIGHT", new Vector2(83f, -85f));
        NewText("HullLabel", targetContentRect, "HULL", 15, hullColor, new Vector2(-119f, -113f), new Vector2(55f, 21f));
        hullFill = NewBar("Hull", targetContentRect, new Vector2(0f, -113f), hullColor, new Vector2(150f, 13f));
        hullText = NewText("HullPercent", targetContentRect, "100%", 14, hullColor, new Vector2(119f, -113f), new Vector2(52f, 21f));
    }

    void BuildShieldArc(int index, string label, Vector2 position)
    {
        NewText(label + "Label", targetContentRect, label, 12, Accent,
            position + new Vector2(-56f, 0f), new Vector2(51f, 18f));
        shieldFills[index] = NewBar(label, targetContentRect, position + new Vector2(1f, 0f), Accent, new Vector2(70f, 12f));
        shieldTexts[index] = NewText(label + "Percent", targetContentRect, "100%", 12, Accent,
            position + new Vector2(57f, 0f), new Vector2(47f, 18f));
    }

    void LateUpdate()
    {
        // Inspector edits made during Play Mode update the generated canvas immediately.
        if (screenRect != null) ApplyScreenPlacement();
        bool powered = shipPower == null || shipPower.hudPowered;
        if (screenRect != null && screenRect.gameObject.activeSelf != powered) screenRect.gameObject.SetActive(powered);
        if (canopyRect != null && canopyRect.gameObject.activeSelf != powered) canopyRect.gameObject.SetActive(powered);
        if (!powered) return;
        if (destroyedMessageUntil > 0f)
        {
            if (Time.time < destroyedMessageUntil) return;
            destroyedMessageUntil = 0f;
            RefreshTargets();
            SelectTarget(targets.Count > 0 ? targets[Mathf.Clamp(selectedIndex, 0, targets.Count - 1)] : null);
        }
        if (Time.time >= nextTargetRefresh || !ReferenceEquals(enemy, null) && !IsValidTarget(enemy))
        {
            int previousIndex = selectedIndex;
            bool destroyed = !ReferenceEquals(enemy, null) && !IsValidTarget(enemy);
            RefreshTargets();
            if (destroyed)
            {
                selectedIndex = previousIndex;
                enemy = null;
                if (markerRect != null) markerRect.gameObject.SetActive(false);
                if (arrowRect != null) arrowRect.gameObject.SetActive(false);
                if (targetContentRect != null) targetContentRect.gameObject.SetActive(false);
                if (noTargetsText != null) { noTargetsText.text = "TARGET DESTROYED"; noTargetsText.gameObject.SetActive(true); }
                destroyedMessageUntil = Time.time + destroyedMessageSeconds;
                return;
            }
            if (!IsValidTarget(enemy))
                SelectTarget(targets.Count == 0 ? null : targets[previousIndex < 0 ? 0 : previousIndex % targets.Count]);
            else selectedIndex = targets.IndexOf(enemy);
            nextTargetRefresh = Time.time + .5f;
        }
        if (enemy == null) return;
        if (canopyRect != null)
        {
            float width = 2f * reticleDistance * Mathf.Tan(reticleHalfAngle * Mathf.Deg2Rad);
            canopyRect.localScale = Vector3.one * (width / canopyRect.rect.width);
            canopyRect.localPosition = Vector3.forward * reticleDistance;
        }
        Vector3 point = TargetCenter();
        Vector3 screen = viewCamera.WorldToScreenPoint(point);
        bool projected = RectTransformUtility.ScreenPointToLocalPointInRectangle(canopyRect,
            screen, viewCamera, out Vector2 projectedLocal);
        bool visible = screen.z > 0f && projected &&
            canopyRect.rect.Contains(projectedLocal);
        if (canopyRect != null)
        {
            markerRect.gameObject.SetActive(visible);
            arrowRect.gameObject.SetActive(!visible);
            if (visible)
            {
                Vector2 local = projectedLocal;
                markerRect.anchoredPosition = ClampToRect(local, canopyRect.rect, 55f);
                Bounds bounds = TargetBounds();
                Vector3 projectedEdge = viewCamera.WorldToScreenPoint(bounds.center + viewCamera.transform.right * bounds.extents.magnitude);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(canopyRect, projectedEdge, viewCamera, out Vector2 edge);
                float size = Mathf.Clamp(Vector2.Distance(local, edge) * 2f, 32f, 145f);
                bracketRect.localScale = Vector3.one * (size / 110f);
                markerText.rectTransform.anchoredPosition = new Vector2(0f, size * .38f + 18f);
            }
            else
            {
                Vector3 local = viewCamera.transform.InverseTransformDirection((point - viewCamera.transform.position).normalized);
                arrowImage.color = local.z < 0f
                    ? (targetIsFriendly ? new Color(.04f, .3f, .34f, 1f) : new Color(.36f, .025f, .025f, 1f))
                    : Accent;
                Vector2 direction = new Vector2(local.x, local.y);
                if (local.z < 0f) direction = -direction;
                if (direction.sqrMagnitude < .001f) direction = local.z < 0f ? Vector2.down : Vector2.up;
                direction.Normalize();
                Vector2 half = canopyRect.rect.size * .42f;
                float edge = Mathf.Min(half.x / Mathf.Max(.01f, Mathf.Abs(direction.x)),
                    half.y / Mathf.Max(.01f, Mathf.Abs(direction.y)));
                arrowRect.anchoredPosition = direction * edge;
                arrowRect.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg);
            }
        }
        if (Time.time < nextReadout) return;
        nextReadout = Time.time + .15f;
        float distance = Vector3.Distance(viewCamera.transform.position, point);
        if (markerText != null) markerText.text = enemy.name.ToUpperInvariant() + "  " + Mathf.RoundToInt(distance) + "m";
        if (screenRect == null) return;
        distanceText.text = "RANGE  " + Mathf.RoundToInt(distance) + " m";
        float hullFraction = health != null ? health.HullFraction : 0f;
        for (int i = 0; i < shieldArcs.Length; i++)
        {
            float fraction = shield != null ? shield.GetArcNormalized(shieldArcs[i]) : 0f;
            shieldFills[i].fillAmount = fraction;
            shieldTexts[i].text = shield != null && shield.IsArcBreached(shieldArcs[i])
                ? "BR " + Mathf.RoundToInt(fraction * 100f) + "%"
                : Mathf.RoundToInt(fraction * 100f) + "%";
        }
        hullFill.fillAmount = hullFraction;
        hullText.text = Mathf.RoundToInt(hullFraction * 100f) + "%";
    }

    static bool IsValidTarget(NPCShipCombatV2 candidate)
    {
        return candidate != null && candidate.isActiveAndEnabled &&
            candidate.TryGetComponent(out CombatDamageable hull) && !hull.IsDestroyed;
    }

    void RefreshTargets()
    {
        targets.Clear();
        NPCShipCombatV2[] found = FindObjectsByType<NPCShipCombatV2>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < found.Length; i++)
            if (IsValidTarget(found[i])) targets.Add(found[i]);
        targets.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
    }

    void SelectTarget(NPCShipCombatV2 next)
    {
        if (!IsValidTarget(next)) next = null;
        enemy = next;
        selectedIndex = next != null ? targets.IndexOf(next) : -1;
        health = next != null ? next.GetComponent<CombatDamageable>() : null;
        shield = next != null ? next.GetComponent<ShieldSystem>() : null;
        renderers = next != null ? next.GetComponentsInChildren<Renderer>(true) : null;
        if (markerRect != null) markerRect.gameObject.SetActive(false);
        if (arrowRect != null) arrowRect.gameObject.SetActive(false);
        if (targetContentRect != null) targetContentRect.gameObject.SetActive(next != null);
        if (noTargetsText != null) noTargetsText.gameObject.SetActive(next == null);
        if (noTargetsText != null) noTargetsText.text = "NO AVAILABLE TARGETS";
        if (next == null || outlineRect == null) return;
        targetNameText.text = next.name.ToUpperInvariant();
        for (int i = outlineRect.childCount - 1; i >= 0; i--)
            Destroy(outlineRect.GetChild(i).gameObject);
        if (!DrawMeshOutline(outlineRect, Accent)) DrawShipOutline(outlineRect, Accent);
        nextReadout = 0f;
    }

    public void NextTarget() => CycleTarget(1);
    public void PreviousTarget() => CycleTarget(-1);

    void CycleTarget(int step)
    {
        destroyedMessageUntil = 0f;
        RefreshTargets();
        if (targets.Count == 0) { SelectTarget(null); return; }
        int current = targets.IndexOf(enemy);
        int next = current < 0 ? step > 0 ? 0 : targets.Count - 1 :
            (current + step + targets.Count) % targets.Count;
        SelectTarget(targets[next]);
    }

    void ApplyScreenPlacement()
    {
        screenRect.localPosition = screenLocalPosition;
        screenRect.localRotation = Quaternion.Euler(screenLocalEulerAngles);
        screenRect.localScale = screenLocalScale;
    }

    Vector3 TargetCenter()
    {
        return TargetBounds().center;
    }

    Bounds TargetBounds()
    {
        Bounds bounds = new Bounds(enemy.transform.position, Vector3.zero);
        if (renderers == null) return bounds;
        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null || !renderers[i].enabled) continue;
            if (!found) { bounds = renderers[i].bounds; found = true; }
            else bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    static Vector2 ClampToRect(Vector2 value, Rect rect, float margin) =>
        new Vector2(Mathf.Clamp(value.x, rect.xMin + margin, rect.xMax - margin),
            Mathf.Clamp(value.y, rect.yMin + margin, rect.yMax - margin));

    static Transform FindDescendant(Transform parent, string wanted)
    {
        if (parent.name == wanted) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDescendant(parent.GetChild(i), wanted);
            if (found != null) return found;
        }
        return null;
    }

    static RectTransform NewRect(string name, Transform parent, Vector2 size, Vector2 position)
    {
        RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    static TextMeshProUGUI NewText(string name, Transform parent, string text, float size, Color color, Vector2 position, Vector2 bounds)
    {
        RectTransform rect = NewRect(name, parent, bounds, position);
        TextMeshProUGUI label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    static void CornerOutline(RectTransform parent, Vector2 size, Color color, float thickness)
    {
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            {
                Image h = NewImage("CornerHorizontal", parent, Vector2.zero, Vector2.zero, color);
                RectTransform hr = h.rectTransform;
                hr.anchorMin = hr.anchorMax = new Vector2(x < 0 ? 0f : 1f, y < 0 ? 0f : 1f);
                hr.pivot = new Vector2(x < 0 ? 0f : 1f, .5f);
                hr.sizeDelta = new Vector2(size.x * .2f, thickness);
                Image v = NewImage("CornerVertical", parent, Vector2.zero, Vector2.zero, color);
                RectTransform vr = v.rectTransform;
                vr.anchorMin = vr.anchorMax = new Vector2(x < 0 ? 0f : 1f, y < 0 ? 0f : 1f);
                vr.pivot = new Vector2(.5f, y < 0 ? 0f : 1f);
                vr.sizeDelta = new Vector2(thickness, size.y * .2f);
            }
    }

    static Image NewImage(string name, Transform parent, Vector2 size, Vector2 position, Color color)
    {
        RectTransform rect = NewRect(name, parent, size, position);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = HUDCanvasHelper.GetWhiteSprite();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static Image NewBar(string name, Transform parent, Vector2 position, Color color, Vector2 size)
    {
        NewImage(name + "Background", parent, size, position, new Color(.13f, .18f, .22f, 1f));
        Image image = NewImage(name + "Fill", parent, size, position, color);
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Horizontal;
        return image;
    }

    static void DrawShipOutline(Transform parent, Color color)
    {
        Vector2[] points = {
            new Vector2(0f, 30f), new Vector2(-9f, 5f), new Vector2(-38f, -12f),
            new Vector2(-37f, -22f), new Vector2(-9f, -13f), new Vector2(0f, -26f),
            new Vector2(9f, -13f), new Vector2(37f, -22f), new Vector2(38f, -12f),
            new Vector2(9f, 5f)
        };
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 a = points[i], b = points[(i + 1) % points.Length];
            DrawEdge(parent, a, b, color);
        }
    }

    bool DrawMeshOutline(Transform parent, Color color)
    {
        MeshFilter[] filters = enemy.GetComponentsInChildren<MeshFilter>(true);
        List<Vector2> points = new List<Vector2>(2048);
        for (int i = 0; i < filters.Length && points.Count < 8000; i++)
        {
            Mesh mesh = filters[i].sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            Vector3[] vertices = mesh.vertices;
            int step = Mathf.Max(1, vertices.Length / 500);
            for (int j = 0; j < vertices.Length; j += step)
            {
                Vector3 local = enemy.transform.InverseTransformPoint(filters[i].transform.TransformPoint(vertices[j]));
                points.Add(new Vector2(local.x, local.z));
            }
        }
        if (points.Count < 3) return false;
        points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        List<Vector2> hull = new List<Vector2>(points.Count * 2);
        for (int i = 0; i < points.Count; i++)
        {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], points[i]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(points[i]);
        }
        int lowerCount = hull.Count;
        for (int i = points.Count - 2; i >= 0; i--)
        {
            while (hull.Count > lowerCount && Cross(hull[hull.Count - 2], hull[hull.Count - 1], points[i]) <= 0f)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(points[i]);
        }
        if (hull.Count < 4) return false;
        hull.RemoveAt(hull.Count - 1);
        Vector2 min = hull[0], max = hull[0];
        for (int i = 1; i < hull.Count; i++) { min = Vector2.Min(min, hull[i]); max = Vector2.Max(max, hull[i]); }
        Vector2 center = (min + max) * .5f;
        float scale = Mathf.Min(85f / Mathf.Max(.01f, max.x - min.x), 57f / Mathf.Max(.01f, max.y - min.y));
        for (int i = 0; i < hull.Count; i++)
            DrawEdge(parent, (hull[i] - center) * scale, (hull[(i + 1) % hull.Count] - center) * scale, color);
        return true;
    }

    static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
        (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

    static void DrawEdge(Transform parent, Vector2 a, Vector2 b, Color color)
    {
        RectTransform segment = NewImage("HullEdge", parent, new Vector2(Vector2.Distance(a, b), 2f),
            (a + b) * .5f, color).rectTransform;
        segment.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg);
    }

    static Sprite GetArrowSprite()
    {
        if (arrowSprite != null) return arrowSprite;
        Texture2D texture = new Texture2D(32, 32, TextureFormat.RGBA32, false);
        texture.filterMode = FilterMode.Bilinear;
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
                texture.SetPixel(x, y, y >= 3 && y <= 29 && Mathf.Abs(x - 15.5f) < y * .5f ? Color.white : Color.clear);
        texture.Apply();
        arrowSprite = Sprite.Create(texture, new Rect(0, 0, 32, 32), new Vector2(.5f, .5f));
        return arrowSprite;
    }
}
