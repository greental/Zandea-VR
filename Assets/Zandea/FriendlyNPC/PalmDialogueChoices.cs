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

        void Awake()
        {
            var origin = GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
            viewer = origin && origin.Camera ? origin.Camera.transform : Camera.main ? Camera.main.transform : null;
            leftStick = new InputAction("Dialogue left stick", InputActionType.Value, "<XRController>{LeftHand}/primary2DAxis");
            rightStick = new InputAction("Dialogue right stick", InputActionType.Value, "<XRController>{RightHand}/primary2DAxis");
            leftConfirm = new InputAction("Dialogue left confirm", InputActionType.Button, "<XRController>{LeftHand}/primaryButton");
            rightConfirm = new InputAction("Dialogue right confirm", InputActionType.Button, "<XRController>{RightHand}/primaryButton");
            BuildNotebook();
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
                !HandRaised(activeHand, hideDistance))
                SetVisible(null);
            if (!visible)
            {
                if (HandRaised(leftHand, showDistance)) SetVisible(leftHand);
                else if (HandRaised(rightHand, showDistance)) SetVisible(rightHand);
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
            var hand = activeHand;
            if (!hand) return;
            // The direct interactor follows the tracked controller. The glove visual can
            // be offset by its model rig, which previously put the panel a metre left.
            panel.transform.position = hand.transform.position + Vector3.up * .12f +
                viewer.right * (hand == leftHand ? .09f : -.09f);
            // TMP's readable face points away from its local forward axis.
            panel.transform.rotation = Quaternion.LookRotation(panel.transform.position - viewer.position, Vector3.up);
        }

        bool HandRaised(XRDirectInteractor hand, float distance)
        {
            if (!hand || !viewer || !hand.gameObject.activeInHierarchy || hand.hasSelection ||
                hand.isPerformingManualInteraction) return false;
            var fromEye = hand.transform.position - viewer.position;
            float belowEye = -fromEye.y;
            return fromEye.sqrMagnitude <= distance * distance &&
                belowEye >= .08f && belowEye <= .68f &&
                Vector3.Dot(viewer.forward, fromEye) > .05f &&
                Mathf.Abs(Vector3.Dot(viewer.right, fromEye)) < .4f;
        }

        void SetVisible(XRDirectInteractor hand)
        {
            if (activeHand == hand && visible == (hand != null) && (!panel || panel.activeSelf == visible)) return;
            FriendlyInputReservation.Set(activeHand, false);
            activeHand = hand;
            visible = hand != null;
            if (visible) lastShownAt = Time.unscaledTime;
            if (panel)
            {
                panel.SetActive(visible);
            }
            FriendlyInputReservation.Set(activeHand, visible, true);
        }

        static Material NotebookMaterial(Color color)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Standard");
            return new Material(shader) { color = color };
        }

        void BuildNotebook()
        {
            if (!panel || panel.transform.Find("Notebook cover")) return;
            AddNotebookPart("Notebook cover", new Vector3(0f, .065f, .03f),
                new Vector3(.39f, .31f, .018f), NotebookMaterial(new Color(.19f, .12f, .075f)));
            AddNotebookPart("Notebook pages", new Vector3(.007f, .065f, .014f),
                new Vector3(.365f, .285f, .012f), NotebookMaterial(new Color(.82f, .75f, .58f)));
            AddNotebookPart("Notebook binding", new Vector3(-.177f, .065f, -.002f),
                new Vector3(.018f, .292f, .01f), NotebookMaterial(new Color(.12f, .075f, .045f)));
        }

        void AddNotebookPart(string name, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = name;
            part.transform.SetParent(panel.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(part.GetComponent<Collider>());
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
