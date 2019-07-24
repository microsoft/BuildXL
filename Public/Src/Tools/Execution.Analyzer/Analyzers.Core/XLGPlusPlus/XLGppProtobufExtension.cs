// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;

namespace BuildXL.Execution.Analyzer
{
    /// <summary>
    /// Extension methods for XLGpp ProtoBuf conversions.
    /// </summary>
    public static class XLGppProtobufExtension
    {

        public static FileArtifactContentDecidedEvent_XLGpp ToFileArtifactContentDecidedEvent_XLGpp(this FileArtifactContentDecidedEventData data, PathTable pathTable)
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
                Hash = new ContentHash_XLGp() { Value = data.FileContentInfo.Hash.ToString() }

            };

            fileArtifactContentDecidedEvent.UUID = uuid;
            fileArtifactContentDecidedEvent.FileArtifact = fileArtifact;
            fileArtifactContentDecidedEvent.FileContentInfo = fileContentInfo;
            fileArtifactContentDecidedEvent.OutputOrigin = (PipOutputOrigin_XLGpp)data.OutputOrigin;
            return fileArtifactContentDecidedEvent;
        }

        public static WorkerListEvent_XLGpp ToWorkerListEvent_XLGpp(this WorkerListEventData data)
        {
            return new WorkerListEvent_XLGpp();
        }
        public static PipExecutionPerformanceEvent_XLGpp ToPipExecutionPerformanceEvent_XLGpp(this PipExecutionPerformanceEventData data)
        {
            return new PipExecutionPerformanceEvent_XLGpp();
        }

        public static DirectoryMembershipHashedEvent_XLGpp ToDirectoryMembershipHashedEvent_XLGpp(this DirectoryMembershipHashedEventData data)
        {
            return new DirectoryMembershipHashedEvent_XLGpp();
        }

        public static ProcessExecutionMonitoringReportedEvent_XLGpp ToProcessExecutionMonitoringReportedEvent_XLGpp(this ProcessExecutionMonitoringReportedEventData data)
        {
            return new ProcessExecutionMonitoringReportedEvent_XLGpp();
        }

        public static ProcessFingerprintComputationEvent_XLGpp ToProcessFingerprintComputationEvent_XLGpp(this StatusEventData data)
        {
            return new ProcessFingerprintComputationEvent_XLGpp();
        }

        public static ExtraEventDataReported_XLGpp ToExtraEventDataReported_XLGpp(this ExtraEventData data)
        {
            return new ExtraEventDataReported_XLGpp();
        }

        public static DependencyViolationReportedEvent_XLGpp ToDependencyViolationReportedEvent_XLGpp(this DependencyViolationEventData data)
        {
            return new DependencyViolationReportedEvent_XLGpp();
        }

        public static PipExecutionStepPerformanceReportedEvent_XLGpp ToPipExecutionStepPerformanceReportedEvent_XLGpp(this PipExecutionStepPerformanceEventData data)
        {
            return new PipExecutionStepPerformanceReportedEvent_XLGpp();
        }


        public static PipCacheMissEvent_XLGpp ToPipCacheMissEvent_XLGpp(this PipCacheMissEventData data)
        {
            return new PipCacheMissEvent_XLGpp();
        }

        public static StatusReportedEvent_XLGpp ToResourceUsageReportedEvent_XLGpp(this StatusEventData data)
        {
            return new StatusReportedEvent_XLGpp();
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

    }
}
