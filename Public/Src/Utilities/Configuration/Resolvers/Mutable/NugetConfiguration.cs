// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class NugetConfiguration : ArtifactLocation, INugetConfiguration
    {
        /// <nodoc />
        public NugetConfiguration()
        {
            DownloadTimeoutMin = 20;
        }

        /// <nodoc />
        public NugetConfiguration(INugetConfiguration template)
            : base(template)
        {
            DownloadTimeoutMin = template.DownloadTimeoutMin;
        }

        /// <inheritdoc/>
        public int? DownloadTimeoutMin { get; set; }
    }
}
