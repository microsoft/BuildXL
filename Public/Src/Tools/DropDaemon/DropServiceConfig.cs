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
        #endregion

        // ==================================================================================================
        // Constructor
        // ==================================================================================================

        /// <nodoc/>
        public DropServiceConfig(string bsiFileLocation = null,
            string makeCatToolPath = null,
            string esrpManifestSignToolPath = null)
        {
            BsiFileLocation = bsiFileLocation ?? string.Empty;
            MakeCatToolPath = makeCatToolPath ?? string.Empty;
            EsrpManifestSignToolPath = esrpManifestSignToolPath ?? string.Empty;
        }
    }
}
