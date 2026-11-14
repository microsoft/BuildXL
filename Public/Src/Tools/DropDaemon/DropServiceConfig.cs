// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Tool.DropDaemon
{
    /// <summary>
    /// Drop service configuration. These settings apply to all drops under the service.
    /// </summary>
    public sealed class DropServiceConfig
    {
        #region ConfigOptions
        /// <summary>
        ///     Represents the BuildSessionInfo: bsi.json file path.
        /// </summary>
        public string BsiFileLocation { get; }

        /// <summary>
        ///     Represents the Path to makecat.exe for Build Manifest Catalog generation.
        /// </summary>
        public string MakeCatToolPath { get; }

        /// <summary>
        ///     Represents the Path to EsrpManifestSign.exe for Build Manifest Catalog Signing.
        /// </summary>
        public string EsrpManifestSignToolPath { get; }

        /// <summary>
        /// Whether all destinations share one association/upload execution queue. Defaults to false.
        /// </summary>
        public bool UseSharedConcurrencyLimiter { get; }

        /// <summary>
        /// Shared batch concurrency, fixed when the daemon starts. Later drop settings do not resize it.
        /// </summary>
        public int MaxParallelUploads { get; }
        #endregion

        // ==================================================================================================
        // Constructor
        // ==================================================================================================

        /// <nodoc/>
        public DropServiceConfig(string bsiFileLocation = null,
            string makeCatToolPath = null,
            string esrpManifestSignToolPath = null,
            bool useSharedConcurrencyLimiter = false,
            int? maxParallelUploads = null)
        {
            BsiFileLocation = bsiFileLocation ?? string.Empty;
            MakeCatToolPath = makeCatToolPath ?? string.Empty;
            EsrpManifestSignToolPath = esrpManifestSignToolPath ?? string.Empty;
            UseSharedConcurrencyLimiter = useSharedConcurrencyLimiter;
            MaxParallelUploads = maxParallelUploads ?? DropConfig.DefaultMaxParallelUploads;
        }
    }
}
