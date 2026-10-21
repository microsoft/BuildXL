// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Core;

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
        public DownloadFileSettings(IDownloadFileSettings template, PathRemapper pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            ModuleName = template.ModuleName;
            Url = template.Url;
            FileName = template.FileName;
            ArchiveType = template.ArchiveType;
            Hash = template.Hash;
            DownloadedValueName = template.DownloadedValueName;
            ExtractedValueName = template.ExtractedValueName;
            RequiredFiles = template.RequiredFiles?.Select(pathRemapper.Remap).ToList();
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

        /// <inheritdoc />
        public string DownloadedValueName { get; set; }

        /// <inheritdoc />
        public string ExtractedValueName { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<RelativePath> RequiredFiles { get; set; }
    }
}
