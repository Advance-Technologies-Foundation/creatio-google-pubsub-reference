using FluentAssertions;
using NUnit.Framework;
using AtfGooglePubSubReference.GooglePubSub;

namespace AtfGooglePubSubReference.Tests {

	[TestFixture]
	public sealed class GooglePubSubSubscriberRuntimeTests {

		[TestCase(-1, 1)]
		[TestCase(0, 1)]
		[TestCase(1, 1)]
		[TestCase(16, 16)]
		[TestCase(32, 32)]
		[TestCase(33, 32)]
		[Description("Clamps the configured client count to the supported inclusive range of 1 through 32.")]
		public void NormalizeWorkerCount_ShouldClampToSupportedRange(int value, int expected) {
			// Arrange and Act
			int actual = GooglePubSubSubscriberRuntime.NormalizeWorkerCount(value);

			// Assert
			actual.Should().Be(expected, "because subscriber client counts outside 1-32 are unsupported");
		}
	}
}
