#!/usr/bin/env python3
"""
Minimal helper to send a script path to a running Rhino instance.
"""

import argparse
import os
import socket
import sys

from scriptsync_instances import add_common_arguments, resolve_port


def main() -> int:
    parser = argparse.ArgumentParser(description="Send a script to a running Rhino instance.")
    add_common_arguments(parser)
    parser.add_argument("script", help="Path of the script to send.")
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

    try:
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.settimeout(5)
        sock.connect((args.host, port))
        sock.send(script_path.encode("utf-8"))
        sock.close()
        print(f"Sent to Rhino on port {port}: {script_path}")
    except ConnectionRefusedError:
        print(f"ERROR: no Rhino instance listening on port {port}. Run 'ScriptSyncStart' in Rhino first.")
        return 1
    except Exception as e:
        print(f"ERROR: {e}")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main())
