import importlib.util
from pathlib import Path
import unittest
import yaml

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("runtime_config", ROOT / "scripts/verity/validate-runtime-config.py")
config = importlib.util.module_from_spec(spec)
spec.loader.exec_module(config)


class RuntimeConfigurationTests(unittest.TestCase):
    def test_distinct_runtime_identity(self):
        config.validate({"RUNTIME_DB_PASSWORD": "ab" * 32, "POSTGRES_PASSWORD": "cd" * 32})

    def test_invalid_configs_fail_closed(self):
        for override in (
            {"RUNTIME_DB_PASSWORD": ""},
            {"RUNTIME_DB_PASSWORD": "ab" * 31},
            {"RUNTIME_DB_PASSWORD": "ab" * 32 + ";Username=postgres"},
            {"POSTGRES_PASSWORD": "ab" * 32},
            {"POSTGRES_USER": "ptp_runtime"},
            {"POSTGRES_USER": "ptp_app"},
            {"POSTGRES_DB": "ProjectPulse;Username=postgres"},
        ):
            with self.subTest(fields=list(override)):
                with self.assertRaises(ValueError):
                    config.validate({"RUNTIME_DB_PASSWORD": "ab" * 32, **override})

    def test_api_cannot_interpolate_provisioning_identity(self):
        environment = yaml.safe_load((ROOT / ".verity/deploy.compose.yml").read_text())["services"]["api"]["environment"]
        connection = environment["ConnectionStrings__ProjectPulse"]
        self.assertIn("Username=ptp_runtime;", connection)
        self.assertIn("${RUNTIME_DB_PASSWORD:", connection)
        for forbidden in ("POSTGRES_USER", "POSTGRES_PASSWORD", "DB_CONNECTION_STRING"):
            self.assertNotIn(forbidden, str(environment))


if __name__ == "__main__":
    unittest.main()
