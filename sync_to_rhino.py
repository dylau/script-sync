#!/usr/bin/env python3
"""Send a Python/C# script to Rhino for execution (same as F4 in VSCode)."""

import socket
import sys
import os

HOST = "127.0.0.1"
PORT = 58258

if len(sys.argv) < 2:
    print(f"Usage: python {sys.argv[0]} <script_path>")
    sys.exit(1)

script_path = os.path.abspath(sys.argv[1])

try:
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.settimeout(5)
    sock.connect((HOST, PORT))
    sock.send(script_path.encode("utf-8"))
    sock.close()
    print(f"Sent to Rhino: {script_path}")
except ConnectionRefusedError:
    print("ERROR: Rhino not listening. Run 'ScriptSyncStart' in Rhino first.")
except Exception as e:
    print(f"ERROR: {e}")
