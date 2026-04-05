---
name: commit
description: Stage relevant files and commit all uncommitted changes with a proper message.
---

## Context

- Current git status: !`git status`
- Staged and unstaged changes: !`git diff HEAD`
- Recent commits (for message style): !`git log --oneline -10`

## Your task

1. Review all uncommitted changes shown above.
2. Stage all relevant modified and new files by name (avoid `git add -A` — skip secrets, binaries, or clearly unrelated files).
3. Write a concise commit message (imperative mood, under 72 chars subject line) that reflects *why* the changes were made, not just what files changed.
4. Commit with the message using a HEREDOC. Append the Co-Authored-By trailer:
   `Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>`
5. Run `git status` after committing to confirm success.

Stage and commit in a single response with no extra commentary.