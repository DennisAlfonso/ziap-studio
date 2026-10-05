# Project write safety

Any mutation below an opened project root **MUST** go through
`ProjectWriteCoordinator`. Feature services retain their own domain validation
(for example RPG Maker command anchors or Fusion schemas), but they do not own
the final filesystem commit.

## Ownership

- `RpgMakerOwned`: direct `data/*.json`, `js/plugins.js`, and
  `game.rmmzproject`. These files are blocked while `RPGMZ.exe` is running.
- `SharedProject`: Localization, `data/fusion/**`, assets, and other custom
  project resources. They may be written while RPG Maker is open, but always
  use optimistic SHA-256 concurrency checks.
- `StudioOwned`: `.ziap/**`, including metadata and pre-flight state. These
  retain atomic and concurrency protection without being blocked by RPG Maker.
- `OutsideProject`: rejected by the coordinator.

## Safe write protocol

Callers provide the normalized project root, target, operation (`ReplaceExisting`
or `CreateNew`), content and, for a replacement, the expected SHA-256 snapshot.
The coordinator validates containment and reparse points, acquires a bounded
cross-process lease, checks the live hash, writes a uniquely named temporary
file with flush-to-disk, validates it, checks the live target again immediately
before commit, atomically moves it, reads it back, and records the new snapshot.

`CreateNew` requires absence both before temporary creation and immediately
before the non-overwriting move. A concurrent writer therefore becomes an
explicit conflict rather than an overwrite.

## Coexistence and monitoring

`RpgMakerProcessMonitor` observes `RPGMZ.exe`. A detected process moves the
project into Protected Coexistence Mode; `RpgMakerOwned` commits fail closed.
After process closure, tracked snapshots are revalidated before those commits
are enabled again. Dirty documents are never automatically rebased.

`ProjectChangeMonitor` is advisory UI infrastructure. It coalesces watcher
events and distinguishes Studio writes only when both the normalized path and
resulting SHA-256 hash match a committed `SelfWriteRegistry` entry. It never
authorizes a write: every commit independently reads and hashes the live file.

## Future feature checklist

1. Keep semantic validation in the feature service.
2. Capture a `DocumentSourceSnapshot` when loading an existing file.
3. Use `ProjectWriteCoordinator` (normally through the shared
   `AtomicJsonFileWriter`) for the final project-root write.
4. Use `ReplaceExisting` with the snapshot hash, or `CreateNew` with no hash.
5. Surface `ProjectWriteException.Failure` to users; do not offer a force-save
   option for RPG Maker-owned files.
