# Generated melee weapons

Run **Tools > Melee Combat > Build missing generated weapons** after Unity imports the `.glb` files in `Assets/MyAssets/mesh/weapons/Generated`. The command creates one prefab per GLB in this folder and leaves existing prefabs alone, so manual tuning survives subsequent runs. **Rebuild generated weapons** overwrites that tuning.

Each prefab has a Rigidbody, a capsule collider, an XR grab interactable, and `PhysicalMeleeWeapon`. The builder puts the model's longest dimension along local Z and scales it to about one meter. Generated images do not identify the handle, blade, or orientation, so inspect each prefab and adjust the `Visual`, `Grip`, collider, mass, strike type, and striking tip markers. Remove or disable any imported colliders inside `Visual` if they interfere with the capsule.

For the player, place a prefab in a scene within reach of an XR direct interactor, or add it to a compatible inventory or holster. It deals damage to `IDamageable` targets when swung and can be dropped and picked up.

For an NPC, add `NpcGeneratedWeapon` to its root, assign the generated prefab and an animated hand or weapon goal transform as `gripTarget`. Assign `health` to drop the weapon on death. For the existing `StarvingBandit` controller, also assign `bandit`; its staff and weapon goal references will be filled at spawn. Adjust the controller's attack paths and reach for each weapon, since its current moves were authored for a staff.
