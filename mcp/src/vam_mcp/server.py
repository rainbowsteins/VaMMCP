from __future__ import annotations

import json
from typing import Any

from mcp.server import MCPServer

from pathlib import Path

from . import __version__
from . import bridge
from .catalog import list_items
from .character import list_characters as list_characters_impl
from .character import load_character as load_character_impl
from .character import save_character as save_character_impl
from .couple import setup_couple as setup_couple_impl
from .expression import list_expressions as list_expressions_impl
from .expression import set_expression as set_expression_impl
from .headlock import lock_head as lock_head_impl
from .hub import download_resource as download_resource_impl
from .hub import download_status as download_status_impl
from .hub import hub_info as hub_info_impl
from .hub import rescan_packages as rescan_packages_impl
from .hub import search_hub as search_hub_impl
from .hub import wait_for_downloads as wait_for_downloads_impl
from .paths import vam_root
from .plugins import add_plugin as add_plugin_impl
from .plugins import list_plugins as list_plugins_impl
from .pose import load_look_keep_pose as load_look_keep_pose_impl
from .pose import load_pose as load_pose_impl
from .storables import call_action as call_action_impl
from .storables import get_appearance as get_appearance_impl
from .storables import list_actions as list_actions_impl
from .storables import set_bool_param as set_bool_param_impl

mcp = MCPServer(
    name="vam-mcp",
    version=__version__,
    instructions=(
        "Unofficial Virt-A-Mate controller. Read AGENTS.md in the VamMCP repo "
        "and follow it. For two people plus a paired pose in the current scene, "
        "call setup_couple(female, male, pose). "
        "Do not ask the user to click On or delete atoms. "
        "Looks/poses must already exist on disk. VamMcpBridge must be loaded. "
        "For face changes use set_expression (smile/neutral/surprise/sad/angry "
        "or a morph name from list_expressions). "
        "If a head turns with the camera, call lock_head. "
        "After any scene change, call capture_view and inspect the saved PNG. "
        "To build an original character: load_look a close base, then "
        "list_morphs / set_morphs and list_geometry_options / "
        "set_geometry_options to adjust, checking capture_view each time, "
        "and save_character when it is right. Reuse a saved one with "
        "list_characters / load_character instead of rebuilding it. "
        "Only installed assets can be combined; nothing new is generated."
    ),
)


def _dump(data: Any) -> str:
    return json.dumps(data, ensure_ascii=False, indent=2)


def _preview_abs() -> str:
    return str(vam_root() / "Saves" / "PluginData" / "vam-mcp" / "preview.png")


def _capture_after(payload: Any) -> Any:
    if not isinstance(payload, dict):
        payload = {"data": payload}
    try:
        cap = bridge.call("capture_view", timeout=25.0)
        payload["preview"] = cap.get("data") or cap
        payload["previewAbsolute"] = _preview_abs()
    except Exception as exc:
        payload["previewError"] = str(exc)
        payload["previewAbsolute"] = _preview_abs()
    return payload


@mcp.tool()
def status() -> str:
    """Check VAM_ROOT and whether the VamMcpBridge session plugin is alive."""
    try:
        return _dump(bridge.status())
    except Exception as exc:
        return _dump({"ok": False, "error": str(exc)})


@mcp.tool()
def list_scenes(query: str = "", limit: int = 25) -> str:
    """Search local VAM scenes (Saves/scene and .var packages)."""
    items = list_items("scene", query=query, limit=limit)
    return _dump(
        {
            "count": len(items),
            "items": [item.__dict__ for item in items],
        }
    )


@mcp.tool()
def list_looks(query: str = "", limit: int = 25) -> str:
    """Search local appearance / look presets (.vap)."""
    items = list_items("look", query=query, limit=limit)
    return _dump(
        {
            "count": len(items),
            "items": [item.__dict__ for item in items],
        }
    )


@mcp.tool()
def list_clothing(query: str = "", limit: int = 25) -> str:
    """Search local clothing presets (.vap)."""
    items = list_items("clothing", query=query, limit=limit)
    return _dump(
        {
            "count": len(items),
            "items": [item.__dict__ for item in items],
        }
    )


_POSE_ALIASES = {
    "sit": "sitting",
    "sitting": "sitting",
    "seated": "seated",
    "坐下": "sitting",
    "坐着": "sitting",
    "坐": "sitting",
    "lie": "lying",
    "lying": "lying",
    "躺": "lying",
    "躺下": "lying",
    "stand": "standing",
    "standing": "standing",
    "站": "standing",
    "站着": "standing",
}


def _pose_query(query: str) -> str:
    key = query.strip().lower()
    return _POSE_ALIASES.get(key, query)


@mcp.tool()
def list_poses(query: str = "", limit: int = 25) -> str:
    """Search local pose presets (.vap). Use sit/sitting, lying, stand. For Chinese 坐下/躺/站 too."""
    items = list_items("pose", query=_pose_query(query), limit=limit)
    return _dump(
        {
            "count": len(items),
            "items": [item.__dict__ for item in items],
        }
    )


@mcp.tool()
def list_persons() -> str:
    """List Person atoms in the currently loaded VAM scene."""
    result = bridge.call("list_persons", timeout=10.0)
    return _dump(result.get("data") or [])


@mcp.tool()
def remove_person(person: str) -> str:
    """Delete a Person atom from the current scene. person is the atom uid from list_persons."""
    result = bridge.call("remove_person", timeout=20.0, person=person)
    return _dump(result.get("data") or result)


@mcp.tool()
def set_person_on(person: str, on: bool = True) -> str:
    """Show or hide a Person atom. Use this to enable Person#2 or hide a broken one."""
    result = bridge.call("set_person_on", timeout=10.0, person=person, on=on)
    return _dump(result.get("data") or result)


@mcp.tool()
def add_person(uid: str = "MCPPerson") -> str:
    """Add a new Person atom to the current scene. Then load_look / load_pose on the returned uid."""
    result = bridge.call("add_person", timeout=20.0, uid=uid)
    return _dump(result.get("data") or result)


@mcp.tool()
def load_scene(path: str, merge: bool = False) -> str:
    """Load a scene by the exact path from list_scenes. merge=True keeps the current scene and adds into it."""
    result = bridge.call("load_scene", timeout=90.0, path=path, merge=merge)
    return _dump(_capture_after(result.get("data") or result))


@mcp.tool()
def save_scene(path: str) -> str:
    """Save the current scene to a JSON file, so what is on screen now survives a VAM restart or a crash. path is VAM-root-relative, normally "Saves/scene/<name>.json"; ".json" is appended if you leave it off, and parent folders are created by VAM's own file layer.

    Two things to know. It OVERWRITES without asking: VAM's own Save button routes through a path that raises a modal "confirm" prompt when the target exists, and a headless caller has nobody to click it, so this op deliberately uses the same internal pair VAM itself writes the file with rather than that button. And it writes the scene only - VAM also drops a matching .jpg preview next to it, which this does not, so the scene browser thumbnail stays empty until you save once from the UI.

    Loading a scene returns before its assets finish, and a merged environment is only in memory until you save, so save after the scene looks right rather than in the same breath as the load.
    """
    result = bridge.call("save_scene", timeout=180.0, path=path)
    return _dump(result.get("data") or result)


@mcp.tool()
def load_look(path: str, person: str = "", keep_pose: bool = False) -> str:
    """Load an appearance/look preset onto a Person. person is the atom uid from list_persons; empty uses the first Person. Most third-party looks also ship a pose, and applying one moves and re-poses the character; pass keep_pose=True to apply only the appearance and leave the current pose alone, which is what you want when swapping outfits or makeup between shots of a posed character."""
    if path.lower().endswith(".json"):
        return _dump(
            {
                "ok": False,
                "error": (
                    "That path is a scene file, not an appearance preset. "
                    "Call load_scene with the same path (or merge=true to add it into the current scene)."
                ),
                "path": path,
            }
        )
    if keep_pose:
        return _dump(_capture_after(load_look_keep_pose_impl(path=path, person=person)))
    args: dict[str, Any] = {"path": path}
    if person:
        args["person"] = person
    result = bridge.call("load_look", timeout=45.0, **args)
    return _dump(_capture_after(result.get("data") or result))


@mcp.tool()
def load_clothing(path: str, person: str = "") -> str:
    """Load a clothing preset onto a Person. person is the atom uid from list_persons; empty uses the first Person."""
    args: dict[str, Any] = {"path": path}
    if person:
        args["person"] = person
    result = bridge.call("load_clothing", timeout=45.0, **args)
    return _dump(_capture_after(result.get("data") or result))


@mcp.tool()
def load_pose(path: str, person: str = "", include_appearance: bool = False) -> str:
    """Load a pose preset, applying only the controllers that make up the pose. Many presets filed as poses (vamX's _POSE LIBRARY among them) also carry a geometry storable, and applying that wholesale replaces the character's face, hair and clothing. Those storables are dropped and reported. person is an atom uid, empty for the first Person, or 'all' to pose everyone. Set include_appearance=True only when you do want the preset's look as well."""
    return _dump(_capture_after(load_pose_impl(
        path=path, person=person, include_appearance=include_appearance)))


@mcp.tool()
def list_expressions(query: str = "", person: str = "") -> str:
    """List facial-expression aliases and live expression morphs on a Person. query filters by name (ahegao, 吐舌, smile, …). person is an atom uid from list_persons; empty uses the first Person."""
    return _dump(list_expressions_impl(query=query, person=person))


@mcp.tool()
def set_expression(name: str, person: str = "", value: float = 1.0, reset: bool = True) -> str:
    """Set a facial expression. name is an alias (smile, neutral, surprise, sad, angry, …) or a morph name from list_expressions. Chinese aliases such as 笑 / 无表情 / 惊讶 also work. value is strength 0-1 (up to 2). reset=True clears other expression morphs first. Does not change clothes, hair, body pose, or face shape."""
    return _dump(_capture_after(set_expression_impl(name=name, person=person, value=value, reset=reset)))


@mcp.tool()
def lock_head(person: str = "", locked: bool = True) -> str:
    """Lock or unlock a Person head so it stops following the monitor camera. locked=True disables Glance/gaze look-at and holds head/neck. person is an atom uid from list_persons; empty uses the first Person."""
    return _dump(_capture_after(lock_head_impl(person=person, locked=locked)))


@mcp.tool()
def get_position(person: str = "") -> str:
    """Get the current world position (x,y,z) and rotation (rx,ry,rz) of a Person's root control. person is the atom uid from list_persons; empty uses the first Person."""
    args: dict[str, Any] = {}
    if person:
        args["person"] = person
    result = bridge.call("get_position", timeout=10.0, **args)
    return _dump(result.get("data") or result)


@mcp.tool()
def move_person(
    person: str = "",
    x: float | None = None,
    y: float | None = None,
    z: float | None = None,
    dx: float | None = None,
    dy: float | None = None,
    dz: float | None = None,
    rx: float | None = None,
    ry: float | None = None,
    rz: float | None = None,
) -> str:
    """Move a Person's root control. x/y/z set absolute world position, dx/dy/dz add an offset; rx/ry/rz set rotation in degrees.

    The axes are world axes and they are NOT interchangeable. ry turns the
    character on the spot - that is the one for facing them a different way.
    rx tips them forward or back. rz ROLLS them sideways, and because the root
    sits at floor level (y=0) a roll of 90 lays a standing character flat on the
    ground. That is geometry, not a physics fault: the write is a rigid
    transform, and rz back to 0 stands the character up again - checked on a
    Person carrying MacGruber's Life with its modules enabled, and on one with no
    plugins at all. Set ry to 0/90/180/270 for a front/right/back/left turnaround.

    Call get_position first to read the current values. person is the atom uid
    from list_persons; empty uses the first Person."""
    args: dict[str, Any] = {}
    if person:
        args["person"] = person
    for key, val in {
        "x": x,
        "y": y,
        "z": z,
        "dx": dx,
        "dy": dy,
        "dz": dz,
        "rx": rx,
        "ry": ry,
        "rz": rz,
    }.items():
        if val is not None:
            args[key] = val
    result = bridge.call("move_person", timeout=10.0, **args)
    return _dump(_capture_after(result.get("data") or result))


@mcp.tool()
def setup_couple(female: str, male: str = "", pose: str = "doggy") -> str:
    """One-shot: put a female and male look in the current scene and apply a paired pose.

    female/male are look names or exact .vap paths.
    pose is the user's pose name (or a name from list_poses). Enables hidden people
    and adds a person if needed. Requires VamMcpBridge 0.3.0+.
    """
    return _dump(_capture_after(setup_couple_impl(female=female, male=male, pose=pose)))


@mcp.tool()
def list_morphs(query: str = "", limit: int = 60, person: str = "") -> str:
    """Search the morphs actually loaded on a Person, by substring of the display name. Use this to find real morph names before set_morphs: names vary by package (breast size, asian, young, chin, nose...). Returns total/matched counts so you can tell a bad query from an empty library. person is an atom uid; empty uses the first Person."""
    args: dict[str, Any] = {"query": query, "limit": limit}
    if person:
        args["person"] = person
    result = bridge.call("list_morphs", timeout=30.0, **args)
    return _dump(result.get("data") or result)


@mcp.tool()
def set_morphs(morphs: list[dict[str, Any]], person: str = "") -> str:
    """Set any morphs on a Person. morphs is a list of {"name": <display name from list_morphs>, "value": <float>}. Values are usually 0..1 but many morphs accept negatives and up to 2. Does not touch expressions, clothing, or hair. Names that do not exist come back in "missing" rather than failing the whole call."""
    args: dict[str, Any] = {"morphs": morphs}
    if person:
        args["person"] = person
    result = bridge.call("set_morphs", timeout=30.0, **args)
    return _dump(_capture_after(result.get("data") or result))


@mcp.tool()
def list_geometry_options(prefix: str = "hair:", query: str = "", limit: int = 80, person: str = "") -> str:
    """List the hair and clothing items available on a Person. These are bool toggles named "hair:<item>" / "clothing:<item>". prefix filters by kind, query filters by substring (twintail, pigtail, skirt...). "activeInPrefix" tells you what is currently worn. The plugin caps how many rows it returns, so pass a query rather than paging through everything: the query is applied to the full list before the cap, and "truncated" in the result says when rows were left out. person is an atom uid; empty uses the first Person."""
    args: dict[str, Any] = {"prefix": prefix, "query": query, "limit": limit}
    if person:
        args["person"] = person
    result = bridge.call("list_geometry_options", timeout=30.0, **args)
    data = result.get("data") or result
    if isinstance(data, dict):
        try:
            matched = int(data.get("matched", 0))
            returned = int(data.get("returned", 0))
        except (TypeError, ValueError):
            matched = returned = 0
        if matched > returned:
            # The plugin clamps its own limit, so a wide listing silently loses
            # rows - which is how a freshly installed item looked absent.
            data["truncated"] = True
            data["note"] = (f"{matched} items matched but only {returned} were returned. "
                            "Pass a query to search the full list instead of listing everything.")
    return _dump(data)


@mcp.tool()
def set_geometry_options(
    options: list[dict[str, Any]],
    clear_prefix: str = "",
    person: str = "",
) -> str:
    """Turn hair or clothing items on or off. options is a list of {"name": "hair:Low Twintails", "on": true}. clear_prefix (e.g. "hair:") switches everything with that prefix off first, so swapping to exactly one hair item is a single call. Get real names from list_geometry_options. A name that does not exist comes back in `failed` with a `didYouMean`; a `duplicates` entry means the same item file is worn twice from two packages (vamX re-bundles other creators' hair) and the meshes are stacking - switch off all but one using the full id."""
    args: dict[str, Any] = {"options": options}
    if clear_prefix:
        args["clearPrefix"] = clear_prefix
    if person:
        args["person"] = person
    result = bridge.call("set_geometry_options", timeout=45.0, **args)
    data = result.get("data") or result
    if isinstance(data, dict) and data.get("duplicates"):
        # Lead with it: this was missed twice when it sat at the end of the reply.
        items = ", ".join(str(d.get("item")) for d in data["duplicates"])
        data = dict(data)
        data["ATTENTION"] = (
            "worn twice, meshes are stacking: %s. Switch off the extra copies "
            "with their full ids before judging the result." % items
        )
    return _dump(_capture_after(data))


@mcp.tool()
def save_character(name: str, description: str = "", person: str = "") -> str:
    """Save the Person's current appearance into the local character library and return its name. Writes a normal VAM appearance preset under Custom/Atom/Person/Appearance/VamMcp plus an index entry, so load_character(name) brings the exact same character back in any later session. Pass a description (age, ethnicity, hair, build) so the character is recognisable in list_characters later. Use this once a character built with set_morphs / set_geometry_options looks right."""
    return _dump(save_character_impl(name=name, description=description, person=person))


@mcp.tool()
def list_characters(query: str = "") -> str:
    """List the characters already in the local library, with the description, base character, hair, clothing and notable morphs recorded for each. Call this before building a new character so an existing one gets reused, and to answer "which characters do I have?". Reads from disk, so characters saved after the server started still show up."""
    return _dump(list_characters_impl(query=query))


@mcp.tool()
def load_character(name: str, person: str = "") -> str:
    """Load a character from the local library by name (from list_characters) onto a Person. Use this instead of load_look for characters saved with save_character; it reproduces the whole appearance including hair colour."""
    return _dump(_capture_after(load_character_impl(name=name, person=person)))


@mcp.tool()
def debug_cameras() -> str:
    """Diagnostic. Dump every camera with its culling mask, the first Person's enabled renderers and their layers, and the geometry storable's params. Use when a capture looks wrong (person missing, frame black) to tell a camera-culling problem from a render-path one, or to find the real name of a storable param."""
    result = bridge.call("debug_cameras", timeout=25.0)
    return _dump(result.get("data") or result)


@mcp.tool()
def capture_view() -> str:
    """Capture the VAM window to Saves/PluginData/vam-mcp/preview.png and read that PNG. Call after any pose/look/scene change. The shot is the real back buffer at the window's own resolution, so it includes the VAM UI and any open panels."""
    result = bridge.call("capture_view", timeout=25.0)
    data = result.get("data") or {}
    if not isinstance(data, dict):
        data = {"data": data}
    data["previewAbsolute"] = _preview_abs()
    return _dump(data)


@mcp.tool()
def search_hub(
    query: str,
    category: str = "",
    pay_type: str = "Free",
    sort: str = "Downloads",
    creator: str = "",
    limit: int = 20,
    hide_installed: bool = False,
) -> str:
    """Search the VAM Hub through VAM's own built-in browser and return results ranked by download count. VAM does the networking with the user's own Hub session, so nothing here logs in or fetches URLs. category/pay_type/sort are matched loosely against the real chooser values and the call reports back which value it actually set; if a filter did not match, the reply lists the valid choices. Set hide_installed=True to drop resources already in the library. Each result carries a resourceId for download_resource. Opens the Hub panel in VAM, so call capture_view if you want to see it."""
    return _dump(
        search_hub_impl(
            query=query,
            category=category,
            pay_type=pay_type,
            sort=sort,
            creator=creator,
            limit=limit,
            hide_installed=hide_installed,
        )
    )


@mcp.tool()
def download_resource(resource_id: str, confirm: bool = False) -> str:
    """List the packages behind a Hub resource, and with confirm=True download them via VAM. Call it first with confirm=False: that opens the resource and returns the manifest - every package with its size, whether it is a dependency, and whether it is already installed - plus totalSize. Show that to the user and only then call again with confirm=True. Packages flagged notOnHub or canDownload=false cannot be fetched (paid or delisted) and are listed under blocked. Use resource_id from search_hub."""
    return _dump(download_resource_impl(resource_id=resource_id, confirm=confirm))


@mcp.tool()
def download_status() -> str:
    """Current Hub download queue: whether VAM is downloading, how many are pending, and per-package state for the open resource."""
    return _dump(download_status_impl())


@mcp.tool()
def wait_for_downloads(timeout: float = 900.0) -> str:
    """Block until VAM's Hub download queue drains, then report the final state. Reports observedActive so a queue that was empty the whole time is not mistaken for a completed download."""
    return _dump(wait_for_downloads_impl(timeout=timeout))


@mcp.tool()
def hub_info() -> str:
    """Diagnostic. Hub enabled/downloader state plus the real names and allowed values of every HubBrowse filter chooser (category, pay type, sort, creator, tags). Use when a search_hub filter did not take."""
    return _dump(hub_info_impl())


@mcp.tool()
def list_plugins(person: str = "") -> str:
    """List the plugins loaded on a Person, as slot -> path. Use it to check whether a plugin a saved character depends on (DecalMaker for makeup) is actually present."""
    return _dump(list_plugins_impl(person=person))


@mcp.tool()
def add_plugin(name: str, person: str = "") -> str:
    """Load a VAM plugin onto a Person and wait for it to compile. name can be a shorthand ("decalmaker") or a full VAR path like "Creator.Pkg.1:/Custom/Scripts/.../load.cslist". Needed because an appearance preset restores a plugin's saved values but cannot load the plugin itself, so a character whose makeup lives in DecalMaker loads bare-faced until the plugin is on the atom. Reports ready=true only once the plugin's storable actually appears; ready=false usually means VAM is showing a plugin permission dialog that needs a click."""
    return _dump(add_plugin_impl(name_or_path=name, person=person))


@mcp.tool()
def rescan_packages() -> str:
    """Make VAM re-index AddonPackages after a Hub download. A .var on disk is not a usable item until VAM rescans, so call this between download_resource and trying to wear the new item; VAM re-indexes asynchronously, so poll list_geometry_options until the item shows up."""
    return _dump(rescan_packages_impl())


@mcp.tool()
def list_actions(query: str = "", person: str = "") -> str:
    """List the button-like JSONStorableActions on a Person, as storable -> action rows. Read this before call_action: action names are exact strings off the atom and are not guessed or case-folded. query filters on the storable id or the action name. Capped at 200 rows. person is an atom uid; empty uses the first Person."""
    return _dump(list_actions_impl(query=query, person=person))


@mcp.tool()
def call_action(storable: str, action: str, person: str = "") -> str:
    """Press one button on a Person's storable. A .vap cannot do this: RestoreFromJSON restores values and never fires an action, which is why a button that a preset depends on stays unpressed. The one that matters is DecalMaker's "Clear All Frames" - its DecalHead makeup array is additive, so clear it before applying a stack or the layers pile up and the alpha halo every eyeshadow texture carries builds into a visible rectangle on the cheek. storable is the storable id ("_vam_decal_maker_2.core"); a plugin storable is "plugin#<n>_Namespace.Class" where n shifts with load order and the bridge remaps that for you. A storable that is not on the atom comes back as an error naming it - call list_plugins, and add_plugin("decalmaker") if the plugin is missing."""
    return _dump(call_action_impl(storable=storable, action=action, person=person))


@mcp.tool()
def set_bool_param(storable: str, param: str, value: bool = True, person: str = "") -> str:
    """Set a bool on a storable through its real setter rather than by restoring raw JSON. Needed when the value alone is not enough: useFemaleMorphsOnMale has to run its setter to rebuild the morph library, and a restored value skips that. Returns the value the storable reports afterwards, so a name that silently did nothing is visible. person is an atom uid; empty uses the first Person."""
    return _dump(set_bool_param_impl(storable=storable, param=param, value=value, person=person))


@mcp.tool()
def get_appearance(person: str = "") -> str:
    """Dump every appearance-like storable on a Person as raw JSON: {person, count, skippedCount, storables}. Each entry is that storable's own GetJSON() with its id attached. Skeleton controllers are skipped (those are pose) and so is anything with "animation" in the id. Plugin storables ARE included, because some of them carry appearance - DecalMaker's makeup layers are the case in point. Also the way to confirm add_plugin("decalmaker") finished compiling: poll it until the storable appears. A storable whose value reads back absent may just be at its default, since GetJSON only serialises values that differ from it. person is an atom uid; empty uses the first Person."""
    return _dump(get_appearance_impl(person=person))


@mcp.tool()
def list_atoms(query: str = "") -> str:
    """List every atom in the scene with its uid, type and on/off state. Unlike list_persons this includes lights, cameras, subscenes and UI atoms. query filters on the uid or the type. Use it to find what get_atom_params / set_atom_params can target."""
    args: dict[str, Any] = {}
    if query:
        args["query"] = query
    return _dump(bridge.call("list_atoms", timeout=25.0, **args).get("data") or {})


@mcp.tool()
def get_atom_params(atom: str, storable: str = "") -> str:
    """Read an atom's parameters, grouped by storable into floats / bools / strings / colors. atom is a uid from list_atoms; pass storable to read just one (a light's is "Light", holding on, intensity, range, shadowsOn and color). This is the only way to see a non-Person atom - get_appearance refuses anything that is not a Person."""
    args: dict[str, Any] = {"atom": atom}
    if storable:
        args["storable"] = storable
    return _dump(bridge.call("get_atom_params", timeout=30.0, **args).get("data") or {})


@mcp.tool()
def set_atom_params(atom: str, storable: str, params: dict[str, Any]) -> str:
    """Set parameters on any atom, including lights. params maps param name to value; the type is taken from the atom, so a float takes a number, a bool takes true/false and a colour takes {"h":..,"s":..,"v":..}. Every write is read straight back and reported under `readBack`, and a name the storable does not have comes back in `failed` rather than passing silently. Example: set_atom_params("3PointLightSetup/LightBack", "Light", {"on": true, "intensity": 6.0})."""
    result = bridge.call("set_atom_params", timeout=45.0, atom=atom, storable=storable, params=params)
    return _dump(result.get("data") or result)


def main() -> None:
    mcp.run()


if __name__ == "__main__":
    main()
