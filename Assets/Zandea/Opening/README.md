# Opening Slice 01 harness

Use **Zandea > Opening > Build and play test scene** in the Unity editor. This builds and saves `Assets/Scenes/Zandea/OpeningCaptureTest.unity` from the existing combat arena, then enters Play mode. **Build test scene** saves without playing. Rebuild to restore the authored setup; during Play mode, use the diagnostics panel's **Restart scene** button to reset runtime state.

The builder references the existing `VRPlayerMainCombat.prefab`, a distinct `OpeningStaffBandit_V2.prefab`, and arena navigation. `OpeningStaffBandit_V1.prefab` preserves the prior working staff bandit with a separate Unity GUID; V2 is the intended opening test version. The builder creates three staff bandits, their existing `BanditGroup`, and an observer. It does not edit Main or the source prefabs/scene. The bandits are currently ordinary hostile bandits: surrender, capture, restraint, and opening direction are explicitly marked *unimplemented* in diagnostics. `OpeningOutcome` supplies only the outcome vocabulary for later slices.

The player scene instance receives reusable `PlayerKnockout` and `KnockoutPresentation` components. The **Zandea > Opening > Debug** menu can request an indefinite KO and recovery in Play mode. These controls do not affect headset or controller tracking. KO is not connected to staff damage yet; nonlethal damage routing belongs to Slice 05.

Before headset work, verify that all three bandits hold Bo Staffs, the player has no practice sword, the NavMesh is valid, and restarting repeats the initial health and AI states. This harness is Editor/development diagnostics only; the overlay and logs are compiled out of release builds.

## Full opening authoring (Slices 05–13)

`Zandea > Opening > Install encounter in Main` is the one-time editor authoring command for the intended game scene. It preserves the existing Main player instance, adds three opening V2 slavers and the reusable player/encounter components, saves Main, and places Main first in build settings. It is idempotent when an opening root already exists. This command has not been run during the writing phase; run and inspect it in the deferred integration pass.

The `OpeningFlow` component bridges gesture, ultimatum, capture, binder, escort and director events. The director owns only awakening, escape/win observation, branch resolution, title policy and handoff. Set voice clips, a binding animation, rope material, an escort waypoint and `OpeningRoute` UnityEvents in the authored scene when assets and next chapter destinations are available. Missing voice and animation use development placeholders; no next chapter is invented.

The `OpeningTitlePresentation` draws a stationary camera-child black plane and TextMeshPro title for both eyes. `KnockoutPresentation` handles reusable KO blackout and audio. The encounter's staff attacks opt into `NonlethalAttackProfile`; other damage packets retain lethal intent. The player may still win or escape. `Zandea > Opening > Debug` contains KO/recover/replay commands for Editor use.

See repository-root `Opening_Validation_Report.md` for the complete pending test matrix. No Unity or headset tests have been run for the full opening under the owner's deferred testing instruction.
