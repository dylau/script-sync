import socket, sys

HOST = "127.0.0.1"
PORT = 58258
script_path = sys.argv[1]
sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
sock.connect((HOST, PORT))
sock.send(script_path.encode("utf-8"))
sock.close()
print(f"Sent to Rhino: {script_path}")
