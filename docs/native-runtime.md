# Native gRPC runtime lifecycle

`Google.Cloud.PubSub.V1` in this reference uses `Grpc.Core 2.46.6`, which loads a platform-specific native library. Loaded native modules remain locked until the Creatio worker process exits. AppDomain restart and managed client disposal are not reliable native-unload boundaries.

## Delivery contract

The build target creates one version-pinned archive:

```text
Files/Bin[/netstandard]/native-artifacts/grpc-core-2.46.6-runtimes.zip
```

On first use, the loader:

1. detects the current RID;
2. locates the package archive;
3. computes its SHA-256 digest;
4. extracts only that RID into a staging directory;
5. validates the required library and blocks ZIP path traversal;
6. writes `.artifact.sha256`;
7. atomically moves the result to:

```text
<Creatio root>/conf/native/grpc-core/2.46.6/<rid>/native/<library>
```

Concurrent workers may race to provision the same immutable content; the loser accepts the already validated directory.

## Upgrade rule

Never replace a populated runtime version directory. Publish a new package with a new `NativeRuntimeVersion` and artifact filename, allow first use to create the new directory, and recycle every worker before removing the old version. Updating managed package files remains safe while workers load the native module from the external versioned location.

Supported Creatio host combinations in this example are Windows x86/x64 and Linux x64/Arm64, matching the packaged `Grpc.Core` assets. Validate any additional OS/architecture explicitly before claiming support.
