# Creatio Google Pub/Sub reference

This repository is a complete, focused Creatio workspace demonstrating a bidirectional Google Pub/Sub integration.

The reference contains:

- a Creatio package with a startup-managed streaming subscriber;
- a publisher REST endpoint;
- correlated request/reply handling;
- bounded subscriber and publisher client concurrency;
- versioned, self-provisioned `Grpc.Core` native runtimes;
- a graceful maintenance endpoint;
- explicit `CanManageSolution` authorization for both package web services;
- unit tests, an explicit live E2E test, and a standalone round-trip/load console.

## Architecture

```text
Console or external producer
        |
        v
Google request topic -> request subscription -> Creatio subscriber
                                                   |
                                                   v
                                            Contact lookup
                                                   |
                                                   v
Google reply topic -> reply subscription -> Console or external consumer
```

Every request carries a `correlationId` attribute. Creatio acknowledges the request only after publishing the correlated reply. A processing or publish failure returns `Nack`, allowing Google Pub/Sub to redeliver it.

## Prerequisites

- Creatio with file-system workspace support and restored local reference assemblies;
- clio configured for the target Creatio environment;
- a Google Cloud project with request/reply topics and subscriptions;
- a service account authorized to publish and subscribe to those resources;
- .NET 8 SDK for local builds and the console.

Do not commit the service-account JSON. The package expects its Base64 representation in a secure Creatio system setting; local tools use Application Default Credentials or an ignored credential file.

## Build

Restore the workspace references into `.application/`, then run:

```powershell
dotnet build MainSolution.slnx -c dev-n8
dotnet test tests/AtfGooglePubSubReference/AtfGooglePubSubReference.Tests.csproj -c dev-n8
```

Use `dev-nf` only when `.application/net-framework` has also been restored.

## Configure Creatio

The package binds the setting definitions and a safe default worker count, but no environment-specific values or credentials.

| System setting code | Purpose |
|---|---|
| `GooglePubSubProjectId` | Google Cloud project ID |
| `GooglePubSubRequestTopic` | Topic used by the package publisher and external requests |
| `GooglePubSubRequestSubscription` | Subscription consumed by Creatio |
| `GooglePubSubReplyTopic` | Topic where Creatio publishes correlated replies |
| `GooglePubSubWorkerCount` | Streaming subscriber/publisher client count, clamped to 1-32 |
| `GooglePubSubServiceAccountJsonBase64` | Base64-encoded service-account JSON stored as `SecureText` |

Restart the Creatio worker after setting configuration so the application-start listener loads the subscriber.

## Verify end to end

Copy the console configuration, supply a local credential path, then send a request:

```powershell
Copy-Item tools/GooglePubSubRoundTrip.Console/appsettings.dev.example.json `
  tools/GooglePubSubRoundTrip.Console/appsettings.dev.json
dotnet run --project tools/GooglePubSubRoundTrip.Console -- "Hello from the reference"
```

The same console can run a controlled-rate load:

```powershell
dotnet run --project tools/GooglePubSubRoundTrip.Console -- load 50 60 30
```

The explicit E2E test offers an automated equivalent; see [live validation](docs/live-validation.md).

## Read next

- [Implementation and configuration](docs/google-pubsub.md)
- [Native runtime lifecycle](docs/native-runtime.md)
- [Live validation](docs/live-validation.md)
- [Load testing](docs/load-testing.md)

This is vetted reference code, not a drop-in production policy. Review IAM scope, retry/dead-letter behavior, observability, data classification, regionality, and operational ownership for your deployment.
