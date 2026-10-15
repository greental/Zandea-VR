using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using JunkerEquipment;
using VRKoombar.WeaponsStaging.Contracts;

namespace ScavengerNPC
{
    public sealed class ScavengerWeapon : MonoBehaviour
    {
        public ScavengerEnemy owner;
        [Tooltip("Optional real pistol prefab. If empty, a child JunkerPistol is found automatically.")]
        public JunkerPistol pistol;
        public Transform muzzle;
        public GameObject flash;
        public AudioSource audioSource;
        public AudioClip shot;
        public LayerMask hitMask = ~0;
        public float damage = 12, range = 40, fireInterval = .25f, burstPause = 1.2f;
        [Range(0, 1), Tooltip("1 is accurate; lower values add random angular error to every NPC shot.")]
        public float aimPrecision = .7f;
        [Range(0, 20), Tooltip("Maximum shot deviation in degrees when Aim Precision is zero.")]
        public float spreadDegrees = 8f;
        public int burstSize = 3;
        [Min(.1f)] public float reloadSeconds = 1.2f;
        float nextShot, flashUntil;
        int burst;
        JunkerPowerCell pendingCell;
        JunkerPistolBatterySocket reloadingSocket;
        float reloadUntil;
        static Material lootLaserMaterial;
        public int ShotsFired { get; private set; }
        void Start() { ResolvePistol(); }
        public void ResolvePistol()
        {
            if (!owner) owner = GetComponentInParent<ScavengerEnemy>();
            if (!owner) return;
            if (!pistol) pistol = owner.GetComponentInChildren<JunkerPistol>(true);
            if (!pistol)
            {
                var belt = owner.GetComponent<ScavengerAmmoBelt>();
                if (belt && belt.cellPrefab) ConfigureLegacyPistol(belt.cellPrefab);
            }
            if (!pistol) return;
            muzzle = pistol.muzzle;
            var body = pistol.GetComponent<Rigidbody>();
            if (body) { body.isKinematic = true; body.useGravity = false; body.interpolation = RigidbodyInterpolation.None; }
            var grab = pistol.GetComponent<XRGrabInteractable>();
            if (grab) grab.enabled = false;
        }
        public bool TryShoot(Vector3 point)
        {
            if (!pistol) ResolvePistol();
            if (!owner || !owner.CanShoot || !muzzle || Time.time < nextShot || Vector3.Distance(muzzle.position, point) > range) return false;
            if (pistol && pistol.batterySocket && !pistol.batterySocket.CanFire())
            {
                BeginReload();
                return false;
            }
            if (pendingCell) return false;
            Vector3 direction = (point - muzzle.position).normalized;
            // The procedural hand grip does not guarantee that a legacy gun's
            // muzzle forward axis matches its projectile axis. Aim from its
            // actual muzzle position and let the obstruction check decide.
            if (!owner.ClearLine(muzzle.position, point)) return false;
            float angle = Mathf.Clamp01(1f - aimPrecision) * Mathf.Max(0f, spreadDegrees);
            if (angle > 0f)
            {
                Vector2 error = Random.insideUnitCircle * Mathf.Tan(angle * Mathf.Deg2Rad);
                Vector3 right = Vector3.Cross(direction, Vector3.up).normalized;
                if (right.sqrMagnitude < .01f) right = Vector3.right;
                Vector3 up = Vector3.Cross(right, direction).normalized;
                direction = (direction + right * error.x + up * error.y).normalized;
            }
            Vector3 scatteredPoint = muzzle.position + direction * Vector3.Distance(muzzle.position, point);
            if (pistol)
            {
                if (!pistol.TryFireNpc(owner.transform, scatteredPoint)) return false;
                ShotsFired++; burst++;
                nextShot = Time.time + (burst >= Mathf.Max(1, burstSize) ? burstPause : fireInterval);
                if (burst >= Mathf.Max(1, burstSize)) burst = 0;
                return true;
            }
            var hits = Physics.RaycastAll(muzzle.position, direction, range, hitMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(owner.transform)) continue;
                var receiver = hit.collider.GetComponentInParent<IDamageable>();
                if (receiver != null) receiver.ApplyDamage(new DamagePacket { BaseDamage = damage, DamageType = DamageType.Kinetic,
                    HitPointWorld = hit.point, HitNormalWorld = hit.normal, Source = owner.gameObject, WeaponId = "Scavenger pistol", Timestamp = Time.time });
                break;
            }
            ShotsFired++; burst++;
            nextShot = Time.time + (burst >= Mathf.Max(1, burstSize) ? burstPause : fireInterval);
            if (burst >= Mathf.Max(1, burstSize)) burst = 0;
            flashUntil = Time.time + .055f; if (flash) flash.SetActive(true);
            if (audioSource && shot) audioSource.PlayOneShot(shot);
            return true;
        }
        void BeginReload()
        {
            if (pendingCell || !pistol || !pistol.batterySocket || !owner) return;
            var belt = owner.GetComponent<ScavengerAmmoBelt>();
            if (!belt) return;
            var fresh = belt.TakeChargedCell();
            if (!fresh) return;
            reloadingSocket = pistol.batterySocket;
            reloadingSocket.OpenLatch();
            var empty = reloadingSocket.InsertedCell;
            if (empty)
            {
                reloadingSocket.Remove(empty);
                var emptyBody = empty.GetComponent<Rigidbody>();
                if (emptyBody) emptyBody.AddForce((-pistol.transform.right + Vector3.up * .4f) * .6f, ForceMode.Impulse);
            }
            pendingCell = fresh;
            reloadUntil = Time.time + Mathf.Max(.1f, reloadSeconds);
            nextShot = reloadUntil;
        }
        void Update()
        {
            if (flash && Time.time >= flashUntil) flash.SetActive(false);
            if (!pendingCell || Time.time < reloadUntil) return;
            if (reloadingSocket && reloadingSocket.CanInsert(pendingCell)) reloadingSocket.Insert(pendingCell);
            else pendingCell.SetLoose();
            pendingCell = null;
            reloadingSocket = null;
        }
        public void DropAsLoot()
        {
            if (pendingCell) { pendingCell.SetLoose(); pendingCell = null; }
            reloadingSocket = null;
            if (pistol && pistol.batterySocket && pistol.batterySocket.IsOpen) pistol.batterySocket.CloseLatch();
            if (!pistol) ConfigureLegacyPistol(null);
            // A replacement pistol may be a sibling of this driver, rather than
            // the GameObject carrying ScavengerWeapon. Drop the real pistol.
            var loot = pistol ? pistol.gameObject : gameObject;
            loot.transform.SetParent(null, true);
            var body = loot.GetComponent<Rigidbody>();
            if (!body) body = loot.AddComponent<Rigidbody>();
            body.isKinematic = false;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            var grab = loot.GetComponent<XRGrabInteractable>();
            if (grab) grab.enabled = true;
            if (owner)
            {
                var ownShapes = loot.GetComponentsInChildren<Collider>(true);
                var corpseShapes = owner.GetComponentsInChildren<Collider>(true);
                foreach (var gunShape in ownShapes)
                    foreach (var corpseShape in corpseShapes)
                        if (gunShape && corpseShape && gunShape != corpseShape) Physics.IgnoreCollision(gunShape, corpseShape);
            }
            // The driver can live on the scavenger root while the pistol is a child.
            // Disabling it here also disables the corpse and its ragdoll.
            enabled = false;
        }
        void ConfigureLegacyPistol(JunkerPowerCell startingCellPrefab)
        {
            if (pistol) return;
            var shape = GetComponent<BoxCollider>();
            if (!shape)
            {
                shape = gameObject.AddComponent<BoxCollider>();
                shape.size = new Vector3(.16f, .12f, .36f);
                shape.center = new Vector3(0, 0, .1f);
            }
            if (!GetComponent<Rigidbody>()) gameObject.AddComponent<Rigidbody>();
            if (!GetComponent<XRGrabInteractable>()) gameObject.AddComponent<XRGrabInteractable>();
            if (!GetComponent<JunkerEquipmentItem>()) gameObject.AddComponent<JunkerEquipmentItem>();
            pistol = gameObject.AddComponent<JunkerPistol>();
            pistol.muzzle = muzzle;
            pistol.flash = flash;
            pistol.shotAudio = audioSource;
            if (shot) pistol.shotClips = new[] { shot };
            pistol.range = range;
            pistol.damage = damage;
            if (!lootLaserMaterial)
            {
                var shader = Shader.Find("Sprites/Default");
                if (shader) lootLaserMaterial = new Material(shader) { color = new Color(.95f, .15f, .07f) };
            }
            pistol.showLaserShot = lootLaserMaterial != null;
            pistol.laserShotMaterial = lootLaserMaterial;
            if (startingCellPrefab)
            {
                var anchor = new GameObject("Power-cell socket").transform;
                anchor.SetParent(transform, false);
                anchor.localPosition = new Vector3(0f, -.055f, .05f);
                var socket = gameObject.AddComponent<JunkerPistolBatterySocket>();
                socket.socketAnchor = anchor;
                pistol.batterySocket = socket;
                var cell = Instantiate(startingCellPrefab);
                cell.name = "Scavenger loaded R4";
                if (!socket.Insert(cell)) Destroy(cell.gameObject);
            }
        }
        void OnDisable() { if (flash) flash.SetActive(false); }
    }
}
