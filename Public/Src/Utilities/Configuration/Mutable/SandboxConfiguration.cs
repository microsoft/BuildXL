// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;

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
            DefaultTimeout = ((int)Defaults.ProcessTimeoutInMinutes) * 60 * 1000;
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
            EnforceAccessPoliciesOnDirectoryCreation = false;
            MeasureProcessCpuTimes = true;                  // always measure process times + ram consumption
            AdminRequiredProcessExecutionMode = AdminRequiredProcessExecutionMode.Internal;
            RedirectedTempFolderRootForVmExecution = AbsolutePath.Invalid;
            RetryOnAzureWatsonExitCode = false;
            EnsureTempDirectoriesExistenceBeforePipExecution = false;
            GlobalUnsafeUntrackedScopes = new List<AbsolutePath>();
            PreserveOutputsForIncrementalTool = false;
            GlobalUnsafePassthroughEnvironmentVariables = new List<string>();
            VmConcurrencyLimit = 0;
            DirectoriesToEnableFullReparsePointParsing = new List<AbsolutePath>();
            ExplicitlyReportDirectoryProbes = OperatingSystemHelper.IsLinuxOS;
            PreserveFileSharingBehaviour = false;
            EnableLinuxPTraceSandbox = true;
            AlwaysRemoteInjectDetoursFrom32BitProcess = true;
            UnconditionallyEnableLinuxPTraceSandbox = false;
            // TODO: flip the default once we have verified this is not a breaking change
            ForceAddExecutionPermission = true;
            EnableEBPFLinuxSandbox = OperatingSystemHelper.IsLinuxOS;
        }

        /// <nodoc />
        public SandboxConfiguration(ISandboxConfiguration template, PathRemapper pathRemapper)
        {
            Contract.Assume(template != null);
            m_unsafeSandboxConfig = new UnsafeSandboxConfiguration(template.UnsafeSandboxConfiguration);

            BreakOnUnexpectedFileAccess = template.BreakOnUnexpectedFileAccess;
            FileAccessIgnoreCodeCoverage = template.FileAccessIgnoreCodeCoverage;
            FailUnexpectedFileAccesses = template.FailUnexpectedFileAccesses;
            DefaultTimeout = template.DefaultTimeout;
            DefaultWarningTimeout = template.DefaultWarningTimeout;
            TimeoutMultiplier = template.TimeoutMultiplier;
            WarningTimeoutMultiplier = template.WarningTimeoutMultiplier;
            TimeoutDumpDirectory = pathRemapper.Remap(template.TimeoutDumpDirectory);
            SurvivingPipProcessChildrenDumpDirectory = pathRemapper.Remap(template.SurvivingPipProcessChildrenDumpDirectory);
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
            EnforceAccessPoliciesOnDirectoryCreation = template.EnforceAccessPoliciesOnDirectoryCreation;
            MeasureProcessCpuTimes = template.MeasureProcessCpuTimes;
            AdminRequiredProcessExecutionMode = template.AdminRequiredProcessExecutionMode;
            RedirectedTempFolderRootForVmExecution = pathRemapper.Remap(template.RedirectedTempFolderRootForVmExecution);
            RetryOnAzureWatsonExitCode = template.RetryOnAzureWatsonExitCode;
            EnsureTempDirectoriesExistenceBeforePipExecution = template.EnsureTempDirectoriesExistenceBeforePipExecution;
            GlobalUnsafeUntrackedScopes = pathRemapper.Remap(template.GlobalUnsafeUntrackedScopes);
            PreserveOutputsForIncrementalTool = template.PreserveOutputsForIncrementalTool;
            GlobalUnsafePassthroughEnvironmentVariables = new List<string>(template.GlobalUnsafePassthroughEnvironmentVariables);
            VmConcurrencyLimit = template.VmConcurrencyLimit;
            DirectoriesToEnableFullReparsePointParsing = pathRemapper.Remap(template.DirectoriesToEnableFullReparsePointParsing);
            ExplicitlyReportDirectoryProbes = template.ExplicitlyReportDirectoryProbes;
            PreserveFileSharingBehaviour = template.PreserveFileSharingBehaviour;
            EnableLinuxPTraceSandbox = template.EnableLinuxPTraceSandbox;
            AlwaysRemoteInjectDetoursFrom32BitProcess = template.AlwaysRemoteInjectDetoursFrom32BitProcess;
            UnconditionallyEnableLinuxPTraceSandbox = template.UnconditionallyEnableLinuxPTraceSandbox;
            ForceAddExecutionPermission = template.ForceAddExecutionPermission;
            EnableEBPFLinuxSandbox = template.EnableEBPFLinuxSandbox;
            EBPFRingBufferSizeMultiplier = template.EBPFRingBufferSizeMultiplier;
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
        public AbsolutePath SurvivingPipProcessChildrenDumpDirectory { get; set; }

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
        public bool MeasureProcessCpuTimes { get; set; }

        /// <inheritdoc />
        public AdminRequiredProcessExecutionMode AdminRequiredProcessExecutionMode { get; set; }

        /// <inheritdoc />
        public AbsolutePath RedirectedTempFolderRootForVmExecution { get; set; }

        /// <inheritdoc />
        public bool RetryOnAzureWatsonExitCode { get; set; }

        /// <inheritdoc />
        public bool EnsureTempDirectoriesExistenceBeforePipExecution { get; set; }

        /// <nodoc />
        public List<AbsolutePath> GlobalUnsafeUntrackedScopes { get; set; }

        /// <inheritdoc />
        IReadOnlyList<AbsolutePath> ISandboxConfiguration.GlobalUnsafeUntrackedScopes => GlobalUnsafeUntrackedScopes;

        /// <inheritdoc />
        public bool PreserveOutputsForIncrementalTool { get; set; }

        /// <nodoc />
        public List<string> GlobalUnsafePassthroughEnvironmentVariables { get; set; }

        /// <inheritdoc />
        IReadOnlyList<string> ISandboxConfiguration.GlobalUnsafePassthroughEnvironmentVariables => GlobalUnsafePassthroughEnvironmentVariables;

        /// <inheritdoc />
        public int VmConcurrencyLimit { get; set; }

        /// <nodoc />
        public List<AbsolutePath> DirectoriesToEnableFullReparsePointParsing { get; set; }

        /// <inheritdoc />
        IReadOnlyList<AbsolutePath> ISandboxConfiguration.DirectoriesToEnableFullReparsePointParsing => DirectoriesToEnableFullReparsePointParsing;

        /// <inheritdoc />
        public bool ExplicitlyReportDirectoryProbes { get; set; }

        /// <inheritdoc />
        public bool PreserveFileSharingBehaviour { get; set; }

        /// <inheritdoc />
        public bool EnableLinuxPTraceSandbox { get; set; }

        /// <inheritdoc />
        public bool AlwaysRemoteInjectDetoursFrom32BitProcess { get; set; }

        /// <inheritdoc />
        public bool UnconditionallyEnableLinuxPTraceSandbox { get; set; }

        /// <inheritdoc/>
        public bool ForceAddExecutionPermission { get; set; }

        /// <inheritdoc/>
        public bool EnableEBPFLinuxSandbox { get; set; }

        /// <inheritdoc/>
        public int? EBPFRingBufferSizeMultiplier { get; set; }
    }
}
