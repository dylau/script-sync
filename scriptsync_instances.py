#!/usr/bin/env python3
"""
Shared helpers to target a specific running Rhino instance.

The Rhino plug-in publishes the instances that listen for scripts in a small JSON
registry (see CsRhino/ScriptSyncInstanceRegistry.cs). This module reads that
registry so that the command line helpers can pick the right Rhino instance.
"""

from __future__ import annotations

import argparse
import json
import os
import sys
from typing import List, Optional

DEFAULT_PORT = 58258
MAX_PORT = 58279


def registry_path() -> str:
    """The full path of the instance registry written by the Rhino plug-in."""
    local_app_data = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~")
    return os.path.join(local_app_data, "script-sync", "instances.json")


def read_instances() -> List[dict]:
    """
    Read every registered instance. Returns an empty list when unavailable.

    The Rhino plug-in serializes the entries with PascalCase property names
    (Pid, Port, Title, DocumentPath, StartedAt), so the same casing is used here.
    """
    path = registry_path()
    try:
        if not os.path.exists(path):
            return []
        with open(path, "r", encoding="utf-8-sig") as handle:
            content = handle.read().strip()
        if not content:
            return []
        data = json.loads(content)
        if not isinstance(data, list):
            return []
        instances = []
        for entry in data:
            if not isinstance(entry, dict):
                continue
            port = entry.get("Port")
            pid = entry.get("Pid")
            if not isinstance(port, int):
                continue
            instances.append(
                {
                    "pid": pid if isinstance(pid, int) else 0,
                    "port": port,
                    "title": entry.get("Title") or "",
                    "documentPath": entry.get("DocumentPath") or "",
                    "startedAt": entry.get("StartedAt") or "",
                }
            )
        return instances
    except (OSError, ValueError):
        # A corrupt or partially written registry is treated as "no instances".
        return []


def is_process_alive(pid: int) -> bool:
    """Check whether a process is still running."""
    if not pid or pid <= 0:
        return False
    try:
        os.kill(pid, 0)
    except OSError:
        return False
    except Exception:
        return False
    return True


def read_live_instances() -> List[dict]:
    """
    The registered instances whose process is still alive.

    The stale entries are also pruned from the file, so that an instance closed
    without running ScriptSyncStop does not linger forever.
    """
    instances = read_instances()
    live = [i for i in instances if is_process_alive(i.get("pid", 0))]
    if len(live) != len(instances):
        try:
            write_instances(live)
        except OSError:
            pass
    return live


def write_instances(instances: List[dict]) -> None:
    """Write the registry back, keeping the format used by the Rhino plug-in."""
    path = registry_path()
    os.makedirs(os.path.dirname(path), exist_ok=True)
    payload = [
        {
            "Pid": i["pid"],
            "Port": i["port"],
            "Title": i.get("title") or "",
            "DocumentPath": i.get("documentPath") or "",
            "StartedAt": i.get("startedAt") or "",
        }
        for i in instances
    ]
    temp = path + ".tmp"
    # utf-8 without the BOM: node's JSON.parse rejects a BOM, and the Rhino
    # plug-in does not write one.
    with open(temp, "w", encoding="utf-8", newline="\n") as handle:
        json.dump(payload, handle, indent=2)
    if os.path.exists(path):
        os.remove(path)
    os.rename(temp, path)


def describe(instance: dict) -> str:
    """A human readable description of an instance."""
    title = instance.get("title") or f"pid {instance.get('pid', '?')}"
    return f"{title} (port {instance.get('port')})"


def find_by_name(instances: List[dict], needle: str) -> List[dict]:
    """Find the instances whose title or document path contains `needle`."""
    needle = needle.lower()
    matches = []
    for instance in instances:
        haystack = " ".join(
            [
                str(instance.get("title") or ""),
                str(instance.get("documentPath") or ""),
            ]
        ).lower()
        if needle in haystack:
            matches.append(instance)
    return matches


def resolve_port(
    port: Optional[int] = None,
    instance: Optional[str] = None,
    host: str = "127.0.0.1",
) -> int:
    """
    Resolve the TCP port of the Rhino instance to talk to.

    Precedence:
      1. an explicit --port
      2. an explicit --instance (matched against the title or the document path)
      3. the only running instance, when there is exactly one
      4. the instance on the default port

    Raises SystemExit when the target cannot be determined unambiguously.
    """
    if port is not None:
        return port

    live = read_live_instances()

    if instance:
        matches = find_by_name(live, instance)
        if not matches:
            available = ", ".join(describe(i) for i in live) or "none"
            raise SystemExit(
                f"ERROR: no running Rhino instance matches '{instance}'. "
                f"Available instances: {available}"
            )
        if len(matches) > 1:
            available = ", ".join(describe(i) for i in matches)
            raise SystemExit(
                f"ERROR: '{instance}' matches several instances: {available}. "
                "Please be more specific or use --port."
            )
        return matches[0]["port"]

    if len(live) == 1:
        return live[0]["port"]

    if len(live) > 1:
        on_default = [i for i in live if i["port"] == DEFAULT_PORT]
        if len(on_default) == 1:
            return on_default[0]["port"]
        available = ", ".join(describe(i) for i in live)
        raise SystemExit(
            f"ERROR: several Rhino instances are running: {available}. "
            "Use --port or --instance to pick one."
        )

    # No instance registered: fall back to the default port so that a Rhino built
    # before the multi-instance feature still works.
    return DEFAULT_PORT


def add_common_arguments(parser: argparse.ArgumentParser) -> None:
    """Add the arguments shared by every helper."""
    parser.add_argument(
        "--port",
        type=int,
        default=None,
        help="TCP port of the Rhino instance to send to.",
    )
    parser.add_argument(
        "--instance",
        type=str,
        default=None,
        help="Name (document title or path) of the Rhino instance to send to.",
    )
    parser.add_argument(
        "--host",
        type=str,
        default="127.0.0.1",
        help="Host of the Rhino instance (default: 127.0.0.1).",
    )


def select_instance_interactively(instances: List[dict]) -> dict:
    """Ask the user which instance to use when several are running."""
    print("Several Rhino instances are running:")
    for index, instance in enumerate(instances, start=1):
        print(f"  {index}) {describe(instance)}")
    while True:
        answer = input("Which instance? [1]: ").strip() or "1"
        try:
            index = int(answer)
        except ValueError:
            print("Please enter a number.")
            continue
        if 1 <= index <= len(instances):
            return instances[index - 1]
        print(f"Please enter a number between 1 and {len(instances)}.")


def main(argv: Optional[List[str]] = None) -> int:
    """Small CLI to list the running Rhino instances."""
    parser = argparse.ArgumentParser(
        description="List the Rhino instances that are listening for scripts."
    )
    add_common_arguments(parser)
    parser.add_argument(
        "--resolve",
        action="store_true",
        help="Print the port that would be used for the given arguments.",
    )
    args = parser.parse_args(argv)

    live = read_live_instances()

    if args.resolve:
        print(resolve_port(args.port, args.instance, args.host))
        return 0

    if not live:
        print("No Rhino instance is listening.")
        return 1

    print(f"{len(live)} Rhino instance(s) listening:")
    for instance in live:
        print(f"  port {instance['port']}  {describe(instance)}")
        if instance.get("documentPath"):
            print(f"      {instance['documentPath']}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
