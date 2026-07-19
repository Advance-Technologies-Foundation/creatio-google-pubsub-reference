using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Common.Logging;
using Google.Api.Gax.Grpc;
using Google.Api.Gax;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.PubSub.V1;
using Terrasoft.Core;
using Terrasoft.Core.Configuration;
using Terrasoft.Core.Entities;
using Terrasoft.Core.Factories;
using Terrasoft.Web.Common;

namespace AtfGooglePubSubReference.GooglePubSub {

	internal sealed class GooglePubSubSubscriberSettings {
		internal string ProjectId { get; set; }
		internal string RequestSubscription { get; set; }
		internal string ReplyTopic { get; set; }
		internal int WorkerCount { get; set; }
		internal string CredentialBase64 { get; set; }

		internal bool IsConfigured => !string.IsNullOrWhiteSpace(ProjectId) &&
			!string.IsNullOrWhiteSpace(RequestSubscription) && !string.IsNullOrWhiteSpace(ReplyTopic) &&
			!string.IsNullOrWhiteSpace(CredentialBase64);

		internal static GooglePubSubSubscriberSettings Load(UserConnection userConnection) {
			return new GooglePubSubSubscriberSettings {
				ProjectId = SysSettings.GetValue(userConnection, Constants.ProjectIdSettingCode, string.Empty),
				RequestSubscription = SysSettings.GetValue(userConnection,
					Constants.RequestSubscriptionSettingCode, string.Empty),
				ReplyTopic = SysSettings.GetValue(userConnection, Constants.ReplyTopicSettingCode, string.Empty),
				WorkerCount = GooglePubSubSubscriberRuntime.NormalizeWorkerCount(SysSettings.GetValue(userConnection,
					Constants.WorkerCountSettingCode, 1)),
				CredentialBase64 = SysSettings.GetValue(userConnection, Constants.CredentialSettingCode, string.Empty)
			};
		}
	}

	internal static class GooglePubSubSubscriberRuntime {
		private static readonly object SyncRoot = new object();
		private static CancellationTokenSource _cancellation;
		private static Thread _thread;

		internal static int NormalizeWorkerCount(int value) {
			return Math.Max(1, Math.Min(32, value));
		}

		internal static void Start() {
			lock (SyncRoot) {
				if (_thread != null && _thread.IsAlive) {
					return;
				}
				UserConnection userConnection = ClassFactory.Get<UserConnection>();
				GooglePubSubSubscriberSettings settings = GooglePubSubSubscriberSettings.Load(userConnection);
				ILog log = LogManager.GetLogger(Constants.LoggerName);
				if (!settings.IsConfigured) {
					log.Warn("Google Pub/Sub subscriber is disabled because its system settings are incomplete.");
					return;
				}
				_cancellation = new CancellationTokenSource();
				_thread = new Thread(() => Run(settings, log, _cancellation.Token)) {
					IsBackground = true,
					Name = "ATF.GooglePubSubReference"
				};
				_thread.Start();
			}
		}

		private static string GetCurrentContactName(UserConnection userConnection) {
			var query = new EntitySchemaQuery(userConnection.EntitySchemaManager, Constants.ContactSchemaName);
			query.AddColumn(Constants.NameColumnName);
			Entity contact = query.GetEntity(userConnection, userConnection.CurrentUser.ContactId);
			return contact?.GetTypedColumnValue<string>(Constants.NameColumnName) ?? string.Empty;
		}

		internal static void Stop() {
			Thread thread;
			lock (SyncRoot) {
				if (_thread == null) {
					return;
				}
				_cancellation.Cancel();
				thread = _thread;
			}
			if (!thread.Join(TimeSpan.FromSeconds(20))) {
				LogManager.GetLogger(Constants.LoggerName).Warn(
					"Google Pub/Sub streaming subscriber did not stop within 20 seconds.");
			}
			lock (SyncRoot) {
				_cancellation.Dispose();
				_cancellation = null;
				_thread = null;
			}
		}

		private static void Run(GooglePubSubSubscriberSettings settings, ILog log,
				CancellationToken cancellationToken) {
			try {
				GoogleGrpcPackageNativeLibraryLoader.Load();
				GoogleCredential credential = CreateCredential(settings.CredentialBase64);
				SubscriptionName subscriptionName = SubscriptionName.FromProjectSubscription(settings.ProjectId,
					settings.RequestSubscription);
				TopicName replyTopicName = TopicName.FromProjectTopic(settings.ProjectId, settings.ReplyTopic);
				SubscriberClient subscriber = new SubscriberClientBuilder {
					GoogleCredential = credential,
					GrpcAdapter = GrpcCoreAdapter.Instance,
					SubscriptionName = subscriptionName,
					ClientCount = settings.WorkerCount,
					Settings = new SubscriberClient.Settings {
						FlowControlSettings = new FlowControlSettings(settings.WorkerCount, null)
					}
				}.Build();
				PublisherClient publisher = new PublisherClientBuilder {
					GoogleCredential = credential,
					GrpcAdapter = GrpcCoreAdapter.Instance,
					TopicName = replyTopicName,
					ClientCount = settings.WorkerCount,
					Settings = new PublisherClient.Settings {
						BatchingSettings = new BatchingSettings(100, 1_000_000, TimeSpan.FromMilliseconds(10))
					}
				}.Build();
				log.InfoFormat("Google Pub/Sub streaming subscriber started with {0} clients. Subscription: {1}; " +
					"reply topic: {2}", settings.WorkerCount, settings.RequestSubscription, settings.ReplyTopic);
				System.Threading.Tasks.Task runTask = subscriber.StartAsync(async (message, token) => {
					try {
						return await ProcessMessage(publisher, message, token).ConfigureAwait(false)
							? SubscriberClient.Reply.Ack
							: SubscriberClient.Reply.Nack;
					} catch (Exception exception) {
						log.Error("Google Pub/Sub message processing failed; the message will be retried.", exception);
						return SubscriberClient.Reply.Nack;
					}
				});
				cancellationToken.WaitHandle.WaitOne();
				subscriber.StopAsync(new SubscriberClient.ShutdownOptions {
					Mode = SubscriberClient.ShutdownMode.WaitForProcessing,
					Timeout = TimeSpan.FromSeconds(15)
				}, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
				runTask.ConfigureAwait(false).GetAwaiter().GetResult();
				log.Info("Google Pub/Sub streaming subscriber stopped.");
			} catch (Exception exception) {
				log.Error("Google Pub/Sub subscriber terminated unexpectedly.", exception);
			}
		}

		private static async System.Threading.Tasks.Task<bool> ProcessMessage(PublisherClient publisher,
				PubsubMessage message, CancellationToken cancellationToken) {
			string contactName = GetCurrentContactName(ClassFactory.Get<UserConnection>());
			if (!GooglePubSubRoundTripHandler.TryCreateReply(message, contactName,
					out string correlationId, out string reply)) {
				return false;
			}
			var attributes = new Dictionary<string, string> {
				[Constants.CorrelationIdAttributeName] = correlationId
			};
			var replyMessage = new PubsubMessage { Data = Google.Protobuf.ByteString.CopyFromUtf8(reply) };
			replyMessage.Attributes.Add(attributes);
			await publisher.PublishAsync(replyMessage).ConfigureAwait(false);
			return true;
		}

		private static GoogleCredential CreateCredential(string credentialBase64) {
			byte[] bytes = Convert.FromBase64String(credentialBase64);
			ServiceAccountCredential credential = CredentialFactory.FromJson<ServiceAccountCredential>(
				System.Text.Encoding.UTF8.GetString(bytes));
			return credential.ToGoogleCredential();
		}
	}

	internal static class GooglePubSubRoundTripHandler {
		internal static bool TryCreateReply(PubsubMessage message, string contactName,
				out string correlationId, out string reply) {
			correlationId = null;
			reply = null;
			if (message == null || !message.Attributes.TryGetValue(Constants.CorrelationIdAttributeName,
					out string value) || !Guid.TryParse(value, out Guid parsedCorrelationId)) {
				return false;
			}
			correlationId = parsedCorrelationId.ToString("D");
			reply = $"I see your message {message.Data.ToStringUtf8()} by {contactName}";
			return true;
		}
	}

	public sealed class GooglePubSubAppEventListener : AppEventListenerBase {
		public override void OnAppStart(AppEventContext context) {
			GooglePubSubSubscriberRuntime.Start();
		}

		public override void OnAppEnd(AppEventContext context) {
			GooglePubSubSubscriberRuntime.Stop();
		}
	}
}
