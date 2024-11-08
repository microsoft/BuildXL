// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildToolsInstaller.Config
{
    /// <summary>
    /// Data transfer object for BuildXL configuration
    /// Meant to be deserialized from a JSON
    /// TODO: Consolidate this configuration. 
    /// This "V0" is meant for the MVP (download the latest Nuget 'GeneralPublic' release)
    /// </summary>
    public class BuildXLGlobalConfig_V0
    {
        /// <summary>
        /// Latest release version number
        /// </summary>
        public required string Release { get; init; }
    }
}
