using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace AtfGooglePubSubReference.GooglePubSub {

	internal sealed class GoogleGrpcNativeRuntimeProvisioner {
		private const string ArtifactHashFileName = ".artifact.sha256";

		internal string Provision(string artifactPath, string destinationPath, string runtime,
			string libraryFileName) {
			if (!File.Exists(artifactPath)) {
				throw new FileNotFoundException("The Google gRPC native runtime artifact was not found.", artifactPath);
			}
			string libraryPath = Path.Combine(destinationPath, "native", libraryFileName);
			string artifactHash = ComputeHash(artifactPath);
			if (IsCurrent(destinationPath, libraryPath, artifactHash)) {
				return libraryPath;
			}
			if (Directory.Exists(destinationPath)) {
				throw new InvalidDataException("The existing Google gRPC runtime does not match its packaged artifact. " +
					"Deploy a new runtime version instead of replacing loaded files.");
			}
			Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
			string stagingPath = destinationPath + ".staging-" + Guid.NewGuid().ToString("N");
			try {
				ExtractRuntime(artifactPath, stagingPath, runtime);
				if (!File.Exists(Path.Combine(stagingPath, "native", libraryFileName))) {
					throw new InvalidDataException("The Google gRPC artifact does not contain the required native library.");
				}
				File.WriteAllText(Path.Combine(stagingPath, ArtifactHashFileName), artifactHash);
				try {
					Directory.Move(stagingPath, destinationPath);
				} catch (IOException) when (IsCurrent(destinationPath, libraryPath, artifactHash)) {
					// Another worker published the same artifact first.
				}
				return libraryPath;
			} finally {
				if (Directory.Exists(stagingPath)) {
					Directory.Delete(stagingPath, true);
				}
			}
		}

		private static void ExtractRuntime(string artifactPath, string stagingPath, string runtime) {
			string prefix = runtime + "/";
			Directory.CreateDirectory(stagingPath);
			string normalizedStagingPath = Path.GetFullPath(stagingPath) + Path.DirectorySeparatorChar;
			using (ZipArchive archive = ZipFile.OpenRead(artifactPath)) {
				foreach (ZipArchiveEntry entry in archive.Entries.Where(item =>
					item.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) {
					string relativePath = entry.FullName.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar);
					if (string.IsNullOrEmpty(relativePath)) continue;
					string targetPath = Path.GetFullPath(Path.Combine(stagingPath, relativePath));
					if (!targetPath.StartsWith(normalizedStagingPath, StringComparison.OrdinalIgnoreCase)) {
						throw new InvalidDataException("The Google gRPC artifact contains an invalid entry path.");
					}
					if (string.IsNullOrEmpty(entry.Name)) {
						Directory.CreateDirectory(targetPath);
					} else {
						Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
						entry.ExtractToFile(targetPath, true);
					}
				}
			}
		}

		private static bool IsCurrent(string destinationPath, string libraryPath, string artifactHash) {
			string hashPath = Path.Combine(destinationPath, ArtifactHashFileName);
			return File.Exists(libraryPath) && File.Exists(hashPath) &&
				string.Equals(File.ReadAllText(hashPath).Trim(), artifactHash, StringComparison.OrdinalIgnoreCase);
		}

		private static string ComputeHash(string path) {
			using (SHA256 sha256 = SHA256.Create())
			using (FileStream stream = File.OpenRead(path)) {
				return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", string.Empty);
			}
		}
	}
}
