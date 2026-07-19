# Agent instructions

This repository is a complete, standalone Creatio reference workspace for Google Pub/Sub. Keep it directly cloneable, buildable, testable, and deployable after the documented prerequisites are supplied.

## Working model

- Treat the repository root as the primary checkout. Create linked worktrees only under `.worktrees/`.
- Keep each task isolated on its own branch and worktree. Do not edit another agent's worktree.
- Never add customer names, live tenant addresses, project IDs, topic names, service-account JSON, tokens, or passwords.
- `appsettings.dev.json`, `.secrets/`, `.application/`, and workspace environment settings are local-only.

## Structure

- Creatio package: `packages/AtfGooglePubSubReference/`
- Unit tests: `tests/AtfGooglePubSubReference/`
- Live round-trip/load console: `tools/GooglePubSubRoundTrip.Console/`
- Human and agent guidance: `docs/`

## Implementation invariants

- Keep the package independent. It may depend on Creatio platform packages, but not on another reference repository.
- Never bind the service-account credential or deployment-specific project/topic/subscription values into the package.
- Preserve the immutable native-runtime contract: build a version-pinned ZIP, extract only the active RID into `conf/native/grpc-core/<version>/<rid>`, verify its hash, and never replace an existing version directory in place.
- Stopping Google clients is a graceful managed shutdown; it does not unload an already loaded native module. Updating a native runtime version requires a full worker-process recycle.
- A subscriber acknowledges a request only after its correlated reply has been published successfully.

## Validation

Restore Creatio references into `.application/`, then run:

```powershell
dotnet build MainSolution.slnx -c dev-n8
dotnet test tests/AtfGooglePubSubReference/AtfGooglePubSubReference.Tests.csproj -c dev-n8
dotnet build tools/GooglePubSubRoundTrip.Console/GooglePubSubRoundTrip.Console.csproj -c Release
```

Use explicit Arrange/Act/Assert sections in tests. Prefer NUnit and FluentAssertions. Add a clear reason to assertions where the assertion library supports it.

## Diary

Append concise, factual discoveries and decisions to `.codex/workspace-diary.md` after non-trivial work. Never rewrite earlier entries.
