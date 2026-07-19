using System;
using System.Net;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Web;
using System.Threading;
using System.Web.SessionState;
using Common.Logging;
using Terrasoft.Core.Configuration;
using Terrasoft.Web.Common;
using AtfGooglePubSubReference.GooglePubSub;

namespace AtfGooglePubSubReference.EntryPoints.WebServices {

	[DataContract]
	public sealed class PublishGooglePackageMessageRequest {
		[DataMember(Name = "message")]
		public string Message { get; set; }
	}

	[DataContract]
	public sealed class PublishGooglePackageMessageResponse {
		[DataMember(Name = "success")]
		public bool Success { get; set; }
		[DataMember(Name = "messageId")]
		public string MessageId { get; set; }
		[DataMember(Name = "error")]
		public string Error { get; set; }
	}

	[ServiceContract]
	[AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
	public sealed class GooglePubSubPackageMessageService : BaseService, IReadOnlySessionState {

		[OperationContract]
		[WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Bare)]
		public PublishGooglePackageMessageResponse Publish(PublishGooglePackageMessageRequest request) {
			if (string.IsNullOrWhiteSpace(request?.Message)) {
				SetStatusCode(400);
				return new PublishGooglePackageMessageResponse { Success = false, Error = "Message is required." };
			}
			try {
				string projectId = SysSettings.GetValue(UserConnection,
					GooglePubSubPackageSettings.ProjectIdSettingCode, string.Empty);
				string topic = SysSettings.GetValue(UserConnection,
					GooglePubSubPackageSettings.RequestTopicSettingCode, string.Empty);
				string credential = SysSettings.GetValue(UserConnection,
					GooglePubSubPackageSettings.CredentialSettingCode, string.Empty);
				string messageId = GooglePubSubPackagePublisher.PublishAsync(projectId, topic, credential,
					request.Message, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
				SetStatusCode(202);
				return new PublishGooglePackageMessageResponse { Success = true, MessageId = messageId };
			} catch (Exception exception) {
				LogManager.GetLogger(Constants.LoggerName).Error("Google Pub/Sub publish request failed.", exception);
				SetStatusCode(502);
				return new PublishGooglePackageMessageResponse {
					Success = false,
					Error = "Google Pub/Sub publish failed. See the Creatio application log for details."
				};
			}
		}

		private void SetStatusCode(int statusCode) {
#if NETSTANDARD2_0
			HttpContextAccessor.GetInstance().Response.StatusCode = statusCode;
#else
			WebOperationContext.Current.OutgoingResponse.StatusCode = (HttpStatusCode)statusCode;
#endif
		}
	}
}
