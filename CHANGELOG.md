# Changelog

All notable changes to this project are listed here. Versions follow [Semantic Versioning](https://semver.org/).

## [0.5.0] - 2026-11-03

Board keys: named values that nodes write and read while a skill runs, so stacking passives and combo counters are built
in the graph instead of custom code. Data without board keys exports and saves byte for byte as in 0.4.0.

### Added
- **Board key table** `BoardKeyData` (ModuleBundles workbook, sheet `Board Keys`; optional, a workbook without the sheet is
  fine): `Name` (Object.Name), `Type` (`int` · `float` · `bool` · `text` · `key:<band>`), `Scope` (`Cast` = one cast with
  the effects it spawns, `Actor` = one character, kept by the game), `Default`, `Desc`.
- Settings `board.table` (the key table, `null` turns boards off) and `board.scopes` (`label` per scope, `kinds` that may
  use it, extra host scopes).
- Value link source `Board`: a slot reads a key's value. Module slot types `board` / `board:<type>,…` (the key the node
  writes) and `board:read[,<type>,…]` (a key read by name); a board slot takes only a Board link.
- Checks `UnknownBoardKey`, `BoardTypeMismatch`, `BoardScopeMismatch`, `BoardWriteTarget`, `BoardKeyName`, `BoardKeyType`
  and the warning `UnusedBoardKey`.
- Logic and presentation JSON: `boardKeys` (name order) when the table has rows; links `[slot, "board", key number]`.
- Edit operations `addBoardKey`, `setBoardKey` (renaming also renames every link to the key), `removeBoardKey` (refused
  while a link uses the key); `setLink` takes `source: "Board"`. The edit model has a `board` block and the slot source
  `board`. Saving the first key of a workbook without the sheet creates the sheet with its header rows.
- Registry: `"optional": true` tables (read as empty when the sheet is missing) and the column type `any` (a value or
  TRUE / FALSE).
- **Runtime** `Blackboard` (typed read / write, `Changed(BoardChange)` with the old and new value, `Values()` = keys away
  from their default, `Load(list, notify)` replaces the whole board), `BoardKey`, `IBoardScopes`, `SkillData.BoardKeys` /
  `NewBoard(scope)`, `BoundNode.BoardKey(slot)` and Board-aware `Value` / `Number` / `Integer` / `Text`.
  `SkillRunner.Start(owner, actor)` and `StartBuff(buff, event, actor)` take the character's board; a cast has one Cast
  board that its spawned effects share; `SkillRunner.Scopes` supplies host scopes. `ModuleCall` reads Board slots and
  has `Read`, `Key`, `Board` and `Write(slot, value)`.
- **Standard board modules** `BoardModules` (optional for a host): `BoardSet`, `BoardAdd` (Min..Max), `BoardCheck` (pass /
  fail exits, operators shown as ≥ > = ≠ ≤ <) and `BoardClear`; `AddTo(builder)`, `AddDisplay(display)`,
  `Handler(next)` for `SkillRunner`, and `Run(node, values, scopes, out result)` for a host's own runner.
- **Editor**: a Board tab (keys, add / edit / remove, the slots that write ▸ or read ◂ the picked key, click to go to the
  node), the slot source "◂▸ Board key" (keys whose type fits), ◂ / ▸ on card rows, faint dashed lines from a writer to a
  reader in the same graph (brighter for the picked key), English / Korean text keys `tab.board`, `board.*`,
  `slot.board`, `row.board*`.
- Capture spec keys `panel: "board"`, `boardKey` and `pick` (switch owners as the toolbar picker does); the capture report
  lists the windows a `"quit": false` run leaves open (`kept`).
- Demo: Slash counts hits in `Combo.Count` (Actor, max 3) and the third makes the next swing a finisher that clears it; a
  hit text (`ShowText`, new demo module) shows the count. `python -m skillgraph.demo --no-board` writes the 0.4.0 demo.
- Screenshots 08 – 10 (board tab, board slots, Korean); 01 – 07 taken again for the new tab.
- Tests for each (66 Python, 33 package EditMode, 39 EditMode with the demo).

### Changed
- Switching skills or buffs fits both graphs to the new owner from the layout's box bounds (at most 100 %). It used to call
  GraphView's frame all 50 ms later, which could run before the new boxes were measured and keep the old zoom.

### Fixed
- Capture with `"quit": false` now leaves the last shot on screen (the capture window and the windows that shot opened);
  every shot used to close its windows, so nothing was left to look at.
- Pressing the toolbar skill picker while its tooltip shows puts the tooltip away before the list opens (it could stay over
  the list for a moment) and brings it back when the list closes.

## [0.4.0] - 2026-11-03

The toolbar skill picker can be searched, for projects with many skills.

### Added
- The skill / buff picker on the toolbar opens a drop-down list instead of the stock popup menu. It lists every skill and
  buff in ascending key order under a search box: typing keeps the entries whose "key name" text contains it (any case,
  outer spaces ignored), so part of a key or part of a name is enough. Up / Down move, Enter picks, Esc closes, a click
  picks; the current one is marked. The closed field looks the same as before.
- Text keys `picker.search`, `picker.count`, `picker.empty` for the drop-down.
- Tests for each (55 Python, 25 package EditMode).

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
