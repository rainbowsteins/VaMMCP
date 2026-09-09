# VamMCP

[English](#vammcp) | [中文说明](docs/zh-CN.md) ([源码视图](docs/zh-CN.md?plain=1))

> **Agents (Codex, Claude Code, Cursor, Grok, Copilot, …):** read **[AGENTS.md](AGENTS.md?plain=1)** first and follow it. Do not improvise a VAM workflow from this README.

VamMCP is an unofficial [Model Context Protocol](https://modelcontextprotocol.io/) integration for **Virt-A-Mate + vamX**. It combines a Python MCP server with a Virt-A-Mate **Session Plugin**, allowing an MCP-capable agent to use tool calls from natural-language conversation to control supported in-game functions—for example switching characters/looks, clothing, scenes, poses, and facial expressions.

This project is **not affiliated with Mesh VR, Virt-A-Mate, or vamX**. You must own legal copies of the software and content you use. See [NOTICE.md](NOTICE.md?plain=1).

**It does not generate assets.** It searches and loads files already on disk, and it can *combine* installed morphs, hair and clothing into an original character that it saves to a local library. It never creates a morph, texture or hair mesh you do not already own, and it never downloads from the Hub.

Current pieces:

| Piece | Version |
| --- | --- |
| Session plugin `VamMcpBridge` | 0.9.0 |
| Python package `vam-mcp` | 0.9.0 |

## How it works

```
MCP client  ->  vam-mcp (Python)  ->  Saves/PluginData/vam-mcp/*.json  ->  VamMcpBridge (Session Plugin)  ->  Virt-A-Mate + vamX
```

The bridge is a pair of local JSON files. Nothing is served on the network, and nothing talks HTTP inside Unity.

## What you need

- Windows
- Virt-A-Mate **1.20+** with vamX, installed in a folder that contains `VaM.exe`
- Python **3.10+** ([uv](https://github.com/astral-sh/uv) is optional)
- An MCP client / coding agent (Codex, Claude Code, Cursor, Grok, Copilot, …)
- Looks / scenes / clothing / poses already in that VAM install

`setup_couple` extra: a local package that still has paired F/M pose files (the server currently looks under `vamX.1.52:/Custom/Atom/Person/Pose/...`). Face aliases need the matching morphs on the Person. If those files are missing, the tool reports `missing` instead of inventing a face.

## Tell the agent your VAM folder

The agent can install the bridge at the canonical loose-script path. It **cannot guess** where VAM is installed.

Before asking it to install, send the **full path of the folder that contains `VaM.exe`**. That folder is `VAM_ROOT`. Example shape (use your real path):

```
VAM_ROOT\VaM.exe
VAM_ROOT\Custom\Scripts\VamMcp\Bridge\VamMcpBridge.cs
```

Do not omit the drive or folder names when you tell the agent. The agent must not invent a default path.

## Install (do these in order)

You need **both** halves: the VAM plugin and the Python MCP server. The plugin must be a **Session Plugin**, not a Scene Plugin and not a plugin on a Person atom.

### 1. Allow plugins in VAM

1. Start VAM / VaMX.
2. Open the main UI (`Esc`).
3. **User Preferences -> Security**.
4. Turn **Enable Plugins** on.
5. Recommended: turn **Plugins Always Enabled** on so VAM does not ask "allow this plugin?" every launch.

If you skip this, `Add Plugin` will either be missing or the script will sit there unused.

### 2. Install the VamMcpBridge plugin

Tell the agent your `VAM_ROOT` (the folder with `VaM.exe`) and ask it to install the plugin. From this repository it should run:

```powershell
.\scripts\install-dev.ps1 -VamRoot "VAM_ROOT"
```

That junctions `plugin\` into the VAM install when possible, and falls back to copying the files. The required result is:

```text
VAM_ROOT\Custom\Scripts\VamMcp\Bridge\VamMcpBridge.cs
```

This loose path is the supported installation method. Some VaMX builds can list a packaged `.var` but then report that `VamMcpBridge.cs` "does not exist" when loading it. If you update the source after installation, open **Session Plugins** and click **Reload** on `VamMcpBridge`.

### 3. Enable the plugin in the game (you must do this)

The agent cannot click the VAM UI. Scene plugins are destroyed when a scene loads. This one **must** stay on **Session Plugins**.

The screenshots below show Virt-A-Mate 1.22 with vamX 1.52. The exact layout may vary slightly by version.

1. Start Virt-A-Mate + vamX and switch the bottom mode selector to **Edit Mode (E)**.

   <img src="1.png" alt="Step 1: switch Virt-A-Mate and vamX to Edit Mode" width="760">

2. Click the main menu button with three horizontal lines near the lower-left corner.

   <img src="2.png" alt="Step 2: open the Virt-A-Mate main menu" width="760">

3. Select the purple **Session Plugins** tab, then click **Add Plugin**. Use the main Session Plugins panel—not Scene Plugins and not a plugin panel on a Person atom.

   <img src="3.png" alt="Step 3: open Session Plugins and click Add Plugin" width="760">

4. In the file browser pick `Custom/Scripts/VamMcp/Bridge/VamMcpBridge.cs` from the local VAM folders. Do not select the packaged `VamMcp.Bridge.N:/...` path.
5. If Virt-A-Mate asks to allow the plugin, choose **Allow**.
6. Confirm the plugin row is present and:
   - the **`enabled`** toggle is on
   - the status text says something like `ready  root=...`
7. Keep it across restarts:
   - still on the Session Plugins panel, open **Session Plugin Presets**
   - **Change User Defaults -> Set Current As User Defaults**

   If you skip it, the bridge is gone the next time you launch VAM.

Leave VAM running. The MCP server only works while this plugin is loaded and `enabled`.

### 4. Install the Python MCP server

From the `mcp` folder in this repo:

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install -e .
```

`VAM_ROOT` must be the folder that contains `VaM.exe` (the path you told the agent).

With [uv](https://github.com/astral-sh/uv) you can skip the venv and run `uv --directory mcp run vam-mcp` from the repo root.

### 5. Configure the agent / MCP client

`command` is this repo's venv Python (`mcp\.venv\Scripts\python.exe`). `VAM_ROOT` is the folder that contains `VaM.exe`. After saving, restart the agent or reload its MCP list.

Open **this repository** as the workspace so the agent auto-loads [AGENTS.md](AGENTS.md?plain=1).

#### Codex (CLI / IDE)

Append [examples/codex.config.toml](examples/codex.config.toml) to `%USERPROFILE%\.codex\config.toml` (or a project `.codex\config.toml`). Replace the two placeholders with your repo path and your `VAM_ROOT`:

```toml
[mcp_servers.vam]
command = 'REPO\mcp\.venv\Scripts\python.exe'
args = ["-m", "vam_mcp.server"]

[mcp_servers.vam.env]
VAM_ROOT = 'VAM_ROOT'
```

Or:

```powershell
codex mcp add vam --env VAM_ROOT=VAM_ROOT -- REPO\mcp\.venv\Scripts\python.exe -m vam_mcp.server
```

Then open this repo and start Codex. It reads `AGENTS.md` automatically.

#### Grok

Append [examples/grok.config.toml](examples/grok.config.toml) to `%USERPROFILE%\.grok\config.toml` (or a project `.grok\config.toml`):

```toml
[mcp_servers.vam]
command = 'REPO\mcp\.venv\Scripts\python.exe'
args = ["-m", "vam_mcp.server"]
enabled = true
startup_timeout_sec = 30
tool_timeout_sec = 120

[mcp_servers.vam.env]
VAM_ROOT = 'VAM_ROOT'
```

Or `grok mcp add vam -e VAM_ROOT=VAM_ROOT -- REPO\mcp\.venv\Scripts\python.exe -m vam_mcp.server`. In Grok run `/mcps` and press `r` to reload.

#### Claude Code

```powershell
claude mcp add vam --env VAM_ROOT=VAM_ROOT -- REPO\mcp\.venv\Scripts\python.exe -m vam_mcp.server
```

Open this repo as the project.

#### Cursor, Copilot, and other JSON clients

Same shape as [examples/claude_desktop.json](examples/claude_desktop.json) / [examples/grok.mcp.json](examples/grok.mcp.json):

```json
{
  "mcpServers": {
    "vam": {
      "command": "REPO\\mcp\\.venv\\Scripts\\python.exe",
      "args": ["-m", "vam_mcp.server"],
      "env": { "VAM_ROOT": "VAM_ROOT" }
    }
  }
}
```

| Client | Config file |
| --- | --- |
| Codex | `%USERPROFILE%\.codex\config.toml` |
| Grok | `%USERPROFILE%\.grok\config.toml` |
| Claude Desktop | `%APPDATA%\Claude\claude_desktop_config.json` |
| Cursor | `%USERPROFILE%\.cursor\mcp.json` or a project `.cursor\mcp.json` |
| VS Code Copilot | MCP section in user/workspace `mcp.json` |
| Any agent that reads `.mcp.json` | project root `.mcp.json` (same JSON as above) |

You should see a `vam` server with tools such as `status`, `list_scenes`, `setup_couple`.

### 6. Smoke test

With VAM running and `VamMcpBridge` loaded:

```powershell
.\scripts\ping-bridge.ps1 -VamRoot "VAM_ROOT"
```

A JSON payload with `"ok": "true"` and `"plugin": "VamMcpBridge"` means the file bridge works. Then ask the MCP client to call `status`.

## Daily use

1. Start **VAM first**, wait until the Session Plugin status says `ready`.
2. Start the agent **in this repo** so it sees [AGENTS.md](AGENTS.md).
3. Talk in plain language. The agent should call `list_*` then `load_*` (or `setup_couple` for two people plus a paired pose).

Examples:

- "What people are in the current scene?"
- "Search looks for this name and load that appearance."
- "Add these two people to the current room and sit them down."
- "Set her expression to smile."
- "Her head is tracking the camera — lock the head."
- "Move him half a metre to the left and turn him to face her."

After any scene / look / pose change the server writes a screenshot to `Saves/PluginData/vam-mcp/preview.png`. Ask the agent to `capture_view` if you want to check the result. The shot is the real back buffer at your window's resolution, so it looks exactly like your screen, VAM UI and all.

Typical tool flow: `list_*` -> pick an exact `path` -> `load_*`. Face changes use `set_expression` (plugin **0.5.0+**). Head tracking uses `lock_head` (plugin **0.5.1+**). Placement uses `get_position` then `move_person` (plugin **0.6.0+**). After you update `VamMcpBridge.cs`, **Reload** the Session Plugin.

## Build an original character

`list_looks` / `load_look` only replay presets someone already made. To make a
new character, start from the closest installed look and adjust it:

1. `list_looks` -> `load_look` a base in roughly the right direction.
2. `list_morphs` with a substring (`flat`, `young`, `chin`) to learn the **real**
   morph names, then `set_morphs`. Names differ per package, so never guess one.
3. `list_geometry_options(prefix="hair:")` / `set_geometry_options` for hair and
   clothing. `clear_prefix="hair:"` swaps to exactly one item in a single call.
4. `capture_view` after each step and actually look at the PNG.
5. `save_character(name, description)` when it is right. Write a description
   that says age, ethnicity, hair and build, because that is what
   `list_characters` shows in a later session.

Reuse comes first: call `list_characters` before building anything, and
`load_character(name)` if a suitable character already exists. That matters for
comics and any other work that needs the same face across many renders.

Anything with no dedicated tool can still be set by writing a small `.vap` with
`setUnlistedParamsToDefault: false` that lists only the storable and parameters
you want, then loading it with `load_look`. The plugin restores any storable id
it finds in a preset, so this reaches materials, colours and hair. Find the
parameter names by grepping the presets already installed under `AddonPackages`
rather than guessing them.

## Tools

| Tool | Role |
| --- | --- |
| `status` | Is `VAM_ROOT` valid and is the plugin alive? |
| `list_scenes` / `load_scene` | Search and load a scene (`merge=true` adds into the current scene) |
| `list_persons` | Person atoms in the current scene |
| `add_person` / `remove_person` / `set_person_on` | Add, delete, or show/hide a Person |
| `capture_view` | Save the VAM window to `Saves/PluginData/vam-mcp/preview.png` |
| `list_looks` / `load_look` | Search and apply an appearance preset (`keep_pose=true` leaves the current pose alone) |
| `list_clothing` / `load_clothing` | Search and apply a clothing preset |
| `list_poses` / `load_pose` | Search and apply a pose. Only pose storables are applied, so a preset that also carries a look cannot overwrite the character |
| `list_expressions` / `set_expression` | List aliases / live face morphs, then set a face (`smile`, `neutral`, `surprise`, …) |
| `lock_head` | Hold the head still so it does not follow the monitor camera |
| `get_position` / `move_person` | Read a Person's world position and rotation, then move or turn them |
| `setup_couple` | One-shot: resolve two looks, enable or add people, apply a paired pose |
| `list_morphs` / `set_morphs` | Search the morphs on a Person and set any of them |
| `list_geometry_options` / `set_geometry_options` | Hair and clothing toggles (`hair:` / `clothing:`) |
| `save_character` / `list_characters` / `load_character` | The local character library |
| `search_hub` | Search the VAM Hub through VAM's built-in browser, ranked by download count |
| `download_resource` | List a resource's packages with sizes, and with `confirm=true` download them |
| `download_status` / `wait_for_downloads` | Hub download queue state, and block until it drains |
| `hub_info` | Diagnostic: Hub state plus the real names and values of every filter chooser |
| `debug_cameras` | Diagnostic: cameras, culling masks, a Person's renderers and layers, geometry params |

### Downloading from the Hub

`search_hub` and `download_resource` drive VAM's own Hub browser — the same one
behind **File → Online Browser**. VAM does the networking with your session and
your `enableHubDownloader` preference, so nothing here logs in or fetches URLs
on its own. Downloading is deliberately two calls: the first returns the package
list with sizes and which ones you already have, and only a second call with
`confirm=true` starts the transfer. Plugin **0.10.0+**.

Do not ask the user to click **On** or delete atoms by hand — `set_person_on` / `remove_person` do that.

## Known limits

- Scene catalog is cached for the life of the MCP process. After you drop new `.var` files or looks into VAM, restart the MCP server so `list_*` sees them.
- `setup_couple` paired-pose paths currently assume **vamX 1.52**. A different package version will fail those `load_pose` calls unless that exact package is still installed.
- `set_expression` only drives morphs that are already on the Person. Missing morphs show up in `missing`; the face will not change.
- `lock_head` holds head/neck controllers and sets eyes to Target. A glance / look-at plugin on the Person can still turn the head — disable that plugin or lock again after it loads.
- `capture_view` returns after the next frame ends, and the PNG is your whole VAM window: screen resolution, UI included. Plugin **0.6.2+**; earlier versions rendered a camera into an offscreen texture, which silently left the character out of the picture because VAM skins bodies on the GPU outside that render.
- `gender` in `list_persons` is guessed from the character name. A male character whose name does not say so is reported as female, which can make `setup_couple` swap the two roles.
- Building a character only combines installed assets. Ethnicity in particular is not a slider: VAM ships no general "Asian" morph, so start from an installed look of the right ethnicity and adjust from there.
- `Breasts Small` does not stick if `TittyMagic` is loaded on the Person — that plugin drives breast morphs. `Flat Chested` is the reliable knob.
- Hair colour lives on the sim-hair storable `<hair internalId>Sim` as `rootColor` / `tipColor` HSV values, not on a morph. `save_character` captures it; a plain `load_look` of someone else's preset may not carry it.
- The MCP server leaves the last `command.json` on disk. Plugin **0.6.1+** claims that id at load and does not re-run it; an older plugin replays that one command every time it loads.
- `move_person` moves the **root control** only. If the Person's limb controllers are pinned by a pose, the body follows the root but a pose anchored to furniture can end up floating; re-apply the pose after a large move.
- `add_person` starts a VAM coroutine and returns immediately. `setup_couple` waits about two seconds; a slow machine may need a retry.
- `list_*` / `load_*` never create Hub content. If the look is not on disk, the answer is "not found".

## Troubleshooting

| Symptom | What to check |
| --- | --- |
| `status` says plugin missing | VAM is not running, or the Session Plugin was never added / not set as user default |
| MCP tool times out | Plugin `enabled` toggle is off; VAM is on a loading screen; or the plugin is a Scene Plugin and a scene load destroyed it |
| `Add Plugin` does nothing | **User Preferences -> Security -> Enable Plugins** |
| Plugin vanishes after restart | Session Plugin Presets -> **Set Current As User Defaults** |
| `VaM.exe not found` | `VAM_ROOT` is not the folder that contains `VaM.exe` |
| Packaged path says `VamMcpBridge.cs does not exist` | Run `.\scripts\install-dev.ps1 -VamRoot "VAM_ROOT"`, then add `Custom/Scripts/VamMcp/Bridge/VamMcpBridge.cs` |
| `unknown op: set_expression` / `lock_head` / `move_person` | Old plugin. Update the loose `.cs` file and **Reload** the Session Plugin (need 0.5.0+ / 0.5.1+ / 0.6.0+) |
| New looks do not appear in `list_looks` | Restart the MCP server so it rescans `AddonPackages` |
| `setup_couple` pose fails | Use `list_poses` / `load_pose`, or install the paired-pose package the server expects |
| Ping script says no response | Same as timeout: VAM running, Session Plugin loaded, `enabled` on |
| Agent ignores VAM tools | Workspace is not this repo (so `AGENTS.md` was not loaded), or the `vam` MCP server is not connected |
| Agent asks for VAM_ROOT | Tell it the folder that contains `VaM.exe`. It will not guess. |

## Pack a `.var` for GitHub Releases (maintainers only)

```powershell
.\scripts\pack-var.ps1 -Version 1
```

This is a release-artifact task, not the recommended VaMX installation method. Users should install the loose script at `VAM_ROOT\Custom\Scripts\VamMcp\Bridge\VamMcpBridge.cs`.

Commit source only. Attach `dist-var/VamMcp.Bridge.1.var` to the GitHub Release. Do not commit `.var` files, looks, scenes, or a VAM install.

## Safety

- Local files only. Nothing is served on the network.
- The tools can load any scene or preset visible under `VAM_ROOT`. That is full control of the running session.
- Do not attach this MCP server to untrusted agents or expose `Saves/PluginData/vam-mcp`.

## License

[MIT](LICENSE) for the code in this repository. Virt-A-Mate remains under its own EULA.
