// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Distribution.Grpc;
using BuildXL.Engine.Distribution.OpenBond;
using BuildXL.Engine.Tracing;
using BuildXL.Pips;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Distribution;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using static BuildXL.Engine.Distribution.Grpc.ClientConnectionManager;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Defines service run on worker nodes in a distributed build.
    /// </summary>
    public sealed partial class WorkerService : DistributionService
    {
        /// <summary>
        /// Gets the build start data from the coordinator passed after the attach operation
        /// </summary>
        public BuildStartData BuildStartData { get; private set; }

        /// <summary>
        /// Returns a task representing the completion of the exit operation
        /// </summary>
        internal Task<bool> ExitCompletion => m_exitCompletionSource.Task;
        private readonly TaskSourceSlim<bool> m_exitCompletionSource;

        /// <summary>
        /// Returns a task representing the completion of the attach operation
        /// </summary>
        internal Task<bool> AttachCompletion => m_attachCompletionSource.Task;
        private readonly TaskSourceSlim<bool> m_attachCompletionSource;

        private readonly ConcurrentDictionary<(PipId, PipExecutionStep), SinglePipBuildRequest> m_pendingBuildRequests =
            new ConcurrentDictionary<(PipId, PipExecutionStep), SinglePipBuildRequest>();

        private readonly ConcurrentDictionary<(PipId,PipExecutionStep), ExtendedPipCompletionData> m_pendingPipCompletions =
            new ConcurrentDictionary<(PipId, PipExecutionStep), ExtendedPipCompletionData>();

        private readonly ConcurrentBigSet<int> m_handledBuildRequests = new ConcurrentBigSet<int>();

        /// <summary>
        /// Identifies the worker
        /// </summary>
        public uint WorkerId { get; private set; }

        private volatile bool m_hasFailures;
        private volatile string m_failureMessage;

        /// <summary>
        /// Whether orchestrator is done with the worker by sending a message to worker.
        /// </summary>
        private volatile bool m_isOrchestratorExited;

        private LoggingContext m_appLoggingContext;
        private readonly IWorkerNotificationManager m_notificationManager;
        private readonly IWorkerPipExecutionService m_pipExecutionService;
        private readonly IConfiguration m_config;
        private readonly ushort m_port;
        
        private readonly IOrchestratorClient m_orchestratorClient;
        private readonly IServer m_workerServer;

        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="appLoggingContext">Application-level logging context</param>
        /// <param name="config">Build config</param>
        /// <param name="buildId">the build id</param>
        public WorkerService(LoggingContext appLoggingContext, IConfiguration config, DistributedBuildId buildId) :  
            this(appLoggingContext,
                config,
                buildId,
                executionService: null,
                workerServer: null,
                notificationManager: null,
                orchestratorClient: null)
        {
            m_pipExecutionService = new WorkerPipExecutionService(this);
            m_notificationManager = new WorkerNotificationManager(this, m_pipExecutionService, appLoggingContext);
            m_orchestratorClient = new Grpc.GrpcOrchestratorClient(m_appLoggingContext, BuildId);
            m_workerServer = new Grpc.GrpcWorkerServer(this, appLoggingContext, buildId);
        }

        internal static WorkerService CreateForTesting(
            LoggingContext appLoggingContext,
            IConfiguration config,
            DistributedBuildId buildId,
            // The following are used for testing:
            IWorkerPipExecutionService executionService,
            IServer server,
            IWorkerNotificationManager notificationManager,
            IOrchestratorClient orchestratorClient)
        {
            return new WorkerService(appLoggingContext, config, buildId, executionService, server, orchestratorClient, notificationManager);
        }

        private WorkerService(LoggingContext appLoggingContext, 
            IConfiguration config,
            DistributedBuildId buildId, 
            IWorkerPipExecutionService executionService, 
            IServer workerServer,
            IOrchestratorClient orchestratorClient,
            IWorkerNotificationManager notificationManager) : base(buildId)
        {
            m_appLoggingContext = appLoggingContext;
            m_port = config.Distribution.BuildServicePort;
            m_config = config;
            m_attachCompletionSource = TaskSourceSlim.Create<bool>();
            m_exitCompletionSource = TaskSourceSlim.Create<bool>();

            m_workerServer = workerServer;
            m_pipExecutionService = executionService;
            m_notificationManager = notificationManager;
            m_orchestratorClient = orchestratorClient;
        }

        internal void Start(EngineSchedule schedule, ExecutionResultSerializer resultSerializer)
        {
            Contract.Assert(AttachCompletion.IsCompleted && AttachCompletion.GetAwaiter().GetResult(), "Start called before finishing attach on worker");
            m_pipExecutionService.Start(schedule, BuildStartData);
            m_notificationManager.Start(m_orchestratorClient, schedule, new PipResultSerializer(resultSerializer));
        }

        /// <summary>
        /// Connects to the orchestrator and enables this node to receive build requests
        /// </summary>
        internal async Task<bool> WhenDoneAsync()
        {
            Contract.Assert(AttachCompletion.IsCompleted && AttachCompletion.GetAwaiter().GetResult(), "ProcessBuildRequests called before finishing attach on worker");

            bool success = await SendAttachCompletedAfterProcessBuildRequestStartedAsync();

            // Wait until the build finishes or we discovered that the orchestrator is dead
            success &= await ExitCompletion; 
            
            success &= !m_hasFailures;
            if (m_failureMessage != null)
            {
                Logger.Log.DistributionWorkerExitFailure(m_appLoggingContext, m_failureMessage);
            }

            m_pipExecutionService.WhenDone();

            return success;
        }

        /// <summary>
        /// Waits for the orchestrator to attach synchronously
        /// </summary>
        internal bool WaitForOrchestratorAttach()
        {
            Logger.Log.DistributionWaitingForOrchestratorAttached(m_appLoggingContext);
            
            var timeout = GrpcSettings.WorkerAttachTimeout;
            if (!AttachCompletion.Wait(timeout))
            {
                Logger.Log.DistributionWorkerTimeoutFailure(m_appLoggingContext);
                Exit(failure: "Timed out waiting for attach request from orchestrator", isUnexpected: true);
                return false;
            }

            if (!AttachCompletion.Result)
            {
                Logger.Log.DistributionInactiveOrchestrator(m_appLoggingContext, (int)timeout.TotalMinutes);
                return false;
            }

            return true;
        }

        /// <nodoc/>
        public void ExitCallReceivedFromOrchestrator()
        {
            m_isOrchestratorExited = true;
            Logger.Log.DistributionExitReceived(m_appLoggingContext);
        }

        /// <nodoc/>
        public void Exit(Optional<string> failure = default, bool isUnexpected = false)
        {
            m_notificationManager.Exit();
            m_orchestratorClient.CloseAsync().GetAwaiter().GetResult();

            m_attachCompletionSource.TrySetResult(false);
            var reportSuccess = !failure.HasValue;
            
            if (!reportSuccess)
            {
                m_hasFailures = true;
                m_failureMessage = failure.Value;
            }

            if (isUnexpected && m_isOrchestratorExited)
            {
                // If the worker unexpectedly exits the build after orchestrator exits the build, 
                // we should log a message to keep track of the frequency.
                Logger.Log.DistributionWorkerUnexpectedFailureAfterOrchestratorExits(m_appLoggingContext);
            }

            // Request server shut down before exiting, so the orchestrator stops requests our way
            // gRPC ensures that any pending calls will still be served as part of the shutdown process,
            // so there is no problem if we're executing this as part of an "exit" RPC (which is the normal way of exiting)
            // Do not await, as this call to Exit may have been made as part of the exit RPC and we need to finish it.
            m_workerServer.ShutdownAsync().Forget();
            m_exitCompletionSource.TrySetResult(reportSuccess);
        }

        /// <summary>
        /// Retrieves the cache descriptor containing the build scheduler state from the coordinator node
        /// </summary>
        /// <returns>true if the operation was successful</returns>
        internal bool TryGetBuildScheduleDescriptor(out PipGraphCacheDescriptor descriptor)
        {
            descriptor = null;
            if (!AttachCompletion.Result)
            {
                return false;
            }

            descriptor = BuildStartData.CachedGraphDescriptor;
            return true;
        }

        internal void AttachCore(BuildStartData buildStartData, string orchestratorName)
        {
            Logger.Log.DistributionAttachReceived(m_appLoggingContext, buildStartData.SessionId, orchestratorName);
            BuildStartData = buildStartData;

            // The app-level logging context has a wrong session id. Fix it now that we know the right one.
            m_appLoggingContext = new LoggingContext(
                m_appLoggingContext.ActivityId,
                m_appLoggingContext.LoggerComponentInfo,
                new LoggingContext.SessionInfo(buildStartData.SessionId, m_appLoggingContext.Session.Environment, m_appLoggingContext.Session.RelatedActivityId),
                m_appLoggingContext);

            m_orchestratorClient.Initialize(buildStartData.OrchestratorLocation.IpAddress, buildStartData.OrchestratorLocation.Port, OnConnectionTimeOutAsync);

            WorkerId = BuildStartData.WorkerId;
            m_attachCompletionSource.TrySetResult(true);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer02:MissingAsyncOpportunity")]
        private async Task<bool> SendAttachCompletedAfterProcessBuildRequestStartedAsync()
        {
            var possiblyAttachCompletionInfo = await m_pipExecutionService.ConstructAttachCompletionInfo();

            if (!possiblyAttachCompletionInfo.Succeeded)
            {
                Exit("Failed to validate retrieve content from orchestrator via cache", isUnexpected: true);
                return false;
            }

            var attachCompletionResult = await m_orchestratorClient.AttachCompletedAsync(possiblyAttachCompletionInfo.Result);

            if (!attachCompletionResult.Succeeded)
            {
                Exit($"Failed to attach to orchestrator. Duration: {(int)attachCompletionResult.Duration.TotalMinutes}", isUnexpected: true);
                return false;
            }

            return true;
        }

        private async void OnConnectionTimeOutAsync(object sender, ConnectionTimeoutEventArgs e)
        {
            Logger.Log.DistributionConnectionTimeout(m_appLoggingContext, "orchestrator", e?.Details ?? "");

            // Stop sending messages
            m_notificationManager.Cancel();
           
            // Unblock caller to make it a fire&forget event handler.
            await Task.Yield();
            Logger.Log.DistributionInactiveOrchestrator(m_appLoggingContext, (int)(GrpcSettings.CallTimeout.TotalMinutes * GrpcSettings.MaxRetry));
            ExitAsync("Connection timed out", isUnexpected: true).Forget();
        }

        /// <nodoc />
        public void ExecutePipsCore(PipBuildRequest request)
        {
            var reportInputsResult = m_pipExecutionService.TryReportInputs(request.Hashes);

            for (int i = 0; i < request.Pips.Count; i++)
            {
                SinglePipBuildRequest pipBuildRequest = request.Pips[i];

                // Start the pip. Handle the case of a retry - the pip may be already started by a previous call.
                if (m_handledBuildRequests.Add(pipBuildRequest.SequenceNumber))
                {
                    var pipId = new PipId(pipBuildRequest.PipIdValue);
                    var pipIdStepTuple = (pipId, (PipExecutionStep)pipBuildRequest.Step);
                    m_pendingBuildRequests[pipIdStepTuple] = pipBuildRequest;

                    var pipCompletionData = new ExtendedPipCompletionData(new PipCompletionData() { PipIdValue = pipId.Value, Step = pipBuildRequest.Step });
                    m_pendingPipCompletions[pipIdStepTuple] = pipCompletionData;

                    m_pipExecutionService.HandlePipStepAsync(pipId, pipCompletionData, pipBuildRequest, reportInputsResult).Forget((ex) =>
                    {
                        Scheduler.Tracing.Logger.Log.HandlePipStepOnWorkerFailed(
                            m_appLoggingContext,
                            m_pipExecutionService.GetPipDescription(pipId),
                            ex.ToString());

                        // HandlePipStep might throw an exception after we remove pipCompletionData from m_pendingPipCompletions.
                        // That's why, we check whether the pipCompletionData still exists there.
                        if (m_pendingPipCompletions.ContainsKey(pipIdStepTuple))
                        {
                            ReportResult(
                                pipId,
                                ExecutionResult.GetFailureNotRunResult(m_appLoggingContext),
                                (PipExecutionStep)pipBuildRequest.Step);
                        }
                    });
                }
            }
        }

        internal void ReportResult(
            PipId pipId,
            ExecutionResult executionResult,
            PipExecutionStep step)
        {
            m_pipExecutionService.Transition(pipId, WorkerPipState.Recording);
            if (executionResult.Result == PipResultStatus.Failed)
            {
                m_hasFailures = true;
            }

            bool found = m_pendingPipCompletions.TryRemove((pipId, step), out var pipCompletion);
            Contract.Assert(found, "Could not find corresponding build completion data for executed pip on worker");

            pipCompletion.ExecutionResult = executionResult;

            if (step == PipExecutionStep.MaterializeOutputs && m_config.Distribution.FireForgetMaterializeOutput)
            {
                // We do not report 'MaterializeOutput' step results back to orchestrator.
                Logger.Log.DistributionWorkerFinishedPipRequest(m_appLoggingContext, pipCompletion.SemiStableHash, step.ToString());
                return;
            }

            m_notificationManager.ReportResult(pipCompletion);
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            LogStatistics(m_appLoggingContext);
            m_pipExecutionService.Dispose();
            m_workerServer.Dispose();
        }

        /// <nodoc/>
        public override async Task ExitAsync(Optional<string> failure, bool isUnexpected)
        {
            await Task.Yield();
            Exit(failure, isUnexpected);
        }

        /// <inheritdoc />
        public override bool Initialize()
        {
            // Start listening to the port
            try
            {
                m_workerServer.Start(m_port);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log.DistributionServiceInitializationError(m_appLoggingContext, DistributedBuildRole.Worker.ToString(), m_port, ExceptionUtilities.GetLogEventMessage(ex));
                return false;
            }
        }

    }
}
