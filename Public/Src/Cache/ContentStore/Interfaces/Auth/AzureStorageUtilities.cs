// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;

#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Auth
{
    /// <nodoc /> 
    public static class AzureStorageUtilities
    {
        /// <nodoc />
        public static bool TryGetAccountName(Uri uri, [NotNullWhen(true)] out string? accountName)
        {
            var uriHost = uri.Host;

            if (uriHost.EndsWith(".blob.core.windows.net", StringComparison.InvariantCultureIgnoreCase) || uriHost.EndsWith(".blob.storage.azure.net", StringComparison.InvariantCultureIgnoreCase))
            {
                accountName = uriHost.Split('.')[0];
                return true;
            }

            if (uriHost == "localhost" || uriHost.StartsWith("127."))
            {
                accountName = uri.Segments[1];
                return true;
            }

            accountName = null;
            return false;
        }

        /// <nodoc /> 
        public static string GetAccountName(Uri uri)
        {
            if (!TryGetAccountName(uri, out var accountName))
            {
                throw new InvalidOperationException($"The provided URI ({uri}) is malformed and the account name could not be retrieved.");
            }

            return accountName;
        }
    }
}
