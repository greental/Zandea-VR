using JunkerEquipment;
using KenshiVR.MeleeCombat;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace Zandea.Props
{
    [DisallowMultipleComponent]
    public sealed class ChestStorageVolume : MonoBehaviour
    {
        static readonly Dictionary<string,List<VRInventoryItem>> persisted=new();
        public PropContainer container;
        BackpackInventory inventory;
        bool parked;
        void Awake()
        {
            if (!container) container = GetComponent<PropContainer>();
            inventory = GetComponent<BackpackInventory>();
            if (!inventory) inventory = gameObject.AddComponent<BackpackInventory>();
            inventory.alwaysOpenStorage = true;
            inventory.hideContentsOnBack = false;
            var root = transform.Find("StoredItemsRoot");
            if (!root) { root = new GameObject("StoredItemsRoot").transform; root.SetParent(transform, false); }
            inventory.storedItemsRoot = root;
            var volumeRoot = transform.Find("StorageVolume");
            if (!volumeRoot) { volumeRoot = new GameObject("StorageVolume").transform; volumeRoot.SetParent(transform, false); }
            var volume = volumeRoot.GetComponent<BackpackStorageVolume>();
            if (!volume) volume = volumeRoot.gameObject.AddComponent<BackpackStorageVolume>();
            volume.inventory = inventory;
            var box = volume.Box; box.isTrigger = true;
            Bounds cavity = CavityBounds();
            volumeRoot.localPosition = cavity.center; volumeRoot.localRotation = Quaternion.identity; volumeRoot.localScale = Vector3.one;
            box.center = Vector3.zero; box.size = cavity.size;
            volume.validStorageIndicator = CreateIndicator(box);
            inventory.storageVolume = volume;
        }
        IEnumerator Start(){yield return null;Restore();}
        void OnEnable(){StartCoroutine(RestoreNextFrame());}
        IEnumerator RestoreNextFrame(){yield return null;Restore();}
        void OnDisable(){Park();}
        void Park()
        {
            if(parked||!inventory||!container||string.IsNullOrEmpty(container.StableId))return;parked=true;
            var saved=new List<VRInventoryItem>();
            foreach(var item in new List<VRInventoryItem>(inventory.Items))if(item)
            {item.ReleaseFromContainer(false);item.gameObject.SetActive(false);DontDestroyOnLoad(item.gameObject);saved.Add(item);}
            if(saved.Count>0)persisted[container.StableId]=saved;
        }
        void Restore()
        {
            parked=false;if(!inventory||!container||string.IsNullOrEmpty(container.StableId)||!persisted.TryGetValue(container.StableId,out var saved))return;
            foreach(var item in saved)if(item)inventory.RestoreStored(item);persisted.Remove(container.StableId);
        }
        Bounds CavityBounds()
        {
            var shapes = GetComponents<BoxCollider>();
            if (shapes.Length == 0) return new Bounds(Vector3.up * .12f, new Vector3(.6f, .25f, .4f));
            float minX=float.PositiveInfinity,maxX=float.NegativeInfinity,minZ=float.PositiveInfinity,maxZ=float.NegativeInfinity, bottom=float.PositiveInfinity, top=float.NegativeInfinity;
            foreach(var shape in shapes) if(shape && !shape.isTrigger)
            { var b=shape.bounds; minX=Mathf.Min(minX,b.min.x);maxX=Mathf.Max(maxX,b.max.x);minZ=Mathf.Min(minZ,b.min.z);maxZ=Mathf.Max(maxZ,b.max.z);bottom=Mathf.Min(bottom,b.min.y);top=Mathf.Max(top,b.max.y); }
            Vector3 worldCenter=new((minX+maxX)*.5f,Mathf.Lerp(bottom,top,.58f),(minZ+maxZ)*.5f);
            Vector3 worldSize=new(Mathf.Max(.3f,(maxX-minX)*.72f),Mathf.Max(.18f,(top-bottom)*.45f),Mathf.Max(.25f,(maxZ-minZ)*.62f));
            return new Bounds(transform.InverseTransformPoint(worldCenter), new Vector3(worldSize.x/Mathf.Max(.001f,transform.lossyScale.x),worldSize.y/Mathf.Max(.001f,transform.lossyScale.y),worldSize.z/Mathf.Max(.001f,transform.lossyScale.z)));
        }
        static GameObject CreateIndicator(BoxCollider box)
        {
            var go=new GameObject("Valid placement green box"); go.transform.SetParent(box.transform,false);
            var line=go.AddComponent<LineRenderer>(); line.useWorldSpace=false; line.loop=false; line.widthMultiplier=.008f;
            line.startColor=line.endColor=new Color(.15f,1f,.3f,1f); line.material=new Material(Shader.Find("Sprites/Default"));
            Vector3 h=box.size*.5f; Vector3[] p={new(-h.x,-h.y,-h.z),new(h.x,-h.y,-h.z),new(h.x,-h.y,h.z),new(-h.x,-h.y,h.z),new(-h.x,-h.y,-h.z),new(-h.x,h.y,-h.z),new(h.x,h.y,-h.z),new(h.x,-h.y,-h.z),new(h.x,h.y,-h.z),new(h.x,h.y,h.z),new(h.x,-h.y,h.z),new(h.x,h.y,h.z),new(-h.x,h.y,h.z),new(-h.x,-h.y,h.z),new(-h.x,h.y,h.z),new(-h.x,h.y,-h.z)};
            line.positionCount=p.Length; line.SetPositions(p); go.SetActive(false); return go;
        }
    }
}
