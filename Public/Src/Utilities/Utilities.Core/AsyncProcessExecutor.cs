// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Interop;
using BuildXL.Utilities.Core.Tasks;
using static BuildXL.Interop.Unix.IO;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Executes process asynchronously.
    /// </summary>
    public class AsyncProcessExecutor : IDisposable
    {
        private readonly TaskSourceSlim<Unit> m_processExitedTcs = TaskSourceSlim.Create<Unit>();
        private readonly TaskSourceSlim<Unit> m_stdoutFlushedTcs = TaskSourceSlim.Create<Unit>();
        private readonly TaskSourceSlim<Unit> m_stderrFlushedTcs = TaskSourceSlim.Create<Unit>();

        /// <summary>
        /// Underlying process
        /// </summary>
        public Process Process { get; private set; }

        /// <summary>
        /// Start time.
        /// </summary>
        public DateTime StartTime { get; private set; }

        /// <summary>
        /// Exit time.
        /// </summary>
        public DateTime ExitTime { get; private set; }

        /// <summary>
        /// Flag indicating if the process was killed.
        /// </summary>
        public bool Killed { get; private set; }

        /// <summary>
        /// Flag indicating if the process timed out.
        /// </summary>
        public bool TimedOut { get; private set; }

        /// <summary>
        /// Task that completes once this process dies.
        /// </summary>
        private Task WhenExited => m_processExitedTcs.Task;

        /// <summary>
        /// Checks if process exit is completed.
        /// </summary>
        public bool ExitCompleted => WhenExited.IsCompleted;

        /// <summary>
        /// Checks if standard out flush is completed.
        /// </summary>
        public bool StdOutCompleted => m_stdoutFlushedTcs.Task.IsCompleted;

        /// <summary>
        /// Checks if standard error flush is completed.
        /// </summary>
        public bool StdErrCompleted => m_stderrFlushedTcs.Task.IsCompleted;

        /// <summary>
        /// Timeout.
        /// </summary>
        private readonly TimeSpan m_timeout;

        /// <summary>
        /// Provenance for logging and exception purpose.
        /// </summary>
        private readonly string m_provenance;
        private readonly Action m_dumpProcessTree;
        private readonly Action<string> m_logger;
        private readonly Action<string> m_outputBuilder;
        private readonly Action<string> m_errorBuilder;

        /// <summary>
        /// Force set the execute permission bit for the root process of process pips in Linux builds.
        /// </summary>
        private readonly bool m_forceAddExecutionPermission;

        private int m_processId = -1;

        private readonly bool m_useGenteKillOnTimeout;

        private readonly int? m_gentleKillTimeoutMs;

        private int GetProcessIdSafe()
        {
            const int ErrorValue = -1;
            try
            {
                return Process != null
                    ? Process.Id // if the process already exited Process.Id throws
                    : ErrorValue;
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                return ErrorValue;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        /// <summary>
        /// Process id.
        /// </summary>
        public int ProcessId => m_processId != -1 ? m_processId : (m_processId = GetProcessIdSafe());

        /// <summary>
        /// Gets memory counters of the process
        /// </summary>
        public ProcessMemoryCountersSnapshot? GetMemoryCountersSnapshot()
        {
            try
            {
                if (Process == null || Process.HasExited)
                {
                    return null;
                }
                
                return Dispatch.GetMemoryCountersSnapshot(Process.Handle, ProcessId);
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                return null;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        /// <summary>
        /// Creates an instance of <see cref="AsyncProcessExecutor"/>.
        /// </summary>
        public AsyncProcessExecutor(
            Process process,
            TimeSpan timeout,
            Action<string> outputBuilder = null,
            Action<string> errorBuilder = null,
            string provenance = null,
            Action<string> logger = null,
            Action dumpProcessTree = null,
            bool forceAddExecutionPermission = true,
            bool useGenteKillOnTimeout = false,
            int? gentleKillTImeoutMs = null)
        {
            Contract.RequiresNotNull(process);
            Contract.Requires(process.EnableRaisingEvents, $"{nameof(AsyncProcessExecutor)} requires EnableRaisingEvents in the underlying process to be true, as it registers to the Exited event for completion");

            m_logger = logger;
            m_outputBuilder = outputBuilder;
            m_errorBuilder = errorBuilder;
            m_useGenteKillOnTimeout = useGenteKillOnTimeout;
            m_gentleKillTimeoutMs = gentleKillTImeoutMs;

            Process = process;
            Process.Exited += (sender, e) => m_processExitedTcs.TrySetResult(Unit.Void);

            if (m_outputBuilder != null)
            {
                process.OutputDataReceived += (sender, e) => FeedOutputBuilder(m_stdoutFlushedTcs, e.Data, m_outputBuilder);
            }

            if (m_errorBuilder != null)
            {
                process.ErrorDataReceived += (sender, e) => FeedOutputBuilder(m_stderrFlushedTcs, e.Data, m_errorBuilder);
            }

            m_timeout = timeout;
            m_provenance = provenance;
            m_dumpProcessTree = dumpProcessTree;
            m_forceAddExecutionPermission = forceAddExecutionPermission;
        }

        /// <summary>
        /// Unix only: sets +x on <paramref name="fileName"/>.  Throws if file doesn't exists and <paramref name="throwIfNotFound"/> is true.
        /// </summary>
        public void SetExecutePermissionIfNeeded(string fileName, bool throwIfNotFound = true)
        {
            if (OperatingSystemHelper.IsWindowsOS)
            {
                return;
            }

            var mode = GetFilePermissionsForFilePath(fileName, followSymlink: false);
            if (mode < 0)
            {
                if (throwIfNotFound)
                {
                    ThrowBuildXLException($"Process creation failed: File '{fileName}' not found", new Win32Exception(0x2));
                }

                return;
            }


            var filePermissions = checked((FilePermissions)mode);
            FilePermissions exePermission = FilePermissions.S_IXUSR;
            if (!filePermissions.HasFlag(exePermission))
            {
                SetFilePermissionsForFilePath(fileName, (filePermissions | exePermission));
            }
        }

        /// <summary>
        /// Starts process.
        /// </summary>
        public void Start()
        {
            if (!string.IsNullOrWhiteSpace(Process.StartInfo.WorkingDirectory) &&
                !System.IO.Directory.Exists(Process.StartInfo.WorkingDirectory))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(Process.StartInfo.WorkingDirectory);
                }
                catch (Exception e)
                {
                    ThrowBuildXLException($"Process creation failed: Working directory '{Process.StartInfo.WorkingDirectory}' cannot be created", e);
                }
            }

            try
            {
                if (m_forceAddExecutionPermission)
                {
                    SetExecutePermissionIfNeeded(Process.StartInfo.FileName, throwIfNotFound: false);
                }

                Process.Start();
            }
            catch (Win32Exception e)
            {
                ThrowBuildXLException($"Failed to start process '{Process.StartInfo.FileName}'", e);
            }

            if (m_outputBuilder != null)
            {
                Process.BeginOutputReadLine();
            }

            if (m_errorBuilder != null)
            {
                Process.BeginErrorReadLine();
            }

            StartTime = DateTime.UtcNow;
            m_processId = Process.Id;
            Log($"started at {StartTime}");
        }

        /// <summary>
        /// Waits for process to exit or to get killed due to timed out.
        /// </summary>
        /// <remarks>
        /// After this task completes, stdout and stderr of <see cref="Process"/> are not necessarily flushed
        /// yet. If you care about those tasks completing, call <see cref="WaitForStdOutAndStdErrAsync"/>.
        /// </remarks>
        public async Task WaitForExitAsync()
        {
            Log($"waiting to exit");
            var finishedTask = await Task.WhenAny(Task.Delay(m_timeout), WhenExited);
            ExitTime = DateTime.UtcNow;

            var timedOut = finishedTask != WhenExited;
            if (timedOut)
            {
                Log($"timed out after {ExitTime.Subtract(StartTime)} (timeout: {m_timeout})");
                TimedOut = true;
                // We always want to dump the process tree on timeout
                await KillAsync(dumpProcessTree: true, gentleKill: m_useGenteKillOnTimeout, gentleKillTimeoutMilliseconds: m_gentleKillTimeoutMs);
            }
            else
            {
                Log($"exited at {ExitTime}");
            }
        }

        /// <summary>
        /// Waits for the process' standard output and error to get flushed.
        /// </summary>
        /// <remarks>
        /// Note that this task completes as soon as <see cref="Process"/> exits.
        /// After <see cref="Process"/> exits, however, any of its child processes might still be running, 
        /// and might still be using their parent's stdout and stderr, which is why this task is not
        /// going to necessarily complete right after <see cref="WaitForExitAsync"/> completes.
        /// 
        /// Note also that no timeout is applied here, i.e., if those child processes never exit,
        /// this task never completes.
        /// </remarks>
        public Task WaitForStdOutAndStdErrAsync()
        {
            Log($"waiting for stderr and stdout to flush");

            if (m_outputBuilder == null)
            {
                m_stdoutFlushedTcs.TrySetResult(Unit.Void);
            }

            if (m_errorBuilder == null)
            {
                m_stderrFlushedTcs.TrySetResult(Unit.Void);
            }

            return Task.WhenAll(m_stdoutFlushedTcs.Task, m_stderrFlushedTcs.Task);
        }

        /// <summary>
        /// Sends a SIGTERM to the process and waits up to the specified timeout for it to exit.
        /// </summary>
        /// <remarks>
        /// Only supported on Linux. On Windows, this method always returns false.
        /// </remarks>
        private async Task<bool> GentleKillAsync(int timeoutMilliseconds)
        {
            Contract.RequiresNotNull(Process);

            if (OperatingSystemHelper.IsWindowsOS)
            {
                return false;
            }

            if (!Process.HasExited)
            {
                Log($"GentleKillAsync({Process.Id})");
                Dispatch.GentleKill(Process.Id);
                try
                {
                    await WhenExited.WithTimeoutAsync(TimeSpan.FromMilliseconds(timeoutMilliseconds));
                    return true;
                }
                catch (TimeoutException)
                {
                    Log($"GentleKillAsync({Process.Id}) timed out after {timeoutMilliseconds} milliseconds");
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Kills process.
        /// </summary>
        public async Task KillAsync(bool dumpProcessTree, bool gentleKill = false, int? gentleKillTimeoutMilliseconds = null)
        {
            Contract.RequiresNotNull(Process);

            try
            {
                if (!Process.HasExited)
                {
                    // Gentle kill will not try to dump the process tree if successful because
                    // attempting to dump the process tree might make the process terminate
                    // without a clean exit
                    if (gentleKill)
                    {
                        Killed = await GentleKillAsync(gentleKillTimeoutMilliseconds ?? 2000);
                    }

                    if (!Killed)
                    {
                        if (dumpProcessTree)
                        {
                            Log($"Dumping process tree for root process {Process.Id}");
                            m_dumpProcessTree?.Invoke();
                        }
                        
                        Log($"Calling Kill({Process.Id})");
                        Process.Kill();
                    }                    
                }
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException)
            {
                // thrown if the process doesn't exist (e.g., because it has already completed on its own)
            }

            m_stdoutFlushedTcs.TrySetResult(Unit.Void);
            m_stderrFlushedTcs.TrySetResult(Unit.Void);
            Killed = true;

            await WhenExited;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Log($"disposing");
            Process?.Dispose();
        }

        private void ThrowBuildXLException(string message, Exception inner = null)
        {
            throw new BuildXLException($"{m_provenance + " " ?? string.Empty}{message}", inner);
        }

        private void Log(FormattableString message)
        {
            m_logger?.Invoke(FormattableStringEx.I($"Process({ProcessId}) - {message}"));
        }

        private static void FeedOutputBuilder(TaskSourceSlim<Unit> signalCompletion, string line, Action<string> eat)
        {
            if (signalCompletion.Task.IsCompleted)
            {
                return;
            }

            eat(line);

            if (line == null)
            {
                signalCompletion.TrySetResult(Unit.Void);
            }
        }
    }
}
