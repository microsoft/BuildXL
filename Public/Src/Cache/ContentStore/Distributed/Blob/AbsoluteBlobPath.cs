// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.RegularExpressions;

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

        public static AbsoluteBlobPath ParseFromChangeEventSubject(BlobCacheStorageAccountName account, string subject)
        {
            var match = BlobChangeFeedEventSubjectRegex.Match(subject);
            if (!match.Success)
            {
                throw new ArgumentException($"Failed to match {nameof(BlobChangeFeedEventSubjectRegex)} to {subject}", nameof(subject));
            }

            var container = BlobCacheContainerName.Parse(match.Groups["container"].Value);
            var path = new BlobPath(match.Groups["path"].Value, relative: false);

            return new(new(Account: account, Container: container), Path: path);
        }

        public override string ToString()
        {
            return $"{ContainerPath}/{Path}";
        }
    }
}
