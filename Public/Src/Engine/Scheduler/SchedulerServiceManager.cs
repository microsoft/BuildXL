// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Scheduler
{
    /// <summary>
    ///     Responsible for keeping track of started services.  Provides methods for starting
    ///     and shutting down service pips on demand (based on pip information in given pip graph).
    /// </summary>
    /// <remarks>
    ///     In a distributed build, each worker has its own instance of <see cref="SchedulerServiceManager"/>,
    ///     i.e., each worker keeps track of their own services.
    /// </remarks>
    internal sealed class SchedulerServiceManager : ServiceManager
    {
        private readonly ConcurrentBigMap<PipId, ServiceTracking> m_startedServices = new ConcurrentBigMap<PipId, ServiceTracking>();
        private readonly Dictionary<PipId, PipId> m_finalizationPipToServicePipMap = new Dictionary<PipId, PipId>();
        private readonly ConcurrentDictionary<int, TaskSourceSlim<bool>> m_serviceReadiness = new();

        private readonly PipGraph m_pipGraph;
        private readonly PipExecutionContext m_context;
        private readonly ServicePipTracker m_servicePipTracker;
        private readonly SchedulerTestHooks m_testHooks;
        private LoggingContext m_executePhaseLoggingContext;
        private OperationTracker m_operationTracker;
        private int m_runningServicesCount;
        private int m_totalServicePipsCompleted;
        private int m_totalServiceShutdownPipsCompleted;

        /// <summary>Whether <see cref="Start"/> has already been called.</summary>
        public bool IsStarted { get; private set; }

        /// <summary>Whether <see cref="ShutdownStartedServices"/> has already been called.</summary>
        public bool ShutdownStarted { get; private set; }

        /// <summary>The count of running services</summary>
        public int RunningServicesCount => m_runningServicesCount;

        /// <summary>Total number of service pips that were run (which either succeeded or failed).</summary>
        public int TotalServicePipsCompleted => m_totalServicePipsCompleted;

        /// <summary>Total number of service shutdown pips that were run (which either succeeded or failed).</summary>
        public int TotalServiceShutdownPipsCompleted => m_totalServiceShutdownPipsCompleted;

        /// <nodoc />
        public SchedulerServiceManager(PipGraph pipGraph, PipExecutionContext context, ServicePipTracker pipTracker, SchedulerTestHooks testHooks)
        {
            m_pipGraph = pipGraph;
            m_context = context;
            m_servicePipTracker = pipTracker;
            m_testHooks = testHooks;
        }

        internal void Start(LoggingContext loggingContext, OperationTracker operationTracker)
        {
            Contract.Requires(!IsStarted);

            m_executePhaseLoggingContext = loggingContext;
            m_operationTracker = operationTracker;

            foreach (var servicePipId in m_pipGraph.GetServicePipIds())
            {
                var serviceMutable = (ProcessMutablePipState)m_pipGraph.PipTable.GetMutable(servicePipId);
                foreach (var finalizationPipId in serviceMutable.ServiceInfo.FinalizationPipIds)
                {
                    m_finalizationPipToServicePipMap[finalizationPipId] = servicePipId;
                }
            }

            IsStarted = true;
        }

        /// <inheritdoc />
        public override bool HasRealConsumers(IpcPip pip)
        {
            foreach (var outgoingEdge in m_pipGraph.DataflowGraph.GetOutgoingEdges(pip.PipId.ToNodeId()))
            {
                var otherPipId = outgoingEdge.OtherNode.ToPipId();
                if (m_pipGraph.PipTable.GetServiceInfo(otherPipId).Kind == ServicePipKind.None)
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public override async Task<bool> TryRunServiceDependenciesAsync(
            IPipExecutionEnvironment environment,
            PipId pipId,
            IEnumerable<PipId> servicePips,
            LoggingContext loggingContext)
        {
            if (m_finalizationPipToServicePipMap.TryGetValue(pipId, out var serviceForFinalizationPip))
            {
                servicePips = servicePips.Concat(new[] { serviceForFinalizationPip });
            }
            
            if (!servicePips.Any())
            {
                return true;
            }

            var results = await Task.WhenAll(
                servicePips.Select(servicePipId => EnsureServiceRunningAsync(servicePipId, environment)));

            return results.All(succeeded => succeeded);
        }

        public override void ReportServiceIsReady(int processId, string processName)
        {
            Logger.Log.ScheduleServicePipReportedReady(m_executePhaseLoggingContext, processId, processName);
            GetServiceReadyCompletionByProcessId(processId).TrySetResult(true);
        }

        /// <summary>
        /// For all service pips found in <see cref="m_startedServices"/> executes their shutdown pips
        /// using the same executor/environment that was used to start the service.
        /// </summary>
        internal async Task<bool> ShutdownStartedServices()
        {
            Contract.Requires(!ShutdownStarted, "Shutdown should only be called once on service manager");
            ShutdownStarted = true;

            var shutdownTasks = m_startedServices.Select(async serviceEntry =>
            {
                var pipId = serviceEntry.Key;
                var serviceTracking = serviceEntry.Value;

                // Notify anything waiting for startup completion that the service was not started successfully
                // This call will no-op if startup completion has already been signaled
                serviceTracking.StartupCompletion.TrySetResult(false);

                var serviceProcess = HydrateServiceStartOrShutdownProcess(pipId);
                var shutdownProcess = HydrateServiceStartOrShutdownProcess(serviceProcess.ShutdownProcessPipId);
                var servicePipDescription = serviceProcess.GetDescription(m_context);
                var shutdownPipDescription = shutdownProcess.GetDescription(m_context);

                var loggingContext = m_executePhaseLoggingContext;
                Logger.Log.ScheduleServicePipShuttingDown(
                    loggingContext,
                    servicePipDescription,
                    shutdownPipDescription);

                var serviceShutdownResult = await StartServiceOrShutdownServiceAsync(
                    serviceTracking.Environment,
                    shutdownProcess.PipId,
                    serviceTracking.ShutdownLaunchCompletion,
                    isStartup: false);
                Interlocked.Increment(ref m_totalServiceShutdownPipsCompleted);

                var serviceExecutionResult = await serviceTracking.ServiceExecutionCompletion;
                Interlocked.Increment(ref m_totalServicePipsCompleted);

                Interlocked.Decrement(ref m_runningServicesCount);

                var shutdownFailed = serviceShutdownResult.Status.IndicatesFailure();
                var serviceFailed = serviceExecutionResult.Status.IndicatesFailure();
                if (shutdownFailed)
                {
                    Logger.Log.ScheduleServicePipShuttingDownFailed(loggingContext, servicePipDescription, shutdownPipDescription);
                }

                if (serviceFailed)
                {
                    Logger.Log.ScheduleServicePipFailed(loggingContext, servicePipDescription);
                }

                return !shutdownFailed && !serviceFailed;
            }).ToList();

            return (await Task.WhenAll(shutdownTasks)).All(b => b);
        }

        /// <summary>
        ///     Checks if the <paramref name="servicePipId"/> has already been started for the requested workerHost/workerId.
        ///     If it hasn't, executes the requested service pip in a given <paramref name="environment"/>.
        /// </summary>
        /// <returns>
        ///     <code>false</code> if the requested service has already failed and <code>true</code> otherwise.
        /// </returns>
        private Task<bool> EnsureServiceRunningAsync(PipId servicePipId, IPipExecutionEnvironment environment)
        {
            Contract.Requires(environment != null);

            ServiceTracking result = m_startedServices.GetOrAdd(
                key: servicePipId,
                data: this,
                addValueFactory: (key, scheduler) =>
                {
                    var serviceStartupCompletion = TaskSourceSlim.Create<bool>();
                    Task<PipResult> serviceCompletion = StartServiceOrShutdownServiceAsync(
                        environment,
                        servicePipId,
                        serviceStartupCompletion,
                        isStartup: true);

                    return new ServiceTracking(environment, serviceStartupCompletion, serviceCompletion);
                }).Item.Value;

            return result.WaitForProcessStartAsync();
        }

        private async Task<PipResult> StartServiceOrShutdownServiceAsync(
            IPipExecutionEnvironment environment,
            PipId pipId,
            TaskSourceSlim<bool> serviceLaunchCompletion,
            bool isStartup)
        {
            // Ensure task does not block current thread
            await Task.Yield();

            var loggingContext = m_executePhaseLoggingContext;
            try
            {
                var serviceProcess = HydrateServiceStartOrShutdownProcess(pipId);
                string pipDescription = null;
                if (isStartup)
                {
                    pipDescription = serviceProcess.GetDescription(m_context);
                    Logger.Log.ScheduleServicePipStarting(
                        loggingContext,
                        pipDescription);

                    Interlocked.Increment(ref m_runningServicesCount);

                    // if a service has a trackable tag, add it to the tracker
                    if (serviceProcess.ServiceInfo.TagToTrack.IsValid)
                    {
                        m_servicePipTracker.ReportServicePipStarted(serviceProcess.ServiceInfo);
                    }
                }

                using (var operationContext = m_operationTracker.StartOperation(
                    isStartup
                        ? PipExecutorCounter.ExecuteServiceDuration
                        : PipExecutorCounter.ExecuteServiceShutdownDuration,
                    pipId,
                    PipType.Process,
                    loggingContext))
                {
                    var serviceStartTask = PipExecutor.ExecuteServiceStartOrShutdownAsync(
                        // Bug #1155822: There is a race condition where service start/shutdown can
                        // cause crash in the operation tracker because the parent operation is already completed
                        // this is not fully understood, but the tracking of details of services operations is not
                        // important so this disables it
                        OperationContext.CreateUntracked(loggingContext),
                        environment,
                        serviceProcess,
                        processId =>
                        {
                            if (processId < 0)
                            {
                                // processIdListener is called twice: 'processId' after the process has started, and
                                // '-processId' after the process has exited. Here we are only interested in the first call.
                                return;
                            }

                            if (isStartup)
                            {
                                // It's a startup of a service, so we need to wait for a call from that service before setting serviceLaunchCompletion.
                                GetServiceReadyTaskByProcessId(pipDescription, processId).ContinueWith(tsk =>
                                {
                                    // tsk is an underlying task of TaskCompletionSource. The completion represents whether the
                                    // service pip process made a callback to the engine to notify that the service is ready.
                                    // Continuation is executed when tsk is completed; be design, it's never faulted or cancelled,
                                    // so it's safe to get the result of tsk.
                                    var serviceStatus = tsk.GetAwaiter().GetResult();
                                    serviceLaunchCompletion.TrySetResult(serviceStatus);
                                });
                            }
                            else
                            {
                                serviceLaunchCompletion.TrySetResult(isStartup);
                            }
                        });

                    using (
                        operationContext.StartAsyncOperation(
                            isStartup
                                ? PipExecutorCounter.ExecuteServiceStartupLaunchDuration
                                : PipExecutorCounter.ExecuteServiceShutdownLaunchDuration))
                    {
                        Analysis.IgnoreResult(
                            await Task.WhenAny(serviceLaunchCompletion.Task, serviceStartTask),
                            justification: "Task<Task> doesn't contain any data."
                        );
                    }

                    var result = await serviceStartTask;
                    return PipResult.CreateWithPointPerformanceInfo(result.Result);
                }
            }
            finally
            {
                // Attempt to set the result to false to indicate service startup failure.
                // This will not succeed if the service is already started
                // and the result is set to true.
                if (serviceLaunchCompletion.TrySetResult(false))
                {
                    var serviceProcess = HydrateServiceStartOrShutdownProcess(pipId);
                    Logger.Log.ScheduleServiceTerminatedBeforeStartupWasSignaled(
                        loggingContext,
                        serviceProcess.GetDescription(m_context));
                }
            }
        }

        private Process HydrateServiceStartOrShutdownProcess(PipId servicePipId)
        {
            var pip = m_pipGraph.PipTable.HydratePip(servicePipId, PipQueryContext.SchedulerExecutePips);
            Contract.Assert(pip.PipType == PipType.Process);
            var process = (Process)pip;
            Contract.Assert(process.ServiceInfo.IsStartOrShutdownKind);
            return process;
        }


        private TaskSourceSlim<bool> GetServiceReadyCompletionByProcessId(int pid)
        {
            return m_serviceReadiness.GetOrAdd(
                pid,
                TaskSourceSlim.Create<bool>());
        }

        private Task<bool> GetServiceReadyTaskByProcessId(string servicePipDescription, int processId)
        {
            Logger.Log.ScheduleServicePipProcessStartedButNotReady(m_executePhaseLoggingContext, servicePipDescription, processId);

            if (m_testHooks != null && m_testHooks.ServicePipReportedReady != null)
            {
                return Task.FromResult(m_testHooks.ServicePipReportedReady.Value);
            }

            var serviceReadyCompletion = GetServiceReadyCompletionByProcessId(processId);
            // There was some weird slowness in ApiServer in processing the callback,
            // so we decided to go with a generous timeout value.
            serviceReadyCompletion.Task.WithTimeoutAsync(TimeSpan.FromHours(3)).ContinueWith(tsk =>
            {
                // If tsk is not faulted, it means that a task returned by WithTimeoutAsync completed successfully, i.e., the completion
                // result was set and no further action is needed.
                if (tsk.Exception != null)
                {
                    // Timeout (task tsk can get into a faulted state only because of a TimeoutException inside WithTimeoutAsync()).
                    // (need to check Exception instead of IsFaulted to avoid TaskUnobservedException)
                    serviceReadyCompletion.TrySetResult(false);
                }
            });

            // A shorter timeout is used here to track delayed callbacks without failing the builds.
            serviceReadyCompletion.Task.WithTimeoutAsync(TimeSpan.FromMinutes(30)).ContinueWith(tsk =>
            {
                if (tsk.Exception != null)
                {
                    Logger.Log.ScheduleServicePipSlowInitialization(m_executePhaseLoggingContext, servicePipDescription);
                }
            });

            return serviceReadyCompletion.Task;
        }

        /// <summary>
        /// A helper class for keeping track of running service tasks.
        /// </summary>
        private sealed class ServiceTracking
        {
            /// <summary>
            /// The captured execution environment for starting the service. This is needed because the shutdown must use the same
            /// execution environment. Namely, distribution requires the WorkerPipExecutionEnvironment to ensure files are materialized
            /// appropriately so using the scheduler which initializes the service manager is not sufficient.
            /// </summary>
            public readonly IPipExecutionEnvironment Environment;

            /// <summary>Completes when the process starts.</summary>
            public readonly TaskSourceSlim<bool> StartupCompletion;

            /// <summary>Completes when the shutdown process is launch.</summary>
            public readonly TaskSourceSlim<bool> ShutdownLaunchCompletion = TaskSourceSlim.Create<bool>();

            /// <summary>Completes when the process exits.</summary>
            public readonly Task<PipResult> ServiceExecutionCompletion;

            /// <nodoc />
            public ServiceTracking(IPipExecutionEnvironment environment, TaskSourceSlim<bool> startupCompletion, Task<PipResult> serviceCompletion)
            {
                Environment = environment;
                StartupCompletion = startupCompletion;
                ServiceExecutionCompletion = serviceCompletion;
            }

            /// <nodoc />
            public Task<bool> WaitForProcessStartAsync()
            {
                return StartupCompletion.Task;
            }
        }
    }
}
