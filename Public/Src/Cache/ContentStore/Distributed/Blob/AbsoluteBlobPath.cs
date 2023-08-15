// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.RegularExpressions;

namespace BuildXL.Cache.ContentStore.Distributed.Blob
{
    /// <summary>
    /// This absolute path is gotten from the Azure Blob change feed. It uniquely identifies a blob within the cache.
    /// </summary>
    public readonly record struct AbsoluteBlobPath(BlobCacheStorageAccountName Account, BlobCacheContainerName Container, BlobPath Path)
    {
        private readonly static Regex BlobChangeFeedEventSubjectRegex = new(@"/blobServices/default/containers/(?<container>[^/]+)/blobs/(?<path>.+)", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public static AbsoluteBlobPath ParseFromChangeEventSubject(BlobCacheStorageAccountName account, string subject)
        {
            var match = BlobChangeFeedEventSubjectRegex.Match(subject);
            if (!match.Success)
            {
                throw new ArgumentException($"Failed to match {nameof(BlobChangeFeedEventSubjectRegex)} to {subject}", nameof(subject));
            }

            var container = BlobCacheContainerName.Parse(match.Groups["container"].Value);
            var path = new BlobPath(match.Groups["path"].Value, relative: false);

            return new(Account: account, Container: container, Path: path);
        }
    }
}
