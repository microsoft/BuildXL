// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class FileAccessWhitelistEntry : TrackedValue, IFileAccessWhitelistEntry
    {
        /// <nodoc />
        public FileAccessWhitelistEntry()
        {
        }

        /// <nodoc />
        public FileAccessWhitelistEntry(IFileAccessWhitelistEntry template, PathRemapper pathRemapper)
            : base(template, pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            Name = template.Name;
            Value = template.Value;
            ToolPath = pathRemapper.Remap(template.ToolPath);
            PathFragment = template.PathFragment;
            PathRegex = template.PathRegex;
        }

        /// <inheritdoc />
        public string Name { get; set; }

        /// <inheritdoc />
        public string Value { get; set; }

        /// <inheritdoc />
        public FileArtifact ToolPath { get; set; }

        /// <inheritdoc />
        public string PathFragment { get; set; }

        /// <inheritdoc />
        public string PathRegex { get; set; }
    }
}
