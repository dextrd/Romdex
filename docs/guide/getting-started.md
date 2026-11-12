# Getting started

## Setting up

The first time you open Romdex, it walks you through setting up, full screen, before anything else. The numbered
circles along the bottom show where you are. Once the admin account is made, any step can be gone back to or skipped,
and everything in it can be changed later under **Settings**. Until the admin finishes, nobody else gets in.

1. **People.** First the admin account. The admin may do everything, and only they can change their own account.
   Then add anyone else who'll use Romdex, with only the permissions they need. Everyone signs in, even at home; tick
   **Remember me** to stay signed in on that browser for 30 days.
2. **Games.** Romdex checks it can read your ROM library and counts what's there, then counts the DATs in your DAT
   folder. If either is empty, check `ROMS_PATH` and `DATS_PATH`.
3. **Plugins.** The DAT folder catalogue is set up already. Test it to see which systems it found. See
   [Plugins](plugins.md).
4. **Apps and webhooks.** API keys for other apps, and webhooks to tell them when things happen. Most people can skip
   this.
5. **Look.** How Romdex looks for everyone. Each person can choose their own later, under **Settings → Appearance**.

## Where DATs come from

A DAT lists every verified dump of one system, with the size and checksums of each file. That's how Romdex knows what
a file is without trusting its name.

- **No-Intro** covers cartridge systems, such as the Game Boy and the SNES. Download its DATs from
  [DAT-o-MATIC](https://datomatic.no-intro.org).
- **Redump** covers disc systems, such as the PlayStation and the GameCube. Download its DATs from
  [redump.info](https://redump.info).

Drop them into your DAT folder as they come, zipped or not. When there are two DATs for the same system, Romdex uses
the newest. Redump's can be fetched for you instead: see [Plugins](plugins.md).

## Your library

**Library**, the home page, has two views. **Systems** lists every system you have games for; **Games** lists every
game in the library together, to filter and search. **Update catalogue** reads the DATs again (Romdex also does this
by itself once a day), and **Scan library** reads your library. A scan hashes each file once; later scans only hash
what's new or changed.

A file counts only when its checksums match a dump in the DAT, whatever it's called. Files can be loose, zipped,
7-Zipped or in CHDs. A disc counts when every track does, and a game of several discs when every disc does.

- **Verified**: every file of a dump of the game matches the DAT.
- **Partial**: you have some discs of it, but not all.
- **Bad**: you have files of it that don't verify, such as a disc with a track missing, or a file named like a dump
  but with other checksums.

A game's page shows the dump you have, with each file's checksums and where it is, and the other dumps the DAT lists
for it, such as other regions and revisions.

**Search** finds any game the DATs list, whether or not you have it. Words can come in any order, and regions and
versions count too, so *kart japan* finds the Japanese releases of a kart game. A game's page shows its dumps with
their checksums.

**Review** lists the files that match nothing, and any that couldn't be read.

Reading inside a CHD needs `chdman`, and inside an RVZ needs `dolphin-tool`. Until they're part of Romdex's
containers, such files are listed under Review as unreadable.

Next: [Users and permissions](users-and-permissions.md).
