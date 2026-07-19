# Workspace diary

## 2026-07-19 - Extract standalone Google Pub/Sub reference
Context: Split the Google Pub/Sub implementation out of a mixed integration lab.
Decision: Use the dedicated package as the source, rename it to `AtfGooglePubSubReference`, assign package UID `399057c1-32ad-4031-b1cc-66de48aa1949`, regenerate all package-owned data-binding and record IDs, move Google settings into the package, and remove every dependency on the former lab package.
Discovery: The original runtime code was isolated, but its system settings lived in a shared package and its maintenance endpoint reflectively controlled that package. The live console also combined multiple transport responsibilities.
Files: packages/AtfGooglePubSubReference, tests/AtfGooglePubSubReference, tests/AtfGooglePubSubReference.E2E, tools/GooglePubSubRoundTrip.Console, docs
Impact: The repository is now an independent, focused Google Pub/Sub reference workspace.

## 2026-07-19 - Harden privileged services and runtime shutdown
Context: Publication review identified missing privileged authorization and an optimistic subscriber stop result.
Decision: Gate both web services with `CanManageSolution`, retain lifecycle state after join timeout, report HTTP 503 truthfully, and shut down the subscriber-owned reply publisher in a `finally` path.
Discovery: Clearing cancellation/thread state after a failed join allowed a second consumer to start while the first could still be active. The long-lived `PublisherClient` also required its own shutdown, separate from the low-level publisher service channel pool.
Files: packages/AtfGooglePubSubReference/Files/src/cs/EntryPoints/WebServices, packages/AtfGooglePubSubReference/Files/src/cs/GooglePubSub/GooglePubSubSubscriberRuntime.cs, tests/AtfGooglePubSubReference
Impact: Unauthorized requests have no side effects, and maintenance responses now describe observed runtime state instead of requested state.
