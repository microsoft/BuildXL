// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


using System;
using System.Threading.Tasks;
using BuildXL.Engine;
using BuildXL.Engine.Distribution;
using BuildXL.Utilities;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Scheduler;
using System.Collections.Generic;
using BuildXL.Scheduler.Distribution;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Instrumentation.Common;
using System.Threading;
using System.Diagnostics.ContractsLight;
using BuildXL.Distribution.Grpc;
using Google.Protobuf;

namespace Test.BuildXL.Distribution
{
    internal class WorkerNotificationManagerMock : IWorkerNotificationManager
    {
        public bool Started => StartCalls > 0;
        public int StartCalls;

        public bool Exited => ExitCalls > 0;
        public int ExitCalls;

        public volatile bool CleanExit; 

        public bool Cancelled => CancelCalls > 0;
        public int CancelCalls;

        public int ReportEventMessageCalls;
        public int ReportResultCalls;

        void IWorkerNotificationManager.Cancel()
        {
            Interlocked.Increment(ref CancelCalls);
        }

        void IWorkerNotificationManager.Exit(bool isCleanExit)
        {
            Interlocked.Increment(ref ExitCalls);
            if (!isCleanExit)
            {
                CleanExit = false;
            }
        }

        void IWorkerNotificationManager.ReportEventMessage(EventMessage eventMessage)
        {
            Interlocked.Increment(ref ReportEventMessageCalls);
        }

        void IWorkerNotificationManager.ReportResult(ExtendedPipCompletionData pipCompletion)
        {
            Interlocked.Increment(ref ReportResultCalls);
        }

        void IWorkerNotificationManager.Start(IOrchestratorClient orchestratorClient, EngineSchedule schedule, IPipResultSerializer serializer)
        {
            Interlocked.Increment(ref StartCalls);
        }

        public void MarkPipProcessingStarted(long semistableHash)
        {
        }
    }

    internal class WorkerPipExecutionServiceMock : IWorkerPipExecutionService
    {
        public bool Started;
        public ISet<(uint PipId, PipExecutionStep ExecutionStep)> StepsToFail = new HashSet<(uint, PipExecutionStep)>();

        public WorkerService WorkerService;
        private readonly LoggingContext m_loggingContext;
        private int m_finishedRequests = 0;
        private int m_ongoingRequests = 0;

        public WorkerPipExecutionServiceMock(LoggingContext loggingContext) => m_loggingContext = loggingContext;

        public void WaitForPendingRequests(int expectedCount, TimeSpan? timeout = null)
        {
            var maxWait = timeout ?? TimeSpan.FromSeconds(20);  // Let's not hang
            var timeoutTask = Task.Delay(maxWait);
            SpinWait.SpinUntil(() => (m_ongoingRequests == 0 && m_finishedRequests == expectedCount) || timeoutTask.IsCompleted);
            Contract.Assert(!timeoutTask.IsCompleted, $"Timed out waiting for pending requests. Finished requests: {m_finishedRequests}. Expected: {expectedCount}. Ongoing {m_ongoingRequests}");
        }

        // Interface methods
        uint IWorkerPipExecutionService.WorkerId => 1;

        Task<Possible<AttachCompletionInfo>> IWorkerPipExecutionService.ConstructAttachCompletionInfo()
        {
            var aci = new AttachCompletionInfo
            {
                WorkerId = 1,
                MaxProcesses = 2,
                MaxMaterialize = 3,
                AvailableRamMb = 100000,
                AvailableCommitMb = 200000,
                WorkerCacheValidationContentHash = ByteString.CopyFrom(new byte[10])
            };

            return Task.FromResult(new Possible<AttachCompletionInfo>(aci));
        }


        void IWorkerPipExecutionService.Start(EngineSchedule schedule, BuildStartData buildStartData)
        {
            Started = true;
        }

        void IWorkerPipExecutionService.WhenDone()
        {
        }

        Possible<Unit> IWorkerPipExecutionService.TryReportInputs(IEnumerable<FileArtifactKeyedHash> hashes) => new Possible<Unit>(Unit.Void);

        async Task IWorkerPipExecutionService.StartPipStepAsync(PipId pipId, ExtendedPipCompletionData pipCompletionData, SinglePipBuildRequest pipBuildRequest, Possible<Unit> reportInputsResult)
        {
            Interlocked.Increment(ref m_ongoingRequests);
            await Task.Yield();
            var step = (PipExecutionStep)pipBuildRequest.Step;

            if (StepsToFail.Contains((pipId.Value, step)))
            {
                WorkerService.ReportResult(
                    pipId,
                    ExecutionResult.GetFailureResultForTesting(),
                    step);
            }
            else
            {
                WorkerService.ReportResult(
                    pipId,
                    ExecutionResult.GetEmptySuccessResult(m_loggingContext),
                    step);
            }

            Interlocked.Increment(ref m_finishedRequests);
            Interlocked.Decrement(ref m_ongoingRequests);
        }

        string IWorkerPipExecutionService.GetPipDescription(PipId pipId) => pipId.ToString();
    }

    internal class PipResultSerializerMock : IPipResultSerializer
    {
        public void SerializeExecutionResult(ExtendedPipCompletionData completionData) { }
    }
}