"""Run the package from a source checkout without installation."""

from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src"))
from vinsa_t906.cli import main

raise SystemExit(main())
