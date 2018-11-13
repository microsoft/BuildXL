// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class SandboxConfiguration : ISandboxConfiguration
    {
        private IUnsafeSandboxConfiguration m_unsafeSandboxConfig;

        /// <nodoc />
        public SandboxConfiguration()
        {
            m_unsafeSandboxConfig = new UnsafeSandboxConfiguration();

            FailUnexpectedFileAccesses = true;
            DefaultTimeout = 10 * 60 * 1000;
            DefaultWarningTimeout = (int)(.85 * DefaultTimeout);
            TimeoutMultiplier = 1;
            WarningTimeoutMultiplier = 1;
            OutputReportingMode = OutputReportingMode.TruncatedOutputOnError;
            FileSystemMode = FileSystemMode.Unset;
            ForceReadOnlyForRequestedReadWrite = false;
            FlushPageCacheToFileSystemOnStoringOutputsToCache = true;
            NormalizeReadTimestamps = true;
            UseLargeNtClosePreallocatedList = false;
            UseExtraThreadToDrainNtClose = true;
            MaskUntrackedAccesses = true;
            LogProcessDetouringStatus = false;
            HardExitOnErrorInDetours = true;
            CheckDetoursMessageCount = true;
            AllowInternalDetoursErrorNotificationFile = true;
            CrashOnCBSSOOMError = false;
            EnforceAccessPoliciesOnDirectoryCreation = false;
            NumberOfKextConnections = 2;
            ReportQueueSizeMb = 0; // Let the sandbox kernel extension apply defaults
        }

        /// <nodoc />
        public SandboxConfiguration(ISandboxConfiguration template, PathRemapper pathRemapper)
        {
            Contract.Assume(template != null);
            m_unsafeSandboxConfig = new UnsafeSandboxConfiguration(template.UnsafeSandboxConfiguration);

            DebugInstantPipOutputs = template.DebugInstantPipOutputs;
            BreakOnUnexpectedFileAccess = template.BreakOnUnexpectedFileAccess;
            FileAccessIgnoreCodeCoverage = template.FileAccessIgnoreCodeCoverage;
            FailUnexpectedFileAccesses = template.FailUnexpectedFileAccesses;
            DefaultTimeout = template.DefaultTimeout;
            DefaultWarningTimeout = template.DefaultWarningTimeout;
            TimeoutMultiplier = template.TimeoutMultiplier;
            WarningTimeoutMultiplier = template.WarningTimeoutMultiplier;
            TimeoutDumpDirectory = pathRemapper.Remap(template.TimeoutDumpDirectory);
            LogObservedFileAccesses = template.LogObservedFileAccesses;
            LogProcesses = template.LogProcesses;
            LogProcessData = template.LogProcessData;
            LogFileAccessTables = template.LogFileAccessTables;
            OutputReportingMode = template.OutputReportingMode;
            FileSystemMode = template.FileSystemMode;
            ForceReadOnlyForRequestedReadWrite = template.ForceReadOnlyForRequestedReadWrite;
            FlushPageCacheToFileSystemOnStoringOutputsToCache = template.FlushPageCacheToFileSystemOnStoringOutputsToCache;
            NormalizeReadTimestamps = template.NormalizeReadTimestamps;
            UseLargeNtClosePreallocatedList = template.UseLargeNtClosePreallocatedList;
            UseExtraThreadToDrainNtClose = template.UseExtraThreadToDrainNtClose;
            MaskUntrackedAccesses = template.MaskUntrackedAccesses;
            LogProcessDetouringStatus = template.LogProcessDetouringStatus;
            HardExitOnErrorInDetours = template.HardExitOnErrorInDetours;
            CheckDetoursMessageCount = template.CheckDetoursMessageCount;
            AllowInternalDetoursErrorNotificationFile = template.AllowInternalDetoursErrorNotificationFile;
            CrashOnCBSSOOMError = template.CrashOnCBSSOOMError;
            EnforceAccessPoliciesOnDirectoryCreation = template.EnforceAccessPoliciesOnDirectoryCreation;
            NumberOfKextConnections = template.NumberOfKextConnections;
            ReportQueueSizeMb = template.ReportQueueSizeMb;
        }

        /// <inheritdoc />
        public IUnsafeSandboxConfiguration UnsafeSandboxConfiguration
        {
            get
            {
                return m_unsafeSandboxConfig;
            }

            set
            {
                m_unsafeSandboxConfig = value;
            }
        }

        /// <nodoc />
        public UnsafeSandboxConfiguration UnsafeSandboxConfigurationMutable
        {
            get
            {
                return (UnsafeSandboxConfiguration)m_unsafeSandboxConfig;
            }

            set
            {
                m_unsafeSandboxConfig = value;
            }
        }

        /// <inheritdoc />
        public bool DebugInstantPipOutputs { get; set; }

        /// <inheritdoc />
        public bool BreakOnUnexpectedFileAccess { get; set; }

        /// <inheritdoc />
        public bool FileAccessIgnoreCodeCoverage { get; set; }

        /// <inheritdoc />
        public bool FailUnexpectedFileAccesses { get; set; }

        /// <inheritdoc />
        public bool EnforceAccessPoliciesOnDirectoryCreation { get; set; }

        /// <inheritdoc />
        public bool ForceReadOnlyForRequestedReadWrite { get; set; }

        /// <inheritdoc />
        public bool FlushPageCacheToFileSystemOnStoringOutputsToCache { get; set; }

        /// <inheritdoc />
        public int DefaultTimeout { get; set; }

        /// <inheritdoc />
        public int DefaultWarningTimeout { get; set; }

        /// <inheritdoc />
        public int TimeoutMultiplier { get; set; }

        /// <inheritdoc />
        public int WarningTimeoutMultiplier { get; set; }

        /// <inheritdoc />
        public AbsolutePath TimeoutDumpDirectory { get; set; }

        /// <inheritdoc />
        public bool LogObservedFileAccesses { get; set; }

        /// <inheritdoc />
        public bool LogProcesses { get; set; }

        /// <inheritdoc />
        public bool LogProcessData { get; set; }

        /// <inheritdoc />
        public bool LogFileAccessTables { get; set; }

        /// <inheritdoc />
        public OutputReportingMode OutputReportingMode { get; set; }

        /// <inheritdoc />
        public FileSystemMode FileSystemMode { get; set; }

        /// <inheritdoc />
        public bool NormalizeReadTimestamps { get; set; }

        /// <inheritdoc />
        public bool UseLargeNtClosePreallocatedList { get; set; }

        /// <inheritdoc />
        public bool UseExtraThreadToDrainNtClose { get; set; }

        /// <inheritdoc />
        public bool MaskUntrackedAccesses { get; set; }

        /// <inheritdoc />
        public bool HardExitOnErrorInDetours { get; set; }

        /// <inheritdoc />
        public bool CheckDetoursMessageCount { get; set; }

        /// <inheritdoc />
        public bool LogProcessDetouringStatus { get; set; }

        /// <inheritdoc />
        public bool AllowInternalDetoursErrorNotificationFile { get; set; }

        /// <inheritdoc />
        public bool CrashOnCBSSOOMError { get; set; }

        /// <inheritdoc />
        public int NumberOfKextConnections { get; set; }

        /// <inheritdoc />
        public int ReportQueueSizeMb { get; set; }
    }
}
