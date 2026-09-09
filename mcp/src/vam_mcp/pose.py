"""Load a pose without letting it repaint the character.

Plenty of presets filed as poses are not pose-only. vamX's `_POSE LIBRARY`
entries, for instance, carry a `geometry` storable, so restoring one wholesale
replaces the character, hair and clothing - it silently wiped a built character
once, which is why this filter exists. Only the controllers that make up a pose
are kept; appearance is left alone unless the caller explicitly asks for it.
"""

from __future__ import annotations

import json
import zipfile
from pathlib import Path
from typing import Any

from . import bridge
from .paths import vam_root

TEMP_REL = "Custom/Atom/Person/Pose/Preset_VamMcp_PoseOnly.vap"
LOOK_TEMP_REL = "Custom/Atom/Person/Appearance/Preset_VamMcp_LookNoPose.vap"

# Excluding appearance is the right way round here: a pose file's remaining
# content is pose data. Keeping only "*Control" ids looked tidy but threw away
# the joint storables - hip, chest, rThigh and the rest - which is most of the
# pose, and left the body half-posed.
_NOT_POSE_EXACT = {"geometry", "skin", "eyes", "rescaleobject"}
_NOT_POSE_TOKENS = ("material", "scalp", "clothing")
# A worn item registers its own storables, and several of them end in "Control"
# without being controllers: WrapControl holds surfaceOffset and smoothing,
# ItemControl holds the fit flags. Dropping those from a look leaves a garment
# fitted with defaults instead of the creator's numbers.
_ITEM_CONTROL_SUFFIXES = ("wrapcontrol", "itemcontrol", "itemdeleter", "itemreloader")


def is_pose_storable(sid: str) -> bool:
    low = (sid or "").lower()
    if not low:
        return False
    if low.startswith("plugin"):
        return False
    # a clothing item's own fit storables are not controllers
    if low.endswith(_ITEM_CONTROL_SUFFIXES):
        return False
    # controllers are pose even when their name mentions hair
    if low.endswith("control"):
        return True
    if low in _NOT_POSE_EXACT or low.endswith("presets") or low.endswith("sim"):
        return False
    if "hair" in low or any(t in low for t in _NOT_POSE_TOKENS):
        return False
    return True


# The mirror of the filter above, and deliberately narrower. Most look presets
# carry storables whose name ends in "Control" that are appearance, not pose:
# BreastControl and GluteControl hold soft-body settings, EyelidControl,
# JawControl and TongueControl hold face setup, the finger controls hold hand
# shape. Stripping those from a look would lose real appearance data. Only what
# positions the skeleton counts as pose here.
_NOT_BODY = (
    "breast", "glute", "eyelid", "jaw", "tongue", "finger", "hair",
    "nipple", "testes", "penis", "eyetarget", "pupil", "lip", "brow",
)
_BONES = {
    "hip", "pelvis", "chest", "abdomen", "abdomen2", "head", "neck",
    "rthigh", "lthigh", "rshin", "lshin", "rfoot", "lfoot", "rtoe", "ltoe",
    "rhand", "lhand", "rforearm", "lforearm", "rshldr", "lshldr",
    "rcollar", "lcollar", "rpectoral", "lpectoral", "lglute", "rglute",
}


def is_body_pose_storable(sid: str) -> bool:
    """Does this storable position the skeleton, as opposed to describe a look?"""
    low = (sid or "").lower()
    if not low or low.startswith("plugin"):
        return False
    # Bone names are explicit, so they settle it before the keyword pass:
    # LGlute is a rigidbody transform even though GluteControl is a look setting.
    if low.endswith(_ITEM_CONTROL_SUFFIXES):
        return False
    if low in _BONES:
        return True
    if any(token in low for token in _NOT_BODY):
        return False
    return low.endswith("control")


def read_preset(path: str) -> dict[str, Any]:
    """Read a preset that may live loose on disk or inside a .var package."""
    root = vam_root()
    if ":/" in path:
        package, inner = path.split(":/", 1)
        var = next((root / "AddonPackages").rglob(package + ".var"), None)
        if var is None:
            raise FileNotFoundError("package not found: " + package)
        with zipfile.ZipFile(var) as zf:
            raw = zf.read(inner.replace("\\", "/"))
        return json.loads(raw.decode("utf-8", "ignore"))
    return json.loads((root / path.replace("/", "\\")).read_text(encoding="utf-8", errors="ignore"))


def load_pose(path: str, person: str = "", include_appearance: bool = False) -> dict[str, Any]:
    args: dict[str, Any] = {}
    if person:
        args["person"] = person

    if include_appearance:
        result = bridge.call("load_pose", timeout=45.0, path=path, **args)
        data = result.get("data") or result
        if isinstance(data, dict):
            data["filtered"] = False
        return data

    try:
        doc = read_preset(path)
    except (OSError, KeyError, ValueError, FileNotFoundError, zipfile.BadZipFile) as exc:
        return {
            "ok": False,
            "error": f"could not read the pose preset: {exc}",
            "path": path,
            "hint": "Pass include_appearance=true to hand the file straight to VAM.",
        }

    storables = doc.get("storables")
    if not isinstance(storables, list):
        return {"ok": False, "error": "preset has no storables array", "path": path}

    kept = [st for st in storables
            if isinstance(st, dict) and is_pose_storable(str(st.get("id") or ""))]
    dropped = [str(st.get("id")) for st in storables
               if isinstance(st, dict) and not is_pose_storable(str(st.get("id") or ""))]
    if not kept:
        return {
            "ok": False,
            "error": "nothing pose-like in that preset - it is an appearance file",
            "path": path,
            "dropped": dropped[:20],
        }

    dest = vam_root() / TEMP_REL.replace("/", "\\")
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(
        {"setUnlistedParamsToDefault": "false", "storables": kept},
        ensure_ascii=False), encoding="utf-8")

    result = bridge.call("load_pose", timeout=45.0, path=TEMP_REL, **args)
    data = result.get("data") or result
    if not isinstance(data, dict):
        data = {"data": data}
    data["source"] = path
    data["filtered"] = True
    data["appliedStorables"] = len(kept)
    data["droppedStorables"] = len(dropped)
    if dropped:
        data["dropped"] = dropped[:20]
        data["note"] = ("That preset also carried appearance storables; they were left out so "
                        "the character keeps its look. Pass include_appearance=true to apply them.")
    return data

def _apply_subset(path: str, keep, op: str, temp_rel: str,
                  person: str = "", drop_note: str = "") -> dict[str, Any]:
    """Write the storables `keep` accepts to a temp preset and load that."""
    try:
        doc = read_preset(path)
    except (OSError, KeyError, ValueError, FileNotFoundError, zipfile.BadZipFile) as exc:
        return {"ok": False, "error": f"could not read the preset: {exc}", "path": path}

    storables = doc.get("storables")
    if not isinstance(storables, list):
        return {"ok": False, "error": "preset has no storables array", "path": path}

    kept = [st for st in storables if isinstance(st, dict) and keep(str(st.get("id") or ""))]
    dropped = [str(st.get("id")) for st in storables
               if isinstance(st, dict) and not keep(str(st.get("id") or ""))]
    if not kept:
        return {"ok": False, "error": "nothing left after filtering that preset",
                "path": path, "dropped": dropped[:20]}

    dest = vam_root() / temp_rel.replace("/", "\\")
    dest.parent.mkdir(parents=True, exist_ok=True)
    dest.write_text(json.dumps(
        {"setUnlistedParamsToDefault": "false", "storables": kept},
        ensure_ascii=False), encoding="utf-8")

    args: dict[str, Any] = {"path": temp_rel}
    if person:
        args["person"] = person
    result = bridge.call(op, timeout=60.0, **args)
    data = result.get("data") or result
    if not isinstance(data, dict):
        data = {"data": data}
    data["source"] = path
    data["filtered"] = True
    data["appliedStorables"] = len(kept)
    data["droppedStorables"] = len(dropped)
    if dropped:
        data["dropped"] = dropped[:20]
        if drop_note:
            data["note"] = drop_note
    return data


def load_look_keep_pose(path: str, person: str = "") -> dict[str, Any]:
    """Apply a look without letting it move or re-pose the character."""
    return _apply_subset(
        path,
        keep=lambda sid: not is_body_pose_storable(sid),
        op="load_look",
        temp_rel=LOOK_TEMP_REL,
        person=person,
        drop_note=("Skeleton controllers in that look were left out so the current pose "
                   "survives. Call load_look without keep_pose to take its pose too."),
    )
