// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Azure.Storage.Blobs.ChangeFeed;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using Xunit;

namespace BuildXL.Cache.BlobLifetimeManager.Test
{
    public class LifetimeManagerRealAzureTests
    {
        [Fact(Skip = "Should only be run by devs")]
        public async Task SeeChangeFeedForBlob()
        {
            var connectionString = "<put your connection string here>";
            var secret = new SecretBasedAzureStorageCredentials(new PlainTextSecret(connectionString));
            var client = secret.CreateBlobChangeFeedClient();
            var blob = "B7176683D345815AF7DBC08872D1A9D5/VSO0:076DCA63CCE012407D1942F54B1C1650286CB383673AF1439BAB1BCD756CDBB800_32F5iTKY/3ozjTzIVQT/6w==";

            var changes = new List<BlobChangeFeedEvent>();
            await foreach (var change in client.GetChangesAsync())
            {
                try
                {
                    var blobPath = AbsoluteBlobPath.ParseFromChangeEventSubject(buildCacheShardMapping: null, new BlobCacheStorageNonShardingAccountName("Doesn't matter"), change.Subject);
                    if (blobPath.Path.Path == blob)
                    {
                        changes.Add(change);
                    }
                }
                catch
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                {
                    // Do nothing
                }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler
            }

            Debugger.Launch();
        }
    }
}
