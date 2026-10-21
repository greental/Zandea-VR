using KenshiVR.MeleeCombat;
using UnityEngine;

namespace Zandea.Props
{
    public sealed class PropWeaponRack : MonoBehaviour
    {
        public NpcItemDefinition[] items;
        public GameObject[] weaponPrefabs;
        public Vector3 firstSocket = new(-.55f,.28f,0f);
        public Vector3 spacing = new(.36f,0f,0f);
        void Awake()
        {
            foreach(var old in GetComponentsInChildren<PropRackSocket>(true)) if(old.transform.parent==transform){old.enabled=false;old.gameObject.SetActive(false);Destroy(old.gameObject);}
            int count=Mathf.Min(items?.Length??0,weaponPrefabs?.Length??0);
            for(int i=0;i<count;i++)
            {
                if(!items[i]||!weaponPrefabs[i])continue;
                var socket=new GameObject("Weapon socket "+i).AddComponent<PropRackSocket>();
                socket.transform.SetParent(transform,false);socket.transform.localPosition=firstSocket+spacing*i;
                socket.transform.localRotation=Quaternion.Euler(0,0,90);socket.startingItem=items[i];socket.weaponPrefab=weaponPrefabs[i];
            }
        }
    }
}
