using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Zandea.FriendlyNPC
{
    public static class FriendlyInputReservation
    {
        static readonly HashSet<XRDirectInteractor> Reserved = new();
        static readonly Dictionary<XRDirectInteractor, InteractionLayerMask> SuppressedLayers = new();
        public static bool IsReserved(XRDirectInteractor hand) => hand && Reserved.Contains(hand);
        public static void Set(XRDirectInteractor hand, bool reserved, bool suppressGrab = false)
        {
            if (!hand) return;
            if (reserved)
            {
                Reserved.Add(hand);
                if (suppressGrab && !SuppressedLayers.ContainsKey(hand))
                {
                    SuppressedLayers.Add(hand, hand.interactionLayers);
                    hand.interactionLayers = 0;
                }
            }
            else
            {
                Reserved.Remove(hand);
                if (SuppressedLayers.Remove(hand, out var original)) hand.interactionLayers = original;
            }
        }
    }

    // The choice canvas is anchored to one controller; no optical tracking is required.
    public sealed class PalmDialogueChoices : MonoBehaviour
    {
        public Transform leftPalm, rightPalm;
        public XRDirectInteractor leftHand, rightHand;
        public GameObject panel;
        public TMP_Text[] optionLabels;
        public XRSimpleInteractable[] optionTargets;
        [Tooltip("Movement and turn providers paused only while choices are visible.")]
        public Behaviour[] pauseWhileVisible;
        [Tooltip("A free controller raised in front of the chest reveals the choices. No optical hand tracking is required.")]
        [Min(.2f)] public float showDistance = .72f;
        [Min(.2f)] public float hideDistance = .82f;
        [Min(.1f)] public float stickRepeatSeconds = .3f;
        readonly List<DialogueResponse> responses = new(4);
        InputAction leftStick, rightStick, leftConfirm, rightConfirm;
        Action<int> selected;
        XRDirectInteractor activeHand;
        int highlighted;
        float nextStick;
        bool visible;
        Transform viewer;
        float lastShownAt;
        bool[] previousProviderStates;

        void Awake()
        {
            var origin = GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
            viewer = origin && origin.Camera ? origin.Camera.transform : Camera.main ? Camera.main.transform : null;
            leftStick = new InputAction("Dialogue left stick", InputActionType.Value, "<XRController>{LeftHand}/primary2DAxis");
            rightStick = new InputAction("Dialogue right stick", InputActionType.Value, "<XRController>{RightHand}/primary2DAxis");
            leftConfirm = new InputAction("Dialogue left confirm", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
            rightConfirm = new InputAction("Dialogue right confirm", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
            previousProviderStates = new bool[pauseWhileVisible?.Length ?? 0];
            if (optionTargets != null)
                for (int i = 0; i < optionTargets.Length; i++)
                    if (optionTargets[i])
                    {
                        int choice = i;
                        optionTargets[i].selectEntered.AddListener(_ => Choose(choice));
                    }
            Hide();
        }

        void OnEnable()
        {
            leftStick?.Enable(); rightStick?.Enable(); leftConfirm?.Enable(); rightConfirm?.Enable();
        }

        void OnDisable()
        {
            Hide();
            leftStick?.Disable(); rightStick?.Disable(); leftConfirm?.Disable(); rightConfirm?.Disable();
        }

        void OnDestroy()
        {
            leftStick?.Dispose(); rightStick?.Dispose(); leftConfirm?.Dispose(); rightConfirm?.Dispose();
        }

        public void Show(IReadOnlyList<DialogueResponse> options, Action<int> callback)
        {
            Hide();
            selected = callback;
            if (options == null) return;
            foreach (var response in options)
                if (response != null && responses.Count < 4) responses.Add(response);
            highlighted = 0;
            RefreshLabels();
        }

        public bool TryGetResponse(int index, out DialogueResponse response)
        {
            response = index >= 0 && index < responses.Count ? responses[index] : null;
            return response != null;
        }

        void Update()
        {
            if (responses.Count == 0) return;
            if (visible && Time.unscaledTime - lastShownAt > .3f &&
                !HandRaised(activeHand, activeHand == leftHand ? leftPalm : rightPalm, hideDistance))
                SetVisible(null);
            if (!visible)
            {
                if (CanShow(leftHand, leftPalm)) SetVisible(leftHand);
                else if (CanShow(rightHand, rightPalm)) SetVisible(rightHand);
            }
            if (!visible) return;
            // The opposite controller's stick and A/X button work when pointing is awkward.
            var stick = activeHand == leftHand ? rightStick : leftStick;
            var confirm = activeHand == leftHand ? rightConfirm : leftConfirm;
            float vertical = stick.ReadValue<Vector2>().y;
            if (Time.time >= nextStick && Mathf.Abs(vertical) > .65f)
            {
                highlighted = (highlighted + (vertical < 0 ? 1 : responses.Count - 1)) % responses.Count;
                nextStick = Time.time + stickRepeatSeconds;
                RefreshLabels();
            }
            if (confirm.WasPressedThisFrame() ||
                ((activeHand == leftHand ? leftConfirm : rightConfirm).WasPressedThisFrame() &&
                 (activeHand == leftHand ? !rightHand || rightHand.hasSelection : !leftHand || leftHand.hasSelection)))
                Choose(highlighted);
        }

        void LateUpdate()
        {
            if (!visible || !panel || !viewer) return;
            var palm = activeHand == leftHand ? leftPalm : rightPalm;
            if (!palm) return;
            var towardViewer = viewer.position - palm.position;
            panel.transform.position = palm.position + Vector3.up * .12f + towardViewer.normalized * .035f;
            // TMP's readable face points away from its local forward axis.
            panel.transform.rotation = Quaternion.LookRotation(panel.transform.position - viewer.position, Vector3.up);
        }

        bool HandRaised(XRDirectInteractor hand, Transform palm, float distance)
        {
            if (!hand || !palm || !viewer || !hand.gameObject.activeInHierarchy || hand.hasSelection ||
                hand.isPerformingManualInteraction) return false;
            var fromEye = palm.position - viewer.position;
            float belowEye = -fromEye.y;
            return fromEye.sqrMagnitude <= distance * distance &&
                belowEye >= .08f && belowEye <= .68f &&
                Vector3.Dot(viewer.forward, fromEye) > .05f;
        }

        bool CanShow(XRDirectInteractor hand, Transform palm) => HandRaised(hand, palm, showDistance);

        void SetVisible(XRDirectInteractor hand)
        {
            if (activeHand == hand && visible == (hand != null) && (!panel || panel.activeSelf == visible)) return;
            if (visible) PauseProviders(false);
            FriendlyInputReservation.Set(activeHand, false);
            activeHand = hand;
            visible = hand != null;
            if (visible) lastShownAt = Time.unscaledTime;
            if (panel)
            {
                panel.SetActive(visible);
            }
            FriendlyInputReservation.Set(activeHand, visible, true);
            if (visible) PauseProviders(true);
        }

        void PauseProviders(bool pause)
        {
            if (pauseWhileVisible == null) return;
            for (int i = 0; i < pauseWhileVisible.Length; i++)
            {
                var provider = pauseWhileVisible[i];
                if (!provider) continue;
                if (pause)
                {
                    previousProviderStates[i] = provider.enabled;
                    provider.enabled = false;
                }
                else provider.enabled = previousProviderStates[i];
            }
        }

        void RefreshLabels()
        {
            if (optionLabels == null) return;
            for (int i = 0; i < optionLabels.Length; i++)
                if (optionLabels[i])
                {
                    bool used = i < responses.Count;
                    optionLabels[i].gameObject.SetActive(used);
                    if (used) optionLabels[i].text = (i == highlighted ? "› " : "  ") + responses[i].text;
                }
        }

        void Choose(int index)
        {
            if (!visible || index < 0 || index >= responses.Count) return;
            selected?.Invoke(index);
        }

        public void Hide()
        {
            SetVisible(null);
            responses.Clear();
            selected = null;
        }
    }
}
