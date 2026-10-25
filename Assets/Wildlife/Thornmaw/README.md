# Thornmaw arachnid

The rigged model is `Art/Thornmaw_Rigged.fbx`. It contains a Generic eight-leg skeleton and looping `Thornmaw_Walk` (1 second) and `Thornmaw_Run` (0.6 second) clips. The source Blender file is `Art/Thornmaw_Rig.blend`; the repeatable rigging script is under `Tools/Thornmaw/`.

`Thornmaw.prefab` is built by `Zandea/Thornmaw/Build Rigged Prefab` after Unity imports the FBX. The `Thornmaw.controller` plays Walk by default and `ThornmawHunter` sets `IsRunning` during pursuit. The hunter roams on ground colliders without a NavMesh, looks for visible `IDamageable` targets, ignores all other `ThornmawHunter` instances, and applies a timed melee bite. Walk and run are the only authored clips; the bite uses a timed hit rather than a separate bite clip.

`ThornmawWorldSpawn` installs itself in the `Main` scene and reads `Resources/Wildlife/ThornmawSpawnSettings.asset` to spawn up to two hunters on live desert terrain. `Zandea/Thornmaw/Update Hunter Behavior` upgrades the prefab and creates the settings asset if they are missing. Patrol, sight, speed, health, and bite values are editable on the prefab.
