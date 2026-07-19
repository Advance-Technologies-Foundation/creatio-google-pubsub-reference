using System;
using FluentAssertions;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using NUnit.Framework;
using AtfGooglePubSubReference;
using AtfGooglePubSubReference.GooglePubSub;

namespace AtfGooglePubSubReference.Tests {

	[TestFixture]
	public sealed class GooglePubSubRoundTripHandlerTests {
		[Test]
		[Description("Preserves a valid correlation ID and includes the request text and Creatio contact in the reply.")]
		public void TryCreateReply_WhenRequestIsValid_ShouldPreserveCorrelationAndBuildReply() {
			// Arrange
			Guid correlationId = Guid.NewGuid();
			var message = new PubsubMessage { Data = ByteString.CopyFromUtf8("hello") };
			message.Attributes.Add(Constants.CorrelationIdAttributeName, correlationId.ToString("D"));

			// Act
			bool result = GooglePubSubRoundTripHandler.TryCreateReply(message, "Reference User",
				out string actualCorrelationId, out string reply);

			// Assert
			result.Should().BeTrue("because a valid correlated request should produce a reply");
			actualCorrelationId.Should().Be(correlationId.ToString("D"),
				"because the response must preserve the request correlation ID");
			reply.Should().Be("I see your message hello by Reference User",
				"because the reply contract includes the payload and current contact");
		}

		[TestCase(null)]
		[TestCase("not-a-guid")]
		[Description("Rejects requests without a valid GUID correlation attribute.")]
		public void TryCreateReply_WhenCorrelationIsMissingOrInvalid_ShouldReject(string correlationId) {
			// Arrange
			var message = new PubsubMessage { Data = ByteString.CopyFromUtf8("hello") };
			if (correlationId != null) {
				message.Attributes.Add(Constants.CorrelationIdAttributeName, correlationId);
			}

			// Act
			bool result = GooglePubSubRoundTripHandler.TryCreateReply(message, "Reference User",
				out string actualCorrelationId, out string reply);

			// Assert
			result.Should().BeFalse("because invalid correlation cannot produce a routable reply");
			actualCorrelationId.Should().BeNull("because no valid correlation ID was supplied");
			reply.Should().BeNull("because rejected input must not produce a reply body");
		}
	}
}
