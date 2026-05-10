---
description: Describe when these instructions should be loaded by the agent based on task context
# applyTo: 'Describe when these instructions should be loaded by the agent based on task context' # when provided, instructions will automatically be added to the request context when the pattern matches an attached file
---

<!-- Tip: Use /create-instructions in chat to generate content with agent assistance -->

# Save removability — mandatory constraint
**ATD must be safe to remove from an existing save.** The mod must not leave ATD-only proto IDs or runtime scaffolding in the game's save file.

This rules out:
- Any permanent game-side serialization hooks (entity components, persisted mod records, custom save payloads, etc.).
- Persisting ATD-owned notification instances into the save. A saved active notification that references an ATD-only proto causes a `CorruptedSaveException` when the mod is absent on next load.
- Adding ad-hoc `INotificationsManager` / `NotifyOnce` calls outside the transient notification manager.

Allowed notification pattern:
- Register ATD notification protos only in `ATD.Notifications.cs`.
- Add `.MuteAudio()` to every ATD notification proto. Restoring transient notifications after autosave must not replay alarm sounds.
- Create/remove active ATD notifications only through the transient notification helpers in `ATD.Notifications.cs`.
- Before serialization, purge all active ATD notifications in `ISimLoopEvents.BeforeSave.AddNonSaveable`. The purge must remove both tracked notification IDs and any active notification whose proto ID is in the ATD notification ID list.
- After `ISaveManager.OnSaveDone`, re-add transient notifications only from runtime/world-derived state. Do not require any saved mod state to restore them.
- `DoNotSaveAttribute` may be useful for ATD-owned runtime bookkeeping, but it does not hide notification instances already stored inside Mafi's `NotificationsManager`; those must be actively removed before save.
- Vanilla notification protos may be used if their semantics match, because vanilla can deserialize them without the mod. ATD-owned protos require the purge-before-save path above.

# Versioning and release model
- **Local/alpha packages** (built via `build.ps1 -Package`): increment the letter suffix on each package — `0.2.5a`, `0.2.5b`, etc. Update both `manifest.json` and `changelog.txt` to the new letter version.
- **Public releases** (CoI Hub): collate all lettered alpha changes into a single new version (e.g. `0.2.5a + 0.2.5b + 0.2.5c → 0.2.6`). Update both `manifest.json` and `changelog.txt`.
- `manifest.json` version and the top `changelog.txt` entry must always match.
- Do not propose version changes unless the user explicitly asks to package or release.

# Externalize constants to settings file
If new constants or parameters are added to the mod, suggest that these be externalized into the settings file with clear descriptions, so power users can easily customize behavior without needing to modify code.

# Update change log with new features and fixes
After having added new features or fixes, suggest that the mod's change log be updated with clear descriptions of what was added or fixed, so users can easily see what's new in each version. If there is no entry for a new version, document unreleased changes in a new section at the end of the file for the upcoming version.

# Review API implementation for breaking changes and new features
After each major update, review the mod API implementation and suggest to update instructions with any new features, changes, or fixes that may impact users or modders. This ensures that the mod's documentation remains accurate and helpful for the community.

# MaFi modding API Reference
Reference for general queries regarding e.g. the manifest file, mod structure, and API usage. For game-specific behavior, also check the Captain of Industry Wiki and Decompiled Source sections below.
- Captain of Industry modding documentation: https://github.com/MaFi-Games/Captain-of-industry-modding

# Captain of Industry Decompiled Source
- Decompiled game source code is in the `./../Mafi` directory. Use it to inspect game logic, entity behavior, and API details when needed. Be mindful that decompiled code may not have original variable names or comments, so it may require some interpretation.

# Captain of Industry Wiki
- Captain of Industry Wiki: https://wiki.coigame.com/
- Not a 100% reliable source of truth (contains some obsolete information), but still a great place to read up on game mechanics, entity behavior.

# Logging and Debugging
- To inspect the latest part of the newest Captain of Industry log, run:
  ```powershell
  Get-ChildItem -LiteralPath "$env:APPDATA\Captain of Industry\Logs" -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object { Get-Content -LiteralPath $_.FullName -Tail 220 }
  ```
