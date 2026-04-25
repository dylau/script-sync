import socket, sys, os, time

HOST = "127.0.0.1"
PORT = 58258
POLL_SEC = 0.3
TIMEOUT = 30
script_path = sys.argv[1]
error_file = script_path + ".py.error"
if os.path.exists(error_file):
    os.remove(error_file)
sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
sock.connect((HOST, PORT))
sock.send(script_path.encode("utf-8"))
sock.close()
print(f"Sent to Rhino: {script_path}")
deadline = time.time() + TIMEOUT
while time.time() < deadline:
    time.sleep(POLL_SEC)
    if os.path.exists(error_file):
        with open(error_file) as f:
            content = f.read().strip()
        if content:
            print(f"--- Rhino traceback ---")
            print(content)
            print("---")
            sys.exit(1)
        else:
            print("OK: script finished with no errors.")
            sys.exit(0)
print("TIMEOUT: no error file produced.")
sys.exit(2)
