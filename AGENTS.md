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
- `list_characters` returns every stored character **with its whole description**, and those
  descriptions are long on purpose. One unfiltered call here cost roughly 6k tokens, and every
  `load_character` echoes its character's description back again. Pass a `query` when you know
  what you are looking for, and do not call it twice in one task.
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
  cannot press it. Use `list_actions` to find it and `call_action` to press it. The one that
  bites: a garment's cloth sim collapses after a load or a root move - a dress skirt lost its
  flare and the back view showed bare skin where the skirt should be - and `<item>Sim` carries a
  `Reset` action that restores it. Because that is an action, **no preset can carry it**: a
  character whose outfit needs one needs the `call_action` after every `load_character`.
- `load_pose` applies pose storables only. Presets filed as poses often are not pose-only -
  vamX's `_POSE LIBRARY` entries carry a `geometry` storable, and applying one wholesale
  replaced a built character's face, hair and clothing. What gets dropped is reported;
  `include_appearance=true` opts back in when the preset's look is genuinely wanted.
- `load_look` takes the preset's pose too, because most third-party looks ship one: 56 of the 78
  installed here carry skeleton controllers. Pass `keep_pose=true` when the character is already
  posed and only the outfit or makeup should change. Do not extend that filter to every id
  ending in "Control" - `BreastControl`, `GluteControl`, `EyelidControl`, `JawControl` and the
  finger controls are appearance, and stripping them loses real look data.
- `load_clothing` means "wear this outfit", not "add this garment": it replaces the whole
  wardrobe. Cosmetic layers are clothing items too, so a face built from `paledriver` / `CMA` eye
  shadows, `EyeGlitter` and a `BooMoon` lips layer comes back bare-faced the moment any dress is
  loaded - verified on `Chen Xiaoman`, whose `activeInPrefix` dropped from 16 items to the dress
  alone. Keep the list of those layers and switch them back on after every outfit change, and do
  **not** tidy up with `clear_prefix="clothing:"`, which takes the makeup with it.
- A look or character preset can carry the atom's **root position and rotation**. R3D's
  `Asian_Girl` stores `control: {position: {x 0.839, y 0.040, z 0.476}, rotation.y 332}`, so
  loading it teleports the character to wherever the creator saved it - read back live, the root
  returned exactly those numbers, which looks precisely like a physics drift and is not one. That
  spot was 0.19 m from the monitor camera, inside the near clip, so the atom rendered invisible
  and every capture came back an empty black frame. `move_person` after the load puts it right.
  When a loaded character does not appear at all, read `get_position` for the preset's own
  coordinates before blaming the camera or the renderer.
- To choose hair, clothing or shoes, read the preview image the creator shipped: a `.vam` almost
  always has a `.jpg` of the same name beside it inside the package. Extract those and compare
  them. Do not put candidates on the character and render them to compare - it took two failed
  attempts to pick one fringe out of five that way, while `VaMChan/Bangs/bangs 0N.jpg` showed all
  five at once, shot straight on by the creator. Names do not tell you the shape.
- `list_geometry_options` is capped by the plugin, so a wide listing drops rows. Pass a `query` -
  it filters the full list before the cap - and check `truncated` in the result. Listing everything
  and searching the reply once made a freshly installed item look absent when it was there all
  along, 400 rows into a list of 526.
- When toggling hair or clothing off, use the `hair:`/`clothing:`-prefixed name from
  `list_geometry_options`. `geometry.hair` reports a raw item path, and passing that lands the
  toggle in the result's `failed` array, now with a `didYouMean` naming the real option. Confirm
  the change against `activeInPrefix` afterwards rather than trusting the call: five fringes
  silently stacked up because nothing checked, and before plugin 0.10.3 an unknown name was
  reported as *applied* because VAM's setter no-ops without raising.
- A material param that reads back absent is ambiguous: `GetJSON()` only serialises values that
  differ from the item's defaults, so "no value" means either not-applied or applied-and-default.
  A red outsole looked like a failed load for exactly that reason. Settle it by comparing the
  creator's other presets, or by writing an obviously wrong colour and checking that one appears.
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
- `move_person`'s rotation axes are world axes and they are **not** interchangeable. `ry` turns the
  character on the spot — that is the one for turning them to face somewhere else, and `ry` =
  0/90/180/270 gives a front/right/back/left turnaround. `rx` tips them forward or back. `rz`
  **rolls** them sideways, and since the root sits at floor level (`y=0`) a roll of 90 lays a
  standing character flat on the ground. That is geometry, not a physics fault: the root write is a
  rigid transform, and `rz` back to 0 stands the character up again. Checked both ways on a Person
  carrying MacGruber's `Life` with all four of its modules enabled, and on one with no plugins at
  all — the roll came back cleanly in both. Turn a character with `ry`; reach for `rx`/`rz` only
  when a tip or a roll is actually what is wanted.
- MacGruber's `Life` is a `.cslist` of six scripts and lands on the atom as four toggleable
  storables — `plugin#<n>_MacGruber.Breathing`, `.DriverBreathing`, `.Gaze` and
  `.AudioAttenuation` — each carrying an `enabled` bool. `DriverBreathing` drives `chestControl`'s
  joint rotation drive and `Gaze` writes `head.transform` every frame, so both touch the body
  rather than only morphs. `set_bool_param(storable, "enabled", false)` switches one off, and
  `load_character` restores whatever the preset saved: Ayaka Male has all four off on purpose, so
  nothing rewrites the face. A fresh `add_plugin` leaves them on.
- Two people into the current room with a paired pose: `setup_couple(female, male, pose)`. `female` / `male` are look names or exact `.vap` paths. `pose` is whatever the user asked for (or a name from `list_poses`). If the paired pose package is missing, fall back to `list_poses` + `load_pose` per person.
- Hidden people: `set_person_on`. Extra person: `add_person`, then `load_look` / `load_pose` on the returned uid. Remove: `remove_person`.
- `load_look` rejects `.json` scene files — those go to `load_scene` (use `merge=true` to add into the current scene).
- A merged scene lives in memory only. `load_scene(merge=true)` is how a background gets changed: install nothing, merge an environment, and the light and the backdrop both change, because in VAM the skybox **is** the image-based lighting, not a layer behind the subject. There is no path colour to set - `VamXFan.Neutral-Environment-Color.1:/Saves/scene/Neutral-Environment-MERGE.json` is the installed grey studio, and its grey comes from three things at once (a `SkyGray` skybox on `CoreControl`'s `GlobalLighting`, an `InvisibleLight`, and a `ColorScale` plugin on the environment asset). `save_scene` afterwards, or the whole thing is gone on the next restart. The MCP cannot touch any of those settings directly: `set_bool_param` and `load_look` both go through `RequiredPerson`, and there is no string or float parameter setter, so `skyName` and `diffuseIntensity` are VAM-UI-only.
- `save_scene` overwrites without asking, on purpose: VAM's own save button runs through a path that raises a modal confirm when the file exists, and a headless caller has nobody to click it. It writes the scene JSON but not the sibling preview `.jpg`, so a scene first saved this way shows an empty thumbnail in VAM's browser until it is saved once from the UI.
- `person=""` = first Person. `person="all"` on `load_pose` applies the pose to everyone.
- After the user adds new `.var` / look files, the catalog is stale until the MCP process restarts. Say that; do not claim the new files are visible.

## Tools

| Tool | Use |
| --- | --- |
| `status` | Bridge alive? Call first. |
| `list_scenes` / `load_scene` / `save_scene` | Search / load / save a scene |
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
| `list_plugins` / `add_plugin` | What plugins a Person carries, and load a missing one |
| `list_actions` / `call_action` | Find and press a storable's buttons (DecalMaker's `Clear All Frames`) |
| `set_bool_param` | Set a bool through its real setter, e.g. `useFemaleMorphsOnMale` |
| `get_appearance` | Every appearance-like storable as raw JSON; also confirms a plugin compiled |
| `rescan_packages` | Make VAM re-index AddonPackages after a Hub download |
| `list_atoms` / `get_atom_params` / `set_atom_params` | Read and write any atom, lights included |
| `search_hub` / `download_resource` | Search VAM's built-in Hub browser and download through it |
| `download_status` / `wait_for_downloads` / `hub_info` | Download queue, and the Hub's real filter values |

- Skin poking through a garment is usually the garment's fit, not the body. Each worn item has a
  `<item>WrapControl` with `surfaceOffset` - how far the wrapped mesh sits off the skin. A shirt
  cut for ordinary shoulders clips on a character given `Shoulder Width=0.45`; raising
  surfaceOffset from the item's default (often ~0.0012) to ~0.008 fixes it and keeps the
  silhouette. Reach for this before narrowing a body you deliberately shaped.

- Not every fit fault is `surfaceOffset`. `GeeMan55 Dress M6` opened its waist seam and showed
  two patches of skin through it at the item's stock values, and raising surfaceOffset twentyfold
  did nothing at all; `wrapToSmoothedVerts=true` alone closed it. **Change one parameter at a
  time.** Setting that plus a thicker `additionalThicknessMultiplier` and more `smoothIterations`
  in one go did close the seam and simultaneously painted dark grey bands along every garment
  seam - and at the figure size it was first shot at, ~714 px tall, that read as fabric shading
  and went unnoticed until the same character was re-shot at 1122 px.
- A garment's own `ItemControl.disableAnatomy=true` does **not** stop the body poking through it.
  The lever that works is on the body: push the anatomy in with morphs (`Nipple Length`,
  `Nipples Depth`, `Nipples Size`, `Nipple Diameter`, `Areolae Perk`, `Areola Depth`) - the same
  recipe `Ayaka JK Femboy` uses to flatten its chest.
- A fit saved into a character preset silently reverts when that character is loaded.
  `<item>WrapControl` and `<item>ItemControl` belong to a garment that is still loading during the
  first pass, so it cannot see them, and a server old enough to lack the fit fix drops them from
  the second pass too. Verified: after `load_character("Chen Xiaoman")` both read back as `{}`
  while `EyelidControl` (not garment-owned) kept its value. Check them with `get_appearance`
  after loading a character that depends on a fit, and re-apply the fit `.vap` until the fixed
  server is running.

- A look is not just materials and morphs. `irises` / `sclera` carry the eye colour (the
  Enhanced Eyes clothing item's own iris layers do **not** render - painting all four bright
  green produces zero green pixels), and `EyelidControl.eyelidLookMorphsEnabled` decides whether
  a narrow eye shape survives: VAM's eyelids-follow-the-gaze system otherwise pins
  `Eyelids Top Up` near 0.475 and cancels every eye-shape morph. `save_character` keeps all of
  these from plugin 0.10.5 on; before that they were silently dropped, so a preset saved by an
  older build needs them pasted back in.
- `Eyes Height` does nothing (tested at -1.0, no change). The morph that narrows an eye is
  `Eyes Height Upper` (negative closes the top lid); `Eyes Height Bottom` **opens** it when
  negative, so use a positive value to raise the lower lid.
- Two packages can ship the same item filename - `Short Pixie.vam` and `Brows Evey.vam` each
  exist in both the original pack and `vamX.Base`, which re-bundles other creators' assets.
  Toggle using the full id from `activeInPrefix`, not the first match on the leaf name, or you
  will switch off the copy that was not worn - and switching one on while the other is already
  on stacks two copies of the mesh. From plugin 0.10.7 `set_geometry_options` reports this as
  `duplicates` plus an `ATTENTION` line; before that it was caught only by eye.

- A saved character can depend on a plugin, and **an appearance preset cannot load one**: the
  preset restores a plugin's stored values by storable id, but the load path skips
  `PluginManager` entirely. A character whose makeup lives in DecalMaker therefore loads
  bare-faced, with its `DecalHead` layers stored and nowhere to go. Call `list_plugins` after
  `load_character`, and `add_plugin("decalmaker")` when it is absent, then re-apply the makeup.
  A plugin compiles asynchronously, so `add_plugin` only reports `ready=true` once the storable
  appears; `ready=false` usually means VAM is showing a permission dialog that needs a click.

- **Never re-serialise a VAM scene file with a script.** Reading a `Saves/scene/*.json` into
  Python, editing it and dumping it back produced a file that still parsed as valid JSON and
  still loaded - but VAM silently dropped the front of the `atoms` array, so the room and its
  lights were gone and every capture came back a near-black frame with only the two people in
  it. Edit scene JSON as **plain text** instead: back the file up first, replace the exact
  strings you need with `re.subn`, assert every replacement count is what you expect, and leave
  the rest of the bytes alone. A structural change (reordering array entries) is not worth the
  re-dump - find a text-level edit that gets the same effect.
- **`save_scene` lands on disk asynchronously, up to ~45 s later.** Reading the file straight
  after the call shows the *old* mtime and size and looks exactly like a failed save; a check at
  20 s still showed the old file, and the write appeared at ~45 s. Poll the mtime/size until it
  changes before verifying, or the round is wasted and the wrong conclusion gets reported.
- **`save_scene` writes no preview `.jpg`**, so the scene shows as a blank tile in VAM's scene
  browser. A same-named 512x288 jpg beside the JSON gives it a thumbnail (crop the 3D area out
  of `preview.png`), or save once from VAM's own UI, which writes its own preview and its own
  file format.
- **vamX picks its male and female by hardcoded atom uids**: `com.DEFAULT_FEMALE = "Person"`,
  `com.DEFAULT_MALE = "Person#2"` (and `Person3some` for a third). `JSONMaleAtomUID` /
  `JSONFemaleAtomUID` in its `vamX.BL_GUI` storable are an **output** - `SetVal(com.maleAtom.uid)`
  rewrites them from its internal state when a scene loads - so editing them in a saved scene
  does nothing at all. The lever is which atom carries which uid. Two mechanisms could produce
  the pairing and the evidence seen so far does not separate them - VAM may honour the `id`
  written in the scene file, or vamX may rename the atoms to its default uids after the load
  from whatever assignment it made. Both routes agree on the practical rule, so do not claim
  which one it is. Verified fix for a reversed couple: swap the two Person atoms' `"id"`
  strings as text (`"id" : "Person"` <-> `"id" : "Person#2"`) plus any `parentAtom` that
  pointed at one of them, then reload. Confirm
  with `list_plugins`: the atom named `Person` must carry the female character's plugin and
  `Person#2` the male's. vamX renames atoms to those uids itself after a load, so re-check the
  pairing after every scene load instead of assuming it survived.

- `Stopper.AlternativeFuta` gives a **female** atom male genitalia by grafting a mesh into the
  skin's second graft slot. Its own thread requires `useMaleMorphsOnFemale = true` (or the penis
  morphs will not save) and `Auto Leg Bend Fix Morphs` **off**; both are plain bools on the
  person's `geometry` storable, both are reachable through `set_bool_param`, and that tool reads
  the value back - so a wrong parameter name is visible rather than silent. The plugin rewrites
  the skin, so any skin or texture change needs a disable/enable cycle on its `enabled` bool to
  rebuild the graft. It is a normal atom plugin: a scene carries it, an appearance preset cannot,
  so `list_plugins` after every `load_character` and `add_plugin` when it is absent.
- **An AltFuta texture pack only works on the body it was cut for.** The `WeebU.AltFuta-<name>`
  packs ship one torso and one genitals texture per source character. The wrong character's torso
  texture paints white blotches across the body, and mixing packs between slots paints black
  wedges at the graft. The pack's own instructions are: **the torso and genitals slots both take
  the pack's `torso` texture**, and the `genitals*` files go into the plugin's penis/pelvis
  material options. Ranking packs by average skin tone is not enough - only a pack whose UV
  layout matches the body renders correctly, which here meant the pack derived from the same
  character as the face and limbs.

- **A pose preset carries the atom's root position and rotation.** `Preset_Kneeling 01` moved Mike
  to `(0.156, 0.599, -0.030)` with `rx 40.7`, and a vamX sitting preset teleported Ayaka and
  changed his `ry` from 146 to 334. `load_pose` therefore both poses the body **and** re-places the
  character, and it only writes the controllers it carries: a kneeling pose applied to a root still
  tipped over from a lying pose leaves the body kneeling with the whole atom at 40 degrees. Read
  `get_position` after every `load_pose`, then re-apply the placement with `move_person` - `rx` and
  `rz` back to 0 stands it upright - and leave that call until last.

- **Pose packs ship the creator's own preview JPGs.** `vamX.1.52.var` holds
  `.../_POSE LIBRARY/Sitting - klphgz+bill_prime/Preset_001.vap` with a `Preset_001.jpg` beside it.
  Extracting 36 of those and tiling them into one contact sheet picked the only cross-legged pose
  out of the pack on the first try, where the 25 identically-named `Preset_0NN` entries give a name
  nothing to choose on. Read the thumbnails before putting candidates on a character - the same
  rule already written for hair and clothing.

- **A pose carries the male genital controllers, and an over-extended penis is the symptom.**
  `penisBaseControl`, `penisMidControl` and `penisTipControl` are ordinary free controllers, so a
  pose that points the tip away from the base stretches the mesh between them. Each carries `Reset`
  and `RestoreAllFromDefaults` in `list_actions`; pressing `Reset` on all three restores the
  proportions at once, and `SaveToStore1/2/3` stores a known-good state to return to.

- **`Alpha Adjust` on a material: positive is sheerer, negative is more opaque.** The note above
  ("`Alpha Adjust` above zero plus a low `Specular Intensity` is what makes legwear read as nylon")
  is the sheer direction. The cosmetic-layer eye shadows (`paledriver:Eyes upper shadow
  MaterialCombined` and its siblings) ship with a very low alpha, are invisible at `0`, and read as
  real makeup at `-0.7`. Setting it back to `0` while chasing a colour silently removed them.

- **A plugin that binds a material at init only renders after a VAM restart.** The DecalMaker binds
  each frame's renderer once, at plugin init (`dMFrames.DMRender.Init(material, ...)` in
  `DMFramesManager.cs`), so adding it mid-session, toggling its `enabled`, or reloading the scene
  leaves it holding a stale material: the `DecalHead` stack reads back complete and correct (format
  verified against the plugin's own `V3toV4` converter, textures verified present in the package)
  and nothing renders. **Save the scene, restart VAM, load the scene** - the plugin then initialises
  in the right order and the makeup and the AltFuta graft are both there. A mid-session
  disable/enable cycle rebuilds the graft's data but not the decal renderer's binding, so treat the
  cycle above as necessary but not sufficient.

- **Face morphs are driven by other systems, and three of them overwrite what you set.** `LipSync`
  (a plain bool on the Person) drives `Mouth Open`, `Mouth Open Wide` and the tongue from audio, so
  a value written with `set_morphs` is back to its driven value within seconds -
  `set_bool_param(storable="LipSync", param="enabled", value=false)` stops it. vamX's action system
  drives the jaw the same way while an action runs, so stop the action too. `Eyes.lookMode` at
  `Target` pins the gaze to a target object and cancels every eye morph: `asco - Look Up` and the
  `NN-Eyes Rolling` morphs sit at 1 and change nothing until `lookMode` is `None`, which frees the
  eyeballs for a roll. **`lock_head(locked=false)` re-enables the camera gaze and clears `lookMode`
  back to its default, so it undoes that fix**, and a `lock_head(true)` afterwards freezes the eyes
  wherever the glance had just pulled them - on a lying character that is the lower edge of the
  socket, which reads as "the irises are gone". Settle a gaze in this order instead: aim the
  monitor camera where the character should look, `lock_head(false)`, wait for the head and eyes to
  arrive, then `lock_head(true)`. For eyes that simply face forward, set `lookMode` **last** and do
  not touch `lock_head` afterwards. Both orders were got wrong twice in one session.

- **A neutral eye is `lEye` and `rEye` at rotation 0 plus `asco - Look Up` at 0.** The two eyeballs
  are ordinary storables carrying `position` and `rotation`, and they are the only lever for the
  horizontal direction: there is no left/right look morph - `asco - Look Up` is the entire
  `_Expressions/asco` set that answers to "Look". A `.vap` setting both `lEye` and `rEye` to
  `rotation 0,0,0` centres the irises, and `asco - Look Up` then sets the pitch: 0 is neutral (and
  reads slightly down), 1 is clearly up, and the morph accepts up to 2. It pitches the eyeballs
  without touching the lids, unlike `02-` and `08-Eyes Rolling`, which also squeeze them and are
  what turns a roll into closed eyes. Both levers need `lookMode=None`, or the gaze system
  overwrites them on the next frame.

## Downloading from the Hub

`search_hub` drives VAM's own Hub browser: VAM does the networking with the
user's session and their own `enableHubDownloader` preference. Never fetch a
Hub URL yourself and never sign in on their behalf.

- Results are ranked by download count, so "popular" is the real figure off the
  card, not a guess. `installed` says it is already in the library.
- `download_resource` is two-step **on purpose**. Call it with `confirm=false`
  first: that returns every package behind the resource with its size, which
  are dependencies, and which are already installed. Show the user that list
  and the total size, then call again with `confirm=true`.
- Packages with `canDownload=false` or `notOnHub=true` are paid or delisted.
  They come back under `blocked`; do not report them as downloaded.
- After `confirm=true`, call `wait_for_downloads`, then confirm the packages
  actually landed. `observedActive=false` means the queue was empty every poll,
  which is *not* proof a download ran.
- A filter that did not match reports the valid values instead of failing
  silently; `hub_info` dumps every chooser and its allowed values.

## If MCP is not connected

Install the Python package as above and attach `vam-mcp` to this agent. `VAM_ROOT` must be the folder the user named (the one with `VaM.exe`). Do not drive VAM by editing Unity files or sending raw JSON unless they explicitly ask you to debug the file bridge.

## Safety

Treat this as full control of the running VAM session. Only load paths the user asked for. Do not expose `Saves/PluginData/vam-mcp`.
