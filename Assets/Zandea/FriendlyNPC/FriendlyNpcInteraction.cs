using System;
using KenshiVR.CharacterCreator;
using KenshiVR.MeleeCombat;
using ScavengerNPC;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Zandea.FriendlyNPC
{
    [DisallowMultipleComponent]
    public sealed class FriendlyNpcInteraction : MonoBehaviour
    {
        public string npcId = "merchant-daran";
        public string displayName = "Daran";
        public StarvingBandit brain;
        public ScavengerHealth health;
        public NPCFacialExpressionController face;
        public Transform head;
        public Transform playerHead;
        public XRSimpleInteractable conversationTarget;
        public DialogueDefinition dialogue;
        public MonoBehaviour conditionSource;
        public AudioSource mouthAudio;
        public TMP_Text subtitle;
        public PalmDialogueChoices choices;
        [Min(1f)] public float maxConversationDistance = 4f;
        [Min(.03f)] public float turnDegreesPerSecond = 85f;
        public UnityEvent onQuestHook = new();
        public event Action<bool> TradingChanged;
        public event Action ConversationEnded;

        DialogueNode current;
        readonly System.Collections.Generic.List<DialogueResponse> availableResponses = new(4);
        float fallbackEnd;
        bool active;
        bool trading;
        bool wasHostile;
        public bool IsConversing => active;
        public bool IsTrading => active && trading;

        void Awake()
        {
            if (!brain) brain = GetComponent<StarvingBandit>();
            if (!health) health = GetComponent<ScavengerHealth>();
            if (!face) face = GetComponentInChildren<NPCFacialExpressionController>(true);
            if (!conversationTarget) conversationTarget = GetComponentInChildren<XRSimpleInteractable>(true);
            if (!choices) choices = FindFirstObjectByType<PalmDialogueChoices>();
            if (mouthAudio)
            {
                mouthAudio.spatialBlend = 1f;
                mouthAudio.rolloffMode = AudioRolloffMode.Logarithmic;
                mouthAudio.minDistance = 1f;
                mouthAudio.maxDistance = 10f;
                mouthAudio.playOnAwake = false;
            }
            if (subtitle) subtitle.gameObject.SetActive(false);
            if (choices) choices.Hide();
        }

        void OnEnable()
        {
            if (conversationTarget) conversationTarget.selectEntered.AddListener(OnSelected);
            if (health)
            {
                health.onDamaged.AddListener(OnDamaged);
                health.StateChanged += OnHealthChanged;
            }
        }

        void OnDisable()
        {
            if (conversationTarget) conversationTarget.selectEntered.RemoveListener(OnSelected);
            if (health)
            {
                health.onDamaged.RemoveListener(OnDamaged);
                health.StateChanged -= OnHealthChanged;
            }
            EndConversation();
        }

        void OnSelected(SelectEnterEventArgs args)
        {
            var hand = args.interactorObject?.transform;
            if (!hand) return;
            var origin = hand.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
            if (!origin || !origin.Camera) return;
            TryBegin(origin.Camera.transform);
        }

        public bool TryBegin(Transform viewer)
        {
            if (active || !viewer || !dialogue || !CanTalk() ||
                Vector3.Distance(viewer.position, transform.position) > maxConversationDistance) return false;
            playerHead = viewer;
            active = true;
            wasHostile = brain && brain.hostileToPlayer;
            ShowNode(dialogue.Find(dialogue.entryNodeId));
            return active;
        }

        bool CanTalk() => (!health || health.State == NpcConsciousness.Conscious) &&
            (!brain || !brain.hostileToPlayer);

        void OnDamaged()
        {
            EndConversation();
            if (brain) brain.hostileToPlayer = true;
        }

        void OnHealthChanged(NpcConsciousness _, NpcConsciousness next)
        {
            if (next != NpcConsciousness.Conscious) EndConversation();
        }

        void Update()
        {
            if (!active) return;
            if (!playerHead || !CanTalk() || brain && brain.hostileToPlayer != wasHostile ||
                Vector3.Distance(playerHead.position, transform.position) > maxConversationDistance ||
                IsPlayerIncapacitated())
            { EndConversation(); return; }

            var direction = Vector3.ProjectOnPlane(playerHead.position - transform.position, Vector3.up);
            if (direction.sqrMagnitude > .01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(direction), turnDegreesPerSecond * Time.deltaTime);

            if (mouthAudio && mouthAudio.isPlaying && current?.mouthEnvelope is { Length: > 0 })
            {
                int index = Mathf.Min(current.mouthEnvelope.Length - 1,
                    Mathf.FloorToInt(mouthAudio.timeSamples / (float)mouthAudio.clip.frequency * 30f));
                if (face) face.SetSpeechOpen(current.mouthEnvelope[index]);
            }
            else if (face) face.SetSpeechOpen(0f);

            if (current != null && (!mouthAudio || !mouthAudio.isPlaying) && Time.time >= fallbackEnd)
            {
                var finished = current;
                current = null;
                if (subtitle) subtitle.gameObject.SetActive(false);
                if (finished.endOnComplete) { EndConversation(); return; }
                availableResponses.Clear();
                foreach (var response in finished.responses)
                    if (response != null && ConditionAllows(response.conditionId)) availableResponses.Add(response);
                if (availableResponses.Count > 0) choices?.Show(availableResponses, SelectResponse);
                else if (!string.IsNullOrEmpty(finished.nextNodeId)) ShowNode(dialogue.Find(finished.nextNodeId));
            }
        }

        bool IsPlayerIncapacitated()
        {
            var player = playerHead ? playerHead.GetComponentInParent<ScavengerPlayerHealth>() : null;
            return player && player.IsDead;
        }

        void ShowNode(DialogueNode node)
        {
            choices?.Hide();
            if (node == null || !ConditionAllows(node.conditionId)) { EndConversation(); return; }
            current = node;
            if (face) face.SetExpression(node.expression);
            if (subtitle)
            {
                subtitle.text = displayName + ": " + node.text;
                subtitle.gameObject.SetActive(true);
            }
            if (mouthAudio)
            {
                mouthAudio.Stop();
                mouthAudio.clip = node.audio;
                if (node.audio) mouthAudio.Play();
            }
            fallbackEnd = Time.time + (node.audio ? node.audio.length + .2f :
                Mathf.Max(1.5f, node.text.Length / 14f));
            Execute(node.onEnter);
        }

        bool ConditionAllows(string conditionId) => string.IsNullOrEmpty(conditionId) ||
            conditionSource is IDialogueConditionSource source && source.Evaluate(conditionId);

        void SelectResponse(int index)
        {
            if (!active || current != null || !choices || !choices.TryGetResponse(index, out var response)) return;
            choices.Hide();
            Execute(response.action);
            if (active) ShowNode(dialogue.Find(response.nextNodeId));
        }

        void Execute(DialogueAction action)
        {
            switch (action)
            {
                case DialogueAction.BeginTrading:
                    trading = true;
                    TradingChanged?.Invoke(true);
                    break;
                case DialogueAction.QuestHook: onQuestHook.Invoke(); break;
                case DialogueAction.EndConversation: EndConversation(); break;
            }
        }

        public void PlayContextLine(string nodeId)
        {
            if (!active || !dialogue) return;
            var node = dialogue.Find(nodeId);
            if (node != null) ShowNode(node);
        }

        public void EndConversation()
        {
            if (!active) return;
            active = false;
            current = null;
            if (mouthAudio) mouthAudio.Stop();
            if (subtitle) subtitle.gameObject.SetActive(false);
            if (face) { face.SetSpeechOpen(0f); face.ResetExpression(); }
            choices?.Hide();
            if (trading) { trading = false; TradingChanged?.Invoke(false); }
            ConversationEnded?.Invoke();
        }
    }
}
