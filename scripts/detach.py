#!/usr/bin/env python3
"""
Launch a command fully detached from this shell.

The shell tool kills its process group when a command finishes, which takes down
anything started with a plain `&` or even `nohup &`. Double-forking puts the
child in its own session, so it survives and keeps running for the user to test
against.

Usage: python3 scripts/detach.py <logfile> <command...>
"""
import os
import sys

if len(sys.argv) < 3:
    sys.exit("usage: detach.py <logfile> <command...>")

logfile = sys.argv[1]
command = sys.argv[2:]

pid = os.fork()
if pid > 0:
    # Original process: exit immediately without waiting on the child.
    os._exit(0)

os.setsid()  # new session, detached from the controlling terminal/process group

pid = os.fork()
if pid > 0:
    os._exit(0)  # session leader exits; grandchild is reparented to init

with open(logfile, "ab", buffering=0) as log:
    os.dup2(log.fileno(), 1)
    os.dup2(log.fileno(), 2)
    devnull = os.open(os.devnull, os.O_RDONLY)
    os.dup2(devnull, 0)
    os.execvp(command[0], command)

os._exit(127)
