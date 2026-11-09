# Installing

Romdex runs in Docker. Save [docker-compose.yml](../../docker-compose.yml) on your server, with a `.env` file beside it:

```
ROMS_PATH=/srv/games/roms
DATS_PATH=/srv/games/dats
```

Then start it:

```bash
docker compose up -d
```

and open `http://your-server:8788`. Setup walks you through the rest.

## The folders

| Folder | In the container | What it is |
| --- | --- | --- |
| `ROMS_PATH` | `/roms` | Your game library. Romdex only reads it for now. |
| `DATS_PATH` | `/dats` | No-Intro and Redump DATs, zipped or not. Romdex only reads it. |
| (a volume) | `/config` | Romdex's database, settings and logs. Back this up. |

Next: [Getting started](getting-started.md).
