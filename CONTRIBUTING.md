# Contributing

Thank you for improving this reference implementation.

1. Create a task branch in a linked worktree under `.worktrees/`.
2. Keep the change focused on Google Pub/Sub integration with Creatio.
3. Do not commit credentials, tenant-specific configuration, customer identifiers, generated binaries, or restored Creatio references.
4. Update tests and documentation with behavior changes.
5. Run the validation commands in `AGENTS.md` and include the results in the pull request.

Changes to the native runtime loader must preserve immutable, versioned extraction and ZIP path traversal protection. Changes to acknowledgement behavior must prove that failed reply publishing leaves the request eligible for retry.
