#!/usr/bin/env python3
"""
Send a Python script file path to Rhino 8 for execution via TCP socket.

Rhino must be listening: run 'ScriptSyncStart' in Rhino first.
If no file is given, the last sent file is used.

After sending, polls for <script>.py.error to report traceback.
"""

import socket
import sys
import os
import time

HOST = "192.168.192.1"  # Windows host IP
PORT = 58258
POLL_SEC = 0.3  # interval between error-file checks
TIMEOUT = 30  # max seconds to wait for error file

SKILL_DIR = os.path.dirname(os.path.abspath(__file__))
LAST_FILE = os.path.join(SKILL_DIR, ".last_file")


def read_last():
    if os.path.exists(LAST_FILE):
        with open(LAST_FILE) as f:
            return f.read().strip()
    return None


def write_last(path):
    with open(LAST_FILE, "w") as f:
        f.write(path)


# -- resolve script path -------------------------------------------------------
if len(sys.argv) >= 2:
    script_path = os.path.abspath(sys.argv[1])
else:
    script_path = read_last()
    if not script_path:
        print("ERROR: no file given and no last file on record.")
        sys.exit(1)
    print(f"No file given — using last: {script_path}")

if not os.path.exists(script_path):
    print(f"ERROR: file not found: {script_path}")
    sys.exit(1)

error_file = script_path + ".error"

# -- clear stale error file ----------------------------------------------------
if os.path.exists(error_file):
    os.remove(error_file)

# -- send to Rhino -------------------------------------------------------------
try:
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.settimeout(5)
    sock.connect((HOST, PORT))
    sock.send(script_path.encode("utf-8"))
    sock.close()
    write_last(script_path)
    print(f"Sent to Rhino: {script_path}")
except ConnectionRefusedError:
    print("ERROR: Rhino not listening. Run 'ScriptSyncStart' in Rhino first.")
    sys.exit(1)
except Exception as e:
    print(f"ERROR: {e}")
    sys.exit(1)

# -- poll for error file -------------------------------------------------------
print("Waiting for Rhino to finish execution...")
deadline = time.time() + TIMEOUT
while time.time() < deadline:
    time.sleep(POLL_SEC)
    if os.path.exists(error_file):
        with open(error_file) as f:
            content = f.read().strip()
        if content:
            print(f"\n--- Rhino traceback ({os.path.basename(error_file)}) ---")
            print(content)
            print("---")
            sys.exit(1)
        else:
            print("OK: script finished with no errors.")
            sys.exit(0)

print(
    "TIMEOUT: no error file produced within the wait window — script may still be running or ran successfully."
)
sys.exit(2)
