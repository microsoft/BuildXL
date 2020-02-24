// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class UnsafeSandboxConfiguration : IUnsafeSandboxConfiguration
    {
        /// <nodoc />
        public UnsafeSandboxConfiguration()
        {
            MonitorFileAccesses = true;
            MonitorNtCreateFile = true;
            UnexpectedFileAccessesAreErrors = true;
            IgnoreReparsePoints = false;
            IgnorePreloadedDlls = false;
            SandboxKind = SandboxKind.Default;

            // TODO: this is a temporarily flag. Take it out in few weeks.
            ExistingDirectoryProbesAsEnumerations = false;
            IgnoreZwRenameFileInformation = false;
            IgnoreZwOtherFileInformation = false;
            IgnoreNonCreateFileReparsePoints = false;
            IgnoreSetFileInformationByHandle = false;
            PreserveOutputs = PreserveOutputsMode.Disabled;
            PreserveOutputsTrustLevel = (int)PreserveOutputsTrustValue.Lowest;
            IgnoreGetFinalPathNameByHandle = false;
            MonitorZwCreateOpenQueryFile = true;
            IgnoreDynamicWritesOnAbsentProbes = DynamicWriteOnAbsentProbePolicy.IgnoreDirectoryProbes; // TODO: eventually change this to IgnoreNothing
            IgnoreUndeclaredAccessesUnderSharedOpaques = false;
            
            // Make sure to update SafeOptions below if necessary when new flags are added
        }

        /// <summary>
        /// Object representing which options are safe. Generally this is just the defaults from above, except for
        /// when the defaults represent an unsafe mode of operation. In that case, the safe mode must be specified here.
        /// </summary>
        public static readonly IUnsafeSandboxConfiguration SafeOptions = new UnsafeSandboxConfiguration()
        {
            IgnorePreloadedDlls = false,
            IgnoreCreateProcessReport = false,
            IgnoreDynamicWritesOnAbsentProbes = DynamicWriteOnAbsentProbePolicy.IgnoreNothing
        };

        /// <nodoc />
        public UnsafeSandboxConfiguration(IUnsafeSandboxConfiguration template)
        {
            MonitorFileAccesses = template.MonitorFileAccesses;
            MonitorNtCreateFile = template.MonitorNtCreateFile;
            MonitorZwCreateOpenQueryFile = template.MonitorZwCreateOpenQueryFile;
            UnexpectedFileAccessesAreErrors = template.UnexpectedFileAccessesAreErrors;
            IgnoreZwRenameFileInformation = template.IgnoreZwRenameFileInformation;
            IgnoreZwOtherFileInformation = template.IgnoreZwOtherFileInformation;
            IgnoreNonCreateFileReparsePoints = template.IgnoreNonCreateFileReparsePoints;
            IgnoreSetFileInformationByHandle = template.IgnoreSetFileInformationByHandle;
            IgnoreReparsePoints = template.IgnoreReparsePoints;
            IgnorePreloadedDlls = template.IgnorePreloadedDlls;
            SandboxKind = template.SandboxKind;
            ExistingDirectoryProbesAsEnumerations = template.ExistingDirectoryProbesAsEnumerations;
            PreserveOutputs = template.PreserveOutputs;
            PreserveOutputsTrustLevel = template.PreserveOutputsTrustLevel;
            IgnoreGetFinalPathNameByHandle = template.IgnoreGetFinalPathNameByHandle;
            IgnoreDynamicWritesOnAbsentProbes = template.IgnoreDynamicWritesOnAbsentProbes;
            DoubleWritePolicy = template.DoubleWritePolicy;
            IgnoreUndeclaredAccessesUnderSharedOpaques = template.IgnoreUndeclaredAccessesUnderSharedOpaques;
            IgnoreCreateProcessReport = template.IgnoreCreateProcessReport;
        }

        /// <inheritdoc />
        public PreserveOutputsMode PreserveOutputs { get; set; }

        /// <inheritdoc />
        public int PreserveOutputsTrustLevel { get; set; }

        /// <inheritdoc />
        public bool MonitorFileAccesses { get; set; }

        /// <inheritdoc />
        public bool IgnoreZwRenameFileInformation { get; set; }

        /// <inheritdoc />
        public bool IgnoreZwOtherFileInformation { get; set; }

        /// <inheritdoc />
        public bool IgnoreNonCreateFileReparsePoints { get; set; }

        /// <inheritdoc />
        public bool IgnoreSetFileInformationByHandle { get; set; }

        /// <inheritdoc />
        public bool IgnoreReparsePoints { get; set; }

        /// <inheritdoc />
        public bool IgnorePreloadedDlls { get; set; }

        /// <inheritdoc />
        public bool ExistingDirectoryProbesAsEnumerations { get; set; }

        /// <inheritdoc />
        public bool MonitorNtCreateFile { get; set; }

        /// <inheritdoc />
        public bool MonitorZwCreateOpenQueryFile { get; set; }

        /// <inheritdoc />
        public SandboxKind SandboxKind { get; set; }

        /// <inheritdoc />
        public bool UnexpectedFileAccessesAreErrors { get; set; }

        /// <inheritdoc />
        public bool IgnoreGetFinalPathNameByHandle { get; set; }

        /// <inheritdoc />
        public DynamicWriteOnAbsentProbePolicy IgnoreDynamicWritesOnAbsentProbes { get; set; }

        /// <inheritdoc />
        public DoubleWritePolicy? DoubleWritePolicy { get; set; }

        /// <inheritdoc />
        public bool IgnoreUndeclaredAccessesUnderSharedOpaques { get; set; }

        /// <inheritdoc />
        public bool IgnoreCreateProcessReport { get; set; }
    }
}
