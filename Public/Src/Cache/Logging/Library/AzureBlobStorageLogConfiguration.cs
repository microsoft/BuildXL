using System;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

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
        public AzureBlobStorageLogConfiguration(AbsolutePath workspace)
        {
            WorkspaceFolderPath = workspace;
        }
    }
}
