// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class DownloadFileSettings : IDownloadFileSettings
    {
        /// <nodoc />
        public DownloadFileSettings()
        {
        }

        /// <nodoc />
        public DownloadFileSettings(IDownloadFileSettings template)
        {
            ModuleName = template.ModuleName;
            Url = template.Url;
            FileName = template.FileName;
            ArchiveType = template.ArchiveType;
            Hash = template.Hash;
        }

        /// <inheritdoc />
        public string ModuleName { get; set; }

        /// <inheritdoc />
        public string Url { get; set; }

        /// <inheritdoc />
        public string FileName { get; set; }

        /// <inheritdoc />
        public DownloadArchiveType ArchiveType { get; set; }

        /// <inheritdoc />
        public string Hash { get; set; }

    }
}
