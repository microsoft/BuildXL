// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Web;

#nullable enable

namespace BuildXL.Cache.Host.Configuration
{
    /// <summary>
    /// Creator and parser of a connection string for <see cref="DistributedContentSettings.EventHubConnectionString"/>
    /// which causes authentication against EventHub to use an Azure Managed Identity.
    ///
    /// Example: sb://yourEventHubNamespace.servicebus.windows.net/?name=eventHubName&identity=my-identity-guid
    /// </summary>
    public static class ManagedIdentityUriHelper
    {
        private const string EventHubNameOption = "name";
        private const string ManagedIdentityIdOption = "identity";

        public static string BuildString(Uri eventHubNamespaceUri, string eventHubName, string managedIdentityId)
        {
            return eventHubNamespaceUri.AbsoluteUri + '?' + CreateQueryVariable(EventHubNameOption, eventHubName) + '&' + CreateQueryVariable(ManagedIdentityIdOption, managedIdentityId);
        }

        public static bool TryParseForManagedIdentity(string uriString, [NotNullWhen(true)] out Uri? eventHubNamespaceUri, [NotNullWhen(true)] out string? eventHubName, [NotNullWhen(true)] out string? managedIdentityId)
        {
            if (Uri.TryCreate(uriString, UriKind.Absolute, out Uri? uri))
            {
                NameValueCollection? queryVariables = HttpUtility.ParseQueryString(uri.Query);
                if (queryVariables.Count > 0)
                {
                    eventHubNamespaceUri = new Uri(uri.Scheme + Uri.SchemeDelimiter + uri.Host);
                    eventHubName = queryVariables[EventHubNameOption];
                    managedIdentityId = queryVariables[ManagedIdentityIdOption];

                    if (!string.IsNullOrEmpty(eventHubName) && !string.IsNullOrEmpty(managedIdentityId))
                    {
                        return true;
                    }
                }
            }

            eventHubNamespaceUri = null;
            eventHubName = null;
            managedIdentityId = null;
            return false;
        }

        private static string CreateQueryVariable(string name, string value)
        {
            return name + "=" + Uri.EscapeDataString(value);
        }
    }
}
