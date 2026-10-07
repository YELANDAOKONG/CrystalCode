#!/bin/bash
set -euo pipefail

if [[ $# -lt 2 || -z "$1" || -z "$2" ]]; then
    echo "Usage: ./changelog.sh <FROM> <TO> [MESSAGE...]" >&2
    exit 2
fi

FROM=$1
TO=$2
shift 2

CMD="crystal"
if ! command -v crystal >/dev/null 2>&1; then
    CMD="CrystalCode"
fi

if ! command -v "$CMD" >/dev/null 2>&1; then
    echo "Error: neither 'crystal' nor 'CrystalCode' was found in PATH" >&2
    exit 127
fi

BASE_MSG="Write the release note for one commit range in this repository. Do not modify files. Do not build or test. Do not commit or push. Do not run changelog.sh or commit.sh. You may run only git log, git show, git diff, git rev-parse, and git remote. Do not ask questions.

Use the two refs exactly as written, including HEAD. Read commits reachable from the end ref and not from the start ref: git log --no-merges --abbrev=7 START..END. Read the diffs before grouping. Every hash you print must be one of those abbreviations. If the range is empty, print one sentence that says so and stop.

Print only the note. No code fence and no preamble. Write in English.

Open with a level-2 heading whose text is What's Changed.

Then level-3 sections, skipping any section that has no items, in this order only. Copy each heading exactly:
- \"### 🚀 New Features\" for a capability an operator can newly use.
- \"### 🐛 Fixes\" for behavior that was wrong and is now corrected.
- \"### ⚡ Performance\" for the same behavior with less work.
- \"### 📝 Documentation\" for documentation only.

Do not add any other section. Omit a commit that does not change what an operator can do and is not documentation. Classify a mixed change by the code, not by the docs that mention it.

Each item is one list line. Start with an asterisk and a space, then a noun phrase that names the change, then a space, then parentheses containing one or more 7-character hashes separated by a comma and a space, oldest first. After the closing parenthesis, a space, an em dash, a space, then one or two sentences. The sentences say what an operator can do or what the product now does. Put commands, flags, config keys, slash commands, modes, and paths in backticks. When several commits are one change, keep a single item and list every hash. Do not paste commit subjects in as the sentences. Within a section, put the largest operator-facing change first.

After a blank line, end with a blockquote. Its label is bold Full Changelog. If origin is a GitHub remote, follow the label with the https compare URL for these two refs joined by three dots, leaving HEAD as HEAD, and strip a trailing .git. Otherwise follow the label with the two refs joined by three dots.

Range start: ${FROM}
Range end: ${TO}\n\n"
TASK="$BASE_MSG"
for arg in "$@"; do
    TASK+=$'\n'"$arg"
done

"$CMD" run --workspace . --approval review --work --show-thinking --prompt-set default --external-tools off --workspace-trust off --duration 1200 "$TASK"
