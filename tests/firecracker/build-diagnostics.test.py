#!/usr/bin/env python3
import importlib.util
import json
import subprocess
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FORMATTER = ROOT / "src" / "Rune.Firecracker" / "format-build-diagnostic.py"

spec = importlib.util.spec_from_file_location("rune_build_diagnostics", FORMATTER)
module = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(module)


class BuildDiagnosticTests(unittest.TestCase):
    def diagnostic(self, language, raw, kind="compilation"):
        return module.build_diagnostic(language, kind, raw)

    def test_rust_error_is_structured(self):
        result = self.diagnostic(
            "rust",
            """error[E0425]: cannot find value `missing` in this scope
 --> /input/source.rs:2:5
  |
2 |     missing;
  |     ^^^^^^^ not found in this scope
""",
        )
        self.assertEqual(result["kind"], "compilation")
        self.assertEqual(result["message"], "cannot find value `missing` in this scope")
        self.assertEqual(result["code"], "E0425")
        self.assertEqual((result["file"], result["line"], result["column"]), ("rune.rs", 2, 5))

    def test_c_error_is_structured(self):
        result = self.diagnostic(
            "c",
            "/input/source.c:3:12: error: use of undeclared identifier 'missing'\n",
        )
        self.assertEqual(result["message"], "use of undeclared identifier 'missing'")
        self.assertEqual((result["file"], result["line"], result["column"]), ("rune.c", 3, 12))

    def test_cpp_error_is_structured(self):
        result = self.diagnostic(
            "cpp",
            "/input/source.cpp:7:4: error: expected expression\n",
        )
        self.assertEqual(result["message"], "expected expression")
        self.assertEqual(result["file"], "rune.cpp")

    def test_javascript_error_is_structured(self):
        result = self.diagnostic(
            "javascript",
            "✘ [ERROR] Expected expression but found \";\"\n\n    /input/source.js:1:15:\n",
        )
        self.assertEqual(result["message"], 'Expected expression but found ";"')
        self.assertEqual((result["file"], result["line"], result["column"]), ("rune.js", 1, 15))

    def test_typescript_error_is_structured(self):
        result = self.diagnostic(
            "typescript",
            "✘ [ERROR] Expected \";\" but found \"}\"\n\n    /input/source.ts:4:1:\n",
        )
        self.assertEqual(result["message"], 'Expected ";" but found "}"')
        self.assertEqual(result["file"], "rune.ts")

    def test_python_error_is_structured(self):
        result = self.diagnostic(
            "python",
            '  File "/input/source.py", line 2\n    if True print("x")\n            ^^^^^\nSyntaxError: invalid syntax\n',
        )
        self.assertEqual(result["message"], "invalid syntax")
        self.assertEqual((result["file"], result["line"]), ("rune.py", 2))

    def test_composed_source_location_maps_back_to_user_line(self):
        result = module.build_diagnostic(
            "typescript",
            "rune.ts:145:11 - error SC0001: broken",
            "compilation",
            user_start_line=143,
            user_end_line=147,
        )
        self.assertEqual(
            (result["file"], result["line"], result["column"]),
            ("rune.ts", 3, 11),
        )

    def test_generated_source_location_is_hidden(self):
        result = module.build_diagnostic(
            "typescript",
            "rune.ts:150:22 - error SC1100: internal bootstrap failure",
            "compilation",
            user_start_line=3,
            user_end_line=6,
        )
        self.assertNotIn("file", result)
        self.assertNotIn("line", result)
        self.assertNotIn("column", result)

    def test_internal_paths_and_ansi_are_removed(self):
        result = self.diagnostic(
            "rust",
            "\x1b[31merror: failed in /work/tmp/output and /tmp/rune-secret/file\x1b[0m\n"
            " --> /input/source.rs:1:1\n",
        )
        details = result["details"]
        self.assertNotIn("/work/", details)
        self.assertNotIn("/input/", details)
        self.assertNotIn("/tmp/", details)
        self.assertNotIn("\x1b", details)

    def test_resource_signatures_override_compilation(self):
        result = self.diagnostic("rust", "Killed\n", "compilation")
        self.assertEqual(result["kind"], "resource")

    def test_timeout_and_infrastructure_have_stable_messages(self):
        timeout = self.diagnostic("python", "", "timeout")
        infrastructure = self.diagnostic("python", "", "infrastructure")
        self.assertIn("wall-time", timeout["message"])
        self.assertIn("environment failed", infrastructure["message"])

    def test_details_are_bounded(self):
        raw = "\n".join(f"error: line {i}" for i in range(1000))
        result = self.diagnostic("rust", raw)
        self.assertLessEqual(len(result["details"]), module.MAX_DETAILS_CHARS + 1)
        self.assertLessEqual(len(result["details"].splitlines()), module.MAX_DETAILS_LINES)

    def test_cli_json_contract_excludes_display_only_fields(self):
        completed = subprocess.run(
            [
                sys.executable,
                str(FORMATTER),
                "--language",
                "c",
                "--kind",
                "compilation",
                "--json",
            ],
            input="/input/source.c:1:1: error: broken\n",
            text=True,
            capture_output=True,
            check=True,
        )
        result = json.loads(completed.stdout)
        self.assertEqual(
            result,
            {
                "language": "c",
                "kind": "compilation",
                "message": "broken",
                "file": "rune.c",
                "line": 1,
                "column": 1,
                "details": "rune.c:1:1: error: broken",
            },
        )


if __name__ == "__main__":
    unittest.main()
