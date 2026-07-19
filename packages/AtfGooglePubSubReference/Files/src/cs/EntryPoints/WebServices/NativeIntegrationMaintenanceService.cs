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

		[DataMember(Name = "packageVersion")]
		public string PackageVersion { get; set; }

		[DataMember(Name = "error")]
		public string Error { get; set; }
	}

	[ServiceContract]
	[AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Required)]
	public sealed class NativeIntegrationMaintenanceService : BaseService, IReadOnlySessionState {

		[OperationContract]
		[WebInvoke(Method = "POST", RequestFormat = WebMessageFormat.Json,
			ResponseFormat = WebMessageFormat.Json, BodyStyle = WebMessageBodyStyle.Bare)]
		public StopNativeIntegrationsResponse Stop() {
			try {
				GooglePubSubSubscriberRuntime.Stop();
				GooglePubSubPackagePublisher.Stop();
				SetStatusCode(200);
				return new StopNativeIntegrationsResponse {
					Success = true,
					GooglePubSubStopped = true,
					PackageVersion = Constants.PackageVersion
				};
			} catch (Exception exception) {
				LogManager.GetLogger(Constants.LoggerName).Error("Google Pub/Sub shutdown request failed.", exception);
				SetStatusCode(500);
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
