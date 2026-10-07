#!/bin/bash
set -euo pipefail

CMD="crystal"
if ! command -v crystal >/dev/null 2>&1; then
    CMD="CrystalCode"
fi

if ! command -v "$CMD" >/dev/null 2>&1; then
    echo "Error: neither 'crystal' nor 'CrystalCode' was found in PATH" >&2
    exit 127
fi

BASE_MSG="Generate commit messages and commit the current change. Do not modify file contents. Do not build or test. Do not push. Do not ask questions.

Do not run any command other than git status, git diff, git log, git add, and git commit. Do not run commit.sh: it starts another copy of this same task and loops.

If staged and unstaged edits belong to the same change, stage the rest and make one commit."

FORMAT_MSG="Commit message format is mandatory. Use Conventional Commits.

Write each subject on one line, in this form:
<type>: <summary>

Use a lowercase type and exactly one space after the colon. Allowed types:
- feat
- fix
- docs
- chore
- refactor
- test
- perf
- build
- ci
- revert

Write the summary in plain English, in the imperative mood. Do not end it with a period. Do not use emoji. Keep the subject at 72 characters or fewer.

If one commit is not enough, create multiple commits, each with a compliant subject.\n\n\n"

TASK="$BASE_MSG
\n
$FORMAT_MSG"

for arg in "$@"; do
    TASK+=$'\n'"$arg"
done

#printf '[%s]\n' 
"$CMD" run --workspace . --approval review --work --show-thinking --prompt-set default --external-tools off --workspace-trust off --duration 600 "$TASK"



