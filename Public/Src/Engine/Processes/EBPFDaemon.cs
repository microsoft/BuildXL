// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Instrumentation.Common;

#nullable enable

namespace BuildXL.Processes
{
    /// <summary>
    /// Allows for asynchronous initialization of the EBPF system.
    /// </summary>
    public sealed class EBPFDaemon : IDisposable
    {
        private CancellationTokenRegistration m_registration;
        private ISandboxedProcess? m_process;
        private readonly ConcurrentQueue<string> m_ebpfErrors = new ();

        /// <summary>
        /// All the errors reported by EBPF during the lifetime of the daemon
        /// </summary>
        /// <remarks>
        /// This may include errors that were reported after EBPF was successfully initialized.
        /// </remarks>
        public IReadOnlyCollection<string> EBPFErrors => m_ebpfErrors.ToArray();

        private EBPFDaemon()
        {
        }

        private static EBPFDaemonTask? s_daemonTask;
        private static readonly object s_lock = new object();

        /// <summary>
        /// Creates a task that initializes the EBPF system. This task is intended to be global and will be shared across all processes that use EBPF.
        /// </summary>
        /// <remarks>
        /// The task will be created if it doesn't exist yet. If the EBPF daemon is already running, this method will fail. Call <see cref="GetEBPFDaemonTask"/> afterwards to get the existing task. 
        /// The distinction between create and use is made as a way to enforce proper disposal: whoever creates the task is responsible for disposing it, and disposing shouldn't happen while other processes are using it.
        /// It is fine to keep a single instance of this task even if server mode is on, and hence the singleton pattern. 
        /// Awaiting the task will block until EBPF is initialized, but the initialization process will start as soon as this task is created. 
        /// The result of the task will be the a <see cref="EBPFDaemon"/> instance on success, or a <see cref="Failure"/> otherwise. If any errors occur after a successful initialization, they will be reported 
        /// in the <see cref="EBPFErrors"/> property.
        /// </remarks>
        public static EBPFDaemonTask CreateEBPFDaemonTask(bool enableEBPFLinuxSandbox, AbsolutePath tempDirectory, PathTable pathTable, LoggingContext loggingContext, CancellationToken cancellationToken)
        {
            Contract.Assert(tempDirectory.IsValid, "The temp directory must be valid.");

            lock (s_lock)
            {
                Contract.Assert(!enableEBPFLinuxSandbox || s_daemonTask == null, "The EBPF daemon task is already running. This should never happen.");

                DateTime startTime = DateTime.UtcNow;
                // This will be disposed when the returns task is disposed.
                var daemon = new EBPFDaemon();

                Task<Possible<EBPFDaemon>> initializationTask;
                // If the EBPF sandbox is not enabled, just return a task that is already completed.
                if (!enableEBPFLinuxSandbox)
                {
                    initializationTask = Task.FromResult(new Possible<EBPFDaemon>(daemon));
                }
                else
                {
                    // This task can be CPU-bound, so let's run in a thread pool thread.
                    initializationTask = Task.Run(async () =>
                    {
                        return await daemon
                            .RunInfiniteEBPFProcessAsync(tempDirectory, pathTable, loggingContext, cancellationToken)
                            .ContinueWith(t =>
                                {
                                    if (t.IsCanceled)
                                    {
                                        return new Failure<string>("EBPF initialization has been cancelled");
                                    }

                                    return t.Result.Then(_ => daemon);
                                });
                    });
                }

                s_daemonTask = new EBPFDaemonTask(daemon, startTime, initializationTask, loggingContext, cancellationToken);
            }

            return s_daemonTask;
        }

        /// <summary>
        /// Retrieves the current instance of the EBPF daemon task.
        /// </summary>
        /// <remarks>
        /// A prior call to <see cref="CreateEBPFDaemonTask(bool, AbsolutePath, PathTable, LoggingContext, CancellationToken)"/> should be made before this method is called.
        /// </remarks>
        public static EBPFDaemonTask GetEBPFDaemonTask()
        {
            lock (s_lock)
            {
                Contract.Assert(s_daemonTask != null, "The EBPF daemon task is not running. This should never happen.");
                return s_daemonTask;
            }
        }

        internal void AddError(string error)
        {
            m_ebpfErrors.Enqueue(error);
        }

        private async Task<Possible<Unit>> RunInfiniteEBPFProcessAsync(AbsolutePath workingDirectory, PathTable pathTable, LoggingContext loggingContext, CancellationToken cancellationToken)
        {
            Contract.Assert(OperatingSystemHelper.IsLinuxOS);

            var toolBuildStorage = new EBPFBuildStorage(pathTable, workingDirectory);
            var fileAccessManifest = new FileAccessManifest(pathTable)
            {
                FailUnexpectedFileAccesses = false,
                ReportFileAccesses = true,
                MonitorChildProcesses = true,
            };

            // Make sure the root node is configured so all accesses are reported
            fileAccessManifest.AddScope(AbsolutePath.Invalid, FileAccessPolicy.MaskNothing, FileAccessPolicy.ReportAccess);
            // Just set a random pip id
            fileAccessManifest.PipId = Guid.NewGuid().GetHashCode();

            var ebpfListener = new EBPFListener(this);

            // Set up a process that will just sleep forever
            // If there is any issue while the sandboxed process is running, the sandbox connection will log the error.
            var info =
                new SandboxedProcessInfo(
                    pathTable,
                    toolBuildStorage,
                    "/usr/bin/sleep",
                    fileAccessManifest,
                    disableConHostSharing: false,
                    loggingContext: loggingContext,
                    detoursEventListener: ebpfListener)
                {
                    // Let's run a process that goes to sleep forever (10 days)
                    // We should never get a build that runs longer than 10 days...
                    Arguments = "10d",
                    WorkingDirectory = workingDirectory.ToString(pathTable),
                    PipSemiStableHash = fileAccessManifest.PipId,
                    PipDescription = "EBPF daemon",
                    EnvironmentVariables = BuildParameters.GetFactory().PopulateFromDictionary(CollectionUtilities.EmptyDictionary<string, string>()),
                    SandboxConnection = new SandboxConnectionLinuxEBPF(ebpfDaemonTask: null),
                };
            info.FileAccessManifest.MonitorChildProcesses = false;

            m_process = await SandboxedProcessFactory.StartAsync(info, forceSandboxing: true);
            
            m_registration = cancellationToken.Register(
                () =>
                {
                    try
                    {
                        m_process?.KillAsync().GetAwaiter().GetResult();
                    }
                    catch (Exception)
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler                    
                    {
                        // If the process has already terminated or doesn't exist, an TaskCanceledException is raised.
                        // In either case, we swallow the exception, cancellation is already requested by the user
                    }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler                    
                });

            // Let's create a task that will complete when the process finishes, emitting a failure if it does.
            Task<Possible<Unit>> processFinishedTask = m_process.GetResultAsync().ContinueWith(t =>
            {
                // On cancellation the PathTable is already invalidated. Just ignore the exception.
                if (t.IsFaulted && cancellationToken.IsCancellationRequested)
                {
                    var _ = t.Exception;
                    return new Possible<Unit>(new Failure<string>("EBPF initialization has been cancelled"));
                }

                if (t.IsCompleted)
                {
                    // Let's try to capture stderr as an EBPF error, just to enhance debugging
                    var stderr = t.GetAwaiter().GetResult().StandardError?.CreateReader().ReadToEnd();
                    if (!string.IsNullOrEmpty(stderr))
                    {
                        m_ebpfErrors.Enqueue(stderr!);
                    }
                }

                return cancellationToken.IsCancellationRequested
                    // If the process finished due to cancellation, report it as a failure
                    ? new Possible<Unit>(new Failure<string>("EBPF initialization has been cancelled"))
                    // Otherwise, the process shouldn't have finished
                    : new Possible<Unit>(new Failure<string>($"EBPF daemon terminated unexpectedly: {string.Join(Environment.NewLine, EBPFErrors)}"));
            });

            // Wait for the process running under EBPF to reach the point where we receive the first event (success) or 
            // when the process finishes (cancellation or unexpected termination).
            var any = await Task.WhenAny(ebpfListener.ProcessEventReceived, processFinishedTask);
            return await any;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_process != null)
            {
                m_process.KillAsync().GetAwaiter().GetResult();
                m_process.Dispose();
                m_process = null;
            }
            m_registration.Dispose();
        }
    }

    /// <summary>
    /// Handles the initialization of the EBPF system, tracking the time taken for initialization and managing a
    /// watchdog timer.
    /// </summary>
    public sealed class EBPFDaemonTask : IDisposable
    {
        private readonly Timer m_ebpfInitWatchdog;
        private readonly LoggingContext m_loggingContext;
        private readonly Task<Possible<EBPFDaemon>> m_initializationTask;
        private long m_firstAwaitTimeTicks = -1;
        private readonly DateTime m_initializationStart;
        private readonly EBPFDaemon m_daemon;

        /// <summary>
        /// The time EBPF spent initializing. This will be zero until EBPF finishes initialization
        /// </summary>
        public TimeSpan InitializationTime { get; private set; }

        /// <summary>
        /// The time EBPF spent initializing. This will be zero until EBPF finishes initialization
        /// </summary>
        public TimeSpan OverlappedInitialization { get; private set; }

        /// <nodoc/>
        internal EBPFDaemonTask(EBPFDaemon daemon, DateTime initializationStart, Task<Possible<EBPFDaemon>> initializationTask, LoggingContext loggingContext, CancellationToken cancellationToken)
        {
            // Timer will start if someone actually waiting on the task (called GetAwaiter()).
            m_ebpfInitWatchdog = new Timer(o => CheckIfEBPFfIsStillInitializing());
            m_loggingContext = loggingContext;
            m_initializationStart = initializationStart;
            m_daemon = daemon;
            m_initializationTask = initializationTask.ContinueWith(t => 
            {
                if (t.IsCanceled)
                {
                    return new Failure<string>("EBPF initialization has been cancelled");
                }
                
                // If an await hasn't happened by completionTime, we pretend that firstAwaited == completionTime.
                DateTime completionTime = DateTime.UtcNow;

                long firstWaitTimeTicksOrNegativeOne = Volatile.Read(ref m_firstAwaitTimeTicks);
                DateTime firstAwaitTime = firstWaitTimeTicksOrNegativeOne == -1
                    ? completionTime
                    : new DateTime(firstWaitTimeTicksOrNegativeOne, DateTimeKind.Utc);

                if (firstAwaitTime > completionTime)
                {
                    firstAwaitTime = completionTime;
                }

                // If an await hasn't happened yet, timeWaitedMs is zero (completionTime == firstAwaitTime; see above)
                int timeWaitedMs = (int)Math.Round(Math.Max(0, (completionTime - firstAwaitTime).TotalMilliseconds));
                Contract.Assert(timeWaitedMs >= 0);

                InitializationTime = completionTime - m_initializationStart;
                if (InitializationTime < TimeSpan.Zero)
                {
                    InitializationTime = TimeSpan.Zero;
                }

                OverlappedInitialization = TimeSpan.FromMilliseconds((int)Math.Round(Math.Max(0, InitializationTime.TotalMilliseconds - timeWaitedMs)));

                Tracing.Logger.Log.SynchronouslyWaitedForEBPF(loggingContext, timeWaitedMs, OverlappedInitialization.Milliseconds);

                return t.Result;
            });
        }

        /// <nodoc />
        public TaskAwaiter<Possible<EBPFDaemon>> GetAwaiter()
        {
            long nowTicks = DateTime.UtcNow.Ticks;

            // Starting the watchdog, it will signal if EBPF takes a lot of time to initialize.
            // Tests show that EBPF normally takes between 2 and 5 seconds to initialize, so 10 seconds should be a good upper bound
            // to start showing a message to the user
            m_ebpfInitWatchdog.Change(TimeSpan.FromMilliseconds(-1), TimeSpan.FromSeconds(10));

            // It's possible multiple threads are waiting on initialization to finish at the same time. We want to remember
            // only the earliest await time.
            Analysis.IgnoreResult(Interlocked.CompareExchange(ref m_firstAwaitTimeTicks, nowTicks, comparand: -1));

            return m_initializationTask.GetAwaiter();
        }

        private void CheckIfEBPFfIsStillInitializing()
        {
            if (!m_initializationTask.IsCompleted)
            {
                Tracing.Logger.Log.EBPFIsStillBeingInitialized(m_loggingContext);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracing.Logger.Log.EBPFDisposed(m_loggingContext, "About to dispose EBPF");

            m_initializationTask?.Dispose();
            m_ebpfInitWatchdog.Dispose();
            m_daemon.Dispose();

            Tracing.Logger.Log.EBPFDisposed(m_loggingContext, "EBPF disposed");

        }
    }

    /// <summary>
    /// Stores file paths for sandboxed processes using a specified directory
    /// </summary>
    internal sealed class EBPFBuildStorage : ISandboxedProcessFileStorage
    {
        private readonly AbsolutePath m_directory;
        private readonly PathTable m_pathTable;

        /// <nodoc />
        public EBPFBuildStorage(PathTable pathTable, AbsolutePath directory)
        {
            m_directory = directory;
            m_pathTable = pathTable;
        }

        /// <inheritdoc />
        public string GetFileName(SandboxedProcessFile file) => m_directory.Combine(m_pathTable, file.DefaultFileName()).ToString(m_pathTable);
    }

    /// <summary>
    /// A detours listener that offers a task to wait for the first event.
    /// </summary>
    public sealed class EBPFListener : ExtendedDetoursEventListener
    {
        private readonly EBPFDaemon m_ebpfDaemon;
        private readonly TaskCompletionSource<Possible<Unit>> m_taskCompletion;

        /// <summary>
        /// Returns a Task that represents the reception of the first event.
        /// </summary>
        public Task<Possible<Unit>> ProcessEventReceived => m_taskCompletion.Task;

        /// <nodoc/>
        public EBPFListener(EBPFDaemon ebpfDaemon)
        {
            m_ebpfDaemon = ebpfDaemon;
            m_taskCompletion = new TaskCompletionSource<Possible<Unit>>();
            // We are interested in file accesses (as a way to know the process started running) and also debug messages
            // (in order to catch any error and reflect it on the daemon)
            SetMessageHandlingFlags(MessageHandlingFlags.DebugMessageNotify | MessageHandlingFlags.FileAccessNotify | MessageHandlingFlags.FileAccessCollect);
        }

        /// <inheritdoc/>
        public override void HandleSandboxInfraMessage(SandboxInfraMessage sandboxMessage)
        {
            // Any EBPF initialization error will be reported as a debug message.
            if (sandboxMessage.Severity == SandboxInfraSeverity.Error)
            {
                m_taskCompletion.TrySetResult(new Failure<string>(sandboxMessage.Message));
            }

            if (sandboxMessage.Severity == SandboxInfraSeverity.Warning || sandboxMessage.Severity == SandboxInfraSeverity.Error)
            {
                m_ebpfDaemon.AddError(sandboxMessage.Message);
            }
        }

        /// <inheritdoc/>
        public override void HandleDebugMessage(DebugData debugData)
        {
        }

        /// <inheritdoc/>
        public override void HandleFileAccess(FileAccessData fileAccessData)
        {
            // The daemon process should never terminate. If that's the case, this is an error we want to report.
            if (fileAccessData.Operation == ReportedFileOperation.ProcessTreeCompletedAck)
            {
                m_ebpfDaemon.AddError("EBPF daemon process terminated unexpectedly.");
            }

            // We only care about the first event since we just want to verify EBPF is up, so we set the task completion source.
            m_taskCompletion.TrySetResult(Unit.Void);
        }

        /// <inheritdoc/>
        public override void HandleProcessData(ProcessData processData)
        {
        }

        /// <inheritdoc/>
        public override void HandleProcessDetouringStatus(ProcessDetouringStatusData data)
        {
        }
    }
}