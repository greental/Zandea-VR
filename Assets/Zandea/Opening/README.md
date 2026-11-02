# Opening Slice 01 harness

Use **Zandea > Opening > Build and play test scene** in the Unity editor. This builds and saves `Assets/Scenes/Zandea/OpeningCaptureTest.unity` from the existing combat arena, then enters Play mode. **Build test scene** saves without playing. Rebuild to restore the authored setup; during Play mode, use the diagnostics panel's **Restart scene** button to reset runtime state.

The builder references the existing `VRPlayerMainCombat.prefab`, `StarvingBandit_BoStaff_Unified.prefab`, and arena navigation. It creates three staff bandits, their existing `BanditGroup`, and an observer. It does not edit Main or the source prefabs/scene. The bandits are currently ordinary hostile bandits: surrender, capture, restraint, knockout, and opening direction are explicitly marked *unimplemented* in diagnostics. `OpeningOutcome` supplies only the outcome vocabulary for later slices.

Before headset work, verify that all three bandits hold Bo Staffs, the player has no practice sword, the NavMesh is valid, and restarting repeats the initial health and AI states. This harness is Editor/development diagnostics only; the overlay and logs are compiled out of release builds.
