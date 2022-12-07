// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.Host.Configuration;

#nullable enable

namespace BuildXL.Cache.Logging
{
    /// <nodoc />
    public sealed class AzureBlobStorageLogConfiguration
    {
        /// <nodoc />
        public AbsolutePath WorkspaceFolderPath { get; set; }

        /// <nodoc />
        public AbsolutePath StagingFolderPath => WorkspaceFolderPath / "Staging";

        /// <nodoc />
        public AbsolutePath UploadFolderPath => WorkspaceFolderPath / "Upload";

        /// <nodoc />
        /// <remarks>
        ///     WARNING: do NOT use anything but lower case letters. This will fail in a weird way.
        /// </remarks>
        public string ContainerName { get; set; } = "cachelogscontainer";

        /// <nodoc />
        public int WriteMaxDegreeOfParallelism { get; set; } = 1;

        /// <nodoc />
        public TimeSpan WriteMaxInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <nodoc />
        public int WriteMaxBatchSize { get; set; } = 1000000;

        /// <nodoc />
        public int UploadMaxDegreeOfParallelism { get; set; } = 4;

        /// <nodoc />
        public TimeSpan UploadMaxInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <nodoc />
        public bool DrainUploadsOnShutdown { get; set; } = false;

        /// <nodoc />
        public RetryPolicyConfiguration FileWriteRetryPolicy { get; set; } = RetryPolicyConfiguration.Exponential(maximumRetryCount: 10);

        /// <nodoc />
        public TimeSpan FileWriteTimeout { get; set; } = TimeSpan.FromMinutes(10);

        /// <nodoc />
        public TimeSpan FileWriteTracePeriod { get; set; } = TimeSpan.FromMinutes(3);

        /// <nodoc />
        public TimeSpan FileWriteAttemptTimeout { get; set; } = TimeSpan.FromMinutes(2);

        /// <nodoc />
        public TimeSpan FileWriteAttemptTracePeriod { get; set; } = TimeSpan.FromSeconds(30);

        /// <nodoc />
        public RetryPolicyConfiguration BlobUploadRetryPolicy { get; set; } = RetryPolicyConfiguration.Exponential(maximumRetryCount: 10);

        /// <nodoc />
        public TimeSpan BlobUploadTimeout { get; set; } = TimeSpan.FromMinutes(30);

        /// <nodoc />
        public TimeSpan BlobUploadTracePeriod { get; set; } = TimeSpan.FromMinutes(10);

        /// <nodoc />
        public TimeSpan BlobUploadAttemptTimeout { get; set; } = TimeSpan.FromMinutes(5);

        /// <nodoc />
        public TimeSpan BlobUploadAttemptTracePeriod { get; set; } = TimeSpan.FromMinutes(1);

        /// <nodoc />
        public AzureBlobStorageLogConfiguration(AbsolutePath workspace)
        {
            WorkspaceFolderPath = workspace;
        }
    }
}
