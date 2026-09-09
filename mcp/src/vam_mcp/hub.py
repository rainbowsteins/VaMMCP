"""Drive VAM's built-in Hub browser.

VAM does the networking, with the user's own Hub session and their own
`enableHubDownloader` preference. Nothing here talks to hub.virtamate.com; the
plugin only sets HubBrowse's filters and reads back the result cards.

Downloads are two-step on purpose: `search_hub` then `download_resource` with
confirm=False returns a manifest with file sizes, and only a second call with
confirm=True actually pulls anything.
"""

from __future__ import annotations

import time
from typing import Any

from . import bridge

# Sort choices VAM offers; the plugin matches these loosely against the real
# chooser values, so a near-miss still lands on the right one.
POPULAR = "download"


def _mb(n: Any) -> str:
    try:
        v = int(n)
    except (TypeError, ValueError):
        return "?"
    if v <= 0:
        return "?"
    if v < 1024 * 1024:
        return "%.0f KB" % (v / 1024.0)
    return "%.1f MB" % (v / 1024.0 / 1024.0)


def _flag(row: dict[str, Any], key: str) -> bool:
    return str(row.get(key, "")).lower() == "true"


def hub_info() -> dict[str, Any]:
    """Hub state plus the real chooser names and their allowed values."""
    return bridge.call("hub_info", timeout=25.0).get("data") or {}


def search_hub(
    query: str,
    category: str = "",
    pay_type: str = "Free",
    sort: str = POPULAR,
    creator: str = "",
    limit: int = 20,
    hide_installed: bool = False,
    timeout: float = 60.0,
) -> dict[str, Any]:
    """Search the Hub and return cards ranked by download count."""
    result = bridge.call(
        "hub_search",
        timeout=timeout,
        query=query,
        category=category,
        payType=pay_type,
        sort=sort,
        creator=creator,
        limit=str(max(1, limit)),
        waitFor=str(int(timeout - 10)),
    )
    data = result.get("data") or {}
    rows = data.get("results") or []

    out = []
    for r in rows:
        if hide_installed and _flag(r, "inLibrary"):
            continue
        out.append(
            {
                "resourceId": r.get("resourceId"),
                "title": r.get("title"),
                "creator": r.get("creator"),
                "category": r.get("category"),
                "payType": r.get("payType"),
                "downloads": int(r.get("downloads") or 0),
                "rating": r.get("rating"),
                "updated": r.get("updated"),
                "installed": _flag(r, "inLibrary"),
                "downloadable": _flag(r, "hubDownloadable"),
                "updateAvailable": _flag(r, "updateAvailable"),
                "dependencies": r.get("dependencies"),
                "tagLine": r.get("tagLine"),
            }
        )
    data["results"] = out
    data["shown"] = len(out)
    return data


def download_resource(
    resource_id: str,
    confirm: bool = False,
    timeout: float = 90.0,
) -> dict[str, Any]:
    """List a resource's packages, and with confirm=True start the downloads.

    A resource is usually several packages: the resource itself plus its
    dependencies. The manifest says which are already installed and how big the
    rest are, so the size is known before anything is fetched.
    """
    result = bridge.call(
        "hub_download",
        timeout=timeout,
        resourceId=str(resource_id),
        confirm="true" if confirm else "false",
        waitFor=str(int(timeout - 20)),
    )
    data = result.get("data") or {}
    pkgs = data.get("packages") or []

    rows = []
    total = 0
    for p in pkgs:
        size = int(p.get("fileSize") or 0)
        needs = _flag(p, "needsDownload")
        if needs and _flag(p, "canBeDownloaded"):
            total += size
        rows.append(
            {
                "name": p.get("name"),
                "creator": p.get("creator"),
                "license": p.get("license"),
                "size": _mb(size),
                "bytes": size,
                "installed": _flag(p, "alreadyHave"),
                "needsDownload": needs,
                "canDownload": _flag(p, "canBeDownloaded"),
                "isDependency": _flag(p, "isDependency"),
                "notOnHub": _flag(p, "notOnHub"),
                "started": _flag(p, "started"),
            }
        )
    data["packages"] = rows
    data["toDownload"] = sum(1 for r in rows if r["needsDownload"] and r["canDownload"])
    data["totalSize"] = _mb(total)
    data["blocked"] = [r["name"] for r in rows if r["needsDownload"] and not r["canDownload"]]
    return data


def download_status() -> dict[str, Any]:
    return bridge.call("hub_status", timeout=25.0).get("data") or {}


def wait_for_downloads(timeout: float = 900.0, poll: float = 5.0) -> dict[str, Any]:
    """Block until VAM's download queue drains, then report what landed.

    A download that never starts would otherwise look identical to one that
    finished, so this reports the last observed queue depth alongside the
    outcome rather than just saying "done".
    """
    deadline = time.time() + timeout
    last: dict[str, Any] = {}
    seen_active = False
    idle = 0
    while time.time() < deadline:
        last = download_status()
        active = str(last.get("isDownloading", "")).lower() == "true"
        pending = int(last.get("pending") or 0)
        if active or pending > 0:
            seen_active = True
            idle = 0
        else:
            idle += 1
            # A short download can finish before the first poll, so idle is not
            # by itself proof that nothing ran - say which case this was.
            if seen_active or idle >= 3:
                last["finished"] = True
                last["observedActive"] = seen_active
                if not seen_active:
                    last["note"] = (
                        "queue was already empty on every poll: the download either "
                        "finished immediately or never started - check the package list"
                    )
                return last
        time.sleep(poll)
    last["finished"] = False
    last["note"] = "still downloading when the wait timed out"
    return last
