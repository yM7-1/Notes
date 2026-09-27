#!/usr/bin/env python3
"""Checks that the localization files expose exactly the same key set.

Run from the repository root: python3 tools/check_i18n.py
"""
import json
import sys

BASE = "src/Runtime/Localization"


def load(name):
    with open(f"{BASE}/{name}", encoding="utf-8") as handle:
        return json.load(handle)


def main():
    zhs = load("zhs.json")
    eng = load("eng.json")
    missing_eng = sorted(set(zhs) - set(eng))
    missing_zhs = sorted(set(eng) - set(zhs))
    if missing_eng or missing_zhs:
        print("!! i18n key mismatch", file=sys.stderr)
        for key in missing_eng:
            print(f"   missing in eng.json: {key}", file=sys.stderr)
        for key in missing_zhs:
            print(f"   missing in zhs.json: {key}", file=sys.stderr)
        return 1
    print(f"    i18n ok ({len(zhs)} keys)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
