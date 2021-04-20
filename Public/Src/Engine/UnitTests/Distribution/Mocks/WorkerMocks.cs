// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.


using System;
using System.Threading.Tasks;
using BuildXL.Engine;
using BuildXL.Engine.Distribution;
using BuildXL.Utilities;
using BuildXL.Engine.Distribution.OpenBond;
using OpenBondBuildStartData = BuildXL.Engine.Distribution.OpenBond.BuildStartData;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Scheduler;
using System.Collections.Generic;
using BuildXL.Scheduler.Distribution;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Instrumentation.Common;
using System.Threading;


namespace Test.BuildXL.Distribution
{
    internal class WorkerNotificationManagerMock : IWorkerNotificationManager
    {
        public bool Started => StartCalls > 0;
        public int StartCalls;

        public bool Exited => ExitCalls > 0;
        public int ExitCalls;

        public bool Cancelled => CancelCalls > 0;
        public int CancelCalls;

        public int ReportEventMessageCalls;
        public int ReportResultCalls;

        void IWorkerNotificationManager.Cancel()
        {
            CancelCalls++;
        }

        void IWorkerNotificationManager.Exit()
        {
            ExitCalls++;
        }

        void IWorkerNotificationManager.ReportEventMessage(EventMessage eventMessage)
        {
            ReportEventMessageCalls++;
        }

        void IWorkerNotificationManager.ReportResult(ExtendedPipCompletionData pipCompletion)
        {
            ReportResultCalls++;
        }

        void IWorkerNotificationManager.Start(IMasterClient orchestratorClient, EngineSchedule schedule)
        {
            StartCalls++;
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

        public void WaitForPendingRequests()
        {
            SpinWait.SpinUntil(() => m_ongoingRequests == 0);
        }

        // Interface methods
        Task<Possible<AttachCompletionInfo>> IWorkerPipExecutionService.ConstructAttachCompletionInfo()
        {
            var aci = new AttachCompletionInfo
            {
                WorkerId = 1,
                MaxProcesses = 2,
                MaxMaterialize = 3,
                AvailableRamMb = 100000,
                AvailableCommitMb = 200000,
                WorkerCacheValidationContentHash = new BondContentHash
                {
                    Data = new ArraySegment<byte>(new byte[10]),
                }
            };

            return Task.FromResult(new Possible<AttachCompletionInfo>(aci));
        }


        void IWorkerPipExecutionService.Start(EngineSchedule schedule, OpenBondBuildStartData buildStartData)
        {
            Started = true;
        }

        void IWorkerPipExecutionService.WhenDone()
        {
        }

        void IWorkerPipExecutionService.Transition(PipId pipId, WorkerPipState state)
        {
        }

        Possible<Unit> IWorkerPipExecutionService.TryReportInputs(List<FileArtifactKeyedHash> hashes) => new Possible<Unit>(Unit.Void);

        async Task IWorkerPipExecutionService.HandlePipStepAsync(PipId pipId, ExtendedPipCompletionData pipCompletionData, SinglePipBuildRequest pipBuildRequest, Possible<Unit> reportInputsResult)
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

        void IDisposable.Dispose()
        {
        }
    }

}