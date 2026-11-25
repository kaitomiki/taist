import os
import runpy
import sys

for stream in (sys.stdin, sys.stdout, sys.stderr):
    stream.reconfigure(encoding="utf-8")
script = sys.argv[1]
sys.argv = [script]
sys.path.insert(0, os.path.dirname(script))
runpy.run_path(script, run_name="__main__")
