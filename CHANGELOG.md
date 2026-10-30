# Changelog

All notable changes to this project are listed here. Versions follow [Semantic Versioning](https://semver.org/).

## [0.2.0] - 2026-10-30

Settings for hosts whose tier families, copy rules and note columns differ from the defaults. Every new setting defaults to
the 0.1.0 behaviour, so existing settings files keep working unchanged.

### Added
- `tiers.owner`, `tiers.familyBy`, `tiers.exclude`: read tier families from any table (for example a class roster), split a
  family by several columns and skip rows such as passive slots.
- `rules.companions[].code`: a companion rule may report its own issue code (default `CompanionMissing`).
- `owners[].duplicateClear`: columns a duplicated owner starts empty (default `["DescLoc"]`).
- `columns.ownerNote`: the owner note column (default: the same name as `columns.nodeNote`).
- Tests for each setting (50 Python tests).

### Fixed
- The edit model read owner notes from a fixed `Note` column instead of the settings.

## [0.1.0] - 2026-10-30

First public version.

### Added
- **Python backend** (`Tools~/python/skillgraph`): settings-driven core (key layout and bands, layers, event meanings, time grid,
  bundle kinds, owner tables, folders, formats, plugins), Excel reading with a 7-row header contract, override expansion
  (`+` / `−` / overrides, value bundle inheritance), validation against the module dictionary, keyframe time references
  (`@node:Name[±offset]`), logic / presentation export, generated views, English / Korean messages.
- **Editor backend**: 23 edit operations, edit model, change list, Excel write-back (changed cells only, new rows under their
  bundle, lock and outside-change checks, one previous copy, read-back comparison), aux file, practice workspace, JSON-lines `serve`.
- **Unity runtime** (`Noskang.SkillGraph`): host-declared `ModuleSet` and `ModuleDisplaySet` with byte-for-byte JSON export,
  `SkillData` binding with modifiers, demo `SkillRunner`, `SkillKeyframeReceiver`.
- **Unity editor** (`Noskang.SkillGraph.Editor`): GraphView skill graph editor (logic and presentation graphs on shared time
  columns, edit layers, tier value table, column and keyframe editing, search window, save dialog), capture command,
  keyframe tool, module dictionary export, preferences page, English / Korean UI.
- **Demo**: made-up skill set (Slash, Ground Spike, War Cry, Dash, Fire Bolt I–III, Guard Field, Charge Beam), reproducible
  sample generator, DemoProject with a playable scene.
- **Tests**: 47 Python tests (including a turn-based preset) and 19 Unity EditMode tests.
- **Docs**: README, architecture, data contract, editor guide, screenshots.
