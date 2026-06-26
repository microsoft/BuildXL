// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO.Pipes;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Engine.Distribution.Grpc;
using BuildXL.Engine.Tracing;
using BuildXL.Pips;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Distribution;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Distribution.Grpc.HelloResponse.Types;
using static BuildXL.Engine.Distribution.Grpc.ClientConnectionManager;
using PipGraphCacheDescriptor = BuildXL.Engine.Cache.Fingerprints.PipGraphCacheDescriptor;
using Logger = BuildXL.Engine.Tracing.Logger;
using static BuildXL.Distribution.Grpc.Orchestrator;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Service methods called by the RPC layer as part of the RPC started in the orchestrator
    /// </summary>
    /// <remarks>This interface is marked internal to reduce visibility to the distribution layer only</remarks>
    internal interface IWorkerService
    {
        /// <summary>
        /// Inform the locaton of this worker to the orchestrator. If the call fails, the exit logic is triggered and this method returns false.
        /// </summary>
        /// <returns>
        /// true if succesful, false when the call fails for whatever reason. 
        /// </returns>
        Task<bool> SayHelloAsync(IDistributionServiceLocation orchestratorLocation);
        
        /// <summary>
        /// Performs attachment 
        /// </summary>
        void Attach(BuildStartData buildStartData, string sender);

        /// <summary>
        /// Requests execution of a pip build request
        /// </summary>
        /// <param name="request"></param>
        Task ExecutePipsAsync(PipBuildRequest request);

        /// <summary>
        /// Notifies the WorkerService that the orchestrator has issued an exit request
        /// </summary>
        /// <param name="failure">If present, the build will be considered a failure</param>
        /// <param name="exitMessage">Details of the operation to log on exit</param>
        void ExitRequested(string exitMessage, Optional<string> failure);

        /// <summary>
        /// Retrieve the EventStats from remote worker
        /// </summary>
        long[] RetrieveWorkerEventStats();
    }

    /// <summary>
    /// Defines service run on worker nodes in a distributed build.
    /// </summary>
    public sealed partial class WorkerService : DistributionService, IWorkerService
    {
        /// <summary>
        /// Gets the build start data from the coordinator passed after the attach operation
        /// </summary>
        public BuildStartData BuildStartData { get; private set; }

        /// <summary>
        /// Returns a task representing the completion of the exit operation
        /// </summary>
        internal Task<bool> ExitCompletion => m_exitCompletion.Task;
        private readonly TaskSourceSlim<bool> m_exitCompletion;

        /// <summary>
        /// Returns a task representing the completion of the attach operation
        /// </summary>
        internal Task<AttachResult> AttachCallTask => m_attachCallCompletion.Task;
        private readonly TaskSourceSlim<AttachResult> m_attachCallCompletion;
        private readonly CancellationTokenSource m_cancellationOnExit = new CancellationTokenSource();

        private readonly ConcurrentDictionary<(PipId, PipExecutionStep), SinglePipBuildRequest> m_pendingBuildRequests = new();

        private readonly ConcurrentDictionary<(PipId, PipExecutionStep), ExtendedPipCompletionData> m_pendingPipCompletions = new();

        internal readonly ConcurrentDictionary<(PipId, PipExecutionStep), bool> PendingScheduleRequests = new();

        private readonly ConcurrentBigSet<int> m_handledBuildRequests = new ConcurrentBigSet<int>();

        /// <nodoc/>
        public string OrchestratorIpAddress { get; private set; }

        /// <summary>
        /// Identifies the worker
        /// </summary>
        public uint WorkerId { get; private set; }

        private volatile bool m_hasFailures;
        private volatile string m_failureMessage;
        private int m_connectionClosedFlag = 0;
        private volatile bool m_exitStarted;

        /// <summary>
        /// Whether orchestrator is done with the worker by sending a message to worker.
        /// </summary>
        private volatile bool m_isOrchestratorExited;

        // Set once by the termination watcher when the runner signals that the orchestrator is gone
        // (terminal Failed/Cancelled/Abandoned state, or runner death -- EOF is treated the same).
        // The worker-side teardown reads this to skip best-effort gRPC sends/flushes/closes that would
        // otherwise wait out the retry budget against a dead peer.
        private int m_orchestratorAbandoned;

        /// <summary>True once the termination watcher observes the orchestrator is gone.</summary>
        internal bool OrchestratorAbandoned => Volatile.Read(ref m_orchestratorAbandoned) != 0;

        private void SetOrchestratorAbandoned() => Interlocked.Exchange(ref m_orchestratorAbandoned, 1);

        private LoggingContext m_appLoggingContext;
        private bool m_orchestratorInitialized;
        private readonly IWorkerNotificationManager m_notificationManager;
        private readonly IWorkerPipExecutionService m_pipExecutionService;
        private readonly IConfiguration m_config;
        private readonly ushort m_port;
        
        private readonly IOrchestratorClient m_orchestratorClient;
        private readonly IServer m_workerServer;
        private IExecutionLogTarget m_executionLogTarget;

        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="appLoggingContext">Application-level logging context</param>
        /// <param name="config">Build config</param>
        /// <param name="invocationId">the distributed invocation id</param>
        public WorkerService(LoggingContext appLoggingContext, IConfiguration config, DistributedInvocationId invocationId) :  
            this(appLoggingContext,
                config,
                invocationId,
                executionService: null,
                workerServer: null,
                notificationManager: null,
                orchestratorClient: null)
        {
            m_pipExecutionService = new WorkerPipExecutionService(this);
            m_notificationManager = new WorkerNotificationManager(this, m_pipExecutionService, appLoggingContext);
            m_orchestratorClient = new Grpc.GrpcOrchestratorClient(m_appLoggingContext, InvocationId, Counters);
            m_workerServer = new Grpc.GrpcWorkerServer(appLoggingContext, this, invocationId);

            // Start only after all collaborators are assigned: a queued signal byte can fire ExitRequested
            // immediately, and Exit() dereferences these fields (e.g. m_workerServer.ShutdownAsync()).
            StartOrchestratorTerminationWatcher();
        }

        internal static WorkerService CreateForTesting(
            LoggingContext appLoggingContext,
            IConfiguration config,
            DistributedInvocationId invocationId,
            // The following are used for testing:
            IWorkerPipExecutionService executionService,
            IServer server,
            IWorkerNotificationManager notificationManager,
            IOrchestratorClient orchestratorClient)
        {
            var workerService = new WorkerService(appLoggingContext, config, invocationId, executionService, server, orchestratorClient, notificationManager);
            workerService.StartOrchestratorTerminationWatcher();
            return workerService;
        }

        private WorkerService(LoggingContext appLoggingContext, 
            IConfiguration config,
            DistributedInvocationId invocationId, 
            IWorkerPipExecutionService executionService, 
            IServer workerServer,
            IOrchestratorClient orchestratorClient,
            IWorkerNotificationManager notificationManager) : base(invocationId)
        {
            m_appLoggingContext = appLoggingContext;
            m_port = config.Distribution.BuildServicePort;
            m_config = config;
            m_attachCallCompletion = TaskSourceSlim.Create<AttachResult>();
            m_exitCompletion = TaskSourceSlim.Create<bool>();

            m_workerServer = workerServer;
            m_pipExecutionService = executionService;
            m_notificationManager = notificationManager;
            m_orchestratorClient = orchestratorClient;

        }

        /// <summary>
        /// Environment variable carrying the inheritable anonymous-pipe client handle the external
        /// launcher (e.g. AdoBuildRunner) uses to signal this worker that the orchestrator is gone.
        /// CODESYNC: Private/AdoBuildRunner/src/Constants.cs (OrchestratorTerminationPipeEnvVar).
        /// Duplicated here because the engine assembly does not reference AdoBuildRunner.
        /// </summary>
        private const string OrchestratorTerminationPipeEnvVar = "BUILDXL_ORCH_TERMINATION_PIPE_HANDLE";

        /// <summary>
        /// If <see cref="OrchestratorTerminationPipeEnvVar"/> is set, opens the inherited pipe and starts a
        /// background task that waits for a signal byte. Only an explicit byte -- written by the runner when
        /// its monitor observes the orchestrator job Failed/Canceled -- sets <see cref="OrchestratorAbandoned"/>
        /// and exits gracefully through the same path used when the orchestrator sends an exit RPC:
        ///  - Pre-attach: <see cref="WaitForOrchestratorAttach"/> returns <see cref="AttachResult.Released"/>
        ///    and the engine completes with <c>SuccessNotRun</c> (exit code 0).
        ///  - Post-attach: behaves like a clean orchestrator-driven exit; <see cref="Exit"/> is idempotent.
        ///
        /// EOF (the runner closed the pipe or died) is intentionally NOT treated as a termination signal:
        /// it does not reveal the orchestrator outcome (which may be success), so the worker takes no action
        /// and relies on the normal gRPC heartbeat / attach-timeout paths instead.
        ///
        /// Cancelled via <see cref="m_cancellationOnExit"/> when the build finishes naturally.
        ///
        /// CODESYNC: Private/AdoBuildRunner/src/Build/WorkerBuildExecutor.cs (pipe creator).
        /// </summary>
        private void StartOrchestratorTerminationWatcher()
        {
            var pipeHandle = Environment.GetEnvironmentVariable(OrchestratorTerminationPipeEnvVar);
            if (string.IsNullOrEmpty(pipeHandle))
            {
                return;
            }

            AnonymousPipeClientStream pipe;
            try
            {
                pipe = new AnonymousPipeClientStream(PipeDirection.In, pipeHandle);
            }
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
            catch (Exception ex)
            {
                Logger.Log.DistributionDebugMessage(
                    m_appLoggingContext,
                    $"Could not open orchestrator-termination pipe (handle '{pipeHandle}'): {ex}. The worker will not be notified externally of orchestrator termination.");
                return;
            }
#pragma warning restore ERP022

            // Fire-and-forget: best-effort background channel; failures inside it never affect the build.
            _ = Task.Run(async () =>
            {
                try
                {
                    using (pipe)
                    {
                        var buffer = new byte[1];
                        int read;
                        try
                        {
                            // Returns > 0 on a signal byte, 0 on EOF (runner closed the pipe or died).
                            read = await pipe.ReadAsync(buffer.AsMemory(0, 1), m_cancellationOnExit.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            return;
                        }

                        if (read == 0)
                        {
                            // EOF means the runner closed the pipe / died, but it does NOT tell us why.
                            // The orchestrator may still be running -- or may even have SUCCEEDED. Tearing
                            // down the worker on this ambiguous signal could abandon a perfectly healthy
                            // build, so we deliberately take NO action and let the normal gRPC heartbeat /
                            // attach-timeout paths decide. Only the explicit signal byte -- written solely
                            // when the runner's monitor observes the orchestrator job Failed/Canceled --
                            // triggers a worker exit.
                            Logger.Log.DistributionDebugMessage(
                                m_appLoggingContext,
                                "Orchestrator-termination pipe reached EOF without a signal byte (runner closed the pipe); taking no action because the orchestrator outcome is unknown.");
                            return;
                        }

                        // Explicit signal byte: the runner confirmed the orchestrator is Failed/Canceled.
                        // Set the flag BEFORE ExitRequested so every blocking site inside Exit() sees it.
                        SetOrchestratorAbandoned();

                        Logger.Log.DistributionWorkerExternalTerminationSignalReceived(m_appLoggingContext, "byte-signal from runner");
                        ((IWorkerService)this).ExitRequested(
                            "External termination signal received via orchestrator-termination pipe (byte-signal from runner).",
                            Optional<string>.Empty);
                    }
                }
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
                catch (Exception ex)
                {
                    try
                    {
                        Logger.Log.DistributionDebugMessage(
                            m_appLoggingContext,
                            $"Orchestrator-termination pipe watcher failed: {ex}");
                    }
                    catch { }
                }
#pragma warning restore ERP022
            });
        }

        internal void Start(EngineSchedule schedule, ExecutionResultSerializer resultSerializer)
        {
            Contract.Assert(AttachCallTask.IsCompleted && AttachCallTask.GetAwaiter().GetResult() == AttachResult.Attached, "Start called before finishing attach on worker");
            m_executionLogTarget = schedule?.Scheduler.ExecutionLog;
            m_orchestratorClient.SetupPerfDataInHeartbeats(schedule?.Scheduler.PerformanceAggregator, WorkerId);
            m_pipExecutionService.Start(schedule, BuildStartData);
            m_notificationManager.Start(m_orchestratorClient, schedule, new PipResultSerializer(resultSerializer), m_config.Logging);
        }

        /// <summary>
        /// Connects to the orchestrator and enables this node to receive build requests
        /// </summary>
        internal async Task<bool> WhenDoneAsync()
        {
            Contract.Assert(AttachCallTask.IsCompleted && AttachCallTask.GetAwaiter().GetResult() != AttachResult.Failed, "ProcessBuildRequests called before finishing attach on worker");

            bool success = await CompleteAttachmentAfterProcessBuildRequestStartedAsync();

            // Wait until the build finishes or we discovered that the orchestrator is dead
            success &= await ExitCompletion;

            success &= !m_hasFailures;
            if (m_failureMessage != null)
            {
                Logger.Log.DistributionWorkerExitFailure(m_appLoggingContext, m_failureMessage);
            }


            if (success && !PendingScheduleRequests.IsEmpty)
            {
                // We might meet this condition on builds with MaterializeOutputs steps: if the orchestrator calls Exit on a worker
                // after fire-forgetting all the materializations, we might still be processing materialization requests and in a stage
                // where they haven't reached the pip queue.
                // We should avoid marking the pip queue as complete if this is the case, so let's wait for everything to be queued
                // before doing that.
                while (!PendingScheduleRequests.IsEmpty)
                {
                    await Task.Delay(5_000);
                }
            }

            m_pipExecutionService.WhenDone();

            return success;
        }

        internal bool SayHello(IDistributionServiceLocation orchestratorLocation)
        {
            OrchestratorIpAddress = orchestratorLocation.IpAddress;
            var timeout = GrpcSettings.WorkerAttachTimeout;
            Logger.Log.DistributionSayingHelloToOrchestrator(m_appLoggingContext);
            var helloTask = ((IWorkerService)this).SayHelloAsync(orchestratorLocation);
            if (!helloTask.Wait(timeout))
            {
                Logger.Log.DistributionWorkerTimeoutFailure(m_appLoggingContext, "trying to say hello to the orchestrator");
                Exit(failure: $"Timed out saying hello to the orchestrator. Timeout: {timeout.TotalMinutes} min", isUnexpected: true);
                return false;
            }
            else if(!helloTask.GetAwaiter().GetResult())
            {
                // Hello failed - Exit has been called already
                Logger.Log.DistributionWorkerExitFailure(m_appLoggingContext, "Hello call to orchestrator failed");
                return false;
            }

            return true;
        }
        
        /// <summary>
        /// The result of the attachment procedure. 
        /// The worker might be released before attachment: this is not a failed state, but we also can't proceed as if attached
        /// </summary>
        public enum AttachResult
        {
            /// <nodoc />
            Attached = 0,

            /// <nodoc />
            Released = 1,

            /// <nodoc />
            Failed = 2
        }

        /// <summary>
        /// Waits for the orchestrator to attach synchronously
        /// </summary>
        internal AttachResult WaitForOrchestratorAttach()
        {
            var timeout = GrpcSettings.WorkerAttachTimeout;
            var sw = Stopwatch.StartNew();

            if (!AttachCallTask.IsCompleted)
            {
                Logger.Log.DistributionWaitingForOrchestratorAttached(m_appLoggingContext);
                if (!AttachCallTask.Wait(timeout))
                {
                    Logger.Log.DistributionWorkerTimeoutFailure(m_appLoggingContext, "waiting for attach request from orchestrator");
                    Exit(failure: $"Timed out waiting for attach request from orchestrator. Timeout: {timeout.TotalMinutes} min", isUnexpected: true);
                    return AttachResult.Failed;
                }
            }

            var attachResult = AttachCallTask.Result;

            if (AttachCallTask.Result == AttachResult.Failed)
            {
                if (m_isOrchestratorExited)
                {
                    // The orchestrator can send an Exit request before attachment if early releasing this worker
                    // This is a corner case, as the orchestrator waits for the attachment to complete before doing this.
                    // We treat this as an early-release
                    Logger.Log.DistributionOrchestratorExitBeforeAttachment(m_appLoggingContext, (int)sw.Elapsed.TotalMilliseconds);
                    return AttachResult.Released;
                }
                else
                {
                    Logger.Log.DistributionInactiveOrchestrator(m_appLoggingContext, (int)timeout.TotalMinutes);
                    return AttachResult.Failed;
                }
            }

            return attachResult;
        }

        /// <nodoc/>
        void IWorkerService.ExitRequested(string exitMessage, Optional<string> failure)
        {
            m_isOrchestratorExited = true;
            Logger.Log.DistributionExitReceived(m_appLoggingContext, exitMessage);
            Exit(failure);
        }

        /// <nodoc/>
        public long[] RetrieveWorkerEventStats()
        {
            if (m_executionLogTarget != null)
            {
                var logTargets = ((MultiExecutionLogTarget)m_executionLogTarget).LogTargets;
                var eventStatsLogTarget = logTargets.Where(target => target.GetType().Equals(typeof(EventStatsExecutionLogTarget))).FirstOrDefault();
                return ((EventStatsExecutionLogTarget)eventStatsLogTarget)?.EventCounts;
            }

            return Array.Empty<long>();
        }

        /// <nodoc/>
        public bool Exit(Optional<string> failure = default, bool isUnexpected = false)
        {
            if (m_exitStarted)
            {
                // We already initiated the stop for the worker.
                return false;
            }

            m_exitStarted = true;
            m_cancellationOnExit.Cancel();
            var reportSuccess = !failure.HasValue;

            m_notificationManager?.Exit(isClean: reportSuccess && !isUnexpected);

            if (OrchestratorAbandoned)
            {
                // CloseAsync has a synchronous prefix (heartbeat join, channel dispose) that can block for
                // minutes against a dead peer. Push it to the threadpool so Exit returns promptly.
                _ = Task.Run(() => m_orchestratorClient?.CloseAsync());
            }
            else
            {
                // Orchestrator healthy or status unknown: wait so any final teardown RPC is delivered.
                m_orchestratorClient?.CloseAsync().GetAwaiter().GetResult();
            }

            m_attachCallCompletion.TrySetResult(AttachResult.Failed);

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

            m_exitCompletion.TrySetResult(reportSuccess);
            return true;
        }

        /// <summary>
        /// Retrieves the cache descriptor containing the build scheduler state from the coordinator node
        /// </summary>
        /// <returns>true if the operation was successful</returns>
        internal bool TryGetBuildScheduleDescriptor(out PipGraphCacheDescriptor descriptor)
        {
            descriptor = null;
            if (AttachCallTask.Result != AttachResult.Attached)
            {
                return false;
            }

            descriptor = BuildStartData.CachedGraphDescriptor.ToCacheGrpc();
            return true;
        }


        async Task<bool> IWorkerService.SayHelloAsync(IDistributionServiceLocation orchestratorLocation)
        {
            m_orchestratorInitialized = true;
            m_orchestratorClient.Initialize(orchestratorLocation.IpAddress, orchestratorLocation.BuildServicePort, OnConnectionFailureAsync);

            var helloRequest = new HelloRequest()
            {
                Location = new ServiceLocation() { IpAddress = m_config.Distribution.MachineHostName, Port = m_port }
            };


            if (m_config.Infra == Infra.Ado && int.TryParse(Environment.GetEnvironmentVariable("SYSTEM_JOBPOSITIONINPHASE"), out int workerOrdinal))
            {
                // We are a worker in an ADO build. We should align the worker id with the 'job position' for UI purposes
                // This environment variable should always be defined for parallel workers: https://learn.microsoft.com/en-us/azure/devops/pipelines/process/phases?view=azure-devops&tabs=yaml#slicing
                helloRequest.RequestedId = workerOrdinal;
            }

            var helloResult = await m_orchestratorClient.SayHelloAsync(helloRequest, m_cancellationOnExit.Token);
            if (!helloResult.Succeeded)
            {
                // If the runner already signaled the orchestrator is gone, the gRPC failure is expected:
                // the watcher has called Exit() and the engine should take the early-released
                // (SuccessNotRun) path. Return true so attach proceeds to AttachResult.Released.
                if (OrchestratorAbandoned)
                {
                    return true;
                }

                // If we can't say hello there is no hope for attachment
                Exit(failure: $"SayHello call failed. Details: {helloResult.Failure.Describe()}", isUnexpected: true);
                return false;
            }

            switch (helloResult.Result)
            {
                case HelloResponseType.Ok:
					// Nothing to do, the orchestrator will attach later.
					break;

				// The orchestrator sends a refusal, either when the worker has been early released
				// before it had a chance to say Hello, or that there are no slots available for this worker.
				// We should exit gracefully in these situations - this is basically the orchestrator refusing
				// attachment, so we treat it the same as an "Exit" call, and we include the reason.
				case HelloResponseType.Released:
                    m_attachCallCompletion.TrySetResult(AttachResult.Released);
                    exit("Worker was early released before initiating attachment");
                    break;
    
				case HelloResponseType.NoSlots:
                    exit("Orchestrator had no slots to allocate to this dynamic worker");
					break;
            }

            return true;
            void exit(string reason)
            {
                var thisService = (IWorkerService)this;
                thisService.ExitRequested(reason, Optional<string>.Empty);
            }

        }

        /// <inheritdoc />
        void IWorkerService.Attach(BuildStartData buildStartData, string orchestratorName)
        {
            Logger.Log.DistributionAttachReceived(m_appLoggingContext, buildStartData.SessionId, orchestratorName);
            BuildStartData = buildStartData;

            Guid sessionIdFromAttach = Guid.Parse(buildStartData.SessionId);

            // The app-level logging context has a wrong session id. Fix it now that we know the right one.
            m_appLoggingContext = new LoggingContext(
                m_appLoggingContext.ActivityId,
                m_appLoggingContext.LoggerComponentInfo,
                new LoggingContext.SessionInfo(sessionIdFromAttach, m_appLoggingContext.Session.Environment, m_appLoggingContext.Session.RelatedActivityId),
                m_appLoggingContext);

            if (!m_orchestratorInitialized) // The orchestrator client is already initialized if the worker know its location at the start of the build
            {
                m_orchestratorClient.Initialize(buildStartData.OrchestratorLocation.IpAddress, buildStartData.OrchestratorLocation.Port, OnConnectionFailureAsync);
            }

            OrchestratorIpAddress = buildStartData.OrchestratorLocation.IpAddress;
            WorkerId = BuildStartData.WorkerId;
            m_attachCallCompletion.TrySetResult(AttachResult.Attached);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer02:MissingAsyncOpportunity")]
        private async Task<bool> CompleteAttachmentAfterProcessBuildRequestStartedAsync()
        {
            var attachCompletionInfo = m_pipExecutionService.ConstructAttachCompletionInfo();
            var attachCompletionResult = await m_orchestratorClient.AttachCompletedAsync(attachCompletionInfo, m_cancellationOnExit.Token);

            if (!attachCompletionResult.Succeeded)
            {
                var callDurationMin = (int)attachCompletionResult.Duration.TotalMinutes;

                if (m_isOrchestratorExited)
                {
                    // We failed to attach after receiving an exit call from the orchestrator.
                    // Don't treat this as a failure: it is a part of the normal early-release process,
                    // where the orchestrator shuts down the connection after releasing this worker.
                    // Log the ocurrence and exit gracefully.
                    Logger.Log.AttachmentFailureAfterOrchestratorExit(m_appLoggingContext, callDurationMin);

                    Exit();
                    return true;
                }
                else
                {
                    Exit($"Failed to attach to orchestrator. Duration: {callDurationMin}", isUnexpected: true);
                    return false;
                }
            }

            return true;
        }

        private async void OnConnectionFailureAsync(object sender, ConnectionFailureEventArgs e)
        {
            if (Interlocked.Increment(ref m_connectionClosedFlag) != 1)
            {
                // Only go through the failure logic once
                return;
            }

            if (OrchestratorAbandoned)
            {
                // The runner already signaled the orchestrator is gone and the watcher has driven a clean
                // shutdown via Exit(). The gRPC failure is the expected consequence and is not a worker
                // failure. Logging an error here would set HasFailures on the TrackingEventListener and
                // force exit code 1, defeating the SuccessNotRun (early-release) path.
                return;
            }

            e?.Log(m_appLoggingContext, machineName: "orchestrator");

            // Stop sending messages
            m_notificationManager.Cancel();
           
            // Unblock caller to make it a fire&forget event handler.
            await Task.Yield();

            if (await ExitAsync("Connection failure", isUnexpected: true))
            {
                // Log an error only if there was no exit call before.
                Logger.Log.DistributionInactiveOrchestrator(m_appLoggingContext, (int)(GrpcSettings.CallTimeout.TotalMinutes * GrpcSettings.MaxAttempts));
            }
        }

        /// <inheritdoc />
        async Task IWorkerService.ExecutePipsAsync(PipBuildRequest request)
        {
            Possible<Unit> reportInputsResult;

            using (Counters.StartStopwatch(DistributionCounter.ReportInputsDuration))
            {
                reportInputsResult = m_pipExecutionService.TryReportInputs(request.Hashes);
            }

            // We want to unblock the caller to process this asynchronously except on builds where we might get
            // fire-and-forget, where we need to make sure to add them to the 'pending' collections before unblocking
            // an orchestrator that might quicky call exit on this worker before we finish that processing.
            // This race, though presumably unlikely, would be silent and hard to diagnose, so we'll pay the extra cost.
            if (!m_config.Distribution.ReplicateOutputsToWorkers())
            {
                await Task.Yield();
            }
            
            using (Counters.StartStopwatch(DistributionCounter.StartPipStepDuration))
            {
                for (int i = 0; i < request.Pips.Count; i++)
                {
                    SinglePipBuildRequest pipBuildRequest = request.Pips[i];

                    // Start the pip. Handle the case of a retry - the pip may be already started by a previous call.
                    if (m_handledBuildRequests.Add(pipBuildRequest.SequenceNumber))
                    {
                        var pipId = new PipId(pipBuildRequest.PipIdValue);
                        var pipIdStepTuple = (pipId, (PipExecutionStep)pipBuildRequest.Step);

                        PendingScheduleRequests[pipIdStepTuple] = true;
                        m_pendingBuildRequests[pipIdStepTuple] = pipBuildRequest;
                        
                        var pipCompletionData = new ExtendedPipCompletionData(new PipCompletionData() { PipIdValue = pipId.Value, Step = pipBuildRequest.Step });
                        m_pendingPipCompletions[pipIdStepTuple] = pipCompletionData;

                        m_pipExecutionService.StartPipStepAsync(pipId, pipCompletionData, pipBuildRequest, reportInputsResult).Forget();
                    }
                }
            }
        }

        internal void ReportResult(
            PipId pipId,
            ExecutionResult executionResult,
            PipExecutionStep step)
        {
            if (m_pendingPipCompletions.TryRemove((pipId, step), out var pipCompletion))
            {
                if (executionResult.Result == PipResultStatus.Failed)
                {
                    m_hasFailures = true;
                }

                if (step == PipExecutionStep.MaterializeOutputs)
                {
                    // We do not report 'MaterializeOutput' step results back to orchestrator.
                    return;
                }

                pipCompletion.ExecutionResult = executionResult;
                m_notificationManager.ReportResult(pipCompletion);
            }
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            LogStatistics(m_appLoggingContext);
            m_workerServer.Dispose();
        }

        /// <nodoc/>
        public override async Task<bool> ExitAsync(Optional<string> failure, bool isUnexpected)
        {
            await Task.Yield();
            return Exit(failure, isUnexpected);
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
                Logger.Log.DistributionServiceInitializationError(m_appLoggingContext, DistributedBuildRole.Worker.ToString(), m_port, ex.ToStringDemystified());
                return false;
            }
        }
    }
}
