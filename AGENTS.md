# AGENTS.md

If you are an agent (Codex, Claude Code, Cursor, Grok, Copilot, Cline, …), **read this file first and follow it**.

你是 Agent 时：**先读本文件再动手**。

## What this repo is

Unofficial Virt-A-Mate controller: Python MCP server `vam-mcp` plus session plugin `VamMcpBridge`.

It **only** searches and loads files already on the user's disk (scenes, looks, clothing, poses, face morphs). It does **not** generate characters, clothes, or Hub downloads.

## VAM_ROOT (required)

`VAM_ROOT` is the folder that contains `VaM.exe`.

- If the user has not given this path, **ask for it**. Do not guess a drive letter or a default install folder.
- Confirm `VaM.exe` exists in that folder before copying files there.

## Install steps the agent should run

When the plugin is not available at
`VAM_ROOT\Custom\Scripts\VamMcp\Bridge\VamMcpBridge.cs`, or the user asks to
install:

1. Ask for `VAM_ROOT` if you do not have it.
2. Confirm `VAM_ROOT\VaM.exe` exists.
3. From this repo root, install the loose plugin:

   ```powershell
   .\scripts\install-dev.ps1 -VamRoot "VAM_ROOT"
   ```

   The script creates a junction when possible and otherwise copies the plugin
   files. The script must end up at:

   ```
   VAM_ROOT\Custom\Scripts\VamMcp\Bridge\VamMcpBridge.cs
   ```

4. Stop. Tell the user they must, in VAM:
   - User Preferences -> Security -> Enable Plugins
   - Session Plugins -> Add Plugin ->
     `Custom/Scripts/VamMcp/Bridge/VamMcpBridge.cs`
   - leave `enabled` on
   - Session Plugin Presets -> Change User Defaults -> Set Current As User Defaults
5. Do **not** click the VAM UI yourself. Do **not** kill `VaM.exe` unless the user asks.

`pack-var.ps1` is for maintainers creating GitHub release artifacts. Do not use
the packaged `.var` as the normal installation path; VaMX can report that the
script inside the package "does not exist". Use the loose path above.

Optional, if the MCP server is not installed yet (from repo `mcp\`):

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e .
```

Then point the current MCP client at that venv Python with env `VAM_ROOT` set to the path the user gave.

## Preconditions (before any scene work)

1. Virt-A-Mate / VaMX is running.
2. `VamMcpBridge` is loaded under **Session Plugins** (not Scene Plugins, not on a Person) and `enabled` is on.
3. MCP server `vam` is connected (`VAM_ROOT` = folder that contains `VaM.exe`).
4. Call `status` first. If the plugin is missing or the call times out: run the loose install steps above if needed, then tell the user to finish the Session Plugin clicks. Do not invent clicks in the VAM UI.

## Operating rules

- Prefer MCP tools on the `vam` server. Do not ask the user to click **On**, delete atoms, or browse presets by hand when a tool exists.
- Looks / poses / scenes must already exist. If `list_*` returns nothing, say so. Do not promise to create or download content.
- Workflow: `list_*` -> pick an exact `path` (or uid) -> `load_*`.
- After any scene, look, clothing, pose, or expression change, call `capture_view` and inspect `Saves/PluginData/vam-mcp/preview.png` (the tool returns `previewAbsolute`). The PNG is the whole VAM window, so the VAM UI and any open panel are in it. `load_scene` returns before its assets finish loading, so a preview taken right after one can show a loading screen rather than the scene — capture again before concluding anything.
- If a capture looks wrong (no person, black frame), call `debug_cameras` before theorising. Do not conclude content is missing from a preview alone.
- New original character: `list_characters` first and reuse one if it fits. Otherwise `load_look` the
  closest installed base, then `list_morphs` -> `set_morphs` and `list_geometry_options` ->
  `set_geometry_options`, checking `capture_view` between steps, and finish with
  `save_character(name, description)`. Report the saved name to the user; that name is how they ask
  for the character next time. Never guess a morph or hair name — `list_*` gives the real one.
- `load_character` restores in two passes on purpose. A preset's `geometry` starts the hair and
  clothing loading, so materials owned by those items do not exist yet during a single pass and get
  skipped: a character loaded into a freshly started VAM came back with default hair and eye colour.
  Do not "simplify" that second pass away.
- Makeup goes through the Chokaphi DecalMaker plugin: its `DecalHead` array is the face decal
  stack, and each entry is an array holding one layer object (`tp` texture, `sv` alpha, `col`
  "r,g,b", `tran`, `scale`). It is **additive** - applying a stack appends to whatever is already
  there. Call `Clear All Frames` through `call_action` first, or layers pile up and the faint
  alpha halo every eyeshadow texture carries builds into a visible rectangle on the cheek.
  `load_character` does this reset for you.
- A button on a storable is a `JSONStorableAction`; `RestoreFromJSON` never touches one, so a vap
  cannot press it. Use `list_actions` to find it and `call_action` to press it.
- `load_pose` applies pose storables only. Presets filed as poses often are not pose-only -
  vamX's `_POSE LIBRARY` entries carry a `geometry` storable, and applying one wholesale
  replaced a built character's face, hair and clothing. What gets dropped is reported;
  `include_appearance=true` opts back in when the preset's look is genuinely wanted.
- `load_look` takes the preset's pose too, because most third-party looks ship one: 56 of the 78
  installed here carry skeleton controllers. Pass `keep_pose=true` when the character is already
  posed and only the outfit or makeup should change. Do not extend that filter to every id
  ending in "Control" - `BreastControl`, `GluteControl`, `EyelidControl`, `JawControl` and the
  finger controls are appearance, and stripping them loses real look data.
- A clothing item can hold several materials (`NoOC:SailorLingerieMaterialTop` / `...Skirt` /
  `...Socks`). To keep part of an outfit and drop the rest, set `hideMaterial` on the material
  through a vap rather than hunting for a different item.
- Sheerness is a material setting, not a texture: `Alpha Adjust` above zero plus a low
  `Specular Intensity` is what makes legwear read as nylon. Measuring a jpg diffuse says
  nothing about it - jpg has no alpha channel.
- Ethnicity is not a morph. There is no general "Asian" slider; start from an installed look of the
  right ethnicity. Say so rather than promising to shape a face from scratch.
- To set something no tool covers (hair colour, materials), write a `.vap` with
  `setUnlistedParamsToDefault: false` listing just that storable and load it with `load_look`. Get the
  parameter names out of presets already in `AddonPackages`; do not invent them.
- Face only: `set_expression` (alias or a morph name from `list_expressions`). Neutral aliases include `smile`, `neutral`, `surprise`, `sad`, `angry`.
- Head tracking the monitor camera: `lock_head`.
- Someone standing in the wrong place or facing the wrong way: call `get_position` first, then
  `move_person`. `x`/`y`/`z` are absolute world coordinates, `dx`/`dy`/`dz` are offsets, and
  `rx`/`ry`/`rz` are degrees. Do not guess coordinates you have not read.
- Two people into the current room with a paired pose: `setup_couple(female, male, pose)`. `female` / `male` are look names or exact `.vap` paths. `pose` is whatever the user asked for (or a name from `list_poses`). If the paired pose package is missing, fall back to `list_poses` + `load_pose` per person.
- Hidden people: `set_person_on`. Extra person: `add_person`, then `load_look` / `load_pose` on the returned uid. Remove: `remove_person`.
- `load_look` rejects `.json` scene files — those go to `load_scene` (use `merge=true` to add into the current scene).
- `person=""` = first Person. `person="all"` on `load_pose` applies the pose to everyone.
- After the user adds new `.var` / look files, the catalog is stale until the MCP process restarts. Say that; do not claim the new files are visible.

## Tools

| Tool | Use |
| --- | --- |
| `status` | Bridge alive? Call first. |
| `list_scenes` / `load_scene` | Search / load a scene |
| `list_persons` | Person atoms in the current scene |
| `add_person` / `remove_person` / `set_person_on` | Add, delete, show/hide |
| `capture_view` | Screenshot to `preview.png` |
| `list_morphs` / `set_morphs` | Find and set any morph |
| `list_geometry_options` / `set_geometry_options` | Hair / clothing toggles |
| `save_character` / `list_characters` / `load_character` | Local character library |
| `debug_cameras` | Diagnostic when a capture looks wrong |
| `list_looks` / `load_look` | Appearance `.vap` |
| `list_clothing` / `load_clothing` | Clothing `.vap` |
| `list_poses` / `load_pose` | Pose `.vap` |
| `list_expressions` / `set_expression` | Face aliases and live morphs |
| `lock_head` | Stop the head following the camera |
| `get_position` / `move_person` | Read or set a Person's world position and rotation |
| `setup_couple` | Two looks + paired pose in the current scene |

## If MCP is not connected

Install the Python package as above and attach `vam-mcp` to this agent. `VAM_ROOT` must be the folder the user named (the one with `VaM.exe`). Do not drive VAM by editing Unity files or sending raw JSON unless they explicitly ask you to debug the file bridge.

## Safety

Treat this as full control of the running VAM session. Only load paths the user asked for. Do not expose `Saves/PluginData/vam-mcp`.
