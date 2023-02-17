// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class ExportConfiguration : IExportConfiguration
    {
        /// <nodoc />
        public ExportConfiguration()
        {
            SnapshotMode = SnapshotMode.Evaluation;
        }

        /// <nodoc />
        public ExportConfiguration(IExportConfiguration template, PathRemapper pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            SnapshotFile = pathRemapper.Remap(template.SnapshotFile);
            SnapshotMode = template.SnapshotMode;
        }

        /// <inhertidoc />
        public AbsolutePath SnapshotFile { get; set; }

        /// <inhertidoc />
        public SnapshotMode SnapshotMode { get; set; }
    }
}
