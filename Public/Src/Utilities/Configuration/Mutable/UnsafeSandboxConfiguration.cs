// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
            IgnorePreloadedDlls = true; // TODO: Make this false when onboarded by users.
            SandboxKind = SandboxKind.Default;

            // TODO: this is a temporarily flag. Take it out in few weeks.
            ExistingDirectoryProbesAsEnumerations = false;
            IgnoreZwRenameFileInformation = false;
            IgnoreZwOtherFileInformation = false;
            IgnoreNonCreateFileReparsePoints = false;
            IgnoreSetFileInformationByHandle = false;
            PreserveOutputs = PreserveOutputsMode.Disabled;
            IgnoreGetFinalPathNameByHandle = false;
            MonitorZwCreateOpenQueryFile = true;
            IgnoreDynamicWritesOnAbsentProbes = false;
            IgnoreUndeclaredAccessesUnderSharedOpaques = true;
            // Make sure to update SafeOptions below if necessary when new flags are added
        }

        /// <summary>
        /// Object representing which options are safe. Generally this is just the defaults from above, except for
        /// when the defaults represent an unsafe mode of operation. In that case, the safe mode must be specified here.
        /// </summary>
        public static readonly IUnsafeSandboxConfiguration SafeOptions = new UnsafeSandboxConfiguration()
        {
            IgnorePreloadedDlls = false,
            IgnoreUndeclaredAccessesUnderSharedOpaques = false,
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
            IgnoreGetFinalPathNameByHandle = template.IgnoreGetFinalPathNameByHandle;
            IgnoreDynamicWritesOnAbsentProbes = template.IgnoreDynamicWritesOnAbsentProbes;
            DoubleWritePolicy = template.DoubleWritePolicy;
            IgnoreUndeclaredAccessesUnderSharedOpaques = template.IgnoreUndeclaredAccessesUnderSharedOpaques;
        }

        /// <inheritdoc />
        public PreserveOutputsMode PreserveOutputs { get; set; }

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
        public bool IgnoreDynamicWritesOnAbsentProbes { get; set; }

        /// <inheritdoc />
        public DoubleWritePolicy? DoubleWritePolicy { get; set; }

        /// <inheritdoc />
        public bool IgnoreUndeclaredAccessesUnderSharedOpaques { get; set; }
    }
}
