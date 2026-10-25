using System.Collections.Generic;
using UnityEngine;

namespace Zandea.FriendlyNPC
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class TradeZone : MonoBehaviour
    {
        [Tooltip("Draw an open holding box around this buy or sell volume.")]
        public bool holdingVolume;
        [SerializeField, Min(.005f)] float frameThickness = .012f;
        BoxCollider box;
        Mesh frameMesh;
        VendorCounter counter;
        readonly HashSet<TradeItem> attemptedItems = new();
        readonly HashSet<CurrencyPouch> attemptedPurses = new();

        void Awake()
        {
            box = GetComponent<BoxCollider>();
            counter = GetComponentInParent<VendorCounter>();
            if (holdingVolume && Application.isPlaying) BuildFrame();
        }

        void OnEnable()
        {
            TradeItem.Selected += OnItemSelected;
            CurrencyPouch.Selected += OnPurseSelected;
        }

        void OnDisable()
        {
            TradeItem.Selected -= OnItemSelected;
            CurrencyPouch.Selected -= OnPurseSelected;
            attemptedItems.Clear();
            attemptedPurses.Clear();
        }

        void OnItemSelected(TradeItem item) => attemptedItems.Remove(item);
        void OnPurseSelected(CurrencyPouch pouch) => attemptedPurses.Remove(pouch);

        void OnTriggerStay(Collider other)
        {
            if (!counter) counter = GetComponentInParent<VendorCounter>();
            if (!counter || !counter.Trading) return;
            if (this == counter.paymentSpot)
            {
                var pouch = other.GetComponentInParent<CurrencyPouch>();
                if (pouch && !pouch.Grab.isSelected && attemptedPurses.Add(pouch))
                    counter.TryPresentPurse(pouch);
                return;
            }
            if (this != counter.buyArea && this != counter.sellArea) return;
            var item = other.GetComponentInParent<TradeItem>();
            if (item && !item.IsHeld && attemptedItems.Add(item)) counter.TryOffer(item);
        }

        void OnTriggerExit(Collider other)
        {
            var item = other.GetComponentInParent<TradeItem>();
            if (item && !Contains(item)) attemptedItems.Remove(item);
            var pouch = other.GetComponentInParent<CurrencyPouch>();
            if (pouch && !Contains(pouch.transform.position)) attemptedPurses.Remove(pouch);
        }

        void OnDestroy()
        {
            if (frameMesh) Destroy(frameMesh);
        }

        void OnDrawGizmosSelected()
        {
            var volume = GetComponent<BoxCollider>();
            if (!holdingVolume || !volume) return;
            Gizmos.color = Color.yellow;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(volume.center, volume.size);
        }

        void BuildFrame()
        {
            var surface = GetComponent<Renderer>();
            if (!surface || !box) return;
            var frame = new GameObject("Holding box outline");
            frame.transform.SetParent(transform, false);
            var filter = frame.AddComponent<MeshFilter>();
            var renderer = frame.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = surface.sharedMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            frameMesh = new Mesh { name = "Trade holding box outline" };
            var vertices = new List<Vector3>(288);
            var triangles = new List<int>(432);
            var min = box.center - box.size * .5f;
            var max = box.center + box.size * .5f;
            var scale = transform.lossyScale;
            var thickness = new Vector3(frameThickness / Mathf.Max(.001f, Mathf.Abs(scale.x)),
                frameThickness / Mathf.Max(.001f, Mathf.Abs(scale.y)),
                frameThickness / Mathf.Max(.001f, Mathf.Abs(scale.z)));
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    AddBar(new Vector3(min.x, y == 0 ? min.y : max.y, x == 0 ? min.z : max.z),
                        new Vector3(max.x, y == 0 ? min.y : max.y, x == 0 ? min.z : max.z),
                        thickness, vertices, triangles);
            for (int x = 0; x < 2; x++)
                for (int z = 0; z < 2; z++)
                    AddBar(new Vector3(x == 0 ? min.x : max.x, min.y, z == 0 ? min.z : max.z),
                        new Vector3(x == 0 ? min.x : max.x, max.y, z == 0 ? min.z : max.z),
                        thickness, vertices, triangles);
            for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                    AddBar(new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, min.z),
                        new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, max.z),
                        thickness, vertices, triangles);
            frameMesh.SetVertices(vertices);
            frameMesh.SetTriangles(triangles, 0);
            frameMesh.RecalculateBounds();
            filter.sharedMesh = frameMesh;
        }

        static void AddBar(Vector3 start, Vector3 end, Vector3 thickness,
            List<Vector3> vertices, List<int> triangles)
        {
            var half = thickness * .5f;
            var low = Vector3.Min(start, end) - half;
            var high = Vector3.Max(start, end) + half;
            int first = vertices.Count;
            vertices.Add(new Vector3(low.x, low.y, low.z));
            vertices.Add(new Vector3(high.x, low.y, low.z));
            vertices.Add(new Vector3(high.x, high.y, low.z));
            vertices.Add(new Vector3(low.x, high.y, low.z));
            vertices.Add(new Vector3(low.x, low.y, high.z));
            vertices.Add(new Vector3(high.x, low.y, high.z));
            vertices.Add(new Vector3(high.x, high.y, high.z));
            vertices.Add(new Vector3(low.x, high.y, high.z));
            int[] faces = { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4, 1, 2, 6, 1, 6, 5,
                2, 3, 7, 2, 7, 6, 3, 0, 4, 3, 4, 7 };
            foreach (int index in faces) triangles.Add(first + index);
        }
        public bool Contains(Vector3 point)
        {
            if (!box || !box.enabled) return false;
            var local = box.transform.InverseTransformPoint(point) - box.center;
            var half = box.size * .5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        public bool Contains(TradeItem item) => item && box && box.enabled &&
            (Contains(item.transform.position) || item.Intersects(box.bounds));
    }
}
