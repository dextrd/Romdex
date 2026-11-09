# Romdex

Romdex knows what's in your game library and what's missing from it. It matches every file by its hash against the
No-Intro and Redump DATs you give it. It's part of the dextrd family, alongside Melodex for music.

Romdex is early. This version is the shell: accounts, plugins, background tasks, the API and the web app. Systems,
matching, intake and tidying come next.

## Running it

```bash
ROMS_PATH=/path/to/roms DATS_PATH=/path/to/dats docker compose up -d
```

Then open http://localhost:8788. See the [guide](docs/guide/README.md).

## Building it

Needs the .NET 10 SDK.

```bash
dotnet build
dotnet test
dotnet run --project src/Romdex.Core
```

The core serves the web app and the API on http://localhost:5072, with the API reference at `/api/docs`.

## Licence

MIT. Romdex never bundles GPL tools such as igir or EmulatorJS: they run in their own containers. Nothing here points
at sources of commercial games; you bring your own dumps.
