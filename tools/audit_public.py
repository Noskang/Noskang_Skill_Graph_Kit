"""Public-release audit: scan the working tree and the whole git history for denied terms.

The term list lives outside the repository (default: .local/denylist.txt, git-ignored),
because publishing the list would itself leak the terms.

List format: one term per line, case-insensitive substring match.
Lines starting with "re:" are case-insensitive regular expressions. "#" lines and blank lines are ignored.

Optional allow file (default: .local/audit-allow.txt): lines of "<key>\t<term>" that were
reviewed by a person and accepted. <key> is a location printed by this script, or a file path
(e.g. "README.md") to accept the term in every version of that file.

Usage:
  python tools/audit_public.py                 # working tree + full history
  python tools/audit_public.py --tree-only
  python tools/audit_public.py --denylist PATH --allow PATH
Exit code: 0 = no findings, 1 = findings, 2 = setup error.
Images are not read; their paths are listed for a visual check.
"""
import argparse
import io
import re
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
IMAGE_EXT = {'.png', '.jpg', '.jpeg', '.gif', '.bmp', '.tga', '.psd', '.webp', '.svg'}
ZIP_EXT = {'.xlsx', '.xlsm', '.docx', '.pptx', '.zip', '.unitypackage'}
SKIP_PREFIXES = ('.local/',)


class Term:
    def __init__(self, raw):
        self.raw = raw
        if raw.startswith('re:'):
            self.rx = re.compile(raw[3:], re.IGNORECASE)
        else:
            self.rx = re.compile(re.escape(raw), re.IGNORECASE)


def load_terms(path):
    terms = []
    for line in path.read_text(encoding='utf-8').splitlines():
        line = line.strip()
        if line and not line.startswith('#'):
            terms.append(Term(line))
    return terms


def load_allow(path):
    if not path or not path.exists():
        return set()
    allow = set()
    for line in path.read_text(encoding='utf-8').splitlines():
        if line.strip() and not line.startswith('#') and '\t' in line:
            loc, term = line.split('\t', 1)
            allow.add((loc.strip(), term.strip()))
    return allow


def git(*args, binary=False):
    out = subprocess.run(['git', '-C', str(ROOT), *args], capture_output=True, check=True)
    return out.stdout if binary else out.stdout.decode('utf-8', 'replace')


def texts_of(name, data):
    """Yield (sub-location, text) for a file's bytes; zip containers are opened."""
    suffix = Path(name).suffix.lower()
    if suffix in ZIP_EXT:
        try:
            with zipfile.ZipFile(io.BytesIO(data)) as z:
                for info in z.infolist():
                    yield f'{name}!{info.filename}', z.read(info).decode('utf-8', 'replace')
            return
        except zipfile.BadZipFile:
            pass
    yield name, data.decode('utf-8', 'replace')


class Audit:
    def __init__(self, terms, allow):
        self.terms, self.allow = terms, allow
        self.findings, self.images = [], set()

    def allowed(self, term, *keys):
        return any((k, term) in self.allow for k in keys if k)

    def scan(self, where, text, path=None):
        for n, line in enumerate(text.splitlines(), 1):
            for t in self.terms:
                if t.rx.search(line):
                    loc = f'{where}:{n}'
                    if not self.allowed(t.raw, loc, where, path):
                        self.findings.append((loc, t.raw, line.strip()[:160]))

    def file(self, prefix, name, data):
        if name.startswith(SKIP_PREFIXES):
            return
        self.scan(f'{prefix}{name}(path)', name)
        if Path(name).suffix.lower() in IMAGE_EXT:
            self.images.add(name)
            return
        for where, text in texts_of(name, data):
            self.scan(f'{prefix}{where}', text, path=where)


def audit_tree(a):
    names = git('ls-files', '-co', '--exclude-standard', '-z').split('\0')
    for name in filter(None, names):
        p = ROOT / name
        if p.is_file():
            a.file('tree:', name, p.read_bytes())


def audit_history(a):
    if not git('rev-list', '--all').strip():
        return
    log = git('log', '--all', '--format=%H%x00%an <%ae>%x00%cn <%ce>%x00%B%x01')
    for entry in filter(str.strip, log.split('\x01')):
        sha, author, committer, body = entry.strip('\n').split('\x00', 3)
        a.scan(f'commit:{sha[:10]}', f'{author}\n{committer}\n{body}')
    seen = set()
    for line in git('rev-list', '--objects', '--all').splitlines():
        parts = line.split(' ', 1)
        if len(parts) < 2 or parts[0] in seen:
            continue
        seen.add(parts[0])
        if git('cat-file', '-t', parts[0]).strip() == 'blob':
            a.file(f'blob:{parts[0][:10]}:', parts[1], git('cat-file', 'blob', parts[0], binary=True))
    for ref in git('for-each-ref', '--format=%(refname)').splitlines():
        a.scan('ref', ref)


def main():
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument('--denylist', type=Path, default=ROOT / '.local' / 'denylist.txt')
    ap.add_argument('--allow', type=Path, default=ROOT / '.local' / 'audit-allow.txt')
    ap.add_argument('--tree-only', action='store_true')
    args = ap.parse_args()
    if not args.denylist.exists():
        print(f'denylist not found: {args.denylist}', file=sys.stderr)
        return 2
    a = Audit(load_terms(args.denylist), load_allow(args.allow))
    audit_tree(a)
    if not args.tree_only:
        audit_history(a)
    for loc, term, line in a.findings:
        print(f'{loc}\t{term}\t{line}')
    for img in sorted(a.images):
        print(f'image (check visually): {img}')
    print(f'findings: {len(a.findings)}, images to check: {len(a.images)}')
    return 1 if a.findings else 0


if __name__ == '__main__':
    sys.exit(main())
