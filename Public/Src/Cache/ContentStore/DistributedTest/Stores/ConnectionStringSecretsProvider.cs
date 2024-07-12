// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using ContentStoreTest.Distributed.Redis;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test;

public class ConnectionStringSecretsProvider : StaticBlobCacheSecretsProvider
{
    public ConnectionStringSecretsProvider(AzuriteStorageProcess process, IReadOnlyList<BlobCacheStorageAccountName> accounts)
        : base(accounts.ToDictionary(
            account => account,
            account =>
            {
                var connectionString = process.ConnectionString.Replace("devstoreaccount1", account.AccountName);
                IAzureStorageCredentials credentials = new SecretBasedAzureStorageCredentials(connectionString);
                Contract.Assert(credentials.GetAccountName() == account.AccountName);
                return credentials;
            }))
    {
    }
}

