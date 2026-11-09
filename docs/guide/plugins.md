# Plugins

Everything Romdex knows about games, and everywhere games come from or go to, is a plugin. Set them up under
**Settings → Plugins**. **Test** on each says whether it's working.

## Catalogue

Index plugins say which systems and dumps exist. Romdex asks them in order; where two have a DAT for the same system,
the first wins.

- **DAT folder** (built in, set up from the start) reads the No-Intro and Redump DATs in your DAT folder.

## Intake

Intake plugins offer new dumps, such as files dropped into an inbox folder. Each runs in its own container and speaks
an HTTP contract, so anyone can write one. Every dump is checked by its hash before it's filed. The first ones come
in a later version.

## Connected apps

Connectors tell another app, such as RomM, when the library changes. They're optional.

Next: [Configuration](configuration.md).
