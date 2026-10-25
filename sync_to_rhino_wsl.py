#!/usr/bin/env python3
"""
Send a Python script file path to Rhino 8 for execution via TCP socket.

Intended to be run from WSL, where the Windows host is reached over the network.

Rhino must be listening: run 'ScriptSyncStart' in Rhino first.
If no file is given, the last sent file is used.

After sending, polls for '<script>.py.error' to report the traceback.

The target Rhino instance is picked automatically when only one is running.
Use --port or --instance to target a specific one.
"""

import argparse
import json
import os
import socket
import sys
import time

from scriptsync_instances import add_common_arguments, resolve_port

# The Windows host, as seen from WSL.
DEFAULT_HOST = "192.168.192.1"
POLL_SEC = 0.3
TIMEOUT = 30

SKILL_DIR = os.path.dirname(os.path.abspath(__file__))
LAST_FILE = os.path.join(SKILL_DIR, ".last_file")


def read_last() -> str | None:
    if os.path.exists(LAST_FILE):
        with open(LAST_FILE, encoding="utf-8") as handle:
            return handle.read().strip()
    return None


def write_last(path: str) -> None:
    with open(LAST_FILE, "w", encoding="utf-8") as handle:
        handle.write(path)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Send a script to a running Rhino instance and wait for the result."
    )
    add_common_arguments(parser)
    parser.add_argument(
        "script",
        nargs="?",
        default=None,
        help="Path of the script to send. Defaults to the last sent file.",
    )
    parser.add_argument(
        "--host",
        type=str,
        default=DEFAULT_HOST,
        help=f"Host of the Rhino instance (default: {DEFAULT_HOST}).",
    )
    parser.add_argument(
        "--timeout",
        type=float,
        default=TIMEOUT,
        help=f"Max seconds to wait for the error file (default: {TIMEOUT}).",
    )
    args = parser.parse_args()

    if args.script:
        script_path = os.path.abspath(args.script)
    else:
        script_path = read_last()
        if not script_path:
            print("ERROR: no file given and no last file on record.")
            return 1
        print(f"No file given — using last: {script_path}")

    if not os.path.exists(script_path):
        print(f"ERROR: file not found: {script_path}")
        return 1

    try:
        port = resolve_port(args.port, args.instance, args.host)
    except SystemExit as error:
        print(error)
        return 1

    error_file = script_path + ".error"

    # Clear a stale error file so that we do not report the previous run.
    if os.path.exists(error_file):
        os.remove(error_file)

    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5)
        sock.connect((args.host, port))
        sock.send(script_path.encode("utf-8"))
        # The Rhino plug-in answers with a JSON payload once the script has run.
        # On success it deletes the error file instead of creating an empty one,
        # so the response is the reliable signal.
        sock.settimeout(args.timeout)
        response = sock.recv(4096).decode("utf-8", errors="replace")
        sock.close()
        write_last(script_path)
        print(f"Sent to Rhino on port {port}: {script_path}")
    except ConnectionRefusedError:
        print(f"ERROR: no Rhino instance listening on {args.host}:{port}. Run 'ScriptSyncStart' in Rhino first.")
        return 1
    except Exception as e:
        print(f"ERROR: {e}")
        return 1

    parsed = None
    try:
        parsed = json.loads(response.strip())
    except ValueError:
        parsed = None

    if isinstance(parsed, dict) and parsed.get("success") is False:
        error = str(parsed.get("error") or "").strip()
        print(f"\n--- Rhino traceback ({os.path.basename(error_file)}) ---")
        print(error)
        print("---")
        return 1

    if isinstance(parsed, dict) and parsed.get("success") is True:
        print("OK: script finished with no errors.")
        return 0

    # The response was not understood: fall back to the error file.
    print("Waiting for Rhino to finish execution...")
    deadline = time.time() + args.timeout
    while time.time() < deadline:
        time.sleep(POLL_SEC)
        if os.path.exists(error_file):
            try:
                with open(error_file, encoding="utf-8", errors="replace") as handle:
                    content = handle.read().strip()
            except OSError:
                content = ""
            if content:
                print(f"\n--- Rhino traceback ({os.path.basename(error_file)}) ---")
                print(content)
                print("---")
                return 1
            print("OK: script finished with no errors.")
            return 0

    print(
        "TIMEOUT: no error file produced within the wait window — "
        "script may still be running or ran successfully."
    )
    return 2


if __name__ == "__main__":
    sys.exit(main())
