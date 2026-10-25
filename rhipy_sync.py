#!/usr/bin/env python3
"""
Send a Python script to Rhino and wait for its outcome.

Rhino must be listening: run 'ScriptSyncStart' in Rhino first.
After sending, the script's '<path>.py.error' file is polled: the Rhino plug-in
writes the traceback there when the script fails and removes the file otherwise.

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

POLL_SEC = 0.3
TIMEOUT = 30


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Send a script to a running Rhino instance and wait for the result."
    )
    add_common_arguments(parser)
    parser.add_argument("script", help="Path of the script to send.")
    parser.add_argument(
        "--timeout",
        type=float,
        default=TIMEOUT,
        help=f"Max seconds to wait for the error file (default: {TIMEOUT}).",
    )
    args = parser.parse_args()

    script_path = os.path.abspath(args.script)
    if not os.path.exists(script_path):
        print(f"ERROR: file not found: {script_path}")
        return 1

    try:
        port = resolve_port(args.port, args.instance, args.host)
    except SystemExit as error:
        print(error)
        return 1

    error_file = script_path + ".py.error"

    # Clear a stale error file so that we do not report the previous run.
    if os.path.exists(error_file):
        os.remove(error_file)

    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5)
        sock.connect((args.host, port))
        sock.send(script_path.encode("utf-8"))
        # The Rhino plug-in answers with a JSON payload once the script has run.
        # Reading it is the reliable way to know the outcome: on success the
        # plug-in deletes the error file instead of creating an empty one.
        sock.settimeout(args.timeout)
        response = sock.recv(4096).decode("utf-8", errors="replace")
        sock.close()
        print(f"Sent to Rhino on port {port}: {script_path}")
    except ConnectionRefusedError:
        print(f"ERROR: no Rhino instance listening on port {port}. Run 'ScriptSyncStart' in Rhino first.")
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
        print("--- Rhino traceback ---")
        print(error)
        print("---")
        return 1

    if isinstance(parsed, dict) and parsed.get("success") is True:
        print("OK: script finished with no errors.")
        return 0

    # The response was not understood: fall back to the error file.
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
                print("--- Rhino traceback ---")
                print(content)
                print("---")
                return 1
            print("OK: script finished with no errors.")
            return 0

    print("TIMEOUT: no error file produced.")
    return 2


if __name__ == "__main__":
    sys.exit(main())
