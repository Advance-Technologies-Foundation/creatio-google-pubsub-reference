using System;
using System.IO;
using System.IO.Compression;
using FluentAssertions;
using NUnit.Framework;
using AtfGooglePubSubReference.GooglePubSub;

namespace AtfGooglePubSubReference.Tests {

	[TestFixture]
	public sealed class GoogleGrpcNativeRuntimeProvisionerTests {
		private string _directory;

		[SetUp]
		public void SetUp() {
			_directory = Path.Combine(Path.GetTempPath(), nameof(GoogleGrpcNativeRuntimeProvisionerTests),
				Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(_directory);
		}

		[TearDown]
		public void TearDown() {
			if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
		}

		[Test]
		[Description("Extracts the selected RID, writes its integrity marker, and reuses identical immutable content.")]
		public void Provision_ShouldExtractRequestedRuntimeAndBeIdempotent() {
			// Arrange
			string artifact = CreateArtifact("native-v1");
			string destination = Path.Combine(_directory, "conf", "native", "grpc-core", "2.46.6", "win-x64");
			var sut = new GoogleGrpcNativeRuntimeProvisioner();

			// Act
			string first = sut.Provision(artifact, destination, "win-x64", "grpc_csharp_ext.x64.dll");
			Action second = () => sut.Provision(artifact, destination, "win-x64", "grpc_csharp_ext.x64.dll");

			// Assert
			File.ReadAllText(first).Should().Be("native-v1", "because the selected native asset must be extracted");
			File.Exists(Path.Combine(destination, ".artifact.sha256")).Should().BeTrue(
				"because immutable runtime content must retain its artifact digest");
			second.Should().NotThrow("because provisioning identical immutable content must be idempotent");
		}

		[Test]
		[Description("Rejects an attempt to overwrite a version directory with content from a different artifact.")]
		public void Provision_WhenVersionContainsDifferentArtifact_ShouldRejectReplacement() {
			// Arrange
			string first = CreateArtifact("v1", "first.zip");
			string second = CreateArtifact("v2", "second.zip");
			string destination = Path.Combine(_directory, "runtime");
			var sut = new GoogleGrpcNativeRuntimeProvisioner();
			sut.Provision(first, destination, "win-x64", "grpc_csharp_ext.x64.dll");

			// Act
			Action action = () => sut.Provision(second, destination, "win-x64", "grpc_csharp_ext.x64.dll");

			// Assert
			action.Should().Throw<InvalidDataException>(
				"because loaded native runtime versions must never be replaced in place")
				.WithMessage("*new runtime version*");
		}

		[Test]
		[Description("Rejects a native artifact entry that would escape the versioned runtime directory.")]
		public void Provision_WhenArtifactContainsTraversalPath_ShouldRejectEntry() {
			// Arrange
			string artifact = Path.Combine(_directory, "traversal.zip");
			using (ZipArchive archive = ZipFile.Open(artifact, ZipArchiveMode.Create)) {
				archive.CreateEntry("win-x64/native/grpc_csharp_ext.x64.dll");
				archive.CreateEntry("win-x64/../../outside.dll");
			}
			string destination = Path.Combine(_directory, "runtime");
			var sut = new GoogleGrpcNativeRuntimeProvisioner();

			// Act
			Action action = () => sut.Provision(artifact, destination, "win-x64", "grpc_csharp_ext.x64.dll");

			// Assert
			action.Should().Throw<InvalidDataException>(
				"because a package artifact must not write outside its staging directory")
				.WithMessage("*invalid entry path*");
			File.Exists(Path.Combine(_directory, "outside.dll")).Should().BeFalse(
				"because a rejected traversal entry must never be materialized");
		}

		private string CreateArtifact(string content, string fileName = "grpc.zip") {
			string path = Path.Combine(_directory, fileName);
			using (ZipArchive archive = ZipFile.Open(path, ZipArchiveMode.Create))
			using (StreamWriter writer = new StreamWriter(
				archive.CreateEntry("win-x64/native/grpc_csharp_ext.x64.dll").Open())) {
				writer.Write(content);
			}
			return path;
		}
	}
}
