using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Grpc.Core;

namespace AtfGooglePubSubReference.ConsoleApp;

internal static class Program {
	private const string CorrelationIdAttributeName = "correlationId";

	private static async Task<int> Main(string[] args) {
		GooglePubSubConsoleSettings? settings = await LoadSettings();
		if (settings == null) {
			return 2;
		}
		if (args.Length > 0 && string.Equals(args[0], "load", StringComparison.OrdinalIgnoreCase)) {
			int rate = args.Length > 1 && int.TryParse(args[1], out int parsedRate) ? parsedRate : 100;
			int duration = args.Length > 2 && int.TryParse(args[2], out int parsedDuration) ? parsedDuration : 60;
			int drain = args.Length > 3 && int.TryParse(args[3], out int parsedDrain) ? parsedDrain : 30;
			return await RunLoad(settings, rate, duration, drain);
		}
		string message = args.Length == 0 ? "Hello from the Google Pub/Sub reference" : string.Join(" ", args);
		return await RunRoundTrip(settings, message);
	}

	private static async Task<GooglePubSubConsoleSettings?> LoadSettings() {
		string path = Path.Combine(AppContext.BaseDirectory, "appsettings.dev.json");
		if (!File.Exists(path)) {
			await Console.Error.WriteLineAsync(
				"Copy appsettings.dev.example.json to appsettings.dev.json and configure Google Pub/Sub.");
			return null;
		}
		await using FileStream stream = File.OpenRead(path);
		ConsoleConfiguration? configuration = await JsonSerializer.DeserializeAsync<ConsoleConfiguration>(stream,
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		GooglePubSubConsoleSettings? settings = configuration?.GooglePubSub;
		if (settings == null || string.IsNullOrWhiteSpace(settings.ProjectId) ||
				string.IsNullOrWhiteSpace(settings.RequestTopic) ||
				string.IsNullOrWhiteSpace(settings.ReplySubscription) ||
				string.IsNullOrWhiteSpace(settings.CredentialPath)) {
			await Console.Error.WriteLineAsync("All GooglePubSub settings are required.");
			return null;
		}
		return settings;
	}

	private static GoogleCredential CreateCredential(GooglePubSubConsoleSettings settings) {
		string path = Path.GetFullPath(settings.CredentialPath, Directory.GetCurrentDirectory());
		return CredentialFactory.FromFile<ServiceAccountCredential>(path).ToGoogleCredential();
	}

	private static async Task<int> RunRoundTrip(GooglePubSubConsoleSettings settings, string originalMessage) {
		GoogleCredential credential = CreateCredential(settings);
		PublisherServiceApiClient publisher = new PublisherServiceApiClientBuilder {
			GoogleCredential = credential
		}.Build();
		SubscriberServiceApiClient subscriber = new SubscriberServiceApiClientBuilder {
			GoogleCredential = credential
		}.Build();
		Guid correlationId = Guid.NewGuid();
		var request = new PubsubMessage { Data = ByteString.CopyFromUtf8(originalMessage) };
		request.Attributes.Add(CorrelationIdAttributeName, correlationId.ToString("D"));
		PublishResponse published = await publisher.PublishAsync(
			TopicName.FromProjectTopic(settings.ProjectId, settings.RequestTopic), new[] { request });
		await Console.Out.WriteLineAsync($"Sent [{correlationId}] ({published.MessageIds[0]}): {originalMessage}");

		SubscriptionName subscription = SubscriptionName.FromProjectSubscription(settings.ProjectId,
			settings.ReplySubscription);
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
		try {
			while (!cancellation.IsCancellationRequested) {
				PullResponse response = await subscriber.PullAsync(subscription, 10, cancellation.Token);
				foreach (ReceivedMessage received in response.ReceivedMessages) {
					await subscriber.AcknowledgeAsync(subscription, new[] { received.AckId });
					if (TryMatch(received.Message, correlationId)) {
						await Console.Out.WriteLineAsync($"Received [{correlationId}]: " +
							received.Message.Data.ToStringUtf8());
						return 0;
					}
				}
			}
		} catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
			// Expected timeout path.
		} catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled &&
				cancellation.IsCancellationRequested) {
			// Grpc.Core represents a client-side timeout as a cancelled RPC.
		}
		await Console.Error.WriteLineAsync("No correlated reply was received before the timeout.");
		return 1;
	}

	private static async Task<int> RunLoad(GooglePubSubConsoleSettings settings, int ratePerSecond,
			int durationSeconds, int drainSeconds) {
		if (ratePerSecond < 1 || durationSeconds < 1 || drainSeconds < 0) {
			await Console.Error.WriteLineAsync("Usage: load [messages-per-second] [duration-seconds] [drain-seconds]");
			return 2;
		}
		GoogleCredential credential = CreateCredential(settings);
		PublisherServiceApiClient publisher = new PublisherServiceApiClientBuilder {
			GoogleCredential = credential
		}.Build();
		SubscriberServiceApiClient subscriber = new SubscriberServiceApiClientBuilder {
			GoogleCredential = credential
		}.Build();
		TopicName requestTopic = TopicName.FromProjectTopic(settings.ProjectId, settings.RequestTopic);
		SubscriptionName replySubscription = SubscriptionName.FromProjectSubscription(settings.ProjectId,
			settings.ReplySubscription);
		var sentAt = new ConcurrentDictionary<Guid, long>();
		var latencies = new ConcurrentBag<double>();
		using var cancellation = new CancellationTokenSource();
		Task receiveTask = CollectReplies(subscriber, replySubscription, sentAt, latencies, cancellation.Token);
		var publishTasks = new List<Task>();
		Stopwatch run = Stopwatch.StartNew();
		long sequence = 0;
		while (run.Elapsed < TimeSpan.FromSeconds(durationSeconds)) {
			long targetSequence = (long)Math.Floor(run.Elapsed.TotalSeconds * ratePerSecond);
			while (sequence <= targetSequence) {
				Guid correlationId = Guid.NewGuid();
				sentAt[correlationId] = Stopwatch.GetTimestamp();
				var message = new PubsubMessage { Data = ByteString.CopyFromUtf8($"load:{sequence}") };
				message.Attributes.Add(CorrelationIdAttributeName, correlationId.ToString("D"));
				publishTasks.Add(publisher.PublishAsync(requestTopic, new[] { message }));
				sequence++;
			}
			await Task.Delay(1);
		}
		await Task.WhenAll(publishTasks);
		await Task.Delay(TimeSpan.FromSeconds(drainSeconds));
		cancellation.Cancel();
		await receiveTask;

		double[] ordered = latencies.OrderBy(value => value).ToArray();
		int sent = checked((int)sequence);
		await Console.Out.WriteLineAsync($"Sent: {sent}; received: {ordered.Length}; missing: {sent - ordered.Length}");
		if (ordered.Length > 0) {
			await Console.Out.WriteLineAsync($"Latency ms p50={Percentile(ordered, 0.50):F1}, " +
				$"p95={Percentile(ordered, 0.95):F1}, p99={Percentile(ordered, 0.99):F1}");
		}
		return ordered.Length == sent ? 0 : 1;
	}

	private static async Task CollectReplies(SubscriberServiceApiClient subscriber, SubscriptionName subscription,
			ConcurrentDictionary<Guid, long> sentAt, ConcurrentBag<double> latencies,
			CancellationToken cancellationToken) {
		while (!cancellationToken.IsCancellationRequested) {
			try {
				PullResponse response = await subscriber.PullAsync(subscription, 1000, cancellationToken);
				var acknowledgementIds = new List<string>();
				foreach (ReceivedMessage received in response.ReceivedMessages) {
					acknowledgementIds.Add(received.AckId);
					if (received.Message.Attributes.TryGetValue(CorrelationIdAttributeName, out string value) &&
							Guid.TryParse(value, out Guid correlationId) &&
							sentAt.TryRemove(correlationId, out long started)) {
						latencies.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
					}
				}
				if (acknowledgementIds.Count > 0) {
					await subscriber.AcknowledgeAsync(subscription, acknowledgementIds, cancellationToken);
				}
			} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
				return;
			} catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled &&
					cancellationToken.IsCancellationRequested) {
				return;
			}
		}
	}

	private static bool TryMatch(PubsubMessage message, Guid correlationId) {
		return message.Attributes.TryGetValue(CorrelationIdAttributeName, out string value) &&
			Guid.TryParse(value, out Guid receivedId) && receivedId == correlationId;
	}

	private static double Percentile(double[] ordered, double percentile) {
		int index = (int)Math.Ceiling(percentile * ordered.Length) - 1;
		return ordered[Math.Max(0, Math.Min(ordered.Length - 1, index))];
	}
}

internal sealed class ConsoleConfiguration {
	public GooglePubSubConsoleSettings? GooglePubSub { get; init; }
}

internal sealed class GooglePubSubConsoleSettings {
	public string ProjectId { get; init; } = string.Empty;
	public string RequestTopic { get; init; } = string.Empty;
	public string ReplySubscription { get; init; } = string.Empty;
	public string CredentialPath { get; init; } = string.Empty;
	public int TimeoutSeconds { get; init; } = 30;
}
