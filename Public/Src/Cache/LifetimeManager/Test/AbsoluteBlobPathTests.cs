// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.BlobLifetimeManager.Test
{
    public class AbsoluteBlobPathTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CanParseFromSubject(bool useBuildCache)
        {
            IReadOnlyDictionary<BlobCacheStorageAccountName, BuildCacheShard>? buildCacheShardMapping = null;

            var path = @"some\path\to\the\file.blob";

            string accountName;
            string container;
            string subject;

            var accountUri = new Uri("https://testacct.blob.core.windows.net/");
            accountName = AzureStorageUtilities.GetAccountName(accountUri);
            if (useBuildCache)
            {
                container = "metadata";
                subject = $@"/blobServices/default/containers/{container}/blobs/{path}";

                var content = new BuildCacheContainer() { Name = "content", SasUrl = new Uri($"{accountUri}/content"), Type = BuildCacheContainerType.Content };
                var metadata = new BuildCacheContainer() { Name = "metadata", SasUrl = new Uri($"{accountUri}/metadata"), Type = BuildCacheContainerType.Metadata };
                var checkpoint = new BuildCacheContainer() { Name = "checkpoint", SasUrl = new Uri($"{accountUri}/checkpoint"), Type = BuildCacheContainerType.Checkpoint };
                var shard = new BuildCacheShard() { Containers = new List<BuildCacheContainer>() { content, metadata, checkpoint }, StorageUri = accountUri };

                buildCacheShardMapping = new Dictionary<BlobCacheStorageAccountName, BuildCacheShard>() { { shard.GetAccountName(), shard } };
            }
            else
            {
                container = "contentv0-matrix-universe-namespace";
                subject = $@"/blobServices/default/containers/{container}/blobs/{path}";
            }

            var absolutePath = AbsoluteBlobPath.ParseFromChangeEventSubject(buildCacheShardMapping, BlobCacheStorageAccountName.Parse(accountName), subject);

            absolutePath.Account.ToString().Should().Be(accountName);
            absolutePath.Path.Path.Should().Be(path);
            absolutePath.Container.ContainerName.Should().Be(container);
        }
    }
}
