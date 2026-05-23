#!/usr/bin/env python3
"""
ArayCode — Python TTS Long-Running Service
Entry point. All logic is in sibling modules.
"""

import sys

# Force UTF-8 on stdout so Cyrillic in JSON responses never hits cp1252
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8")

from service import run

if __name__ == "__main__":
    run()
