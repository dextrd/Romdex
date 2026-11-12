# Plugins

Everything Romdex knows about games, and everywhere games come from or go to, is a plugin. Set them up under
**Settings → Plugins**. **Test** on each says whether it's working.

## Catalogue

Index plugins say which systems and dumps exist. Romdex asks them in order; where two have a DAT for the same system,
the first wins.

- **DAT folder** (built in, set up from the start) reads the No-Intro and Redump DATs in your DAT folder.
- **Redump** (built in, off until you add it) downloads Redump's DATs for the systems you list, such as `PSX, GC`,
  and their BIOS DATs, and checks for new ones every week.

## Intake

Intake plugins bring dumps in, as Melodex's retrievers bring music. One can search for a game you found under
**Search** and fetch it, or hand over files that arrived by themselves, such as those dropped into an inbox folder.
Each runs in its own container and speaks a small HTTP contract, so anyone can write one in any language. Every file
is checked by its hash before it's filed, whatever the plugin says it is. Romdex comes with none that fetch commercial
games; fetching and filing arrive in a later version.

## Connected apps

Connectors tell another app, such as RomM, when the library changes. They're optional.

Next: [Configuration](configuration.md).
