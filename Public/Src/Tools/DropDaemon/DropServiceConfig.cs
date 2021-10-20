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
        ///     Repo path of the code being built
        /// </summary>
        public string Repo { get; }

        /// <summary>
        ///     Current Git branch within the specified <see cref="Repo"/>
        /// </summary>
        public string Branch { get; }

        /// <summary>
        ///     Current Git CommitId within the specified <see cref="Repo"/>
        /// </summary>
        public string CommitId { get; }

        /// <summary>
        ///     Represents the RelatedActivityId specific to the cloud build environment
        /// </summary>
        public string CloudBuildId { get; }

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
        public DropServiceConfig(
            string repo = null,
            string branch = null,
            string commitId = null,
            string cloudBuildId = null,
            string bsiFileLocation = null,
            string makeCatToolPath = null,
            string esrpManifestSignToolPath = null)
        {
            Repo = repo ?? string.Empty;
            Branch = branch ?? string.Empty;
            CommitId = commitId ?? string.Empty;
            CloudBuildId = cloudBuildId ?? string.Empty;
            BsiFileLocation = bsiFileLocation ?? string.Empty;
            MakeCatToolPath = makeCatToolPath ?? string.Empty;
            EsrpManifestSignToolPath = esrpManifestSignToolPath ?? string.Empty;
        }
    }
}
