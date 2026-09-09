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
from .paths import vam_root
from .pose import load_pose as load_pose_impl

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
def load_look(path: str, person: str = "") -> str:
    """Load an appearance/look preset onto a Person. person is the atom uid from list_persons; empty uses the first Person."""
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
    """Move a Person's root control. x/y/z set absolute world position, dx/dy/dz add an offset, rx/ry/rz set rotation in degrees. Call get_position first to read the current values. person is the atom uid from list_persons; empty uses the first Person."""
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
    """List the hair and clothing items available on a Person. These are bool toggles named "hair:<item>" / "clothing:<item>". prefix filters by kind, query filters by substring (twintail, pigtail, skirt...). "activeInPrefix" tells you what is currently worn. person is an atom uid; empty uses the first Person."""
    args: dict[str, Any] = {"prefix": prefix, "query": query, "limit": limit}
    if person:
        args["person"] = person
    result = bridge.call("list_geometry_options", timeout=30.0, **args)
    return _dump(result.get("data") or result)


@mcp.tool()
def set_geometry_options(
    options: list[dict[str, Any]],
    clear_prefix: str = "",
    person: str = "",
) -> str:
    """Turn hair or clothing items on or off. options is a list of {"name": "hair:Low Twintails", "on": true}. clear_prefix (e.g. "hair:") switches everything with that prefix off first, so swapping to exactly one hair item is a single call. Get real names from list_geometry_options."""
    args: dict[str, Any] = {"options": options}
    if clear_prefix:
        args["clearPrefix"] = clear_prefix
    if person:
        args["person"] = person
    result = bridge.call("set_geometry_options", timeout=45.0, **args)
    return _dump(_capture_after(result.get("data") or result))


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


def main() -> None:
    mcp.run()


if __name__ == "__main__":
    main()
