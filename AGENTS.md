# Disk-space rule for this Unity project

# Scene and editor menu organization

- Create and save project-authored Unity scenes under `Assets/Scenes/`. Use subfolders there when helpful. Keep imported third-party sample scenes in their packages.
- Put project-authored Unity editor menu commands under the top-level `Zandea/` menu, with feature submenus as needed. Do not add new commands under `Tools/`.

- Check free space on D: before and after asset imports, terrain bakes, Unity batch tests, Blender exports, or large validation captures. Do not start a disk-heavy run below 20 GiB free; report the shortage first.
- Keep one copy of generated intermediate assets. Put disposable logs and exports in a named temporary directory, record its path, and clean that directory when the task finishes if deletion is permitted.
- Treat `Assets/`, `ProjectSettings/`, `Packages/`, authored TileSets (including `Assets/PlanetPopulation/Generated/Planet_42_0336eccd/`), saved scenes, source downloads, and `TestCaptures/` as user data. Never clear them as cache.
- Unity `Library/`, project `Temp/`, and download caches can be regenerated, but first check whether Unity is running, inspect the exact target and size, and verify the resolved absolute path. Never delete a live project's `Library/` or `Temp/`.
- If a cleanup command is blocked by policy, stop. Do not retry through another shell or tool. Give the user the exact cache path and size for manual cleanup.
- A low-space cleanup should start with owned temporary output and known download caches. Do not empty the Recycle Bin or remove other applications' data without the user's explicit direction.

# Scene promotion and prefab versions

- When work is authored or tested in a feature scene, finish and validate it there, then ask the project owner whether to promote that specific setup to `Assets/Main.unity`. Do not silently assume a feature-scene change reaches Main. An explicit request to integrate it into Main is approval for that integration.
- Before changing a shared Player, bandit, NPC, or other feature prefab, preserve the previous working version as a separate `V1` prefab with its own Unity `.meta` GUID. Make changes in a separate `V2` prefab and point only the intended test scene or integration at V2. Do not overwrite or repurpose V1 while V2 is being evaluated.
- Promote V2 into the established main prefab or replace V1 only after the project owner says they are happy with it and explicitly asks to merge. Preserve references and verify the target scene after promotion.
