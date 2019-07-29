// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Pips;
using BuildXL.Processes;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Extension methods for XLGpp ProtoBuf conversions.
    /// </summary>
    public static class XLGppProtobufExtensions
    {
        /// <nodoc />
        public static FileArtifactContentDecidedEvent_XLGpp ToFileArtifactContentDecidedEvent_XLGpp(this FileArtifactContentDecidedEventData data, uint workerID, PathTable pathTable)
        {
            var uuid = Guid.NewGuid().ToString();

            return new FileArtifactContentDecidedEvent_XLGpp()
            {
                UUID = uuid,
                WorkerID = workerID,
                FileArtifact = data.FileArtifact.ToFileArtifact_XLGpp(pathTable),
                FileContentInfo = new FileContentInfo_XLGpp
                {
                    LengthAndExistence = data.FileContentInfo.SerializedLengthAndExistence,
                    Hash = new ContentHash_XLGpp() { Value = data.FileContentInfo.Hash.ToString() }
                },
                OutputOrigin = (PipOutputOrigin_XLGpp)data.OutputOrigin
            };
        }

        /// <nodoc />
        public static WorkerListEvent_XLGpp ToWorkerListEvent_XLGpp(this WorkerListEventData data, uint workerID)
        {
            var uuid = Guid.NewGuid().ToString();

            var workerListEvent = new WorkerListEvent_XLGpp
            {
                UUID = uuid
            };

            foreach (var worker in data.Workers)
            {
                workerListEvent.Workers.Add(worker);
            }
            return workerListEvent;
        }

        /// <nodoc />
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

                processPipExecPerformance.Fingerprint = performance.Fingerprint.ToFingerprint_XLGpp();

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

        /// <nodoc />
        public static DirectoryMembershipHashedEvent_XLGpp ToDirectoryMembershipHashedEvent_XLGpp(this DirectoryMembershipHashedEventData data, uint workerID, PathTable pathTable)
        {
            var uuid = Guid.NewGuid().ToString();

            var directoryMembershipEvent = new DirectoryMembershipHashedEvent_XLGpp()
            {
                UUID = uuid,
                WorkerID = workerID,
                DirectoryFingerprint = new DirectoryFingerprint_XLGpp()
                {
                    Hash = new ContentHash_XLGpp() { Value = data.DirectoryFingerprint.Hash.ToString() }
                },
                Directory = data.Directory.ToAbsolutePath_XLGpp(pathTable),
                IsStatic = data.IsSearchPath,
                IsSearchPath = data.IsSearchPath,
                PipID = data.PipId.Value,
                EnumeratePatternRegex = data.EnumeratePatternRegex ?? ""
            };

            foreach (var member in data.Members)
            {
                directoryMembershipEvent.Members.Add(member.ToAbsolutePath_XLGpp(pathTable));
            }

            return directoryMembershipEvent;
        }

        /// <nodoc />
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
                processExecutionMonitoringReportedEvent.ReportedProcesses.Add(rp.ToReportedProcess_XLGpp());
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

        /// <nodoc />
        public static ProcessFingerprintComputationEvent_XLGpp ToProcessFingerprintComputationEvent_XLGpp(this ProcessFingerprintComputationEventData data, uint workerID, PathTable pathTable)
        {
            var uuid = Guid.NewGuid().ToString();

            var processFingerprintComputationEvent = new ProcessFingerprintComputationEvent_XLGpp
            {
                UUID = uuid,
                WorkerID = workerID,
                Kind = (FingerprintComputationKind_XLGpp)data.Kind,
                PipID = data.PipId.Value,
                WeakFingerprint = new WeakContentFingerPrint_XLGpp()
                {
                    Hash = data.WeakFingerprint.Hash.ToFingerprint_XLGpp()
                },
            };

            foreach (var strongFingerprintComputation in data.StrongFingerprintComputations)
            {
                var processStrongFingerprintComputationData = new ProcessStrongFingerprintComputationData_XLGpp()
                {
                    PathSet = strongFingerprintComputation.PathSet.ToObservedPathSet_XLGpp(pathTable),
                    PathSetHash = new ContentHash_XLGpp()
                    {
                        Value = strongFingerprintComputation.PathSetHash.ToString()
                    },
                    UnsafeOptions = strongFingerprintComputation.UnsafeOptions.ToUnsafeOptions_XLGpp(),
                    Succeeded = strongFingerprintComputation.Succeeded,
                    IsStrongFingerprintHit = strongFingerprintComputation.IsStrongFingerprintHit,
                    ComputedStrongFingerprint = new StrongContentFingerPrint_XLGpp()
                    {
                        Hash = strongFingerprintComputation.ComputedStrongFingerprint.Hash.ToFingerprint_XLGpp()
                    }
                };

                foreach (var pathEntry in strongFingerprintComputation.PathEntries)
                {
                    processStrongFingerprintComputationData.PathEntries.Add(pathEntry.ToObservedPathEntry_XLGpp(pathTable));
                }

                foreach (var observedAccessedFileName in strongFingerprintComputation.ObservedAccessedFileNames)
                {
                    processStrongFingerprintComputationData.ObservedAccessedFileNames.Add(new StringId_XLGpp()
                    {
                        Value = observedAccessedFileName.Value
                    });
                }

                foreach (var priorStrongFingerprint in strongFingerprintComputation.PriorStrongFingerprints)
                {
                    processStrongFingerprintComputationData.PriorStrongFingerprints.Add(new StrongContentFingerPrint_XLGpp()
                    {
                        Hash = priorStrongFingerprint.Hash.ToFingerprint_XLGpp()
                    });
                }

                foreach (var observedInput in strongFingerprintComputation.ObservedInputs)
                {
                    processStrongFingerprintComputationData.ObservedInputs.Add(new ObservedInput_XLGpp()
                    {
                        Type = (ObservedInputType_XLGpp)observedInput.Type,
                        Hash = new ContentHash_XLGpp()
                        {
                            Value = observedInput.Hash.ToString()
                        },
                        PathEntry = observedInput.PathEntry.ToObservedPathEntry_XLGpp(pathTable),
                        Path = observedInput.Path.ToAbsolutePath_XLGpp(pathTable),
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

        /// <nodoc />
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
                Path = data.Path.ToAbsolutePath_XLGpp(pathTable)
            };
        }

        /// <nodoc />
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

        /// <nodoc />
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

        /// <nodoc />
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

        /// <nodoc />
        public static BXLInvocationEvent_XLGpp ToBXLInvocationEvent_XLGpp(this DominoInvocationEventData data, uint workerID, PathTable pathTable)
        {
            var loggingConfig = data.Configuration.Logging;
            var uuid = Guid.NewGuid().ToString();

            var bxlInvEvent = new BXLInvocationEvent_XLGpp
            {
                UUID = uuid,
                WorkerID = workerID,
                SubstSource = loggingConfig.SubstSource.ToAbsolutePath_XLGpp(pathTable),
                SubstTarget = loggingConfig.SubstTarget.ToAbsolutePath_XLGpp(pathTable),
                IsSubstSourceValid = loggingConfig.SubstSource.IsValid,
                IsSubstTargetValid = loggingConfig.SubstTarget.IsValid
            };

            return bxlInvEvent;
        }

        /// <nodoc />
        public static PipExecutionDirectoryOutputsEvent_XLGpp ToPipExecutionDirectoryOutputsEvent_XLGpp(this PipExecutionDirectoryOutputs data, uint workerID, PathTable pathTable)
        {
            var uuid = Guid.NewGuid().ToString();

            var pipExecDirectoryOutputEvent = new PipExecutionDirectoryOutputsEvent_XLGpp();
            pipExecDirectoryOutputEvent.UUID = uuid;
            pipExecDirectoryOutputEvent.WorkerID = workerID;

            foreach (var (directoryArtifact, fileArtifactArray) in data.DirectoryOutputs)
            {
                var directoryOutput = new DirectoryOutput_XLGpp()
                {
                    DirectoryArtifact = new DirectoryArtifact_XLGpp()
                    {
                        IsValid = directoryArtifact.IsValid,
                        Path = directoryArtifact.Path.ToAbsolutePath_XLGpp(pathTable),
                        PartialSealID = directoryArtifact.PartialSealId,
                        IsSharedOpaque = directoryArtifact.IsSharedOpaque
                    }
                };

                foreach (var file in fileArtifactArray)
                {
                    directoryOutput.FileArtifactArray.Add(file.ToFileArtifact_XLGpp(pathTable));
                }

                pipExecDirectoryOutputEvent.DirectoryOutput.Add(directoryOutput);
            }
            return pipExecDirectoryOutputEvent;
        }

        /// <nodoc />
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
                Process = reportedFileAccess.Process.ToReportedProcess_XLGpp(),
                ShareMode = (ShareMode_XLGpp)reportedFileAccess.ShareMode,
                Status = (FileAccessStatus_XLGpp)reportedFileAccess.Status,
                Method = (FileAccessStatusMethod_XLGpp)reportedFileAccess.Method,
                RequestedAccess = (RequestedAccess_XLGpp)reportedFileAccess.RequestedAccess,
                Operation = (ReportedFileOperation_XLGpp)reportedFileAccess.Operation,
                ExplicitlyReported = reportedFileAccess.ExplicitlyReported,
                EnumeratePattern = reportedFileAccess.EnumeratePattern
            };
        }

        /// <nodoc />
        public static ObservedPathSet_XLGpp ToObservedPathSet_XLGpp(this ObservedPathSet pathSet, PathTable pathTable)
        {
            var observedPathSet = new ObservedPathSet_XLGpp();

            foreach (var pathEntry in pathSet.Paths)
            {
                observedPathSet.Paths.Add(pathEntry.ToObservedPathEntry_XLGpp(pathTable));
            }

            foreach (var observedAccessedFileName in pathSet.ObservedAccessedFileNames)
            {
                observedPathSet.ObservedAccessedFileNames.Add(new StringId_XLGpp()
                {
                    Value = observedAccessedFileName.Value
                });
            }

            observedPathSet.UnsafeOptions = pathSet.UnsafeOptions.ToUnsafeOptions_XLGpp();

            return observedPathSet;
        }

        /// <nodoc />
        public static UnsafeOptions_XLGpp ToUnsafeOptions_XLGpp(this UnsafeOptions unsafeOption)
        {
            var unsafeOpt = new UnsafeOptions_XLGpp()
            {
                PreserveOutputsSalt = new ContentHash_XLGpp()
                {
                    Value = unsafeOption.PreserveOutputsSalt.ToString()
                },
                UnsafeConfiguration = new IUnsafeSandboxConfiguration_XLGpp()
                {
                    PreserveOutputs = (PreserveOutputsMode_XLGpp)unsafeOption.UnsafeConfiguration.PreserveOutputs,
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
                    SandboxKind = (SandboxKind_XLGpp)unsafeOption.UnsafeConfiguration.SandboxKind,
                    UnexpectedFileAccessesAreErrors = unsafeOption.UnsafeConfiguration.UnexpectedFileAccessesAreErrors,
                    IgnoreGetFinalPathNameByHandle = unsafeOption.UnsafeConfiguration.IgnoreGetFinalPathNameByHandle,
                    IgnoreDynamicWritesOnAbsentProbes = unsafeOption.UnsafeConfiguration.IgnoreDynamicWritesOnAbsentProbes,
                    IgnoreUndeclaredAccessesUnderSharedOpaques = unsafeOption.UnsafeConfiguration.IgnoreUndeclaredAccessesUnderSharedOpaques,
                }
            };

            if (unsafeOption.UnsafeConfiguration.DoubleWritePolicy != null)
            {
                unsafeOpt.UnsafeConfiguration.DoubleWritePolicy = (DoubleWritePolicy_XLGpp)unsafeOption.UnsafeConfiguration.DoubleWritePolicy;
            }
            return unsafeOpt;
        }

        /// <nodoc />
        public static AbsolutePath_XLGpp ToAbsolutePath_XLGpp(this AbsolutePath path, PathTable pathTable)
        {
            return new AbsolutePath_XLGpp()
            {
                Value = path.ToString(pathTable, PathFormat.HostOs)
            };
        }

        /// <nodoc />
        public static FileArtifact_XLGpp ToFileArtifact_XLGpp(this FileArtifact fileArtifact, PathTable pathTable)
        {
            return new FileArtifact_XLGpp
            {
                Path = fileArtifact.Path.ToAbsolutePath_XLGpp(pathTable),
                RewriteCount = fileArtifact.RewriteCount,
                IsSourceFile = fileArtifact.IsSourceFile,
                IsOutputFile = fileArtifact.IsOutputFile
            };
        }

        /// <nodoc />
        public static ReportedProcess_XLGpp ToReportedProcess_XLGpp(this ReportedProcess reportedProcess)
        {
            return new ReportedProcess_XLGpp()
            {
                Path = reportedProcess.Path,
                ProcessId = reportedProcess.ProcessId,
                ProcessArgs = reportedProcess.ProcessArgs,
                ReadCounters = new IOTypeCounters_XLGpp
                {
                    OperationCount = reportedProcess.IOCounters.ReadCounters.OperationCount,
                    TransferCOunt = reportedProcess.IOCounters.ReadCounters.TransferCount
                },
                WriteCounters = new IOTypeCounters_XLGpp
                {
                    OperationCount = reportedProcess.IOCounters.WriteCounters.OperationCount,
                    TransferCOunt = reportedProcess.IOCounters.WriteCounters.TransferCount
                },
                OtherCounters = new IOTypeCounters_XLGpp
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
        public static ObservedPathEntry_XLGpp ToObservedPathEntry_XLGpp(this ObservedPathEntry pathEntry, PathTable pathTable)
        {
            return new ObservedPathEntry_XLGpp()
            {
                Path = pathEntry.Path.ToAbsolutePath_XLGpp(pathTable),
                EnumeratePatternRegex = pathEntry.EnumeratePatternRegex ?? ""
            };
        }

        /// <nodoc />
        public static Fingerprint_XLGpp ToFingerprint_XLGpp(this Fingerprint fingerprint)
        {
            return new Fingerprint_XLGpp()
            {
                Length = fingerprint.Length,
                Bytes = Google.Protobuf.ByteString.CopyFrom(fingerprint.ToByteArray())
            };
        }
    }
}
