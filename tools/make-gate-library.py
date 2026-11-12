#!/usr/bin/env python3
"""Makes a library of made-up games, with DATs to match, for the Phase 1 gate.

The plan's four systems each test a way of matching: plain cartridges (Game Boy), headered cartridges (NES), discs of
several tracks with a BIOS (PlayStation) and large discs (GameCube). Every game, file and checksum here is made up.

    tools/make-gate-library.py out/

writes out/dats (a DAT per system, in Logiqx XML), out/roms (the library) and out/expected.csv (what each dump should
be: FOUND, INCOMPLETE or MISSING, as igir's report says it). Then scan out/roms with Romdex and run igir on it:

    igir report --dat out/dats --input out/roms --report-output out/igir.csv

and compare the two with tools/compare-igir.py. Needs 7z for the 7-Zip case.
"""

import argparse
import csv
import hashlib
import os
import shutil
import subprocess
import sys
import zipfile
import zlib
from xml.sax.saxutils import quoteattr

VERSION = "20261112-000000"
SECTOR = 2352


def data(name, size):
    """Bytes standing in for a dump: the same for the same name, different for any other."""
    out = bytearray()
    block = hashlib.sha256(name.encode()).digest()
    while len(out) < size:
        out += block
        block = hashlib.sha256(block).digest()
    return bytes(out[:size])


class Dat:
    def __init__(self, name):
        self.name = name
        self.games = []  # (name, cloneof, category, [(file, bytes)])

    def game(self, name, files, cloneof=None, category=None):
        self.games.append((name, cloneof, category, files))
        return files

    def write(self, folder):
        lines = ['<?xml version="1.0"?>', "<datafile>", "  <header>", f"    <name>{self.name}</name>",
                 f"    <description>{self.name}</description>", f"    <version>{VERSION}</version>", "  </header>"]
        for name, cloneof, category, files in self.games:
            clone = f" cloneof={quoteattr(cloneof)}" if cloneof else ""
            lines.append(f"  <game name={quoteattr(name)}{clone}>")
            if category:
                lines.append(f"    <category>{category}</category>")
            lines.append(f"    <description>{name}</description>")
            for file, content in files:
                lines.append(f"    <rom name={quoteattr(file)} size=\"{len(content)}\" crc=\"{zlib.crc32(content):08x}\" "
                             f"md5=\"{hashlib.md5(content).hexdigest()}\" sha1=\"{hashlib.sha1(content).hexdigest()}\"/>")
            lines.append("  </game>")
        lines.append("</datafile>")
        with open(os.path.join(folder, f"{self.name}.dat"), "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + "\n")


def cartridge(name, ext, size=32768):
    return [(f"{name}{ext}", data(name, size))]


def disc(name, tracks):
    """A cue sheet and its tracks, each a few sectors long."""
    bins = [(f"{name} (Track {i + 1}).bin" if tracks > 1 else f"{name}.bin", data(f"{name} track {i + 1}", SECTOR * (8 + i)))
            for i in range(tracks)]
    cue = "".join(f'FILE "{b}" BINARY\r\n  TRACK {i + 1:02d} {"MODE2/2352" if i == 0 else "AUDIO"}\r\n    INDEX 01 00:00:00\r\n'
                  for i, (b, _) in enumerate(bins))
    return [(f"{name}.cue", cue.encode())] + bins


def main():
    parser = argparse.ArgumentParser(description="Make a library of made-up games for the Phase 1 gate.")
    parser.add_argument("out", help="the folder to make; emptied first")
    args = parser.parse_args()

    out = args.out
    if os.path.exists(out):
        shutil.rmtree(out)
    dats, roms = os.path.join(out, "dats"), os.path.join(out, "roms")
    os.makedirs(dats)
    expected = []

    def put(path, content):
        path = os.path.join(roms, path)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "wb") as f:
            f.write(content)

    def expect(dat, game, status):
        expected.append((dat.name, game, status))

    # Game Boy: plain cartridges, loose, zipped, 7-Zipped, renamed, and one with a dump's name but other contents.
    gb = Dat("Gate - Game Boy")
    pond = gb.game("Pixel Pond (USA)", cartridge("Pixel Pond (USA)", ".gb"))
    gb.game("Pixel Pond (Europe)", cartridge("Pixel Pond (Europe)", ".gb"), cloneof="Pixel Pond (USA)")
    maze = gb.game("Moss Maze (Japan)", cartridge("Moss Maze (Japan)", ".gb"))
    lantern = gb.game("Lantern Run (World)", cartridge("Lantern Run (World)", ".gb", 65536))
    gb.game("Quiet Quarry (USA)", cartridge("Quiet Quarry (USA)", ".gb"))
    bell = gb.game("Tide Bell (USA) (Rev 1)", cartridge("Tide Bell (USA) (Rev 1)", ".gb"))
    gb.game("Paper Kite (Europe) (Demo)", cartridge("Paper Kite (Europe) (Demo)", ".gb", 16384))
    put("Game Boy/Pixel Pond (USA).gb", pond[0][1])
    with zipfile.ZipFile(os.path.join(roms, "Game Boy", "Moss Maze (Japan).zip"), "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr(maze[0][0], maze[0][1])
    put(f"Game Boy/staging/{lantern[0][0]}", lantern[0][1])
    subprocess.run(["7z", "a", "-bso0", "-bsp0", os.path.join(roms, "Game Boy", "Lantern Run (World).7z"),
                    os.path.join(roms, "Game Boy", "staging", lantern[0][0])], check=True)
    shutil.rmtree(os.path.join(roms, "Game Boy", "staging"))
    put("Game Boy/Quiet Quarry (USA).gb", data("not the real thing", 32768))
    put("Game Boy/tidebell.gb", bell[0][1])
    put("Game Boy/notes/homebrew.gb", data("someone's own game", 8192))
    for game, status in [("Pixel Pond (USA)", "FOUND"), ("Pixel Pond (Europe)", "MISSING"), ("Moss Maze (Japan)", "FOUND"),
                         ("Lantern Run (World)", "FOUND"), ("Quiet Quarry (USA)", "MISSING"), ("Tide Bell (USA) (Rev 1)", "FOUND"),
                         ("Paper Kite (Europe) (Demo)", "MISSING")]:
        expect(gb, game, status)

    # NES: the DAT lists cartridges without the 16-byte iNES header that dumps carry.
    nes = Dat("Gate - NES")
    rabbit = nes.game("Rocket Rabbit (USA)", cartridge("Rocket Rabbit (USA)", ".nes", 40960))
    rabbit_eu = nes.game("Rocket Rabbit (Europe)", cartridge("Rocket Rabbit (Europe)", ".nes", 40960), cloneof="Rocket Rabbit (USA)")
    canal = nes.game("Canal Kings (Japan)", cartridge("Canal Kings (Japan)", ".nes", 24576))
    nes.game("Night Owl (USA)", cartridge("Night Owl (USA)", ".nes", 24576))
    header = b"NES\x1a" + bytes([2, 1, 1, 0]) + bytes(8)
    put("NES/Rocket Rabbit (USA).nes", header + rabbit[0][1])
    with zipfile.ZipFile(os.path.join(roms, "NES", "Rocket Rabbit (Europe).zip"), "w", zipfile.ZIP_DEFLATED) as z:
        z.writestr(rabbit_eu[0][0], header + rabbit_eu[0][1])
    put("NES/Canal Kings (Japan).nes", canal[0][1])
    for game, status in [("Rocket Rabbit (USA)", "FOUND"), ("Rocket Rabbit (Europe)", "FOUND"), ("Canal Kings (Japan)", "FOUND"),
                         ("Night Owl (USA)", "MISSING")]:
        expect(nes, game, status)

    # PlayStation: discs of several tracks, a track two regions share, a disc with a track missing, a game of two discs
    # with one of them here, and the BIOS from a DAT of its own.
    ps = Dat("Gate - PlayStation")
    signal = ps.game("Signal Lost (USA)", disc("Signal Lost (USA)", 2))
    europe = disc("Signal Lost (Europe)", 2)
    # Europe's audio track is the same as America's.
    europe[2] = ("Signal Lost (Europe) (Track 2).bin", signal[2][1])
    ps.game("Signal Lost (Europe)", europe)
    harbor = ps.game("Harbor Lights (Japan)", disc("Harbor Lights (Japan)", 3))
    night1 = ps.game("Long Night (USA) (Disc 1)", disc("Long Night (USA) (Disc 1)", 1))
    ps.game("Long Night (USA) (Disc 2)", disc("Long Night (USA) (Disc 2)", 1))
    for name, content in signal:
        put(f"PlayStation/Signal Lost (USA)/{name}", content)
    for name, content in harbor[:2]:
        put(f"PlayStation/Harbor Lights (Japan)/{name}", content)
    for name, content in night1:
        put(f"PlayStation/Long Night (USA)/{name}", content)
    for game, status in [("Signal Lost (USA)", "FOUND"), ("Signal Lost (Europe)", "INCOMPLETE"), ("Harbor Lights (Japan)", "INCOMPLETE"),
                         ("Long Night (USA) (Disc 1)", "FOUND"), ("Long Night (USA) (Disc 2)", "MISSING")]:
        expect(ps, game, status)

    bios = Dat("Gate - PlayStation - BIOS Images")
    firmware = bios.game("Gate BIOS (USA) (v1.0)", [("Gate BIOS (USA) (v1.0).bin", data("gate bios", 131072))])
    bios.game("Gate BIOS (Japan) (v1.0)", [("Gate BIOS (Japan) (v1.0).bin", data("gate bios japan", 131072))])
    put(f"PlayStation/bios/{firmware[0][0]}", firmware[0][1])
    expect(bios, "Gate BIOS (USA) (v1.0)", "FOUND")
    expect(bios, "Gate BIOS (Japan) (v1.0)", "MISSING")

    # GameCube: one large disc image each. Kept small here; size makes no difference to matching.
    gc = Dat("Gate - GameCube")
    glass = gc.game("Glass Garden (USA)", [("Glass Garden (USA).iso", data("Glass Garden (USA)", 1 << 20))])
    gc.game("Glass Garden (Europe) (En,Fr,De)", [("Glass Garden (Europe) (En,Fr,De).iso", data("Glass Garden (Europe)", 1 << 20))],
            cloneof="Glass Garden (USA)")
    gc.game("Ember Isle (Japan)", [("Ember Isle (Japan).iso", data("Ember Isle (Japan)", 1 << 20))])
    put(f"GameCube/{glass[0][0]}", glass[0][1])
    for game, status in [("Glass Garden (USA)", "FOUND"), ("Glass Garden (Europe) (En,Fr,De)", "MISSING"), ("Ember Isle (Japan)", "MISSING")]:
        expect(gc, game, status)

    for dat in (gb, nes, ps, bios, gc):
        dat.write(dats)
    with open(os.path.join(out, "expected.csv"), "w", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        writer.writerow(["DAT Name", "Game Name", "Status"])
        writer.writerows(expected)
    print(f"{len(expected)} dumps in {len([gb, nes, ps, bios, gc])} DATs, library in {roms}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
