// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Interfaces.Auth;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blob
{
    public class EnvironmentVariableCacheSecretsProvider : StaticBlobCacheSecretsProvider
    {
        public EnvironmentVariableCacheSecretsProvider(string environmentVariableName)
            : base(ExtractCredsFromEnvironmentVariable(environmentVariableName))
        {
        }

        public static Dictionary<BlobCacheStorageAccountName, IAzureStorageCredentials> ExtractCredsFromEnvironmentVariable(string environmentVariableName)
        {
            var connectionStringsString = Environment.GetEnvironmentVariable(environmentVariableName);
            if (string.IsNullOrEmpty(connectionStringsString))
            {
                throw new ArgumentException($"Connections strings for the L3 cache must be provided via the {environmentVariableName} environment variable " +
                    $"in the format of comma-separated strings.");
            }

            var connectionStrings = connectionStringsString.Split(',');
            var creds = connectionStrings.Select(connString => new SecretBasedAzureStorageCredentials(new PlainTextSecret(connString)) as IAzureStorageCredentials).ToArray();
            return creds.ToDictionary(
                cred => BlobCacheStorageAccountName.Parse(cred.GetAccountName()),
                cred => cred);
        }
    }
}
