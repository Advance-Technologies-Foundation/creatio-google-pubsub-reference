using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

	internal sealed class GooglePubSubRuntimeStopResult {
		internal bool WasRunning { get; set; }
		internal bool Stopped { get; set; }
		internal bool TimedOut { get; set; }
	}

	internal interface IGooglePubSubRuntimeThread {
		bool IsAlive { get; }
		void Start();
		bool Join(TimeSpan timeout);
	}

	internal sealed class GooglePubSubRuntimeThread : IGooglePubSubRuntimeThread {
		private readonly Thread _thread;

		internal GooglePubSubRuntimeThread(ThreadStart start) {
			_thread = new Thread(start) {
				IsBackground = true,
				Name = "ATF.GooglePubSubReference"
			};
		}

		public bool IsAlive => _thread.IsAlive;
		public void Start() => _thread.Start();
		public bool Join(TimeSpan timeout) => _thread.Join(timeout);
	}

	internal sealed class GooglePubSubRuntimeLifecycle {
		private readonly object _syncRoot = new object();
		private CancellationTokenSource _cancellation;
		private IGooglePubSubRuntimeThread _thread;

		internal bool TryStart(Func<CancellationToken, IGooglePubSubRuntimeThread> threadFactory) {
			lock (_syncRoot) {
				if (_thread != null) {
					if (_thread.IsAlive) {
						return false;
					}
					ClearState();
				}
				var cancellation = new CancellationTokenSource();
				IGooglePubSubRuntimeThread thread = threadFactory(cancellation.Token);
				_cancellation = cancellation;
				_thread = thread;
				try {
					thread.Start();
					return true;
				} catch {
					ClearState();
					throw;
				}
			}
		}

		internal GooglePubSubRuntimeStopResult Stop(TimeSpan timeout) {
			IGooglePubSubRuntimeThread thread;
			lock (_syncRoot) {
				if (_thread == null) {
					return new GooglePubSubRuntimeStopResult { Stopped = true };
				}
				_cancellation.Cancel();
				thread = _thread;
			}
			if (!thread.Join(timeout)) {
				return new GooglePubSubRuntimeStopResult {
					WasRunning = true,
					TimedOut = true
				};
			}
			lock (_syncRoot) {
				if (ReferenceEquals(_thread, thread)) {
					ClearState();
				}
			}
			return new GooglePubSubRuntimeStopResult {
				WasRunning = true,
				Stopped = true
			};
		}

		private void ClearState() {
			_cancellation?.Dispose();
			_cancellation = null;
			_thread = null;
		}
	}

	internal interface IGooglePubSubReplyPublisher {
		Task PublishAsync(PubsubMessage message);
		Task ShutdownAsync(TimeSpan timeout);
	}

	internal sealed class GooglePubSubReplyPublisher : IGooglePubSubReplyPublisher {
		private readonly PublisherClient _publisher;

		internal GooglePubSubReplyPublisher(PublisherClient publisher) {
			_publisher = publisher;
		}

		public Task PublishAsync(PubsubMessage message) => _publisher.PublishAsync(message);
		public Task ShutdownAsync(TimeSpan timeout) => _publisher.ShutdownAsync(timeout);
	}

	internal static class GooglePubSubSubscriberRuntime {
		private static readonly GooglePubSubRuntimeLifecycle Lifecycle = new GooglePubSubRuntimeLifecycle();

		internal static int NormalizeWorkerCount(int value) {
			return Math.Max(1, Math.Min(32, value));
		}

		internal static void Start() {
			UserConnection userConnection = ClassFactory.Get<UserConnection>();
			GooglePubSubSubscriberSettings settings = GooglePubSubSubscriberSettings.Load(userConnection);
			ILog log = LogManager.GetLogger(Constants.LoggerName);
			if (!settings.IsConfigured) {
				log.Warn("Google Pub/Sub subscriber is disabled because its system settings are incomplete.");
				return;
			}
			Lifecycle.TryStart(token => new GooglePubSubRuntimeThread(() => Run(settings, log, token)));
		}

		private static string GetCurrentContactName(UserConnection userConnection) {
			var query = new EntitySchemaQuery(userConnection.EntitySchemaManager, Constants.ContactSchemaName);
			query.AddColumn(Constants.NameColumnName);
			Entity contact = query.GetEntity(userConnection, userConnection.CurrentUser.ContactId);
			return contact?.GetTypedColumnValue<string>(Constants.NameColumnName) ?? string.Empty;
		}

		internal static GooglePubSubRuntimeStopResult Stop() {
			GooglePubSubRuntimeStopResult result = Lifecycle.Stop(TimeSpan.FromSeconds(20));
			if (result.TimedOut) {
				LogManager.GetLogger(Constants.LoggerName).Warn(
					"Google Pub/Sub streaming subscriber did not stop within 20 seconds.");
			}
			return result;
		}

		private static void Run(GooglePubSubSubscriberSettings settings, ILog log,
				CancellationToken cancellationToken) {
			IGooglePubSubReplyPublisher publisher = null;
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
				PublisherClient publisherClient = new PublisherClientBuilder {
					GoogleCredential = credential,
					GrpcAdapter = GrpcCoreAdapter.Instance,
					TopicName = replyTopicName,
					ClientCount = settings.WorkerCount,
					Settings = new PublisherClient.Settings {
						BatchingSettings = new BatchingSettings(100, 1_000_000, TimeSpan.FromMilliseconds(10))
					}
				}.Build();
				publisher = new GooglePubSubReplyPublisher(publisherClient);
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
			} finally {
				ShutdownReplyPublisher(publisher,
					exception => log.Error("Google Pub/Sub reply publisher shutdown failed.", exception));
			}
		}

		internal static bool ShutdownReplyPublisher(IGooglePubSubReplyPublisher publisher,
				Action<Exception> reportError) {
			if (publisher == null) {
				return true;
			}
			try {
				publisher.ShutdownAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false).GetAwaiter().GetResult();
				return true;
			} catch (Exception exception) {
				reportError(exception);
				return false;
			}
		}

		private static async Task<bool> ProcessMessage(IGooglePubSubReplyPublisher publisher,
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
