# NPC inventory slice

`NpcItemDefinition` is immutable catalog data. `NpcItemInstance` has a GUID, condition, equipment slot, owner, and optional one-level contents. `NpcInventory` owns runtime instances and exposes Add, Remove, Equip, Unequip, PutInside, TransferTo, Drop, and TakeWorld. Transfers keep the same instance and raise `Changed`. A dropped item gets `NpcWorldItem` on its existing physical GameObject.

The configured prefab is `MocapTest/GeneratedVariants/StarvingBandit_BoStaff_Unified.prefab`. Its `NpcGeneratedWeapon` registers the instantiated Bo Staff as the main-weapon item. Disarm, knockout, and death call the existing physical `Drop`, then move that item to world ownership. Reacquiring the staff takes the item from its world marker. No weapon is generated on wake-up.

`Starving Bandit Loot.asset` guarantees a Rag Shirt and Rag Pants, then rolls 0–15 individual cats in the torso pocket, occasional scrap in the leg pocket, and uncommon food or bandage. The visual references currently use the existing scarf wrap torso mesh and ragged skirt leg mesh; these are stand-ins for dedicated rag shirt and pants art. Clothing stays attached to the existing character rig. No physical clothing removal is implemented.

`seed = 0` on a new prefab instance creates and stores a runtime seed in `NpcInventory`; set a nonzero seed in the Inspector for reproducible loot. Generation happens once in `Awake`. The generated list persists across knockout, recovery, and component re-enable. The Inspector's **Log inventory** context action, or `logChanges`, shows instances in development builds.

The item and pocket state is serialized in the component for future save/load work, but scene runtime state is not yet saved to disk. Generic consumables and currency currently have no physical world prefabs, so `Drop` rejects them until one is configured. The scope does not include player inventory UI or physical clothing interactions.

Validation performed: static prefab and asset GUID checks, six configured loot entries, and `git diff --check`. The running Unity editor had not refreshed these changes during implementation, so the requested Play Mode spawn, knockout, disarm, and wake-up sequence remains to be exercised in the editor.
