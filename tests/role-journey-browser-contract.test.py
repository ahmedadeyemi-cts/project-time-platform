#!/usr/bin/env python3
"""Regression tests for the installed assigned-only role browser verifier.

Use the real verifier's coroutine with an isolated locator double. These tests
make no network requests and do not replace the authenticated live UAT gate.
"""
import ast
import asyncio
import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "scripts/release-test/run-flowhive-my-role-browser.py"
SOURCE = SCRIPT.read_text(encoding="utf-8")
SPEC = importlib.util.spec_from_file_location("role_browser_verifier", SCRIPT)
VERIFIER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(VERIFIER)


class LocatorTimeout(Exception):
    pass


class DeferredButtons:
    def __init__(self, values, *, timeout=False):
        self.values = values
        self.timeout = timeout
        self.ready = False
        self.events = []

    def filter(self, *, has_text):
        self.events.append(("filter", has_text))
        return self

    @property
    def first(self):
        return self

    async def wait_for(self, *, state):
        self.events.append(("wait", state))
        if self.timeout:
            raise LocatorTimeout()
        self.ready = True

    async def all_text_contents(self):
        if not self.ready:
            raise AssertionError("Read role contents before verified panel was visible")
        self.events.append(("read", None))
        return self.values


def load_real_waiter():
    tree = ast.parse(SOURCE)
    nodes = [node for node in ast.walk(tree)
             if isinstance(node, ast.AsyncFunctionDef) and node.name == "wait_for_assigned_role"]
    if len(nodes) != 1:
        raise AssertionError("Expected the real unique role-verification wait helper")
    namespace = {
        "require": VERIFIER.require,
        "VerificationError": VERIFIER.VerificationError,
        "PlaywrightTimeoutError": LocatorTimeout,
    }
    module = ast.Module(body=nodes, type_ignores=[])
    exec(compile(ast.fix_missing_locations(module), str(SCRIPT), "exec"), namespace)
    return namespace["wait_for_assigned_role"]


class AssignedRoleBrowserContract(unittest.TestCase):
    def run_waiter(self, locator, code="assigned_role_missing"):
        return asyncio.run(load_real_waiter()(locator, code))

    def test_waits_for_delayed_authority_before_reading_roles(self):
        locator = DeferredButtons(["Project Manager Your role"])
        self.assertEqual(self.run_waiter(locator), 0)
        self.assertEqual(locator.events, [("filter", "Your role"), ("wait", "visible"), ("read", None)])

    def test_missing_authority_fails_closed(self):
        with self.assertRaisesRegex(VERIFIER.VerificationError, "assigned_role_missing"):
            self.run_waiter(DeferredButtons([], timeout=True))

    def test_reentry_timeout_retains_its_diagnostic(self):
        with self.assertRaisesRegex(VERIFIER.VerificationError, "my_role_reentry_has_no_assigned_playbook"):
            self.run_waiter(DeferredButtons([], timeout=True), "my_role_reentry_has_no_assigned_playbook")

    def test_empty_verified_panel_is_not_accepted(self):
        with self.assertRaisesRegex(VERIFIER.VerificationError, "assigned_role_missing"):
            self.run_waiter(DeferredButtons([]))

    def test_unassigned_only_panel_is_not_accepted(self):
        with self.assertRaisesRegex(VERIFIER.VerificationError, "assigned_role_missing"):
            self.run_waiter(DeferredButtons(["Administrator Explore this role"]))

    def test_mixed_assigned_and_unassigned_panel_is_rejected(self):
        with self.assertRaisesRegex(VERIFIER.VerificationError, "my_role_exposes_unassigned_playbook"):
            self.run_waiter(DeferredButtons(["Engineer Your role", "Administrator Explore this role"]))

    def test_unassigned_first_is_also_rejected(self):
        with self.assertRaisesRegex(VERIFIER.VerificationError, "my_role_exposes_unassigned_playbook"):
            self.run_waiter(DeferredButtons(["Administrator", "Engineer Your role"]))

    def test_multiple_assigned_playbooks_remain_supported(self):
        self.assertEqual(self.run_waiter(DeferredButtons(["Manager Your role", "Engineer Your role"])), 0)

    def test_initial_visit_and_reentry_use_assigned_only_selector(self):
        selector = 'aside[aria-label="Your assigned roles"] button[aria-pressed]'
        self.assertEqual(SOURCE.count(selector), 2)
        self.assertNotIn('aside[aria-label="Choose a role"]', SOURCE)
        self.assertEqual(SOURCE.count("assigned_index = await wait_for_assigned_role("), 2)

    def test_role_count_is_sampled_after_authority_wait(self):
        visit = SOURCE.index('role_buttons = journey.locator(')
        wait = SOURCE.index('assigned_index = await wait_for_assigned_role(', visit)
        count = SOURCE.index('role_count = await role_buttons.count()', visit)
        nonempty = SOURCE.index('require(role_count > 0, "my_role_has_no_playbooks")', visit)
        self.assertLess(wait, count)
        self.assertLess(count, nonempty)

    def test_live_access_navigation_and_write_guards_are_preserved(self):
        required = (
            'require(anonymous_status in (401, 403), "anonymous_handoff_access_not_denied")',
            'require(step_count >= 3, "my_role_has_incomplete_steps")',
            '"my_role_step_navigation_stuck"',
            '"my_role_access_boundary_missing"',
            '"assigned_role_route_invalid"',
            '"browser_timeout_signed_handoff_reload"',
            'require(not writes, "browser_attempted_mutation")',
            'require(not page_errors, "browser_runtime_error")',
            'if parsed.method not in ("GET", "HEAD", "OPTIONS"):',
            'await route.abort()',
            'page.set_default_timeout(45_000)',
            '"mockedResponses": False',
            '"productionMutation": False',
        )
        for contract in required:
            with self.subTest(contract=contract):
                self.assertIn(contract, SOURCE)
        self.assertEqual(VERIFIER.ORIGIN, "https://phd-west-test.onenecklab.com")


if __name__ == "__main__":
    unittest.main(verbosity=2)
