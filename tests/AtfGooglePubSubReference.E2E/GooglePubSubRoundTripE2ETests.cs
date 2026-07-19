using FluentAssertions;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using NUnit.Framework;

namespace AtfGooglePubSubReference.E2E;

[TestFixture]
[Category("E2E")]
public sealed class GooglePubSubRoundTripE2ETests {
	[Test]
	[Explicit("Requires a deployed Creatio package and configured Google Pub/Sub resources.")]
	[Description("Publishes a correlated request and verifies that the deployed Creatio subscriber publishes its reply.")]
	public async Task DeployedSubscriber_ShouldCompleteCorrelatedRoundTrip() {
		// Arrange
		string projectId = RequireEnvironmentVariable("GOOGLE_PUBSUB_PROJECT_ID");
		string requestTopic = RequireEnvironmentVariable("GOOGLE_PUBSUB_REQUEST_TOPIC");
		string replySubscription = RequireEnvironmentVariable("GOOGLE_PUBSUB_REPLY_SUBSCRIPTION");
		int timeoutSeconds = int.TryParse(Environment.GetEnvironmentVariable("GOOGLE_PUBSUB_TIMEOUT_SECONDS"),
			out int configuredTimeout) ? configuredTimeout : 30;
		PublisherServiceApiClient publisher = await PublisherServiceApiClient.CreateAsync();
		SubscriberServiceApiClient subscriber = await SubscriberServiceApiClient.CreateAsync();
		Guid correlationId = Guid.NewGuid();
		string requestText = $"ATF Google Pub/Sub E2E {correlationId:D}";
		var message = new PubsubMessage { Data = ByteString.CopyFromUtf8(requestText) };
		message.Attributes.Add("correlationId", correlationId.ToString("D"));

		// Act
		await publisher.PublishAsync(TopicName.FromProjectTopic(projectId, requestTopic), new[] { message });
		PubsubMessage? reply = await WaitForReply(subscriber,
			SubscriptionName.FromProjectSubscription(projectId, replySubscription), correlationId,
			TimeSpan.FromSeconds(timeoutSeconds));

		// Assert
		reply.Should().NotBeNull("because the deployed Creatio subscriber should publish a correlated reply");
		reply!.Data.ToStringUtf8().Should().Contain(requestText,
			"because the reply should preserve the request payload");
	}

	private static async Task<PubsubMessage?> WaitForReply(SubscriberServiceApiClient subscriber,
			SubscriptionName subscription, Guid correlationId, TimeSpan timeout) {
		using var cancellation = new CancellationTokenSource(timeout);
		try {
			while (!cancellation.IsCancellationRequested) {
				PullResponse response = await subscriber.PullAsync(subscription, 10, cancellation.Token);
				foreach (ReceivedMessage received in response.ReceivedMessages) {
					await subscriber.AcknowledgeAsync(subscription, new[] { received.AckId });
					if (received.Message.Attributes.TryGetValue("correlationId", out string? value) &&
							Guid.TryParse(value, out Guid actual) && actual == correlationId) {
						return received.Message;
					}
				}
			}
		} catch (OperationCanceledException) when (cancellation.IsCancellationRequested) {
			return null;
		}
		return null;
	}

	private static string RequireEnvironmentVariable(string name) {
		return Environment.GetEnvironmentVariable(name) ??
			throw new InvalidOperationException($"Environment variable {name} is required.");
	}
}
