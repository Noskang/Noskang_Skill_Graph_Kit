"""SkillGraph Kit Python backend: module-composed skills with Excel values.

Layers: config (host settings) -> workbook (Excel contract) -> expand (override layers) -> validate -> pipeline (export)
-> editor (edit operations, model, save) and views (generated read-only views).
"""
from .config import Config
from .core import Dictionary, Report, SkillDataError
from .pipeline import VERSION, build, run, run_data

__all__ = ['Config', 'Dictionary', 'Report', 'SkillDataError', 'VERSION', 'build', 'run', 'run_data']
