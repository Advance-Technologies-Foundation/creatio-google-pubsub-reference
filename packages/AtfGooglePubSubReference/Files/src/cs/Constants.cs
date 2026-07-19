namespace AtfGooglePubSubReference {

	/// <summary>
	/// Constants used by the Google Pub/Sub reference package.
	/// </summary>
	internal static class Constants {
		internal const string LoggerName = "AtfGooglePubSubReference";
		internal const string PackageVersion = "1.0.0";
		internal const string NativeRuntimeVersion = "2.46.6";
		internal const string NativeRuntimeArtifactFileName = "grpc-core-2.46.6-runtimes.zip";
		internal const string NativeArtifactsDirectoryName = "native-artifacts";
		internal const string RuntimeProductName = "grpc-core";
		internal const string CorrelationIdAttributeName = "correlationId";
		internal const string ContactSchemaName = "Contact";
		internal const string NameColumnName = "Name";
		internal const string ProjectIdSettingCode = "GooglePubSubProjectId";
		internal const string RequestTopicSettingCode = "GooglePubSubRequestTopic";
		internal const string RequestSubscriptionSettingCode = "GooglePubSubRequestSubscription";
		internal const string ReplyTopicSettingCode = "GooglePubSubReplyTopic";
		internal const string WorkerCountSettingCode = "GooglePubSubWorkerCount";
		internal const string CredentialSettingCode = "GooglePubSubServiceAccountJsonBase64";
		internal const string ManageSolutionOperationName = "CanManageSolution";
		internal const string PrivilegedOperationRequiredError =
			"Operation requires the CanManageSolution permission.";
	}
}
