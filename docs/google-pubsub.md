# Google Pub/Sub implementation

## Runtime lifecycle

`GooglePubSubAppEventListener` starts one package-owned background thread during Creatio application startup and stops it during application shutdown. The thread creates long-lived Google `SubscriberClient` and `PublisherClient` instances and waits on a cancellation token.

Configuration is loaded once at startup. A worker restart is required after changing subscriber settings. `GooglePubSubWorkerCount` is clamped to 1-32 and controls both Google client counts and the per-subscriber flow-control element limit.

If required settings are missing, startup logs a warning and leaves the subscriber disabled instead of failing the Creatio application.

## Message contract

Requests use UTF-8 message data and a GUID-valued `correlationId` attribute. A valid request produces:

```text
I see your message <request body> by <current Creatio contact name>
```

The response preserves the same `correlationId`. The subscriber reads the current Creatio user's contact name through an `EntitySchemaQuery`, publishes the reply, and then acknowledges the request. Invalid correlation IDs and failed processing are negatively acknowledged.

## Publisher endpoint

`POST /rest/GooglePubSubPackageMessageService/Publish` accepts:

```json
{
  "message": "hello"
}
```

It reads project, request-topic, and secure credential settings from Creatio and publishes with `PublisherServiceApiClient`. A successful request returns HTTP 202 and the Google message ID. Validation returns HTTP 400; Google/configuration failures return HTTP 502.

## Maintenance endpoint

`POST /rest/NativeIntegrationMaintenanceService/Stop` stops the streaming subscriber and shuts down default publisher channels. This is useful for graceful application shutdown and diagnostics.

It does not unload `grpc_csharp_ext` from the worker process. A native-runtime replacement still requires the worker process to terminate; see [native runtime lifecycle](native-runtime.md).

## Credentials and settings

The service-account JSON is encoded outside Creatio:

```powershell
$bytes = [IO.File]::ReadAllBytes('service-account.json')
[Convert]::ToBase64String($bytes)
```

Store the result in `GooglePubSubServiceAccountJsonBase64`, whose setting definition is `SecureText`. Never add it to a package data binding, source file, console configuration, test fixture, or log.

The service account needs access only to the configured topics and subscriptions. Use separate identities and resources per environment.
