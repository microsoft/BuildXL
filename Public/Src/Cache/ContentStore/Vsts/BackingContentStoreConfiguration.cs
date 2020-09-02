// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    /// Configuration for <see cref="BackingContentStore"/>
    /// </summary>
    public class BackingContentStoreConfiguration
    {
        /// <summary>
        /// Filesystem used to read/write files.
        /// </summary>
        public IAbsFileSystem FileSystem { get; set; }

        /// <summary>
        /// Backing Store HTTP client factory.
        /// </summary>
        public IArtifactHttpClientFactory ArtifactHttpClientFactory { get; set; }

        /// <summary>
        /// Gets or sets the amount of time to keep content before it is referenced by metadata.
        /// </summary>
        public TimeSpan TimeToKeepContent { get; set; }

        /// <summary>
        /// Maximum time-to-live to inline pin calls.
        /// </summary>
        public TimeSpan PinInlineThreshold { get; set; }

        /// <summary>
        /// Minimum time-to-live to ignore pin calls.
        /// </summary>
        public TimeSpan IgnorePinThreshold { get; set; }

        /// <summary>
        /// Gets or sets whether Dedup is enabled.
        /// </summary>
        public bool UseDedupStore { get; set; }

        /// <summary>
        /// Gets whether basic HttpClient is used with downloading blobs from Azure blob store
        /// as opposed to using Azure Storage SDK.
        /// </summary>
        /// <remarks>
        /// There are known issues with timeouts, hangs, unobserved exceptions in the Azure
        /// Storage SDK, so this is provided as potentially permanent workaround by performing
        /// downloads using basic http requests.
        /// </remarks>
        public bool DownloadBlobsUsingHttpClient { get; set; }
    }
}
