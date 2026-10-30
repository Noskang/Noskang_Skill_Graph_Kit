# Changelog

All notable changes to this project are listed here. Versions follow [Semantic Versioning](https://semver.org/).

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
