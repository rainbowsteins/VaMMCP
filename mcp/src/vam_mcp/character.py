"""Local character library.

Characters are ordinary VAM appearance presets under
``Custom/Atom/Person/Appearance/VamMcp``, so VAM's own preset browser can load
them. Alongside them sits ``characters.json``, an index that remembers what each
one is, which is what makes a name enough to find a character in a later
session.

The preset file is written here rather than by the plugin. The server has plain
filesystem access, so it does not trip VAM's "plugin wants to save json"
prompt, and deciding which storables belong in a look stays a Python concern
that never needs a plugin reload.
"""

from __future__ import annotations

import json
import re
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from . import bridge
from .paths import vam_root
from .pose import is_body_pose_storable

LIB_REL = "Custom/Atom/Person/Appearance/VamMcp"
INDEX_NAME = "characters.json"

# Storables worth keeping in a look. "Sim" is the sim-hair storable that holds
# rootColor / tipColor, and it is the reason a naive name filter loses hair
# colour. Materials cover skin, eyes and scalp.
_KEEP_EXACT = {
    "geometry", "skin", "eyes", "rescaleobject",
    # The eye colour lives here, not on the Enhanced Eyes clothing item: painting
    # all four of that item's iris layers bright green renders zero green pixels,
    # so those layers are not what you see.
    "irises", "sclera",
}
_KEEP_SUBSTRINGS = ("material", "hair", "clothing", "scalp")

# Storables that drive the face every frame. They are not appearance in
# themselves, but leaving them out means a saved look does not reproduce: VAM's
# eyelids-follow-the-gaze system pins Eyelids Top Up near 0.475, which cancels
# any eye-shape morph and the eyes come back round.
_KEEP_DRIVERS = {"eyelidcontrol", "autoexpressions"}

# Storables named "...Control" that hold face or soft-body setup rather than a
# position. Everything else ending in Control is either a skeleton controller or
# a scene coordinate such as eyeTargetControl.
_KEEP_CONTROLS = {
    "eyelidcontrol", "breastcontrol", "glutecontrol",
    "jawcontrol", "tonguecontrol", "pectoralcontrol",
}

# A worn item's own fit storables, matched on the suffix because the prefix is
# the item id. WrapControl holds surfaceOffset - how far the garment sits off
# the skin - which is what stops a broad-shouldered character's skin poking
# through a shirt. Dropping it means the fix does not survive a save.
_ITEM_FIT_SUFFIXES = ("wrapcontrol", "itemcontrol")

# Plugin storables whose enabled state changes how the character renders at
# rest. Unlike the appearance plugins below, only their on/off matters.
_DRIVER_PLUGINS = (
    "macgruber.gaze",
    "macgruber.breathing",
    "macgruber.driverbreathing",
    "macgruber.audioattenuation",
)

# Plugin storables that carry appearance rather than behaviour, matched on the
# id suffix so the "plugin#<n>_" index a plugin happens to get does not matter.
# DecalMaker identifies itself the same way. Physics and animation plugins are
# deliberately absent: their state is not part of a look.
#
# The value is the plugin's own reset action, needed because these plugins are
# additive: restoring DecalMaker's layers appends them, so loading a character
# twice would stack the makeup twice and darken the halo each texture carries.
# Calling the reset first makes a load idempotent.
#
# Each reset is only sent once _has_plugin_action confirms the storable is
# really on the atom: this dict lists every DecalMaker build whose ids have been
# seen, and only the one actually loaded can answer.
_APPEARANCE_PLUGINS = {
    "_vam_decal_maker_2.core": "Clear All Frames",
    "_vam_decal_maker.decal_maker": "Clear All Frames",
}


def _lib_dir() -> Path:
    path = vam_root() / LIB_REL.replace("/", "\\")
    path.mkdir(parents=True, exist_ok=True)
    return path


def slug(name: str) -> str:
    cleaned = re.sub(r"[^0-9A-Za-z_\- ]+", "", name or "").strip()
    cleaned = re.sub(r"\s+", " ", cleaned)
    return cleaned[:60]


def _is_appearance(sid: str) -> bool:
    low = (sid or "").lower()
    if not low:
        return False
    # Skeleton controllers carry the pose, never the look. pose.py already
    # decides which ids those are, so the rule is not restated here.
    if is_body_pose_storable(low):
        return False
    if low.startswith("plugin"):
        if any(low.endswith(suffix) for suffix in _APPEARANCE_PLUGINS):
            return True
        return any(tag in low for tag in _DRIVER_PLUGINS)
    if low in _KEEP_EXACT or low in _KEEP_DRIVERS:
        return True
    if low.endswith("sim"):
        return True
    if "control" in low:
        # Named, not inferred: eyeTargetControl is a world position and would
        # drag a scene coordinate into a look, so "not a bone" is too loose.
        if low.endswith(_ITEM_FIT_SUFFIXES):
            return True
        return low in _KEEP_CONTROLS or "finger" in low or "thumb" in low
    return any(token in low for token in _KEEP_SUBSTRINGS)


def _index_path() -> Path:
    return _lib_dir() / INDEX_NAME


def _read_index() -> dict[str, Any]:
    path = _index_path()
    if not path.is_file():
        return {"characters": []}
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {"characters": []}
    if not isinstance(data, dict) or not isinstance(data.get("characters"), list):
        return {"characters": []}
    return data


def _write_index(data: dict[str, Any]) -> None:
    _index_path().write_text(
        json.dumps(data, ensure_ascii=False, indent=2), encoding="utf-8"
    )


def _summarise(storables: list[dict[str, Any]]) -> dict[str, Any]:
    """Pull the few facts that make an index entry recognisable later."""
    out: dict[str, Any] = {}
    geo = next((st for st in storables if st.get("id") == "geometry"), None)
    if geo:
        out["character"] = geo.get("character") or ""
        hair = [h.get("internalId") or h.get("id") for h in geo.get("hair") or []
                if str(h.get("enabled")).lower() != "false"]
        clothing = [c.get("internalId") or c.get("id") for c in geo.get("clothing") or []
                    if str(c.get("enabled")).lower() != "false"]
        out["hair"] = [str(h).split("/")[-1] for h in hair if h]
        out["clothing"] = [str(c).split("/")[-1] for c in clothing if c]
        morphs = geo.get("morphs") or []
        out["morphCount"] = len(morphs)
        notable = []
        for m in morphs:
            try:
                if abs(float(m.get("value") or 0)) >= 0.3:
                    notable.append(f"{m.get('name')}={m.get('value')}")
            except (TypeError, ValueError):
                continue
        out["notableMorphs"] = notable[:8]
    coloured = [st.get("id") for st in storables
                if any(k for k in st if "olor" in k)]
    out["colouredStorables"] = [c for c in coloured if c][:12]
    return out


def save_character(name: str, description: str = "", person: str = "") -> dict[str, Any]:
    safe = slug(name)
    if not safe:
        return {
            "ok": False,
            "error": "name must contain letters, digits, spaces, _ or -",
            "given": name,
        }

    args: dict[str, Any] = {}
    if person:
        args["person"] = person
    live = bridge.call("get_appearance", timeout=45.0, **args)
    data = live.get("data") or {}
    raw = data.get("storables")
    if not isinstance(raw, list) or not raw:
        return {"ok": False, "error": "the plugin returned no appearance storables"}

    kept = [st for st in raw if isinstance(st, dict) and _is_appearance(str(st.get("id") or ""))]
    if not kept:
        return {"ok": False, "error": "no appearance storables survived filtering"}

    vap = {"setUnlistedParamsToDefault": "true", "storables": kept}
    dest = _lib_dir() / f"Preset_{safe}.vap"
    dest.write_text(json.dumps(vap, ensure_ascii=False, indent=2), encoding="utf-8")
    rel = f"{LIB_REL}/Preset_{safe}.vap"

    entry: dict[str, Any] = {
        "name": safe,
        "description": description,
        "file": rel,
        "saved": datetime.now(timezone.utc).astimezone().isoformat(timespec="seconds"),
        "storableCount": len(kept),
    }
    entry.update(_summarise(kept))

    index = _read_index()
    index["characters"] = [c for c in index["characters"]
                           if str(c.get("name", "")).lower() != safe.lower()]
    index["characters"].append(entry)
    index["characters"].sort(key=lambda c: str(c.get("name", "")).lower())
    _write_index(index)

    return {
        "ok": True,
        "name": safe,
        "path": rel,
        "absolute": str(dest),
        "droppedStorables": len(raw) - len(kept),
        "entry": entry,
        "hint": f'Reload it any time with load_character("{safe}").',
    }


def list_characters(query: str = "") -> dict[str, Any]:
    index = _read_index()
    rows = index["characters"]
    q = (query or "").strip().lower()
    if q:
        rows = [c for c in rows
                if q in str(c.get("name", "")).lower()
                or q in str(c.get("description", "")).lower()]

    # A preset someone dropped in by hand, or saved before the index existed.
    known = {str(c.get("name", "")).lower() for c in index["characters"]}
    orphans = []
    for path in sorted(_lib_dir().glob("Preset_*.vap")):
        stem = path.stem[len("Preset_"):]
        if stem.lower() not in known:
            orphans.append({"name": stem, "file": f"{LIB_REL}/{path.name}"})

    return {
        "count": len(rows),
        "library": LIB_REL,
        "characters": rows,
        "unindexed": orphans,
    }


def resolve_character(name: str) -> dict[str, Any]:
    wanted = (name or "").strip().lower()
    if not wanted:
        return {"ok": False, "error": "empty character name"}
    for c in _read_index()["characters"]:
        if str(c.get("name", "")).lower() == wanted:
            return {"ok": True, "name": c.get("name"), "path": c.get("file"), "entry": c}
    # Fall back to the files themselves so a hand-placed preset still loads.
    for path in _lib_dir().glob("Preset_*.vap"):
        if path.stem[len("Preset_"):].lower() == wanted:
            return {"ok": True, "name": path.stem[len("Preset_"):],
                    "path": f"{LIB_REL}/{path.name}"}
    have = [c.get("name") for c in _read_index()["characters"]]
    return {
        "ok": False,
        "error": f"no character named {name!r} in the library",
        "available": have,
    }


def _has_plugin_action(suffix: str, action: str, person: str = "") -> bool | None:
    """Is `action` on the storable whose id ends in `suffix`?

    True when list_actions named it, False when the atom answered and does not
    have it, and None when the question could not be asked at all (a bridge too
    old to know list_actions). The caller treats None as "assume it is there",
    so an old bridge keeps its previous unconditional behaviour instead of
    silently losing the reset.

    The query is filtered server-side against the real storable id, so the
    "plugin#<n>_" index a plugin happens to get does not matter. Asking the
    atom this way is far cheaper than get_appearance, which dumps every
    storable's JSON to answer a yes/no question.
    """
    args: dict[str, Any] = {"query": suffix}
    if person:
        args["person"] = person
    try:
        data = bridge.call("list_actions", timeout=30.0, **args).get("data") or {}
    except Exception:
        return None
    want = action.lower()
    for row in data.get("actions") or []:
        if str(row.get("action") or "").lower() != want:
            continue
        if suffix in str(row.get("storable") or "").lower():
            return True
    return False


def _reset_appearance_plugins(person: str = "") -> list[str]:
    """Clear additive appearance plugins so a load does not stack on the last.

    Only plugins that are actually on the atom are asked. DecalMaker's reset is
    the one that matters, and a character that carries no DecalMaker - which is
    most of them - used to be sent two calls that were certain to fail: the
    plugin threw "storable not found", the bridge logged a stack trace, and the
    load carried on. Two guaranteed failures per load, for nothing.
    """
    done: list[str] = []
    for suffix, action in _APPEARANCE_PLUGINS.items():
        if not action:
            continue
        if _has_plugin_action(suffix, action, person) is False:
            continue
        args: dict[str, Any] = {"storable": suffix, "action": action}
        if person:
            args["person"] = person
        try:
            bridge.call("call_action", timeout=30.0, **args)
            done.append(f"{suffix} -> {action}")
        except Exception:
            # The plugin is not loaded on this Person after all; that is fine.
            continue
    return done


def _needs_second_pass(storable: dict[str, Any]) -> bool:
    """Storables that belong to an asynchronously loaded hair or clothing item.

    The plugin restores a preset in one pass. Applying `geometry` starts the
    hair and clothing loads, so a storable owned by one of those items does not
    exist yet when the same pass reaches it, and gets skipped without a word.
    Hair colour lives on exactly such a storable, which is why a character
    loaded into a fresh VAM came back with default hair.
    """
    sid = str(storable.get("id") or "").lower()
    if not sid:
        return False
    if sid.endswith("sim") or "material" in sid or "scalp" in sid:
        return True
    # Plugin storables are deliberately excluded: an additive one would apply
    # its whole payload a second time. They come through on the first pass.
    if sid.startswith("plugin"):
        return False
    return any("olor" in key for key in storable if key != "id")


def load_character(name: str, person: str = "") -> dict[str, Any]:
    found = resolve_character(name)
    if not found.get("ok"):
        return found
    args: dict[str, Any] = {"path": found["path"]}
    if person:
        args["person"] = person

    reset = _reset_appearance_plugins(person)
    result = bridge.call("load_look", timeout=60.0, **args)
    data = result.get("data") or result
    if not isinstance(data, dict):
        data = {"data": data}
    data["character"] = found["name"]
    if reset:
        data["resetPlugins"] = reset
    if found.get("entry"):
        data["entry"] = found["entry"]

    # Second pass: now that the items exist, apply their materials again.
    try:
        doc = json.loads((vam_root() / found["path"].replace("/", "\\")).read_text(encoding="utf-8"))
        subset = [st for st in doc.get("storables") or []
                  if isinstance(st, dict) and _needs_second_pass(st)]
    except (OSError, json.JSONDecodeError, KeyError):
        subset = []
    if subset:
        time.sleep(3.0)
        tmp = _lib_dir() / "_restore.vap"
        tmp.write_text(json.dumps(
            {"setUnlistedParamsToDefault": "false", "storables": subset},
            ensure_ascii=False), encoding="utf-8")
        again: dict[str, Any] = {"path": f"{LIB_REL}/_restore.vap"}
        if person:
            again["person"] = person
        try:
            bridge.call("load_look", timeout=60.0, **again)
            data["secondPass"] = [st.get("id") for st in subset]
        except Exception as exc:
            data["secondPassError"] = str(exc)
    return data
