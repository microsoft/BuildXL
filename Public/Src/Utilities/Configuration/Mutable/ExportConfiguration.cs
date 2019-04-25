// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;

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
