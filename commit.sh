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

BASE_MSG="Generate commit messages and commit the current change. Do not modify file contents. Do not build or test. Do not run any command other than git status, git diff, git log, git add, and git commit. Do not push. Do not re-execute commit.sh. Do not ask questions. If staged and unstaged edits belong to the same change, stage the rest and make one commit.\n\n"
TASK="$BASE_MSG"
for arg in "$@"; do
    TASK+=$'\n'"$arg"
done

#printf '[%s]\n' 
"$CMD" run --workspace . --approval review --work --show-thinking --external-tools off --duration 600 "$TASK"



