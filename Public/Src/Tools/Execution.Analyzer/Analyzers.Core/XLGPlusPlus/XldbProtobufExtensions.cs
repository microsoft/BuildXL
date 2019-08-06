// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Extension methods for Xldb ProtoBuf conversions.
    /// </summary>
    public static class XldbProtobufExtensions
    {
        /// <nodoc />
        public static Xldb.FileArtifactContentDecidedEvent ToFileArtifactContentDecidedEvent(this FileArtifactContentDecidedEventData data, uint workerID, PathTable pathTable)
        {
            var Uuid = Guid.NewGuid().ToString();

            return new Xldb.FileArtifactContentDecidedEvent()
            {
                UUID = Uuid,
                WorkerID = workerID,
                FileArtifact = data.FileArtifact.ToFileArtifact(pathTable),
                FileContentInfo = new Xldb.FileContentInfo
                {
                    LengthAndExistence = data.FileContentInfo.SerializedLengthAndExistence,
                    Hash = new Xldb.ContentHash() { Value = data.FileContentInfo.Hash.ToString() }
                },
                OutputOrigin = (Xldb.PipOutputOrigin)data.OutputOrigin
            };
        }

        /// <nodoc />
        public static Xldb.WorkerListEvent ToWorkerListEvent(this WorkerListEventData data, uint workerID)
        {
            var Uuid = Guid.NewGuid().ToString();

            var workerListEvent = new Xldb.WorkerListEvent
            {
                UUID = Uuid
            };

            workerListEvent.Workers.AddRange(data.Workers.Select(worker => worker));
            return workerListEvent;
        }

        /// <nodoc />
        public static Xldb.PipExecutionPerformanceEvent ToPipExecutionPerformanceEvent(this PipExecutionPerformanceEventData data)
        {
            var pipExecPerfEvent = new Xldb.PipExecutionPerformanceEvent();
            var Uuid = Guid.NewGuid().ToString();

            var pipExecPerformance = new Xldb.PipExecutionPerformance();
            pipExecPerformance.PipExecutionLevel = (int)data.ExecutionPerformance.ExecutionLevel;
            pipExecPerformance.ExecutionStart = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.ExecutionPerformance.ExecutionStart);
            pipExecPerformance.ExecutionStop = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.ExecutionPerformance.ExecutionStop);

            var processPipExecPerformance = new Xldb.ProcessPipExecutionPerformance();
            var performance = data.ExecutionPerformance as ProcessPipExecutionPerformance;
            if (performance != null)
            {
                processPipExecPerformance.ProcessExecutionTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(performance.ProcessExecutionTime);
                processPipExecPerformance.ReadCounters = new Xldb.IOTypeCounters()
                {
                    OperationCount = performance.IO.ReadCounters.OperationCount,
                    TransferCOunt = performance.IO.ReadCounters.TransferCount
                };

                processPipExecPerformance.WriteCounters = new Xldb.IOTypeCounters()
                {
                    OperationCount = performance.IO.WriteCounters.OperationCount,
                    TransferCOunt = performance.IO.WriteCounters.TransferCount
                };

                processPipExecPerformance.OtherCounters = new Xldb.IOTypeCounters()
                {
                    OperationCount = performance.IO.OtherCounters.OperationCount,
                    TransferCOunt = performance.IO.OtherCounters.TransferCount
                };

                processPipExecPerformance.UserTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(performance.UserTime);
                processPipExecPerformance.KernelTime = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(performance.KernelTime);
                processPipExecPerformance.PeakMemoryUsage = performance.PeakMemoryUsage;
                processPipExecPerformance.PeakMemoryUsageMb = performance.PeakMemoryUsageMb;
                processPipExecPerformance.NumberOfProcesses = performance.NumberOfProcesses;

                processPipExecPerformance.FileMonitoringViolationCounters = new Xldb.FileMonitoringViolationCounters()
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

            pipExecPerfEvent.UUID = Uuid;
            pipExecPerfEvent.WorkerID = data.ExecutionPerformance.WorkerId;
            pipExecPerfEvent.PipID = data.PipId.Value;
            pipExecPerfEvent.PipExecutionPerformance = pipExecPerformance;
            pipExecPerfEvent.ProcessPipExecutionPerformance = processPipExecPerformance;
            return pipExecPerfEvent;
        }

        /// <nodoc />
        public static Xldb.DirectoryMembershipHashedEvent ToDirectoryMembershipHashedEvent(this DirectoryMembershipHashedEventData data, uint workerID, PathTable pathTable)
        {
            var Uuid = Guid.NewGuid().ToString();

            var directoryMembershipEvent = new Xldb.DirectoryMembershipHashedEvent()
            {
                UUID = Uuid,
                WorkerID = workerID,
                DirectoryFingerprint = new Xldb.DirectoryFingerprint()
                {
                    Hash = new Xldb.ContentHash() { Value = data.DirectoryFingerprint.Hash.ToString() }
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
        public static Xldb.ProcessExecutionMonitoringReportedEvent ToProcessExecutionMonitoringReportedEvent(this ProcessExecutionMonitoringReportedEventData data, uint workerID, PathTable pathTable)
        {
            var Uuid = Guid.NewGuid().ToString();

            var processExecutionMonitoringReportedEvent = new Xldb.ProcessExecutionMonitoringReportedEvent
            {
                UUID = Uuid,
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
                processExecutionMonitoringReportedEvent.ProcessDetouringStatuses.Add(new Xldb.ProcessDetouringStatusData()
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
        public static Xldb.ProcessFingerprintComputationEvent ToProcessFingerprintComputationEvent(this ProcessFingerprintComputationEventData data, uint workerID, PathTable pathTable)
        {
            var Uuid = Guid.NewGuid().ToString();

            var processFingerprintComputationEvent = new Xldb.ProcessFingerprintComputationEvent
            {
                UUID = Uuid,
                WorkerID = workerID,
                Kind = (Xldb.FingerprintComputationKind)data.Kind,
                PipID = data.PipId.Value,
                WeakFingerprint = new Xldb.WeakContentFingerPrint()
                {
                    Hash = data.WeakFingerprint.Hash.ToFingerprint()
                },
            };

            foreach (var strongFingerprintComputation in data.StrongFingerprintComputations)
            {
                var processStrongFingerprintComputationData = new Xldb.ProcessStrongFingerprintComputationData()
                {
                    PathSet = strongFingerprintComputation.PathSet.ToObservedPathSet(pathTable),
                    PathSetHash = new Xldb.ContentHash()
                    {
                        Value = strongFingerprintComputation.PathSetHash.ToString()
                    },
                    UnsafeOptions = strongFingerprintComputation.UnsafeOptions.ToUnsafeOptions(),
                    Succeeded = strongFingerprintComputation.Succeeded,
                    IsStrongFingerprintHit = strongFingerprintComputation.IsStrongFingerprintHit,
                    ComputedStrongFingerprint = new Xldb.StrongContentFingerPrint()
                    {
                        Hash = strongFingerprintComputation.ComputedStrongFingerprint.Hash.ToFingerprint()
                    }
                };

                processStrongFingerprintComputationData.PathEntries.AddRange(
                    strongFingerprintComputation.PathEntries.Select(
                        pathEntry => pathEntry.ToObservedPathEntry(pathTable)));
                processStrongFingerprintComputationData.ObservedAccessedFileNames.AddRange(
                    strongFingerprintComputation.ObservedAccessedFileNames.Select(
                        observedAccessedFileName => new Xldb.StringId() { Value = observedAccessedFileName.Value }));
                processStrongFingerprintComputationData.PriorStrongFingerprints.AddRange(
                    strongFingerprintComputation.PriorStrongFingerprints.Select(
                        priorStrongFingerprint => new Xldb.StrongContentFingerPrint() { Hash = priorStrongFingerprint.Hash.ToFingerprint() }));

                foreach (var observedInput in strongFingerprintComputation.ObservedInputs)
                {
                    processStrongFingerprintComputationData.ObservedInputs.Add(new Xldb.ObservedInput()
                    {
                        Type = (Xldb.ObservedInputType)observedInput.Type,
                        Hash = new Xldb.ContentHash()
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
        public static Xldb.ExtraEventDataReported ToExtraEventDataReported(this ExtraEventData data, uint workerID)
        {
            var Uuid = Guid.NewGuid().ToString();

            return new Xldb.ExtraEventDataReported
            {
                UUID = Uuid,
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
                SearchPathToolsHash = new Xldb.ContentHash() { Value = data.SearchPathToolsHash.ToString() },
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
        public static Xldb.DependencyViolationReportedEvent ToDependencyViolationReportedEvent(this DependencyViolationEventData data, uint workerID, PathTable pathTable)
        {
            var Uuid = Guid.NewGuid().ToString();

            return new Xldb.DependencyViolationReportedEvent()
            {
                UUID = Uuid,
                WorkerID = workerID,
                ViolatorPipID = data.ViolatorPipId.Value,
                RelatedPipID = data.RelatedPipId.Value,
                ViolationType = (Xldb.FileMonitoringViolationAnalyzer_DependencyViolationType)data.ViolationType,
                AccessLevel = (Xldb.FileMonitoringViolationAnalyzer_AccessLevel)data.AccessLevel,
                Path = data.Path.ToAbsolutePath(pathTable)
            };
        }

        /// <nodoc />
        public static Xldb.PipExecutionStepPerformanceReportedEvent ToPipExecutionStepPerformanceReportedEvent(this PipExecutionStepPerformanceEventData data, uint workerID)
        {
            var Uuid = Guid.NewGuid().ToString();

            var pipExecStepPerformanceEvent = new Xldb.PipExecutionStepPerformanceReportedEvent
            {
                UUID = Uuid,
                WorkerID = workerID,
                PipID = data.PipId.Value,
                StartTime = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(data.StartTime),
                Duration = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(data.Duration),
                Step = (Xldb.PipExecutionStep)data.Step,
                Dispatcher = (Xldb.WorkDispatcher_DispatcherKind)data.Dispatcher
            };

            return pipExecStepPerformanceEvent;
        }

        /// <nodoc />
        public static Xldb.PipCacheMissEvent ToPipCacheMissEvent(this PipCacheMissEventData data, uint workerID)
        {
            var Uuid = Guid.NewGuid().ToString();
            return new Xldb.PipCacheMissEvent()
            {
                UUID = Uuid,
                WorkerID = workerID,
                PipID = data.PipId.Value,
                CacheMissType = (Xldb.PipCacheMissType)data.CacheMissType
            };
        }

        /// <nodoc />
        public static Xldb.StatusReportedEvent ToResourceUsageReportedEvent(this StatusEventData data, uint workerID)
        {
            var Uuid = Guid.NewGuid().ToString();

            var statusReportedEvent = new Xldb.StatusReportedEvent()
            {
                UUID = Uuid,
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
                LimitingResource = (Xldb.ExecutionSampler_LimitingResource)data.LimitingResource,
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
        public static Xldb.BXLInvocationEvent ToBXLInvocationEvent(this DominoInvocationEventData data, uint workerID, PathTable pathTable)
        {
            var loggingConfig = data.Configuration.Logging;
            var Uuid = Guid.NewGuid().ToString();

            var bxlInvEvent = new Xldb.BXLInvocationEvent
            {
                UUID = Uuid,
                WorkerID = workerID,
                SubstSource = loggingConfig.SubstSource.ToAbsolutePath(pathTable),
                SubstTarget = loggingConfig.SubstTarget.ToAbsolutePath(pathTable),
                IsSubstSourceValid = loggingConfig.SubstSource.IsValid,
                IsSubstTargetValid = loggingConfig.SubstTarget.IsValid
            };

            return bxlInvEvent;
        }

        /// <nodoc />
        public static Xldb.PipExecutionDirectoryOutputsEvent ToPipExecutionDirectoryOutputsEvent(this PipExecutionDirectoryOutputs data, uint workerID, PathTable pathTable)
        {
            var Uuid = Guid.NewGuid().ToString();
            var pipExecDirectoryOutputEvent = new Xldb.PipExecutionDirectoryOutputsEvent();
            pipExecDirectoryOutputEvent.UUID = Uuid;
            pipExecDirectoryOutputEvent.WorkerID = workerID;

            foreach (var (directoryArtifact, fileArtifactArray) in data.DirectoryOutputs)
            {
                var directoryOutput = new Xldb.DirectoryOutput()
                {
                    DirectoryArtifact = new Xldb.DirectoryArtifact()
                    {
                        IsValid = directoryArtifact.IsValid,
                        Path = directoryArtifact.Path.ToAbsolutePath(pathTable),
                        PartialSealID = directoryArtifact.PartialSealId,
                        IsSharedOpaque = directoryArtifact.IsSharedOpaque
                    }
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
                CreationDisposition = (Xldb.CreationDisposition)reportedFileAccess.CreationDisposition,
                DesiredAccess = (Xldb.DesiredAccess)reportedFileAccess.DesiredAccess,
                Error = reportedFileAccess.Error,
                Usn = reportedFileAccess.Usn.Value,
                FlagsAndAttributes = (Xldb.FlagsAndAttributes)reportedFileAccess.FlagsAndAttributes,
                Path = reportedFileAccess.Path,
                ManifestPath = reportedFileAccess.ManifestPath.ToString(pathTable, PathFormat.HostOs),
                Process = reportedFileAccess.Process.ToReportedProcess(),
                ShareMode = (Xldb.ShareMode)reportedFileAccess.ShareMode,
                Status = (Xldb.FileAccessStatus)reportedFileAccess.Status,
                Method = (Xldb.FileAccessStatusMethod)reportedFileAccess.Method,
                RequestedAccess = (Xldb.RequestedAccess)reportedFileAccess.RequestedAccess,
                Operation = (Xldb.ReportedFileOperation)reportedFileAccess.Operation,
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
                    observedAccessedFileName => new Xldb.StringId() { Value = observedAccessedFileName.Value }));
            observedPathSet.UnsafeOptions = pathSet.UnsafeOptions.ToUnsafeOptions();

            return observedPathSet;
        }

        /// <nodoc />
        public static Xldb.UnsafeOptions ToUnsafeOptions(this UnsafeOptions unsafeOption)
        {
            var unsafeOpt = new Xldb.UnsafeOptions()
            {
                PreserveOutputsSalt = new Xldb.ContentHash()
                {
                    Value = unsafeOption.PreserveOutputsSalt.ToString()
                },
                UnsafeConfiguration = new Xldb.UnsafeSandboxConfiguration()
                {
                    PreserveOutputs = (Xldb.PreserveOutputsMode)unsafeOption.UnsafeConfiguration.PreserveOutputs,
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
                    SandboxKind = (Xldb.SandboxKind)unsafeOption.UnsafeConfiguration.SandboxKind,
                    UnexpectedFileAccessesAreErrors = unsafeOption.UnsafeConfiguration.UnexpectedFileAccessesAreErrors,
                    IgnoreGetFinalPathNameByHandle = unsafeOption.UnsafeConfiguration.IgnoreGetFinalPathNameByHandle,
                    IgnoreDynamicWritesOnAbsentProbes = unsafeOption.UnsafeConfiguration.IgnoreDynamicWritesOnAbsentProbes,
                    IgnoreUndeclaredAccessesUnderSharedOpaques = unsafeOption.UnsafeConfiguration.IgnoreUndeclaredAccessesUnderSharedOpaques,
                }
            };

            if (unsafeOption.UnsafeConfiguration.DoubleWritePolicy != null)
            {
                unsafeOpt.UnsafeConfiguration.DoubleWritePolicy = (Xldb.DoubleWritePolicy)unsafeOption.UnsafeConfiguration.DoubleWritePolicy;
            }
            return unsafeOpt;
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
                ReadCounters = new Xldb.IOTypeCounters
                {
                    OperationCount = reportedProcess.IOCounters.ReadCounters.OperationCount,
                    TransferCOunt = reportedProcess.IOCounters.ReadCounters.TransferCount
                },
                WriteCounters = new Xldb.IOTypeCounters
                {
                    OperationCount = reportedProcess.IOCounters.WriteCounters.OperationCount,
                    TransferCOunt = reportedProcess.IOCounters.WriteCounters.TransferCount
                },
                OtherCounters = new Xldb.IOTypeCounters
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
    }
}
