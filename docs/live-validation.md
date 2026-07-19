# Live validation

## Console smoke test

Copy `appsettings.dev.example.json` to the ignored `appsettings.dev.json`, configure project/topic/subscription names and a local service-account path, and run:

```powershell
dotnet run --project tools/GooglePubSubRoundTrip.Console -- "reference smoke test"
```

Success prints the Google request message ID and a response with the same correlation ID.

## Explicit E2E test

The E2E fixture is explicit so ordinary builds never touch Google Cloud or a Creatio environment. Configure Application Default Credentials and set:

- `GOOGLE_PUBSUB_PROJECT_ID`
- `GOOGLE_PUBSUB_REQUEST_TOPIC`
- `GOOGLE_PUBSUB_REPLY_SUBSCRIPTION`
- optional `GOOGLE_PUBSUB_TIMEOUT_SECONDS`

Then select the test by its full name:

```powershell
dotnet test tests/AtfGooglePubSubReference.E2E/AtfGooglePubSubReference.E2E.csproj `
  --filter "FullyQualifiedName~DeployedSubscriber_ShouldCompleteCorrelatedRoundTrip"
```

This validates the complete path: Google publish, deployed Creatio subscriber, Creatio data access, reply publish, and correlated Google pull.

Use isolated topics and subscriptions for automation. A shared reply subscription can acknowledge messages intended for another concurrent test client.
