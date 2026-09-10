"""Load a VAM plugin onto a Person.

An appearance preset restores a plugin's saved values by storable id, but the
preset load path skips PluginManager, so it can never load a plugin that is not
already on the atom. A character whose makeup lives in DecalMaker therefore
arrives bare-faced, with its layers stored and nowhere to go. `add_plugin`
closes that gap.
"""

from __future__ import annotations

import time
from typing import Any

from . import bridge
from .paths import vam_root

# The plugins a saved character is likely to depend on, by the storable-id
# fragment that proves the plugin is live.
KNOWN = {
    "decalmaker": {
        "path": "Chokaphi.DecalMaker.57:/Custom/Scripts/Chokaphi/VAM_Decal_Maker/load.cslist",
        "storable": "Decal_Maker",
        "what": "makeup and face decals",
    },
}


def list_plugins(person: str = "") -> dict[str, Any]:
    args: dict[str, Any] = {}
    if person:
        args["person"] = person
    return bridge.call("list_plugins", timeout=25.0, **args).get("data") or {}


def _resolve(name_or_path: str) -> tuple[str, str | None]:
    key = name_or_path.strip().lower().replace(" ", "").replace("_", "")
    if key in KNOWN:
        entry = KNOWN[key]
        return entry["path"], entry["storable"]
    return name_or_path, None


def add_plugin(
    name_or_path: str,
    person: str = "",
    wait: float = 60.0,
) -> dict[str, Any]:
    """Load a plugin and wait for it to finish compiling.

    A VAM plugin compiles asynchronously, so the call returning is not the same
    as the plugin being usable. When the plugin is a known one, poll until its
    storable actually appears rather than reporting success on the request.
    """
    path, storable_hint = _resolve(name_or_path)
    args: dict[str, Any] = {"path": path}
    if person:
        args["person"] = person
    data = bridge.call("add_plugin", timeout=45.0, **args).get("data") or {}

    if str(data.get("alreadyLoaded", "")).lower() == "true":
        data["ready"] = True
        return data

    if not storable_hint:
        data["ready"] = None
        data["note"] = (
            "loaded, but this plugin is not in the known list so readiness was "
            "not confirmed - check list_plugins or get_appearance"
        )
        return data

    deadline = time.time() + wait
    while time.time() < deadline:
        time.sleep(3.0)
        app = bridge.call("get_appearance", timeout=45.0, **({"person": person} if person else {}))
        for st in (app.get("data") or {}).get("storables") or []:
            if storable_hint in str(st.get("id")):
                data["ready"] = True
                data["storable"] = str(st.get("id"))
                return data

    data["ready"] = False
    data["error"] = (
        "the plugin did not appear within %.0fs. VAM may be showing a plugin "
        "permission dialog that needs a click, or the path may be wrong." % wait
    )
    return data


def plugin_path_hint() -> dict[str, Any]:
    """What the known plugins are, and whether their packages are installed."""
    root = vam_root() / "AddonPackages"
    out = {}
    for key, entry in KNOWN.items():
        pkg = entry["path"].split(":", 1)[0]
        base = pkg.rsplit(".", 1)[0]
        installed = [p.name for p in root.rglob(base + "*.var")]
        out[key] = {
            "path": entry["path"],
            "provides": entry["what"],
            "installed": installed,
        }
    return out
