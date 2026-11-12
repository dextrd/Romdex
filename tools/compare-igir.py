#!/usr/bin/env python3
"""Compares igir's report on a library with Romdex's, DAT by DAT and game by game.

The Phase 1 gate: Romdex agrees with igir about which dumps are in the library. Run igir on the library first,
for example

    igir report --dat ./dats --input ./roms --report-output igir.csv

then point this at that report and at Romdex, with an API key that can read:

    tools/compare-igir.py igir.csv --romdex http://localhost:8788 --api-key rdx_...

or at a report saved from Romdex (GET /api/v1/systems/{id}/report.csv) with --romdex-report. Exits 1 when the two
disagree on any dump, listing each one.
"""

import argparse
import csv
import io
import json
import sys
import urllib.request


def read_report(text):
    """{DAT name: {game name: status}} from a report in igir's shape."""
    dats = {}
    for row in csv.DictReader(io.StringIO(text)):
        dats.setdefault(row["DAT Name"], {})[row["Game Name"]] = row["Status"].upper()
    return dats


def fetch(url, key):
    request = urllib.request.Request(url, headers={"X-Api-Key": key})
    with urllib.request.urlopen(request) as response:
        return response.read().decode("utf-8")


def romdex_reports(base, key, names):
    """Romdex's reports for the systems igir reported on, by name."""
    systems = {s["name"]: s["id"] for s in json.loads(fetch(f"{base}/api/v1/systems", key))}
    dats = {}
    for name in names:
        if name in systems:
            dats.update(read_report(fetch(f"{base}/api/v1/systems/{systems[name]}/report.csv", key)))
    return dats


def main():
    parser = argparse.ArgumentParser(description="Compare igir's report with Romdex's.")
    parser.add_argument("igir", help="igir's CSV report")
    parser.add_argument("--romdex", help="Romdex's address, such as http://localhost:8788")
    parser.add_argument("--api-key", help="a Romdex API key that can read")
    parser.add_argument("--romdex-report", action="append", default=[], help="a report saved from Romdex instead; repeatable")
    parser.add_argument("--show", type=int, default=25, help="how many disagreements to list per DAT (default 25)")
    args = parser.parse_args()

    with open(args.igir, encoding="utf-8-sig") as f:
        igir = read_report(f.read())
    if args.romdex_report:
        romdex = {}
        for path in args.romdex_report:
            with open(path, encoding="utf-8-sig") as f:
                romdex.update(read_report(f.read()))
    elif args.romdex and args.api_key:
        romdex = romdex_reports(args.romdex.rstrip("/"), args.api_key, igir.keys())
    else:
        parser.error("give --romdex and --api-key, or --romdex-report")

    agreed = True
    for dat in sorted(igir):
        theirs = igir[dat]
        ours = romdex.get(dat)
        if ours is None:
            print(f"{dat}: Romdex has no system of this name")
            agreed = False
            continue
        # A game absent from either report counts as missing there.
        names = sorted(set(theirs) | set(ours))
        differ = [(n, theirs.get(n, "MISSING"), ours.get(n, "(not in DAT)")) for n in names if theirs.get(n, "MISSING") != ours.get(n, "MISSING")]
        found = sum(1 for s in theirs.values() if s == "FOUND")
        print(f"{dat}: igir found {found}, Romdex found {sum(1 for s in ours.values() if s == 'FOUND')}, "
              f"{'agree' if not differ else f'{len(differ)} disagree'}")
        for name, a, b in differ[:args.show]:
            print(f"    {name}: igir {a}, Romdex {b}")
        if len(differ) > args.show:
            print(f"    and {len(differ) - args.show} more")
        agreed = agreed and not differ
    return 0 if agreed else 1


if __name__ == "__main__":
    sys.exit(main())
