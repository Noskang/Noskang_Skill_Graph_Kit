"""SkillGraph Kit Python backend: module-composed skills with Excel values.

Layers: config (host settings) -> workbook (Excel contract) -> expand (override layers) -> validate -> pipeline (export)
-> editor (edit operations, model, save) and views (generated read-only views).

Public API (stable names for host tools; everything else is internal):
  Config                      settings file
  run, run_data, build        validate and export (run_data takes rows already read, e.g. after edits)
  load_workbook, load_registry, Dictionary, Report, sha
                              read workbooks against the table registry and module dictionary; issue report; content hash
  Backend, practice, OpError  editor backend (one request dict -> one response dict), practice workspace create/reset
  codes                       every issue code with level and message text (core, settings rules, plugins)
  SkillDataError, VERSION
"""
from .codes import codes
from .config import Config
from .core import Dictionary, Report, SkillDataError, load_registry, sha
from .editor import Backend, OpError, practice
from .pipeline import VERSION, build, run, run_data
from .workbook import load as load_workbook

__all__ = ['Backend', 'Config', 'Dictionary', 'OpError', 'Report', 'SkillDataError', 'VERSION', 'build', 'codes', 'load_registry',
           'load_workbook', 'practice', 'run', 'run_data', 'sha']
