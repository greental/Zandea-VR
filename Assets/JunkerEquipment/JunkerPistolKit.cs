using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace JunkerEquipment
{
    /// <summary>Add to a pistol mesh or its parent to install the same equipment stack used by PKO.</summary>
    [DisallowMultipleComponent]
    public sealed class JunkerPistolKit : MonoBehaviour
    {
        [Header("Identity and equipment")]
        public string itemId = "JKR-88";
        public JunkerEquipmentRig equipmentRig;
        [Min(.01f)] public float mass = .8f;
        public Vector3 gripPositionOffset;
        public Vector3 gripRotationOffset;
        [Min(.01f)] public float holsterRadius = .18f;

        [Header("Model-specific anchors")]
        [Tooltip("The visible mesh root. Moving this transform gives whole-pistol recoil when no separate slide exists.")]
        public Transform visualRoot;
        public Transform gripPose;
        [Tooltip("Place at the bore exit; its blue/Z axis is the bullet direction.")]
        public Transform muzzle;
        [Tooltip("Optional separately movable slide or compensator. Leave empty for a one-piece mesh.")]
        public Transform movingSlide;
        [Tooltip("Optional optic window. Leave empty for iron sights.")]
        public Transform sightWindow;
        public bool addReflexDot;
        public GameObject muzzleFlash;

        [Header("Ballistics and audio")]
        [Min(.1f)] public float fireRate = 5f;
        [Min(.1f)] public float range = 100f;
        [Min(0f)] public float damage = 25f;
        public LayerMask hitMask = ~0;
        public AudioClip[] shotClips;
        [Min(0f)] public float recoilTravel = .014f;

        [Header("Optional laser shot visual")]
        public bool addLaserShot;
        public Material laserShotMaterial;
        [Min(1f)] public float laserShotSpeed = 45f;
        [Min(.02f)] public float laserShotLength = .28f;
        [Min(.001f)] public float laserShotWidth = .009f;

        [Header("Optional power-cell ammunition")]
        public bool usesPowerCell;
        public Transform powerCellSocket;
        public JunkerPowerCell startingCell;
        public AudioClip latchClick;
        [Min(.01f)] public float cellInsertionRadius = .18f;

        [SerializeField, HideInInspector] bool installed;

        void Awake()
        {
            // Prefab assets cannot reference a scene rig. Bind spawned pistols at runtime.
            var item = GetComponent<JunkerEquipmentItem>();
            if (!item) return;
            if (!equipmentRig) equipmentRig = FindFirstObjectByType<JunkerEquipmentRig>();
            if (!item.rig) item.rig = equipmentRig;
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (installed || EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.IsPersistent(this)) return;
            EditorApplication.delayCall += () =>
            {
                if (this && !installed && !EditorApplication.isPlayingOrWillChangePlaymode && !EditorUtility.IsPersistent(this))
                    InstallPistolComponents();
            };
        }

        [ContextMenu("Install or refresh pistol components")]
        public void InstallPistolComponents()
        {
            if (Application.isPlaying) return;
            Undo.RecordObject(this, "Configure Junker pistol");

            var body = GetOrAdd<Rigidbody>();
            body.mass = mass;
            body.useGravity = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            var grab = GetOrAdd<XRGrabInteractable>();
            grab.useDynamicAttach = false;
            grab.retainTransformParent = false;
            grab.throwOnDetach = true;
            grab.attachEaseInTime = .08f;

            if (!visualRoot)
            {
                var renderer = GetComponentInChildren<Renderer>(true);
                if (renderer) visualRoot = renderer.transform;
            }
            if (GetComponentsInChildren<Collider>(true).Length == 0)
            {
                var bounds = VisualBounds();
                var box = GetOrAdd<BoxCollider>();
                box.center = transform.InverseTransformPoint(bounds.center);
                var scale = transform.lossyScale;
                box.size = new Vector3(bounds.size.x / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                    bounds.size.y / Mathf.Max(.0001f, Mathf.Abs(scale.y)),
                    bounds.size.z / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
            }

            if (!gripPose) gripPose = Anchor("Grip", Vector3.zero);
            if (!muzzle)
            {
                var bounds = VisualBounds();
                muzzle = Anchor("Muzzle", transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.center.y, bounds.max.z + .005f)));
            }
            grab.attachTransform = gripPose;

            var item = GetOrAdd<JunkerEquipmentItem>();
            item.itemId = itemId;
            if (!equipmentRig) equipmentRig = FindFirstObjectByType<JunkerEquipmentRig>();
            item.rig = equipmentRig;
            item.gripPose = gripPose;
            item.muzzle = muzzle;
            item.gripPositionOffset = gripPositionOffset;
            item.gripRotationOffset = gripRotationOffset;
            item.holsterRadius = holsterRadius;

            var shotSource = GetOrAdd<AudioSource>();
            shotSource.playOnAwake = false;
            shotSource.spatialBlend = 1f;
            shotSource.dopplerLevel = .15f;
            shotSource.minDistance = 1.5f;
            shotSource.maxDistance = 70f;
            shotSource.rolloffMode = AudioRolloffMode.Logarithmic;

            if (!muzzleFlash)
            {
                var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Undo.RegisterCreatedObjectUndo(flash, "Create pistol muzzle flash");
                flash.name = "Muzzle flash";
                flash.transform.SetParent(muzzle, false);
                flash.transform.localScale = new Vector3(.015f, .015f, .04f);
                Undo.DestroyObjectImmediate(flash.GetComponent<Collider>());
                var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/JunkerEquipment/Generated/Muzzle flash.mat");
                if (material) flash.GetComponent<Renderer>().sharedMaterial = material;
                muzzleFlash = flash;
            }
            muzzleFlash.SetActive(false);

            var pistol = GetOrAdd<JunkerPistol>();
            pistol.muzzle = muzzle;
            pistol.recoilVisual = visualRoot;
            pistol.muzzleCompe = movingSlide;
            pistol.muzzleCompeTravel = recoilTravel;
            pistol.flash = muzzleFlash;
            pistol.shotAudio = shotSource;
            pistol.shotClips = shotClips;
            pistol.fireRate = fireRate;
            pistol.range = range;
            pistol.damage = damage;
            pistol.hitMask = hitMask;
            pistol.showLaserShot = addLaserShot;
            pistol.laserShotMaterial = laserShotMaterial;
            pistol.laserShotSpeed = laserShotSpeed;
            pistol.laserShotLength = laserShotLength;
            pistol.laserShotWidth = laserShotWidth;

            if (usesPowerCell)
            {
                if (!powerCellSocket) powerCellSocket = Anchor("Power-cell socket", new Vector3(0f, .035f, -.055f));
                var socket = GetOrAdd<JunkerPistolBatterySocket>();
                socket.socketAnchor = powerCellSocket;
                socket.startingCell = startingCell;
                socket.insertionRadius = cellInsertionRadius;
                socket.latchClick = latchClick;
                if (!socket.latchAudio)
                {
                    var audioObject = transform.Find("Latch audio");
                    if (!audioObject)
                    {
                        audioObject = new GameObject("Latch audio").transform;
                        Undo.RegisterCreatedObjectUndo(audioObject.gameObject, "Create latch audio");
                        audioObject.SetParent(transform, false);
                    }
                    socket.latchAudio = audioObject.GetComponent<AudioSource>();
                    if (!socket.latchAudio) socket.latchAudio = Undo.AddComponent<AudioSource>(audioObject.gameObject);
                }
                socket.latchAudio.playOnAwake = false;
                socket.latchAudio.spatialBlend = 1f;
                pistol.batterySocket = socket;
            }

            if (addReflexDot && sightWindow)
            {
                var sight = GetOrAdd<JunkerReflexSight>();
                sight.muzzle = muzzle;
                sight.sightWindow = sightWindow;
                if (!sight.dot)
                {
                    var dot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    Undo.RegisterCreatedObjectUndo(dot, "Create pistol reflex dot");
                    dot.name = "Reflex sight dot";
                    dot.transform.SetParent(transform, false);
                    Undo.DestroyObjectImmediate(dot.GetComponent<Collider>());
                    sight.dot = dot.transform;
                    var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/JunkerEquipment/Generated/PKO reflex dot.mat");
                    if (material) dot.GetComponent<Renderer>().sharedMaterial = material;
                    dot.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                sight.dot.gameObject.SetActive(false);
            }

            installed = true;
            foreach (var component in GetComponents<Component>()) EditorUtility.SetDirty(component);
            EditorUtility.SetDirty(this);
            if (gameObject.scene.IsValid()) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }

        T GetOrAdd<T>() where T : Component
        {
            var component = GetComponent<T>();
            return component ? component : Undo.AddComponent<T>(gameObject);
        }

        Transform Anchor(string name, Vector3 localPosition)
        {
            var anchor = transform.Find(name);
            if (!anchor)
            {
                anchor = new GameObject(name).transform;
                Undo.RegisterCreatedObjectUndo(anchor.gameObject, "Create pistol " + name);
                anchor.SetParent(transform, false);
                anchor.localPosition = localPosition;
            }
            return anchor;
        }

        Bounds VisualBounds()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(transform.position, Vector3.one * .1f);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
#endif
    }
}
