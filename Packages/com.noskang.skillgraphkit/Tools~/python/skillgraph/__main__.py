"""Command line.

  python -X utf8 -m skillgraph check            [--config skillgraph.json]   validate as product data, print the manifest block
  python -X utf8 -m skillgraph validate         [--dir FOLDER]               validate without product-only rules
  python -X utf8 -m skillgraph export --out DIR                              write logic/presentation JSON (product rules)
  python -X utf8 -m skillgraph dictionary-view                               write the generated module dictionary workbook
  python -X utf8 -m skillgraph expand-view                                   write the generated expanded sheet (after a lock check)
  python -X utf8 -m skillgraph graph-view --out FILE                         write the read-only graph model JSON
  python -X utf8 -m skillgraph serve                                         editor backend: one JSON request/response per line
  python -X utf8 -m skillgraph model|changes|save [--workspace practice] [--ops OPS.json] [--base BASE.json] [--out FILE]
  python -X utf8 -m skillgraph practice create|reset

The settings file is --config, else $SKILLGRAPH_CONFIG, else ./skillgraph.json when present, else built-in defaults.
"""
import argparse
import json
import os
import sys
from pathlib import Path

from .config import Config
from .core import SkillDataError
from .editor import Backend, OpError, serve
from .pipeline import build, run
from .views import dictionary_view, expand_view, graph_view, write_json


def find_config(arg):
    path = arg or os.environ.get('SKILLGRAPH_CONFIG')
    if not path and Path('skillgraph.json').is_file():
        path = 'skillgraph.json'
    return Config.load(path)


def main(argv=None):
    p = argparse.ArgumentParser(prog='skillgraph', description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument('action', choices=['check', 'validate', 'export', 'dictionary-view', 'expand-view', 'graph-view',
                                      'serve', 'model', 'changes', 'save', 'practice'])
    p.add_argument('sub', nargs='?', help='practice: create|reset')
    p.add_argument('--config')
    p.add_argument('--dir', help='data folder (default: dataDir from the settings)')
    p.add_argument('--out')
    p.add_argument('--workspace', default='product')
    p.add_argument('--ops')
    p.add_argument('--base')
    a = p.parse_args(argv)
    cfg = find_config(a.config)
    if a.action == 'serve':
        serve(cfg)
        return 0
    try:
        if a.action == 'check':
            out = build(cfg, a.dir)
            result = dict(manifest=out['manifest'], warnings=[str(w) for w in out['warnings']])
        elif a.action == 'validate':
            r = run(cfg.documents(a.dir), cfg, product=False)
            result = dict(errors=[str(x) for x in r['errors']], warnings=[str(x) for x in r['warnings']])
            print(json.dumps(result, ensure_ascii=False, indent=1))
            return 1 if r['errors'] else 0
        elif a.action == 'export':
            if not a.out:
                raise ValueError('export needs --out')
            out = build(cfg, a.dir)
            target = Path(a.out)
            target.mkdir(parents=True, exist_ok=True)
            (target / out['manifest']['logicFile']).write_bytes(out['logic'])
            (target / out['manifest']['presentationFile']).write_bytes(out['presentation'])
            write_json(target / 'skills.manifest.json', out['manifest'])
            result = dict(manifest=out['manifest'], warnings=[str(w) for w in out['warnings']])
        elif a.action == 'dictionary-view':
            result = dictionary_view(cfg)
        elif a.action == 'expand-view':
            result = expand_view(cfg, a.dir)
        elif a.action == 'graph-view':
            if not a.out:
                raise ValueError('graph-view needs --out')
            result = graph_view(cfg, a.out, a.dir)
        else:
            req = dict(cmd=a.action, workspace=a.workspace, action=a.sub,
                       ops=json.loads(Path(a.ops).read_text(encoding='utf-8')) if a.ops else [],
                       base=json.loads(Path(a.base).read_text(encoding='utf-8')) if a.base else None)
            result = Backend(cfg).handle(req)
            if a.action == 'model' and a.out:
                write_json(a.out, result['model'])
                m = result['model']
                result = dict(ok=True, file=a.out, owners=len(m['owners']), bundles=len(m['bundles']),
                              errors=sum(1 for i in m['issues'] if i['level'] == 'error'),
                              warnings=sum(1 for i in m['issues'] if i['level'] == 'warning'))
        print(json.dumps(result, ensure_ascii=False, indent=1, default=str))
        return 0 if result.get('ok', True) else 1
    except (SkillDataError, OpError, OSError, ValueError, KeyError) as error:
        print('skillgraph: ' + str(error), file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
