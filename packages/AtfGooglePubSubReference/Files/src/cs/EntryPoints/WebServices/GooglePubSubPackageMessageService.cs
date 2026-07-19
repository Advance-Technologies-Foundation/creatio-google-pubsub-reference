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
		private readonly Func<bool> _canManageSolution;
		private readonly Func<PublishGooglePackageMessageRequest, string> _publish;
		private readonly Action<int> _setStatusCode;

		public GooglePubSubPackageMessageService() {
			_canManageSolution = () => PrivilegedOperationAuthorization.CanManageSolution(
				operation => UserConnection.DBSecurityEngine.GetCanExecuteOperation(operation));
			_publish = PublishCore;
			_setStatusCode = SetStatusCode;
		}

		internal GooglePubSubPackageMessageService(Func<bool> canManageSolution,
				Func<PublishGooglePackageMessageRequest, string> publish, Action<int> setStatusCode) {
			_canManageSolution = canManageSolution;
			_publish = publish;
			_setStatusCode = setStatusCode;
		}

		[OperationContract]
		[WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Bare)]
		public PublishGooglePackageMessageResponse Publish(PublishGooglePackageMessageRequest request) {
			try {
				if (!_canManageSolution()) {
					_setStatusCode(403);
					return new PublishGooglePackageMessageResponse {
						Success = false,
						Error = Constants.PrivilegedOperationRequiredError
					};
				}
				if (string.IsNullOrWhiteSpace(request?.Message)) {
					_setStatusCode(400);
					return new PublishGooglePackageMessageResponse { Success = false, Error = "Message is required." };
				}
				string messageId = _publish(request);
				_setStatusCode(202);
				return new PublishGooglePackageMessageResponse { Success = true, MessageId = messageId };
			} catch (Exception exception) {
				LogManager.GetLogger(Constants.LoggerName).Error("Google Pub/Sub publish request failed.", exception);
				_setStatusCode(502);
				return new PublishGooglePackageMessageResponse {
					Success = false,
					Error = "Google Pub/Sub publish failed. See the Creatio application log for details."
				};
			}
		}

		private string PublishCore(PublishGooglePackageMessageRequest request) {
			string projectId = SysSettings.GetValue(UserConnection,
				GooglePubSubPackageSettings.ProjectIdSettingCode, string.Empty);
			string topic = SysSettings.GetValue(UserConnection,
				GooglePubSubPackageSettings.RequestTopicSettingCode, string.Empty);
			string credential = SysSettings.GetValue(UserConnection,
				GooglePubSubPackageSettings.CredentialSettingCode, string.Empty);
			return GooglePubSubPackagePublisher.PublishAsync(projectId, topic, credential,
				request.Message, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();
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
