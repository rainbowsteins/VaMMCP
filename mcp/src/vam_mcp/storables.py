"""Reach the storables a preset cannot: buttons, raw flags, and the whole list.

Three gaps this closes.

A button on a storable is a JSONStorableAction, and RestoreFromJSON never
touches one - a .vap cannot press it. DecalMaker's "Clear All Frames" is the
case that matters: its DecalHead array is additive, so every applied makeup
stack appends to the last one and the faint alpha halo each eyeshadow texture
carries builds into a visible rectangle on the cheek. `call_action` presses it.

Some bools only take effect through their real setter. Setting
useFemaleMorphsOnMale by restoring raw JSON skips the morph-library rebuild, so
the morphs never arrive. `set_bool_param` runs the setter instead.

`list_actions` is what makes the other two usable: without it a caller is
guessing at action names. Action names are exact strings and are not
case-folded, so read them off the atom first.

Not exposed here: the bridge's `save_look`. It is superseded by
`get_appearance` (the server decides what counts as appearance and saves it
itself), and pressing it makes VAM raise a file-write permission dialog. It
stays in the bridge only so an older server keeps working.
"""

from __future__ import annotations

from typing import Any

from . import bridge


def _with_person(person: str, **args: Any) -> dict[str, Any]:
    if person:
        args["person"] = person
    return args


def list_actions(query: str = "", person: str = "") -> dict[str, Any]:
    """List the JSONStorableAction buttons on a Person, as storable -> action rows.

    Call this before call_action: action names are exact strings off the atom and
    are not guessed. query filters on either the storable id or the action name.
    The bridge caps the list at 200 rows.
    """
    args = _with_person(person, query=query)
    return bridge.call("list_actions", timeout=30.0, **args).get("data") or {}


def call_action(storable: str, action: str, person: str = "") -> dict[str, Any]:
    """Press one button (a JSONStorableAction) on a Person's storable.

    Needed because a .vap cannot press a button: RestoreFromJSON restores values
    and never fires an action. DecalMaker's "Clear All Frames" is the one that
    matters - DecalHead is additive, so clear it before applying a makeup stack
    or the layers pile up.

    storable is the storable id, and a plugin storable is
    "plugin#<n>_Namespace.Class" where n shifts with load order; the bridge
    remaps that, so the id a preset recorded still resolves. A storable that is
    not on the atom comes back as an error naming it - check list_plugins or
    add_plugin("decalmaker") when that happens.
    """
    args = _with_person(person, storable=storable, action=action)
    return bridge.call("call_action", timeout=30.0, **args).get("data") or {}


def set_bool_param(
    storable: str,
    param: str,
    value: bool = True,
    person: str = "",
) -> dict[str, Any]:
    """Set a bool on a storable through its real setter instead of raw JSON.

    Use when restoring the value is not enough: useFemaleMorphsOnMale has to run
    its setter to rebuild the morph library. Returns the value the storable
    reports afterwards, so a name that silently did nothing is visible.
    """
    args = _with_person(person, storable=storable, param=param, value=value)
    return bridge.call("set_bool_param", timeout=30.0, **args).get("data") or {}


def get_appearance(person: str = "") -> dict[str, Any]:
    """Dump every appearance-like storable on a Person as raw JSON.

    Returns {person, count, skippedCount, storables}, where each entry is that
    storable's own GetJSON() with its id attached. Skeleton controllers
    (FreeControllerV3) are skipped - those are pose - and so is anything with
    "animation" in the id. Plugin storables ARE included, because some of them
    hold appearance; DecalMaker's makeup layers are the case in point.

    This is the read side of a look save, and the way to check that add_plugin
    finished compiling: poll it until the storable you need appears.
    """
    args = _with_person(person)
    return bridge.call("get_appearance", timeout=45.0, **args).get("data") or {}
