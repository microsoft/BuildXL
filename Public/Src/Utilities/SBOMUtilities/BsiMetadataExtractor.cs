// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Newtonsoft.Json;
using Microsoft.Sbom.Contracts;
using Microsoft.Sbom.Contracts.Enums;

namespace BuildXL.Utilities.SBOMUtilities
{
    /// <summary>
    /// Utility class to produce entities of the SBOM API object model from a CloudBuild BuildSessionInfo file.
    /// </summary>
    public class BsiMetadataExtractor
    {
        /// <summary>
        /// Generates an <see cref="SBOMMetadata"/> entity from a BuildSessionInfo file.
        /// </summary>
        /// <param name="pathToBuildSessionInfo">The full path to the JSON file with the BuildSessionInfo</param>
        /// <param name="packageName">The package name for the SBOM Metadata. This is an optional field and may be left unspecified. </param>
        /// <param name="packageVersion">The package version for the SBOM Metadata. This is an optional field and may be left unspecified. </param>
        /// <param name="buildEnvironment" />
        /// <exception cref="ArgumentNullException">If the specified path is null</exception>
        /// <exception cref="ArgumentException">If the specified path does not point to an existing file</exception>
        /// <exception cref="DeserializationException">If errors are encountered during deserialization of the JSON file</exception>
        public static SBOMMetadata ProduceSbomMetadata(string pathToBuildSessionInfo, BuildEnvironmentName buildEnvironment, string packageName = null, string packageVersion = null) => new BsiMetadataExtractor(pathToBuildSessionInfo).ProduceSbomMetadata(buildEnvironment, packageName, packageVersion);

        private readonly BuildSessionInfoData m_backingBsi;

        /// <summary>
        /// Creates a <see cref="BsiMetadataExtractor"/> which will pull the relevant information from a BuildSessionInfo file
        /// </summary>
        /// <param name="pathToBuildSessionInfo">The full path to the JSON file with the BuildSessionInfo which will back this object</param>
        /// <exception cref="ArgumentNullException">If the specified path is null</exception>
        /// <exception cref="ArgumentException">If the specified path does not point to an existing file</exception>
        /// <exception cref="DeserializationException">If errors are encountered during deserialization of the JSON file</exception>
        public BsiMetadataExtractor(string pathToBuildSessionInfo)
        {
            if (pathToBuildSessionInfo == null)
            {
                throw new ArgumentNullException(nameof(pathToBuildSessionInfo));
            }

            if (!File.Exists(pathToBuildSessionInfo))
            {
                throw new ArgumentException($"The path to BuildSessionInfo provided, {pathToBuildSessionInfo} is invalid or does not point to an existing file", paramName: nameof(pathToBuildSessionInfo));
            }

            try
            {
                m_backingBsi = JsonConvert.DeserializeObject<BuildSessionInfoData>(File.ReadAllText(pathToBuildSessionInfo));
            }
            catch (Exception e)
            {
                throw new DeserializationException($"Exception caught while deserializing the JSON file at {pathToBuildSessionInfo}", e);
            }
        }

        /// <summary>
        /// Generates an <see cref="SBOMMetadata"/> entity from the BuildSessionInfo file that backs this <see cref="BsiMetadataExtractor"/>.
        /// </summary>
        /// <param name="packageName">The package name for the SBOM Metadata.</param>
        /// <param name="packageVersion">The package version for the SBOM Metadata. If left unspecified, the build id will be used as the version. </param>
        /// <param name="buildEnvironment" />
        public SBOMMetadata ProduceSbomMetadata(BuildEnvironmentName buildEnvironment, string packageName, string packageVersion = null)
        {
            return new SBOMMetadata()
            {
                BuildEnvironmentName = buildEnvironment,
                BuildName = m_backingBsi.BuildQueue,
                BuildId = m_backingBsi.UniqueSessionId,
                CommitId = m_backingBsi.ChangeSummary?.ChangeId,
                Branch = m_backingBsi.BranchName,
                RepositoryUri = GetWellFormedRepositoryUri(m_backingBsi),
                PackageName = packageName,
                PackageVersion = !string.IsNullOrEmpty(packageVersion) ? packageVersion : m_backingBsi.UniqueSessionId,
            };
        }

        // The BSI might not have a well-formed URI in the RepositoryUri field (this may be the case in source depot based repositories, for example).
        // If this is the case, we try to make it into one to preserve this information.
        private static string GetWellFormedRepositoryUri(BuildSessionInfoData bsi)
        {
            var bsiUriString = bsi.SourceControlServer?.Uri;
            if (bsiUriString == null)
            {
                return null;
            }

            if (Uri.IsWellFormedUriString(bsiUriString, UriKind.Absolute))
            {
                return bsiUriString;
            }

            // If the string is not a URI, try to make it into one. If this is still not a valid URI,
            // something is wrong in the original string, so return null to leave it unspecified.
            var hopefullyFixedUri = $"sourcecontrol://{bsiUriString}";
            return Uri.IsWellFormedUriString(hopefullyFixedUri, UriKind.Absolute) ? hopefullyFixedUri : null;
        }
    }

    /// <summary>
    /// A projection of the actual BuildSessionInfo with the fields relevant to generating the SBOM Metadata
    /// </summary>
    /// <remarks>
    /// These field names should be kept in sync with the corresponding object in the CloudBuild codebase, but they are 
    /// supposed to be stable because that object is meant to be JSON-serialized.
    /// </remarks>
    internal class BuildSessionInfoData
    {
        internal class SourceControlServerData
        {
            /// <nodoc />
            public string Uri { get; set; }
        }

        internal class ChangeSummaryData
        {
            /// <nodoc />
            public string ChangeId { get; set; }
        }

        /// <nodoc />
        public SourceControlServerData SourceControlServer { get; set; }

        /// <nodoc />
        public ChangeSummaryData ChangeSummary { get; set; }

        /// <nodoc />
        public string BranchName { get; set; }

        /// <nodoc />
        public string CommitId { get; set; }

        /// <nodoc />
        public string UniqueSessionId { get; set; }

        /// <nodoc />
        public string BuildQueue { get; set;  }
    }

}
