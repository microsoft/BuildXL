// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Execution.Analyzer.Xldb;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using AbsolutePath = BuildXL.Utilities.AbsolutePath;
using CopyFile = BuildXL.Pips.Operations.CopyFile;
using DirectedGraph = BuildXL.Scheduler.Graph.DirectedGraph;
using DirectoryArtifact = BuildXL.Utilities.DirectoryArtifact;
using Edge = BuildXL.Scheduler.Graph.Edge;
using FileArtifact = BuildXL.Utilities.FileArtifact;
using FileOrDirectoryArtifact = BuildXL.Utilities.FileOrDirectoryArtifact;
using Fingerprint = BuildXL.Cache.MemoizationStore.Interfaces.Sessions.Fingerprint;
using HashSourceFile = BuildXL.Pips.Operations.HashSourceFile;
using IpcPip = BuildXL.Pips.Operations.IpcPip;
using ModulePip = BuildXL.Pips.Operations.ModulePip;
using NodeId = BuildXL.Scheduler.Graph.NodeId;
using NodeRange = BuildXL.Scheduler.Graph.NodeRange;
using ObservedPathEntry = BuildXL.Scheduler.Fingerprints.ObservedPathEntry;
using ObservedPathSet = BuildXL.Scheduler.Fingerprints.ObservedPathSet;
using Pip = BuildXL.Pips.Operations.Pip;
using PipData = BuildXL.Pips.Operations.PipData;
using PipGraph = BuildXL.Scheduler.Graph.PipGraph;
using PipProvenance = BuildXL.Pips.Operations.PipProvenance;
using PipTable = BuildXL.Pips.PipTable;
using Process = BuildXL.Pips.Operations.Process;
using ProcessPipExecutionPerformance = BuildXL.Pips.ProcessPipExecutionPerformance;
using ReportedFileAccess = BuildXL.Processes.ReportedFileAccess;
using ReportedProcess = BuildXL.Processes.ReportedProcess;
using SealDirectory = BuildXL.Pips.Operations.SealDirectory;
using SpecFilePip = BuildXL.Pips.Operations.SpecFilePip;
using UnsafeOptions = BuildXL.Scheduler.Fingerprints.UnsafeOptions;
using WriteFile = BuildXL.Pips.Operations.WriteFile;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Extension methods for Xldb ProtoBuf conversions.
    /// </summary>
    public static class XldbProtobufExtensions
    {
        /// <nodoc />
        public static FileArtifactContentDecidedEvent ToFileArtifactContentDecidedEvent(this FileArtifactContentDecidedEventData data, uint workerID, PathTable pathTable)
        {
            return new FileArtifactContentDecidedEvent()
            {
                WorkerID = workerID,
                FileArtifact = data.FileArtifact.ToFileArtifact(pathTable),
                FileContentInfo = new FileContentInfo
                {
                    LengthAndExistence = data.FileContentInfo.SerializedLengthAndExistence,
                    Hash = new ContentHash() { Value = data.FileContentInfo.Hash.ToString() }
                },
                OutputOrigin = (PipOutputOrigin)data.OutputOrigin
            };
        }

        /// <nodoc />
        public static WorkerListEvent ToWorkerListEvent(this WorkerListEventData data, uint workerID)
        {
            var workerListEvent = new WorkerListEvent
            {
                WorkerID = workerID
            };

            workerListEvent.Workers.AddRange(data.Workers.Select(worker => worker));
            return workerListEvent;
        }

        /// <nodoc />
        public static PipExecutionPerformanceEvent ToPipExecutionPerformanceEvent(this PipExecutionPerformanceEventData data)
        {
            var pipExecPerfEvent = new PipExecutionPerformanceEvent();
            var pipExecPerformance = new PipExecutionPerformance();
            pipExecPerformance.PipExecutionLevel = (int)data.ExecutionPerformance.ExecutionLevel;
            pipExecPerformance.ExecutionStart = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.ExecutionPerformance.ExecutionStart);
            pipExecPerformance.ExecutionStop = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.ExecutionPerformance.ExecutionStop);

            var processPipExecPerformance = new Xldb.ProcessPipExecutionPerformance();
            var performance = data.ExecutionPerformance as ProcessPipExecutionPerformance;
            if (performance != null)
            {
                processPipExecPerformance.ProcessExecutionTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(performance.ProcessExecutionTime);
                processPipExecPerformance.ReadCounters = new IOTypeCounters()
                {
                    OperationCount = performance.IO.ReadCounters.OperationCount,
                    TransferCOunt = performance.IO.ReadCounters.TransferCount
                };

                processPipExecPerformance.WriteCounters = new IOTypeCounters()
                {
                    OperationCount = performance.IO.WriteCounters.OperationCount,
                    TransferCOunt = performance.IO.WriteCounters.TransferCount
                };

                processPipExecPerformance.OtherCounters = new IOTypeCounters()
                {
                    OperationCount = performance.IO.OtherCounters.OperationCount,
                    TransferCOunt = performance.IO.OtherCounters.TransferCount
                };

                processPipExecPerformance.UserTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(performance.UserTime);
                processPipExecPerformance.KernelTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(performance.KernelTime);
                processPipExecPerformance.PeakMemoryUsage = performance.PeakMemoryUsage;
                processPipExecPerformance.PeakMemoryUsageMb = performance.PeakMemoryUsageMb;
                processPipExecPerformance.NumberOfProcesses = performance.NumberOfProcesses;

                processPipExecPerformance.FileMonitoringViolationCounters = new FileMonitoringViolationCounters()
                {
                    NumFileAccessesWhitelistedAndCacheable = performance.FileMonitoringViolations.NumFileAccessesWhitelistedAndCacheable,
                    NumFileAccessesWhitelistedButNotCacheable = performance.FileMonitoringViolations.NumFileAccessesWhitelistedButNotCacheable,
                    NumFileAccessViolationsNotWhitelisted = performance.FileMonitoringViolations.NumFileAccessViolationsNotWhitelisted
                };

                processPipExecPerformance.Fingerprint = performance.Fingerprint.ToFingerprint();

                if (performance.CacheDescriptorId.HasValue)
                {
                    processPipExecPerformance.CacheDescriptorId = performance.CacheDescriptorId.Value;
                }
            }

            pipExecPerfEvent.WorkerID = data.ExecutionPerformance.WorkerId;
            pipExecPerfEvent.PipID = data.PipId.Value;
            pipExecPerfEvent.PipExecutionPerformance = pipExecPerformance;
            pipExecPerfEvent.ProcessPipExecutionPerformance = processPipExecPerformance;
            return pipExecPerfEvent;
        }

        /// <nodoc />
        public static DirectoryMembershipHashedEvent ToDirectoryMembershipHashedEvent(this DirectoryMembershipHashedEventData data, uint workerID, PathTable pathTable)
        {
            var directoryMembershipEvent = new DirectoryMembershipHashedEvent()
            {
                WorkerID = workerID,
                DirectoryFingerprint = new DirectoryFingerprint()
                {
                    Hash = new ContentHash() { Value = data.DirectoryFingerprint.Hash.ToString() }
                },
                Directory = data.Directory.ToAbsolutePath(pathTable),
                IsStatic = data.IsSearchPath,
                IsSearchPath = data.IsSearchPath,
                PipID = data.PipId.Value,
                EnumeratePatternRegex = data.EnumeratePatternRegex ?? ""
            };

            directoryMembershipEvent.Members.AddRange(data.Members.Select(member => member.ToAbsolutePath(pathTable)));

            return directoryMembershipEvent;
        }

        /// <nodoc />
        public static ProcessExecutionMonitoringReportedEvent ToProcessExecutionMonitoringReportedEvent(this ProcessExecutionMonitoringReportedEventData data, uint workerID, PathTable pathTable)
        {
            var processExecutionMonitoringReportedEvent = new ProcessExecutionMonitoringReportedEvent
            {
                WorkerID = workerID,
                PipID = data.PipId.Value
            };

            processExecutionMonitoringReportedEvent.ReportedProcesses.AddRange(
                data.ReportedProcesses.Select(rp => rp.ToReportedProcess()));
            processExecutionMonitoringReportedEvent.ReportedFileAccesses.AddRange(
                data.ReportedFileAccesses.Select(reportedFileAccess => reportedFileAccess.ToReportedFileAccess(pathTable)));
            processExecutionMonitoringReportedEvent.WhitelistedReportedFileAccesses.AddRange(
                data.WhitelistedReportedFileAccesses.Select(
                    whiteListReportedFileAccess => whiteListReportedFileAccess.ToReportedFileAccess(pathTable)));

            foreach (var processDetouringStatus in data.ProcessDetouringStatuses)
            {
                processExecutionMonitoringReportedEvent.ProcessDetouringStatuses.Add(new ProcessDetouringStatusData()
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

        /// <nodoc />
        public static ProcessFingerprintComputationEvent ToProcessFingerprintComputationEvent(this ProcessFingerprintComputationEventData data, uint workerID, PathTable pathTable)
        {
            var processFingerprintComputationEvent = new ProcessFingerprintComputationEvent
            {
                WorkerID = workerID,
                Kind = (Xldb.FingerprintComputationKind)data.Kind,
                PipID = data.PipId.Value,
                WeakFingerprint = new WeakContentFingerPrint()
                {
                    Hash = data.WeakFingerprint.Hash.ToFingerprint()
                },
            };

            foreach (var strongFingerprintComputation in data.StrongFingerprintComputations)
            {
                var processStrongFingerprintComputationData = new Xldb.ProcessStrongFingerprintComputationData()
                {
                    PathSet = strongFingerprintComputation.PathSet.ToObservedPathSet(pathTable),
                    PathSetHash = new ContentHash()
                    {
                        Value = strongFingerprintComputation.PathSetHash.ToString()
                    },
                    UnsafeOptions = strongFingerprintComputation.UnsafeOptions.ToUnsafeOptions(),
                    Succeeded = strongFingerprintComputation.Succeeded,
                    IsStrongFingerprintHit = strongFingerprintComputation.IsStrongFingerprintHit,
                    ComputedStrongFingerprint = new StrongContentFingerPrint()
                    {
                        Hash = strongFingerprintComputation.ComputedStrongFingerprint.Hash.ToFingerprint()
                    }
                };

                processStrongFingerprintComputationData.PathEntries.AddRange(
                    strongFingerprintComputation.PathEntries.Select(
                        pathEntry => pathEntry.ToObservedPathEntry(pathTable)));
                processStrongFingerprintComputationData.ObservedAccessedFileNames.AddRange(
                    strongFingerprintComputation.ObservedAccessedFileNames.Select(
                        observedAccessedFileName => observedAccessedFileName.ToXldbString(pathTable)));
                processStrongFingerprintComputationData.PriorStrongFingerprints.AddRange(
                    strongFingerprintComputation.PriorStrongFingerprints.Select(
                        priorStrongFingerprint => new StrongContentFingerPrint() { Hash = priorStrongFingerprint.Hash.ToFingerprint() }));

                foreach (var observedInput in strongFingerprintComputation.ObservedInputs)
                {
                    processStrongFingerprintComputationData.ObservedInputs.Add(new ObservedInput()
                    {
                        Type = (ObservedInputType)observedInput.Type,
                        Hash = new ContentHash()
                        {
                            Value = observedInput.Hash.ToString()
                        },
                        PathEntry = observedInput.PathEntry.ToObservedPathEntry(pathTable),
                        Path = observedInput.Path.ToAbsolutePath(pathTable),
                        IsSearchPath = observedInput.IsSearchPath,
                        IsDirectoryPath = observedInput.IsDirectoryPath,
                        DirectoryEnumeration = observedInput.DirectoryEnumeration
                    });
                }

                processFingerprintComputationEvent.StrongFingerprintComputations.Add(processStrongFingerprintComputationData);
            }

            return processFingerprintComputationEvent;
        }

        /// <nodoc />
        public static ExtraEventDataReported ToExtraEventDataReported(this ExtraEventData data, uint workerID)
        {
            return new ExtraEventDataReported
            {
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
                SearchPathToolsHash = new ContentHash() { Value = data.SearchPathToolsHash.ToString() },
                UnexpectedFileAccessesAreErrors = data.UnexpectedFileAccessesAreErrors,
                MonitorFileAccesses = data.MonitorFileAccesses,
                MaskUntrackedAccesses = data.MaskUntrackedAccesses,
                NormalizeReadTimestamps = data.NormalizeReadTimestamps,
                PipWarningsPromotedToErrors = data.PipWarningsPromotedToErrors,
                ValidateDistribution = data.ValidateDistribution,
                RequiredKextVersionNumber = data.RequiredKextVersionNumber
            };
        }

        /// <nodoc />
        public static DependencyViolationReportedEvent ToDependencyViolationReportedEvent(this DependencyViolationEventData data, uint workerID, PathTable pathTable)
        {
            return new DependencyViolationReportedEvent()
            {
                WorkerID = workerID,
                ViolatorPipID = data.ViolatorPipId.Value,
                RelatedPipID = data.RelatedPipId.Value,
                ViolationType = (FileMonitoringViolationAnalyzer_DependencyViolationType)data.ViolationType,
                AccessLevel = (FileMonitoringViolationAnalyzer_AccessLevel)data.AccessLevel,
                Path = data.Path.ToAbsolutePath(pathTable)
            };
        }

        /// <nodoc />
        public static PipExecutionStepPerformanceReportedEvent ToPipExecutionStepPerformanceReportedEvent(this PipExecutionStepPerformanceEventData data, uint workerID)
        {
            var pipExecStepPerformanceEvent = new PipExecutionStepPerformanceReportedEvent
            {
                WorkerID = workerID,
                PipID = data.PipId.Value,
                StartTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.StartTime),
                Duration = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(data.Duration),
                Step = (PipExecutionStep)data.Step,
                Dispatcher = (WorkDispatcher_DispatcherKind)data.Dispatcher
            };

            return pipExecStepPerformanceEvent;
        }

        /// <nodoc />
        public static PipCacheMissEvent ToPipCacheMissEvent(this PipCacheMissEventData data, uint workerID)
        {
            return new PipCacheMissEvent()
            {
                WorkerID = workerID,
                PipID = data.PipId.Value,
                CacheMissType = (PipCacheMissType)data.CacheMissType
            };
        }

        /// <nodoc />
        public static StatusReportedEvent ToResourceUsageReportedEvent(this StatusEventData data, uint workerID)
        {
            var statusReportedEvent = new StatusReportedEvent()
            {
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
                LimitingResource = (ExecutionSampler_LimitingResource)data.LimitingResource,
                UnresponsivenessFactor = data.UnresponsivenessFactor,
                ProcessPipsPending = data.ProcessPipsPending,
                ProcessPipsAllocatedSlots = data.ProcessPipsAllocatedSlots
            };

            statusReportedEvent.DiskPercents.AddRange(data.DiskPercents.Select(percent => percent));
            statusReportedEvent.DiskQueueDepths.AddRange(data.DiskQueueDepths.Select(depth => depth));
            statusReportedEvent.PipsSucceededAllTypes.AddRange(data.PipsSucceededAllTypes.Select(type => type));

            return statusReportedEvent;
        }

        /// <nodoc />
        public static BXLInvocationEvent ToBXLInvocationEvent(this DominoInvocationEventData data, uint workerID, PathTable pathTable)
        {
            var loggingConfig = data.Configuration.Logging;

            var bxlInvEvent = new BXLInvocationEvent
            {
                WorkerID = workerID,
                SubstSource = loggingConfig.SubstSource.ToAbsolutePath(pathTable),
                SubstTarget = loggingConfig.SubstTarget.ToAbsolutePath(pathTable),
                IsSubstSourceValid = loggingConfig.SubstSource.IsValid,
                IsSubstTargetValid = loggingConfig.SubstTarget.IsValid
            };

            return bxlInvEvent;
        }

        /// <nodoc />
        public static PipExecutionDirectoryOutputsEvent ToPipExecutionDirectoryOutputsEvent(this PipExecutionDirectoryOutputs data, uint workerID, PathTable pathTable)
        {
            var pipExecDirectoryOutputEvent = new PipExecutionDirectoryOutputsEvent
            {
                WorkerID = workerID
            };

            foreach (var (directoryArtifact, fileArtifactArray) in data.DirectoryOutputs)
            {
                var directoryOutput = new DirectoryOutput()
                {
                    DirectoryArtifact = directoryArtifact.ToDirectoryArtifact(pathTable)
                };

                directoryOutput.FileArtifactArray.AddRange(
                    fileArtifactArray.Select(
                        file => file.ToFileArtifact(pathTable)));
                pipExecDirectoryOutputEvent.DirectoryOutput.Add(directoryOutput);
            }

            return pipExecDirectoryOutputEvent;
        }

        /// <nodoc />
        public static Xldb.ReportedFileAccess ToReportedFileAccess(this ReportedFileAccess reportedFileAccess, PathTable pathTable)
        {
            return new Xldb.ReportedFileAccess()
            {
                CreationDisposition = (CreationDisposition)reportedFileAccess.CreationDisposition,
                DesiredAccess = (DesiredAccess)reportedFileAccess.DesiredAccess,
                Error = reportedFileAccess.Error,
                Usn = reportedFileAccess.Usn.Value,
                FlagsAndAttributes = (FlagsAndAttributes)reportedFileAccess.FlagsAndAttributes,
                Path = reportedFileAccess.Path,
                ManifestPath = reportedFileAccess.ManifestPath.ToString(pathTable, PathFormat.HostOs),
                Process = reportedFileAccess.Process.ToReportedProcess(),
                ShareMode = (ShareMode)reportedFileAccess.ShareMode,
                Status = (FileAccessStatus)reportedFileAccess.Status,
                Method = (FileAccessStatusMethod)reportedFileAccess.Method,
                RequestedAccess = (RequestedAccess)reportedFileAccess.RequestedAccess,
                Operation = (ReportedFileOperation)reportedFileAccess.Operation,
                ExplicitlyReported = reportedFileAccess.ExplicitlyReported,
                EnumeratePattern = reportedFileAccess.EnumeratePattern
            };
        }

        /// <nodoc />
        public static Xldb.ObservedPathSet ToObservedPathSet(this ObservedPathSet pathSet, PathTable pathTable)
        {
            var observedPathSet = new Xldb.ObservedPathSet();
            observedPathSet.Paths.AddRange(pathSet.Paths.Select(pathEntry => pathEntry.ToObservedPathEntry(pathTable)));
            observedPathSet.ObservedAccessedFileNames.AddRange(
                pathSet.ObservedAccessedFileNames.Select(
                    observedAccessedFileName => observedAccessedFileName.ToXldbString(pathTable)));
            observedPathSet.UnsafeOptions = pathSet.UnsafeOptions.ToUnsafeOptions();

            return observedPathSet;
        }

        /// <nodoc />
        public static Xldb.UnsafeOptions ToUnsafeOptions(this UnsafeOptions unsafeOption)
        {
            var unsafeOpt = new Xldb.UnsafeOptions()
            {
                PreserveOutputsSalt = new ContentHash()
                {
                    Value = unsafeOption.PreserveOutputsSalt.ToString()
                },
                UnsafeConfiguration = new UnsafeSandboxConfiguration()
                {
                    PreserveOutputs = (PreserveOutputsMode)unsafeOption.UnsafeConfiguration.PreserveOutputs,
                    MonitorFileAccesses = unsafeOption.UnsafeConfiguration.MonitorFileAccesses,
                    IgnoreZwRenameFileInformation = unsafeOption.UnsafeConfiguration.IgnoreZwRenameFileInformation,
                    IgnoreZwOtherFileInformation = unsafeOption.UnsafeConfiguration.IgnoreZwOtherFileInformation,
                    IgnoreNonCreateFileReparsePoints = unsafeOption.UnsafeConfiguration.IgnoreNonCreateFileReparsePoints,
                    IgnoreSetFileInformationByHandle = unsafeOption.UnsafeConfiguration.IgnoreSetFileInformationByHandle,
                    IgnoreReparsePoints = unsafeOption.UnsafeConfiguration.IgnoreReparsePoints,
                    IgnorePreloadedDlls = unsafeOption.UnsafeConfiguration.IgnorePreloadedDlls,
                    ExistingDirectoryProbesAsEnumerations = unsafeOption.UnsafeConfiguration.ExistingDirectoryProbesAsEnumerations,
                    MonitorNtCreateFile = unsafeOption.UnsafeConfiguration.MonitorNtCreateFile,
                    MonitorZwCreateOpenQueryFile = unsafeOption.UnsafeConfiguration.MonitorZwCreateOpenQueryFile,
                    SandboxKind = (SandboxKind)unsafeOption.UnsafeConfiguration.SandboxKind,
                    UnexpectedFileAccessesAreErrors = unsafeOption.UnsafeConfiguration.UnexpectedFileAccessesAreErrors,
                    IgnoreGetFinalPathNameByHandle = unsafeOption.UnsafeConfiguration.IgnoreGetFinalPathNameByHandle,
                    IgnoreDynamicWritesOnAbsentProbes = unsafeOption.UnsafeConfiguration.IgnoreDynamicWritesOnAbsentProbes,
                    IgnoreUndeclaredAccessesUnderSharedOpaques = unsafeOption.UnsafeConfiguration.IgnoreUndeclaredAccessesUnderSharedOpaques,
                }
            };

            if (unsafeOption.UnsafeConfiguration.DoubleWritePolicy != null)
            {
                unsafeOpt.UnsafeConfiguration.DoubleWritePolicy = (DoubleWritePolicy)unsafeOption.UnsafeConfiguration.DoubleWritePolicy;
            }

            return unsafeOpt;
        }

        public static string ToXldbString(this StringId stringId, PathTable pathTable)
        {
            return stringId.IsValid ? stringId.ToString(pathTable.StringTable) : "";
        }

        /// <nodoc />
        public static Xldb.AbsolutePath ToAbsolutePath(this AbsolutePath path, PathTable pathTable)
        {
            return new Xldb.AbsolutePath()
            {
                Value = path.ToString(pathTable, PathFormat.HostOs)
            };
        }

        /// <nodoc />
        public static Xldb.FileArtifact ToFileArtifact(this FileArtifact fileArtifact, PathTable pathTable)
        {
            return new Xldb.FileArtifact
            {
                Path = fileArtifact.Path.ToAbsolutePath(pathTable),
                RewriteCount = fileArtifact.RewriteCount,
                IsValid = fileArtifact.IsValid
            };
        }

        /// <nodoc />
        public static Xldb.ReportedProcess ToReportedProcess(this ReportedProcess reportedProcess)
        {
            return new Xldb.ReportedProcess()
            {
                Path = reportedProcess.Path,
                ProcessId = reportedProcess.ProcessId,
                ProcessArgs = reportedProcess.ProcessArgs,
                ReadCounters = new IOTypeCounters
                {
                    OperationCount = reportedProcess.IOCounters.ReadCounters.OperationCount,
                    TransferCOunt = reportedProcess.IOCounters.ReadCounters.TransferCount
                },
                WriteCounters = new IOTypeCounters
                {
                    OperationCount = reportedProcess.IOCounters.WriteCounters.OperationCount,
                    TransferCOunt = reportedProcess.IOCounters.WriteCounters.TransferCount
                },
                OtherCounters = new IOTypeCounters
                {
                    OperationCount = reportedProcess.IOCounters.OtherCounters.OperationCount,
                    TransferCOunt = reportedProcess.IOCounters.OtherCounters.TransferCount
                },
                CreationTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(reportedProcess.CreationTime),
                ExitTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(reportedProcess.ExitTime),
                KernelTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(reportedProcess.KernelTime),
                UserTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(reportedProcess.UserTime),
                ExitCode = reportedProcess.ExitCode,
                ParentProcessId = reportedProcess.ParentProcessId
            };
        }

        /// <nodoc />
        public static Xldb.ObservedPathEntry ToObservedPathEntry(this ObservedPathEntry pathEntry, PathTable pathTable)
        {
            return new Xldb.ObservedPathEntry()
            {
                Path = pathEntry.Path.ToAbsolutePath(pathTable),
                EnumeratePatternRegex = pathEntry.EnumeratePatternRegex ?? ""
            };
        }

        /// <nodoc />
        public static Xldb.Fingerprint ToFingerprint(this Fingerprint fingerprint)
        {
            return new Xldb.Fingerprint()
            {
                Length = fingerprint.Length,
                Bytes = Google.Protobuf.ByteString.CopyFrom(fingerprint.ToByteArray())
            };
        }

        /// <nodoc />
        public static Xldb.DirectoryArtifact ToDirectoryArtifact(this DirectoryArtifact artifact, PathTable pathTable)
        {
            return new Xldb.DirectoryArtifact()
            {
                IsValid = artifact.IsValid,
                Path = artifact.Path.ToAbsolutePath(pathTable),
                PartialSealID = artifact.PartialSealId,
                IsSharedOpaque = artifact.IsSharedOpaque
            };
        }

        /// <nodoc />
        public static Xldb.PipTable ToPipTable(this PipTable pipTable)
        {
            var xldbPipTable = new Xldb.PipTable
            {
                IsDisposed = pipTable.IsDisposed,
                Reads = pipTable.Reads,
                Writes = pipTable.Writes,
                Count = pipTable.Count,
                PageStreamsCount = pipTable.PageStreamsCount,
                Size = pipTable.Size,
                WritesMilliseconds = pipTable.WritesMilliseconds,
                ReadsMilliseconds = pipTable.ReadsMilliseconds,
                Used = pipTable.Used,
                Alive = pipTable.Alive
            };

            xldbPipTable.StableKeys.AddRange(pipTable.StableKeys.Select(stableKey => stableKey.Value));
            xldbPipTable.Keys.AddRange(pipTable.Keys.Select(key => key.Value));
            xldbPipTable.DeserializationContexts.AddRange(pipTable.DeserializationContexts.Select(
                context => new PipDeserializationContext() { Key = (PipQueryContext)context.Key, Value = context.Value }));
            return xldbPipTable;
        }

        /// <nodoc />
        public static Xldb.PipData ToPipData(this PipData pipData, PathTable pathTable)
        {
            return !pipData.IsValid ? null : new Xldb.PipData
            {
                IsValid = pipData.IsValid,
                FragmentSeparator = pipData.FragmentSeparator.ToXldbString(pathTable),
                FragmentCount = pipData.FragmentCount,
                FragmentEscaping = (PipDataFragmentEscaping)pipData.FragmentEscaping
            };

        }

        /// <nodoc />
        public static Xldb.PipProvenance ToPipProvenance(this PipProvenance provenance, PathTable pathTable)
        {
            return provenance == null ? null : new Xldb.PipProvenance()
            {
                Usage = provenance.Usage.ToPipData(pathTable),
                ModuleId = provenance.ModuleId.Value.ToXldbString(pathTable),
                ModuleName = provenance.ModuleName.ToXldbString(pathTable),
                SemiStableHash = provenance.SemiStableHash
            };
        }

        public static Xldb.FileOrDirectoryArtifact ToFileOrDirectoryArtifact(this FileOrDirectoryArtifact artifact, PathTable pathTable)
        {
            var xldbFileOrDirectoryArtifact = new Xldb.FileOrDirectoryArtifact()
            {
                IsValid = artifact.IsValid
            };

            if (artifact.IsDirectory)
            {
                xldbFileOrDirectoryArtifact.IsDirectory = true;
                xldbFileOrDirectoryArtifact.DirectoryArtifact = artifact.DirectoryArtifact.ToDirectoryArtifact(pathTable);
            }
            else
            {
                xldbFileOrDirectoryArtifact.IsFile = true;
                xldbFileOrDirectoryArtifact.FileArtifact = artifact.FileArtifact.ToFileArtifact(pathTable);
            }

            return xldbFileOrDirectoryArtifact;
        }

        /// <nodoc />
        public static Xldb.Pip ToPip(this Pip pip, PathTable pathTable, DirectedGraph directedGraph)
        {
            var xldbPip = new Xldb.Pip()
            {
                SemiStableHash = pip.SemiStableHash,
                FormattedSemiStableHash = pip.FormattedSemiStableHash,
                ProcessAllowsUndeclaredSourceReads = pip.ProcessAllowsUndeclaredSourceReads,
                PipId = pip.PipId.Value,
                PipType = (PipType)pip.PipType,
                Provenance = pip.Provenance.ToPipProvenance(pathTable),
            };

            if (pip.Tags.IsValid)
            {
                xldbPip.Tags.AddRange(pip.Tags.Select(key => key.ToXldbString(pathTable)));
            }

            xldbPip.IncomingEdges.AddRange(directedGraph.GetIncomingEdges(pip.PipId.ToNodeId()).Select(edge => edge.ToEdge()));
            xldbPip.OutgoingEdges.AddRange(directedGraph.GetOutgoingEdges(pip.PipId.ToNodeId()).Select(edge => edge.ToEdge()));
            return xldbPip;
        }

        /// <nodoc />
        public static Xldb.ModulePip ToModulePip(this ModulePip pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbModulePip = new Xldb.ModulePip
            {
                ParentPipInfo = parentPip,
                Module = pip.Module.Value.ToXldbString(pathTable),
                Identity = pip.Identity.ToXldbString(pathTable),
                ResolverKind = pip.ResolverKind.ToXldbString(pathTable),
                ResolverName = pip.ResolverName.ToXldbString(pathTable),
                Version = pip.Version.ToXldbString(pathTable),
                Location = new Xldb.LocationData()
                {
                    IsValid = pip.Location.IsValid,
                    Line = pip.Location.Line,
                    Path = pip.Location.Path.ToAbsolutePath(pathTable),
                    Position = pip.Location.Position
                },
                Provenance = pip.Provenance.ToPipProvenance(pathTable),
                PipType = (PipType)pip.PipType
            };

            if (pip.Tags.IsValid)
            {
                xldbModulePip.Tags.AddRange(pip.Tags.Select(key => key.ToXldbString(pathTable)));
            }

            return xldbModulePip;
        }

        /// <nodoc />
        public static Xldb.SealDirectory ToSealDirectory(this SealDirectory pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbSealDirectory = new Xldb.SealDirectory
            {
                ParentPipInfo = parentPip,
                Kind = (SealDirectoryKind)pip.Kind,
                DirectoryRoot = pip.DirectoryRoot.ToAbsolutePath(pathTable),
                IsComposite = pip.IsComposite,
                Scrub = pip.Scrub,
                IsInitialzed = pip.IsInitialized,
                Directory = pip.Directory.ToDirectoryArtifact(pathTable),
                IsSealSourceDirectory = pip.IsSealSourceDirectory,
                Provenance = pip.Provenance.ToPipProvenance(pathTable),
                PipType = (PipType)pip.PipType

            };

            xldbSealDirectory.Patterns.AddRange(pip.Patterns.Select(key => key.ToXldbString(pathTable)));
            xldbSealDirectory.Contents.AddRange(pip.Contents.Select(file => file.ToFileArtifact(pathTable)));
            xldbSealDirectory.ComposedDirectories.AddRange(pip.ComposedDirectories.Select(dir => dir.ToDirectoryArtifact(pathTable)));

            if (pip.Tags.IsValid)
            {
                xldbSealDirectory.Tags.AddRange(pip.Tags.Select(key => key.ToXldbString(pathTable)));
            }

            return xldbSealDirectory;
        }

        /// <nodoc />
        public static Xldb.CopyFile ToCopyFile(this CopyFile pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbCopyFile = new Xldb.CopyFile
            {
                ParentPipInfo = parentPip,
                Source = pip.Source.ToFileArtifact(pathTable),
                Destination = pip.Destination.ToFileArtifact(pathTable),
                OutputsMustRemainWritable = pip.OutputsMustRemainWritable,
                Provenance = pip.Provenance.ToPipProvenance(pathTable),
                PipType = (PipType)pip.PipType
            };

            if (pip.Tags.IsValid)
            {
                xldbCopyFile.Tags.AddRange(pip.Tags.Select(key => key.ToXldbString(pathTable)));
            }

            return xldbCopyFile;
        }

        /// <nodoc />
        public static Xldb.WriteFile ToWriteFile(this WriteFile pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbWriteFile = new Xldb.WriteFile
            {
                ParentPipInfo = parentPip,
                Destination = pip.Destination.ToFileArtifact(pathTable),
                Contents = pip.Contents.ToPipData(pathTable),
                Encoding = (WriteFileEncoding)pip.Encoding,
                Provenance = pip.Provenance.ToPipProvenance(pathTable),
                PipType = (PipType)pip.PipType
            };

            if (pip.Tags.IsValid)
            {
                xldbWriteFile.Tags.AddRange(pip.Tags.Select(key => key.ToXldbString(pathTable)));
            }

            return xldbWriteFile;
        }

        /// <nodoc />
        public static ProcessPip ToProcessPip(this Process pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbProcessPip = new ProcessPip
            {
                ParentPipInfo = parentPip,
                ProcessOptions = (Options)pip.ProcessOptions,
                ProcessAbsentPathProbeInUndeclaredOpaquesMode = (AbsentPathProbeInUndeclaredOpaquesMode)pip.ProcessAbsentPathProbeInUndeclaredOpaquesMode,
                StandardInputFile = pip.StandardInputFile.ToFileArtifact(pathTable),
                StandardInputData = pip.StandardInputData.ToPipData(pathTable),
                StandardInput = new StandardInput()
                {
                    File = pip.StandardInput.File.ToFileArtifact(pathTable),
                    Data = pip.StandardInput.Data.ToPipData(pathTable),
                    IsValid = pip.StandardInput.IsValid
                },
                StandardOutput = pip.StandardOutput.ToFileArtifact(pathTable),
                StandardError = pip.StandardError.ToFileArtifact(pathTable),
                StandardDirectory = pip.StandardDirectory.ToAbsolutePath(pathTable),
                UniqueOutputDirectory = pip.UniqueOutputDirectory.ToAbsolutePath(pathTable),
                UniqueRedirectedDirectoryRoot = pip.UniqueRedirectedDirectoryRoot.ToAbsolutePath(pathTable),
                ResponseFile = pip.ResponseFile.ToFileArtifact(pathTable),
                ResponseFileData = pip.ResponseFileData.ToPipData(pathTable),
                Executable = pip.Executable.ToFileArtifact(pathTable),
                ToolDescription = pip.ToolDescription.ToXldbString(pathTable),
                WorkingDirectory = pip.WorkingDirectory.ToAbsolutePath(pathTable),
                Arguments = pip.Arguments.ToPipData(pathTable),
                WarningRegex = new RegexDescriptor()
                {
                    Pattern = pip.WarningRegex.Pattern.ToXldbString(pathTable),
                    Options = (RegexOptions)pip.WarningRegex.Options,
                    IsValid = pip.WarningRegex.IsValid
                },
                ErrorRegex = new RegexDescriptor()
                {
                    Pattern = pip.ErrorRegex.Pattern.ToXldbString(pathTable),
                    Options = (RegexOptions)pip.ErrorRegex.Options,
                    IsValid = pip.ErrorRegex.IsValid
                },
                TempDirectory = pip.TempDirectory.ToAbsolutePath(pathTable),
                Weight = pip.Weight,
                Priority = pip.Priority,
                TestRetries = pip.TestRetries,
                IsStartOrShutdownKind = pip.IsStartOrShutdownKind,
                Provenance = pip.Provenance.ToPipProvenance(pathTable),
                PipType = (PipType)pip.PipType,
                HasUntrackedChildProcesses = pip.HasUntrackedChildProcesses,
                ProducesPathIndependentOutputs = pip.ProducesPathIndependentOutputs,
                OutputsMustRemainWritable = pip.OutputsMustRemainWritable,
                RequiresAdmin = pip.RequiresAdmin,
                AllowPreserveOutputs = pip.AllowPreserveOutputs,
                IsLight = pip.IsLight,
                IsService = pip.IsService,
                AllowUndeclaredSourceReads = pip.AllowUndeclaredSourceReads,
                NeedsToRunInContainer = pip.NeedsToRunInContainer,
                ShutdownProcessPipId = pip.ShutdownProcessPipId.Value,
                DisableCacheLookup = pip.DisableCacheLookup,
                DoubleWritePolicy = (DoubleWritePolicy)pip.DoubleWritePolicy,
                ContainerIsolationLevel = (ContainerIsolationLevel)pip.ContainerIsolationLevel
            };

            if (pip.WarningTimeout != null)
            {
                xldbProcessPip.WarningTimeout = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan((TimeSpan)pip.WarningTimeout);
            }

            if (pip.Timeout != null)
            {
                xldbProcessPip.Timeout = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan((TimeSpan)pip.Timeout);
            }

            if (pip.NestedProcessTerminationTimeout != null)
            {
                xldbProcessPip.NestedProcessTerminationTimeout = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan((TimeSpan)pip.NestedProcessTerminationTimeout);
            }

            var serviceInfo = new ServiceInfo
            {
                Kind = (ServicePipKind)pip.ServiceInfo.Kind,
                ShutdownPipId = pip.ServiceInfo.ShutdownPipId.Value,
                IsValid = pip.ServiceInfo.IsValid,
                IsStartOrShutdownKind = pip.ServiceInfo.IsStartOrShutdownKind
            };

            serviceInfo.ServicePipDependencies.AddRange(pip.ServiceInfo.ServicePipDependencies.Select(key => key.Value));
            serviceInfo.FinalizationPipIds.AddRange(pip.ServiceInfo.FinalizationPipIds.Select(key => key.Value));

            xldbProcessPip.ServiceInfo = serviceInfo;
            xldbProcessPip.EnvironmentVariable.AddRange(pip.EnvironmentVariables.Select(
                envVar => new EnvironmentVariable() { Name = envVar.Name.ToXldbString(pathTable), Value = envVar.Value.ToPipData(pathTable), IsPassThrough = envVar.IsPassThrough }));
            xldbProcessPip.Dependencies.AddRange(pip.Dependencies.Select(file => file.ToFileArtifact(pathTable)));
            xldbProcessPip.DirectoryDependencies.AddRange(pip.DirectoryDependencies.Select(dir => dir.ToDirectoryArtifact(pathTable)));
            xldbProcessPip.OrderDependencies.AddRange(pip.OrderDependencies.Select(order => order.Value));
            xldbProcessPip.UntrackedPaths.AddRange(pip.UntrackedPaths.Select(path => path.ToAbsolutePath(pathTable)));
            xldbProcessPip.UntrackedScopes.AddRange(pip.UntrackedScopes.Select(path => path.ToAbsolutePath(pathTable)));
            xldbProcessPip.SuccessExitCodes.AddRange(pip.SuccessExitCodes.Select(code => code));
            xldbProcessPip.RetryExitCodes.AddRange(pip.RetryExitCodes.Select(code => code));
            xldbProcessPip.FileOutputs.AddRange(pip.FileOutputs.Select(
                output => new Xldb.FileArtifactWithAttributes()
                { IsValid = output.IsValid, Path = output.Path.ToAbsolutePath(pathTable), RewriteCount = output.RewriteCount, FileExistence = (Xldb.FileExistence)output.FileExistence }));
            xldbProcessPip.DirectoryOutputs.AddRange(pip.DirectoryOutputs.Select(dir => dir.ToDirectoryArtifact(pathTable)));
            xldbProcessPip.Semaphores.AddRange(pip.Semaphores.Select(
                semaphore => new ProcessSemaphoreInfo() { Name = semaphore.Name.ToXldbString(pathTable), Value = semaphore.Value, Limit = semaphore.Limit, IsValid = semaphore.IsValid }));
            xldbProcessPip.AdditionalTempDirectories.AddRange(pip.AdditionalTempDirectories.Select(dir => dir.ToAbsolutePath(pathTable)));
            xldbProcessPip.PreserveOutputWhitelist.AddRange(pip.PreserveOutputWhitelist.Select(path => path.ToAbsolutePath(pathTable)));
            xldbProcessPip.ServicePipDependencies.AddRange(pip.ServicePipDependencies.Select(dep => dep.Value));

            if (pip.Tags.IsValid)
            {
                xldbProcessPip.Tags.AddRange(pip.Tags.Select(key => key.ToXldbString(pathTable)));
            }

            return xldbProcessPip;
        }

        /// <nodoc />
        public static Xldb.HashSourceFile ToHashSourceFile(this HashSourceFile pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbHashSourceFile = new Xldb.HashSourceFile()
            {
                ParentPipInfo = parentPip,
                Artifact = pip.Artifact.ToFileArtifact(pathTable),
                Provenance = pip.Provenance.ToPipProvenance(pathTable),
                PipType = (PipType)pip.PipType
            };

            if (pip.Tags.IsValid)
            {
                xldbHashSourceFile.Tags.AddRange(pip.Tags.Select(key => key.ToXldbString(pathTable)));
            }

            return xldbHashSourceFile;
        }

        /// <nodoc />
        public static Xldb.SpecFilePip ToSpecFilePip(this SpecFilePip pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbSpecFilePip = new Xldb.SpecFilePip()
            {
                ParentPipInfo = parentPip,
                SpecFile = pip.SpecFile.ToFileArtifact(pathTable),
                DefinitionLocation = new Xldb.LocationData()
                {
                    IsValid = pip.DefinitionLocation.IsValid,
                    Line = pip.DefinitionLocation.Line,
                    Path = pip.DefinitionLocation.Path.ToAbsolutePath(pathTable),
                    Position = pip.DefinitionLocation.Position
                },
                OwningModule = pip.OwningModule.Value.ToXldbString(pathTable),
                Provenance = pip.Provenance.ToPipProvenance(pathTable),
                PipType = (PipType)pip.PipType
            };

            if (pip.Tags.IsValid)
            {
                xldbSpecFilePip.Tags.AddRange(pip.Tags.Select(key => key.ToXldbString(pathTable)));
            }

            return xldbSpecFilePip;
        }

        /// <nodoc />
        public static Xldb.IpcPip ToIpcPip(this IpcPip pip, PathTable pathTable, Xldb.Pip parentPip)
        {
            var xldbIpcPip = new Xldb.IpcPip()
            {
                ParentPipInfo = parentPip,
                IpcInfo = new IpcClientInfo()
                {
                    IpcMonikerId = pip.IpcInfo.IpcMonikerId.ToXldbString(pathTable),
                    IpcClientConfig = new ClientConfig()
                    {
                        MaxConnectRetries = pip.IpcInfo.IpcClientConfig.MaxConnectRetries,
                        ConnectRetryDelay = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(pip.IpcInfo.IpcClientConfig.ConnectRetryDelay)
                    }
                },
                MessageBody = pip.MessageBody.ToPipData(pathTable),
                OutputFile = pip.OutputFile.ToFileArtifact(pathTable),
                IsServiceFinalization = pip.IsServiceFinalization,
                MustRunOnMaster = pip.MustRunOnMaster,
                Provenance = pip.Provenance.ToPipProvenance(pathTable),
                PipType = (PipType)pip.PipType
            };

            if (pip.Tags.IsValid)
            {
                xldbIpcPip.Tags.AddRange(pip.Tags.Select(key => key.ToXldbString(pathTable)));
            }

            xldbIpcPip.ServicePipDependencies.AddRange(pip.ServicePipDependencies.Select(pipId => pipId.Value));
            xldbIpcPip.FileDependencies.AddRange(pip.FileDependencies.Select(file => file.ToFileArtifact(pathTable)));
            xldbIpcPip.DirectoryDependencies.AddRange(pip.DirectoryDependencies.Select(directory => directory.ToDirectoryArtifact(pathTable)));
            xldbIpcPip.LazilyMaterializedDependencies.AddRange(pip.LazilyMaterializedDependencies.Select(dep => dep.ToFileOrDirectoryArtifact(pathTable)));

            return xldbIpcPip;
        }

        /// <nodoc />
        public static Xldb.NodeId ToNodeId(this NodeId nodeId)
        {
            return new Xldb.NodeId()
            {
                IsValid = nodeId.IsValid,
                Value = nodeId.Value
            };
        }

        public static Xldb.Edge ToEdge(this Edge edge)
        {
            return new Xldb.Edge()
            {
                OtherNode = edge.OtherNode.ToNodeId(),
                IsLight = edge.IsLight,
                Value = edge.Value
            };
        }

        public static Xldb.NodeRange ToNodeRange(this NodeRange nodeRange)
        {
            return new Xldb.NodeRange()
            {
                IsEmpty = nodeRange.IsEmpty,
                Size = nodeRange.Size,
                FromInclusive = nodeRange.FromInclusive.ToNodeId(),
                ToInclusive = nodeRange.ToInclusive.ToNodeId()
            };
        }

        /// <nodoc />
        public static Xldb.PipGraph ToPipGraph(this PipGraph pipGraph, PathTable pathTable)
        {
            var xldbPipGraph = new Xldb.PipGraph()
            {
                GraphId = pipGraph.GraphId.ToString(),
                SemistableFingerprint = new ContentFingerprint() { Hash = pipGraph.SemistableFingerprint.Hash.ToFingerprint() },
                NodeRange = pipGraph.NodeRange.ToNodeRange(),
                MaxAbsolutePathIndex = pipGraph.MaxAbsolutePathIndex,
                FileCount = pipGraph.FileCount,
                ContentCount = pipGraph.ContentCount,
                ArtifactContentCount = pipGraph.ArtifactContentCount,
                ApiServerMoniker = pipGraph.ApiServerMoniker.ToXldbString(pathTable)
            };

            xldbPipGraph.AllFilesAndProducers.AddRange(pipGraph.AllFilesAndProducers.Select(kvp => new FileArtifactMap()
            {
                Artifact = kvp.Key.ToFileArtifact(pathTable),
                Value = kvp.Value.Value
            }));
            xldbPipGraph.AllSealDirectoriesAndProducers.AddRange(pipGraph.AllSealDirectoriesAndProducers.Select(kvp => new DirectoryArtifactMap()
            {
                Artifact = kvp.Key.ToDirectoryArtifact(pathTable),
                Value = kvp.Value.Value
            }));
            xldbPipGraph.AllOutputDirectoriesAndProducers.AddRange(pipGraph.AllOutputDirectoriesAndProducers.Select(kvp => new DirectoryArtifactMap()
            {
                Artifact = kvp.Key.ToDirectoryArtifact(pathTable),
                Value = kvp.Value.Value
            }));

            foreach (var kvp in pipGraph.Modules)
            {
                xldbPipGraph.Modules.Add(kvp.Key.Value.ToXldbString(pathTable), new Xldb.NodeId() { IsValid = kvp.Value.IsValid, Value = kvp.Value.Value });
            }

            return xldbPipGraph;
        }
    }
}
