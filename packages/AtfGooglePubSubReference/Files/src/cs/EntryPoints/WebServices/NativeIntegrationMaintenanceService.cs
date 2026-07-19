using System;
using System.Net;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Activation;
using System.ServiceModel.Web;
using System.Web.SessionState;
using Common.Logging;
using Terrasoft.Web.Common;
using AtfGooglePubSubReference.GooglePubSub;

namespace AtfGooglePubSubReference.EntryPoints.WebServices {

	[DataContract]
	public sealed class StopNativeIntegrationsResponse {
		[DataMember(Name = "success")]
		public bool Success { get; set; }

		[DataMember(Name = "googlePubSubStopped")]
		public bool GooglePubSubStopped { get; set; }

		[DataMember(Name = "googlePubSubStopTimedOut")]
		public bool GooglePubSubStopTimedOut { get; set; }

		[DataMember(Name = "packageVersion")]
		public string PackageVersion { get; set; }

		[DataMember(Name = "error")]
		public string Error { get; set; }
	}

	[ServiceContract]
	[AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
	public sealed class NativeIntegrationMaintenanceService : BaseService, IReadOnlySessionState {
		private readonly Func<bool> _canManageSolution;
		private readonly Func<GooglePubSubRuntimeStopResult> _stopSubscriber;
		private readonly Action _stopPackagePublisher;
		private readonly Action<int> _setStatusCode;

		public NativeIntegrationMaintenanceService() {
			_canManageSolution = () => PrivilegedOperationAuthorization.CanManageSolution(
				operation => UserConnection.DBSecurityEngine.GetCanExecuteOperation(operation));
			_stopSubscriber = GooglePubSubSubscriberRuntime.Stop;
			_stopPackagePublisher = GooglePubSubPackagePublisher.Stop;
			_setStatusCode = SetStatusCode;
		}

		internal NativeIntegrationMaintenanceService(Func<bool> canManageSolution,
				Func<GooglePubSubRuntimeStopResult> stopSubscriber, Action stopPackagePublisher,
				Action<int> setStatusCode) {
			_canManageSolution = canManageSolution;
			_stopSubscriber = stopSubscriber;
			_stopPackagePublisher = stopPackagePublisher;
			_setStatusCode = setStatusCode;
		}

		[OperationContract]
		[WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Bare)]
		public StopNativeIntegrationsResponse Stop() {
			try {
				if (!_canManageSolution()) {
					_setStatusCode(403);
					return new StopNativeIntegrationsResponse {
						Success = false,
						Error = Constants.PrivilegedOperationRequiredError,
						PackageVersion = Constants.PackageVersion
					};
				}
				GooglePubSubRuntimeStopResult stopResult = _stopSubscriber();
				_stopPackagePublisher();
				if (stopResult.TimedOut) {
					_setStatusCode(503);
					return new StopNativeIntegrationsResponse {
						Success = false,
						GooglePubSubStopped = false,
						GooglePubSubStopTimedOut = true,
						PackageVersion = Constants.PackageVersion,
						Error = "Google Pub/Sub subscriber did not stop before the timeout."
					};
				}
				_setStatusCode(200);
				return new StopNativeIntegrationsResponse {
					Success = stopResult.Stopped,
					GooglePubSubStopped = stopResult.Stopped,
					PackageVersion = Constants.PackageVersion
				};
			} catch (Exception exception) {
				LogManager.GetLogger(Constants.LoggerName).Error("Google Pub/Sub shutdown request failed.", exception);
				_setStatusCode(500);
				return new StopNativeIntegrationsResponse {
					Success = false,
					Error = "Google Pub/Sub shutdown failed. See the Creatio application log for details."
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
