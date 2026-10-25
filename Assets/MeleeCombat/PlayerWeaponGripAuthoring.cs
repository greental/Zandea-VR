using System;
using System.Collections.Generic;
using UnityEngine;

namespace KenshiVR.MeleeCombat
{
    // Lives on the actual player prefab. Each saved entry is applied whenever the
    // matching weapon is grabbed, while the editor preview is posed on the hand.
    public sealed class PlayerWeaponGripAuthoring : MonoBehaviour
    {
        [Serializable] public sealed class Fit
        {
            public string weaponId;
            public GameObject weaponPrefab;
            public Vector3 rightPosition, rightEuler, leftPosition, leftEuler;
            public Vector3 secondaryPosition, secondaryEuler;
        }
        public GameObject selectedWeapon;
        public List<Fit> savedFits = new();
        [HideInInspector] public GameObject preview;

        public bool TryGet(string id, out Fit fit)
        {
            fit = savedFits.Find(item => item != null && item.weaponId == id);
            return fit != null;
        }
    }
}
