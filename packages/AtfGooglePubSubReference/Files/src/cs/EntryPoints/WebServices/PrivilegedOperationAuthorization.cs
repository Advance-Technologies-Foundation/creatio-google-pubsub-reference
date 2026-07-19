using System;

namespace AtfGooglePubSubReference.EntryPoints.WebServices {

	internal static class PrivilegedOperationAuthorization {
		internal static bool CanManageSolution(Func<string, bool> getCanExecuteOperation) {
			return getCanExecuteOperation(Constants.ManageSolutionOperationName);
		}
	}
}
