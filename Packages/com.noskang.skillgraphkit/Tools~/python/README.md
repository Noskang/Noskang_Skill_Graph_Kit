# skillgraph (Python backend)

Python 3.10+ and `openpyxl` 3.1. No other packages.

| Module | Role |
|---|---|
| `config.py` | Host settings (`skillgraph.json`): key bands and layout, layer names, events, time grid, bundle kinds, folders, formats, plugins |
| `workbook.py` | Excel contract: header rows, typed cells, reading tables, lock check, atomic save |
| `expand.py` | Override layers: `+` / `-` / overridden nodes and slots, value bundle inheritance |
| `validate.py` | Checks against the module dictionary and between owners, bundles and values |
| `keyframes.py` | `@node:keyframe[+offset]` time references read from `.anim` clips |
| `pipeline.py` | read → expand → validate → export (logic / presentation JSON) |
| `editor.py` | Graph editor backend: operations, edit model, change list, Excel write-back, aux file, practice workspace, `serve` |
| `views.py` | Generated read-only views (expanded sheet, module dictionary workbook, graph model) |
| `demo.py` | Builds the made-up demo sample (`Samples~/Demo`) reproducibly |
| `tables.json` | Default table registry |
| `messages.py` | English / Korean messages |

```bash
cd Packages/com.noskang.skillgraphkit/Tools~/python
python -X utf8 -m skillgraph check --config ../../Samples~/Demo/skillgraph.json
python -X utf8 -m unittest discover -s tests -t tests
```
