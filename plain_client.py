#!/usr/bin/env python3

import socket
import ssl
from random import randrange
from multiprocessing import Process

HOST = '127.0.0.1'
PORT = 5005
WORKERS = 200
ITERATIONS_PER_WORKER = 200
SOCKET_TIMEOUT = 5

def worker():
    sock = socket.create_connection((HOST, PORT))

    for i in range(0, ITERATIONS_PER_WORKER):
        msg = random_message()
        sock.send(msg)

        # Read our message back. Never send more than one message at a time per
        # connection.
        buf = sock.recv(len(msg))

        if buf == msg:
            print("Server successfully echoed %d byte message" % (len(msg)))
        else:
            print("Wrong response for %d byte message" % (len(msg)))

    sock.close()

def random_message():
    arr = []
    for i in range(0, randrange(16, 512)):
        arr.append(randrange(0, 255))

    return bytes(arr)

if __name__ == '__main__':
    workers = []
    for i in range(0, WORKERS):
        workers.append(Process(target=worker))

    for w in workers:
        w.start()

    for w in workers:
        w.join()
