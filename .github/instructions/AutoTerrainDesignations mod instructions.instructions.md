---
description: Load when working on AutoTerrainDesignations mod code, making changes, or investigating bugs.
applyTo: 'src/**/*.cs'
---

# ATD-specific instructions

Read the shared workspace instructions first:

- `../../../.github/instructions/coi-maintained-mods.instructions.md`

The rules below are specific to AutoTerrainDesignations.

# ATD notification save-removability pattern

The shared workspace instructions require ATD, AFD, and DTK to remain safe to
remove from existing saves. ATD additionally has mod-owned notification protos,
so it must follow this concrete notification pattern.

Do not persist ATD-owned notification instances into the save. A saved active
notification that references an ATD-only proto causes a `CorruptedSaveException`
when the mod is absent on next load.

Do not add ad-hoc `INotificationsManager` / `NotifyOnce` calls outside the
transient notification manager.

Allowed notification pattern:
- Register ATD notification protos only in `ATD.Notifications.cs`.
- Add `.MuteAudio()` to every ATD notification proto. Restoring transient notifications after autosave must not replay alarm sounds.
- Create/remove active ATD notifications only through the transient notification helpers in `ATD.Notifications.cs`.
- Before serialization, purge all active ATD notifications in `ISimLoopEvents.BeforeSave.AddNonSaveable`. The purge must remove both tracked notification IDs and any active notification whose proto ID is in the ATD notification ID list.
- After `ISaveManager.OnSaveDone`, re-add transient notifications only from runtime/world-derived state. Do not require any saved mod state to restore them.
- `DoNotSaveAttribute` may be useful for ATD-owned runtime bookkeeping, but it does not hide notification instances already stored inside Mafi's `NotificationsManager`; those must be actively removed before save.
- Vanilla notification protos may be used if their semantics match, because vanilla can deserialize them without the mod. ATD-owned protos require the purge-before-save path above.

# Build verification

For ATD-only changes, run:

```powershell
dotnet build AutoTerrainDesignations.sln -c Debug
```

# ATD log helper scripts

ATD and AFD each emit a version + DLL timestamp line early in every game session:

```
I HH:MM:SS,mmm SNNNNNN ~Mai: [ATD] AutoTerrainDesignations vX.Y.Zn | dll: YYYY-MM-DD HH:MM:SS
I HH:MM:SS,mmm SNNNNNN ~Mai: [AFD] AutoForestryDesignations vX.Y.Zn | dll: YYYY-MM-DD HH:MM:SS
```
Compare the `dll:` timestamp against the DLL file's last-modified time to confirm the expected build was actually loaded. A mismatch means the game loaded an older build (stale bin output, wrong install path, etc.).

To show only these version rows from the newest log:
```powershell
.\tools\get-mod-log.ps1 -DllOnly
```

### Extract all mod-tagged rows
`tools/get-mod-log.ps1` grabs every line prefixed with `[ATD` or `[AFD` — version rows, warnings, errors, and performance logs — from the newest log (or a specified file):
```powershell
# All mod-tagged rows in the newest log
.\tools\get-mod-log.ps1

# Last 50 mod-tagged rows (useful for recent session activity)
.\tools\get-mod-log.ps1 -Last 50

# Use a specific log file
.\tools\get-mod-log.ps1 -LogPath "C:\...\26-05-17_08-56-26_5925.log"
```

`tools/extract-atd-farming-perf.ps1` extracts only `[ATD Farming Perf]` lines:

```powershell
# All perf rows in the newest log
.\tools\extract-atd-farming-perf.ps1

# Last 20 perf rows
.\tools\extract-atd-farming-perf.ps1 -Last 20
```
