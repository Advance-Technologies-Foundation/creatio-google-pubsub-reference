# Load testing

The console provides a controlled-rate correlated round-trip load command:

```powershell
dotnet run --project tools/GooglePubSubRoundTrip.Console -- load <rate-per-second> <duration-seconds> <drain-seconds>
```

Example:

```powershell
dotnet run --project tools/GooglePubSubRoundTrip.Console -- load 50 60 30
```

It records each correlation ID before publishing, pulls replies concurrently, acknowledges received messages, and reports sent/received/missing counts plus p50/p95/p99 latency.

For trustworthy results:

- use dedicated empty topics and subscriptions per environment and run;
- verify that only the intended Creatio deployment consumes the request subscription;
- keep `GooglePubSubWorkerCount` and Google subscription flow control recorded with the result;
- separate the measurement interval from the drain interval;
- monitor Creatio, Google quotas, outstanding messages, and retry/dead-letter counts;
- warm the application before measurement and record whether credentials/native runtime were already initialized.

The harness is intended to reproduce behavior, not to publish universal capacity numbers. Sustainable throughput depends on message work, tenant topology, Google quotas, network latency, and worker concurrency.
