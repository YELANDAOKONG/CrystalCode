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

BASE_MSG="Generate commit messages and commit, do not build, test, or push. Don't do anything else. If there is any uncertainty, suspend the execution and do not continue!"
TASK="$BASE_MSG"
for arg in "$@"; do
    TASK+=$'\n'"$arg"
done

#printf '[%s]\n' 
"$CMD" run --workspace . --approval review --work --show-thinking --external-tools off --duration 600 "$TASK"



