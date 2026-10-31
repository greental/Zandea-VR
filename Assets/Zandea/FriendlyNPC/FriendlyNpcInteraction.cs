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
using Unity.Profiling;

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
        public Zandea.Missions.DaranContract contract;
        public AudioSource mouthAudio;
        public TMP_Text subtitle;
        public PalmDialogueChoices choices;
        [Min(1f)] public float maxConversationDistance = 9f;
        [Min(1f)] public float invitationDistance = 6f;
        [Min(.5f)] public float browseDistance = 3f;
        [Min(6f)] public float marketCallDistance = 16f;
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
        static readonly HashSet<FriendlyNpcInteraction> Available = new();
        static readonly ProfilerMarker WaveSelectionMarker = new("Zandea.Dialogue.SelectWaveTarget");
        static readonly ProfilerMarker BeginConversationMarker = new("Zandea.Dialogue.BeginConversation");
        static readonly ProfilerMarker VoicePlaybackMarker = new("Zandea.Dialogue.StartVoice");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetAvailable() => Available.Clear();

        public static bool TryBeginBestWave(Transform viewer)
        {
            using var sample = WaveSelectionMarker.Auto();
            if (!viewer) return false;
            FriendlyNpcInteraction best = null;
            float bestDot = .57f;
            float bestDistance = float.PositiveInfinity;
            foreach (var npc in Available)
            {
                if (!npc || !npc.isActiveAndEnabled || !npc.CanTalk()) continue;
                if (npc.active) return false; // One player cannot open another dialogue mid-conversation.
                Vector3 target = npc.head ? npc.head.position : npc.transform.position + Vector3.up * 1.5f;
                Vector3 toward = target - viewer.position;
                float distance = Vector3.ProjectOnPlane(npc.transform.position - viewer.position,
                    Vector3.up).magnitude;
                if (distance > npc.browseDistance || distance < .15f) continue;
                float dot = Vector3.Dot(viewer.forward, toward.normalized);
                if (dot > bestDot + .001f || Mathf.Abs(dot - bestDot) <= .001f && distance < bestDistance)
                { best = npc; bestDot = dot; bestDistance = distance; }
            }
            return best && best.TryBegin(viewer);
        }

        DialogueNode current;
        readonly System.Collections.Generic.List<DialogueResponse> availableResponses = new(4);
        float fallbackEnd;
        float mouthOpening;
        float[] speakingEnvelope;
        readonly Dictionary<string, int> lastTakeByEvent = new();
        readonly Dictionary<string, float> lastPlayedAt = new();
        float nextAmbientCall;
        float lastMarketCallAt = -100f;
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
        bool farZoneSeen;
        bool invitationSpeaking;
        bool theftHostilityPending;
        bool nearby;
        Transform cachedViewer;
        float nextViewerLookup;
        NavMeshAgent navAgent;
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
            // Conversation is started by the player's deliberate wave. The old
            // chest grab target must not steal item or handshake selection.
            if (conversationTarget && !conversationTarget.GetComponent<HandshakeTarget>())
                conversationTarget.enabled = false;
            navAgent = GetComponent<NavMeshAgent>();
            home = transform.position;
            var origin = FindFirstObjectByType<XROrigin>();
            cachedViewer = origin && origin.Camera ? origin.Camera.transform :
                Camera.main ? Camera.main.transform : null;
            ResolveChoices(cachedViewer);
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
            if (conversationTarget)
            {
                talkCue = new GameObject("Talk cue");
                talkCue.transform.SetParent(conversationTarget.transform, false);
                talkCue.transform.localPosition = new Vector3(0f, .28f, 0f);
                talkCue.transform.localScale = Vector3.one * .045f;
                var cueText = talkCue.AddComponent<TextMeshPro>();
                cueText.text = "Come have a look";
                cueText.fontSize = 2.2f;
                cueText.alignment = TextAlignmentOptions.Center;
                talkCue.AddComponent<BillboardLabel>();
                talkCue.SetActive(false);
            }
            ScheduleAmbientCall();
        }

        void OnEnable()
        {
            Available.Add(this);
            if (health)
            {
                health.onDamaged.AddListener(OnDamaged);
                health.StateChanged += OnHealthChanged;
            }
        }

        void OnDisable()
        {
            Available.Remove(this);
            if (talkCue) talkCue.SetActive(false);
            if (health)
            {
                health.onDamaged.RemoveListener(OnDamaged);
                health.StateChanged -= OnHealthChanged;
            }
            EndConversation();
        }

        public bool TryBegin(Transform viewer)
        {
            using var sample = BeginConversationMarker.Auto();
            if (active || !viewer || !dialogue || !CanTalk() ||
                PlanarDistance(viewer.position, transform.position) > browseDistance) return false;
            ResolveChoices(viewer);
            StopVoice();
            ambientSpeaking = invitationSpeaking = false;
            playerHead = viewer;
            active = true;
            wasHostile = brain && brain.hostileToPlayer;
            alternateGreeting = !alternateGreeting;
            var alternate = dialogue.Find("greeting_002");
            ShowNode(alternateGreeting && alternate != null ? alternate : dialogue.Find(dialogue.entryNodeId));
            return active;
        }

        // Releasing owned goods into a trade box is itself an intentional request
        // to trade, even when the player did not first open the notebook dialogue.
        public bool TryBeginTradeFromCounter()
        {
            if (!dialogue || !CanTalk()) return false;
            if (!active)
            {
                var viewer = playerHead ? playerHead : cachedViewer;
                float closest = viewer ? PlanarDistance(viewer.position, transform.position) : float.PositiveInfinity;
                // A streamed stall can appear before the XR Origin is registered.
                // Resolve a nearby active headset camera when the player actually
                // releases an item; this scan never runs in the per-frame idle path.
                if (closest > browseDistance)
                {
                    foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                    {
                        if (!camera || !camera.isActiveAndEnabled ||
                            !camera.CompareTag("MainCamera") && !camera.GetComponentInParent<XROrigin>()) continue;
                        float distance = PlanarDistance(camera.transform.position, transform.position);
                        if (distance >= closest) continue;
                        closest = distance;
                        viewer = camera.transform;
                    }
                }
                if (!viewer || closest > browseDistance)
                    return false;
                ResolveChoices(viewer);
                StopVoice();
                ambientSpeaking = invitationSpeaking = false;
                playerHead = viewer;
                active = true;
                wasHostile = brain && brain.hostileToPlayer;
            }
            if (trading) return true;
            var offer = dialogue.Find("trade_offer_001");
            if (offer != null) ShowNode(offer);
            if (!trading) Execute(DialogueAction.BeginTrading);
            return trading;
        }

        bool CanTalk() => (!health || health.State == NpcConsciousness.Conscious) &&
            (!brain || !brain.hostileToPlayer);

        // A scene can retain an older, inactive V2 player as a review copy.
        // Always drive the notebook belonging to the actual viewer, even if a
        // serialized reference still points at that inactive player.
        void ResolveChoices(Transform viewer)
        {
            var playerChoices = viewer ? viewer.root.GetComponentInChildren<PalmDialogueChoices>(true) : null;
            if (playerChoices && playerChoices.isActiveAndEnabled)
            {
                if (choices && choices != playerChoices) choices.Hide();
                choices = playerChoices;
            }
            else if (!choices || !choices.isActiveAndEnabled)
                choices = FindFirstObjectByType<PalmDialogueChoices>();
        }

        static float PlanarDistance(Vector3 a, Vector3 b) =>
            Vector3.ProjectOnPlane(a - b, Vector3.up).magnitude;

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
                float distance = cachedViewer ? PlanarDistance(cachedViewer.position, transform.position) : float.MaxValue;
                nearby = CanTalk() && distance <= invitationDistance;
                playerHead = nearby ? cachedViewer : null;
                if (talkCue && talkCue.activeSelf) talkCue.SetActive(false);
                if (distance > invitationDistance + 1f) greetedPlayer = false;
                bool inMarketRange = distance > invitationDistance && distance <= marketCallDistance;
                if (inMarketRange && !farZoneSeen && Time.time - lastMarketCallAt > 20f)
                    nextAmbientCall = Time.time;
                farZoneSeen = inMarketRange;
                if (nearby && distance > browseDistance && !greetedPlayer)
                {
                    greetedPlayer = true;
                    InviteNearby();
                }
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
                    invitationSpeaking = false;
                    if (mouthAudio) mouthAudio.maxDistance = 10f;
                    if (subtitle) subtitle.gameObject.SetActive(false);
                    if (face) face.ResetExpression();
                    if (theftHostilityPending)
                    {
                        theftHostilityPending = false;
                        if (brain && (!health || health.State == NpcConsciousness.Conscious))
                            brain.hostileToPlayer = true;
                    }
                }
                if (!ambientSpeaking && inMarketRange && CanTalk() && Time.time >= nextAmbientCall)
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
                PlanarDistance(playerHead.position, transform.position) > maxConversationDistance ||
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
                bool theft = eventId == "stock_stolen_001";
                bool warning = theft || eventId == "stock_warning_001";
                var barkNode = dialogue ? dialogue.Find(eventId) : null;
                if (face) face.SetExpression(warning ? ExpressionType.Angry : ExpressionType.Neutral);
                barkEnd = Time.time + PlayVoiceEvent(eventId, barkNode?.audio,
                    warning ? theft ? "That's mine. Bring it back." :
                        "Don't walk off with my stock, traveler." : barkNode?.text,
                    barkNode?.mouthEnvelope);
                barkSpeaking = true;
            }
            else if (current == null && !barkSpeaking && !string.IsNullOrEmpty(pendingContext))
            {
                string eventId = pendingContext;
                pendingContext = null;
                PlayContextLine(eventId);
            }
        }

        void InviteNearby()
        {
            var pose = GetComponent<MerchantHandshakePose>();
            if (pose) pose.PlayGesture("wave");
            alternateGreeting = !alternateGreeting;
            string eventId = alternateGreeting && dialogue.Find("greeting_002") != null ?
                "greeting_002" : dialogue.entryNodeId;
            var node = dialogue.Find(eventId);
            if (face) face.SetExpression(ExpressionType.Happy, .3f);
            ambientEnd = Time.time + PlayVoiceEvent(eventId, node?.audio, node?.text,
                node?.mouthEnvelope);
            ambientSpeaking = invitationSpeaking = true;
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
            if (node != null && contract)
                node = dialogue.Find(contract.ResolveNodeId(node.id)) ?? node;
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
            contract && contract.Evaluate(conditionId) ||
            conditionSource is IDialogueConditionSource source && source.Evaluate(conditionId);

        void SelectResponse(int index)
        {
            if (!active || current != null || !choices || !choices.TryGetResponse(index, out var response)) return;
            choices.Hide();
            Execute(response.action);
            if (response.action == DialogueAction.BrowseFreely)
            {
                availableResponses.Clear();
                return;
            }
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
                case DialogueAction.AcceptContract: contract?.Accept(); break;
                case DialogueAction.SettleContract: contract?.Settle(); break;
                case DialogueAction.AbandonContract: contract?.Abandon(); break;
                case DialogueAction.EndConversation: EndConversation(); break;
                case DialogueAction.BrowseFreely: break;
            }
        }

        public void PlayContextLine(string nodeId)
        {
            if (!active || !dialogue) return;
            var node = dialogue.Find(nodeId);
            if (node != null) ShowNode(node);
        }

        // Mission state is updated before this optional presentation. Use the
        // existing spatial voice, subtitles, face, and speech envelope.
        public bool TrySpeakWorldLine(string nodeId, Vector3 eventPosition, float hearingRange)
        {
            if (active || ambientSpeaking || !CanTalk() || !dialogue ||
                mouthAudio && mouthAudio.isPlaying ||
                PlanarDistance(transform.position, eventPosition) > hearingRange) return false;
            if (!cachedViewer)
            {
                var origin = FindFirstObjectByType<XROrigin>();
                cachedViewer = origin && origin.Camera ? origin.Camera.transform :
                    Camera.main ? Camera.main.transform : null;
            }
            if (!cachedViewer || PlanarDistance(transform.position, cachedViewer.position) > hearingRange)
                return false;
            var node = dialogue.Find(nodeId);
            if (node == null) return false;
            if (face) face.SetExpression(node.expression,
                node.expression == ExpressionType.Angry ? .45f : .3f);
            if (mouthAudio) mouthAudio.maxDistance = Mathf.Min(10f, hearingRange + 1f);
            ambientEnd = Time.time + PlayVoiceEvent(node.id, node.audio, node.text, node.mouthEnvelope);
            ambientSpeaking = true;
            return true;
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

        public void WarnAboutStock(bool stolen)
        {
            if (!CanTalk()) return;
            string eventId = stolen ? "stock_stolen_001" : "stock_warning_001";
            if (active) { pendingBark = eventId; return; }
            var node = dialogue ? dialogue.Find(eventId) : null;
            if (face) face.SetExpression(ExpressionType.Angry, .4f);
            var pose = GetComponent<MerchantHandshakePose>();
            if (pose) pose.PlayGesture("wave");
            ambientEnd = Time.time + PlayVoiceEvent(eventId, node?.audio,
                stolen ? "That's mine. Bring it back." :
                    "Don't walk off with my stock, traveler.", node?.mouthEnvelope);
            ambientSpeaking = true;
        }

        public void RespondToStockTheft(bool becomeHostile)
        {
            if (!CanTalk()) return;
            // Cancel the pending exchange first, then let the accusation finish
            // before handing control back to the existing combat AI.
            EndConversation();
            WarnAboutStock(true);
            theftHostilityPending = becomeHostile;
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
            lastMarketCallAt = Time.time;
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
            using var sample = VoicePlaybackMarker.Auto();
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
            invitationSpeaking = false;
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
