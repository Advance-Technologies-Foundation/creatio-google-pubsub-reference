using System;
using FluentAssertions;
using NUnit.Framework;
using AtfGooglePubSubReference.EntryPoints.WebServices;
using AtfGooglePubSubReference.GooglePubSub;

namespace AtfGooglePubSubReference.Tests {

	[TestFixture]
	public sealed class GooglePubSubServiceAuthorizationTests {
		[Test]
		[Description("Uses the stable CanManageSolution Creatio operation for privileged service authorization.")]
		public void CanManageSolution_ShouldEvaluateStableCreatioOperation() {
			// Arrange
			string operationName = null;

			// Act
			bool granted = PrivilegedOperationAuthorization.CanManageSolution(operation => {
				operationName = operation;
				return true;
			});

			// Assert
			granted.Should().BeTrue("because the simulated Creatio operation grants access");
			operationName.Should().Be(Constants.ManageSolutionOperationName,
				"because both privileged endpoints must use the stable CanManageSolution operation");
		}

		[Test]
		[Description("Rejects publish requests before any Google Pub/Sub operation when CanManageSolution is denied.")]
		public void Publish_WhenCanManageSolutionIsDenied_ShouldReturnForbiddenWithoutPublishing() {
			// Arrange
			bool publishCalled = false;
			int statusCode = 0;
			var sut = new GooglePubSubPackageMessageService(
				() => false,
				request => {
					publishCalled = true;
					return "unexpected";
				},
				value => statusCode = value);

			// Act
			PublishGooglePackageMessageResponse response = sut.Publish(
				new PublishGooglePackageMessageRequest { Message = "hello" });

			// Assert
			response.Success.Should().BeFalse("because privileged publishing was denied");
			response.Error.Should().Be(Constants.PrivilegedOperationRequiredError,
				"because authorization failures use the stable public error contract");
			statusCode.Should().Be(403, "because denied privileged operations are forbidden");
			publishCalled.Should().BeFalse("because authorization must run before publishing");
		}

		[Test]
		[Description("Allows a publish request to execute after CanManageSolution is granted.")]
		public void Publish_WhenCanManageSolutionIsGranted_ShouldPublish() {
			// Arrange
			bool publishCalled = false;
			int statusCode = 0;
			var sut = new GooglePubSubPackageMessageService(
				() => true,
				request => {
					publishCalled = true;
					return "message-id";
				},
				value => statusCode = value);

			// Act
			PublishGooglePackageMessageResponse response = sut.Publish(
				new PublishGooglePackageMessageRequest { Message = "hello" });

			// Assert
			response.Success.Should().BeTrue("because the privileged operation was granted");
			response.MessageId.Should().Be("message-id", "because the publisher result is returned to the caller");
			statusCode.Should().Be(202, "because Google accepted the message for delivery");
			publishCalled.Should().BeTrue("because authorized requests should reach the publisher");
		}

		[Test]
		[Description("Rejects maintenance requests before stopping either runtime when CanManageSolution is denied.")]
		public void Stop_WhenCanManageSolutionIsDenied_ShouldReturnForbiddenWithoutStoppingRuntimes() {
			// Arrange
			bool subscriberStopCalled = false;
			bool packagePublisherStopCalled = false;
			int statusCode = 0;
			var sut = new NativeIntegrationMaintenanceService(
				() => false,
				() => {
					subscriberStopCalled = true;
					return new GooglePubSubRuntimeStopResult { Stopped = true };
				},
				() => packagePublisherStopCalled = true,
				value => statusCode = value);

			// Act
			StopNativeIntegrationsResponse response = sut.Stop();

			// Assert
			response.Success.Should().BeFalse("because privileged maintenance was denied");
			response.Error.Should().Be(Constants.PrivilegedOperationRequiredError,
				"because authorization failures use the stable public error contract");
			statusCode.Should().Be(403, "because denied privileged operations are forbidden");
			subscriberStopCalled.Should().BeFalse("because authorization must precede subscriber shutdown");
			packagePublisherStopCalled.Should().BeFalse("because authorization must precede publisher shutdown");
		}

		[Test]
		[Description("Reports an unavailable response when the subscriber has not stopped before its timeout.")]
		public void Stop_WhenSubscriberTimesOut_ShouldReturnTruthfulUnavailableResponse() {
			// Arrange
			bool packagePublisherStopCalled = false;
			int statusCode = 0;
			var sut = new NativeIntegrationMaintenanceService(
				() => true,
				() => new GooglePubSubRuntimeStopResult { WasRunning = true, TimedOut = true },
				() => packagePublisherStopCalled = true,
				value => statusCode = value);

			// Act
			StopNativeIntegrationsResponse response = sut.Stop();

			// Assert
			response.Success.Should().BeFalse("because the subscriber remains active after the timeout");
			response.GooglePubSubStopped.Should().BeFalse("because shutdown was not observed");
			response.GooglePubSubStopTimedOut.Should().BeTrue("because the join deadline expired");
			statusCode.Should().Be(503, "because the requested maintenance state is not yet available");
			packagePublisherStopCalled.Should().BeTrue(
				"because package publisher channels should still receive their shutdown signal");
		}
	}
}
