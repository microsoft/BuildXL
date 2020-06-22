// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class FileAccessAllowlistEntry : TrackedValue, IFileAccessAllowlistEntry
    {
        /// <nodoc />
        public FileAccessAllowlistEntry()
        {
        }

        /// <nodoc />
        public FileAccessAllowlistEntry(IFileAccessAllowlistEntry template, PathRemapper pathRemapper)
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
