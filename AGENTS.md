# Codex Workspace Instructions

When working in this Captain of Industry mods workspace, read and use the
general instruction set under `AutoTerrainDesignations/.github/` before making
changes or investigating game/mod behavior.

In particular:

- Use `AutoTerrainDesignations/.github/instructions/AutoTerrainDesignations mod instructions.instructions.md`
  for general Captain of Industry modding workflow guidance.
- Use the documented decompiled-source location:
  `%APPDATA%\Captain of Industry\Mafi`.
- Treat ATD's `.github` instructions as workspace conventions when they
  generalize to sibling mods such as Designer Toolkit, AutoHelpers, and AFD.
- Do not blindly apply ATD-specific release, manifest, save-removability, or
  notification rules to another mod unless that mod has the same requirement.
- If a sibling mod has its own instructions, use those first and then apply the
  ATD workspace guidance where compatible.
- After code changes, build the active project or solution for the mod being
  changed, not necessarily ATD's solution.
