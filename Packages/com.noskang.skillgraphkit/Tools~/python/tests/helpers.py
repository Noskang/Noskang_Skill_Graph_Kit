"""Test helpers: every test works on a temporary copy of the demo sample (or a fresh build of it)."""
import copy
import hashlib
import shutil
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
PYTHON = HERE.parent
sys.path.insert(0, str(PYTHON))

from skillgraph import demo  # noqa: E402
from skillgraph.config import Config  # noqa: E402
from skillgraph.core import Dictionary, load_registry  # noqa: E402
from skillgraph.keyframes import clear_cache  # noqa: E402

SAMPLE = PYTHON.parents[1] / 'Samples~' / 'Demo'


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()


class Temp:
    """A temp copy of the demo sample. rows: optional function(rows) that edits demo rows before the workbooks are written."""

    def __init__(self, rows=None, settings=None, lang=None):
        self.rows, self.settings, self.lang = rows, settings, lang

    def __enter__(self):
        self.dir = Path(tempfile.mkdtemp(prefix='skillgraph-test-'))
        shutil.copytree(SAMPLE, self.dir, dirs_exist_ok=True)
        clear_cache()
        data = dict(demo.SETTINGS)
        if self.lang:
            data['language'] = self.lang
        if self.settings:
            data = Config(data).data | self.settings
        self.cfg = Config(data, self.dir)
        if self.rows or self.lang:
            rows = demo.demo_rows()
            if self.rows:
                self.rows(rows)
            self.write(rows)
        return self

    def write(self, rows):
        d = Dictionary(self.cfg.dictionary_path, self.cfg)
        demo.write_workbooks(self.cfg, load_registry(self.cfg, d), rows, self.cfg.data_dir)

    def __exit__(self, *a):
        clear_cache()
        shutil.rmtree(self.dir, ignore_errors=True)


def find(rows, table, **key):
    return next(r for r in rows[table] if all(r.get(k) == v for k, v in key.items()))


def codes(result):
    return sorted({x.code for x in result['errors'] + result['warnings']})


def error_codes(result):
    return sorted({x.code for x in result['errors']})


def deep(x):
    return copy.deepcopy(x)
