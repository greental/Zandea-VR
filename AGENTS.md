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
