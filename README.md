# BakiPicks

Netflix-style auto-generated category rows for Jellyfin. Runs as a server-side plugin — daily scheduled task analyses your library and watch history, produces ranked categories, exposes them via REST endpoints that any Jellyfin client (or a forked client) can consume.

## Install

1. In Jellyfin: **Dashboard → Plugins → Repositories → Add**
2. Repository URL:
   ```
   https://raw.githubusercontent.com/<your-github-user>/bakipicks/main/manifest.json](https://github.com/nishantkapur/bakipicks/blob/main/manifest.json
   ```
3. Save → go to **Catalog** → install **BakiPicks** → restart Jellyfin when prompted

## First run

After install:

1. **Dashboard → Plugins → BakiPicks** — pick the target user (single-user setup) and tweak any settings
2. **Dashboard → Scheduled Tasks → "Rebuild BakiPicks Categories"** → click **Run**
3. Watch the log: `tail -f /var/log/jellyfin/jellyfin*.log | grep BakiPicks`
4. Inspect output: `cat <jellyfin-config>/plugins/BakiPicks/state.json`

## API

All endpoints under `/BakiPicks/`:

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/BakiPicks/Categories` | List of category definitions |
| `GET` | `/BakiPicks/Categories/{id}/Items?startIndex=0&limit=20` | Paged items in a category |
| `POST` | `/BakiPicks/Rebuild` | Trigger rebuild (admin auth) |

Manual test once installed:
```bash
curl -H "X-Emby-Token: <api-key>" http://<jellyfin-host>:8096/BakiPicks/Categories
```

## How it picks categories

Six-stage pipeline:

1. **Signal collection** — reads Jellyfin's `UserData` (watch %, favorites, play counts)
2. **Feature extraction** — per-item vectors of genres, decade, people, studios, tags, runtime, rating
3. **Affinity scoring** — weighted sum of signals × features, smoothed for small samples
4. **Label propagation** — predicts affinity for unwatched items
5. **Candidate generation** — three sources: taxonomy seeds (~120 Netflix-style templates), high-affinity feature combos, exploration rows
6. **Diversity-aware top-K selection** — picks N categories with greedy diversity penalty so rows don't overlap

Detailed weights live in plugin config and `Resources/taxonomy_seeds.json` (editable post-install at `<jellyfin-config>/plugins/BakiPicks/taxonomy_seeds.json`).

## Tuning without rebuilding

- **Taxonomy seeds**: edit `<jellyfin-config>/plugins/BakiPicks/taxonomy_seeds.json`, re-run the scheduled task
- **Signal weights, K, thresholds**: change in the plugin's admin UI

## Build

```bash
cd Jellyfin.Plugin.BakiPicks
dotnet build -c Release
```

Output DLL: `Jellyfin.Plugin.BakiPicks/bin/Release/net8.0/Jellyfin.Plugin.BakiPicks.dll`

## Release flow

Tag a commit:
```bash
git tag v1.0.0.0
git push --tags
```

GitHub Action builds, zips, computes checksum, creates a GitHub Release, appends the new version entry to `manifest.json`, and pushes that update back. Jellyfin servers using the manifest URL will see the new version in their plugin catalog within their normal refresh interval.
