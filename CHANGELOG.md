# Changelog

## 0.1.0 (unreleased)

- Romdex's shell, from Melodex: household accounts, API keys and webhooks, background tasks, the activity log, and
  the `/api/v1` API with its reference at `/api/docs`.
- Plugins: index, intake and connector contracts, and the built-in DAT folder catalogue, which reads No-Intro and
  Redump DATs, zipped or not.
- The web app: first-run setup, Activity, and Settings.
- The catalogue: DATs imported by system and grouped into titles, updated daily, with IDs that survive a new DAT
  version. The Redump plugin fetches Redump's DATs and BIOS DATs.
- The library: every file hashed once (CRC32, MD5 and SHA-1, with and without a header), inside zips, 7z and CHDs, and
  matched against the catalogue.
- The Library, with Systems and Games views, a system's games, a game's dumps, and Review: what's verified, partial
  and bad. The API also has what's missing after 1G1R preferences, ignoring and choosing a dump, and an igir-shaped
  report per system (`/systems/{id}/report.csv`).
- Search: every game the DATs list, in the library or not, by system and by whether you have it.
- The intake contract gains search and fetch, as Melodex's retrieval contract has, for plugins that bring dumps in.
- `tools/compare-igir.py`, to check Romdex's report against igir's.
