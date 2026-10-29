---
name: git-commit
description: >
  Use this skill when the user asks to commit the current change, write a
  commit message, or split work into commits in this repository. Covers how
  to group changes into commits and the mandatory Conventional Commits
  subject format. Trigger it for requests like "commit this", "write the
  commit message", or "make the commits", even when the user does not
  mention the format.
---

# Git Commit

## Workflow

1. Inspect the change with `git status`, `git diff`, and `git diff --staged`.
   Use `git log` to see recent subjects when the type is unclear.
2. If staged and unstaged edits belong to the same change, stage the rest and
   make one commit.
3. If one commit is not enough, create multiple commits, each with a
   compliant subject.

## Message format (mandatory)

Use Conventional Commits. Write each subject on one line:

```text
<type>: <summary>
```

- Lowercase type, then a colon, then exactly one space.
- Allowed types: `feat`, `fix`, `docs`, `chore`, `refactor`, `test`, `perf`,
  `build`, `ci`, `revert`, `init`.
- Summary in plain English, imperative mood ("add", not "added").
- No trailing period. No emoji.
- Subject is 72 characters or fewer.

Examples:

```text
feat: add prompt set selection to crystal run
fix: keep composer cursor after paste
docs: describe bridge opt-in capabilities
```
