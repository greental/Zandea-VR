using UnityEngine;
using UnityEngine.EventSystems;

namespace VRKoombar.WeaponsStaging.Core
{
    /// <summary>
    /// Attached to each aim dot when <see cref="WeaponAimHudIndicators.HudCalibrationMode"/> is on.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeaponAimDotDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private WeaponAimHudIndicators owner;
        [SerializeField] private int dotIndex;

        public void Configure(WeaponAimHudIndicators hud, int index)
        {
            owner = hud;
            dotIndex = index;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            owner?.NotifyDotBeginDrag(dotIndex);
        }

        public void OnDrag(PointerEventData eventData)
        {
            owner?.NotifyDotDrag(dotIndex, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            owner?.NotifyDotEndDrag(dotIndex);
        }
    }
}
