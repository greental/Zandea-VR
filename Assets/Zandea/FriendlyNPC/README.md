# Friendly merchant integration

`DaranMerchantBuilder` is the authoring entry point. In Unity, choose **Zandea > Friendly NPC > Build Daran test scene**. It creates a Daran prefab, a reusable counter prefab, a personal purse, a payout pouch, dialogue and vendor assets, and `DaranMerchantTest.unity` under `Generated/`. Stock includes an existing physical sword and bo staff, plus an existing bandage prefab. The build checks D: free space first and stops below 20 GiB.

The merchant uses `CombinedStarvingBandit`'s CC_Base skeleton, `BanditRigMotion`, health and facial rig. Its Animator has no Avatar and is disabled by the existing rig driver. `StarvingBandit.hostileToPlayer` is false for Daran and stays true for existing bandits. Damage makes Daran hostile and ends dialogue. `MerchantHandshakePose` performs a small arm reach after `BanditRigMotion`; it only runs while an offer is ready. `NPCFacialExpressionController` remains the sole owner of the expression blendshapes and jaw. Dialogue adds a bounded speech opening to that controller's existing jaw result.

## Author dialogue and voices

Each `DialogueDefinition` contains stable node IDs, NPC ID, text, emotion, response links, actions, optional clips and baked mouth envelopes. `VoiceProfile` chooses the provider and voice. Nodes can override the default profile. Use `DialogueAction.QuestHook` for a future contract handler. To gate a node or response, assign a condition ID and a component implementing `IDialogueConditionSource`; a missing resolver rejects gated content.

To add an NPC, create a voice profile and dialogue asset from **Assets > Create > Zandea > Friendly NPC**. Attach `FriendlyNpcInteraction` to a compatible existing humanoid, assign the dialogue, facial controller, head AudioSource, conversation contact and palm choices, then assign any vendor counter. Avoid adding another facial controller.

Open **Zandea > Friendly NPC > Generate dialogue voice**. Select the dialogue and choose one line or the whole NPC. Generated files are placed at `Assets/Audio/Dialogue/<npc ID>/<node ID>_<hash>.wav` for Kokoro or `.mp3` for ElevenLabs. The hash includes text, provider, voice and speech rate. Existing files remain on disk when a line changes; the asset points to the new file after successful generation. An existing clip can also be dragged onto a node manually. Clip import bakes a 30 Hz amplitude envelope for the jaw, then sets compressed playback. Runtime never calls a TTS provider.

Kokoro needs `kokoro` in a local Python environment plus its model download. Set the window's Python executable to that environment. The exporter is `Tools/Voice/kokoro_generate.py`. The default `am_michael` voice is listed in the upstream voice catalog; listen to a sample before final voice approval. The [Kokoro model card](https://huggingface.co/hexgrad/Kokoro-82M) states Apache 2.0 for its weights. Check the selected voice and dependencies before distribution, and retain any required notices. No model weights are bundled here.

ElevenLabs uses `ELEVENLABS_API_KEY` from the developer's environment. The key is never put in Unity assets or builds. Set `providerId` to `elevenlabs` and `providerVoiceId` to the chosen voice ID. Confirm the account's current commercial terms and voice rights before using generated clips in a release.

## Physical interaction

The player grips the merchant's conversation contact to begin. A raised free controller hand shows up to four responses by the palm. The opposite controller's thumbstick cycles choices and its primary button confirms; the choice colliders also accept direct interaction. Lowering the palm hides the panel. Input reservation suppresses the choosing hand's grab and martial stance, and pauses movement/turn providers while the panel is visible. Previous input states are restored when it hides. Controller axis and palm orientation require headset tuning.

Merchant stock carries `TradeItem` with an existing `NpcItemDefinition`, instance ID and legal owner ID. Only release into the Buy or Sell area registers an offer. Grabbing removes it. The counter verifies identity, owner, position and current quote again at handshake. Item price labels show a red minus for purchases and a green plus for sales. `NetCats = sale proceeds − purchase costs`.

`BackpackUtilityAttachments` adds `CoinPurseAnchor` and a reserved `MapAnchor` outside the storage volume. The purse is one grabbable `CurrencyPouch` with one balance. The payment spot accepts that personal purse but never prices it as merchandise. A completed debit waits until it is released, then remounts it. A merchant payout creates a separate pouch on the counter; hold the personal purse near that pouch and activate it to merge exactly its value. The payout is emptied and destroyed after merging.

The handshake contact is enabled only for a valid offer. Deliberate Grip on that contact calls `VendorCounter.TryCommit`. The commit rechecks every offered object, final prices, purse position, and both balances. It creates a payout pouch before any mutation, changes goods and net currency once, and rolls back on an exception. Sold goods become merchant owned and their grab component is disabled. Bought goods remain on the counter for pickup.

## Current validation and limits

Use `DaranMerchantBuilder.BuildAndValidate` from a Unity batch Editor run to build the scene and check its Edit Mode wiring. This does not verify actual XRI selection, runtime settlement, headset reach, readable text, arm pose or frame time. Those require Play Mode and Quest 2 via PCVR.

Vendor balances, transferred source IDs and outstanding payout pouches are retained across POI unload/reload within one play session. A disk save adapter is still required for persistence across application restarts. Actual generated speech is also required before the spoken dialogue acceptance test passes. Do not mark the merchant feature complete until those checks pass.
