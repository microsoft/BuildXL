// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Distribution;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Extension methods for XLGpp ProtoBuf conversions.
    /// </summary>
    public static class XLGppProtobufExtension
    {

        public static FileArtifactContentDecidedEvent_XLGpp ToFileArtifactContentDecidedEvent_XLGpp(this FileArtifactContentDecidedEventData data, uint workerID, PathTable pathTable)
        {
            var fileArtifactContentDecidedEvent = new FileArtifactContentDecidedEvent_XLGpp();
            var uuid = Guid.NewGuid().ToString();

            var fileArtifact = new FileArtifact_XLGpp
            {
                Path = new AbsolutePath_XLGpp() { Value = data.FileArtifact.Path.ToString(pathTable, PathFormat.HostOs) },
                RewriteCount = data.FileArtifact.RewriteCount,
                IsSourceFile = data.FileArtifact.IsSourceFile,
                IsOutputFile = data.FileArtifact.IsOutputFile
            };

            var fileContentInfo = new FileContentInfo_XLGpp
            {
                LengthAndExistence = data.FileContentInfo.SerializedLengthAndExistence,
                Hash = new ContentHash_XLGpp() { Value = data.FileContentInfo.Hash.ToString() }
            };

            fileArtifactContentDecidedEvent.UUID = uuid;
            fileArtifactContentDecidedEvent.WorkerID = workerID;
            fileArtifactContentDecidedEvent.FileArtifact = fileArtifact;
            fileArtifactContentDecidedEvent.FileContentInfo = fileContentInfo;
            fileArtifactContentDecidedEvent.OutputOrigin = (PipOutputOrigin_XLGpp)data.OutputOrigin;
            return fileArtifactContentDecidedEvent;
        }

        public static WorkerListEvent_XLGpp ToWorkerListEvent_XLGpp(this WorkerListEventData data, uint workerID)
        {
            var workerListEvent = new WorkerListEvent_XLGpp();
            var uuid = Guid.NewGuid().ToString();

            workerListEvent.UUID = uuid;
            foreach (var worker in data.Workers)
            {
                workerListEvent.Workers.Add(worker);
            }
            return workerListEvent;
        }

        public static PipExecutionPerformanceEvent_XLGpp ToPipExecutionPerformanceEvent_XLGpp(this PipExecutionPerformanceEventData data)
        {
            var pipExecPerfEvent = new PipExecutionPerformanceEvent_XLGpp();
            var uuid = Guid.NewGuid().ToString();

            var pipExecPerformance = new PipExecutionPerformance_XLGpp();
            pipExecPerformance.PipExecutionLevel = (int)data.ExecutionPerformance.ExecutionLevel;
            pipExecPerformance.ExecutionStart = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.ExecutionPerformance.ExecutionStart);
            pipExecPerformance.ExecutionStop = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.ExecutionPerformance.ExecutionStop);

            var processPipExecPerformance = new ProcessPipExecutionPerformance_XLGpp();
            var performance = data.ExecutionPerformance as ProcessPipExecutionPerformance;
            if (performance != null)
            {
                processPipExecPerformance.ProcessExecutionTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(performance.ProcessExecutionTime);
                processPipExecPerformance.ReadCounters = new IOTypeCounters_XLGpp()
                {
                    OperationCount = performance.IO.ReadCounters.OperationCount,
                    TransferCOunt = performance.IO.ReadCounters.TransferCount
                };

                processPipExecPerformance.WriteCounters = new IOTypeCounters_XLGpp()
                {
                    OperationCount = performance.IO.WriteCounters.OperationCount,
                    TransferCOunt = performance.IO.WriteCounters.TransferCount
                };

                processPipExecPerformance.OtherCounters = new IOTypeCounters_XLGpp()
                {
                    OperationCount = performance.IO.OtherCounters.OperationCount,
                    TransferCOunt = performance.IO.OtherCounters.TransferCount
                };

                processPipExecPerformance.UserTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(performance.UserTime);
                processPipExecPerformance.KernelTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(performance.KernelTime);
                processPipExecPerformance.PeakMemoryUsage = performance.PeakMemoryUsage;
                processPipExecPerformance.PeakMemoryUsageMb = performance.PeakMemoryUsageMb;
                processPipExecPerformance.NumberOfProcesses = performance.NumberOfProcesses;

                processPipExecPerformance.FileMonitoringViolationCounters = new FileMonitoringViolationCounters_XLGpp()
                {
                    NumFileAccessesWhitelistedAndCacheable = performance.FileMonitoringViolations.NumFileAccessesWhitelistedAndCacheable,
                    NumFileAccessesWhitelistedButNotCacheable = performance.FileMonitoringViolations.NumFileAccessesWhitelistedButNotCacheable,
                    NumFileAccessViolationsNotWhitelisted = performance.FileMonitoringViolations.NumFileAccessViolationsNotWhitelisted
                };

                processPipExecPerformance.Fingerprint = new Fingerprint_XLGpp()
                {
                    Length = performance.Fingerprint.Length,
                    Bytes = Google.Protobuf.ByteString.CopyFrom(performance.Fingerprint.ToByteArray())
                };

                if (performance.CacheDescriptorId.HasValue)
                {
                    processPipExecPerformance.CacheDescriptorId = performance.CacheDescriptorId.Value;
                }
            }

            pipExecPerfEvent.UUID = uuid;
            pipExecPerfEvent.WorkerID = data.ExecutionPerformance.WorkerId;
            pipExecPerfEvent.PipID = data.PipId.Value;
            pipExecPerfEvent.PipExecutionPerformance = pipExecPerformance;
            pipExecPerfEvent.ProcessPipExecutionPerformance = processPipExecPerformance;
            return pipExecPerfEvent;
        }

        public static DirectoryMembershipHashedEvent_XLGpp ToDirectoryMembershipHashedEvent_XLGpp(this DirectoryMembershipHashedEventData data)
        {
            return new DirectoryMembershipHashedEvent_XLGpp();
        }

        public static ProcessExecutionMonitoringReportedEvent_XLGpp ToProcessExecutionMonitoringReportedEvent_XLGpp(this ProcessExecutionMonitoringReportedEventData data, uint workerID, PathTable pathTable)
        {
            var uuid = Guid.NewGuid().ToString();

            var processExecutionMonitoringReportedEvent = new ProcessExecutionMonitoringReportedEvent_XLGpp
            {
                UUID = uuid,
                WorkerID = workerID,
                PipID = data.PipId.Value
            };

            foreach (var rp in data.ReportedProcesses)
            {
                processExecutionMonitoringReportedEvent.ReportedProcesses.Add(new ReportedProcess_XLGpp()
                {
                    Path = rp.Path,
                    ProcessId = rp.ProcessId,
                    ProcessArgs = rp.ProcessArgs,
                    ReadCounters = new IOTypeCounters_XLGpp
                    {
                        OperationCount = rp.IOCounters.ReadCounters.OperationCount,
                        TransferCOunt = rp.IOCounters.ReadCounters.TransferCount
                    },
                    WriteCounters = new IOTypeCounters_XLGpp
                    {
                        OperationCount = rp.IOCounters.WriteCounters.OperationCount,
                        TransferCOunt = rp.IOCounters.WriteCounters.TransferCount
                    },
                    OtherCounters = new IOTypeCounters_XLGpp
                    {
                        OperationCount = rp.IOCounters.OtherCounters.OperationCount,
                        TransferCOunt = rp.IOCounters.OtherCounters.TransferCount
                    },
                    CreationTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(rp.CreationTime),
                    ExitTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(rp.ExitTime),
                    KernelTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(rp.KernelTime),
                    UserTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(rp.UserTime),
                    ExitCode = rp.ExitCode,
                    ParentProcessId = rp.ParentProcessId
                });
            }

            foreach (var reportedFileAccess in data.ReportedFileAccesses)
            {
                processExecutionMonitoringReportedEvent.ReportedFileAccesses.Add(reportedFileAccess.ToReportedFileAccess_XLGpp(pathTable));
            }

            foreach (var whiteListReportedFileAccess in data.WhitelistedReportedFileAccesses)
            {
                processExecutionMonitoringReportedEvent.WhitelistedReportedFileAccesses.Add(whiteListReportedFileAccess.ToReportedFileAccess_XLGpp(pathTable));
            }

            foreach (var processDetouringStatus in data.ProcessDetouringStatuses)
            {
                processExecutionMonitoringReportedEvent.ProcessDetouringStatuses.Add(new ProcessDetouringStatusData_XLGpp()
                {
                    ProcessID = processDetouringStatus.ProcessId,
                    ReportStatus = processDetouringStatus.ReportStatus,
                    ProcessName = processDetouringStatus.ProcessName,
                    StartApplicationName = processDetouringStatus.StartApplicationName,
                    StartCommandLine = processDetouringStatus.StartCommandLine,
                    NeedsInjection = processDetouringStatus.NeedsInjection,
                    Job = processDetouringStatus.Job,
                    DisableDetours = processDetouringStatus.DisableDetours,
                    CreationFlags = processDetouringStatus.CreationFlags,
                    Detoured = processDetouringStatus.Detoured,
                    Error = processDetouringStatus.Error
                });
            }

            return processExecutionMonitoringReportedEvent;
        }

        public static ProcessFingerprintComputationEvent_XLGpp ToProcessFingerprintComputationEvent_XLGpp(this ProcessFingerprintComputationEventData data)
        {
            return new ProcessFingerprintComputationEvent_XLGpp();
        }

        public static ExtraEventDataReported_XLGpp ToExtraEventDataReported_XLGpp(this ExtraEventData data, uint workerID)
        {
            var uuid = Guid.NewGuid().ToString();

            return new ExtraEventDataReported_XLGpp
            {
                UUID = uuid,
                WorkerID = workerID,
                DisableDetours = data.DisableDetours,
                IgnoreReparsePoints = data.IgnoreReparsePoints,
                IgnorePreloadedDlls = data.IgnorePreloadedDlls,
                ExistingDirectoryProbesAsEnumerations = data.ExistingDirectoryProbesAsEnumerations,
                NtFileCreateMonitored = data.NtFileCreateMonitored,
                ZwFileCreateOpenMonitored = data.ZwFileCreateOpenMonitored,
                IgnoreZwRenameFileInformation = data.IgnoreZwRenameFileInformation,
                IgnoreZwOtherFileInformation = data.IgnoreZwOtherFileInformation,
                IgnoreNonCreateFileReparsePoints = data.IgnoreNonCreateFileReparsePoints,
                IgnoreSetFileInformationByHandle = data.IgnoreSetFileInformationByHandle,
                IgnoreGetFinalPathNameByHandle = data.IgnoreGetFinalPathNameByHandle,
                FingerprintVersion = (int)data.FingerprintVersion,
                FingerprintSalt = data.FingerprintSalt,
                SearchPathToolsHash = new ContentHash_XLGpp() { Value = data.SearchPathToolsHash.ToString() },
                UnexpectedFileAccessesAreErrors = data.UnexpectedFileAccessesAreErrors,
                MonitorFileAccesses = data.MonitorFileAccesses,
                MaskUntrackedAccesses = data.MaskUntrackedAccesses,
                NormalizeReadTimestamps = data.NormalizeReadTimestamps,
                PipWarningsPromotedToErrors = data.PipWarningsPromotedToErrors,
                ValidateDistribution = data.ValidateDistribution,
                RequiredKextVersionNumber = data.RequiredKextVersionNumber
            };
        }

        public static DependencyViolationReportedEvent_XLGpp ToDependencyViolationReportedEvent_XLGpp(this DependencyViolationEventData data, uint workerID, PathTable pathTable)
        {
            var uuid = Guid.NewGuid().ToString();
            return new DependencyViolationReportedEvent_XLGpp()
            {
                UUID = uuid,
                WorkerID = workerID,
                ViolatorPipID = data.ViolatorPipId.Value,
                RelatedPipID = data.RelatedPipId.Value,
                ViolationType = (FileMonitoringViolationAnalyzer_DependencyViolationType_XLGpp)data.ViolationType,
                AccessLevel = (FileMonitoringViolationAnalyzer_AccessLevel_XLGpp)data.AccessLevel,
                Path = data.Path.ToString(pathTable, PathFormat.HostOs)
            };
        }

        public static PipExecutionStepPerformanceReportedEvent_XLGpp ToPipExecutionStepPerformanceReportedEvent_XLGpp(this PipExecutionStepPerformanceEventData data, uint workerID)
        {
            var uuid = Guid.NewGuid().ToString();

            var pipExecStepPerformanceEvent = new PipExecutionStepPerformanceReportedEvent_XLGpp
            {
                UUID = uuid,
                WorkerID = workerID,
                PipID = data.PipId.Value,
                StartTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.StartTime),
                Duration = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(data.Duration),
                Step = (PipExecutionStep_XLGpp)data.Step,
                Dispatcher = (WorkDispatcher_DispatcherKind_XLGpp)data.Dispatcher
            };

            return pipExecStepPerformanceEvent;
        }


        public static PipCacheMissEvent_XLGpp ToPipCacheMissEvent_XLGpp(this PipCacheMissEventData data, uint workerID)
        {
            var uuid = Guid.NewGuid().ToString();
            return new PipCacheMissEvent_XLGpp()
            {
                UUID = uuid,
                WorkerID = workerID,
                PipID = data.PipId.Value,
                CacheMissType = (PipCacheMissType_XLGpp)data.CacheMissType
            };
        }

        public static StatusReportedEvent_XLGpp ToResourceUsageReportedEvent_XLGpp(this StatusEventData data, uint workerID)
        {
            var uuid = Guid.NewGuid().ToString();

            var statusReportedEvent = new StatusReportedEvent_XLGpp()
            {
                UUID = uuid,
                WorkerID = workerID,
                Time = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.Time),
                CpuPercent = data.CpuPercent,
                RamPercent = data.RamPercent,
                MachineRamUtilizationMB = data.MachineRamUtilizationMB,
                CommitPercent = data.CommitPercent,
                CommitTotalMB = data.CommitTotalMB,
                ProcessCpuPercent = data.ProcessCpuPercent,
                ProcessWorkingSetMB = data.ProcessWorkingSetMB,
                CpuWaiting = data.CpuWaiting,
                CpuRunning = data.CpuRunning,
                IoCurrentMax = data.IoCurrentMax,
                IoWaiting = data.IoWaiting,
                IoRunning = data.IoRunning,
                LookupWaiting = data.LookupWaiting,
                LookupRunning = data.LookupRunning,
                ExternalProcesses = data.ExternalProcesses,
                LimitingResource = (ExecutionSampler_LimitingResource_XLGpp)data.LimitingResource,
                UnresponsivenessFactor = data.UnresponsivenessFactor,
                ProcessPipsPending = data.ProcessPipsPending,
                ProcessPipsAllocatedSlots = data.ProcessPipsAllocatedSlots
            };

            foreach (var percent in data.DiskPercents)
            {
                statusReportedEvent.DiskPercents.Add(percent);
            }

            foreach (var depth in data.DiskQueueDepths)
            {
                statusReportedEvent.DiskQueueDepths.Add(depth);
            }

            foreach (var type in data.PipsSucceededAllTypes)
            {
                statusReportedEvent.PipsSucceededAllTypes.Add(type);
            }

            return statusReportedEvent;
        }

        public static BXLInvocationEvent_XLGpp ToBXLInvocationEvent_XLGpp(this DominoInvocationEventData data, uint workerID, PathTable pathTable)
        {
            var bxlInvEvent = new BXLInvocationEvent_XLGpp();
            var loggingConfig = data.Configuration.Logging;

            var uuid = Guid.NewGuid().ToString();

            bxlInvEvent.UUID = uuid;
            bxlInvEvent.WorkerID = workerID;
            bxlInvEvent.SubstSource = loggingConfig.SubstSource.ToString(pathTable, PathFormat.HostOs);
            bxlInvEvent.SubstTarget = loggingConfig.SubstTarget.ToString(pathTable, PathFormat.HostOs);
            bxlInvEvent.IsSubstSourceValid = loggingConfig.SubstSource.IsValid;
            bxlInvEvent.IsSubstTargetValid = loggingConfig.SubstTarget.IsValid;

            return bxlInvEvent;
        }

        public static PipExecutionDirectoryOutputsEvent_XLGpp ToPipExecutionDirectoryOutputsEvent_XLGpp(this PipExecutionDirectoryOutputs data)
        {
            return new PipExecutionDirectoryOutputsEvent_XLGpp();
        }

        public static ReportedFileAccess_XLGpp ToReportedFileAccess_XLGpp(this ReportedFileAccess reportedFileAccess, PathTable pathTable)
        {
            return new ReportedFileAccess_XLGpp()
            {
                CreationDisposition = (CreationDisposition_XLGpp)reportedFileAccess.CreationDisposition,
                DesiredAccess = (DesiredAccess_XLGpp)reportedFileAccess.DesiredAccess,
                Error = reportedFileAccess.Error,
                Usn = reportedFileAccess.Usn.Value,
                FlagsAndAttributes = (FlagsAndAttributes_XLGpp)reportedFileAccess.FlagsAndAttributes,
                Path = reportedFileAccess.Path,
                ManifestPath = reportedFileAccess.ManifestPath.ToString(pathTable, PathFormat.HostOs),
                Process = new ReportedProcess_XLGpp()
                {
                    Path = reportedFileAccess.Process.Path,
                    ProcessId = reportedFileAccess.Process.ProcessId,
                    ProcessArgs = reportedFileAccess.Process.ProcessArgs,
                    ReadCounters = new IOTypeCounters_XLGpp
                    {
                        OperationCount = reportedFileAccess.Process.IOCounters.ReadCounters.OperationCount,
                        TransferCOunt = reportedFileAccess.Process.IOCounters.ReadCounters.TransferCount
                    },
                    WriteCounters = new IOTypeCounters_XLGpp
                    {
                        OperationCount = reportedFileAccess.Process.IOCounters.WriteCounters.OperationCount,
                        TransferCOunt = reportedFileAccess.Process.IOCounters.WriteCounters.TransferCount
                    },
                    OtherCounters = new IOTypeCounters_XLGpp
                    {
                        OperationCount = reportedFileAccess.Process.IOCounters.OtherCounters.OperationCount,
                        TransferCOunt = reportedFileAccess.Process.IOCounters.OtherCounters.TransferCount
                    },
                    CreationTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(reportedFileAccess.Process.CreationTime),
                    ExitTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(reportedFileAccess.Process.ExitTime),
                    KernelTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(reportedFileAccess.Process.KernelTime),
                    UserTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(reportedFileAccess.Process.UserTime),
                    ExitCode = reportedFileAccess.Process.ExitCode,
                    ParentProcessId = reportedFileAccess.Process.ParentProcessId
                },
                ShareMode = (ShareMode_XLGpp)reportedFileAccess.ShareMode,
                Status = (FileAccessStatus_XLGpp)reportedFileAccess.Status,
                Method = (FileAccessStatusMethod_XLGpp)reportedFileAccess.Method,
                RequestedAccess = (RequestedAccess_XLGpp)reportedFileAccess.RequestedAccess,
                Operation = (ReportedFileOperation_XLGpp)reportedFileAccess.Operation,
                ExplicitlyReported = reportedFileAccess.ExplicitlyReported,
                EnumeratePattern = reportedFileAccess.EnumeratePattern
            };
        }
    }
}
