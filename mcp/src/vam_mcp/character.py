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
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from . import bridge
from .paths import vam_root

LIB_REL = "Custom/Atom/Person/Appearance/VamMcp"
INDEX_NAME = "characters.json"

# Storables worth keeping in a look. "Sim" is the sim-hair storable that holds
# rootColor / tipColor, and it is the reason a naive name filter loses hair
# colour. Materials cover skin, eyes and scalp.
_KEEP_EXACT = {"geometry", "skin", "eyes", "rescaleobject"}
_KEEP_SUBSTRINGS = ("material", "hair", "clothing", "scalp")


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
    if "control" in low or low.startswith("plugin"):
        return False
    if low in _KEEP_EXACT:
        return True
    if low.endswith("sim"):
        return True
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


def load_character(name: str, person: str = "") -> dict[str, Any]:
    found = resolve_character(name)
    if not found.get("ok"):
        return found
    args: dict[str, Any] = {"path": found["path"]}
    if person:
        args["person"] = person
    result = bridge.call("load_look", timeout=60.0, **args)
    data = result.get("data") or result
    if not isinstance(data, dict):
        data = {"data": data}
    data["character"] = found["name"]
    if found.get("entry"):
        data["entry"] = found["entry"]
    return data
