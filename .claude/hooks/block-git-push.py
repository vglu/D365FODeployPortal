#!/usr/bin/env python3
"""PreToolUse hook: block `git push` issued by Claude.

Reads the tool_input JSON from stdin. If the Bash command contains
`git push` as a token (handles pipes, subshells, &&), emits a deny
JSON that Claude Code honors regardless of the allow-list.

Rationale: build/tag/push are three separate authorization gates in
this repo. Push must come from the user's fingers — invoke via `! git
push` in the CLI prompt. This hook is the physical stop that survives
context compaction, memory drift, and session boundaries.

Tag pushes (`git push origin v*` / `--tags`) trigger the GHCR + Docker
Hub release workflow — that is exactly the kind of action that should
require a deliberate human keystroke.

Exit: always 0. The decision is communicated via stdout JSON.
"""
from __future__ import annotations
import json
import re
import sys

GIT_PUSH_RE = re.compile(r"\bgit\s+push\b")


def main() -> int:
    try:
        payload = json.load(sys.stdin)
    except json.JSONDecodeError:
        # Malformed input — don't block, let Claude Code handle it.
        print("{}")
        return 0

    cmd = payload.get("tool_input", {}).get("command", "") or ""
    if GIT_PUSH_RE.search(cmd):
        decision = {
            "hookSpecificOutput": {
                "hookEventName": "PreToolUse",
                "permissionDecision": "deny",
                "permissionDecisionReason": (
                    "git push is blocked by local policy. "
                    "Build/tag/push are three separate authorization gates. "
                    "User must run `! git push ...` themselves from the CLI prompt. "
                    "Tag pushes (v*) trigger the release workflow — these especially "
                    "should be a deliberate human action. "
                    "See .claude/hooks/block-git-push.py for details."
                ),
            }
        }
        print(json.dumps(decision))
    else:
        print("{}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
