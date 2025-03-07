// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.Cache.BuildCacheResource.Model;

namespace BuildXL.Cache.ContentStore.Distributed.Blob
{
    /// <summary>
    /// This absolute path is gotten from the Azure Blob change feed. It uniquely identifies a blob within the cache.
    /// </summary>
    public readonly record struct AbsoluteBlobPath(AbsoluteContainerPath ContainerPath, BlobPath Path)
    {
        private static readonly Regex BlobChangeFeedEventSubjectRegex = new(@"/blobServices/default/containers/(?<container>[^/]+)/blobs/(?<path>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public BlobCacheStorageAccountName Account => ContainerPath.Account;

        public BlobCacheContainerName Container => ContainerPath.Container;

        public BlobNamespaceId NamespaceId => Container.NamespaceId;

        public bool HasMatrixMatch(string matrix)
        {
            return Container.Matrix.Equals(matrix, StringComparison.OrdinalIgnoreCase);
        }

        public static AbsoluteBlobPath ParseFromChangeEventSubject(IReadOnlyDictionary<BlobCacheStorageAccountName, BuildCacheShard> buildCacheShardMapping, BlobCacheStorageAccountName account, string subject)
        {
            var match = BlobChangeFeedEventSubjectRegex.Match(subject);
            if (!match.Success)
            {
                throw new ArgumentException($"Failed to match {nameof(BlobChangeFeedEventSubjectRegex)} to {subject}", nameof(subject));
            }

            var matchedContainerName = match.Groups["container"].Value;

            BlobCacheContainerName container;
            if (buildCacheShardMapping == null)
            {
                container = LegacyBlobCacheContainerName.Parse(matchedContainerName);
            }
            else
            {
                // For the build cache resource case, match the account and container name to retrieve name and purpose
                if (!buildCacheShardMapping.TryGetValue(account, out var shard))
                {
                    throw new InvalidOperationException($"Failed to match account name {account} to the build cache resource configuration");
                }

                var buildCacheContainer = shard.Containers.FirstOrDefault(container => container.Name == matchedContainerName);
                if (buildCacheContainer == null)
                {
                    throw new InvalidOperationException($"Failed to match container name {matchedContainerName} to the build cache resource configuration");
                }

                container = new FixedCacheBlobContainerName(buildCacheContainer.Name, buildCacheContainer.Type.ToContainerPurpose());
            }

            var path = new BlobPath(match.Groups["path"].Value, relative: false);

            return new(new(Account: account, Container: container), Path: path);
        }

        public override string ToString()
        {
            return $"{ContainerPath}/{Path}";
        }
    }
}
