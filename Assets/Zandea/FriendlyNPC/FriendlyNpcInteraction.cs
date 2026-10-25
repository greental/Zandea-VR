using System;
using System.Collections.Generic;
using KenshiVR.CharacterCreator;
using KenshiVR.MeleeCombat;
using ScavengerNPC;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.AI;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;

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
        public NpcVoiceBank voiceBank;
        public MonoBehaviour conditionSource;
        public AudioSource mouthAudio;
        public TMP_Text subtitle;
        public PalmDialogueChoices choices;
        [Min(1f)] public float maxConversationDistance = 4f;
        [Min(.03f)] public float turnDegreesPerSecond = 85f;
        [Min(.2f)] public float closeGripDistance = .65f;
        public UnityEvent onQuestHook = new();
        [Header("Friendly idle movement")]
        [Min(0f)] public float wanderRadius = 3f;
        public Vector2 wanderPauseSeconds = new(4f, 9f);
        [Header("Ambient market calls")]
        public Vector2 ambientCallIntervalSeconds = new(45f, 120f);
        public event Action<bool> TradingChanged;
        public event Action ConversationEnded;

        DialogueNode current;
        readonly System.Collections.Generic.List<DialogueResponse> availableResponses = new(4);
        float fallbackEnd;
        float mouthOpening;
        float[] speakingEnvelope;
        readonly Dictionary<string, int> lastTakeByEvent = new();
        readonly Dictionary<string, float> lastPlayedAt = new();
        float nextAmbientCall;
        float ambientEnd;
        bool ambientSpeaking;
        int lastAmbientIndex = -1;
        bool alternateGreeting = true;
        string pendingBark;
        string pendingContext;
        bool barkSpeaking;
        float barkEnd;
        bool active;
        bool trading;
        bool wasHostile;
        bool greetedPlayer;
        bool nearby;
        Transform cachedViewer;
        float nextViewerLookup;
        NavMeshAgent navAgent;
        InputAction leftTalkGrip, rightTalkGrip;
        GameObject talkCue;
        Vector3 home;
        float nextWander;
        public bool IsConversing => active;
        public bool IsTrading => active && trading;
        public bool IsAttending => active || nearby;

        void Awake()
        {
            if (!brain) brain = GetComponent<StarvingBandit>();
            if (!health) health = GetComponent<ScavengerHealth>();
            if (!face) face = GetComponentInChildren<NPCFacialExpressionController>(true);
            if (!conversationTarget) conversationTarget = GetComponentInChildren<XRSimpleInteractable>(true);
            if (!choices) choices = FindFirstObjectByType<PalmDialogueChoices>();
            navAgent = GetComponent<NavMeshAgent>();
            home = transform.position;
            var origin = FindFirstObjectByType<XROrigin>();
            cachedViewer = origin && origin.Camera ? origin.Camera.transform :
                Camera.main ? Camera.main.transform : null;
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
            leftTalkGrip = new InputAction("Talk to NPC left grip", InputActionType.Button,
                "<XRController>{LeftHand}/gripPressed");
            rightTalkGrip = new InputAction("Talk to NPC right grip", InputActionType.Button,
                "<XRController>{RightHand}/gripPressed");
            if (conversationTarget)
            {
                talkCue = new GameObject("Talk cue");
                talkCue.transform.SetParent(conversationTarget.transform, false);
                talkCue.transform.localPosition = new Vector3(0f, .28f, 0f);
                talkCue.transform.localScale = Vector3.one * .045f;
                var cueText = talkCue.AddComponent<TextMeshPro>();
                cueText.text = "Grip near me to talk";
                cueText.fontSize = 2.2f;
                cueText.alignment = TextAlignmentOptions.Center;
                talkCue.AddComponent<BillboardLabel>();
                talkCue.SetActive(false);
            }
            ScheduleAmbientCall();
        }

        void OnEnable()
        {
            if (conversationTarget) conversationTarget.selectEntered.AddListener(OnSelected);
            leftTalkGrip?.Enable(); rightTalkGrip?.Enable();
            if (health)
            {
                health.onDamaged.AddListener(OnDamaged);
                health.StateChanged += OnHealthChanged;
            }
        }

        void OnDisable()
        {
            if (conversationTarget) conversationTarget.selectEntered.RemoveListener(OnSelected);
            leftTalkGrip?.Disable(); rightTalkGrip?.Disable();
            if (talkCue) talkCue.SetActive(false);
            if (health)
            {
                health.onDamaged.RemoveListener(OnDamaged);
                health.StateChanged -= OnHealthChanged;
            }
            EndConversation();
        }

        void OnDestroy() { leftTalkGrip?.Dispose(); rightTalkGrip?.Dispose(); }

        void OnSelected(SelectEnterEventArgs args)
        {
            TryBeginFromHand(args.interactorObject?.transform);
        }

        public bool TryBeginFromHand(Transform hand)
        {
            if (!hand) return false;
            var origin = hand.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>();
            var viewer = origin && origin.Camera ? origin.Camera.transform : Camera.main ? Camera.main.transform : null;
            return viewer && TryBegin(viewer);
        }

        public bool TryBegin(Transform viewer)
        {
            if (active || !viewer || !dialogue || !CanTalk() ||
                Vector3.Distance(viewer.position, transform.position) > maxConversationDistance) return false;
            StopVoice();
            ambientSpeaking = false;
            playerHead = viewer;
            active = true;
            wasHostile = brain && brain.hostileToPlayer;
            alternateGreeting = !alternateGreeting;
            var alternate = dialogue.Find("greeting_002");
            ShowNode(alternateGreeting && alternate != null ? alternate : dialogue.Find(dialogue.entryNodeId));
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
            if (!active)
            {
                if (!cachedViewer && Time.time >= nextViewerLookup)
                {
                    nextViewerLookup = Time.time + 2f;
                    var origin = FindFirstObjectByType<XROrigin>();
                    cachedViewer = origin && origin.Camera ? origin.Camera.transform :
                        Camera.main ? Camera.main.transform : null;
                }
                float distance = cachedViewer ? Vector3.Distance(cachedViewer.position, transform.position) : float.MaxValue;
                nearby = CanTalk() && distance <= Mathf.Min(2.5f, maxConversationDistance);
                playerHead = nearby ? cachedViewer : null;
                if (talkCue) talkCue.SetActive(nearby);
                if (nearby && choices && conversationTarget)
                {
                    var contact = conversationTarget.transform.position;
                    if (CanStartWithGrip(choices.leftHand, leftTalkGrip, contact) ||
                        CanStartWithGrip(choices.rightHand, rightTalkGrip, contact))
                    {
                        TryBegin(cachedViewer);
                        return;
                    }
                }
                if (nearby && !greetedPlayer)
                {
                    greetedPlayer = true;
                    GreetNearby();
                }
                if (distance > 3.5f) greetedPlayer = false;
                if (nearby)
                {
                    var toward = Vector3.ProjectOnPlane(cachedViewer.position - transform.position, Vector3.up);
                    if (toward.sqrMagnitude > .01f)
                        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                            Quaternion.LookRotation(toward), turnDegreesPerSecond * .45f * Time.deltaTime);
                }
                UpdateMouth();
                if (ambientSpeaking && VoiceFinished(ambientEnd))
                {
                    ambientSpeaking = false;
                    if (mouthAudio) mouthAudio.maxDistance = 10f;
                    if (subtitle) subtitle.gameObject.SetActive(false);
                    if (face) face.ResetExpression();
                }
                if (!ambientSpeaking && !nearby && CanTalk() && Time.time >= nextAmbientCall)
                    TryAmbientCall();
            }
            if (!active && navAgent && navAgent.enabled && navAgent.isOnNavMesh && wanderRadius > 0f && Time.time >= nextWander)
            {
                nextWander = Time.time + UnityEngine.Random.Range(wanderPauseSeconds.x, wanderPauseSeconds.y);
                Vector2 circle = UnityEngine.Random.insideUnitCircle * wanderRadius;
                if (NavMesh.SamplePosition(home + new Vector3(circle.x, 0f, circle.y), out var destination, 2f, NavMesh.AllAreas))
                    navAgent.SetDestination(destination.position);
            }
            if (!active) return;
            if (talkCue && talkCue.activeSelf) talkCue.SetActive(false);
            if (navAgent && navAgent.enabled && navAgent.isOnNavMesh) navAgent.ResetPath();
            if (!playerHead || !CanTalk() || brain && brain.hostileToPlayer != wasHostile ||
                Vector3.Distance(playerHead.position, transform.position) > maxConversationDistance ||
                IsPlayerIncapacitated())
            { EndConversation(); return; }

            var direction = Vector3.ProjectOnPlane(playerHead.position - transform.position, Vector3.up);
            if (direction.sqrMagnitude > .01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    Quaternion.LookRotation(direction), turnDegreesPerSecond * Time.deltaTime);

            UpdateMouth();

            if (barkSpeaking)
            {
                if (VoiceFinished(barkEnd))
                {
                    barkSpeaking = false;
                    if (subtitle) subtitle.gameObject.SetActive(false);
                    if (face) face.ResetExpression();
                    if (availableResponses.Count > 0) choices?.Show(availableResponses, SelectResponse);
                }
                return;
            }

            if (current != null && VoiceFinished(fallbackEnd))
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
            if (current == null && !barkSpeaking && !string.IsNullOrEmpty(pendingBark))
            {
                var eventId = pendingBark;
                pendingBark = null;
                choices?.Hide();
                if (face) face.SetExpression(ExpressionType.Neutral);
                barkEnd = Time.time + PlayVoiceEvent(eventId, null, null);
                barkSpeaking = true;
            }
            else if (current == null && !barkSpeaking && !string.IsNullOrEmpty(pendingContext))
            {
                string eventId = pendingContext;
                pendingContext = null;
                PlayContextLine(eventId);
            }
        }

        bool CanStartWithGrip(XRDirectInteractor hand, InputAction grip, Vector3 contact) =>
            hand && !hand.hasSelection && grip != null && grip.WasPressedThisFrame() &&
            (hand.transform.position - contact).sqrMagnitude <= closeGripDistance * closeGripDistance;

        void GreetNearby()
        {
            var pose = GetComponent<MerchantHandshakePose>();
            if (pose) pose.PlayGesture("wave");
        }

        void UpdateMouth()
        {
            float target = 0f;
            if (mouthAudio && mouthAudio.isPlaying && mouthAudio.clip &&
                speakingEnvelope is { Length: > 0 })
            {
                int index = Mathf.Min(speakingEnvelope.Length - 1,
                    Mathf.FloorToInt(mouthAudio.timeSamples / (float)mouthAudio.clip.frequency * 30f));
                target = speakingEnvelope[index];
            }
            mouthOpening = Mathf.MoveTowards(mouthOpening, target, Time.deltaTime * 5f);
            if (face) face.SetSpeechOpen(mouthOpening);
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
            if (face) face.SetExpression(node.expression,
                node.expression == ExpressionType.Happy ? .3f : .65f);
            var pose = GetComponent<MerchantHandshakePose>();
            if (pose) pose.PlayGesture(node.gestureId);
            fallbackEnd = Time.time + PlayVoiceEvent(node.id, node.audio, node.text,
                node.mouthEnvelope);
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

        public void QueueContextLine(string nodeId)
        {
            if (active && dialogue && dialogue.Find(nodeId) != null) pendingContext = nodeId;
        }

        public void WithdrawContextLine(string nodeId)
        {
            if (pendingContext == nodeId) pendingContext = null;
            if (active && current != null && current.id == nodeId)
            {
                StopVoice();
                ShowNode(dialogue.Find("trade_offer_001"));
            }
        }

        public void QueueBark(string eventId)
        {
            if (!active || !trading || !voiceBank || voiceBank.Find(eventId) == null) return;
            if (lastPlayedAt.TryGetValue(eventId, out float last) &&
                Time.time - last < voiceBank.Find(eventId).cooldownSeconds) return;
            if (pendingBark == eventId || barkSpeaking) return;
            pendingBark = eventId;
        }

        void ScheduleAmbientCall()
        {
            float low = Mathf.Max(10f, ambientCallIntervalSeconds.x);
            nextAmbientCall = Time.time + UnityEngine.Random.Range(low,
                Mathf.Max(low, ambientCallIntervalSeconds.y));
        }

        void TryAmbientCall()
        {
            ScheduleAmbientCall();
            if (!voiceBank || voiceBank.ambientEventIds.Count == 0 ||
                mouthAudio && mouthAudio.isPlaying) return;
            int count = voiceBank.ambientEventIds.Count;
            int index = NpcVoiceBank.SelectNextIndex(count, lastAmbientIndex,
                UnityEngine.Random.Range(0, count));
            lastAmbientIndex = index;
            string eventId = voiceBank.ambientEventIds[index];
            if (voiceBank.Find(eventId) == null) return;
            if (face) face.SetExpression(ExpressionType.Neutral);
            ambientEnd = Time.time + PlayVoiceEvent(eventId, null, null);
            if (mouthAudio) mouthAudio.maxDistance = 16f;
            ambientSpeaking = true;
            var pose = GetComponent<MerchantHandshakePose>();
            if (pose && index == 0) pose.PlayGesture("present");
        }

        bool VoiceFinished(float end) => (!mouthAudio || !mouthAudio.isPlaying) && Time.time >= end;

        float PlayVoiceEvent(string eventId, AudioClip fallbackClip, string fallbackText,
            float[] fallbackEnvelope = null)
        {
            AudioClip clip = fallbackClip;
            string spokenText = fallbackText;
            speakingEnvelope = fallbackEnvelope;
            var voiceEvent = voiceBank ? voiceBank.Find(eventId) : null;
            if (voiceEvent != null && voiceEvent.takes.Count > 0)
            {
                int count = voiceEvent.takes.Count;
                int last = lastTakeByEvent.TryGetValue(eventId, out int previous) ? previous : -1;
                int index = NpcVoiceBank.SelectNextIndex(count, last,
                    UnityEngine.Random.Range(0, count));
                lastTakeByEvent[eventId] = index;
                var take = voiceEvent.takes[index];
                if (take != null)
                {
                    clip = take.clip;
                    spokenText = string.IsNullOrEmpty(take.subtitle) ? fallbackText : take.subtitle;
                    speakingEnvelope = take.mouthEnvelope;
                }
                lastPlayedAt[eventId] = Time.time;
            }
            if (subtitle)
            {
                subtitle.text = string.IsNullOrWhiteSpace(spokenText) ? "" : displayName + ": " + spokenText;
                subtitle.gameObject.SetActive(!string.IsNullOrWhiteSpace(spokenText));
            }
            if (mouthAudio)
            {
                mouthAudio.Stop();
                mouthAudio.clip = clip;
                if (clip) mouthAudio.Play();
            }
            return clip ? clip.length + .15f :
                Mathf.Max(1.5f, (spokenText?.Length ?? 0) / 14f);
        }

        void StopVoice()
        {
            if (mouthAudio) { mouthAudio.Stop(); mouthAudio.maxDistance = 10f; }
            speakingEnvelope = null;
            if (face) face.SetSpeechOpen(0f);
            mouthOpening = 0f;
            if (subtitle) subtitle.gameObject.SetActive(false);
        }

        public void EndConversation()
        {
            if (!active && !ambientSpeaking) return;
            active = false;
            current = null;
            ambientSpeaking = barkSpeaking = false;
            pendingBark = null;
            pendingContext = null;
            StopVoice();
            if (face) face.ResetExpression();
            ScheduleAmbientCall();
            choices?.Hide();
            if (trading) { trading = false; TradingChanged?.Invoke(false); }
            ConversationEnded?.Invoke();
        }
    }
}
