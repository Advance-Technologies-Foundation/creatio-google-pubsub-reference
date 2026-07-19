using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Google.Api.Gax.Grpc;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;

namespace AtfGooglePubSubReference.GooglePubSub {

	internal static class GooglePubSubPackageSettings {
		internal const string PackageName = "AtfGooglePubSubReference";
		internal const string ProjectIdSettingCode = Constants.ProjectIdSettingCode;
		internal const string RequestTopicSettingCode = Constants.RequestTopicSettingCode;
		internal const string RequestSubscriptionSettingCode = Constants.RequestSubscriptionSettingCode;
		internal const string ReplyTopicSettingCode = Constants.ReplyTopicSettingCode;
		internal const string CredentialSettingCode = Constants.CredentialSettingCode;
	}

	internal static class GooglePubSubPackagePublisher {
		internal static async Task<string> PublishAsync(string projectId, string topic, string credentialBase64,
				string message, CancellationToken cancellationToken) {
			return await PublishAsync(projectId, topic, credentialBase64, message, null, cancellationToken)
				.ConfigureAwait(false);
		}

		internal static async Task<string> PublishAsync(string projectId, string topic, string credentialBase64,
				string message, IDictionary<string, string> attributes, CancellationToken cancellationToken) {
			GoogleGrpcPackageNativeLibraryLoader.Load();
			byte[] bytes = Convert.FromBase64String(credentialBase64);
			ServiceAccountCredential credential = CredentialFactory.FromJson<ServiceAccountCredential>(
				System.Text.Encoding.UTF8.GetString(bytes));
			PublisherServiceApiClient client = new PublisherServiceApiClientBuilder {
				GoogleCredential = credential.ToGoogleCredential(),
				GrpcAdapter = GrpcCoreAdapter.Instance
			}.Build();
			var pubsubMessage = new PubsubMessage { Data = ByteString.CopyFromUtf8(message) };
			if (attributes != null) {
				pubsubMessage.Attributes.Add(attributes);
			}
			PublishResponse response = await client.PublishAsync(TopicName.FromProjectTopic(projectId, topic),
				new[] { pubsubMessage }, cancellationToken)
				.ConfigureAwait(false);
			return response.MessageIds.Count == 0 ? null : response.MessageIds[0];
		}

		internal static void Stop() {
			PublisherServiceApiClient.ShutdownDefaultChannelsAsync().ConfigureAwait(false).GetAwaiter().GetResult();
		}
	}

	internal static class GoogleGrpcPackageNativeLibraryLoader {
		private const int RtldNow = 2;
		private static readonly object SyncRoot = new object();
		private static readonly GoogleGrpcNativeRuntimeProvisioner RuntimeProvisioner =
			new GoogleGrpcNativeRuntimeProvisioner();
		private static bool _isLoaded;

		internal static void Load() {
			if (_isLoaded) return;
			lock (SyncRoot) {
				if (_isLoaded) return;
				string path = FindPackagedPath();
				IntPtr handle = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
					? LoadLibrary(path)
					: Dlopen(path, RtldNow);
				if (handle == IntPtr.Zero) {
					throw new InvalidOperationException("The packaged Google gRPC native library could not be loaded: " + path);
				}
				_isLoaded = true;
			}
		}

		private static string FindPackagedPath() {
			Architecture architecture = RuntimeInformation.ProcessArchitecture;
			string runtime;
			string fileName;
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				runtime = architecture == Architecture.X86 ? "win-x86" : "win-x64";
				fileName = architecture == Architecture.X86 ? "grpc_csharp_ext.x86.dll" : "grpc_csharp_ext.x64.dll";
			} else {
				runtime = architecture == Architecture.Arm64 ? "linux-arm64" : "linux-x64";
				fileName = architecture == Architecture.Arm64 ? "libgrpc_csharp_ext.arm64.so" : "libgrpc_csharp_ext.x64.so";
			}
			string relativePath = Path.Combine("runtimes", runtime, "native", fileName);
			foreach (string root in GetSearchRoots()) {
				if (Directory.Exists(Path.Combine(root, "conf")) &&
						Directory.Exists(Path.Combine(root, "Terrasoft.Configuration"))) {
					string destination = Path.Combine(root, "conf", "native", Constants.RuntimeProductName,
						Constants.NativeRuntimeVersion, runtime);
					string versionedPath = Path.Combine(destination, "native", fileName);
					if (File.Exists(versionedPath)) return versionedPath;
					string artifactPath = FindArtifactPath(root);
					if (!string.IsNullOrWhiteSpace(artifactPath)) {
						return RuntimeProvisioner.Provision(artifactPath, destination, runtime, fileName);
					}
				}
				foreach (string candidate in new[] {
					Path.Combine(root, "conf", relativePath),
					Path.Combine(root, relativePath),
					Path.Combine(root, "Terrasoft.Configuration", "Pkg", GooglePubSubPackageSettings.PackageName,
						"Files", "Bin", relativePath),
					Path.Combine(root, "Terrasoft.Configuration", "Pkg", GooglePubSubPackageSettings.PackageName,
						"Files", "Bin", "netstandard", relativePath)
				}) {
					if (File.Exists(candidate)) return candidate;
				}
			}
			throw new FileNotFoundException("The packaged Google gRPC native library could not be discovered.", relativePath);
		}

		private static string FindArtifactPath(string applicationRoot) {
			string packageBin = Path.Combine(applicationRoot, "Terrasoft.Configuration", "Pkg",
				GooglePubSubPackageSettings.PackageName, "Files", "Bin");
			string[] candidates = {
				Path.Combine(packageBin, Constants.NativeArtifactsDirectoryName,
					Constants.NativeRuntimeArtifactFileName),
				Path.Combine(packageBin, "netstandard", Constants.NativeArtifactsDirectoryName,
					Constants.NativeRuntimeArtifactFileName)
			};
			return Array.Find(candidates, File.Exists);
		}

		private static IEnumerable<string> GetSearchRoots() {
			var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			string location = typeof(GoogleGrpcPackageNativeLibraryLoader).GetTypeInfo().Assembly.Location;
			if (!string.IsNullOrWhiteSpace(location)) {
				AddWithParents(roots, Path.GetDirectoryName(location));
			}
			AddWithParents(roots, AppContext.BaseDirectory);
			AddWithParents(roots, Directory.GetCurrentDirectory());
			return roots;
		}

		private static void AddWithParents(ISet<string> roots, string path) {
			if (string.IsNullOrWhiteSpace(path)) return;
			DirectoryInfo directory = new DirectoryInfo(path);
			for (int level = 0; directory != null && level < 10; level++, directory = directory.Parent) {
				roots.Add(directory.FullName);
			}
		}

		[DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
		private static extern IntPtr LoadLibrary(string path);

		[DllImport("libdl.so.2", EntryPoint = "dlopen")]
		private static extern IntPtr Dlopen(string path, int flags);
	}
}
