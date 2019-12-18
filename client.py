#!/usr/bin/env python3

import socket
import ssl
from random import randrange
from threading import Thread

HOST = '127.0.0.1'
PORT = 5004
WORKERS = 50
SOCKET_TIMEOUT = 15

class Worker(Thread):
    iterations = 200

    def run(self):
        sslctx = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
        sslctx.check_hostname = False
        sslctx.verify_mode = ssl.CERT_NONE

        sock = socket.create_connection((HOST, PORT))
        ssock = sslctx.wrap_socket(sock)
        ssock.settimeout(SOCKET_TIMEOUT)

        for i in range(0, self.iterations):
            msg = self.random_message()
            nw = 0
            while nw < len(msg):
                nw += ssock.send(msg[nw:])

            buf = b''
            while len(buf) < len(msg):
                buf += ssock.read(len(msg) - len(buf))

            if buf == msg:
                print("Server successfully echoed %d byte message" % (len(msg)))
            else:
                print("Wrong response for %d byte message" % (len(msg)))

        ssock.shutdown(socket.SHUT_RDWR)
        sock.close()

    def random_message(self):
        arr = []
        for i in range(0, randrange(16, 512)):
            arr.append(randrange(0, 255))

        return bytes(arr)

if __name__ == '__main__':
    workers = []
    for i in range(0, WORKERS):
        workers.append(Worker())

    for w in workers:
        w.start()

    for w in workers:
        w.join()
