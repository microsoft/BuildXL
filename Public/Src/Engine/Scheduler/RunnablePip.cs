// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Distribution;
using BuildXL.Scheduler.Tracing;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Storage;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.FormattableStringEx;
using BuildXL.Native.IO;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Execution state that is carried between pip execution steps.
    /// </summary>
    public class RunnablePip
    {
        /// <summary>
        /// PipId
        /// </summary>
        public PipId PipId { get; }

        /// <summary>
        /// The operation scope for the active operation for the runnable pip
        /// </summary>
        public OperationContext OperationContext { get; private set; }

        /// <summary>
        /// Gets the runnable pip observer
        /// </summary>
        public RunnablePipObserver Observer { get; private set; } = RunnablePipObserver.Default;

        /// <summary>
        /// Sequence number used to track changes in ChooseWorker state as indication that ChooseWorker should be
        /// paused/unpaused based on whether workers are possibly available
        /// </summary>
        public int ChooseWorkerSequenceNumber { get; set; }

        /// <summary>
        /// Pip type
        /// </summary>
        public PipType PipType { get; }

        /// <summary>
        /// Priority
        /// </summary>
        public int Priority { get; private set; }

        /// <summary>
        /// Execution environment
        /// </summary>
        public IPipExecutionEnvironment Environment { get; }

        /// <summary>
        /// The underlying pip
        /// </summary>
        public Pip Pip
        {
            get
            {
                if (m_pip == null)
                {
                    m_pip = Environment.PipTable.HydratePip(PipId, PipQueryContext.RunnablePip);
                }

                return m_pip;
            }
        }

        private Pip m_pip;

        /// <summary>
        /// Pip description
        /// </summary>
        public string Description
        {
            get
            {
                if (m_description == null)
                {
                    m_description = Pip.GetDescription(Environment.Context);
                }

                return m_description;
            }
        }

        private string m_description;

        /// <summary>
        /// Whether the pip is set as cancelled due to 'StopOnFirstFailure'.
        /// </summary>
        public bool IsCancelled { get; private set; }

        /// <summary>
        /// Logging context
        /// </summary>
        /// <remarks>
        /// Initially when the RunnablePip is constructed the loggingcontext will be a generic phase level context. when
        /// the pip actually starts, a pip specific context is created. This way we can ensure there is always an associated
        /// LoggingContext no matter what the state of the pip is.</remarks>
        public LoggingContext LoggingContext => OperationContext.LoggingContext;

        /// <summary>
        /// Time when the pip is started executing
        /// </summary>
        public DateTime StartTime { get; private set; }

        /// <summary>
        /// Time when the pip is scheduled
        /// </summary>
        public DateTime ScheduleTime { get; private set; }

        /// <summary>
        /// The time spent running for the pip (not queued)
        /// </summary>
        public TimeSpan RunningTime { get; private set; }

        /// <summary>
        /// Pip result
        /// </summary>
        public PipResult? Result { get; private set; }

        /// <summary>
        /// Pip execution result
        /// </summary>
        public ExecutionResult ExecutionResult { get; private set; }

        /// <summary>
        /// The current pip execution step
        /// </summary>
        public PipExecutionStep Step { get; private set; }

        /// <summary>
        /// The current dispatcher
        /// </summary>
        internal DispatcherKind DispatcherKind { get; private set; }

        /// <summary>
        /// Worker which executes this pip
        /// </summary>
        public Worker Worker { get; private set; }

        /// <summary>
        /// Worker which executes this pip. This field is only valid after acquiring worker resources and before releasing resources.
        /// NOTE: This is different than <see cref="Worker"/> which is set for steps that don't acquire resources.
        /// </summary>
        public Worker AcquiredResourceWorker { get; internal set; }

        /// <summary>
        /// Gets whether the machine represents a distributed worker
        /// </summary>
        private bool IsDistributedWorker => Environment.Configuration.Distribution.BuildRole == DistributedBuildRoles.Worker;

        private readonly Func<RunnablePip, Task> m_executionFunc;

        private DispatcherReleaser m_dispatcherReleaser;

        internal RunnablePipPerformanceInfo Performance { get; }

        /// <summary>
        /// Whether waiting on resources (worker).
        /// </summary>
        public bool IsWaitingForWorker { get; set; }

        internal RunnablePip(
            LoggingContext phaseLoggingContext,
            PipId pipId,
            PipType type,
            int priority,
            Func<RunnablePip, Task> executionFunc,
            IPipExecutionEnvironment environment,
            Pip pip = null)
        {
            Contract.Requires(phaseLoggingContext != null);
            Contract.Requires(environment != null);

            PipId = pipId;
            PipType = type;
            Priority = priority;
            OperationContext = OperationContext.CreateUntracked(phaseLoggingContext);
            m_executionFunc = executionFunc;
            Environment = environment;
            Transition(PipExecutionStep.Start);
            ScheduleTime = DateTime.UtcNow;
            Performance = new RunnablePipPerformanceInfo(ScheduleTime);
            m_pip = pip;
        }

        /// <summary>
        /// Transition to another step
        /// </summary>
        public void Transition(PipExecutionStep toStep, bool force = false)
        {
            if (!force && !Step.CanTransitionTo(toStep))
            {
                Contract.Assert(false, I($"Cannot transition from {Step} to {toStep}"));
            }

            Step = toStep;

            if (toStep == PipExecutionStep.Done)
            {
                End();
            }
        }

        /// <summary>
        /// Changes the priority of the pip
        /// </summary>
        public void ChangePriority(int priority)
        {
            Priority = priority;
        }

        /// <summary>
        /// Sets logging context and start time of the pip
        /// </summary>
        public void Start(OperationTracker tracker, LoggingContext loggingContext)
        {
            Contract.Assert(Step == PipExecutionStep.Start || IsDistributedWorker);

            OperationContext = tracker.StartOperation(PipExecutorCounter.PipRunningStateDuration, PipId, PipType, loggingContext, OperationCompleted);
            StartTime = DateTime.UtcNow;
        }

        /// <nodoc/>
        protected virtual void OperationCompleted(OperationKind kind, TimeSpan duration)
        {
        }

        /// <summary>
        /// Ends the context for the runnable pip
        /// </summary>
        public void End()
        {
            OperationContext.Dispose();
            OperationContext = OperationContext.CreateUntracked(OperationContext.LoggingContext);
            Performance.Completed();
        }

        /// <summary>
        /// Sets the pip as cancelled and return <see cref="PipExecutionStep.Cancel"/> step
        /// </summary>
        public PipExecutionStep Cancel()
        {
            IsCancelled = true;
            return PipExecutionStep.Cancel;
        }

        /// <summary>
        /// Sets the pip result and return <see cref="PipExecutionStep.HandleResult"/> step to handle the result
        /// </summary>
        public PipExecutionStep SetPipResult(PipResultStatus status)
        {
            return SetPipResult(CreatePipResult(status));
        }

        /// <summary>
        /// Creates a pip result for the given status
        /// </summary>
        public PipResult CreatePipResult(PipResultStatus status)
        {
            return PipResult.Create(status, StartTime);
        }

        /// <summary>
        /// Sets the pip result and return <see cref="PipExecutionStep.HandleResult"/> step to handle the result
        /// </summary>
        public PipExecutionStep SetPipResult(in PipResult result)
        {
            if (result.Status.IndicatesFailure())
            {
                Contract.Assert(LoggingContext.ErrorWasLogged, "Error was not logged for pip marked as failure");
            }

            Result = result;
            return PipExecutionStep.HandleResult;
        }

        /// <summary>
        /// Sets the pip result with <see cref="ExecutionResult"/>
        /// </summary>
        public virtual PipExecutionStep SetPipResult(ExecutionResult executionResult)
        {
            SetExecutionResult(executionResult);

            // For process pips, create the pip result with the performance info.
            bool withPerformanceInfo = Pip.PipType == PipType.Process;
            var pipResult = CreatePipResultFromExecutionResult(StartTime, executionResult, withPerformanceInfo);
            return SetPipResult(pipResult);
        }

        /// <summary>
        /// Sets the execution result
        /// </summary>
        public void SetExecutionResult(ExecutionResult executionResult)
        {
            Contract.Requires(PipType == PipType.Process || PipType == PipType.Ipc, "Only process or IPC pips can set the execution result");

            if (!executionResult.IsSealed)
            {
                executionResult.Seal();
            }

            ExecutionResult = executionResult;
        }

        /// <summary>
        /// Sets the observer
        /// </summary>
        public void SetObserver(RunnablePipObserver observer)
        {
            Observer = observer ?? RunnablePipObserver.Default;
        }

        /// <summary>
        /// Sets the dispatcher kind
        /// </summary>
        public void SetDispatcherKind(DispatcherKind kind)
        {
            DispatcherKind = kind;
            Performance.Enqueued(kind);
        }

        /// <summary>
        /// Sets the worker
        /// </summary>
        public void SetWorker(Worker worker)
        {
            Worker = worker;
        }

        /// <summary>
        /// Runs executionFunc and release resources if any worker is given
        /// </summary>
        public Task RunAsync(DispatcherReleaser dispatcherReleaser = null)
        {
            Contract.Requires(m_executionFunc != null);

            m_dispatcherReleaser = dispatcherReleaser ?? m_dispatcherReleaser;

            Performance.Dequeued();
            return m_executionFunc(this);
        }

        /// <summary>
        /// Release dispatcher
        /// </summary>
        public void ReleaseDispatcher()
        {
            m_dispatcherReleaser?.Release();
        }

        /// <summary>
        /// Logs the performance information for the <see cref="PipExecutionStep"/> to the execution log
        /// </summary>
        /// <remarks>
        /// If the step is executed on the remote worker, the duration will include the distribution 
        /// overhead (sending, receiving, queue time on worker) as well. 
        /// </remarks>
        public void LogExecutionStepPerformance(
            PipExecutionStep step,
            DateTime startTime,
            TimeSpan duration)
        {
            if (step.IncludeInRunningTime())
            {
                RunningTime += duration;
            }

            Performance.Executed(step, duration);

            Environment.State.ExecutionLog?.PipExecutionStepPerformanceReported(new PipExecutionStepPerformanceEventData
            {
                PipId = PipId,
                StartTime = startTime,
                Duration = duration,
                Dispatcher = DispatcherKind,
                Step = step,
            });
        }

        /// <summary>
        /// Logs the performance information for the <see cref="PipExecutionStep"/> to the execution log
        /// </summary>
        public void LogRemoteExecutionStepPerformance(
            uint workerId,
            PipExecutionStep step,
            TimeSpan remoteStepDuration,
            TimeSpan remoteQueueDuration,
            TimeSpan queueRequestDuration,
            TimeSpan sendRequestDuration)
        {
            Performance.RemoteExecuted(workerId, step, remoteStepDuration, remoteQueueDuration, queueRequestDuration, sendRequestDuration);
        }

        /// <summary>
        /// Creates a runnable pip
        /// </summary>
        public static RunnablePip Create(
            LoggingContext loggingContext,
            IPipExecutionEnvironment environment,
            PipId pipId,
            PipType type,
            int priority,
            Func<RunnablePip, Task> executionFunc,
            ushort cpuUsageInPercent)
        {
            switch (type)
            {
                case PipType.Process:
                    return new ProcessRunnablePip(loggingContext, pipId, priority, executionFunc, environment, cpuUsageInPercent);
                default:
                    return new RunnablePip(loggingContext, pipId, type, priority, executionFunc, environment);
            }
        }

        /// <summary>
        /// Creates a runnable pip with a hydrated pip
        /// </summary>
        public static RunnablePip Create(
            LoggingContext loggingContext,
            IPipExecutionEnvironment environment,
            Pip pip,
            int priority,
            Func<RunnablePip, Task> executionFunc)
        {
            switch (pip.PipType)
            {
                case PipType.Process:
                    return new ProcessRunnablePip(loggingContext, pip.PipId, priority, executionFunc, environment, pip: pip);
                default:
                    return new RunnablePip(loggingContext, pip.PipId, pip.PipType, priority, executionFunc, environment, pip);
            }
        }

        /// <summary>
        /// Creates the pip result from the execution result
        /// </summary>
        public static PipResult CreatePipResultFromExecutionResult(DateTime start, ExecutionResult result, bool withPerformanceInfo = false)
        {
            result.Seal();

            Contract.Assert(result.Result.IndicatesExecution());
            DateTime stop = DateTime.UtcNow;

            PipExecutionPerformance perf;

            if (withPerformanceInfo)
            {
                if (result.PerformanceInformation != null)
                {
                    var performanceInformation = result.PerformanceInformation;

                    perf = new ProcessPipExecutionPerformance(
                        performanceInformation.ExecutionLevel,
                        start,
                        stop,
                        fingerprint: performanceInformation.Fingerprint,
                        processExecutionTime: performanceInformation.ProcessExecutionTime,
                        fileMonitoringViolations: performanceInformation.FileMonitoringViolations,
                        ioCounters: performanceInformation.IO,
                        userTime: performanceInformation.UserTime,
                        kernelTime: performanceInformation.KernelTime,
                        peakMemoryUsage: performanceInformation.PeakMemoryUsage,
                        numberOfProcesses: performanceInformation.NumberOfProcesses,
                        workerId: performanceInformation.WorkerId);
                }
                else
                {
                    PipExecutionLevel level = result.Result.ToExecutionLevel();

                    // We didn't try to run a sandboxed process at all, or it didn't make it to the execution phase (no useful counters).
                    perf = new ProcessPipExecutionPerformance(
                            level,
                            start,
                            stop,
                            fingerprint: result.WeakFingerprint?.Hash ?? FingerprintUtilities.ZeroFingerprint,
                            processExecutionTime: TimeSpan.Zero,
                            fileMonitoringViolations: default(FileMonitoringViolationCounters),
                            ioCounters: default(IOCounters),
                            userTime: TimeSpan.Zero,
                            kernelTime: TimeSpan.Zero,
                            peakMemoryUsage: 0,
                            numberOfProcesses: 0,
                            workerId: 0);
                }
            }
            else
            {
                perf = PipExecutionPerformance.CreatePoint(result.Result);
            }

            return new PipResult(
                result.Result,
                perf,
                result.MustBeConsideredPerpetuallyDirty,
                result.DynamicallyObservedFiles,
                result.DynamicallyObservedEnumerations);
        }

        /// <summary>
        /// Replaces active operation context for the runnable pip and returns a scope
        /// which restores the current active context when disposed
        /// </summary>
        public OperationScope EnterOperation(OperationContext context)
        {
            var scope = new OperationScope(this);
            OperationContext = context;
            return scope;
        }

        /// <summary>
        /// Captures operation context and restores it when the scope is exited
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public readonly struct OperationScope : IDisposable
        {
            private readonly OperationContext m_capturedContext;
            private readonly RunnablePip m_runnablePip;

            /// <nodoc />
            public OperationScope(RunnablePip runnablePip)
            {
                m_runnablePip = runnablePip;
                m_capturedContext = runnablePip.OperationContext;
            }

            /// <nodoc />
            public void Dispose()
            {
                m_runnablePip.OperationContext = m_capturedContext;
            }
        }
    }
}
