# fixinggenie/Listenarr

A fork of [Listenarrs/Listenarr](https://github.com/Listenarrs/Listenarr) that carries a few
stability fixes until they land upstream.

- `canary` mirrors upstream and is never committed to here.
- `stable` is upstream `canary` plus the fixes below. It builds to
  `ghcr.io/fixinggenie/listenarr:stable` (also tagged `stable-<sha>`).
- Every Monday the **Fork stable image** workflow merges upstream `canary` into `stable`, runs the
  backend tests and publishes. A merge conflict or a failing test stops the run, and the last
  good image stays in place.

## Fixes

On a small host, page loads stalled for 20 to 40 seconds and requests failed with
`SQLite Error 5: 'database is locked'` inside session authentication. That signed users out and
made "Add to Library" hang. Three causes, all in SQLite write contention:

1. **WAL was never enabled.** `SqlitePragmaInitializer.ApplyPragmas` had no callers, so the
   database ran in rollback-journal mode, where any write blocks every reader. It now runs at
   startup after migrations, and the journal mode is logged.
2. **Unchanged downloads were rewritten on every poll.** Imported downloads stay "active" while
   they seed, and the monitor saved each of them every 15 seconds. `EfDownloadRepository.UpdateAsync`
   now skips the write when nothing changed.
3. **Every authenticated request wrote to the database.** The session check saved
   `LastAccessed` on each API call and hub connection. It now writes at most every 5 minutes.

Tests: `tests/Features/Infrastructure/Persistence/SqliteWriteContentionTests.cs`.

## Switching a host to this image

```yaml
image: ghcr.io/fixinggenie/listenarr:stable   # instead of ghcr.io/listenarrs/listenarr:canary
```

No database migrations are added, so switching back to the upstream image works the same way.
