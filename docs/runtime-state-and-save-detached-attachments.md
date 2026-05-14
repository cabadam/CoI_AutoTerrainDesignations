# Runtime State and Save-Detached Attachments in ATD

## Purpose

This document clarifies how Auto Terrain Designations (ATD) currently relates to the helper-level concept of save-detached vanilla attachments.

ATD already fulfills the intended "save-removable" contract in most areas. Most ATD runtime state is not part of the vanilla save object graph in the first place.

This distinction is important.

## Most ATD runtime state is not a save-detachment problem

Examples:

- corner mode active flag
- drag state
- preview tile tracking
- protected one-shot tile sets
- reflected controller references
- sound references
- cursor references
- toolbox references
- renderer references
- reflection caches

These are ordinary runtime-only fields.

They require:

- lifecycle cleanup
- mode exit handling
- proper unload/dispose behavior
- safe reconstruction after reload

They do **not** generally require save-detachment logic because vanilla save traversal does not normally reach them.

## The actual save-detachment problem

The real save-detachment problem appears when:

```text
A mod-created object is attached to or referenced by a vanilla-owned object graph
in a way that vanilla save traversal might otherwise persist.
```

Examples may include:

- custom notification objects attached to vanilla notification lists
- custom status entries inserted into vanilla-owned UI containers
- future custom inspector fragments
- helper-created runtime wrappers attached to vanilla entities
- future helper-created settings or command descriptors registered into vanilla systems

Those objects may need to function normally at runtime, including clickable behaviors and references back to vanilla game objects.

The issue is not runtime visibility.

The issue is unintended vanilla save ownership.

## Important ATD distinction

ATD corner designation placement itself is normal vanilla game state:

```csharp
s_desigManager.AddOrReplaceDesignation(s_activeCornerProto, data);
```

The resulting designation data should be saved by vanilla normally.

The runtime machinery that helps create the designation should not be persisted.

This distinction should remain explicit in future helper work.

## Current ATD contract

ATD already behaves correctly in the following sense:

```text
Placed game state
  should survive save/load and survive mod removal where possible.

Runtime placement machinery
  should disappear cleanly when the mod is absent.
```

This is the contract future helper persistence and attachment systems should preserve.

## Future helper direction

The helper architecture should separate three concerns:

```text
Runtime/
  runtime-only state and lifecycle helpers

VanillaAttachments/
  save-detached vanilla runtime attachments

Persistence/
  helper-owned explicit persisted state
```

## Future ATD risks

ATD itself currently has limited save-detachment pressure.

However, future helper infrastructure or ATD features could introduce it.

Likely future examples:

- custom notifications
- persistent mod metadata attached to towers
- custom inspector UI entries
- helper-created status entries
- helper-created runtime wrappers around vanilla objects
- helper persistence handles accidentally referenced from vanilla state

For those cases, the helper-level save-detached attachment architecture should be used.

## Design rule

Preferred architecture:

```text
Persisted model
  helper-owned, serializable, versioned

Runtime attachment
  disposable, runtime-facing, attached to vanilla only temporarily

Vanilla object
  referenced by stable key/id
```

Do not persist the same object that is attached to vanilla runtime systems.

## Relationship to CoI_AutoHelpers

See:

```text
docs/save-detached-vanilla-attachments.md
```

in the `CoI_AutoHelpers` repository for the canonical architecture note.
