// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class LayoutConfiguration : ILayoutConfiguration
    {
        /// <nodoc />
        public LayoutConfiguration()
        {
        }

        /// <nodoc />
        public LayoutConfiguration(ILayoutConfiguration template, PathRemapper pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            PrimaryConfigFile = pathRemapper.Remap(template.PrimaryConfigFile);
            SourceDirectory = pathRemapper.Remap(template.SourceDirectory);
            OutputDirectory = pathRemapper.Remap(template.OutputDirectory);
            ObjectDirectory = pathRemapper.Remap(template.ObjectDirectory);
            FrontEndDirectory = pathRemapper.Remap(template.FrontEndDirectory);
            CacheDirectory = pathRemapper.Remap(template.CacheDirectory);
            EngineCacheDirectory = pathRemapper.Remap(template.EngineCacheDirectory);
            TempDirectory = pathRemapper.Remap(template.TempDirectory);
            BuildXlBinDirectory = pathRemapper.Remap(template.BuildXlBinDirectory);
            FileContentTableFile = pathRemapper.Remap(template.FileContentTableFile);
            SymlinkDefinitionFile = pathRemapper.Remap(template.SymlinkDefinitionFile);
            SchedulerFileChangeTrackerFile = pathRemapper.Remap(template.SchedulerFileChangeTrackerFile);
            IncrementalSchedulingStateFile = pathRemapper.Remap(template.IncrementalSchedulingStateFile);
            FingerprintStoreDirectory = pathRemapper.Remap(template.FingerprintStoreDirectory);
        }

        /// <inheritdoc />
        public AbsolutePath PrimaryConfigFile { get; set; }

        /// <inheritdoc />
        public AbsolutePath SourceDirectory { get; set; }

        /// <inheritdoc />
        public AbsolutePath OutputDirectory { get; set; }

        /// <inheritdoc />
        public AbsolutePath ObjectDirectory { get; set; }

        /// <inheritdoc />
        public AbsolutePath FrontEndDirectory { get; set; }

        /// <inheritdoc />
        public AbsolutePath CacheDirectory { get; set; }

        /// <inheritdoc />
        public AbsolutePath EngineCacheDirectory { get; set; }

        /// <inheritdoc />
        public AbsolutePath TempDirectory { get; set; }

        /// <inheritdoc />
        public AbsolutePath BuildXlBinDirectory { get; set; }

        /// <inheritdoc />
        public AbsolutePath FileContentTableFile { get; set; }

        /// <inheritdoc />
        public AbsolutePath SymlinkDefinitionFile { get; set; }

        /// <inheritdoc />
        public AbsolutePath SchedulerFileChangeTrackerFile { get; set; }

        /// <inheritdoc />
        public AbsolutePath IncrementalSchedulingStateFile { get; set; }
        
        /// <inheritdoc />
        public AbsolutePath FingerprintStoreDirectory { get; set; }
    }
}
