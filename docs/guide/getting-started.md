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
the newest.

Next: [Users and permissions](users-and-permissions.md).
