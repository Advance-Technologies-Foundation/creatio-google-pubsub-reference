using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Google.Cloud.PubSub.V1;
using NUnit.Framework;
using AtfGooglePubSubReference.GooglePubSub;

namespace AtfGooglePubSubReference.Tests {

	[TestFixture]
	public sealed class GooglePubSubRuntimeLifecycleTests {
		[Test]
		[Description("Preserves live runtime state after a stop timeout so a second consumer cannot start.")]
		public void Stop_WhenJoinTimesOut_ShouldPreserveStateAndRejectDuplicateStart() {
			// Arrange
			var lifecycle = new GooglePubSubRuntimeLifecycle();
			var thread = new FakeRuntimeThread { IsAlive = true, JoinResult = false };
			CancellationToken cancellationToken = default;
			lifecycle.TryStart(token => {
				cancellationToken = token;
				return thread;
			});
			bool duplicateFactoryCalled = false;

			// Act
			GooglePubSubRuntimeStopResult result = lifecycle.Stop(TimeSpan.Zero);
			bool duplicateStarted = lifecycle.TryStart(token => {
				duplicateFactoryCalled = true;
				return new FakeRuntimeThread();
			});

			// Assert
			result.Stopped.Should().BeFalse("because the runtime thread did not join");
			result.TimedOut.Should().BeTrue("because the join deadline expired");
			cancellationToken.IsCancellationRequested.Should().BeTrue(
				"because the original consumer must still receive its shutdown signal");
			duplicateStarted.Should().BeFalse("because the original consumer may still be active");
			duplicateFactoryCalled.Should().BeFalse("because duplicate runtime construction must be prevented");
		}

		[Test]
		[Description("Clears lifecycle state only after the subscriber thread has joined successfully.")]
		public void Stop_WhenJoinSucceeds_ShouldAllowLaterStart() {
			// Arrange
			var lifecycle = new GooglePubSubRuntimeLifecycle();
			var firstThread = new FakeRuntimeThread { IsAlive = true, JoinResult = true };
			lifecycle.TryStart(token => firstThread);

			// Act
			GooglePubSubRuntimeStopResult result = lifecycle.Stop(TimeSpan.Zero);
			bool restarted = lifecycle.TryStart(token => new FakeRuntimeThread());

			// Assert
			result.Stopped.Should().BeTrue("because the original runtime thread joined");
			result.TimedOut.Should().BeFalse("because shutdown completed before the deadline");
			restarted.Should().BeTrue("because completed runtime state should be released");
		}

		[Test]
		[Description("Shuts down the long-lived reply publisher when the subscriber runtime exits.")]
		public void ShutdownReplyPublisher_WhenPublisherExists_ShouldInvokeShutdown() {
			// Arrange
			var publisher = new FakeReplyPublisher();
			Exception reportedException = null;

			// Act
			bool result = GooglePubSubSubscriberRuntime.ShutdownReplyPublisher(publisher,
				exception => reportedException = exception);

			// Assert
			result.Should().BeTrue("because publisher shutdown completed");
			publisher.ShutdownCalled.Should().BeTrue("because the long-lived publisher must release its clients");
			reportedException.Should().BeNull("because successful shutdown should not report an error");
		}

		private sealed class FakeRuntimeThread : IGooglePubSubRuntimeThread {
			public bool IsAlive { get; set; }
			public bool JoinResult { get; set; } = true;
			public bool Started { get; private set; }

			public void Start() {
				Started = true;
			}

			public bool Join(TimeSpan timeout) {
				return JoinResult;
			}
		}

		private sealed class FakeReplyPublisher : IGooglePubSubReplyPublisher {
			public bool ShutdownCalled { get; private set; }

			public Task PublishAsync(PubsubMessage message) {
				return Task.CompletedTask;
			}

			public Task ShutdownAsync(TimeSpan timeout) {
				ShutdownCalled = true;
				return Task.CompletedTask;
			}
		}
	}
}
