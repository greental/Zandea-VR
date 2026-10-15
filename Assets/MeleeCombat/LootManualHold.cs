using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace KenshiVR.MeleeCombat
{
    public sealed class LootManualHold : MonoBehaviour
    {
        XRDirectInteractor hand;
        PlayerMartialHand martialHand;
        public void Bind(XRDirectInteractor selected)
        {
            hand = selected;
            foreach (var candidate in FindObjectsByType<PlayerMartialHand>(FindObjectsSortMode.None))
                if (candidate.interactor == hand) { martialHand = candidate; break; }
        }
        void Update()
        {
            if (!hand || !hand.isPerformingManualInteraction) { enabled = false; return; }
            if (martialHand && martialHand.glove && martialHand.glove.input &&
                (martialHand.glove.input.gripValue.action?.ReadValue<float>() ?? 0f) < .35f)
            { hand.EndManualInteraction(); enabled = false; }
        }
        void OnDestroy()
        {
            if (hand && hand.isPerformingManualInteraction && hand.interactablesSelected.Count > 0 &&
                ReferenceEquals(hand.interactablesSelected[0], GetComponent<XRGrabInteractable>())) hand.EndManualInteraction();
        }
    }
}
