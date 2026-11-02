# Changelog

All notable changes to this project are listed here. Versions follow [Semantic Versioning](https://semver.org/).

## [0.3.2] - 2026-11-02

### Fixed
- The package EditMode tests put the editor language preference back when they finish. It is shared by every project on the
  machine, so a test run used to leave it at English for an editor of another project.

## [0.3.1] - 2026-11-02

More wording settings for hosts that keep their own editor text. The defaults give the 0.3.0 text.

### Added
- `editor.changeKeySeparator`: joins the Key columns of a row in the save window's change list (default `" / "`).
- `editor.boxCountNameMax` and text keys `box.count`, `box.countSeparator`, `box.countCut`: the count line of a folded box or
  a folded presentation column (module name length, line format, separator, cut mark).
- Text keys `columnDialog.keyframeFrame`, `columnDialog.keyframeStart`, `columnDialog.keyframeNode`: the parts of a row in the
  column window's keyframe list. They no longer follow `column.keyframe` / `column.motionStart`; the default text is the same.
- Tests for each (55 Python, 22 package EditMode).

## [0.3.0] - 2026-11-02

Settings for hosts that keep their own editor wording, data pipeline and player builds. Every new setting defaults to the
0.2.0 behaviour; the package screens and data are unchanged without them.

### Added
- `editor.text`: per-language overrides of editor strings, including module-specific exit names (`exit.<Module>.<exit event>`)
  and the line under a timed column's time (`column.time.<event>`, may be empty).
- `editor.export`: the More menu data build can run the project's own commands (`commands`, with `{project}` / `{config}` /
  `{out}` placeholders; `confirm` asks first) instead of `python -m skillgraph export`.
- Separate keys for dialog titles that shared the menu text (`export.title`, `workspace.resetTitle`, `bundleDesc.title`,
  `bands.title`, `values.addTitle`) and for the column menu's `column.addHere`; their default text is unchanged.
- The editor's first language follows the settings `language` until the user picks one.
- `SKILLGRAPH_EDITOR_ONLY` define: a project can leave the runtime assembly out of player builds.
- `skillgraph.codes(cfg)` and `python -m skillgraph codes`: every issue code with level and message text, including settings
  rule codes and plugin `CODES` tables.
- Public Python names in `skillgraph.__all__` (`Backend`, `practice`, `load_workbook`, `load_registry`, `sha`, `codes`, …).
- Display settings: `editor.newSkillModuleParent` (the New Skill window's parent), `editor.boxMinColumns`, `editor.valueUnits`,
  `editor.boxSummary` (folded box line) and `editor.compactCard` (one-line presentation cards: a time span and plain values).
- Text keys `values.titleTiers` (a tier family's values title; also gets the first and last tier keys), `values.descField`,
  `bundleDesc.field`, `combo.desc`, `card.range` and `card.rangeEnd`; their default text is unchanged.
- Tests for each (54 Python, 21 package EditMode).

### Changed
- `UnusedValue` warnings come after the plugin checks (the final pass over every value).

### Fixed
- The practice workspace now reads its flat copies of documents kept outside the data folder (it looked for the original
  relative path and missed them).
- Capture: a shot whose operations fail now fails with the message instead of opening the modal error dialog, which stopped
  an unattended capture.
- Capture windows no longer read or write the editor session (`Library/SkillGraphKit/session-product.json`); a capture used
  to leave its last shot's operations there for the next editor window.

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
